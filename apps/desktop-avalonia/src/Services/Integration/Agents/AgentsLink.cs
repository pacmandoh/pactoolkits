using System;
using System.Collections.Generic;
using System.IO.Pipes;
using System.Threading;
using System.Threading.Tasks;
using PacToolkits.Agents.Contracts.Abstractions;
using PacToolkits.Agents.Contracts.Agents;

namespace PacToolkits.Desktop.Avalonia.Services.Integration.Agents;

/// <summary>
/// Desktop 侧 <see cref="IAgentsClient"/>：管道 Connect / desired / quit / Snapshot
/// </summary>
internal sealed class AgentsLink : IAgentsClient
{
    private readonly object _gate = new();
    private readonly object _writeGate = new();
    private CancellationTokenSource? _cts;
    private Task? _readLoop;
    private NamedPipeClientStream? _pipe;
    private AgentsStatus? _status;
    private DateTimeOffset _statusAt = DateTimeOffset.MinValue;
    private bool _disposed;

    public event Action? SnapshotChanged;

    public event Action<string, string?>? ModuleFailed;

    public bool IsLinkConnected
    {
        get
        {
            lock (_gate)
            {
                return _pipe is { IsConnected: true };
            }
        }
    }

    public AgentsStatus? LastSnapshot
    {
        get
        {
            lock (_gate)
            {
                return _status?.Clone();
            }
        }
    }

    public AgentsStatus? TryGetStatus(TimeSpan maxAge)
    {
        lock (_gate)
        {
            if (_status is null)
            {
                return null;
            }

            if (_pipe is not { IsConnected: true }
                && DateTimeOffset.UtcNow - _statusAt > maxAge)
            {
                return null;
            }

            return _status?.Clone();
        }
    }

    public async Task<bool> ConnectAsync(string agentsDir, TimeSpan timeout, CancellationToken ct = default)
    {
        Disconnect();
        var pipeName = AgentsIpc.PipeName(agentsDir);
        var pipe = new NamedPipeClientStream(
            ".",
            pipeName,
            PipeDirection.InOut,
            PipeOptions.Asynchronous);

        try
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
            linked.CancelAfter(timeout);
            await pipe.ConnectAsync(linked.Token).ConfigureAwait(false);
        }
        catch
        {
            await pipe.DisposeAsync().ConfigureAwait(false);
            return false;
        }

        var cts = new CancellationTokenSource();
        lock (_gate)
        {
            _pipe = pipe;
            _cts = cts;
            _readLoop = Task.Run(() => ReadLoopAsync(pipe, cts.Token));
        }

        return true;
    }

    public void Disconnect()
        => DisposeLink();

    public bool TrySendDesired(IEnumerable<string> modules)
    {
        if (!IsLinkConnected)
        {
            return false;
        }

        return Send(AgentsIpc.Desired(modules, Guid.NewGuid().ToString("N")));
    }

    public bool TrySendQuit()
    {
        if (!IsLinkConnected)
        {
            return false;
        }

        return Send(AgentsIpc.Quit(Guid.NewGuid().ToString("N")));
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        DisposeLink();
    }

    private bool Send(AgentsIpcMessage message)
    {
        NamedPipeClientStream? pipe;
        lock (_gate)
        {
            pipe = _pipe;
            if (pipe is not { IsConnected: true })
            {
                return false;
            }
        }

        try
        {
            lock (_writeGate)
            {
                AgentsIpcStream.WriteAsync(pipe, message, CancellationToken.None)
                    .GetAwaiter()
                    .GetResult();
            }

            return true;
        }
        catch
        {
            DisposeLink();
            return false;
        }
    }

    private async Task ReadLoopAsync(NamedPipeClientStream pipe, CancellationToken ct)
    {
        var buffer = new AgentsIpcReadBuffer();
        try
        {
            while (!ct.IsCancellationRequested && pipe.IsConnected)
            {
                var message = await AgentsIpcStream.ReadAsync(pipe, buffer, ct).ConfigureAwait(false);
                if (message is null)
                {
                    break;
                }

                if (message.Ev == AgentsIpcEvs.Status && message.Status is not null)
                {
                    ApplySnapshot(message.Status);
                }
                else if (message.Ev == AgentsIpcEvs.ModuleFailed
                         && !string.IsNullOrWhiteSpace(message.ModuleId))
                {
                    if (message.Status is not null)
                    {
                        ApplySnapshot(message.Status);
                    }

                    try
                    {
                        ModuleFailed?.Invoke(message.ModuleId, message.Message);
                    }
                    catch
                    {
                        // UI 订阅异常不打断会话
                    }
                }
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // ok
        }
        catch
        {
            // 断开
        }
        finally
        {
            DisposeLink();
        }
    }

    private void ApplySnapshot(AgentsStatus status)
    {
        var changed = false;
        lock (_gate)
        {
            changed = !AgentsStatus.ContentEquals(_status, status);
            _status = status;
            _statusAt = DateTimeOffset.UtcNow;
        }

        if (changed)
        {
            RaiseSnapshot();
        }
    }

    private void RaiseSnapshot()
    {
        try
        {
            SnapshotChanged?.Invoke();
        }
        catch
        {
            // UI 订阅异常不打断会话
        }
    }

    private void DisposeLink()
    {
        CancellationTokenSource? cts;
        Task? loop;
        NamedPipeClientStream? pipe;
        lock (_gate)
        {
            cts = _cts;
            loop = _readLoop;
            pipe = _pipe;
            _cts = null;
            _readLoop = null;
            _pipe = null;
            // 断线保留最后一帧 Snapshot 供 UI 缓存
        }

        try
        {
            cts?.Cancel();
        }
        catch
        {
            // ignore
        }

        try
        {
            pipe?.Dispose();
        }
        catch
        {
            // ignore
        }

        try
        {
            cts?.Dispose();
        }
        catch
        {
            // ignore
        }

        _ = loop;
    }
}
