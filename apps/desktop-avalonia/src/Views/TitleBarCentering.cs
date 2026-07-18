using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace PacToolkits.Desktop.Avalonia.Views;

internal sealed class TitleBarCentering : IDisposable
{
    private readonly Control _content;
    private readonly Control _anchor;
    private readonly Control _surface;
    private readonly TranslateTransform _translation = new();

    public TitleBarCentering(Control content, Control anchor, Control surface)
    {
        _content = content;
        _anchor = anchor;
        _surface = surface;
        _content.RenderTransform = _translation;
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
        if (ReferenceEquals(_content.RenderTransform, _translation))
        {
            _content.RenderTransform = null;
        }
    }

    private void OnTrackedPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == Visual.BoundsProperty)
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

        var transform = _anchor.TransformToVisual(_surface);
        if (transform is null)
        {
            return;
        }

        var renderedCenter = transform.Value.Transform(new Point(_anchor.Bounds.Width / 2, 0)).X;
        var arrangedCenter = renderedCenter - _translation.X;
        var rawTranslation = (_surface.Bounds.Width / 2) - arrangedCenter;
        var renderScaling = TopLevel.GetTopLevel(_surface)?.RenderScaling ?? 1;
        // Render transforms bypass layout rounding, so keep the title content on the physical pixel grid.
        var nextTranslation = Math.Round(rawTranslation * renderScaling) / renderScaling;
        if (Math.Abs(_translation.X - nextTranslation) > 0.1)
        {
            _translation.X = nextTranslation;
        }
    }
}
