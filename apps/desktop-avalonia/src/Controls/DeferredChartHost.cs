using System;
using Avalonia;
using Avalonia.Controls;

namespace PacToolkits.Desktop.Avalonia.Controls;

/// <summary>Creates chart content only after the host is visible and has completed layout.</summary>
public sealed class DeferredChartHost : ContentControl
{
    private bool _wasActive;

    public static readonly StyledProperty<bool> IsActiveProperty =
        AvaloniaProperty.Register<DeferredChartHost, bool>(nameof(IsActive));

    public Func<Control?>? Factory { get; set; }

    static DeferredChartHost()
    {
        IsActiveProperty.Changed.AddClassHandler<DeferredChartHost>((host, _) => host.OnActivationChanged());
        IsVisibleProperty.Changed.AddClassHandler<DeferredChartHost>((host, _) => host.OnActivationChanged());
    }

    public DeferredChartHost()
    {
        LayoutUpdated += OnLayoutUpdated;
    }

    public bool IsActive
    {
        get => GetValue(IsActiveProperty);
        set => SetValue(IsActiveProperty, value);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        WatchLayout();
        TryLoad();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        _wasActive = false;
        WatchLayout();
        TryLoad();
    }

    private void OnActivationChanged()
    {
        if (!IsActive || !IsVisible)
        {
            _wasActive = false;
            return;
        }

        WatchLayout();
        TryLoad();
    }

    private void TryLoad()
    {
        if (_wasActive
            || !IsActive
            || !IsVisible
            || VisualRoot is null
            || Bounds.Width <= 0
            || Bounds.Height <= 0)
        {
            return;
        }

        var content = Factory?.Invoke();
        if (content is null)
        {
            return;
        }

        Content = content;
        _wasActive = true;
        LayoutUpdated -= OnLayoutUpdated;
    }

    private void WatchLayout()
    {
        LayoutUpdated -= OnLayoutUpdated;
        LayoutUpdated += OnLayoutUpdated;
    }

    private void OnLayoutUpdated(object? sender, EventArgs e) => TryLoad();
}
