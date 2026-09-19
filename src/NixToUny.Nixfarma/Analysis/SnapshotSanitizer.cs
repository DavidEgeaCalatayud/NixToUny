using System.Security.Cryptography;
using System.Text;
using NixToUny.Nixfarma.Schema;

namespace NixToUny.Nixfarma.Analysis;

/// <summary>
/// Sanitiza muestras antes de sacarlas del equipo de la farmacia.
/// Mantiene estructura y relaciones útiles para análisis, pero evita exportar
/// identificadores directos y datos personales/clínicos obvios.
/// </summary>
public sealed class SnapshotSanitizer
{
    private readonly byte[] _key = RandomNumberGenerator.GetBytes(32);

    private static readonly string[] DirectRedactionTerms =
    [
        "NOMBRE", "APELL", "NIF", "CIF", "DNI", "NIE",
        "DIREC", "DOMIC", "CALLE", "POBLAC", "LOCALIDAD",
        "TELEF", "MOVIL", "EMAIL", "E_MAIL", "MAIL",
        "IBAN", "CUENTA_BANC", "TARJETA_SANIT", "NUMSS",
        "OBSERV", "COMENT", "NOTA", "DIAGN", "PRESCRIP",
        "RECETA", "MEDICO", "DOCTOR", "NACIM", "FECNAC",
        "FECHA_NAC", "SEXO", "MUTUA", "ASEGUR"
    ];

    private static readonly string[] PersonIdentifierTerms =
    [
        "IDCLIENT", "ID_CLIENT", "CODCLIENT", "COD_CLIENT",
        "CODCLI", "CLIENTE", "IDPACIENT", "ID_PACIENT",
        "CODPACIENT", "COD_PACIENT", "CODPAC", "PACIENTE", "TITULAR"
    ];

    private static readonly string[] SensitiveObjectTerms =
    [
        "CLIENT", "PACIENT", "RECETA", "PRESCRIP",
        "DISPENS", "HISTORIA", "CREDITO", "DEUDA"
    ];

    public string GetProtection(
        string objectName,
        OracleColumnInfo column)
    {
        if (IsLargeOrBinary(column.DataType))
            return "omitted";

        if (ContainsAny(column.Name, DirectRedactionTerms))
            return "redacted";

        if (ContainsAny(column.Name, PersonIdentifierTerms))
            return "pseudonymized";

        if (IsSensitiveObject(objectName) && column.IsPrimaryKey)
            return "pseudonymized";

        if (IsSensitiveObject(objectName) &&
            IsDateLike(column.DataType))
        {
            return "redacted";
        }

        return "preserved";
    }

    public string? Protect(
        string owner,
        string objectName,
        OracleColumnInfo column,
        string? value)
    {
        if (value is null)
            return null;

        return GetProtection(objectName, column) switch
        {
            "omitted" => "<omitted>",
            "redacted" => value.Length == 0 ? string.Empty : "<redacted>",
            "pseudonymized" => Tokenize(value),
            _ => Limit(value)
        };
    }

    private string Tokenize(string value)
    {
        using var hmac = new HMACSHA256(_key);
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(value));
        return "anon_" + Convert.ToHexString(hash.AsSpan(0, 8)).ToLowerInvariant();
    }

    private static string Limit(string value)
    {
        var normalized = value.Replace("\r", " ").Replace("\n", " ");
        return normalized.Length <= 300
            ? normalized
            : normalized[..300] + "…";
    }

    public static bool IsLargeOrBinary(string dataType) =>
        dataType.Contains("BLOB", StringComparison.OrdinalIgnoreCase) ||
        dataType.Contains("BFILE", StringComparison.OrdinalIgnoreCase) ||
        dataType.Contains("CLOB", StringComparison.OrdinalIgnoreCase) ||
        dataType.Contains("NCLOB", StringComparison.OrdinalIgnoreCase) ||
        dataType.Contains("LONG", StringComparison.OrdinalIgnoreCase) ||
        dataType.Contains("RAW", StringComparison.OrdinalIgnoreCase);

    private static bool IsDateLike(string dataType) =>
        dataType.Contains("DATE", StringComparison.OrdinalIgnoreCase) ||
        dataType.Contains("TIMESTAMP", StringComparison.OrdinalIgnoreCase);

    private static bool IsSensitiveObject(string objectName) =>
        ContainsAny(objectName, SensitiveObjectTerms);

    private static bool ContainsAny(string value, IEnumerable<string> terms) =>
        terms.Any(term => value.Contains(term, StringComparison.OrdinalIgnoreCase));
}
