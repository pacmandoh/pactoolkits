using System;
using Avalonia;
using Avalonia.Controls;

namespace PacToolkits.Desktop.Avalonia.Controls;

/// <summary>
/// BusyArea 模板宿主：IsBusy 时冻结测量高度，避免 Auto 增高宿主带动居中 Loading 下沉
/// </summary>
public sealed class BusyHostPanel : Panel
{
    public static readonly StyledProperty<bool> IsBusyProperty =
        AvaloniaProperty.Register<BusyHostPanel, bool>(nameof(IsBusy));

    private double? _frozenHeight;

    static BusyHostPanel()
    {
        AffectsMeasure<BusyHostPanel>(IsBusyProperty);
        IsBusyProperty.Changed.AddClassHandler<BusyHostPanel>((panel, e) =>
        {
            panel._frozenHeight = e.GetNewValue<bool>() && panel.Bounds.Height > 0
                ? panel.Bounds.Height
                : null;
            panel.InvalidateMeasure();
        });
    }

    public bool IsBusy
    {
        get => GetValue(IsBusyProperty);
        set => SetValue(IsBusyProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        if (!IsBusy)
        {
            _frozenHeight = null;
            return MeasureChildren(availableSize);
        }

        if (_frozenHeight is not > 0)
        {
            if (!double.IsInfinity(availableSize.Height) && availableSize.Height > 0)
            {
                _frozenHeight = availableSize.Height;
            }
            else
            {
                var min = double.IsFinite(MinHeight) && MinHeight > 0 ? MinHeight : 0;
                _frozenHeight = Math.Max(min, MeasureChildren(availableSize).Height);
            }
        }

        var size = MeasureChildren(new Size(availableSize.Width, _frozenHeight.Value));
        return new Size(size.Width, _frozenHeight.Value);
    }

    private Size MeasureChildren(Size availableSize)
    {
        double width = 0;
        double height = 0;
        foreach (var child in Children)
        {
            child.Measure(availableSize);
            width = Math.Max(width, child.DesiredSize.Width);
            height = Math.Max(height, child.DesiredSize.Height);
        }

        return new Size(width, height);
    }
}
