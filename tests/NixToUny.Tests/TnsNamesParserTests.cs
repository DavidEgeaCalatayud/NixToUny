using NixToUny.Nixfarma.Detection;
using Xunit;

namespace NixToUny.Tests;

public sealed class TnsNamesParserTests
{
    [Fact]
    public void Parse_ReadsMultilineNixfarmaDescriptor()
    {
        const string content = """
            # comentario
            NIXFARMA =
              (DESCRIPTION =
                (ADDRESS = (PROTOCOL = TCP)(HOST = 192.168.1.50)(PORT = 1521))
                (CONNECT_DATA = (SERVICE_NAME = NIXFARMA))
              )
            """;

        var entries = TnsNamesParser.Parse(content);

        Assert.True(entries.ContainsKey("NIXFARMA"));
        Assert.Contains("192.168.1.50", entries["NIXFARMA"]);
    }

    [Fact]
    public void Detector_PrefersNixfarmaAlias()
    {
        var entries = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["ZZZ"] = "(DESCRIPTION=(HOST=10.0.0.2)(SERVICE_NAME=OTHER))",
            ["NIXFARMA"] = "(DESCRIPTION=(HOST=10.0.0.1)(SERVICE_NAME=NIX))"
        };

        var found = NixfarmaDetector.TryPickBestEntry(entries, out var alias, out _);

        Assert.True(found);
        Assert.Equal("NIXFARMA", alias);
    }

    [Fact]
    public void Extract_UsesSidAsServiceNameFallback()
    {
        const string descriptor =
            "(DESCRIPTION=(ADDRESS=(PROTOCOL=TCP)(HOST=db.local)(PORT=1522))(CONNECT_DATA=(SID=NIX)))";

        var found = NixfarmaDetector.TryExtractConnectionData(
            descriptor,
            out var host,
            out var port,
            out var service);

        Assert.True(found);
        Assert.Equal("db.local", host);
        Assert.Equal(1522, port);
        Assert.Equal("NIX", service);
    }
}
