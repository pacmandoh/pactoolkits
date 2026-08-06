using PacToolkits.Core;

namespace PacToolkits.Desktop.Tests;

public sealed class DbSchemaCompatTests
{
    [Theory]
    [InlineData("1.2.21", DbSchemaCompatibility.BelowMinimum)]
    [InlineData("1.2.22", DbSchemaCompatibility.Compatible)]
    [InlineData("1.2.23", DbSchemaCompatibility.AboveMaximum)]
    public void Evaluate_enforces_closed_compatibility_range(
        string current,
        DbSchemaCompatibility expected)
    {
        var result = DbSchemaCompat.Evaluate(current, "1.2.22", "1.2.22");

        Assert.Equal(expected, result.Status);
    }

    [Fact]
    public void Above_maximum_message_explains_that_upgrade_is_required()
    {
        var result = DbSchemaCompat.Evaluate("1.2.23", "1.2.20", "1.2.22");

        Assert.Contains("数据库版本高于当前程序支持范围", result.Message, StringComparison.Ordinal);
    }
}
