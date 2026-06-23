using PacToolkits.Desktop.Avalonia.Contracts;
using PacToolkits.Desktop.Avalonia.Services.Application;

namespace PacToolkits.Desktop.Tests;

public sealed class SectionEmptyCopyTests
{
    [Fact]
    public void GetTitle_returns_ready_title_or_default()
    {
        Assert.Equal("期间无使用情况", SectionEmptyCopy.GetTitle(readyTitle: "期间无使用情况"));
        Assert.Equal("暂无数据", SectionEmptyCopy.GetTitle(readyTitle: null));
    }

    [Fact]
    public void GetHint_returns_ready_hint_when_page_ready()
    {
        var hint = SectionEmptyCopy.GetHint(
            PageDataAvailability.Ready,
            readyHint: "筛选无结果");

        Assert.Equal("筛选无结果", hint);
    }

    [Fact]
    public void GetHint_returns_ready_hint_during_loading()
    {
        Assert.Equal(
            "筛选无结果",
            SectionEmptyCopy.GetHint(PageDataAvailability.Loading, readyHint: "筛选无结果"));
    }

    [Fact]
    public void GetHint_returns_blocked_message_with_reason()
    {
        var hint = SectionEmptyCopy.GetHint(
            PageDataAvailability.AccessBlocked,
            readyHint: "筛选无结果",
            blockReason: "数据库版本 1.2.22 低于最低支持版本 1.2.23");

        Assert.Contains("1.2.22", hint, StringComparison.Ordinal);
        Assert.Contains("设置", hint, StringComparison.Ordinal);
    }

    [Fact]
    public void GetHint_returns_stale_message_when_disconnected()
    {
        var hint = SectionEmptyCopy.GetHint(PageDataAvailability.Stale, readyHint: "筛选无结果");

        Assert.Contains("断开", hint, StringComparison.Ordinal);
    }

    [Fact]
    public void GetIcon_uses_neutral_icon_for_ready_state()
    {
        Assert.Equal("Inbox", SectionEmptyCopy.GetIcon(PageDataAvailability.Ready));
    }

    [Fact]
    public void GetIcon_uses_neutral_icon_during_loading()
    {
        Assert.Equal("Inbox", SectionEmptyCopy.GetIcon(PageDataAvailability.Loading));
    }

    [Fact]
    public void GetIcon_uses_shield_for_blocked_state()
    {
        Assert.Equal("ShieldAlert", SectionEmptyCopy.GetIcon(PageDataAvailability.AccessBlocked));
    }
}
