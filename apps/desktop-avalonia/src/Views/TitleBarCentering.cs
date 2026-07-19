using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;

namespace PacToolkits.Desktop.Avalonia.Views;

/// <summary>
/// Keeps the title-path anchor horizontally centered inside the title-bar surface by adjusting
/// the host content's layout margin.
/// Prefer margin over a render-time translate transform: render transforms skip layout rounding
/// and land on fractional device pixels, which blurs title-bar text/icons.
/// </summary>
internal sealed class TitleBarCentering : IDisposable
{
    private readonly Control _content;
    private readonly Control _anchor;
    private readonly Control _surface;
    private Thickness _baseMargin;
    private bool _capturedBaseMargin;
    private bool _updating;

    public TitleBarCentering(Control content, Control anchor, Control surface)
    {
        _content = content;
        _anchor = anchor;
        _surface = surface;
        _content.PropertyChanged += OnTrackedPropertyChanged;
        _anchor.PropertyChanged += OnTrackedPropertyChanged;
        _surface.PropertyChanged += OnTrackedPropertyChanged;
        Update();
    }

    public void Dispose()
    {
        _content.PropertyChanged -= OnTrackedPropertyChanged;
        _anchor.PropertyChanged -= OnTrackedPropertyChanged;
        _surface.PropertyChanged -= OnTrackedPropertyChanged;
        if (_capturedBaseMargin)
        {
            _content.Margin = _baseMargin;
        }
    }

    private void OnTrackedPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (_updating)
        {
            return;
        }

        if (e.Property == Visual.BoundsProperty || e.Property == Layoutable.MarginProperty)
        {
            Update();
        }
    }

    private void Update()
    {
        if (_anchor.Bounds.Width <= 0 || _surface.Bounds.Width <= 0)
        {
            return;
        }

        if (!_capturedBaseMargin)
        {
            _baseMargin = _content.Margin;
            _capturedBaseMargin = true;
        }

        var transform = _anchor.TransformToVisual(_surface);
        if (transform is null)
        {
            return;
        }

        var currentOffset = _content.Margin.Left - _baseMargin.Left;
        var renderedCenter = transform.Value.Transform(new Point(_anchor.Bounds.Width / 2, 0)).X;
        var arrangedCenter = renderedCenter - currentOffset;
        var rawOffset = (_surface.Bounds.Width / 2) - arrangedCenter;
        var renderScaling = TopLevel.GetTopLevel(_surface)?.RenderScaling ?? 1;
        // Keep the offset on the physical pixel grid even before layout rounding runs.
        var nextOffset = Math.Round(rawOffset * renderScaling) / renderScaling;
        if (Math.Abs(currentOffset - nextOffset) * renderScaling < 0.5)
        {
            return;
        }

        _updating = true;
        try
        {
            _content.Margin = new Thickness(
                _baseMargin.Left + nextOffset,
                _baseMargin.Top,
                _baseMargin.Right,
                _baseMargin.Bottom);
        }
        finally
        {
            _updating = false;
        }
    }
}
