using System.Text;

namespace NixToUny.Nixfarma.Detection;

/// <summary>
/// Parser deliberadamente pequeño para tnsnames.ora.
/// Conserva el descriptor completo de cada alias y soporta descriptores multilínea.
/// </summary>
public static class TnsNamesParser
{
    public static IReadOnlyDictionary<string, string> Parse(string content)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        if (string.IsNullOrWhiteSpace(content))
            return result;

        var cleaned = RemoveCommentAndBlankLines(content);
        var index = 0;

        while (index < cleaned.Length)
        {
            SkipWhitespace(cleaned, ref index);
            if (index >= cleaned.Length)
                break;

            var equalsIndex = FindNextEquals(cleaned, index);
            if (equalsIndex < 0)
                break;

            var aliasSection = cleaned[index..equalsIndex].Trim();
            if (!IsPlausibleAliasSection(aliasSection))
            {
                index = equalsIndex + 1;
                continue;
            }

            var descriptorStart = equalsIndex + 1;
            SkipWhitespace(cleaned, ref descriptorStart);

            // Entradas IFILE u otras directivas no son descriptores Oracle.
            if (descriptorStart >= cleaned.Length || cleaned[descriptorStart] != '(')
            {
                index = MoveToNextLine(cleaned, descriptorStart);
                continue;
            }

            var descriptorEnd = FindBalancedDescriptorEnd(cleaned, descriptorStart);
            if (descriptorEnd < 0)
                break;

            var descriptor = cleaned[descriptorStart..(descriptorEnd + 1)].Trim();

            foreach (var alias in aliasSection
                         .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (IsValidAlias(alias))
                    result[alias] = descriptor;
            }

            index = descriptorEnd + 1;
        }

        return result;
    }

    private static string RemoveCommentAndBlankLines(string content)
    {
        var sb = new StringBuilder();
        using var reader = new StringReader(content);

        while (reader.ReadLine() is { } line)
        {
            var trimmed = line.Trim();
            if (trimmed.Length == 0 ||
                trimmed.StartsWith('#') ||
                trimmed.StartsWith("//", StringComparison.Ordinal))
            {
                continue;
            }

            sb.AppendLine(line);
        }

        return sb.ToString();
    }

    private static int FindNextEquals(string text, int start)
    {
        for (var i = start; i < text.Length; i++)
        {
            if (text[i] == '=')
                return i;

            // Si encontramos un descriptor antes de un '=', no estamos en una cabecera de alias.
            if (text[i] == '(')
                return -1;
        }

        return -1;
    }

    private static bool IsPlausibleAliasSection(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 512)
            return false;

        return value.All(c =>
            char.IsLetterOrDigit(c) ||
            char.IsWhiteSpace(c) ||
            c is '_' or '-' or '.' or ',' or '$' or '#');
    }

    private static bool IsValidAlias(string alias) =>
        alias.Length > 0 && alias.All(c =>
            char.IsLetterOrDigit(c) || c is '_' or '-' or '.' or '$' or '#');

    private static int FindBalancedDescriptorEnd(string text, int start)
    {
        var depth = 0;
        var inQuote = false;
        char quote = '\0';

        for (var i = start; i < text.Length; i++)
        {
            var c = text[i];

            if ((c == '\'' || c == '"') && (i == 0 || text[i - 1] != '\\'))
            {
                if (!inQuote)
                {
                    inQuote = true;
                    quote = c;
                }
                else if (quote == c)
                {
                    inQuote = false;
                }
            }

            if (inQuote)
                continue;

            if (c == '(')
                depth++;
            else if (c == ')')
            {
                depth--;
                if (depth == 0)
                    return i;
            }
        }

        return -1;
    }

    private static void SkipWhitespace(string text, ref int index)
    {
        while (index < text.Length && char.IsWhiteSpace(text[index]))
            index++;
    }

    private static int MoveToNextLine(string text, int index)
    {
        var nl = text.IndexOf('\n', index);
        return nl < 0 ? text.Length : nl + 1;
    }
}
