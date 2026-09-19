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
        int candidatesPerArea = 20,
        CancellationToken cancellationToken = default)
    {
        if (candidatesPerArea is < 1 or > 50)
            throw new ArgumentOutOfRangeException(nameof(candidatesPerArea));

        var objectsTask = _explorer.DiscoverObjectsAsync(cancellationToken);
        var columnsTask = _explorer.DiscoverAllColumnsAsync(cancellationToken);
        var relationsTask = _explorer.DiscoverAllRelationsAsync(cancellationToken);

        await Task.WhenAll(objectsTask, columnsTask, relationsTask);

        var objects = await objectsTask;
        var columns = await columnsTask;
        var relations = await relationsTask;

        var columnsByObject = columns
            .GroupBy(x => Key(x.Owner, x.ObjectName), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                x => x.Key,
                x => (IReadOnlyList<OracleColumnInfo>)x
                    .OrderBy(c => c.Position)
                    .ToArray(),
                StringComparer.OrdinalIgnoreCase);

        var outgoingByObject = relations
            .GroupBy(x => Key(x.Owner, x.TableName), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                x => x.Key,
                x => (IReadOnlyList<OracleRelationInfo>)x.ToArray(),
                StringComparer.OrdinalIgnoreCase);

        var incomingByObject = relations
            .GroupBy(x => Key(x.ReferencedOwner, x.ReferencedTableName), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                x => x.Key,
                x => (IReadOnlyList<OracleRelationInfo>)x.ToArray(),
                StringComparer.OrdinalIgnoreCase);

        var ownerReports = objects
            .GroupBy(x => x.Owner, StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
            .Select(ownerGroup =>
            {
                var objectReports = ownerGroup
                    .OrderBy(x => x.ObjectType, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
                    .Select(obj =>
                    {
                        var key = Key(obj.Owner, obj.Name);

                        columnsByObject.TryGetValue(key, out var objectColumns);
                        outgoingByObject.TryGetValue(key, out var outgoing);
                        incomingByObject.TryGetValue(key, out var incoming);

                        return new NixfarmaObjectReport(
                            obj,
                            objectColumns ?? [],
                            outgoing ?? [],
                            incoming ?? [],
                            SampleQueryBuilder.Build(obj, 20));
                    })
                    .ToArray();

                return new NixfarmaOwnerReport(
                    ownerGroup.Key,
                    objectReports.Length,
                    objectReports);
            })
            .ToArray();

        var candidateAreas = new List<NixfarmaAreaReport>();

        foreach (var area in Enum.GetValues<NixfarmaDataArea>())
        {
            var candidates = await _explorer.FindCandidatesAsync(
                area,
                candidatesPerArea,
                cancellationToken);

            candidateAreas.Add(new NixfarmaAreaReport(
                area,
                candidates.Select(candidate => new NixfarmaCandidateReport(
                    candidate.Object.QualifiedName,
                    candidate.Object.ObjectType,
                    candidate.Score,
                    candidate.MatchedTerms,
                    candidate.MatchingColumns))
                .ToArray()));
        }

        return new NixfarmaSchemaReport(
            "nix-to-uny-schema-v2",
            DateTimeOffset.UtcNow,
            ownerReports.Length,
            objects.Count(x => string.Equals(x.ObjectType, "TABLE", StringComparison.OrdinalIgnoreCase)),
            objects.Count(x => string.Equals(x.ObjectType, "VIEW", StringComparison.OrdinalIgnoreCase)),
            columns.Count,
            relations.Count,
            ownerReports,
            candidateAreas);
    }

    public async Task<NixfarmaSchemaReport> ExportAsync(
        string filePath,
        int candidatesPerArea = 20,
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

    private static string Key(string owner, string objectName) =>
        $"{owner}\u001F{objectName}";
}
