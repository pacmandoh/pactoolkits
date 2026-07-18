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
    private readonly GridSplitter _splitter;

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
        _splitter = this.FindControl<GridSplitter>("Splitter")
            ?? throw new InvalidOperationException("Resizable card splitter is missing.");
        _secondPresenter = this.FindControl<ContentPresenter>("SecondPresenter")
            ?? throw new InvalidOperationException("Resizable card second presenter is missing.");
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

    private void UpdatePairLayout()
    {
        if (_layoutRoot is null)
        {
            return;
        }

        _layoutRoot.ColumnDefinitions.Clear();
        _layoutRoot.RowDefinitions.Clear();
        _splitter.Classes.Set("Horizontal", Orientation == Orientation.Horizontal);
        _splitter.Classes.Set("Vertical", Orientation == Orientation.Vertical);

        if (Orientation == Orientation.Horizontal)
        {
            ConfigureHorizontalLayout();
            return;
        }

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
        _splitter.ResizeDirection = GridResizeDirection.Columns;
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
        _splitter.ResizeDirection = GridResizeDirection.Rows;
    }
}
