using System;
using System.Collections;
using System.Linq;
using System.Windows.Input;
using Avalonia;
using global::Avalonia.Controls;
using PacToolkits.Desktop.Avalonia.Common;

namespace PacToolkits.Desktop.Avalonia.Controls;

public enum DataGridPagerPlacement
{
    Bottom,
    Top
}

public partial class DataGridPager : UserControl
{
    private static readonly object[] DefaultPageSizeOptions = [20, 50, 100];

    public static readonly StyledProperty<int> PageIndexProperty =
        AvaloniaProperty.Register<DataGridPager, int>(nameof(PageIndex), 1);

    public static readonly StyledProperty<int> TotalPagesProperty =
        AvaloniaProperty.Register<DataGridPager, int>(nameof(TotalPages), 1);

    public static readonly StyledProperty<int> TotalCountProperty =
        AvaloniaProperty.Register<DataGridPager, int>(nameof(TotalCount));

    public static readonly StyledProperty<int> PageSizeProperty =
        AvaloniaProperty.Register<DataGridPager, int>(nameof(PageSize));

    public static readonly StyledProperty<int> SelectedCountProperty =
        AvaloniaProperty.Register<DataGridPager, int>(nameof(SelectedCount), -1);

    public static readonly StyledProperty<string?> StatusTextProperty =
        AvaloniaProperty.Register<DataGridPager, string?>(nameof(StatusText));

    public static readonly StyledProperty<IEnumerable?> PageSizeOptionsProperty =
        AvaloniaProperty.Register<DataGridPager, IEnumerable?>(nameof(PageSizeOptions));

    public static readonly StyledProperty<object?> SelectedPageSizeProperty =
        AvaloniaProperty.Register<DataGridPager, object?>(nameof(SelectedPageSize));

    public static readonly StyledProperty<bool> ShowPageSizeSectionProperty =
        AvaloniaProperty.Register<DataGridPager, bool>(nameof(ShowPageSizeSection), true);

    public static readonly StyledProperty<bool> IsResponsiveProperty =
        AvaloniaProperty.Register<DataGridPager, bool>(nameof(IsResponsive), true);

    public static readonly StyledProperty<double> ResponsiveCompactThresholdProperty =
        AvaloniaProperty.Register<DataGridPager, double>(nameof(ResponsiveCompactThreshold), 520d);

    public static readonly StyledProperty<double> ResponsiveMinimalThresholdProperty =
        AvaloniaProperty.Register<DataGridPager, double>(nameof(ResponsiveMinimalThreshold), 340d);

    public static readonly StyledProperty<DataGridPagerPlacement> PlacementProperty =
        AvaloniaProperty.Register<DataGridPager, DataGridPagerPlacement>(nameof(Placement), DataGridPagerPlacement.Bottom);

    public static readonly StyledProperty<Thickness> PagerPaddingProperty =
        AvaloniaProperty.Register<DataGridPager, Thickness>(nameof(PagerPadding), new Thickness(0, 4, 0, 0));

    public static readonly StyledProperty<double> HorizontalInsetProperty =
        AvaloniaProperty.Register<DataGridPager, double>(nameof(HorizontalInset));

    public static readonly StyledProperty<ICommand?> FirstPageCommandProperty =
        AvaloniaProperty.Register<DataGridPager, ICommand?>(nameof(FirstPageCommand));

    public static readonly StyledProperty<ICommand?> PrevPageCommandProperty =
        AvaloniaProperty.Register<DataGridPager, ICommand?>(nameof(PrevPageCommand));

    public static readonly StyledProperty<ICommand?> NextPageCommandProperty =
        AvaloniaProperty.Register<DataGridPager, ICommand?>(nameof(NextPageCommand));

    public static readonly StyledProperty<ICommand?> LastPageCommandProperty =
        AvaloniaProperty.Register<DataGridPager, ICommand?>(nameof(LastPageCommand));

    public static readonly StyledProperty<bool> IsContentEmptyProperty =
        AvaloniaProperty.Register<DataGridPager, bool>(nameof(IsContentEmpty));

    public static readonly StyledProperty<bool> IsActiveProperty =
        AvaloniaProperty.Register<DataGridPager, bool>(nameof(IsActive), true);

    public static readonly StyledProperty<DataGrid?> ClearTargetProperty =
        AvaloniaProperty.Register<DataGridPager, DataGrid?>(nameof(ClearTarget));

    public static readonly DirectProperty<DataGridPager, string> PageSummaryTextProperty =
        AvaloniaProperty.RegisterDirect<DataGridPager, string>(nameof(PageSummaryText), o => o.PageSummaryText);

    public static readonly DirectProperty<DataGridPager, string> SelectionSummaryTextProperty =
        AvaloniaProperty.RegisterDirect<DataGridPager, string>(
            nameof(SelectionSummaryText),
            o => o.SelectionSummaryText);

    public static readonly DirectProperty<DataGridPager, bool> ShowSelectionSummaryProperty =
        AvaloniaProperty.RegisterDirect<DataGridPager, bool>(nameof(ShowSelectionSummary), o => o.ShowSelectionSummary);

    public static readonly DirectProperty<DataGridPager, bool> HasStatusTextProperty =
        AvaloniaProperty.RegisterDirect<DataGridPager, bool>(nameof(HasStatusText), o => o.HasStatusText);

    public static readonly DirectProperty<DataGridPager, bool> CanGoFirstPageProperty =
        AvaloniaProperty.RegisterDirect<DataGridPager, bool>(nameof(CanGoFirstPage), o => o.CanGoFirstPage);

    public static readonly DirectProperty<DataGridPager, bool> CanGoPrevPageProperty =
        AvaloniaProperty.RegisterDirect<DataGridPager, bool>(nameof(CanGoPrevPage), o => o.CanGoPrevPage);

    public static readonly DirectProperty<DataGridPager, bool> CanGoNextPageProperty =
        AvaloniaProperty.RegisterDirect<DataGridPager, bool>(nameof(CanGoNextPage), o => o.CanGoNextPage);

    public static readonly DirectProperty<DataGridPager, bool> CanGoLastPageProperty =
        AvaloniaProperty.RegisterDirect<DataGridPager, bool>(nameof(CanGoLastPage), o => o.CanGoLastPage);

    public static readonly DirectProperty<DataGridPager, bool> HasFirstPageCommandProperty =
        AvaloniaProperty.RegisterDirect<DataGridPager, bool>(nameof(HasFirstPageCommand), o => o.HasFirstPageCommand);

    public static readonly DirectProperty<DataGridPager, bool> HasPrevPageCommandProperty =
        AvaloniaProperty.RegisterDirect<DataGridPager, bool>(nameof(HasPrevPageCommand), o => o.HasPrevPageCommand);

    public static readonly DirectProperty<DataGridPager, bool> HasNextPageCommandProperty =
        AvaloniaProperty.RegisterDirect<DataGridPager, bool>(nameof(HasNextPageCommand), o => o.HasNextPageCommand);

    public static readonly DirectProperty<DataGridPager, bool> HasLastPageCommandProperty =
        AvaloniaProperty.RegisterDirect<DataGridPager, bool>(nameof(HasLastPageCommand), o => o.HasLastPageCommand);

    public static readonly DirectProperty<DataGridPager, bool> ShowResponsivePageSizeSectionProperty =
        AvaloniaProperty.RegisterDirect<DataGridPager, bool>(
            nameof(ShowResponsivePageSizeSection),
            o => o.ShowResponsivePageSizeSection);

    public static readonly DirectProperty<DataGridPager, bool> ShowResponsivePageSummaryProperty =
        AvaloniaProperty.RegisterDirect<DataGridPager, bool>(
            nameof(ShowResponsivePageSummary),
            o => o.ShowResponsivePageSummary);

    private string _pageSummaryText = "第 1 页，共 1 页";
    private string _selectionSummaryText = string.Empty;
    private bool _showSelectionSummary;
    private bool _hasStatusText;
    private bool _canGoFirstPage;
    private bool _canGoPrevPage;
    private bool _canGoNextPage;
    private bool _canGoLastPage;
    private bool _hasFirstPageCommand;
    private bool _hasPrevPageCommand;
    private bool _hasNextPageCommand;
    private bool _hasLastPageCommand;
    private bool _showResponsivePageSizeSection = true;
    private bool _showResponsivePageSummary = true;
    private IEnumerable? _boundPageSizeOptions;
    private readonly EventHandler _onCommandCanExecuteChanged;

    public int PageIndex
    {
        get => GetValue(PageIndexProperty);
        set => SetValue(PageIndexProperty, value);
    }

    public int TotalPages
    {
        get => GetValue(TotalPagesProperty);
        set => SetValue(TotalPagesProperty, value);
    }

    public int TotalCount
    {
        get => GetValue(TotalCountProperty);
        set => SetValue(TotalCountProperty, value);
    }

    public int PageSize
    {
        get => GetValue(PageSizeProperty);
        set => SetValue(PageSizeProperty, value);
    }

    public int SelectedCount
    {
        get => GetValue(SelectedCountProperty);
        set => SetValue(SelectedCountProperty, value);
    }

    public string? StatusText
    {
        get => GetValue(StatusTextProperty);
        set => SetValue(StatusTextProperty, value);
    }

    public IEnumerable? PageSizeOptions
    {
        get => GetValue(PageSizeOptionsProperty);
        set => SetValue(PageSizeOptionsProperty, value);
    }

    public object? SelectedPageSize
    {
        get => GetValue(SelectedPageSizeProperty);
        set => SetValue(SelectedPageSizeProperty, value);
    }

    public bool ShowPageSizeSection
    {
        get => GetValue(ShowPageSizeSectionProperty);
        set => SetValue(ShowPageSizeSectionProperty, value);
    }

    public bool IsResponsive
    {
        get => GetValue(IsResponsiveProperty);
        set => SetValue(IsResponsiveProperty, value);
    }

    public double ResponsiveCompactThreshold
    {
        get => GetValue(ResponsiveCompactThresholdProperty);
        set => SetValue(ResponsiveCompactThresholdProperty, value);
    }

    public double ResponsiveMinimalThreshold
    {
        get => GetValue(ResponsiveMinimalThresholdProperty);
        set => SetValue(ResponsiveMinimalThresholdProperty, value);
    }

    public DataGridPagerPlacement Placement
    {
        get => GetValue(PlacementProperty);
        set => SetValue(PlacementProperty, value);
    }

    public Thickness PagerPadding
    {
        get => GetValue(PagerPaddingProperty);
        set => SetValue(PagerPaddingProperty, value);
    }

    public double HorizontalInset
    {
        get => GetValue(HorizontalInsetProperty);
        set => SetValue(HorizontalInsetProperty, value);
    }

    public ICommand? FirstPageCommand
    {
        get => GetValue(FirstPageCommandProperty);
        set => SetValue(FirstPageCommandProperty, value);
    }

    public ICommand? PrevPageCommand
    {
        get => GetValue(PrevPageCommandProperty);
        set => SetValue(PrevPageCommandProperty, value);
    }

    public ICommand? NextPageCommand
    {
        get => GetValue(NextPageCommandProperty);
        set => SetValue(NextPageCommandProperty, value);
    }

    public ICommand? LastPageCommand
    {
        get => GetValue(LastPageCommandProperty);
        set => SetValue(LastPageCommandProperty, value);
    }

    public bool IsContentEmpty
    {
        get => GetValue(IsContentEmptyProperty);
        set => SetValue(IsContentEmptyProperty, value);
    }

    public bool IsActive
    {
        get => GetValue(IsActiveProperty);
        set => SetValue(IsActiveProperty, value);
    }

    public DataGrid? ClearTarget
    {
        get => GetValue(ClearTargetProperty);
        set => SetValue(ClearTargetProperty, value);
    }

    public string PageSummaryText => _pageSummaryText;
    public string SelectionSummaryText => _selectionSummaryText;
    public bool ShowSelectionSummary => _showSelectionSummary;
    public bool HasStatusText => _hasStatusText;
    public bool CanGoFirstPage => _canGoFirstPage;
    public bool CanGoPrevPage => _canGoPrevPage;
    public bool CanGoNextPage => _canGoNextPage;
    public bool CanGoLastPage => _canGoLastPage;
    public bool HasFirstPageCommand => _hasFirstPageCommand;
    public bool HasPrevPageCommand => _hasPrevPageCommand;
    public bool HasNextPageCommand => _hasNextPageCommand;
    public bool HasLastPageCommand => _hasLastPageCommand;
    public bool ShowResponsivePageSizeSection => _showResponsivePageSizeSection;
    public bool ShowResponsivePageSummary => _showResponsivePageSummary;

    public DataGridPager()
    {
        _onCommandCanExecuteChanged = (_, _) => RefreshDerivedState();
        InitializeComponent();
        RefreshPageSizeComboItems();
        SyncSelectedPageSize();
        RefreshDerivedState();
        ApplyPlacementVisuals();
        UpdatePagerVisibility();
        UpdateResponsiveState();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == PageSizeOptionsProperty)
        {
            RefreshPageSizeComboItems();
            SyncSelectedPageSize();
        }
        else if (change.Property == SelectedPageSizeProperty
                 || change.Property == PageSizeProperty)
        {
            SyncSelectedPageSize();
        }

        if (change.Property == FirstPageCommandProperty
            || change.Property == PrevPageCommandProperty
            || change.Property == NextPageCommandProperty
            || change.Property == LastPageCommandProperty)
        {
            if (change.OldValue is ICommand oldCommand)
            {
                oldCommand.CanExecuteChanged -= _onCommandCanExecuteChanged;
            }

            if (change.NewValue is ICommand newCommand)
            {
                newCommand.CanExecuteChanged += _onCommandCanExecuteChanged;
            }

            RefreshDerivedState();
            return;
        }

        if (change.Property == PageIndexProperty)
        {
            RefreshDerivedState();
            if (change.NewValue is int newIndex && change.OldValue is int oldIndex)
            {
                DataGridInteractionHelper.ClearOnPageChange(ClearTarget, oldIndex, newIndex);
            }

            return;
        }

        if (change.Property == TotalPagesProperty
            || change.Property == TotalCountProperty
            || change.Property == PageSizeProperty
            || change.Property == SelectedCountProperty
            || change.Property == StatusTextProperty
            || change.Property == PageSizeOptionsProperty
            || change.Property == SelectedPageSizeProperty
            || change.Property == ShowPageSizeSectionProperty)
        {
            RefreshDerivedState();
        }

        if (change.Property == PlacementProperty
            || change.Property == HorizontalInsetProperty)
        {
            ApplyPlacementVisuals();
        }

        if (change.Property == IsContentEmptyProperty
            || change.Property == IsActiveProperty)
        {
            UpdatePagerVisibility();
        }

        if (change.Property == BoundsProperty
            || change.Property == IsResponsiveProperty
            || change.Property == ResponsiveCompactThresholdProperty
            || change.Property == ResponsiveMinimalThresholdProperty
            || change.Property == ShowPageSizeSectionProperty)
        {
            UpdateResponsiveState();
        }
    }

    protected override void OnDetachedFromVisualTree(global::Avalonia.VisualTreeAttachmentEventArgs e)
    {
        UnhookCommands();
        base.OnDetachedFromVisualTree(e);
    }

    private void UnhookCommands()
    {
        FirstPageCommand?.CanExecuteChanged -= _onCommandCanExecuteChanged;
        PrevPageCommand?.CanExecuteChanged -= _onCommandCanExecuteChanged;
        NextPageCommand?.CanExecuteChanged -= _onCommandCanExecuteChanged;
        LastPageCommand?.CanExecuteChanged -= _onCommandCanExecuteChanged;
    }

    private void RefreshPageSizeComboItems()
    {
        var options = ResolvePageSizeOptions();
        if (ReferenceEquals(_boundPageSizeOptions, options))
        {
            return;
        }

        _boundPageSizeOptions = options;
        PageSizeCombo.ItemsSource = options;
    }

    private void ApplyPlacementVisuals()
    {
        var edge = GetPagerEdgeSpacing();
        var inset = HorizontalInset;
        var isTop = Placement == DataGridPagerPlacement.Top;
        PagerPadding = isTop
            ? new Thickness(inset, 0, inset, edge * 2)
            : new Thickness(inset, edge, inset, edge);
        PagerChrome.BorderThickness = isTop ? new Thickness(0, 0, 0, 1) : new Thickness(0);
        Classes.Set("Top", isTop);
        Classes.Set("Bottom", !isTop);
    }

    private static double GetPagerEdgeSpacing()
    {
        if (global::Avalonia.Application.Current?.TryGetResource("PagerEdgeSpacing", null, out var value) == true
            && value is double spacing)
        {
            return spacing;
        }

        return 4;
    }

    private void UpdatePagerVisibility()
    {
        IsVisible = IsActive && !IsContentEmpty;
    }

    private void UpdateResponsiveState()
    {
        var width = Bounds.Width;
        var compact = IsResponsive && width > 0 && width < ResponsiveCompactThreshold;
        var minimal = IsResponsive && width > 0 && width < ResponsiveMinimalThreshold;

        Classes.Set("ResponsiveCompact", compact);
        Classes.Set("ResponsiveMinimal", minimal);
        SetAndRaise(
            ShowResponsivePageSizeSectionProperty,
            ref _showResponsivePageSizeSection,
            ShowPageSizeSection && !compact);
        SetAndRaise(
            ShowResponsivePageSummaryProperty,
            ref _showResponsivePageSummary,
            !minimal);
    }

    private void RefreshDerivedState()
    {
        var safeTotalPages = Math.Max(1, TotalPages);
        var safePageIndex = Math.Clamp(PageIndex, 1, safeTotalPages);

        SetAndRaise(PageSummaryTextProperty, ref _pageSummaryText, $"第 {safePageIndex} 页，共 {safeTotalPages} 页");

        var showSelection = SelectedCount >= 0 && TotalCount >= 0;
        SetAndRaise(ShowSelectionSummaryProperty, ref _showSelectionSummary, showSelection);
        SetAndRaise(
            SelectionSummaryTextProperty,
            ref _selectionSummaryText,
            showSelection ? $"已选择 {SelectedCount} / {TotalCount} 行" : string.Empty);

        var status = StatusText?.Trim();
        SetAndRaise(HasStatusTextProperty, ref _hasStatusText, !string.IsNullOrEmpty(status));

        var hasPrev = safePageIndex > 1;
        var hasNext = safePageIndex < safeTotalPages;
        // Button IsEnabled is explicit; must AND CanExecute or busy/guard gates never show disabled.
        SetAndRaise(CanGoFirstPageProperty, ref _canGoFirstPage, hasPrev && CanExecute(FirstPageCommand));
        SetAndRaise(CanGoPrevPageProperty, ref _canGoPrevPage, hasPrev && CanExecute(PrevPageCommand));
        SetAndRaise(CanGoNextPageProperty, ref _canGoNextPage, hasNext && CanExecute(NextPageCommand));
        SetAndRaise(CanGoLastPageProperty, ref _canGoLastPage, hasNext && CanExecute(LastPageCommand));

        SetAndRaise(HasFirstPageCommandProperty, ref _hasFirstPageCommand, FirstPageCommand is not null);
        SetAndRaise(HasPrevPageCommandProperty, ref _hasPrevPageCommand, PrevPageCommand is not null);
        SetAndRaise(HasNextPageCommandProperty, ref _hasNextPageCommand, NextPageCommand is not null);
        SetAndRaise(HasLastPageCommandProperty, ref _hasLastPageCommand, LastPageCommand is not null);
    }

    private static bool CanExecute(ICommand? command)
        => command is not null && command.CanExecute(null);

    private IEnumerable ResolvePageSizeOptions()
    {
        if (PageSizeOptions is not null)
        {
            var materialized = PageSizeOptions.Cast<object?>().Where(x => x is not null).ToArray();
            if (materialized.Length > 0)
            {
                return PageSizeOptions;
            }
        }

        return DefaultPageSizeOptions;
    }

    private void SyncSelectedPageSize()
    {
        var options = ResolvePageSizeOptions().Cast<object?>().Where(x => x is not null).ToList();
        if (options.Count == 0)
        {
            return;
        }

        var current = SelectedPageSize;
        if (current is not null && options.Any(option => OptionsEqual(option, current)))
        {
            if (!OptionsEqual(PageSizeCombo.SelectedItem, current))
            {
                PageSizeCombo.SelectedItem = options.First(option => OptionsEqual(option, current));
            }

            return;
        }

        if (PageSize > 0)
        {
            var pageSizeMatch = options.FirstOrDefault(option =>
                string.Equals(option?.ToString(), PageSize.ToString(), StringComparison.Ordinal));
            if (pageSizeMatch is not null)
            {
                SelectedPageSize = pageSizeMatch;
                return;
            }
        }

        if (current is not null)
        {
            var currentMatch = options.FirstOrDefault(option =>
                string.Equals(option?.ToString(), current.ToString(), StringComparison.Ordinal));
            if (currentMatch is not null)
            {
                SelectedPageSize = currentMatch;
                return;
            }
        }

        SelectedPageSize = options[0];
    }

    private static bool OptionsEqual(object? left, object? right)
    {
        if (left is null || right is null)
        {
            return false;
        }

        if (Equals(left, right))
        {
            return true;
        }

        return string.Equals(left.ToString(), right.ToString(), StringComparison.Ordinal);
    }
}
