using System;
using System.IO;
using System.IO.Pipes;
using System.Threading;
using System.Threading.Tasks;

namespace PacToolkits.Desktop.Avalonia;

internal sealed class SingleInstance : IDisposable
{
    private const string MutexName = @"Local\PacToolkits.Desktop.SingleInstance";
    private const string PipeName = "PacToolkits.Desktop.SingleInstance";

    private readonly object _gate = new();
    private readonly Mutex _mutex;
    private readonly CancellationTokenSource _stop = new();
    private Action? _activate;
    private bool _pendingActivation;
    private Task? _listener;
    private bool _disposed;

    private SingleInstance(Mutex mutex)
    {
        _mutex = mutex;
    }

    public static SingleInstance? TryAcquire()
    {
        var mutex = new Mutex(initiallyOwned: true, MutexName, out var createdNew);
        if (createdNew)
        {
            return new SingleInstance(mutex);
        }

        mutex.Dispose();
        return null;
    }

    public static async Task NotifyAsync()
    {
        var timeoutAt = DateTime.UtcNow.AddSeconds(3);
        do
        {
            try
            {
                await using var client = new NamedPipeClientStream(
                    ".",
                    PipeName,
                    PipeDirection.Out,
                    PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await client.ConnectAsync(250).ConfigureAwait(false);
                return;
            }
            catch (TimeoutException)
            {
            }
            catch (IOException)
            {
            }

            await Task.Delay(50).ConfigureAwait(false);
        }
        while (DateTime.UtcNow < timeoutAt);
    }

    public void Listen()
    {
        _listener ??= ListenAsync(_stop.Token);
    }

    public void SetActivationHandler(Action activate)
    {
        ArgumentNullException.ThrowIfNull(activate);

        var invoke = false;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _activate = activate;
            invoke = _pendingActivation;
            _pendingActivation = false;
        }

        if (invoke)
        {
            activate();
        }
    }

    private async Task ListenAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await using var server = new NamedPipeServerStream(
                    PipeName,
                    PipeDirection.In,
                    1,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await server.WaitForConnectionAsync(cancellationToken).ConfigureAwait(false);
                RequestActivation();
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (IOException)
            {
                await Task.Delay(100, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private void RequestActivation()
    {
        Action? activate;
        lock (_gate)
        {
            activate = _activate;
            if (activate is null)
            {
                _pendingActivation = true;
                return;
            }
        }

        activate();
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _activate = null;
        }

        _stop.Cancel();
        try
        {
            _listener?.GetAwaiter().GetResult();
        }
        catch (OperationCanceledException)
        {
        }
        catch (IOException)
        {
        }

        _stop.Dispose();
        _mutex.ReleaseMutex();
        _mutex.Dispose();
    }
}
