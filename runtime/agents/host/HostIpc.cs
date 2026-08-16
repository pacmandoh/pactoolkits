using System.Collections.Concurrent;
using System.IO.Pipes;
using PacToolkits.Agents.Contracts.Agents;

namespace PacToolkits.Agents.Host;

/// <summary>
/// Host 侧命名管道服务：desired / quit / status 推送
/// </summary>
internal sealed class HostIpc : IDisposable
{
    private static readonly TimeSpan WriteTimeout = TimeSpan.FromSeconds(2);

    private readonly string _agentsDir;
    private readonly string _pipeName;
    private readonly CancellationTokenSource _cts = new();
    private readonly ConcurrentQueue<Action> _actions = new();
    private readonly object _desiredGate = new();
    private readonly object _writeGate = new();
    private readonly object _queueGate = new();
    private readonly SemaphoreSlim _sendSignal = new(0, 1);
    private readonly HashSet<string> _failedAnnounced = new(StringComparer.Ordinal);
    private readonly HashSet<string> _desired;
    private Stream? _client;
    private Task? _acceptLoop;
    private Task? _sendLoop;
    private volatile bool _quitRequested;
    private AgentsStatus? _lastStatus;
    private AgentsStatus? _lastPushedStatus;
    private AgentsStatus? _queuedStatus;

    public HostIpc(string agentsDir)
    {
        _agentsDir = agentsDir;
        _pipeName = AgentsIpc.PipeName(agentsDir);
        _desired = HostDesired.ReadMountIds(agentsDir);
    }

    public bool QuitRequested => _quitRequested;

    public void Start()
    {
        _sendLoop = Task.Run(() => SendLoopAsync(_cts.Token));
        _acceptLoop = Task.Run(() => AcceptLoopAsync(_cts.Token));
        HostLog.Info("host.ipc.ready", "IPC pipe listening", new { pipe = _pipeName });
    }

    public void DrainActions()
    {
        while (_actions.TryDequeue(out var action))
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                HostLog.Error("host.ipc.action_fail", ex.Message);
            }
        }
    }

    public HashSet<string> ReadDesiredIds()
    {
        lock (_desiredGate)
        {
            return new HashSet<string>(_desired, StringComparer.Ordinal);
        }
    }

    public void PushStatus(AgentsStatus status)
    {
        _lastStatus = status;
        if (AgentsStatus.ContentEquals(_lastPushedStatus, status))
        {
            NoteFailedModules(status, push: false);
            return;
        }

        bool hasClient;
        lock (_writeGate)
        {
            hasClient = _client is not null;
        }

        if (!hasClient)
        {
            NoteFailedModules(status, push: false);
            _lastPushedStatus = status;
            return;
        }

        lock (_queueGate)
        {
            _queuedStatus = status;
        }

        try
        {
            _sendSignal.Release();
        }
        catch (SemaphoreFullException)
        {
        }
    }

    private async Task SendLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await _sendSignal.WaitAsync(ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            AgentsStatus? next;
            lock (_queueGate)
            {
                next = _queuedStatus;
                _queuedStatus = null;
            }

            if (next is null || AgentsStatus.ContentEquals(_lastPushedStatus, next))
            {
                continue;
            }

            if (!WriteStatus(next, ct))
            {
                continue;
            }

            _lastPushedStatus = next;
        }
    }

    private bool WriteStatus(AgentsStatus status, CancellationToken ct)
    {
        Stream? client;
        lock (_writeGate)
        {
            client = _client;
            if (client is null)
            {
                NoteFailedModules(status, push: false);
                return false;
            }
        }

        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(WriteTimeout);
            lock (_writeGate)
            {
                if (!ReferenceEquals(client, _client))
                {
                    return false;
                }

                AgentsIpcStream.WriteAsync(client, AgentsIpc.StatusEvent(status), timeout.Token)
                    .GetAwaiter()
                    .GetResult();
                foreach (var failed in NoteFailedModules(status, push: true))
                {
                    AgentsIpcStream.WriteAsync(
                            client,
                            AgentsIpc.ModuleFailedEvent(failed.Id, failed.LastError, status),
                            timeout.Token)
                        .GetAwaiter()
                        .GetResult();
                }
            }

            return true;
        }
        catch
        {
            CloseClient();
            return false;
        }
    }

    private List<AgentsStatusModule> NoteFailedModules(AgentsStatus status, bool push)
    {
        lock (_failedAnnounced)
        {
            var newly = new List<AgentsStatusModule>();
            var stillFailed = new HashSet<string>(StringComparer.Ordinal);
            foreach (var module in status.Modules)
            {
                if (module.State != AgentsRunState.Failed || string.IsNullOrWhiteSpace(module.Id))
                {
                    continue;
                }

                stillFailed.Add(module.Id);
                if (_failedAnnounced.Add(module.Id) && push)
                {
                    newly.Add(module);
                }
            }

            _failedAnnounced.RemoveWhere(id => !stillFailed.Contains(id));
            return newly;
        }
    }

    public void Dispose()
    {
        _cts.Cancel();
        try
        {
            _sendSignal.Release();
        }
        catch
        {
        }

        try
        {
            _sendLoop?.Wait(TimeSpan.FromSeconds(3));
        }
        catch
        {
        }

        try
        {
            _acceptLoop?.Wait(TimeSpan.FromSeconds(3));
        }
        catch
        {
        }

        CloseClient();
        _cts.Dispose();
        _sendSignal.Dispose();
    }

    private async Task AcceptLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            NamedPipeServerStream? server = null;
            try
            {
                server = CreateServer();
                await server.WaitForConnectionAsync(ct).ConfigureAwait(false);
                HostLog.Info("host.ipc.connected", "Desktop attached", new { pipe = _pipeName });
                lock (_writeGate)
                {
                    _client?.Dispose();
                    _client = server;
                    server = null;
                }

                Stream session;
                lock (_writeGate)
                {
                    session = _client!;
                }

                await SessionAsync(session, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                if (!ct.IsCancellationRequested)
                {
                    HostLog.Error("host.ipc.session_fail", ex.Message);
                }
            }
            finally
            {
                server?.Dispose();
                CloseClient();
            }

            if (ct.IsCancellationRequested)
            {
                break;
            }

            try
            {
                await Task.Delay(50, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private async Task SessionAsync(Stream stream, CancellationToken ct)
    {
        var buffer = new AgentsIpcReadBuffer();
        if (_lastStatus is not null)
        {
            if (!WriteLocked(stream, AgentsIpc.StatusEvent(_lastStatus)))
            {
                return;
            }
        }

        while (!ct.IsCancellationRequested)
        {
            AgentsIpcMessage? message;
            try
            {
                message = await AgentsIpcStream.ReadAsync(stream, buffer, ct).ConfigureAwait(false);
            }
            catch
            {
                return;
            }

            if (message is null)
            {
                return;
            }

            Handle(stream, message);
        }
    }

    private void Handle(Stream stream, AgentsIpcMessage message)
    {
        try
        {
            switch (message.Op)
            {
                case AgentsIpcOps.Ping:
                    WriteLocked(stream, AgentsIpc.Pong(message.Id));
                    break;

                case AgentsIpcOps.GetStatus:
                    if (_lastStatus is not null)
                    {
                        WriteLocked(stream, AgentsIpc.StatusEvent(_lastStatus, message.Id));
                    }
                    else
                    {
                        WriteLocked(stream, AgentsIpc.Ok(message.Id));
                    }

                    break;

                case AgentsIpcOps.Desired:
                    {
                        var modules = message.Modules ?? [];
                        lock (_desiredGate)
                        {
                            _desired.Clear();
                            foreach (var id in modules)
                            {
                                _desired.Add(id);
                            }
                        }

                        var agentsDir = _agentsDir;
                        _actions.Enqueue(() => AgentsDesired.Write(agentsDir, modules));
                        WriteLocked(stream, AgentsIpc.Ok(message.Id));
                        break;
                    }

                case AgentsIpcOps.Quit:
                    _quitRequested = true;
                    WriteLocked(stream, AgentsIpc.Ok(message.Id));
                    break;

                default:
                    WriteLocked(stream, AgentsIpc.Error("unknown op", message.Id));
                    break;
            }
        }
        catch (Exception ex)
        {
            HostLog.Error("host.ipc.handle_fail", ex.Message, new { message.Op });
            WriteLocked(stream, AgentsIpc.Error(ex.Message, message.Id));
        }
    }

    private bool WriteLocked(Stream stream, AgentsIpcMessage message)
    {
        lock (_writeGate)
        {
            if (!ReferenceEquals(stream, _client))
            {
                return false;
            }

            try
            {
                using var timeout = new CancellationTokenSource(WriteTimeout);
                AgentsIpcStream.WriteAsync(stream, message, timeout.Token)
                    .GetAwaiter()
                    .GetResult();
                return true;
            }
            catch
            {
                try
                {
                    _client?.Dispose();
                }
                catch
                {
                    // ignore
                }

                _client = null;
                return false;
            }
        }
    }

    private NamedPipeServerStream CreateServer()
        => new(
            _pipeName,
            PipeDirection.InOut,
            maxNumberOfServerInstances: 1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous);

    private void CloseClient()
    {
        lock (_writeGate)
        {
            try
            {
                _client?.Dispose();
            }
            catch
            {
                // ignore
            }

            _client = null;
        }
    }
}
