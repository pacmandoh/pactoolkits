using PacToolkits.Desktop.Avalonia.Services.Infrastructure.Api;
using PacToolkits.Desktop.Avalonia.Services.Presentation.Connectivity;

namespace PacToolkits.Desktop.Tests;

public sealed class ConnectivityBannerTests
{
    [Fact]
    public void Create_hides_banner_when_ready()
    {
        var banner = ConnectivityBanner.Create(Snap(ApiAvailabilityState.Ready));

        Assert.False(banner.IsVisible);
        Assert.Equal(ConnectivitySeverity.None, banner.Severity);
        Assert.False(banner.ShowOpenSettings);
    }

    [Fact]
    public void Create_hides_banner_when_first_check_incomplete()
    {
        var banner = ConnectivityBanner.Create(new ApiAvailabilitySnapshot(
            ApiAvailabilityState.Connecting,
            Detail: null,
            CheckedAt: DateTimeOffset.UtcNow,
            FirstCheckCompleted: false));

        Assert.False(banner.IsVisible);
        Assert.Equal(ConnectivitySeverity.None, banner.Severity);
    }

    [Fact]
    public void Create_warns_with_settings_when_not_configured()
    {
        var banner = ConnectivityBanner.Create(
            Snap(ApiAvailabilityState.Unavailable, "ignored"),
            isConfigured: false);

        Assert.True(banner.IsVisible);
        Assert.Equal(ConnectivitySeverity.Warning, banner.Severity);
        Assert.True(banner.ShowOpenSettings);
    }

    [Fact]
    public void Create_maps_blocked_and_down_to_error_without_settings()
    {
        Assert.Equal(
            ConnectivitySeverity.Error,
            ConnectivityBanner.Create(Snap(ApiAvailabilityState.ContractBlocked, "协议不兼容")).Severity);
        Assert.Equal(
            ConnectivitySeverity.Error,
            ConnectivityBanner.Create(Snap(ApiAvailabilityState.SchemaBlocked, "结构不兼容")).Severity);
        Assert.Equal(
            ConnectivitySeverity.Error,
            ConnectivityBanner.Create(Snap(ApiAvailabilityState.Unavailable, "不通")).Severity);
        Assert.Equal(
            ConnectivitySeverity.Error,
            ConnectivityBanner.Create(Snap(ApiAvailabilityState.ServerDatabaseBlocked, "库不通")).Severity);

        Assert.False(ConnectivityBanner.Create(Snap(ApiAvailabilityState.ContractBlocked, "x")).ShowOpenSettings);
        Assert.False(ConnectivityBanner.Create(Snap(ApiAvailabilityState.Unavailable, "x")).ShowOpenSettings);
    }

    private static ApiAvailabilitySnapshot Snap(ApiAvailabilityState state, string? detail = null)
        => new(state, detail, DateTimeOffset.UtcNow, FirstCheckCompleted: true);
}
