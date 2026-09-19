using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using NixToUny.Nixfarma.Configuration;
using NixToUny.Nixfarma.Connection;
using NixToUny.Nixfarma.Schema;
using Oracle.ManagedDataAccess.Client;

namespace NixToUny.Nixfarma.Analysis;

/// <summary>
/// Genera un snapshot técnico portátil del esquema accesible y una muestra
/// sanitizada, limitada y de solo lectura de cada tabla/vista.
/// </summary>
public sealed class NixfarmaSnapshotExporter
{
    private readonly NixfarmaConnectionOptions _options;
    private readonly NixfarmaSchemaExplorer _explorer;
    private readonly SnapshotSanitizer _sanitizer = new();

    public NixfarmaSnapshotExporter(
        NixfarmaConnectionOptions options,
        NixfarmaSchemaExplorer explorer)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _explorer = explorer ?? throw new ArgumentNullException(nameof(explorer));
    }

    public async Task<NixfarmaSnapshotManifest> ExportAsync(
        string zipPath,
        int rowsPerObject = 10,
        IProgress<NixfarmaSnapshotProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(zipPath))
            throw new ArgumentException("La ruta no puede estar vacía.", nameof(zipPath));

        if (rowsPerObject is < 1 or > 20)
            throw new ArgumentOutOfRangeException(nameof(rowsPerObject));

        var schemaExporter = new NixfarmaSchemaReportExporter(_explorer);
        var schema = await schemaExporter.BuildAsync(20, cancellationToken);

        var objectReports = schema.Owners
            .SelectMany(owner => owner.Objects)
            .OrderBy(x => x.Object.Owner, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.Object.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var outputDirectory = Path.GetDirectoryName(Path.GetFullPath(zipPath));
        if (!string.IsNullOrWhiteSpace(outputDirectory))
            Directory.CreateDirectory(outputDirectory);

        if (File.Exists(zipPath))
            File.Delete(zipPath);

        var jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        var summaries = new List<NixfarmaSnapshotObjectSummary>(objectReports.Length);

        await using var connection = NixfarmaConnectionFactory.Create(_options);
        await connection.OpenAsync(cancellationToken);

        using var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create);

        await WriteJsonEntryAsync(
            archive,
            "schema.json",
            schema,
            jsonOptions,
            cancellationToken);

        var index = 0;

        foreach (var objectReport in objectReports)
        {
            cancellationToken.ThrowIfCancellationRequested();
            index++;

            var obj = objectReport.Object;
            var fileName =
                $"samples/{SanitizePathPart(obj.Owner)}/{SanitizePathPart(obj.Name)}.{obj.ObjectType.ToLowerInvariant()}.json";

            progress?.Report(new NixfarmaSnapshotProgress(
                index,
                objectReports.Length,
                obj.QualifiedName,
                "Leyendo muestra"));

            try
            {
                var sample = await ReadSampleAsync(
                    connection,
                    objectReport,
                    rowsPerObject,
                    cancellationToken);

                await WriteJsonEntryAsync(
                    archive,
                    fileName,
                    sample,
                    jsonOptions,
                    cancellationToken);

                summaries.Add(new NixfarmaSnapshotObjectSummary(
                    obj.Owner,
                    obj.Name,
                    obj.ObjectType,
                    objectReport.Columns.Count,
                    sample.Rows.Count,
                    fileName,
                    null));

                progress?.Report(new NixfarmaSnapshotProgress(
                    index,
                    objectReports.Length,
                    obj.QualifiedName,
                    $"{sample.Rows.Count} filas"));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                summaries.Add(new NixfarmaSnapshotObjectSummary(
                    obj.Owner,
                    obj.Name,
                    obj.ObjectType,
                    objectReport.Columns.Count,
                    0,
                    fileName,
                    CleanError(ex.Message)));

                progress?.Report(new NixfarmaSnapshotProgress(
                    index,
                    objectReports.Length,
                    obj.QualifiedName,
                    "Error; se continúa"));
            }
        }

        var manifest = new NixfarmaSnapshotManifest(
            "nix-to-uny-snapshot-v1",
            DateTimeOffset.UtcNow,
            rowsPerObject,
            true,
            summaries.Count,
            summaries.Count(x => x.Error is null),
            summaries.Count(x => x.Error is not null),
            summaries);

        await WriteJsonEntryAsync(
            archive,
            "manifest.json",
            manifest,
            jsonOptions,
            cancellationToken);

        await WriteTextEntryAsync(
            archive,
            "README.txt",
            BuildReadme(manifest),
            cancellationToken);

        return manifest;
    }

    private async Task<NixfarmaObjectSample> ReadSampleAsync(
        OracleConnection connection,
        NixfarmaObjectReport report,
        int maxRows,
        CancellationToken cancellationToken)
    {
        if (report.Columns.Count == 0)
        {
            return new NixfarmaObjectSample(
                report.Object,
                maxRows,
                [],
                []);
        }

        var projections = report.Columns
            .Select(column => BuildProjection(column))
            .ToArray();

        var sql =
            $"SELECT {string.Join(", ", projections)} " +
            $"FROM {Quote(report.Object.Owner)}.{Quote(report.Object.Name)} " +
            "WHERE ROWNUM <= :max_rows";

        await using var command = connection.CreateCommand();
        command.BindByName = true;
        command.CommandText = sql;
        command.CommandTimeout = 15;
        command.Parameters.Add("max_rows", OracleDbType.Int32).Value = maxRows;

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        var columnModels = report.Columns
            .Select(column => new NixfarmaSampleColumn(
                column.Name,
                column.DataType,
                _sanitizer.GetProtection(column.Name, column.DataType)))
            .ToArray();

        var rows = new List<IReadOnlyList<string?>>();

        while (await reader.ReadAsync(cancellationToken))
        {
            var row = new string?[report.Columns.Count];

            for (var i = 0; i < report.Columns.Count; i++)
            {
                var column = report.Columns[i];

                if (SnapshotSanitizer.IsLargeOrBinary(column.DataType))
                {
                    row[i] = "<omitted>";
                    continue;
                }

                var raw = reader.IsDBNull(i)
                    ? null
                    : FormatValue(reader.GetValue(i));

                row[i] = _sanitizer.Protect(
                    report.Object.Owner,
                    report.Object.Name,
                    column,
                    raw);
            }

            rows.Add(row);
        }

        return new NixfarmaObjectSample(
            report.Object,
            maxRows,
            columnModels,
            rows);
    }

    private static string BuildProjection(OracleColumnInfo column)
    {
        if (SnapshotSanitizer.IsLargeOrBinary(column.DataType))
            return $"'[omitted]' AS {Quote(column.Name)}";

        return Quote(column.Name);
    }

    private static string? FormatValue(object? value)
    {
        if (value is null)
            return null;

        return value switch
        {
            DateTime dateTime =>
                dateTime.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
            DateTimeOffset dateTimeOffset =>
                dateTimeOffset.ToString("yyyy-MM-dd HH:mm:ss zzz", CultureInfo.InvariantCulture),
            byte[] bytes =>
                $"<binary:{bytes.Length}>",
            _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? value.ToString()
        };
    }

    private static async Task WriteJsonEntryAsync<T>(
        ZipArchive archive,
        string entryName,
        T value,
        JsonSerializerOptions options,
        CancellationToken cancellationToken)
    {
        var entry = archive.CreateEntry(entryName, CompressionLevel.Optimal);
        await using var stream = entry.Open();
        await JsonSerializer.SerializeAsync(
            stream,
            value,
            options,
            cancellationToken);
    }

    private static async Task WriteTextEntryAsync(
        ZipArchive archive,
        string entryName,
        string content,
        CancellationToken cancellationToken)
    {
        var entry = archive.CreateEntry(entryName, CompressionLevel.Optimal);
        await using var stream = entry.Open();
        await using var writer = new StreamWriter(
            stream,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

        cancellationToken.ThrowIfCancellationRequested();
        await writer.WriteAsync(content);
    }

    private static string BuildReadme(NixfarmaSnapshotManifest manifest) =>
        $"""
        NixToUny - Snapshot técnico de Nixfarma
        ========================================

        Formato: {manifest.FormatVersion}
        Generado (UTC): {manifest.GeneratedAtUtc:O}
        Filas máximas por objeto: {manifest.RowsPerObject}
        Objetos: {manifest.ObjectCount}
        Muestras correctas: {manifest.SuccessfulSamples}
        Muestras con error: {manifest.FailedSamples}

        Contenido:
        - schema.json: esquema completo accesible, columnas, PK/FK y candidatos.
        - manifest.json: índice de todas las muestras y errores.
        - samples/: hasta {manifest.RowsPerObject} filas sanitizadas por tabla/vista.

        El snapshot está diseñado para análisis técnico.
        Los identificadores personales obvios se redactan o pseudonimizan,
        los valores LOB/binarios se omiten y no se incluyen credenciales.

        Revísalo antes de compartirlo fuera del entorno autorizado.
        """;

    private static string CleanError(string message)
    {
        var clean = message.Replace("\r", " ").Replace("\n", " ").Trim();
        return clean.Length <= 500 ? clean : clean[..500] + "…";
    }

    private static string Quote(string identifier) =>
        $"\"{identifier.Replace("\"", "\"\"")}\"";

    private static string SanitizePathPart(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var result = new string(value.Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray());
        return string.IsNullOrWhiteSpace(result) ? "_" : result;
    }
}
