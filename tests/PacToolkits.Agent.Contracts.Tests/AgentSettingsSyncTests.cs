using PacToolkits.Agent.Contracts.Models;
using Xunit;

namespace PacToolkits.Agent.Contracts.Tests;

public sealed class AgentSettingsSyncTests
{
    [Fact]
    public void Round_trips_legacy()
    {
        var legacy = new AgentToolOptions
        {
            PgDriver = "{PostgreSQL ODBC Driver}",
            PgSsl = "require",
        };
        legacy.AppWin["MainWindow"] = 1;

        var settings = AgentSettingsSync.ToSettings(legacy);
        var roundTrip = AgentSettingsSync.FromSettings(settings);

        Assert.True(AgentSettingsSync.HasData(legacy));
        Assert.Equal(legacy.PgDriver, roundTrip.PgDriver);
        Assert.Equal(legacy.PgSsl, roundTrip.PgSsl);
        Assert.Equal(legacy.AppWin, roundTrip.AppWin);
    }

    [Fact]
    public void TryFromSettings_bad_type()
    {
        var settings = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["PgDriver"] = 123,
        };

        var ok = AgentSettingsSync.TryFromSettings(settings, out var options);

        Assert.False(ok);
        Assert.Equal(new AgentToolOptions().PgDriver, options.PgDriver);
    }

    [Fact]
    public void SettingsMatch_bad_type()
    {
        var settings = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["PgDriver"] = 123,
        };

        Assert.False(AgentSettingsSync.SettingsMatch(settings, new AgentToolOptions()));
    }
}
