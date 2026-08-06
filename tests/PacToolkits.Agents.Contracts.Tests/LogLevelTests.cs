using PacToolkits.Logger;
using Xunit;

namespace PacToolkits.Agents.Contracts.Tests;

public sealed class LogLevelTests
{
    [Theory]
    [InlineData("debug", "Debug")]
    [InlineData("WARN", "Warn")]
    [InlineData("err", "Error")]
    [InlineData("fatal", "Fatal")]
    public void Canonical_maps_aliases(string raw, string expected)
        => Assert.Equal(expected, LogLevel.Canonical(raw));

    [Fact]
    public void ShouldWrite_respects_minimum()
    {
        Assert.False(LogLevel.ShouldWrite(LogLevel.Info, LogLevel.Error));
        Assert.True(LogLevel.ShouldWrite(LogLevel.Error, LogLevel.Error));
        Assert.True(LogLevel.ShouldWrite(LogLevel.Fatal, LogLevel.Warn));
    }
}
