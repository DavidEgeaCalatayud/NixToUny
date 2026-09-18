using NixToUny.Nixfarma.Schema;
using Xunit;

namespace NixToUny.Tests;

public sealed class SampleQueryBuilderTests
{
    [Fact]
    public void Build_LimitsQueryToTwentyRows()
    {
        var obj = new OracleObjectInfo("NIX", "ARTICULOS", "TABLE");

        var sql = SampleQueryBuilder.Build(obj, 20);

        Assert.Equal(
            "SELECT * FROM \"NIX\".\"ARTICULOS\" WHERE ROWNUM <= 20",
            sql);
    }

    [Fact]
    public void Build_RejectsExcessiveSampleSizes()
    {
        var obj = new OracleObjectInfo("NIX", "ARTICULOS", "TABLE");

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            SampleQueryBuilder.Build(obj, 101));
    }
}
