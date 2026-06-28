using System.Threading;

namespace PacToolkits.Desktop.Avalonia.Services.Application;

/// <summary>
/// Drops stale in-flight mapping-batch group reloads when filters change or the session closes.
/// </summary>
public sealed class MsfxBatchReloadGate
{
    private int _epoch;

    public int BeginReload() => Interlocked.Increment(ref _epoch);

    public void Invalidate() => Interlocked.Increment(ref _epoch);

    public bool IsCurrent(int epoch) => epoch == Volatile.Read(ref _epoch);
}
