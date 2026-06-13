using System;
using Avalonia;
using global::Avalonia.Controls;
using global::Avalonia.Media;
using global::Avalonia.Threading;

namespace PacToolkits.Desktop.Avalonia.Controls;

public partial class BusyCircle : UserControl
{
    public static readonly StyledProperty<IBrush?> DotBrushProperty =
        AvaloniaProperty.Register<BusyCircle, IBrush?>(nameof(DotBrush));

    private readonly DispatcherTimer _timer;
    private int _phase;
    private Border[]? _dots;

    public IBrush? DotBrush
    {
        get => GetValue(DotBrushProperty);
        set => SetValue(DotBrushProperty, value);
    }

    public BusyCircle()
    {
        InitializeComponent();
        _timer = new DispatcherTimer(TimeSpan.FromMilliseconds(90), DispatcherPriority.Background, OnTick);
        AttachedToVisualTree += (_, _) => Start();
        DetachedFromVisualTree += (_, _) => Stop();
        _dots = [Dot0, Dot1, Dot2, Dot3, Dot4, Dot5, Dot6, Dot7];
        UpdateDots();
    }

    private void Start()
    {
        if (!_timer.IsEnabled)
            _timer.Start();
    }

    private void Stop()
    {
        _timer.Stop();
        _phase = 0;
        UpdateDots();
    }

    private void OnTick(object? sender, EventArgs e)
    {
        _phase = (_phase + 1) % 8;
        UpdateDots();
    }

    private void UpdateDots()
    {
        if (_dots is null)
            return;

        for (var i = 0; i < _dots.Length; i++)
        {
            var distance = (i - _phase + _dots.Length) % _dots.Length;
            _dots[i].Opacity = distance switch
            {
                0 => 1.0,
                1 => 0.8,
                2 => 0.6,
                3 => 0.4,
                _ => 0.2
            };
        }
    }
}
