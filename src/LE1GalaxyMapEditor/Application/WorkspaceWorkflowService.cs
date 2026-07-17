using System.IO;
using System.Text;
using LE1GalaxyMapEditor.Models;
using LE1GalaxyMapEditor.Services;
using LE1GalaxyMapEditor.Workflows.Editing;

namespace LE1GalaxyMapEditor.Workflows;

public sealed class WorkspaceWorkflowService
{
    private static readonly GalaxyMapTable[] ReservableTables =
    [
        GalaxyMapTable.Cluster,
        GalaxyMapTable.System,
        GalaxyMapTable.Planet,
        GalaxyMapTable.Map,
        GalaxyMapTable.Relay
    ];

    private readonly EditorSession _session;
    private readonly EditSessionService _edits;
    private readonly CsvGalaxyMapLoader _loader;
    private readonly GalaxyMapModuleManifestStore _manifestStore;
    private readonly GalaxyMapWorkspaceStore _workspaceStore;
    private readonly List<ValidationDiagnostic> _startupDiagnostics = [];
    private readonly List<RememberedModule> _missingRememberedModules = [];
    private bool _isRestoring;

    public WorkspaceWorkflowService(
        EditorSession session,
        EditSessionService edits,
        CsvGalaxyMapLoader loader,
        GalaxyMapModuleManifestStore? manifestStore = null,
        GalaxyMapWorkspaceStore? workspaceStore = null)
    {
        _session = session;
        _edits = edits;
        _loader = loader;
        _manifestStore = manifestStore ?? new GalaxyMapModuleManifestStore();
        _workspaceStore = workspaceStore ?? new GalaxyMapWorkspaceStore();
    }

    public IReadOnlyList<ValidationDiagnostic> StartupDiagnostics => _startupDiagnostics;
    public IReadOnlyList<RememberedModule> MissingRememberedModules => _missingRememberedModules;

    public WorkflowResult LoadBuiltIn()
    {
        try
        {
            _session.Workspace = new GalaxyMapWorkspace(_loader.LoadBuiltInLayer());
            _session.Publish(ChangeImpact.StructuralAll);
            return WorkflowResult.Success("Loaded the read-only BASEGAME galaxy map.",
                navigation: NavigationTarget.Galaxy,
                impact: ChangeImpact.StructuralAll);
        }
        catch (Exception exception) when (IsExpectedOperationFailure(exception))
        {
            return WorkflowResult.Failure(exception.Message);
        }
    }

    /// <summary>
    /// Loads BASEGAME and the remembered module stack as one startup transition.
    /// A corrupt workspace file is degraded to a usable BASEGAME-only session with
    /// a structured diagnostic because no preceding editor state exists to preserve.
    /// </summary>
    public WorkflowResult LoadRememberedWorkspace()
    {
        if (_session.Document is not null || _session.Changes.HasChanges ||
            _session.History.CanUndo || _session.History.CanRedo)
        {
            return WorkflowResult.Failure(
                "Initial workspace loading cannot replace a live editor session; use the reload workflow instead.");
        }

        return ReplaceRememberedWorkspace(transactionalReload: false);
    }

    /// <summary>
    /// Prepares a complete replacement off-session, then accepts it atomically and
    /// clears transient edits. Failure leaves the current workspace, edit state,
    /// history, diagnostics, selection, and revision untouched.
    /// </summary>
    public WorkflowResult ReloadRememberedWorkspace(bool requireCurrentlyMountedModules = true)
        => ReplaceRememberedWorkspace(
            transactionalReload: true,
            requireCurrentlyMountedModules: requireCurrentlyMountedModules);

    private WorkflowResult ReplaceRememberedWorkspace(
        bool transactionalReload,
        bool requireCurrentlyMountedModules = false)
    {
        GalaxyMapLayer baseLayer;
        try
        {
            baseLayer = _loader.LoadBuiltInLayer();
        }
        catch (Exception exception) when (IsExpectedOperationFailure(exception))
        {
            return WorkflowResult.Failure(exception.Message);
        }

        GalaxyMapWorkspace? candidate = null;
        List<ValidationDiagnostic> diagnostics = [];
        List<RememberedModule> missingModules = [];
        string? startupFallbackError = null;
        try
        {
            BeginRestoration();
            var remembered = _workspaceStore.Load();
            var loadedLayers = LoadRememberedLayers(
                [baseLayer.Module.Tag],
                remembered,
                diagnostics,
                missingModules);
            candidate = new GalaxyMapWorkspace(baseLayer, loadedLayers);
            RestoreActiveModule(candidate, remembered.ActiveModuleTag);

            if (transactionalReload && requireCurrentlyMountedModules &&
                MissingCurrentlyMountedModules(candidate) is { Count: > 0 } missingCurrent)
            {
                return WorkflowResult.Failure(
                    "The remembered workspace could not recreate the currently mounted module(s): " +
                    string.Join(", ", missingCurrent) + ". Staged changes were left intact.");
            }
        }
        catch (Exception exception) when (IsExpectedOperationFailure(exception))
        {
            if (transactionalReload)
            {
                return WorkflowResult.Failure(
                    $"The remembered workspace could not be reloaded: {exception.Message} Staged changes were left intact.");
            }

            // Startup has no earlier authoring state to protect. Keep BASEGAME usable
            // and surface the workspace-file failure in both the banner and diagnostics.
            var diagnostic = new ValidationDiagnostic(
                "WORKSPACE-LOAD",
                ValidationSeverity.Error,
                $"Remembered workspace could not be loaded: {exception.Message}",
                string.Empty);
            diagnostics = [diagnostic];
            missingModules = [];
            candidate = new GalaxyMapWorkspace(baseLayer);
            startupFallbackError = diagnostic.Message;
        }
        finally
        {
            _isRestoring = false;
        }

        // Acceptance is intentionally outside the preparation catch. Once edit state
        // is cleared and the session is swapped, an observer exception must propagate;
        // it must never be misreported as a rejected, state-preserving reload.
        var acceptedCandidate = candidate
            ?? throw new InvalidOperationException("A remembered workspace candidate was not prepared.");
        AcceptRestorationDiagnostics(diagnostics, missingModules);
        if (transactionalReload)
        {
            ClearTransientEditState();
        }
        _session.Workspace = acceptedCandidate;
        _session.Publish(ChangeImpact.StructuralAll);
        return startupFallbackError is null
            ? CreateRestorationResult(acceptedCandidate, diagnostics)
            : new WorkflowResult(
                false,
                "Loaded BASEGAME without remembered modules.",
                Navigation: NavigationTarget.Galaxy,
                Impact: ChangeImpact.StructuralAll,
                Error: startupFallbackError);
    }

    public WorkflowResult LoadReferenceFolder(string folderPath)
    {
        GalaxyMapDocument document;
        try
        {
            document = _loader.LoadFolder(folderPath);
        }
        catch (Exception exception) when (IsExpectedOperationFailure(exception))
        {
            return WorkflowResult.Failure(exception.Message);
        }

        // Only a fully loaded reference source supersedes remembered-workspace
        // diagnostics. Session acceptance is outside the loader-failure catch so an
        // observer failure cannot be misreported as preserving the previous source.
        _startupDiagnostics.Clear();
        _missingRememberedModules.Clear();
        _session.AttachReferenceDocument(document);
        return WorkflowResult.Success(
            "Loaded a read-only Legendary Explorer export folder.",
            navigation: NavigationTarget.Galaxy,
            impact: ChangeImpact.StructuralAll);
    }

    public WorkflowResult CreateModule(
        string parentFolder,
        string name,
        string tag,
        ModuleColor color,
        ModuleIdReservations reservations,
        int? loadOrder = null)
    {
        var workspace = _session.Workspace;
        if (workspace is null)
        {
            return WorkflowResult.Failure("Load BASEGAME before creating an authoring module.");
        }

        try
        {
            ValidateNewModuleRanges(reservations);
            var folderName = SafeFolderName(name, tag);
            var moduleFolder = Path.Combine(Path.GetFullPath(parentFolder), folderName);
            if (Directory.Exists(moduleFolder) && Directory.EnumerateFileSystemEntries(moduleFolder).Any())
            {
                throw new InvalidOperationException(
                    $"The module folder already exists and is not empty: {moduleFolder}");
            }

            Directory.CreateDirectory(moduleFolder);
            var module = new GalaxyMapModule(
                name,
                tag,
                color,
                moduleFolder,
                isReadOnly: false,
                loadOrder: loadOrder ?? NextLoadOrder(),
                reservations);
            EnsureUniqueTag(module.Tag);
            _manifestStore.Save(module);

            var layer = new GalaxyMapLayer(module);
            foreach (var table in Enum.GetValues<GalaxyMapTable>())
            {
                layer.SetSchema(CsvGalaxyMapLoader.GetCanonicalSchema(table));
            }

            workspace.Mount(layer);
            workspace.SetActiveModule(module);
            ClearStartupIssueFor(module.Tag, module.FolderPath);
            RememberCurrentWorkspace();
            _session.Publish(ChangeImpact.StructuralAll);
            return WorkflowResult.Success(
                $"Created authoring module {module.Name} [{module.Tag}]. Changes will remain staged until Commit.",
                navigation: NavigationTarget.Galaxy,
                impact: ChangeImpact.StructuralAll);
        }
        catch (Exception exception) when (IsExpectedOperationFailure(exception))
        {
            return WorkflowResult.Failure(exception.Message);
        }
    }

    public WorkflowResult OpenExistingModule(string folderPath)
    {
        var workspace = _session.Workspace;
        if (workspace is null)
        {
            return WorkflowResult.Failure("Load BASEGAME before opening a module.");
        }

        try
        {
            var module = _manifestStore.Load(folderPath);
            EnsureUniqueTag(module.Tag);
            var layer = _loader.LoadPartFolder(folderPath, module);
            workspace.Mount(layer);
            if (!module.IsReadOnly)
            {
                workspace.SetActiveModule(module);
            }

            ClearStartupIssueFor(module.Tag, module.FolderPath);
            RememberCurrentWorkspace();
            _session.Publish(ChangeImpact.StructuralAll);
            return WorkflowResult.Success(
                module.IsReadOnly
                    ? $"Mounted read-only module {module.Name} [{module.Tag}]."
                    : $"Opened authoring module {module.Name} [{module.Tag}]. Live CSV editing is active.",
                navigation: NavigationTarget.Galaxy,
                impact: ChangeImpact.StructuralAll);
        }
        catch (Exception exception) when (IsExpectedOperationFailure(exception))
        {
            return WorkflowResult.Failure(exception.Message);
        }
    }

    public WorkflowResult MountReadOnlyModule(
        string folderPath,
        string name,
        string tag,
        ModuleColor color,
        ModuleIdReservations reservations,
        int? loadOrder = null)
    {
        var workspace = _session.Workspace;
        if (workspace is null)
        {
            return WorkflowResult.Failure("Load BASEGAME before mounting a module.");
        }

        try
        {
            var module = new GalaxyMapModule(
                name,
                tag,
                color,
                folderPath,
                isReadOnly: true,
                loadOrder: loadOrder ?? NextLoadOrder(),
                reservations);
            EnsureUniqueTag(module.Tag);
            var layer = _loader.LoadPartFolder(folderPath, module);
            workspace.Mount(layer);
            ClearStartupIssueFor(module.Tag, module.FolderPath);
            RememberCurrentWorkspace();
            _session.Publish(ChangeImpact.StructuralAll);
            return WorkflowResult.Success(
                $"Mounted read-only module {module.Name} [{module.Tag}] above the lower layers.",
                navigation: NavigationTarget.Galaxy,
                impact: ChangeImpact.StructuralAll);
        }
        catch (Exception exception) when (IsExpectedOperationFailure(exception))
        {
            return WorkflowResult.Failure(exception.Message);
        }
    }

    public WorkflowResult SetActiveModule(GalaxyMapModule module)
    {
        ArgumentNullException.ThrowIfNull(module);
        if (_session.Workspace is null || module.IsBaseGame || module.IsReadOnly)
        {
            return WorkflowResult.Failure("Only a mounted writable module can become active.");
        }

        try
        {
            _session.Workspace.SetActiveModule(module);
            RememberCurrentWorkspace();
            _session.Publish(ChangeImpact.Empty);
            return WorkflowResult.Success($"{module.Name} [{module.Tag}] is now the active editing module.");
        }
        catch (Exception exception) when (IsExpectedOperationFailure(exception))
        {
            return WorkflowResult.Failure(exception.Message);
        }
    }

    public WorkflowResult UnlinkModule(GalaxyMapModule module)
    {
        ArgumentNullException.ThrowIfNull(module);
        var workspace = _session.Workspace;
        if (workspace is null || module.IsBaseGame ||
            workspace.ModuleLayers.All(layer => !ReferenceEquals(layer.Module, module)))
        {
            return WorkflowResult.Failure("The module is not mounted in this workspace.");
        }

        var layer = workspace.ModuleLayers.Single(candidate => ReferenceEquals(candidate.Module, module));
        var wasActive = ReferenceEquals(module, workspace.ActiveModule);
        try
        {
            workspace.Unmount(module);
            if (wasActive)
            {
                var fallback = workspace.Modules
                    .Where(candidate => !candidate.IsReadOnly)
                    .OrderByDescending(candidate => candidate.LoadOrder)
                    .ThenBy(candidate => candidate.Tag, StringComparer.OrdinalIgnoreCase)
                    .FirstOrDefault();
                workspace.SetActiveModule(fallback);
            }

            try
            {
                RememberCurrentWorkspace();
            }
            catch
            {
                workspace.Mount(layer);
                if (wasActive)
                {
                    workspace.SetActiveModule(module);
                }

                throw;
            }

            _edits.RemoveModuleChanges(module.Tag);
            _edits.ClearHistory();
            _session.Publish(ChangeImpact.StructuralAll);
            return WorkflowResult.Success(
                $"Unlinked {module.Name} [{module.Tag}] from the workspace. Module files were left untouched.",
                navigation: NavigationTarget.Galaxy,
                impact: ChangeImpact.StructuralAll);
        }
        catch (Exception exception) when (IsExpectedOperationFailure(exception))
        {
            return WorkflowResult.Failure($"The module could not be unlinked: {exception.Message}");
        }
    }

    public WorkflowResult UpdateModuleMetadata(
        GalaxyMapModule module,
        string name,
        string tag,
        ModuleColor color,
        int loadOrder,
        ModuleIdReservations reservations,
        HistoryPresentationState presentation)
    {
        var workspace = _session.Workspace;
        if (workspace is null || module.IsBaseGame)
        {
            return WorkflowResult.Failure("BASEGAME module metadata cannot be edited.");
        }

        try
        {
            ValidateNewModuleRanges(reservations, module);
            ValidateUpdatedModuleRows(module, reservations);
            if (!string.Equals(module.Tag, tag, StringComparison.OrdinalIgnoreCase))
            {
                EnsureUniqueTag(tag);
            }
            var replacement = module.With(name, tag, color, loadOrder, reservations);
            var originalActiveTag = workspace.ActiveModule?.Tag;
            return _edits.ExecuteSessionMutation(new SessionMutationRequest(
                () =>
                {
                    workspace.ReplaceModule(module, replacement);
                    _edits.MigrateModuleTag(module.Tag, replacement.Tag);
                    _edits.MarkMetadataDirty(replacement);
                },
                () =>
                {
                    if (workspace.Modules.Any(candidate => ReferenceEquals(candidate, replacement)))
                    {
                        workspace.ReplaceModule(replacement, module);
                    }
                    if (workspace.Modules.FirstOrDefault(candidate =>
                            string.Equals(candidate.Tag, originalActiveTag, StringComparison.OrdinalIgnoreCase)) is
                        { IsReadOnly: false } originalActive)
                    {
                        workspace.SetActiveModule(originalActive);
                    }
                },
                ChangeImpact.StructuralAll,
                presentation,
                $"metadata changes for {replacement.Name} [{replacement.Tag}]."));
        }
        catch (Exception exception) when (IsExpectedOperationFailure(exception))
        {
            return WorkflowResult.Failure(exception.Message);
        }
    }

    private void BeginRestoration()
    {
        _isRestoring = true;
    }

    private void ClearTransientEditState()
    {
        _session.Changes.Clear();
        _edits.ClearHistory();
    }

    private List<GalaxyMapLayer> LoadRememberedLayers(
        IEnumerable<string> existingModuleTags,
        RememberedWorkspace remembered,
        List<ValidationDiagnostic> diagnostics,
        List<RememberedModule> missingModules)
    {
        var loadedLayers = new List<GalaxyMapLayer>();
        var loadedTags = existingModuleTags.ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in remembered.Modules)
        {
            if (!Directory.Exists(entry.FolderPath))
            {
                missingModules.Add(entry);
                diagnostics.Add(new ValidationDiagnostic(
                    "WORKSPACE-MODULE-MISSING",
                    ValidationSeverity.Error,
                    $"Remembered module folder is missing: {entry.FolderPath}",
                    entry.DiagnosticTag));
                continue;
            }

            try
            {
                var manifestPath = Path.Combine(entry.FolderPath, GalaxyMapModuleManifestStore.FileName);
                var module = File.Exists(manifestPath)
                    ? _manifestStore.Load(entry.FolderPath)
                    : entry.UnmanifestedReadOnlyModule?.ToModule(entry.FolderPath)
                      ?? throw new GalaxyMapLoadException(
                          $"The remembered module folder has no {GalaxyMapModuleManifestStore.FileName}: {entry.FolderPath}");
                if (!loadedTags.Add(module.Tag))
                {
                    throw new InvalidOperationException($"A module tagged {module.Tag} is already mounted.");
                }

                loadedLayers.Add(_loader.LoadPartFolder(entry.FolderPath, module));
            }
            catch (Exception exception) when (IsExpectedOperationFailure(exception))
            {
                diagnostics.Add(new ValidationDiagnostic(
                    "WORKSPACE-MODULE-LOAD",
                    ValidationSeverity.Error,
                    $"Remembered module could not be loaded: {exception.Message}",
                    entry.DiagnosticTag));
            }
        }

        return loadedLayers;
    }

    private List<string> MissingCurrentlyMountedModules(GalaxyMapWorkspace candidate)
    {
        var current = _session.Workspace;
        if (current is null)
        {
            return [];
        }

        return current.ModuleLayers
            .Where(layer => candidate.ModuleLayers.All(candidateLayer =>
                !string.Equals(
                    candidateLayer.Module.FolderPath,
                    layer.Module.FolderPath,
                    StringComparison.OrdinalIgnoreCase)))
            .Select(layer => $"{layer.Module.Name} [{layer.Module.Tag}]")
            .ToList();
    }

    private void AcceptRestorationDiagnostics(
        IEnumerable<ValidationDiagnostic> diagnostics,
        IEnumerable<RememberedModule> missingModules)
    {
        _startupDiagnostics.Clear();
        _startupDiagnostics.AddRange(diagnostics);
        _missingRememberedModules.Clear();
        _missingRememberedModules.AddRange(missingModules);
    }

    private static void RestoreActiveModule(GalaxyMapWorkspace workspace, string? activeModuleTag)
    {
        var active = workspace.Modules.FirstOrDefault(module => !module.IsReadOnly &&
            string.Equals(module.Tag, activeModuleTag, StringComparison.OrdinalIgnoreCase))
            ?? workspace.Modules.Where(module => !module.IsReadOnly)
                .OrderByDescending(module => module.LoadOrder)
                .FirstOrDefault();
        if (active is not null)
        {
            workspace.SetActiveModule(active);
        }
    }

    private static WorkflowResult CreateRestorationResult(
        GalaxyMapWorkspace workspace,
        IReadOnlyCollection<ValidationDiagnostic> diagnostics)
    {
        var message = workspace.ModuleLayers.Count == 0
            ? "No remembered modules were mounted."
            : $"Restored {workspace.ModuleLayers.Count} remembered module(s).";
        return diagnostics.Count == 0
            ? WorkflowResult.Success(message, navigation: NavigationTarget.Galaxy, impact: ChangeImpact.StructuralAll)
            : new WorkflowResult(
                false,
                message,
                Navigation: NavigationTarget.Galaxy,
                Impact: ChangeImpact.StructuralAll,
                Error: string.Join(Environment.NewLine, diagnostics.Select(item => item.Message)));
    }

    public void RememberCurrentWorkspace()
    {
        var workspace = _session.Workspace;
        if (workspace is null || _isRestoring)
        {
            return;
        }

        var mounted = workspace.Modules
            .OrderBy(module => module.LoadOrder)
            .ThenBy(module => module.Tag, StringComparer.OrdinalIgnoreCase)
            .Select(RememberedModule.FromModule)
            .ToList();
        foreach (var missing in _missingRememberedModules.Where(missing => mounted.All(module =>
                     !string.Equals(module.FolderPath, missing.FolderPath, StringComparison.OrdinalIgnoreCase))))
        {
            mounted.Add(missing);
        }

        _workspaceStore.Save(mounted, workspace.ActiveModule?.Tag);
    }

    public ModuleIdReservations InferReservations(string folder)
    {
        var workspace = _session.Workspace;
        if (workspace is null)
        {
            return ModuleIdReservations.Empty;
        }

        var preview = new GalaxyMapModule(
            "Module preview",
            $"PREVIEW_{Guid.NewGuid():N}"[..16],
            ModuleColor.Magenta,
            folder,
            isReadOnly: true,
            loadOrder: NextLoadOrder(),
            ModuleIdReservations.Empty);
        var layer = _loader.LoadPartFolder(folder, preview);

        RowIdRange? NewRange(GalaxyMapTable table)
        {
            var newIds = layer.Rows(table)
                .Where(row => workspace.Resolve(row.Key) is null)
                .Select(row => row.RowId)
                .OrderBy(rowId => rowId)
                .ToArray();
            return newIds.Length == 0 ? null : new RowIdRange(newIds[0], newIds[^1]);
        }

        return new ModuleIdReservations(
            NewRange(GalaxyMapTable.Cluster),
            NewRange(GalaxyMapTable.System),
            NewRange(GalaxyMapTable.Planet),
            NewRange(GalaxyMapTable.Map),
            NewRange(GalaxyMapTable.Relay));
    }

    public int NextLoadOrder()
        => _session.Workspace is null ? 1 : _session.Workspace.Layers.Max(layer => layer.Module.LoadOrder) + 1;

    public static ModuleIdReservations DefaultReservations()
        => new(
            new RowIdRange(100, 199),
            new RowIdRange(1_000, 1_999),
            new RowIdRange(10_000, 19_999),
            new RowIdRange(1_000, 1_999),
            new RowIdRange(1_000, 1_999));

    private void ClearStartupIssueFor(string tag, string? folderPath)
    {
        _missingRememberedModules.RemoveAll(module =>
            string.Equals(module.DiagnosticTag, tag, StringComparison.OrdinalIgnoreCase) ||
            (!string.IsNullOrWhiteSpace(folderPath) &&
             string.Equals(module.FolderPath, folderPath, StringComparison.OrdinalIgnoreCase)));
        _startupDiagnostics.RemoveAll(item =>
            string.Equals(item.ModuleTag, tag, StringComparison.OrdinalIgnoreCase) ||
            (!string.IsNullOrWhiteSpace(folderPath) &&
             item.Code.StartsWith("WORKSPACE-MODULE-", StringComparison.Ordinal) &&
             item.Message.Contains(folderPath, StringComparison.OrdinalIgnoreCase)));
    }

    private void ValidateNewModuleRanges(
        ModuleIdReservations reservations,
        GalaxyMapModule? ignoredModule = null)
    {
        if (_session.Workspace is null)
        {
            return;
        }

        foreach (var existing in _session.Workspace.Modules)
        foreach (var table in ReservableTables)
        {
            if (ReferenceEquals(existing, ignoredModule))
            {
                continue;
            }
            var candidate = reservations.GetRange(table);
            var current = existing.Reservations.GetRange(table);
            if (candidate is { } left && current is { } right && left.Overlaps(right))
            {
                throw new InvalidOperationException(
                    $"The proposed {table} range {left} overlaps {existing.Tag}'s reserved range {right}.");
            }
        }
    }

    private void ValidateUpdatedModuleRows(
        GalaxyMapModule module,
        ModuleIdReservations reservations)
    {
        var workspace = _session.Workspace;
        var layer = workspace?.ModuleLayers.FirstOrDefault(candidate => ReferenceEquals(candidate.Module, module));
        if (workspace is null || layer is null)
        {
            return;
        }

        foreach (var table in ReservableTables)
        foreach (var row in layer.Rows(table))
        {
            var overridesLowerLayer = workspace.GetOverrideChain(row.Key).Any(candidate =>
                !string.Equals(candidate.Origin?.ModuleTag, module.Tag, StringComparison.OrdinalIgnoreCase));
            if (overridesLowerLayer)
            {
                continue;
            }

            var oldRange = module.Reservations.GetRange(table);
            var newRange = reservations.GetRange(table);
            if (oldRange?.Contains(row.RowId) == true && newRange?.Contains(row.RowId) != true)
            {
                throw new InvalidOperationException(
                    $"The proposed {table} range {newRange?.ToString() ?? "(none)"} would exclude existing new row {row.RowId}.");
            }
        }
    }

    private void EnsureUniqueTag(string tag)
    {
        if (_session.Workspace is not null && _session.Workspace.Layers.Any(layer =>
                string.Equals(layer.Module.Tag, tag, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException($"A module tagged {tag} is already mounted.");
        }
    }

    private static string SafeFolderName(string name, string fallback)
    {
        var invalid = Path.GetInvalidFileNameChars().ToHashSet();
        var builder = new StringBuilder(name.Length);
        foreach (var character in name.Trim())
        {
            builder.Append(invalid.Contains(character) ? '_' : character);
        }

        var result = builder.ToString().Trim(' ', '.');
        return result.Length == 0 ? GalaxyMapModule.SuggestTag(fallback) : result;
    }

    private static bool IsExpectedOperationFailure(Exception exception)
        => exception is GalaxyMapLoadException or IOException or UnauthorizedAccessException or
            InvalidOperationException or ArgumentException or OverflowException;
}
