using LE1GalaxyMapEditor.Models;

namespace LE1GalaxyMapEditor.Workflows.Ports;

public sealed record ExternalOperationResult<T>(
    bool Succeeded,
    T? Value = default,
    string? Error = null,
    IReadOnlyList<string>? Warnings = null);

public sealed record PackageTableData(
    GalaxyMapTable Table,
    IReadOnlyList<string> Columns,
    IReadOnlyList<IReadOnlyList<object?>> Rows);

public sealed record PackageImportRequest(string PackagePath, IReadOnlySet<GalaxyMapTable> Tables);
public sealed record PackageExportRequest(string SourcePackagePath, string OutputPackagePath, string ModuleTag);
public sealed record TlkEntry(int Id, string Text);
public sealed record TlkWriteRequest(string SourcePath, string OutputPath, IReadOnlyList<TlkEntry> Entries);

public interface ILegendaryExplorerGateway
{
    Task<ExternalOperationResult<IReadOnlyList<PackageTableData>>> ExtractGalaxyMapTablesAsync(
        PackageImportRequest request,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default);

    Task<ExternalOperationResult<string>> WriteGalaxyMapTablesAsync(
        PackageExportRequest request,
        IReadOnlyList<PackageTableData> tables,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default);
}

public interface ITlkGateway
{
    Task<ExternalOperationResult<IReadOnlyDictionary<int, string>>> ReadAsync(
        string tlkPath,
        CancellationToken cancellationToken = default);

    Task<ExternalOperationResult<int>> AllocateIdAsync(
        string tlkPath,
        IReadOnlySet<int> reservedIds,
        CancellationToken cancellationToken = default);

    Task<ExternalOperationResult<string>> WriteAsync(
        TlkWriteRequest request,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default);
}
