using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace NixToUny.Nixfarma.Detection;

/// <summary>
/// Detecta una instalación/conexión de Nixfarma a partir de tnsnames.ora.
/// La estrategia está basada en el comportamiento observado en fSync:
/// TNS_ADMIN -> ORACLE_HOME -> registro Oracle -> rutas comunes.
/// </summary>
public sealed partial class NixfarmaDetector
{
    private const int DefaultOraclePort = 1521;

    public NixfarmaDetectionResult? Detect()
    {
        foreach (var tnsFile in FindTnsFiles())
        {
            try
            {
                var content = File.ReadAllText(tnsFile, Encoding.Default);
                var entries = TnsNamesParser.Parse(content);

                if (!TryPickBestEntry(entries, out var alias, out var descriptor))
                    continue;

                if (!TryExtractConnectionData(descriptor, out var host, out var port, out var service))
                    continue;

                return new NixfarmaDetectionResult(
                    tnsFile,
                    alias,
                    host,
                    port,
                    service,
                    descriptor);
            }
            catch (IOException)
            {
                // Probamos el siguiente candidato.
            }
            catch (UnauthorizedAccessException)
            {
                // Probamos el siguiente candidato.
            }
        }

        return null;
    }

    public IReadOnlyList<string> FindTnsFiles()
    {
        var files = new List<string>();

        foreach (var folder in GetCandidateFolders())
        {
            var path = Path.Combine(folder, "tnsnames.ora");
            if (File.Exists(path))
                files.Add(path);
        }

        return files;
    }

    public IEnumerable<string> GetCandidateFolders()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        static string Normalize(string path)
        {
            try { return Path.GetFullPath(path.Trim()); }
            catch { return path.Trim(); }
        }

        IEnumerable<string> YieldIfValid(string? path)
        {
            if (string.IsNullOrWhiteSpace(path))
                yield break;

            var normalized = Normalize(path);
            if (Directory.Exists(normalized) && seen.Add(normalized))
                yield return normalized;
        }

        foreach (var p in YieldIfValid(Environment.GetEnvironmentVariable("TNS_ADMIN")))
            yield return p;

        var oracleHome = Environment.GetEnvironmentVariable("ORACLE_HOME");
        if (!string.IsNullOrWhiteSpace(oracleHome))
        {
            foreach (var p in YieldIfValid(Path.Combine(oracleHome, "network", "admin")))
                yield return p;
        }

        foreach (var registryPath in GetOracleHomesFromRegistry())
        {
            foreach (var p in YieldIfValid(registryPath))
                yield return p;
        }

        var common = new[]
        {
            @"C:\oracle\network\admin",
            @"C:\oracle\product\11.2.0\client_1\network\admin",
            @"C:\oracle\product\12.2.0\client_1\network\admin",
            @"C:\oracle\product\19.0.0\client_1\network\admin",
            @"C:\instantclient_19_8\network\admin",
            @"C:\instantclient_19_9\network\admin",
            @"C:\instantclient_21_6\network\admin",
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "Oracle", "network", "admin")
        };

        foreach (var candidate in common)
        {
            foreach (var p in YieldIfValid(candidate))
                yield return p;
        }
    }

    private static IEnumerable<string> GetOracleHomesFromRegistry()
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var views = new[] { RegistryView.Registry64, RegistryView.Registry32 };
        var roots = new[] { @"SOFTWARE\ORACLE", @"SOFTWARE\WOW6432Node\ORACLE" };

        foreach (var view in views)
        {
            foreach (var root in roots)
            {
                try
                {
                    using var hklm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view);
                    using var oracle = hklm.OpenSubKey(root);
                    if (oracle is null)
                        continue;

                    // Algunas instalaciones guardan ORACLE_HOME directamente en la raíz.
                    AddOracleHome(oracle.GetValue("ORACLE_HOME") as string, result);

                    foreach (var subKeyName in oracle.GetSubKeyNames())
                    {
                        if (!subKeyName.StartsWith("KEY_", StringComparison.OrdinalIgnoreCase))
                            continue;

                        using var key = oracle.OpenSubKey(subKeyName);
                        AddOracleHome(key?.GetValue("ORACLE_HOME") as string, result);
                    }
                }
                catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or System.Security.SecurityException)
                {
                    // El registro es una ayuda para autodetección; un fallo aquí no debe impedir continuar.
                }
            }
        }

        return result;
    }

    private static void AddOracleHome(string? oracleHome, ISet<string> result)
    {
        if (string.IsNullOrWhiteSpace(oracleHome))
            return;

        result.Add(Path.Combine(oracleHome.Trim(), "network", "admin"));
    }

    public static bool TryPickBestEntry(
        IReadOnlyDictionary<string, string> entries,
        out string alias,
        out string descriptor)
    {
        alias = string.Empty;
        descriptor = string.Empty;

        var candidates = entries
            .Where(x => !string.IsNullOrWhiteSpace(x.Key))
            .Where(x => !x.Key.StartsWith("EXTPROC", StringComparison.OrdinalIgnoreCase))
            .Where(x => !x.Key.StartsWith("HS_", StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (candidates.Count == 0)
            return false;

        var preferred = candidates.FirstOrDefault(x =>
            string.Equals(x.Key, "NIXFARMA", StringComparison.OrdinalIgnoreCase));

        var selected = !string.IsNullOrEmpty(preferred.Key)
            ? preferred
            : candidates.OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase).First();

        alias = selected.Key;
        descriptor = selected.Value;
        return true;
    }

    public static bool TryExtractConnectionData(
        string descriptor,
        out string host,
        out int port,
        out string serviceName)
    {
        host = FindValue(descriptor, "HOST") ?? string.Empty;
        var portText = FindValue(descriptor, "PORT");
        serviceName = FindValue(descriptor, "SERVICE_NAME")
                      ?? FindValue(descriptor, "SID")
                      ?? string.Empty;

        port = int.TryParse(portText, out var parsedPort) && parsedPort is > 0 and <= 65535
            ? parsedPort
            : DefaultOraclePort;

        return !string.IsNullOrWhiteSpace(host) &&
               !string.IsNullOrWhiteSpace(serviceName);
    }

    private static string? FindValue(string descriptor, string key)
    {
        if (string.IsNullOrWhiteSpace(descriptor))
            return null;

        var match = DescriptorValueRegex().Match(descriptor, 0);
        while (match.Success)
        {
            if (string.Equals(match.Groups["key"].Value, key, StringComparison.OrdinalIgnoreCase))
                return match.Groups["value"].Value.Trim();

            match = match.NextMatch();
        }

        return null;
    }

    [GeneratedRegex(@"\(\s*(?<key>[A-Za-z0-9_]+)\s*=\s*(?<value>[^\(\)]+?)\s*\)", RegexOptions.Compiled)]
    private static partial Regex DescriptorValueRegex();
}
