using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;

namespace PacToolkits.Desktop.Avalonia.Views;

/// <summary>
/// 通过调整内容布局边距保持标题路径在标题栏内水平居中
///
/// 使用布局边距可保留像素舍入；渲染变换可能落在非整数设备像素上，导致文字和图标模糊
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
        // 右栏展开模块等改变两侧占位后，仅靠 Bounds 有时漏触发
        _surface.LayoutUpdated += OnLayoutUpdated;
        Update();
    }

    public void Dispose()
    {
        _content.PropertyChanged -= OnTrackedPropertyChanged;
        _anchor.PropertyChanged -= OnTrackedPropertyChanged;
        _surface.PropertyChanged -= OnTrackedPropertyChanged;
        _surface.LayoutUpdated -= OnLayoutUpdated;
        if (_capturedBaseMargin)
        {
            _content.Margin = _baseMargin;
        }
    }

    private void OnLayoutUpdated(object? sender, EventArgs e)
        => Update();

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
        if (_updating)
        {
            return;
        }

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
        // 偏移量必须在布局舍入前对齐物理像素，避免标题内容模糊
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
