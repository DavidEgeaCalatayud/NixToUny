using NixToUny.Nixfarma.Schema;

namespace NixToUny.Nixfarma.Analysis;

public sealed record NixfarmaSnapshotManifest(
    string FormatVersion,
    DateTimeOffset GeneratedAtUtc,
    int RowsPerObject,
    bool Sanitized,
    int ObjectCount,
    int SuccessfulSamples,
    int FailedSamples,
    IReadOnlyList<NixfarmaSnapshotObjectSummary> Objects);

public sealed record NixfarmaSnapshotObjectSummary(
    string Owner,
    string Name,
    string ObjectType,
    int ColumnCount,
    int SampleRows,
    string SampleFile,
    string? Error);

public sealed record NixfarmaObjectSample(
    OracleObjectInfo Object,
    int RowLimit,
    IReadOnlyList<NixfarmaSampleColumn> Columns,
    IReadOnlyList<IReadOnlyList<string?>> Rows);

public sealed record NixfarmaSampleColumn(
    string Name,
    string DataType,
    string Protection);

public sealed record NixfarmaSnapshotProgress(
    int Current,
    int Total,
    string QualifiedName,
    string Status);
