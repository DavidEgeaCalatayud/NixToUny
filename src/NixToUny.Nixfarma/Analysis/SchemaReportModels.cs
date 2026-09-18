using NixToUny.Nixfarma.Schema;

namespace NixToUny.Nixfarma.Analysis;

public sealed record NixfarmaSchemaReport(
    string FormatVersion,
    DateTimeOffset GeneratedAtUtc,
    int ObjectCount,
    IReadOnlyList<NixfarmaAreaReport> Areas);

public sealed record NixfarmaAreaReport(
    NixfarmaDataArea Area,
    IReadOnlyList<NixfarmaCandidateReport> Candidates);

public sealed record NixfarmaCandidateReport(
    OracleObjectInfo Object,
    int Score,
    IReadOnlyList<string> MatchedTerms,
    IReadOnlyList<string> MatchingColumns,
    IReadOnlyList<OracleColumnInfo> Columns,
    IReadOnlyList<OracleRelationInfo> Relations,
    string SampleQuery);
