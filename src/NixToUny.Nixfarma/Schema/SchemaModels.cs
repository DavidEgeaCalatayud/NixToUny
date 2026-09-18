namespace NixToUny.Nixfarma.Schema;

public enum NixfarmaDataArea
{
    Articles,
    Families,
    Customers,
    Credits,
    Stock
}

public sealed record OracleObjectInfo(
    string Owner,
    string Name,
    string ObjectType)
{
    public string QualifiedName => $"{Owner}.{Name}";
}

public sealed record OracleColumnInfo(
    string Owner,
    string ObjectName,
    string Name,
    string DataType,
    int DataLength,
    int? DataPrecision,
    int? DataScale,
    bool IsNullable,
    int Position,
    bool IsPrimaryKey);

public sealed record OracleRelationInfo(
    string ConstraintName,
    string Owner,
    string TableName,
    string ColumnName,
    string ReferencedOwner,
    string ReferencedTableName,
    string ReferencedColumnName,
    int Position)
{
    public bool IsOutgoingFrom(string owner, string tableName) =>
        string.Equals(Owner, owner, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(TableName, tableName, StringComparison.OrdinalIgnoreCase);
}

public sealed record SchemaCandidateResult(
    OracleObjectInfo Object,
    int Score,
    IReadOnlyList<string> MatchedTerms,
    IReadOnlyList<string> MatchingColumns);
