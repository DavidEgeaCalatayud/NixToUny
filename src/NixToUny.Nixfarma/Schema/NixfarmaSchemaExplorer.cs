using NixToUny.Nixfarma.Configuration;
using NixToUny.Nixfarma.Connection;
using Oracle.ManagedDataAccess.Client;

namespace NixToUny.Nixfarma.Schema;

/// <summary>
/// Explorador de metadatos Oracle de solo lectura.
/// Todas las sentencias son SELECT fijos contra el diccionario de datos.
/// No acepta SQL arbitrario ni contiene operaciones INSERT/UPDATE/DELETE/MERGE/DDL.
/// </summary>
public sealed class NixfarmaSchemaExplorer
{
    private const string ExcludedOwners = """
        'SYS','SYSTEM','XDB','MDSYS','CTXSYS','ORDSYS','ORDDATA','OUTLN',
        'DBSNMP','WMSYS','AUDSYS','GSMADMIN_INTERNAL','OJVMSYS','DVSYS','LBACSYS'
        """;

    private readonly NixfarmaConnectionOptions _options;

    private IReadOnlyList<OracleObjectInfo>? _objectsCache;
    private IReadOnlyList<OracleColumnInfo>? _columnIndexCache;

    public NixfarmaSchemaExplorer(NixfarmaConnectionOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    public async Task<IReadOnlyList<OracleObjectInfo>> DiscoverObjectsAsync(
        CancellationToken cancellationToken = default)
    {
        if (_objectsCache is not null)
            return _objectsCache;

        var sql = $"""
            SELECT owner, object_name, object_type
            FROM (
                SELECT owner, table_name AS object_name, 'TABLE' AS object_type
                FROM all_tables
                UNION ALL
                SELECT owner, view_name AS object_name, 'VIEW' AS object_type
                FROM all_views
            )
            WHERE owner NOT IN ({ExcludedOwners})
            ORDER BY owner, object_name
            """;

        var result = new List<OracleObjectInfo>();

        await using var connection = NixfarmaConnectionFactory.Create(_options);
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.CommandTimeout = 30;

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(new OracleObjectInfo(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2)));
        }

        _objectsCache = result;
        return result;
    }

    public async Task<IReadOnlyList<OracleColumnInfo>> DiscoverColumnsAsync(
        string owner,
        string objectName,
        CancellationToken cancellationToken = default)
    {
        ValidateIdentifier(owner, nameof(owner));
        ValidateIdentifier(objectName, nameof(objectName));

        const string sql = """
            SELECT
                c.owner,
                c.table_name,
                c.column_name,
                c.data_type,
                c.data_length,
                c.data_precision,
                c.data_scale,
                c.nullable,
                c.column_id,
                CASE
                    WHEN pk.column_name IS NULL THEN 0
                    ELSE 1
                END AS is_primary_key
            FROM all_tab_columns c
            LEFT JOIN (
                SELECT cc.owner, cc.table_name, cc.column_name
                FROM all_constraints con
                INNER JOIN all_cons_columns cc
                    ON cc.owner = con.owner
                   AND cc.constraint_name = con.constraint_name
                   AND cc.table_name = con.table_name
                WHERE con.constraint_type = 'P'
            ) pk
                ON pk.owner = c.owner
               AND pk.table_name = c.table_name
               AND pk.column_name = c.column_name
            WHERE c.owner = :owner
              AND c.table_name = :object_name
            ORDER BY c.column_id
            """;

        var result = new List<OracleColumnInfo>();

        await using var connection = NixfarmaConnectionFactory.Create(_options);
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.BindByName = true;
        command.CommandText = sql;
        command.CommandTimeout = 30;
        command.Parameters.Add("owner", OracleDbType.Varchar2).Value = owner.ToUpperInvariant();
        command.Parameters.Add("object_name", OracleDbType.Varchar2).Value = objectName.ToUpperInvariant();

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
            result.Add(ReadColumn(reader, includesPrimaryKey: true));

        return result;
    }

    public async Task<IReadOnlyList<OracleRelationInfo>> DiscoverRelationsAsync(
        string owner,
        string tableName,
        CancellationToken cancellationToken = default)
    {
        ValidateIdentifier(owner, nameof(owner));
        ValidateIdentifier(tableName, nameof(tableName));

        const string sql = """
            SELECT
                fk.constraint_name,
                fk.owner,
                fk.table_name,
                fkc.column_name,
                pk.owner AS referenced_owner,
                pk.table_name AS referenced_table,
                pkc.column_name AS referenced_column,
                fkc.position
            FROM all_constraints fk
            INNER JOIN all_cons_columns fkc
                ON fkc.owner = fk.owner
               AND fkc.constraint_name = fk.constraint_name
               AND fkc.table_name = fk.table_name
            INNER JOIN all_constraints pk
                ON pk.owner = fk.r_owner
               AND pk.constraint_name = fk.r_constraint_name
            INNER JOIN all_cons_columns pkc
                ON pkc.owner = pk.owner
               AND pkc.constraint_name = pk.constraint_name
               AND pkc.table_name = pk.table_name
               AND pkc.position = fkc.position
            WHERE fk.constraint_type = 'R'
              AND (
                    (fk.owner = :owner AND fk.table_name = :table_name)
                 OR (pk.owner = :owner AND pk.table_name = :table_name)
              )
            ORDER BY fk.owner, fk.table_name, fk.constraint_name, fkc.position
            """;

        var result = new List<OracleRelationInfo>();

        await using var connection = NixfarmaConnectionFactory.Create(_options);
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.BindByName = true;
        command.CommandText = sql;
        command.CommandTimeout = 30;
        command.Parameters.Add("owner", OracleDbType.Varchar2).Value = owner.ToUpperInvariant();
        command.Parameters.Add("table_name", OracleDbType.Varchar2).Value = tableName.ToUpperInvariant();

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(new OracleRelationInfo(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4),
                reader.GetString(5),
                reader.GetString(6),
                Convert.ToInt32(reader.GetValue(7))));
        }

        return result;
    }

    public async Task<IReadOnlyList<SchemaCandidateResult>> FindCandidatesAsync(
        NixfarmaDataArea area,
        int maxResults = 30,
        CancellationToken cancellationToken = default)
    {
        var objects = await DiscoverObjectsAsync(cancellationToken);
        var columns = await LoadColumnIndexAsync(cancellationToken);

        return SchemaSearchScorer.Score(area, objects, columns, maxResults);
    }

    public async Task<IReadOnlyList<SchemaCandidateResult>> SearchAsync(
        string query,
        int maxResults = 100,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query))
            return [];

        var objects = await DiscoverObjectsAsync(cancellationToken);
        var columns = await LoadColumnIndexAsync(cancellationToken);

        return SchemaSearchScorer.SearchText(query, objects, columns, maxResults);
    }

    public void ClearCache()
    {
        _objectsCache = null;
        _columnIndexCache = null;
    }

    private async Task<IReadOnlyList<OracleColumnInfo>> LoadColumnIndexAsync(
        CancellationToken cancellationToken)
    {
        if (_columnIndexCache is not null)
            return _columnIndexCache;

        var sql = $"""
            SELECT
                owner,
                table_name,
                column_name,
                data_type,
                data_length,
                data_precision,
                data_scale,
                nullable,
                column_id
            FROM all_tab_columns
            WHERE owner NOT IN ({ExcludedOwners})
            ORDER BY owner, table_name, column_id
            """;

        var result = new List<OracleColumnInfo>();

        await using var connection = NixfarmaConnectionFactory.Create(_options);
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.CommandTimeout = 45;

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
            result.Add(ReadColumn(reader, includesPrimaryKey: false));

        _columnIndexCache = result;
        return result;
    }

    private static OracleColumnInfo ReadColumn(
        OracleDataReader reader,
        bool includesPrimaryKey)
    {
        return new OracleColumnInfo(
            reader.GetString(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetString(3),
            Convert.ToInt32(reader.GetValue(4)),
            reader.IsDBNull(5) ? null : Convert.ToInt32(reader.GetValue(5)),
            reader.IsDBNull(6) ? null : Convert.ToInt32(reader.GetValue(6)),
            string.Equals(reader.GetString(7), "Y", StringComparison.OrdinalIgnoreCase),
            Convert.ToInt32(reader.GetValue(8)),
            includesPrimaryKey && Convert.ToInt32(reader.GetValue(9)) == 1);
    }

    private static void ValidateIdentifier(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("El identificador Oracle no puede estar vacío.", parameterName);

        if (value.Length > 128)
            throw new ArgumentException("El identificador Oracle es demasiado largo.", parameterName);
    }
}
