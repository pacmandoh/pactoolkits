using PacToolkits.Desktop.Avalonia.Services.Application;

namespace PacToolkits.Desktop.Tests;

public sealed class MsfxBatchReloadGateTests
{
    [Fact]
    public void BeginReload_invalidates_previous_epoch()
    {
        var gate = new MsfxBatchReloadGate();

        var first = gate.BeginReload();
        var second = gate.BeginReload();

        Assert.False(gate.IsCurrent(first));
        Assert.True(gate.IsCurrent(second));
    }

    [Fact]
    public void Invalidate_discards_inflight_epoch()
    {
        var gate = new MsfxBatchReloadGate();
        var epoch = gate.BeginReload();

        Assert.True(gate.IsCurrent(epoch));

        gate.Invalidate();

        Assert.False(gate.IsCurrent(epoch));
    }

    [Fact]
    public void Invalidate_after_newer_reload_only_affects_older_epochs()
    {
        var gate = new MsfxBatchReloadGate();
        var stale = gate.BeginReload();
        var fresh = gate.BeginReload();

        gate.Invalidate();

        Assert.False(gate.IsCurrent(stale));
        Assert.False(gate.IsCurrent(fresh));
    }
}
