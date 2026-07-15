using LE1GalaxyMapEditor.Models;

namespace LE1GalaxyMapEditor.Workflows.Editing;

public sealed record PendingFileWrite(
    string ModuleTag,
    string RelativePath,
    byte[] Contents,
    string Purpose,
    GalaxyMapRowKey? RelatedRow = null,
    string? CacheKey = null);

public sealed record EditChangeSetSnapshot(
    IReadOnlyDictionary<string, IReadOnlySet<GalaxyMapTable>> DirtyTables,
    IReadOnlySet<string> DirtyModuleMetadata,
    IReadOnlyList<PendingFileWrite> PendingFiles);

public sealed class EditChangeSet
{
    private readonly Dictionary<string, HashSet<GalaxyMapTable>> _dirtyTables =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _dirtyModuleMetadata = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<(string ModuleTag, string RelativePath), PendingFileWrite> _pendingFiles = [];

    public IReadOnlyDictionary<string, IReadOnlySet<GalaxyMapTable>> DirtyTables
        => _dirtyTables.ToDictionary(
            pair => pair.Key,
            pair => (IReadOnlySet<GalaxyMapTable>)pair.Value,
            StringComparer.OrdinalIgnoreCase);

    public IReadOnlySet<string> DirtyModuleMetadata => _dirtyModuleMetadata;
    public IReadOnlyCollection<PendingFileWrite> PendingFiles => _pendingFiles.Values;
    public bool HasChanges => _dirtyTables.Count > 0 || _dirtyModuleMetadata.Count > 0 || _pendingFiles.Count > 0;
    public int Count => _dirtyTables.Values.Sum(tables => tables.Count) +
                        _dirtyModuleMetadata.Count + _pendingFiles.Count;

    internal bool MarkTable(string moduleTag, GalaxyMapTable table)
    {
        if (!_dirtyTables.TryGetValue(moduleTag, out var tables))
        {
            tables = [];
            _dirtyTables[moduleTag] = tables;
        }

        return tables.Add(table);
    }

    internal bool MarkTables(string moduleTag, IEnumerable<GalaxyMapTable> dirtyTables)
    {
        var changed = false;
        foreach (var table in dirtyTables)
        {
            changed |= MarkTable(moduleTag, table);
        }

        return changed;
    }

    internal bool MarkMetadata(string moduleTag) => _dirtyModuleMetadata.Add(moduleTag);

    internal void StageFile(PendingFileWrite pending)
        => _pendingFiles[(pending.ModuleTag, pending.RelativePath)] = Clone(pending);

    public bool ContainsModule(string moduleTag)
        => _dirtyTables.ContainsKey(moduleTag) ||
           _dirtyModuleMetadata.Contains(moduleTag) ||
           _pendingFiles.Keys.Any(key => string.Equals(key.ModuleTag, moduleTag, StringComparison.OrdinalIgnoreCase));

    internal IEnumerable<string> ModuleTags()
        => _dirtyTables.Keys
            .Concat(_dirtyModuleMetadata)
            .Concat(_pendingFiles.Keys.Select(key => key.ModuleTag))
            .Distinct(StringComparer.OrdinalIgnoreCase);

    internal IReadOnlySet<GalaxyMapTable> TablesFor(string moduleTag)
        => _dirtyTables.TryGetValue(moduleTag, out var tables)
            ? tables
            : new HashSet<GalaxyMapTable>();

    internal IReadOnlyList<PendingFileWrite> FilesFor(string moduleTag)
        => _pendingFiles.Values.Where(file =>
                string.Equals(file.ModuleTag, moduleTag, StringComparison.OrdinalIgnoreCase))
            .ToArray();

    internal bool IsMetadataDirty(string moduleTag) => _dirtyModuleMetadata.Contains(moduleTag);

    internal void ClearModule(string moduleTag)
    {
        _dirtyTables.Remove(moduleTag);
        _dirtyModuleMetadata.Remove(moduleTag);
        foreach (var key in _pendingFiles.Keys.Where(key =>
                     string.Equals(key.ModuleTag, moduleTag, StringComparison.OrdinalIgnoreCase)).ToArray())
        {
            _pendingFiles.Remove(key);
        }
    }

    internal void RemoveModule(string moduleTag) => ClearModule(moduleTag);

    internal void MigrateModuleTag(string oldTag, string newTag)
    {
        if (string.Equals(oldTag, newTag, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (_dirtyTables.Remove(oldTag, out var tables))
        {
            _dirtyTables[newTag] = tables;
        }

        if (_dirtyModuleMetadata.Remove(oldTag))
        {
            _dirtyModuleMetadata.Add(newTag);
        }

        foreach (var pair in _pendingFiles.Where(pair =>
                     string.Equals(pair.Key.ModuleTag, oldTag, StringComparison.OrdinalIgnoreCase)).ToArray())
        {
            _pendingFiles.Remove(pair.Key);
            var migrated = pair.Value with { ModuleTag = newTag };
            _pendingFiles[(newTag, migrated.RelativePath)] = migrated;
        }
    }

    internal EditChangeSetSnapshot Capture()
        => new(
            _dirtyTables.ToDictionary(
                pair => pair.Key,
                pair => (IReadOnlySet<GalaxyMapTable>)new HashSet<GalaxyMapTable>(pair.Value),
                StringComparer.OrdinalIgnoreCase),
            new HashSet<string>(_dirtyModuleMetadata, StringComparer.OrdinalIgnoreCase),
            _pendingFiles.Values.Select(Clone).ToArray());

    internal void Restore(EditChangeSetSnapshot snapshot)
    {
        Clear();
        foreach (var pair in snapshot.DirtyTables)
        {
            _dirtyTables[pair.Key] = new HashSet<GalaxyMapTable>(pair.Value);
        }

        foreach (var tag in snapshot.DirtyModuleMetadata)
        {
            _dirtyModuleMetadata.Add(tag);
        }

        foreach (var pending in snapshot.PendingFiles)
        {
            StageFile(pending);
        }
    }

    internal void Clear()
    {
        _dirtyTables.Clear();
        _dirtyModuleMetadata.Clear();
        _pendingFiles.Clear();
    }

    private static PendingFileWrite Clone(PendingFileWrite pending)
        => pending with { Contents = pending.Contents.ToArray() };
}
