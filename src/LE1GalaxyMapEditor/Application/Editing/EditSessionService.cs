using System.IO;
using LE1GalaxyMapEditor.Models;
using LE1GalaxyMapEditor.Services;

namespace LE1GalaxyMapEditor.Workflows.Editing;

public sealed record EditMutationRequest(
    IReadOnlyCollection<GalaxyMapRowKey> AffectedRows,
    IReadOnlyCollection<GalaxyMapTable> Tables,
    Action Mutation,
    HistoryPresentationState Presentation,
    string SuccessMessage,
    bool IsStructural = true);

public sealed record HistoryRestoreResult(
    bool Succeeded,
    string Message,
    HistoryPresentationState? Presentation = null,
    ChangeImpact? Impact = null,
    string? Error = null);

public sealed record EditScopeRequest(
    Func<IReadOnlyList<FieldChange>, WorkflowResult> Preview,
    Func<WorkflowResult> Commit,
    Action Cancel);

public sealed record SessionMutationRequest(
    Action Mutation,
    Action Rollback,
    ChangeImpact Impact,
    HistoryPresentationState Presentation,
    string SuccessMessage);

public sealed class EditSessionService
{
    private readonly EditorSession _session;
    private readonly GalaxyMapCsvWriter _writer;
    private readonly GalaxyMapModuleManifestStore _manifestStore;
    private bool _editSnapshotCaptured;
    private EditHistorySnapshot? _historyBeforeUserEdit;
    private EditChangeSetSnapshot? _changesBeforeUserEdit;

    public EditSessionService(
        EditorSession session,
        GalaxyMapCsvWriter? writer = null,
        GalaxyMapModuleManifestStore? manifestStore = null)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _writer = writer ?? new GalaxyMapCsvWriter();
        _manifestStore = manifestStore ?? new GalaxyMapModuleManifestStore();
    }

    public bool IsApplying { get; private set; }
    public bool CanUndo => _session.History.CanUndo;
    public bool CanRedo => _session.History.CanRedo;

    public void MarkTableDirty(GalaxyMapModule module, GalaxyMapTable table)
        => _session.Changes.MarkTable(module.Tag, table);

    public void MarkTablesDirty(GalaxyMapModule module, IEnumerable<GalaxyMapTable> tables)
        => _session.Changes.MarkTables(module.Tag, tables);

    public void MarkMetadataDirty(GalaxyMapModule module)
        => _session.Changes.MarkMetadata(module.Tag);

    public void StageFile(PendingFileWrite pending)
        => _session.Changes.StageFile(pending);

    public void RemoveModuleChanges(string moduleTag)
        => _session.Changes.RemoveModule(moduleTag);

    public void MigrateModuleTag(string oldTag, string newTag)
        => _session.Changes.MigrateModuleTag(oldTag, newTag);

    public void Publish(ChangeImpact impact) => _session.Publish(impact);

    public void BeginUserEdit(HistoryPresentationState presentation)
    {
        if (_session.Workspace is null || _editSnapshotCaptured)
        {
            return;
        }

        _historyBeforeUserEdit = _session.History.Capture();
        _changesBeforeUserEdit = _session.Changes.Capture();
        _session.History.PushUndo(CaptureState(presentation));
        _session.History.ClearRedo();
        _editSnapshotCaptured = true;
    }

    public void EnsureUndoSnapshot(HistoryPresentationState presentation)
    {
        if (_editSnapshotCaptured)
        {
            _editSnapshotCaptured = false;
            return;
        }

        if (_session.Workspace is null)
        {
            return;
        }

        _session.History.PushUndo(CaptureState(presentation));
        _session.History.ClearRedo();
    }

    public void CompleteUserEdit()
    {
        _editSnapshotCaptured = false;
        _historyBeforeUserEdit = null;
        _changesBeforeUserEdit = null;
    }

    public void CancelUserEdit()
        => RestoreUserEditCheckpoint();

    public WorkflowResult CompleteObservedEdit(
        GalaxyMapRow row,
        GalaxyMapModule module,
        string column,
        HistoryPresentationState presentation)
    {
        try
        {
            GalaxyMapRowAuthoring.EnsureSnapshot(row).MarkDirty(column);
            _session.Changes.MarkTable(module.Tag, row.Table);
            var impact = ChangeImpact.For([row.Table], [row.Key], isStructural: false);
            _session.Publish(impact);
            CompleteUserEdit();
            return WorkflowResult.Success(
                $"Staged {row.Table} row {row.RowId} in {module.Tag}.",
                presentation.SelectionKey ?? row.Key,
                presentation.Navigation,
                impact);
        }
        catch (Exception exception) when (IsExpectedOperationFailure(exception))
        {
            RestoreUserEditCheckpoint();
            _session.Workspace?.Recompose();
            return WorkflowResult.Failure(exception.Message, presentation.SelectionKey);
        }
    }

    public WorkflowResult ExecuteMutation(EditMutationRequest request)
    {
        if (_session.Workspace?.ActiveLayer is not { } layer)
        {
            return WorkflowResult.Failure("A writable active module is required for this edit.", request.Presentation.SelectionKey);
        }

        var coalescedUserEdit = _editSnapshotCaptured;
        var keys = request.AffectedRows.Distinct().ToArray();
        var tables = request.Tables.Distinct().ToArray();
        var backups = keys.ToDictionary(
            key => key,
            key => layer.Find(key) is { } row ? GalaxyMapRowCloner.Clone(row) : null);
        var changeSnapshot = _session.Changes.Capture();
        var historySnapshot = _session.History.Capture();
        var editSnapshotCaptured = _editSnapshotCaptured;

        try
        {
            EnsureUndoSnapshot(request.Presentation);
            IsApplying = true;
            request.Mutation();
            _session.Changes.MarkTables(layer.Module.Tag, tables);
            _session.Workspace.Recompose();
            var impact = ChangeImpact.For(tables, keys, request.IsStructural);
            _session.Publish(impact);
            if (coalescedUserEdit)
            {
                CompleteUserEdit();
            }
            return WorkflowResult.Success($"Staged: {request.SuccessMessage}",
                request.Presentation.SelectionKey,
                request.Presentation.Navigation,
                impact);
        }
        catch (Exception exception) when (IsExpectedOperationFailure(exception))
        {
            RestoreRows(layer, backups);
            if (coalescedUserEdit)
            {
                RestoreUserEditCheckpoint();
            }
            else
            {
                _session.Changes.Restore(changeSnapshot);
                _session.History.Restore(historySnapshot);
                _editSnapshotCaptured = editSnapshotCaptured;
            }
            _session.Workspace.Recompose();
            return WorkflowResult.Failure(exception.Message, request.Presentation.SelectionKey);
        }
        finally
        {
            IsApplying = false;
        }
    }

    public WorkflowResult ExecuteSessionMutation(SessionMutationRequest request)
    {
        if (_session.Workspace is null)
        {
            return WorkflowResult.Failure("A workspace is required for this edit.", request.Presentation.SelectionKey);
        }

        var coalescedUserEdit = _editSnapshotCaptured;
        var changeSnapshot = _session.Changes.Capture();
        var historySnapshot = _session.History.Capture();
        var editSnapshotCaptured = _editSnapshotCaptured;
        try
        {
            EnsureUndoSnapshot(request.Presentation);
            IsApplying = true;
            request.Mutation();
            _session.Publish(request.Impact);
            if (coalescedUserEdit)
            {
                CompleteUserEdit();
            }
            return WorkflowResult.Success(
                $"Staged: {request.SuccessMessage}",
                request.Presentation.SelectionKey,
                request.Presentation.Navigation,
                request.Impact);
        }
        catch (Exception exception) when (IsExpectedOperationFailure(exception))
        {
            request.Rollback();
            if (coalescedUserEdit)
            {
                RestoreUserEditCheckpoint();
            }
            else
            {
                _session.Changes.Restore(changeSnapshot);
                _session.History.Restore(historySnapshot);
                _editSnapshotCaptured = editSnapshotCaptured;
            }
            return WorkflowResult.Failure(exception.Message, request.Presentation.SelectionKey);
        }
        finally
        {
            IsApplying = false;
        }
    }

    public WorkflowResult Commit()
    {
        var workspace = _session.Workspace;
        if (workspace is null || !_session.Changes.HasChanges)
        {
            return WorkflowResult.Success(string.Empty);
        }

        var failedModules = new List<string>();
        foreach (var tag in _session.Changes.ModuleTags().ToArray())
        {
            var layer = workspace.ModuleLayers.FirstOrDefault(candidate =>
                string.Equals(candidate.Module.Tag, tag, StringComparison.OrdinalIgnoreCase));
            if (layer is null)
            {
                failedModules.Add($"{tag}: module is no longer mounted");
                continue;
            }

            try
            {
                var files = _session.Changes.FilesFor(tag);
                foreach (var pending in files)
                {
                    var targetPath = GalaxyMapTextureService.ResolveModuleTexturePath(layer.Module, pending.RelativePath)
                        ?? throw new InvalidOperationException($"Invalid module file path '{pending.RelativePath}'.");
                    AtomicFileWriter.Write(targetPath, pending.Contents);
                }

                var tables = _session.Changes.TablesFor(tag);
                if (tables.Count > 0)
                {
                    _writer.WriteTables(layer, tables);
                }

                if (!layer.Module.IsReadOnly &&
                    (_session.Changes.IsMetadataDirty(tag) || files.Count > 0))
                {
                    _manifestStore.Save(layer.Module);
                }

                _session.Changes.ClearModule(tag);
            }
            catch (Exception exception) when (IsExpectedOperationFailure(exception))
            {
                failedModules.Add($"{tag}: {exception.Message}");
            }
        }

        if (failedModules.Count > 0)
        {
            return WorkflowResult.Failure(
                "Some changes could not be committed: " + string.Join(" | ", failedModules));
        }

        workspace.Recompose();
        ClearHistory();
        _session.Publish(ChangeImpact.StructuralAll);
        return WorkflowResult.Success("Committed all staged module changes to CSV.", impact: ChangeImpact.StructuralAll);
    }

    public void DiscardChangeState()
    {
        _session.Changes.Clear();
        ClearHistory();
        _session.Publish(ChangeImpact.StructuralAll);
    }

    public HistoryRestoreResult Undo(HistoryPresentationState currentPresentation)
    {
        if (_session.Workspace is null || !_session.History.CanUndo)
        {
            return new HistoryRestoreResult(false, string.Empty);
        }

        _session.History.PushRedo(CaptureState(currentPresentation));
        return RestoreState(_session.History.PopUndo(), "Undid the last staged change.");
    }

    public HistoryRestoreResult Redo(HistoryPresentationState currentPresentation)
    {
        if (_session.Workspace is null || !_session.History.CanRedo)
        {
            return new HistoryRestoreResult(false, string.Empty);
        }

        _session.History.PushUndo(CaptureState(currentPresentation));
        return RestoreState(_session.History.PopRedo(), "Redid the staged change.");
    }

    public void ClearHistory()
    {
        _session.History.Clear();
        _editSnapshotCaptured = false;
        _historyBeforeUserEdit = null;
        _changesBeforeUserEdit = null;
    }

    public IEditScope BeginEditScope(EditScopeRequest request, HistoryPresentationState presentation)
    {
        BeginUserEdit(presentation);
        return new CoalescedEditScope(this, request);
    }

    private EditorHistoryState CaptureState(HistoryPresentationState presentation)
    {
        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        var layers = _session.Workspace!.ModuleLayers.Select(GalaxyMapLayerCloner.Clone).ToArray();
        var changes = _session.Changes.Capture();
        var allocatedBytes = Math.Max(1, GC.GetAllocatedBytesForCurrentThread() - allocatedBefore);
        return new EditorHistoryState(
            layers,
            _session.ActiveModule?.Tag,
            changes,
            presentation,
            allocatedBytes);
    }

    private HistoryRestoreResult RestoreState(EditorHistoryState state, string message)
    {
        if (_session.Workspace is null)
        {
            return new HistoryRestoreResult(false, string.Empty);
        }

        var restored = new GalaxyMapWorkspace(_session.Workspace.BaseLayer, state.Layers);
        var active = restored.Modules.FirstOrDefault(module =>
            string.Equals(module.Tag, state.ActiveModuleTag, StringComparison.OrdinalIgnoreCase));
        if (active is { IsReadOnly: false })
        {
            restored.SetActiveModule(active);
        }

        _session.Workspace = restored;
        _session.Changes.Restore(state.Changes);
        _editSnapshotCaptured = false;
        _session.Publish(ChangeImpact.StructuralAll);
        return new HistoryRestoreResult(true, message, state.Presentation, ChangeImpact.StructuralAll);
    }

    private static void RestoreRows(
        GalaxyMapLayer layer,
        IReadOnlyDictionary<GalaxyMapRowKey, GalaxyMapRow?> backups)
    {
        foreach (var (key, backup) in backups)
        {
            if (layer.Find(key) is { } current)
            {
                layer.Remove(current);
            }

            if (backup is not null)
            {
                layer.Upsert(backup);
            }
        }
    }

    private void RestoreUserEditCheckpoint()
    {
        if (_changesBeforeUserEdit is not null)
        {
            _session.Changes.Restore(_changesBeforeUserEdit);
        }
        if (_historyBeforeUserEdit is not null)
        {
            _session.History.Restore(_historyBeforeUserEdit);
        }
        CompleteUserEdit();
    }

    private static bool IsExpectedOperationFailure(Exception exception)
        => exception is GalaxyMapLoadException or IOException or UnauthorizedAccessException or
            InvalidOperationException or ArgumentException or OverflowException;

    private sealed class CoalescedEditScope(EditSessionService owner, EditScopeRequest request) : IEditScope
    {
        private bool _completed;

        public WorkflowResult Preview(IReadOnlyList<FieldChange> changes)
            => _completed
                ? WorkflowResult.Failure("The edit scope is already complete.")
                : request.Preview(changes);

        public WorkflowResult Commit()
        {
            if (_completed)
            {
                return WorkflowResult.Failure("The edit scope is already complete.");
            }

            var result = request.Commit();
            if (result.Succeeded)
            {
                _completed = true;
                owner.CompleteUserEdit();
            }

            return result;
        }

        public void Cancel()
        {
            if (_completed)
            {
                return;
            }

            request.Cancel();
            _completed = true;
            owner.CompleteUserEdit();
        }

        public void Dispose() => Cancel();
    }
}
