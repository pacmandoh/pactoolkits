using PacToolkits.Application.Abstractions;
using PacToolkits.Desktop.Avalonia.Services.Application;

namespace PacToolkits.Desktop.Tests;

public sealed class ConnectivityBannerFactoryTests
{
    [Fact]
    public void Create_hides_banner_when_connected_and_unblocked()
    {
        var guard = new StubAccessGuard(isBlocked: false);

        var banner = ConnectivityBannerFactory.Create(
            isDbConnected: true,
            isConnectivityKnown: true,
            accessGuard: guard);

        Assert.False(banner.IsVisible);
        Assert.Equal(ConnectivitySeverity.None, banner.Severity);
    }

    [Fact]
    public void Create_hides_banner_when_disconnected_but_connectivity_unknown()
    {
        var guard = new StubAccessGuard(isBlocked: false);

        var banner = ConnectivityBannerFactory.Create(
            isDbConnected: false,
            isConnectivityKnown: false,
            accessGuard: guard);

        Assert.False(banner.IsVisible);
        Assert.Equal(ConnectivitySeverity.None, banner.Severity);
    }

    [Fact]
    public void Create_shows_error_banner_when_access_blocked()
    {
        var guard = new StubAccessGuard(isBlocked: true, blockReason: "数据库版本 1.2.22 低于最低支持版本 1.2.23");

        var banner = ConnectivityBannerFactory.Create(
            isDbConnected: true,
            isConnectivityKnown: true,
            accessGuard: guard);

        Assert.True(banner.IsVisible);
        Assert.Equal(ConnectivitySeverity.Error, banner.Severity);
        Assert.True(banner.ShowOpenSettings);
        Assert.Contains("1.2.22", banner.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Create_shows_warning_banner_when_disconnected_and_known()
    {
        var guard = new StubAccessGuard(isBlocked: false);

        var banner = ConnectivityBannerFactory.Create(
            isDbConnected: false,
            isConnectivityKnown: true,
            accessGuard: guard);

        Assert.True(banner.IsVisible);
        Assert.Equal(ConnectivitySeverity.Warning, banner.Severity);
        Assert.True(banner.ShowOpenSettings);
    }

    [Fact]
    public void Create_prefers_access_blocked_over_disconnected()
    {
        var guard = new StubAccessGuard(isBlocked: true, blockReason: "blocked");

        var banner = ConnectivityBannerFactory.Create(
            isDbConnected: false,
            isConnectivityKnown: true,
            accessGuard: guard);

        Assert.Equal(ConnectivitySeverity.Error, banner.Severity);
        Assert.Equal("数据库不可用", banner.Title);
    }

    private sealed class StubAccessGuard(bool isBlocked, string? blockReason = null) : IDbAccessGuard
    {
        public bool IsBlocked { get; } = isBlocked;
        public string? BlockReason { get; } = blockReason;

        public void Block(string reason) => throw new NotSupportedException();
        public void Clear() => throw new NotSupportedException();
        public void ThrowIfBlocked() => throw new NotSupportedException();
    }
}
