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

    [Fact]
    public void Required_range_is_the_intersection_of_component_ranges()
    {
        var requiredMin = DbSchemaCompat.GetRequiredMin("1.2.20", "1.2.22");
        var requiredMax = DbSchemaCompat.GetRequiredMax("1.2.25", "1.2.24");

        Assert.Equal("1.2.22", requiredMin);
        Assert.Equal("1.2.24", requiredMax);
    }
}
