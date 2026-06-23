using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using PacToolkits.Desktop.Avalonia.Controls;

namespace PacToolkits.Desktop.Avalonia.Common;

/// <summary>
/// Mounts registered <see cref="DeferredGridSlot"/> instances one Dispatcher frame at a time
/// after the host page completes its first layout pass. Pauses while the host is hidden.
/// </summary>
public sealed class PageGridMountScheduler
{
    private readonly Control _host;
    private readonly List<(DeferredGridSlot Slot, int Priority)> _queue = new();
    private readonly object _gate = new();
    private CancellationTokenSource? _cts;
    private bool _disposed;
    private bool _firstLayoutSeen;
    private bool _paused;
    private bool _runnerActive;
    private bool _started;

    public PageGridMountScheduler(Control host)
    {
        _host = host ?? throw new ArgumentNullException(nameof(host));
    }

    public void StartAfterFirstLayout()
    {
        if (_started)
        {
            return;
        }

        _started = true;
        _host.LayoutUpdated += OnHostLayoutUpdated;
        _host.PropertyChanged += OnHostPropertyChanged;
        _paused = !_host.IsVisible;
        if (_host.Bounds.Width > 0 || _host.Bounds.Height > 0)
        {
            TryBeginAfterFirstLayout();
        }
    }

    public void Cancel()
    {
        _disposed = true;
        _host.LayoutUpdated -= OnHostLayoutUpdated;
        _host.PropertyChanged -= OnHostPropertyChanged;
        _paused = true;
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
    }

    public void RequestMount(DeferredGridSlot slot, int priority)
    {
        Register(slot, priority);
        TryStartQueueRunner();
    }

    private void TryStartQueueRunner()
    {
        if (_disposed || !_firstLayoutSeen || !_host.IsVisible)
        {
            return;
        }

        _paused = false;
        _ = RunQueueAsync();
    }

    private void Register(DeferredGridSlot slot, int priority)
    {
        ArgumentNullException.ThrowIfNull(slot);
        if (slot.IsMounted)
        {
            return;
        }

        lock (_gate)
        {
            _queue.RemoveAll(entry => ReferenceEquals(entry.Slot, slot));
            _queue.Add((slot, priority));
        }
    }

    private void OnHostPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property != Visual.IsVisibleProperty)
        {
            return;
        }

        if (_host.IsVisible)
        {
            ResumeQueue();
        }
        else
        {
            PauseQueue();
        }
    }

    private void PauseQueue()
    {
        if (_paused)
        {
            return;
        }

        _paused = true;
        _cts?.Cancel();
    }

    private void ResumeQueue()
    {
        if (_disposed || !_host.IsVisible)
        {
            return;
        }

        _paused = false;
        TryStartQueueRunner();
    }

    private void OnHostLayoutUpdated(object? sender, EventArgs e)
        => TryBeginAfterFirstLayout();

    private void TryBeginAfterFirstLayout()
    {
        if (_firstLayoutSeen || _disposed)
        {
            return;
        }

        _firstLayoutSeen = true;
        _host.LayoutUpdated -= OnHostLayoutUpdated;
        if (_host.IsVisible)
        {
            _paused = false;
            Dispatcher.UIThread.Post(TryStartQueueRunner, DispatcherPriority.Background);
        }
    }

    private async Task RunQueueAsync()
    {
        if (_disposed || _paused || !_host.IsVisible)
        {
            return;
        }

        lock (_gate)
        {
            if (_runnerActive)
            {
                return;
            }

            _runnerActive = true;
        }

        _cts?.Cancel();
        _cts?.Dispose();
        _cts = new CancellationTokenSource();
        var token = _cts.Token;

        try
        {
            await Dispatcher.UIThread.InvokeAsync(static () => { }, DispatcherPriority.Background);

            while (!_disposed && !_paused && _host.IsVisible)
            {
                token.ThrowIfCancellationRequested();

                DeferredGridSlot? next;
                lock (_gate)
                {
                    next = _queue
                        .Where(entry => !entry.Slot.IsMounted)
                        .OrderBy(entry => entry.Priority)
                        .Select(entry => entry.Slot)
                        .FirstOrDefault();
                }

                if (next is null)
                {
                    break;
                }

                await Dispatcher.UIThread.InvokeAsync(() => next.MountGrid(), DispatcherPriority.Background);
                await Dispatcher.UIThread.InvokeAsync(static () => { }, DispatcherPriority.Background);
            }
        }
        catch (OperationCanceledException)
        {
            // Host hidden or page left before all slots mounted.
        }
        finally
        {
            lock (_gate)
            {
                _runnerActive = false;
            }
        }
    }
}
