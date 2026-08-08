using System;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using PacToolkits.Application.Services;

namespace PacToolkits.Desktop.Avalonia.Services.Presentation;

/// <summary>
/// 主窗前台用户输入续期敏感解锁空闲；后台或无输入则走会话空闲超时
/// </summary>
public sealed class UnlockActivity : IDisposable
{
    private static readonly TimeSpan ActivityThrottle = TimeSpan.FromSeconds(1);

    private readonly SensitiveUnlockSession _session;
    private Window? _window;
    private bool _mainForeground;
    private DateTimeOffset _lastNotedUtc = DateTimeOffset.MinValue;
    private bool _disposed;

    public UnlockActivity(SensitiveUnlockSession session)
    {
        _session = session;
    }

    public void Attach(Window window)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_window is not null)
        {
            throw new InvalidOperationException("UnlockActivity is already attached");
        }

        _window = window;
        _mainForeground = window.IsActive;
        window.Activated += OnActivated;
        window.Deactivated += OnDeactivated;
        window.AddHandler(InputElement.PointerPressedEvent, OnInput, RoutingStrategies.Tunnel);
        window.AddHandler(InputElement.PointerMovedEvent, OnInput, RoutingStrategies.Tunnel);
        window.AddHandler(InputElement.PointerWheelChangedEvent, OnInput, RoutingStrategies.Tunnel);
        window.AddHandler(InputElement.KeyDownEvent, OnInput, RoutingStrategies.Tunnel);
        window.AddHandler(InputElement.TextInputEvent, OnInput, RoutingStrategies.Tunnel);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_window is null)
        {
            return;
        }

        _window.Activated -= OnActivated;
        _window.Deactivated -= OnDeactivated;
        _window.RemoveHandler(InputElement.PointerPressedEvent, OnInput);
        _window.RemoveHandler(InputElement.PointerMovedEvent, OnInput);
        _window.RemoveHandler(InputElement.PointerWheelChangedEvent, OnInput);
        _window.RemoveHandler(InputElement.KeyDownEvent, OnInput);
        _window.RemoveHandler(InputElement.TextInputEvent, OnInput);
        _window = null;
    }

    private void OnActivated(object? sender, EventArgs e)
    {
        _mainForeground = true;
        Note();
    }

    private void OnDeactivated(object? sender, EventArgs e)
        => _mainForeground = false;

    private void OnInput(object? sender, RoutedEventArgs e)
    {
        if (_disposed || !_mainForeground)
        {
            return;
        }

        Note();
    }

    private void Note()
    {
        var now = DateTimeOffset.UtcNow;
        if (now - _lastNotedUtc < ActivityThrottle)
        {
            return;
        }

        _lastNotedUtc = now;
        _session.NoteActivity(now);
    }
}
