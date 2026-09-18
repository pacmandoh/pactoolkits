using PacToolkits.Desktop.Avalonia.Services.Infrastructure.Api;
using PacToolkits.Desktop.Avalonia.Services.Presentation.Connectivity;

namespace PacToolkits.Desktop.Tests;

public sealed class ConnectionViewTests
{
    [Fact]
    public void Empty_is_not_configured()
    {
        var view = ConnectionView.From(
            Snap(ApiAvailabilityState.Unavailable),
            PacApiOptionsState.Empty);

        Assert.Equal(ConnectionKind.NotConfigured, view.Kind);
        Assert.False(ConnectionView.IsReady(Snap(ApiAvailabilityState.Ready), PacApiOptionsState.Empty));
    }

    [Fact]
    public void Invalid_is_invalid_with_error_message()
    {
        var view = ConnectionView.From(
            Snap(ApiAvailabilityState.Ready),
            PacApiOptionsState.Invalid,
            "公网须 HTTPS");

        Assert.Equal(ConnectionKind.Invalid, view.Kind);
        Assert.Equal("PacAPI 配置无效", view.Title);
        Assert.Equal("公网须 HTTPS", view.Message);
        Assert.False(ConnectionView.IsReady(Snap(ApiAvailabilityState.Ready), PacApiOptionsState.Invalid));
    }

    [Fact]
    public void First_check_incomplete_is_unknown()
    {
        var uncheckedSnap = new ApiAvailabilitySnapshot(
            ApiAvailabilityState.Connecting,
            Detail: null,
            CheckedAt: DateTimeOffset.UtcNow,
            FirstCheckCompleted: false);

        Assert.Equal(ConnectionKind.Unknown, ConnectionView.From(uncheckedSnap).Kind);
        Assert.False(ConnectionView.IsReady(new ApiAvailabilitySnapshot(
            ApiAvailabilityState.Ready,
            Detail: null,
            CheckedAt: DateTimeOffset.UtcNow,
            FirstCheckCompleted: false)));
    }

    [Fact]
    public void Ready_is_up()
        => Assert.Equal(ConnectionKind.Up, ConnectionView.From(Snap(ApiAvailabilityState.Ready)).Kind);

    [Fact]
    public void Unavailable_and_server_database_are_down()
    {
        Assert.Equal(ConnectionKind.Down, ConnectionView.From(Snap(ApiAvailabilityState.Unavailable)).Kind);
        Assert.Equal(ConnectionKind.Down, ConnectionView.From(Snap(ApiAvailabilityState.ServerDatabaseBlocked)).Kind);
    }

    [Fact]
    public void Contract_and_schema_are_blocked()
    {
        Assert.Equal(ConnectionKind.Blocked, ConnectionView.From(Snap(ApiAvailabilityState.ContractBlocked)).Kind);
        Assert.Equal(ConnectionKind.Blocked, ConnectionView.From(Snap(ApiAvailabilityState.SchemaBlocked)).Kind);
        Assert.True(ConnectionView.IsBlocked(Snap(ApiAvailabilityState.SchemaBlocked)));
        Assert.False(ConnectionView.IsBlocked(Snap(ApiAvailabilityState.ServerDatabaseBlocked)));
    }

    private static ApiAvailabilitySnapshot Snap(ApiAvailabilityState state, string? detail = null)
        => new(state, detail, DateTimeOffset.UtcNow, FirstCheckCompleted: true);
}
