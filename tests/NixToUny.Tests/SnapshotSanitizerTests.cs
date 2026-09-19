using NixToUny.Nixfarma.Analysis;
using NixToUny.Nixfarma.Schema;
using Xunit;

namespace NixToUny.Tests;

public sealed class SnapshotSanitizerTests
{
    [Fact]
    public void RedactsDirectIdentifiers()
    {
        var sanitizer = new SnapshotSanitizer();
        var column = Column("NIF", "VARCHAR2", false);

        var value = sanitizer.Protect("NIX", "CLIENTES", column, "12345678Z");

        Assert.Equal("<redacted>", value);
    }

    [Fact]
    public void PseudonymizesClientIdentifiersConsistently()
    {
        var sanitizer = new SnapshotSanitizer();
        var column = Column("CODCLI", "NUMBER", false);

        var first = sanitizer.Protect("NIX", "VENTAS", column, "42");
        var second = sanitizer.Protect("NIX", "CREDITOS", column, "42");

        Assert.StartsWith("anon_", first);
        Assert.Equal(first, second);
    }

    [Fact]
    public void PseudonymizesGenericPrimaryKeyOfSensitiveObject()
    {
        var sanitizer = new SnapshotSanitizer();
        var column = Column("CODIGO", "NUMBER", true);

        var value = sanitizer.Protect("NIX", "CLIENTES", column, "1001");

        Assert.StartsWith("anon_", value);
    }

    [Fact]
    public void PreservesNonSensitiveTechnicalValue()
    {
        var sanitizer = new SnapshotSanitizer();
        var column = Column("CODNAC", "NUMBER", false);

        var value = sanitizer.Protect("NIX", "ARTICULOS", column, "123456");

        Assert.Equal("123456", value);
    }

    [Fact]
    public void RedactsDatesInsideSensitiveObjects()
    {
        var sanitizer = new SnapshotSanitizer();
        var column = Column("FECHA", "DATE", false);

        var value = sanitizer.Protect("NIX", "PACIENTES", column, "2026-09-19 10:00:00");

        Assert.Equal("<redacted>", value);
    }

    private static OracleColumnInfo Column(
        string name,
        string dataType,
        bool primaryKey) =>
        new(
            "NIX",
            "TEST",
            name,
            dataType,
            100,
            null,
            null,
            true,
            1,
            primaryKey);
}
