namespace NixToUny.Nixfarma.Schema;

public static class SchemaSearchScorer
{
    private sealed record SearchProfile(
        string[] ObjectTerms,
        string[] ColumnTerms);

    private static readonly IReadOnlyDictionary<NixfarmaDataArea, SearchProfile> Profiles =
        new Dictionary<NixfarmaDataArea, SearchProfile>
        {
            [NixfarmaDataArea.Articles] = new(
                ["ARTIC", "PRODUCT", "FARMAC", "MEDIC", "ESPECIAL", "CATALOG"],
                ["CODNAC", "CODIGO", "ARTIC", "DESCRIP", "PVP", "PVF", "EAN", "LABOR", "FAMIL"]),

            [NixfarmaDataArea.Families] = new(
                ["FAMIL", "GRUPO", "CATEGOR", "SECCION"],
                ["FAMIL", "CODFAM", "GRUPO", "CATEGOR", "DESCRIP"]),

            [NixfarmaDataArea.Customers] = new(
                ["CLIENT", "PACIENT", "SOCIO", "TITULAR"],
                ["CLIENT", "NIF", "CIF", "NOMBRE", "APELL", "DIREC", "TELEF", "EMAIL"]),

            [NixfarmaDataArea.Credits] = new(
                ["CREDIT", "DEUDA", "FIADO", "PENDIENT", "CUENTA"],
                ["CREDIT", "DEUDA", "SALDO", "IMPORTE", "CLIENT", "FECHA", "PENDIENT"]),

            [NixfarmaDataArea.Stock] = new(
                ["STOCK", "EXIST", "ALMAC", "INVENT"],
                ["STOCK", "EXIST", "MINIMO", "MAXIMO", "CANTIDAD", "UNIDAD", "ALMAC"])
        };

    public static IReadOnlyList<SchemaCandidateResult> Score(
        NixfarmaDataArea area,
        IReadOnlyCollection<OracleObjectInfo> objects,
        IReadOnlyCollection<OracleColumnInfo> columns,
        int maxResults = 30)
    {
        if (!Profiles.TryGetValue(area, out var profile))
            return [];

        var columnLookup = BuildColumnLookup(columns);
        var results = new List<SchemaCandidateResult>();

        foreach (var obj in objects)
        {
            var score = 0;
            var matched = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var matchingColumns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var term in profile.ObjectTerms)
            {
                var termScore = ScoreIdentifier(obj.Name, term, exact: 30, startsWith: 20, contains: 12);
                if (termScore <= 0)
                    continue;

                score += termScore;
                matched.Add(term);
            }

            if (columnLookup.TryGetValue(Key(obj.Owner, obj.Name), out var objectColumns))
            {
                foreach (var column in objectColumns)
                {
                    foreach (var term in profile.ColumnTerms)
                    {
                        var termScore = ScoreIdentifier(column.Name, term, exact: 10, startsWith: 7, contains: 4);
                        if (termScore <= 0)
                            continue;

                        score += termScore;
                        matched.Add(term);
                        matchingColumns.Add(column.Name);
                    }
                }
            }

            if (score > 0)
            {
                results.Add(new SchemaCandidateResult(
                    obj,
                    score,
                    matched.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray(),
                    matchingColumns.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray()));
            }
        }

        return results
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.Object.Owner, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.Object.Name, StringComparer.OrdinalIgnoreCase)
            .Take(Math.Max(1, maxResults))
            .ToArray();
    }

    public static IReadOnlyList<SchemaCandidateResult> SearchText(
        string query,
        IReadOnlyCollection<OracleObjectInfo> objects,
        IReadOnlyCollection<OracleColumnInfo> columns,
        int maxResults = 100)
    {
        var terms = query
            .Split([' ', ',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (terms.Length == 0)
            return [];

        var columnLookup = BuildColumnLookup(columns);
        var results = new List<SchemaCandidateResult>();

        foreach (var obj in objects)
        {
            var score = 0;
            var matched = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var matchingColumns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var term in terms)
            {
                var objectScore = ScoreIdentifier(obj.Name, term, exact: 30, startsWith: 20, contains: 12);
                if (objectScore > 0)
                {
                    score += objectScore;
                    matched.Add(term);
                }
            }

            if (columnLookup.TryGetValue(Key(obj.Owner, obj.Name), out var objectColumns))
            {
                foreach (var column in objectColumns)
                {
                    foreach (var term in terms)
                    {
                        var columnScore = ScoreIdentifier(column.Name, term, exact: 10, startsWith: 7, contains: 4);
                        if (columnScore <= 0)
                            continue;

                        score += columnScore;
                        matched.Add(term);
                        matchingColumns.Add(column.Name);
                    }
                }
            }

            if (score > 0)
            {
                results.Add(new SchemaCandidateResult(
                    obj,
                    score,
                    matched.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray(),
                    matchingColumns.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray()));
            }
        }

        return results
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.Object.Owner, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.Object.Name, StringComparer.OrdinalIgnoreCase)
            .Take(Math.Max(1, maxResults))
            .ToArray();
    }

    private static Dictionary<string, List<OracleColumnInfo>> BuildColumnLookup(
        IReadOnlyCollection<OracleColumnInfo> columns)
    {
        var lookup = new Dictionary<string, List<OracleColumnInfo>>(StringComparer.OrdinalIgnoreCase);

        foreach (var column in columns)
        {
            var key = Key(column.Owner, column.ObjectName);

            if (!lookup.TryGetValue(key, out var list))
            {
                list = [];
                lookup[key] = list;
            }

            list.Add(column);
        }

        return lookup;
    }

    private static int ScoreIdentifier(
        string source,
        string term,
        int exact,
        int startsWith,
        int contains)
    {
        if (string.Equals(source, term, StringComparison.OrdinalIgnoreCase))
            return exact;

        if (source.StartsWith(term, StringComparison.OrdinalIgnoreCase))
            return startsWith;

        return source.Contains(term, StringComparison.OrdinalIgnoreCase)
            ? contains
            : 0;
    }

    private static string Key(string owner, string objectName) =>
        $"{owner}\u001F{objectName}";
}
