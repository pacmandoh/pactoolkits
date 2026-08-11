using PacToolkits.Application.Services;
using PacToolkits.Core;

namespace PacToolkits.Desktop.Tests;

public sealed class DbSchemaDesktopTests
{
    [Fact]
    public void Incompatible_guidance_for_above_max()
    {
        var text = DbSchemaDesktop.Incompatible(
            schemaOk: true,
            schemaValue: "1.2.25",
            schemaReason: null,
            min: "1.2.20",
            max: "1.2.22",
            status: DbSchemaCompatibility.AboveMaximum);

        Assert.Contains("请升级 PacToolkits", text, StringComparison.Ordinal);
        Assert.Contains("1.2.25", text, StringComparison.Ordinal);
    }

    [Fact]
    public void GateBlock_below_min()
    {
        var eval = DbSchemaCompatibilityResult.FromRange(
            SemVerRange.Classify("1.2.19", "1.2.20", "1.2.25", allowPrerelease: false));
        var text = DbSchemaDesktop.GateBlock(eval);

        Assert.Contains("过低", text, StringComparison.Ordinal);
        Assert.Contains("1.2.19", text, StringComparison.Ordinal);
        Assert.Contains("1.2.20", text, StringComparison.Ordinal);
    }

    [Fact]
    public void GateBlock_unknown_uses_read_reason_not_from_range()
    {
        var readFail = new DbSchemaCompatibilityResult(
            DbSchemaCompatibility.Unknown,
            string.Empty,
            "1.2.20",
            "1.2.25",
            "schema_version 表不存在");
        Assert.Equal("schema_version 表不存在", DbSchemaDesktop.GateBlock(readFail));

        // FromRange 不把 Core 英文填入 Message
        var fromRange = DbSchemaCompatibilityResult.FromRange(
            SemVerRange.Classify("1.2.20-beta.1", "1.2.20", "1.2.25", allowPrerelease: false));
        Assert.Equal(DbSchemaCompatibility.Unknown, fromRange.Status);
        Assert.True(string.IsNullOrEmpty(fromRange.Message));
        Assert.Equal("无法确定数据库版本兼容范围", DbSchemaDesktop.GateBlock(fromRange));
    }
}
