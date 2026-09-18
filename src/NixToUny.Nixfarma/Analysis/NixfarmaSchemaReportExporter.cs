using System.Text.Json;
using NixToUny.Nixfarma.Schema;

namespace NixToUny.Nixfarma.Analysis;

public sealed class NixfarmaSchemaReportExporter
{
    private readonly NixfarmaSchemaExplorer _explorer;

    public NixfarmaSchemaReportExporter(NixfarmaSchemaExplorer explorer)
    {
        _explorer = explorer ?? throw new ArgumentNullException(nameof(explorer));
    }

    public async Task<NixfarmaSchemaReport> BuildAsync(
        int candidatesPerArea = 10,
        CancellationToken cancellationToken = default)
    {
        if (candidatesPerArea is < 1 or > 30)
            throw new ArgumentOutOfRangeException(nameof(candidatesPerArea));

        var objects = await _explorer.DiscoverObjectsAsync(cancellationToken);
        var areas = new List<NixfarmaAreaReport>();

        foreach (var area in Enum.GetValues<NixfarmaDataArea>())
        {
            var candidates = await _explorer.FindCandidatesAsync(
                area,
                candidatesPerArea,
                cancellationToken);

            var reports = new List<NixfarmaCandidateReport>();

            foreach (var candidate in candidates)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var columns = await _explorer.DiscoverColumnsAsync(
                    candidate.Object.Owner,
                    candidate.Object.Name,
                    cancellationToken);

                IReadOnlyList<OracleRelationInfo> relations =
                    string.Equals(candidate.Object.ObjectType, "TABLE", StringComparison.OrdinalIgnoreCase)
                        ? await _explorer.DiscoverRelationsAsync(
                            candidate.Object.Owner,
                            candidate.Object.Name,
                            cancellationToken)
                        : [];

                reports.Add(new NixfarmaCandidateReport(
                    candidate.Object,
                    candidate.Score,
                    candidate.MatchedTerms,
                    candidate.MatchingColumns,
                    columns,
                    relations,
                    SampleQueryBuilder.Build(candidate.Object, 20)));
            }

            areas.Add(new NixfarmaAreaReport(area, reports));
        }

        return new NixfarmaSchemaReport(
            "nix-to-uny-schema-v1",
            DateTimeOffset.UtcNow,
            objects.Count,
            areas);
    }

    public async Task<NixfarmaSchemaReport> ExportAsync(
        string filePath,
        int candidatesPerArea = 10,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            throw new ArgumentException("La ruta no puede estar vacía.", nameof(filePath));

        var report = await BuildAsync(candidatesPerArea, cancellationToken);

        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        await using var stream = File.Create(filePath);
        await JsonSerializer.SerializeAsync(stream, report, options, cancellationToken);

        return report;
    }
}
