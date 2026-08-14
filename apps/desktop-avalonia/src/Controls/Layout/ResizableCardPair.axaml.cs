using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Input;
using Avalonia.Layout;

namespace PacToolkits.Desktop.Avalonia.Controls;

public partial class ResizableCardPair : UserControl
{
    private readonly ContentPresenter _firstPresenter;
    private readonly Grid _layoutRoot;
    private readonly ContentPresenter _secondPresenter;
    private readonly Border _splitter;
    private bool _dragging;
    private double _dragFirstSize;
    private Point _dragOrigin;

    public static readonly StyledProperty<object?> FirstContentProperty =
        AvaloniaProperty.Register<ResizableCardPair, object?>(nameof(FirstContent));

    public static readonly StyledProperty<object?> SecondContentProperty =
        AvaloniaProperty.Register<ResizableCardPair, object?>(nameof(SecondContent));

    public static readonly StyledProperty<Orientation> OrientationProperty =
        AvaloniaProperty.Register<ResizableCardPair, Orientation>(
            nameof(Orientation),
            Orientation.Horizontal);

    public static readonly StyledProperty<double> FirstMinSizeProperty =
        AvaloniaProperty.Register<ResizableCardPair, double>(nameof(FirstMinSize), 280d);

    public static readonly StyledProperty<double> SecondMinSizeProperty =
        AvaloniaProperty.Register<ResizableCardPair, double>(nameof(SecondMinSize), 280d);

    public static readonly StyledProperty<double> SplitterSizeProperty =
        AvaloniaProperty.Register<ResizableCardPair, double>(nameof(SplitterSize), 12d);

    static ResizableCardPair()
    {
        OrientationProperty.Changed.AddClassHandler<ResizableCardPair>(
            static (control, _) => control.UpdatePairLayout());
        FirstMinSizeProperty.Changed.AddClassHandler<ResizableCardPair>(
            static (control, _) => control.UpdatePairLayout());
        SecondMinSizeProperty.Changed.AddClassHandler<ResizableCardPair>(
            static (control, _) => control.UpdatePairLayout());
        SplitterSizeProperty.Changed.AddClassHandler<ResizableCardPair>(
            static (control, _) => control.UpdatePairLayout());
    }

    public ResizableCardPair()
    {
        InitializeComponent();
        _layoutRoot = this.FindControl<Grid>("LayoutRoot")
            ?? throw new InvalidOperationException("Resizable card layout root is missing.");
        _firstPresenter = this.FindControl<ContentPresenter>("FirstPresenter")
            ?? throw new InvalidOperationException("Resizable card first presenter is missing.");
        _splitter = this.FindControl<Border>("Splitter")
            ?? throw new InvalidOperationException("Resizable card splitter is missing.");
        _secondPresenter = this.FindControl<ContentPresenter>("SecondPresenter")
            ?? throw new InvalidOperationException("Resizable card second presenter is missing.");
        _splitter.PointerPressed += OnSplitterPressed;
        _splitter.PointerMoved += OnSplitterMoved;
        _splitter.PointerReleased += OnSplitterReleased;
        _splitter.PointerCaptureLost += OnSplitterCaptureLost;
        UpdatePairLayout();
    }

    public object? FirstContent
    {
        get => GetValue(FirstContentProperty);
        set => SetValue(FirstContentProperty, value);
    }

    public object? SecondContent
    {
        get => GetValue(SecondContentProperty);
        set => SetValue(SecondContentProperty, value);
    }

    public Orientation Orientation
    {
        get => GetValue(OrientationProperty);
        set => SetValue(OrientationProperty, value);
    }

    public double FirstMinSize
    {
        get => GetValue(FirstMinSizeProperty);
        set => SetValue(FirstMinSizeProperty, value);
    }

    public double SecondMinSize
    {
        get => GetValue(SecondMinSizeProperty);
        set => SetValue(SecondMinSizeProperty, value);
    }

    public double SplitterSize
    {
        get => GetValue(SplitterSizeProperty);
        set => SetValue(SplitterSizeProperty, value);
    }

    private bool IsHorizontalLayout =>
        _layoutRoot.ColumnDefinitions.Count == 3 && _layoutRoot.RowDefinitions.Count == 1;

    private bool IsVerticalLayout =>
        _layoutRoot.ColumnDefinitions.Count == 1 && _layoutRoot.RowDefinitions.Count == 3;

    // 拖出缝外仍要收到移动，必须 Capture
    // Capture 会摘掉 :pointerover，按住时用 Hot 保住主色
    private void OnSplitterPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(_splitter).Properties.IsLeftButtonPressed
            || !TryPinLeading(out _dragFirstSize))
        {
            return;
        }

        _dragging = true;
        _dragOrigin = e.GetPosition(_layoutRoot);
        e.Pointer.Capture(_splitter);
        _splitter.Classes.Set("Hot", true);
        e.PreventGestureRecognition();
        e.Handled = true;
    }

    private void OnSplitterMoved(object? sender, PointerEventArgs e)
    {
        if (!_dragging)
        {
            return;
        }

        var pos = e.GetPosition(_layoutRoot);
        var delta = Orientation == Orientation.Horizontal
            ? pos.X - _dragOrigin.X
            : pos.Y - _dragOrigin.Y;
        ApplyDrag(_dragFirstSize + delta);
        e.Handled = true;
    }

    private void OnSplitterReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!_dragging)
        {
            return;
        }

        EndDrag();
        if (ReferenceEquals(e.Pointer.Captured, _splitter))
        {
            e.Pointer.Capture(null);
        }

        e.Handled = true;
    }

    private void OnSplitterCaptureLost(object? sender, PointerCaptureLostEventArgs e) => EndDrag();

    private void EndDrag()
    {
        _dragging = false;
        _splitter.Classes.Set("Hot", false);
    }

    private bool TryPinLeading(out double firstSize)
    {
        firstSize = 0;
        if (Orientation == Orientation.Horizontal)
        {
            if (!IsHorizontalLayout)
            {
                return false;
            }

            var first = _layoutRoot.ColumnDefinitions[0];
            if (first.ActualWidth <= 0)
            {
                return false;
            }

            firstSize = first.ActualWidth;
            first.Width = new GridLength(firstSize, GridUnitType.Pixel);
            return true;
        }

        if (!IsVerticalLayout)
        {
            return false;
        }

        var firstRow = _layoutRoot.RowDefinitions[0];
        if (firstRow.ActualHeight <= 0)
        {
            return false;
        }

        firstSize = firstRow.ActualHeight;
        firstRow.Height = new GridLength(firstSize, GridUnitType.Pixel);
        return true;
    }

    private void ApplyDrag(double firstSize)
    {
        if (Orientation == Orientation.Horizontal)
        {
            var gap = _layoutRoot.ColumnDefinitions[1].ActualWidth;
            var upper = Math.Max(FirstMinSize, _layoutRoot.Bounds.Width - gap - SecondMinSize);
            _layoutRoot.ColumnDefinitions[0].Width =
                new GridLength(Math.Clamp(firstSize, FirstMinSize, upper), GridUnitType.Pixel);
            return;
        }

        var rowGap = _layoutRoot.RowDefinitions[1].ActualHeight;
        var rowUpper = Math.Max(FirstMinSize, _layoutRoot.Bounds.Height - rowGap - SecondMinSize);
        _layoutRoot.RowDefinitions[0].Height =
            new GridLength(Math.Clamp(firstSize, FirstMinSize, rowUpper), GridUnitType.Pixel);
    }

    private void UpdatePairLayout()
    {
        if (_layoutRoot is null)
        {
            return;
        }

        _splitter.Classes.Set("Horizontal", Orientation == Orientation.Horizontal);
        _splitter.Classes.Set("Vertical", Orientation == Orientation.Vertical);

        // 同向只改最小尺寸；清列会丢掉拖出来的像素宽
        if (Orientation == Orientation.Horizontal)
        {
            if (IsHorizontalLayout)
            {
                _layoutRoot.ColumnDefinitions[0].MinWidth = FirstMinSize;
                _layoutRoot.ColumnDefinitions[2].MinWidth = SecondMinSize;
                _splitter.Width = SplitterSize;
                _splitter.Height = double.NaN;
                _splitter.Cursor = new Cursor(StandardCursorType.SizeWestEast);
                return;
            }

            _layoutRoot.ColumnDefinitions.Clear();
            _layoutRoot.RowDefinitions.Clear();
            ConfigureHorizontalLayout();
            return;
        }

        if (IsVerticalLayout)
        {
            _layoutRoot.RowDefinitions[0].MinHeight = FirstMinSize;
            _layoutRoot.RowDefinitions[2].MinHeight = SecondMinSize;
            _splitter.Width = double.NaN;
            _splitter.Height = SplitterSize;
            _splitter.Cursor = new Cursor(StandardCursorType.SizeNorthSouth);
            return;
        }

        _layoutRoot.ColumnDefinitions.Clear();
        _layoutRoot.RowDefinitions.Clear();
        ConfigureVerticalLayout();
    }

    private void ConfigureHorizontalLayout()
    {
        _layoutRoot.ColumnDefinitions.Add(
            new ColumnDefinition(GridLength.Star) { MinWidth = FirstMinSize });
        _layoutRoot.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        _layoutRoot.ColumnDefinitions.Add(
            new ColumnDefinition(GridLength.Star) { MinWidth = SecondMinSize });
        _layoutRoot.RowDefinitions.Add(new RowDefinition(GridLength.Star));

        Grid.SetColumn(_firstPresenter, 0);
        Grid.SetRow(_firstPresenter, 0);
        Grid.SetColumn(_splitter, 1);
        Grid.SetRow(_splitter, 0);
        Grid.SetColumn(_secondPresenter, 2);
        Grid.SetRow(_secondPresenter, 0);

        _splitter.Width = SplitterSize;
        _splitter.Height = double.NaN;
        _splitter.Cursor = new Cursor(StandardCursorType.SizeWestEast);
    }

    private void ConfigureVerticalLayout()
    {
        _layoutRoot.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        _layoutRoot.RowDefinitions.Add(
            new RowDefinition(GridLength.Star) { MinHeight = FirstMinSize });
        _layoutRoot.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        _layoutRoot.RowDefinitions.Add(
            new RowDefinition(GridLength.Star) { MinHeight = SecondMinSize });

        Grid.SetColumn(_firstPresenter, 0);
        Grid.SetRow(_firstPresenter, 0);
        Grid.SetColumn(_splitter, 0);
        Grid.SetRow(_splitter, 1);
        Grid.SetColumn(_secondPresenter, 0);
        Grid.SetRow(_secondPresenter, 2);

        _splitter.Width = double.NaN;
        _splitter.Height = SplitterSize;
        _splitter.Cursor = new Cursor(StandardCursorType.SizeNorthSouth);
    }
}
