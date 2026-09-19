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
        "RECETA", "MEDICO", "DOCTOR"
    ];

    private static readonly string[] PersonIdentifierTerms =
    [
        "IDCLIENT", "ID_CLIENT", "CODCLIENT", "COD_CLIENT",
        "CODCLI", "CLIENTE", "IDPACIENT", "ID_PACIENT",
        "CODPACIENT", "COD_PACIENT", "PACIENTE", "TITULAR"
    ];

    public string GetProtection(string columnName, string dataType)
    {
        if (IsLargeOrBinary(dataType))
            return "omitted";

        if (ContainsAny(columnName, DirectRedactionTerms))
            return "redacted";

        if (ContainsAny(columnName, PersonIdentifierTerms))
            return "pseudonymized";

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

        return GetProtection(column.Name, column.DataType) switch
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

    private static bool ContainsAny(string value, IEnumerable<string> terms) =>
        terms.Any(term => value.Contains(term, StringComparison.OrdinalIgnoreCase));
}
