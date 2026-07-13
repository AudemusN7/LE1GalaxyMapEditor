using System.Collections.ObjectModel;

namespace LE1GalaxyMapEditor.Models;

/// <summary>
/// Preserves the exact lexical form of a source CSV row so an override can copy
/// every untouched value verbatim. The first header may be blank, as it is in a
/// Legendary Explorer 2DA export.
/// </summary>
public sealed class CsvRowSnapshot
{
    public const string RowIdColumnName = "Row ID";

    private readonly string[] _headers;
    private readonly string[] _originalValues;
    private readonly HashSet<string> _dirtyColumns;
    private readonly ReadOnlyCollection<string> _headerView;
    private readonly ReadOnlyCollection<string> _valueView;

    public CsvRowSnapshot(
        string sourceName,
        int sourceRowNumber,
        IEnumerable<string> headers,
        IEnumerable<string> originalValues)
        : this(sourceName, sourceRowNumber, headers, originalValues, [])
    {
    }

    private CsvRowSnapshot(
        string sourceName,
        int sourceRowNumber,
        IEnumerable<string> headers,
        IEnumerable<string> originalValues,
        IEnumerable<string> dirtyColumns)
    {
        SourceName = sourceName ?? string.Empty;
        SourceRowNumber = sourceRowNumber;
        _headers = headers.ToArray();
        _originalValues = originalValues.ToArray();
        if (_headers.Length != _originalValues.Length)
        {
            throw new ArgumentException("CSV headers and row values must have the same length.");
        }

        _dirtyColumns = new HashSet<string>(dirtyColumns, StringComparer.OrdinalIgnoreCase);
        _headerView = Array.AsReadOnly(_headers);
        _valueView = Array.AsReadOnly(_originalValues);
    }

    private CsvRowSnapshot(
        string sourceName,
        int sourceRowNumber,
        string[] headers,
        string[] originalValues,
        ReadOnlyCollection<string> headerView,
        ReadOnlyCollection<string> valueView,
        IEnumerable<string> dirtyColumns)
    {
        SourceName = sourceName;
        SourceRowNumber = sourceRowNumber;
        _headers = headers;
        _originalValues = originalValues;
        _dirtyColumns = new HashSet<string>(dirtyColumns, StringComparer.OrdinalIgnoreCase);
        _headerView = headerView;
        _valueView = valueView;
    }

    public string SourceName { get; }
    public int SourceRowNumber { get; }
    public IReadOnlyList<string> Headers => _headerView;
    public IReadOnlyList<string> OriginalValues => _valueView;
    public IReadOnlySet<string> DirtyColumns => _dirtyColumns;
    public bool HasChanges => _dirtyColumns.Count > 0;

    public void MarkDirty(string columnName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(columnName);
        _dirtyColumns.Add(NormalizeColumnName(columnName));
    }

    public bool IsDirty(string columnName)
        => !string.IsNullOrWhiteSpace(columnName) && _dirtyColumns.Contains(NormalizeColumnName(columnName));

    public string? GetOriginalValue(string columnName)
    {
        var index = FindColumnIndex(columnName);
        return index >= 0 ? _originalValues[index] : null;
    }

    public CsvRowSnapshot Clone()
        => new(
            SourceName,
            SourceRowNumber,
            _headers,
            _originalValues,
            _headerView,
            _valueView,
            _dirtyColumns);

    /// <summary>
    /// Creates the pristine snapshot used when materialising a same-ID override.
    /// The editor marks only the newly changed column dirty afterwards.
    /// </summary>
    public CsvRowSnapshot CloneForOverride()
        => new(
            SourceName,
            SourceRowNumber,
            _headers,
            _originalValues,
            _headerView,
            _valueView,
            []);

    /// <summary>
    /// Transfers arrays created by the CSV parser into an immutable snapshot.
    /// The parser never mutates these arrays after this call, so later row clones
    /// can safely share them and allocate only their independent dirty-column set.
    /// </summary>
    internal static CsvRowSnapshot FromParsedRow(
        string sourceName,
        int sourceRowNumber,
        string[] headers,
        string[] originalValues)
    {
        ArgumentNullException.ThrowIfNull(headers);
        ArgumentNullException.ThrowIfNull(originalValues);
        if (headers.Length != originalValues.Length)
        {
            throw new ArgumentException("CSV headers and row values must have the same length.");
        }

        return new CsvRowSnapshot(
            sourceName ?? string.Empty,
            sourceRowNumber,
            headers,
            originalValues,
            Array.AsReadOnly(headers),
            Array.AsReadOnly(originalValues),
            []);
    }

    private int FindColumnIndex(string columnName)
    {
        if (string.IsNullOrWhiteSpace(columnName))
        {
            return -1;
        }

        var normalized = NormalizeColumnName(columnName);
        for (var index = 0; index < _headers.Length; index++)
        {
            var header = index == 0 && string.IsNullOrWhiteSpace(_headers[index])
                ? RowIdColumnName
                : _headers[index];
            if (string.Equals(header, normalized, StringComparison.OrdinalIgnoreCase))
            {
                return index;
            }
        }

        return -1;
    }

    private static string NormalizeColumnName(string columnName)
        => string.Equals(columnName.Trim(), RowIdColumnName, StringComparison.OrdinalIgnoreCase)
            ? RowIdColumnName
            : columnName.Trim();
}

/// <summary>Exact header metadata for a physical CSV table, including its blank first header.</summary>
public sealed class CsvTableSchema
{
    private readonly ReadOnlyCollection<string> _headers;

    public CsvTableSchema(GalaxyMapTable table, IEnumerable<string> headers)
    {
        Table = table;
        _headers = Array.AsReadOnly(headers.ToArray());
        if (_headers.Count == 0)
        {
            throw new ArgumentException("A CSV table schema must contain at least its Row ID column.", nameof(headers));
        }
    }

    public GalaxyMapTable Table { get; }
    public IReadOnlyList<string> Headers => _headers;
}
