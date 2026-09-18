using NixToUny.Nixfarma.Schema;
using Xunit;

namespace NixToUny.Tests;

public sealed class SchemaSearchScorerTests
{
    [Fact]
    public void Articles_PrefersArticleTableWithRelevantColumns()
    {
        OracleObjectInfo[] objects =
        [
            new("NIX", "ARTICULOS", "TABLE"),
            new("NIX", "USUARIOS", "TABLE")
        ];

        OracleColumnInfo[] columns =
        [
            Column("ARTICULOS", "CODNAC"),
            Column("ARTICULOS", "DESCRIPCION"),
            Column("ARTICULOS", "PVP"),
            Column("USUARIOS", "CODIGO"),
            Column("USUARIOS", "NOMBRE")
        ];

        var results = SchemaSearchScorer.Score(
            NixfarmaDataArea.Articles,
            objects,
            columns);

        Assert.NotEmpty(results);
        Assert.Equal("ARTICULOS", results[0].Object.Name);
        Assert.Contains("CODNAC", results[0].MatchingColumns);
    }

    [Fact]
    public void Credits_RecognizesDebtAndBalanceColumns()
    {
        OracleObjectInfo[] objects =
        [
            new("NIX", "DEUDAS_CLIENTES", "TABLE"),
            new("NIX", "HISTORICO", "TABLE")
        ];

        OracleColumnInfo[] columns =
        [
            Column("DEUDAS_CLIENTES", "CLIENTE"),
            Column("DEUDAS_CLIENTES", "SALDO"),
            Column("DEUDAS_CLIENTES", "IMPORTE"),
            Column("HISTORICO", "FECHA")
        ];

        var results = SchemaSearchScorer.Score(
            NixfarmaDataArea.Credits,
            objects,
            columns);

        Assert.NotEmpty(results);
        Assert.Equal("DEUDAS_CLIENTES", results[0].Object.Name);
        Assert.True(results[0].Score > 0);
    }

    [Fact]
    public void FreeText_SearchesTableAndColumnNames()
    {
        OracleObjectInfo[] objects =
        [
            new("NIX", "MAESTRO", "TABLE"),
            new("NIX", "OTRA", "TABLE")
        ];

        OracleColumnInfo[] columns =
        [
            Column("MAESTRO", "STOCK_MINIMO"),
            Column("OTRA", "DESCRIPCION")
        ];

        var results = SchemaSearchScorer.SearchText(
            "stock",
            objects,
            columns);

        Assert.Single(results);
        Assert.Equal("MAESTRO", results[0].Object.Name);
        Assert.Contains("STOCK_MINIMO", results[0].MatchingColumns);
    }

    private static OracleColumnInfo Column(string table, string name) =>
        new(
            "NIX",
            table,
            name,
            "VARCHAR2",
            100,
            null,
            null,
            true,
            1,
            false);
}
