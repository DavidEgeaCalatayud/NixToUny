using NixToUny.Nixfarma.Schema;

namespace NixToUny.Nixfarma.Analysis;

public sealed record NixfarmaSchemaReport(
    string FormatVersion,
    DateTimeOffset GeneratedAtUtc,
    int OwnerCount,
    int TableCount,
    int ViewCount,
    int ColumnCount,
    int RelationCount,
    IReadOnlyList<NixfarmaOwnerReport> Owners,
    IReadOnlyList<NixfarmaAreaReport> CandidateAreas);

public sealed record NixfarmaOwnerReport(
    string Owner,
    int ObjectCount,
    IReadOnlyList<NixfarmaObjectReport> Objects);

public sealed record NixfarmaObjectReport(
    OracleObjectInfo Object,
    IReadOnlyList<OracleColumnInfo> Columns,
    IReadOnlyList<OracleRelationInfo> OutgoingRelations,
    IReadOnlyList<OracleRelationInfo> IncomingRelations,
    string SampleQuery);

public sealed record NixfarmaAreaReport(
    NixfarmaDataArea Area,
    IReadOnlyList<NixfarmaCandidateReport> Candidates);

public sealed record NixfarmaCandidateReport(
    string QualifiedName,
    string ObjectType,
    int Score,
    IReadOnlyList<string> MatchedTerms,
    IReadOnlyList<string> MatchingColumns);
