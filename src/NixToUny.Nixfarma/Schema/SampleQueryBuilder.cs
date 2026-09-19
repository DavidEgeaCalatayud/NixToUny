namespace NixToUny.Nixfarma.Schema;

public static class SampleQueryBuilder
{
    public static string Build(OracleObjectInfo obj, int maxRows = 20)
    {
        ArgumentNullException.ThrowIfNull(obj);

        if (maxRows is < 1 or > 100)
            throw new ArgumentOutOfRangeException(nameof(maxRows));

        return $"SELECT * FROM {Quote(obj.Owner)}.{Quote(obj.Name)} WHERE ROWNUM <= {maxRows}";
    }

    private static string Quote(string identifier) =>
        $"\"{identifier.Replace("\"", "\"\"")}\"";
}
