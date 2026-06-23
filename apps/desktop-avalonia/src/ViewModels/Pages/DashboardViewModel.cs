using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using global::Avalonia.Threading;
using PacToolkits.Application.Abstractions;
using PacToolkits.Application.DTOs;
using PacToolkits.Application.Services;
using PacToolkits.Desktop.Avalonia.Common;
using PacToolkits.Desktop.Avalonia.Services.Infrastructure;

namespace PacToolkits.Desktop.Avalonia.ViewModels.Pages;

public sealed record OptionItem(string Raw, string Display)
{
    public override string ToString() => Display;
}

public sealed partial class DashboardViewModel : AppPageBase
{
    private const int DefaultTopN = 10;
    private const int EntryOverviewTopN = 6;
    private static readonly int[] TabPageSizeOptionValues = [20, 50, 100];

    public override string DisplayName => "概览";
    public override string Icon => "LayoutDashboard";
    public override int Index => 0;
    protected override bool AutoRefreshOnDbDisconnected => true;
    protected override bool AutoRefreshOnDbReconnected => true;

    private readonly IDashboardService _dashboard;
    private readonly IToastService _toast;
    private readonly IClientAliasService _clientAlias;
    private readonly ILookupCatalogService _lookup;
    private readonly PageNavigationService _nav;
    private readonly InventoryOverviewViewModel _inventoryOverview;
    private bool _suppressRowSelectionAction;
    private int _specLoadGeneration;
    private readonly RollingDateRangeController _dateRangeController;

    [ObservableProperty] private int _selectedTabIndex;
    [ObservableProperty] private bool _isFilterBarVisible = true;

    public bool IsOverviewTab => SelectedTabIndex == 0;
    public bool IsInputTab => SelectedTabIndex == 1;
    public bool IsTxnTab => SelectedTabIndex == 2;
    public bool IsAbnormalTab => SelectedTabIndex == 3;

    partial void OnSelectedTabIndexChanged(int value)
    {
        OnPropertyChanged(nameof(IsOverviewTab));
        OnPropertyChanged(nameof(IsInputTab));
        OnPropertyChanged(nameof(IsTxnTab));
        OnPropertyChanged(nameof(IsAbnormalTab));

        ClearBrowsingSelections();

        EnsureCurrentTabDataLoaded();
    }

    partial void OnTabPageSizeChanged(int value)
    {
        if (value <= 0)
        {
            return;
        }

        EntryPageIndex = 1;
        TxnPageIndex = 1;
        TxnTrendPageIndex = 1;
        AbnormalPageIndex = 1;
        OnPropertyChanged(nameof(EntryPageSize));
        OnPropertyChanged(nameof(TxnPageSize));
        OnPropertyChanged(nameof(TxnTrendPageSize));
        OnPropertyChanged(nameof(AbnormalPageSize));
        OnPropertyChanged(nameof(EntryTotalPages));
        OnPropertyChanged(nameof(TxnTotalPages));
        OnPropertyChanged(nameof(TxnTrendTotalPages));
        OnPropertyChanged(nameof(AbnormalTotalPages));
        EnsureCurrentTabDataLoaded();
    }

    [ObservableProperty] private DateTime? _fromDate = DateTime.Today.AddDays(-6);
    [ObservableProperty] private DateTime? _toDate = DateTime.Today;

    public DateTime? FromMaxDate => ToDate?.Date;
    public DateTime? ToMinDate => FromDate?.Date;
    public DateTime? ToMaxDate => DateTime.Today;

    public ObservableCollection<ClientInfo> Clients { get; } = new();

    [ObservableProperty] private ClientInfo? _selectedClient;

    private static readonly ClientInfo AllClients = new(
        Raw: "",
        Display: "全部客户端",
        Machine: null,
        User: null,
        Ip: null,
        Os: null,
        Version: null
    );

    public ObservableCollection<OptionItem> DrugOptions { get; } = new();
    public ObservableCollection<OptionItem> SpecOptions { get; } = new();
    private IReadOnlyList<OptionItem> _drugCatalog = [];

    private static readonly OptionItem AllSpec = new("", "全部规格");

    [ObservableProperty] private string? _drugText;


    [ObservableProperty] private OptionItem _selectedSpec = AllSpec;

    [ObservableProperty] private bool _isDrugSuggestOpen;

    partial void OnDrugTextChanged(string? value)
    {
        ClearDrugSpecFilterCommand.NotifyCanExecuteChanged();
        RefreshDrugOptionsOrder(value);

        var drug = NormalizeInput(value);
        if (string.IsNullOrWhiteSpace(drug))
        {
            IsDrugSuggestOpen = false;
            EnsureAllSpecOnly();
            if (!IsReloadSuppressed)
            {
                RequestReloadWithPagingReset();
            }

            return;
        }

        IsDrugSuggestOpen = true;

    }

    private void RefreshDrugOptionsOrder(string? searchText)
    {
        if (_drugCatalog.Count == 0)
        {
            return;
        }

        using (SuppressReload())
        {
            DrugAutoCompleteCatalogHelper.RefreshVisibleOptions(DrugOptions, _drugCatalog, searchText);
        }
    }

    partial void OnSelectedSpecChanged(OptionItem value)
    {
        if (IsReloadSuppressed)
            return;

        RequestReloadWithPagingReset();
    }

    private void EnsureAllSpecOnly()
    {
        using (SuppressReload())
        {
            SpecOptions.Clear();
            SpecOptions.Add(AllSpec);
            SelectedSpec = AllSpec;
        }
    }

    private async Task ReloadDrugOptionsAsync(CancellationToken ct)
    {
        if (IsLookupCatalogSuspended())
        {
            await RunOnUiAsync(() =>
            {
                using (SuppressReload())
                {
                    DrugOptions.Clear();
                    _drugCatalog = [];
                    IsDrugSuggestOpen = false;
                    EnsureAllSpecOnly();
                }
            }, DispatcherPriority.Background);
            return;
        }

        var list = await LookupOptionLoader.LoadDrugOptionsAsync(_lookup, ct).ConfigureAwait(false);

        await RunOnUiAsync(() =>
        {
            using (SuppressReload())
            {
                _drugCatalog = list;
                DrugAutoCompleteCatalogHelper.RefreshVisibleOptions(DrugOptions, _drugCatalog, DrugText);

                if (SpecOptions.Count == 0)
                {
                    SpecOptions.Add(AllSpec);
                }
            }
        }, DispatcherPriority.Background);
    }

    private async Task ReloadSpecsAsync(string drug)
    {
        var generation = Interlocked.Increment(ref _specLoadGeneration);
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(6));
            var ct = cts.Token;

            IReadOnlyList<string> specs = Array.Empty<string>();

            if (!string.IsNullOrWhiteSpace(drug))
            {
                specs = await LookupOptionLoader.LoadSpecsAsync(_lookup, drug, ct).ConfigureAwait(false);
            }

            await RunOnUiAsync(() =>
            {
                if (generation != _specLoadGeneration)
                {
                    return;
                }

                using (SuppressReload())
                {
                    var prevRaw = SelectedSpec.Raw;

                    OptionCollectionHelper.ReplaceRaw(SpecOptions, specs, StringComparison.Ordinal);
                    if (SpecOptions.Count == 0 || !string.IsNullOrWhiteSpace(SpecOptions[0].Raw))
                    {
                        SpecOptions.Insert(0, AllSpec);
                    }

                    SelectedSpec = string.IsNullOrWhiteSpace(prevRaw)
                        ? AllSpec
                        : (SpecOptions.FirstOrDefault(x => x.Raw == prevRaw) ?? AllSpec);
                }
            }, DispatcherPriority.Background);
        }
        catch
        {
            await RunOnUiAsync(() =>
            {
                if (SpecOptions.Count == 0)
                {
                    SpecOptions.Add(AllSpec);
                    SelectedSpec = AllSpec;
                }
            });
        }
    }

    public DashboardKpiModel Kpi { get; } = new();
    public string SectionHint => BuildRangeMeta(CurrentRange, SelectedClient);

    public ObservableCollection<TrendDrugItem> DrugTrend { get; } = new();
    public ObservableCollection<TrendDrugItem> TxnTrendRows { get; } = new();
    [ObservableProperty] private TrendDrugItem? _selectedTrendItem;
    [ObservableProperty] private bool _isTrendChartVisible;
    public string TrendViewToggleText => IsTrendChartVisible ? "数据框" : "图表";
    public string TrendViewToggleIcon => IsTrendChartVisible ? "Table2" : "ChartNoAxesCombined";

    partial void OnIsTrendChartVisibleChanged(bool value)
    {
        OnPropertyChanged(nameof(TrendViewToggleText));
        OnPropertyChanged(nameof(TrendViewToggleIcon));
    }
    [ObservableProperty] private TxnItem? _selectedTxn;
    [ObservableProperty] private EntryRecentItem? _selectedEntryRecent;
    [ObservableProperty] private AbnormalItem? _selectedAbnormal;

    public ObservableCollection<SimpleModeItem> TrendModes { get; } = new()
    {
        new("按使用量"),
        new("按事务次数")
    };

    [ObservableProperty] private SimpleModeItem? _trendMode;
    partial void OnTrendModeChanged(SimpleModeItem? value) => RequestReload();

    public ObservableCollection<TopClientItem> TopClients { get; } = new();
    public ObservableCollection<int> TabPageSizeOptions { get; } = new(TabPageSizeOptionValues);

    public ObservableCollection<SimpleModeItem> ClientMetricModes { get; } = new()
    {
        new("按使用量")
    };

    [ObservableProperty] private SimpleModeItem? _clientMetricMode;
    partial void OnClientMetricModeChanged(SimpleModeItem? value) => RequestReload();

    public ObservableCollection<TxnItem> RecentTxns { get; } = new();
    public ObservableCollection<TxnItem> RecentTxnsOverview { get; } = new();
    [ObservableProperty] private bool _isTxnBusy;
    [ObservableProperty] private int _txnPageIndex = 1;
    [ObservableProperty] private int _txnTotalCount;
    [ObservableProperty] private int _tabPageSize = 50;
    public int TxnPageSize => TabPageSize;
    public int TxnTotalPages => Math.Max(1, (int)Math.Ceiling(TxnTotalCount / (double)TxnPageSize));
    public bool HasTxnPrevPage => TxnPageIndex > 1;
    public bool HasTxnNextPage => TxnPageIndex < TxnTotalPages;

    public ObservableCollection<SimpleModeItem> TxnPanelModes { get; } = new()
    {
        new("事务明细"),
        new("事务趋势")
    };

    [ObservableProperty] private SimpleModeItem? _txnPanelMode;
    public bool IsTxnPanelDetailMode => TxnPanelMode?.Title != "事务趋势";
    public bool IsTxnPanelTrendMode => TxnPanelMode?.Title == "事务趋势";
    public bool IsTxnPanelEmpty => IsRecentTxnsEmpty && IsTxnTrendEmpty;

    partial void OnTxnPanelModeChanged(SimpleModeItem? value)
    {
        OnPropertyChanged(nameof(IsTxnPanelDetailMode));
        OnPropertyChanged(nameof(IsTxnPanelTrendMode));
        OnPropertyChanged(nameof(IsTxnPanelEmpty));

        if (IsTxnPanelTrendMode && TxnTrendRows.Count == 0 && !IsTxnBusy)
            _ = ReloadTxnTrendPageOnlyAsync();
    }

    partial void OnTxnPageIndexChanged(int value)
    {
        OnPropertyChanged(nameof(HasTxnPrevPage));
        OnPropertyChanged(nameof(HasTxnNextPage));
    }

    partial void OnTxnTotalCountChanged(int value)
    {
        OnPropertyChanged(nameof(TxnTotalPages));
        OnPropertyChanged(nameof(HasTxnPrevPage));
        OnPropertyChanged(nameof(HasTxnNextPage));
        OnPropertyChanged(nameof(IsRecentTxnsEmpty));
        OnPropertyChanged(nameof(IsTxnPanelEmpty));
    }

    [ObservableProperty] private int _txnTrendPageIndex = 1;
    [ObservableProperty] private int _txnTrendTotalCount;
    public int TxnTrendPageSize => TabPageSize;
    public int TxnTrendTotalPages => Math.Max(1, (int)Math.Ceiling(TxnTrendTotalCount / (double)TxnTrendPageSize));
    public bool HasTxnTrendPrevPage => TxnTrendPageIndex > 1;
    public bool HasTxnTrendNextPage => TxnTrendPageIndex < TxnTrendTotalPages;

    partial void OnTxnTrendPageIndexChanged(int value)
    {
        OnPropertyChanged(nameof(HasTxnTrendPrevPage));
        OnPropertyChanged(nameof(HasTxnTrendNextPage));
    }

    partial void OnTxnTrendTotalCountChanged(int value)
    {
        OnPropertyChanged(nameof(TxnTrendTotalPages));
        OnPropertyChanged(nameof(HasTxnTrendPrevPage));
        OnPropertyChanged(nameof(HasTxnTrendNextPage));
        OnPropertyChanged(nameof(IsTxnTrendEmpty));
        OnPropertyChanged(nameof(IsTxnPanelEmpty));
    }

    public ObservableCollection<EntryRecentItem> EntryRecentOverview { get; } = new();
    public ObservableCollection<EntryRecentItem> EntryRecent { get; } = new();
    [ObservableProperty] private bool _isEntryBusy;
    [ObservableProperty] private int _entryPageIndex = 1;
    [ObservableProperty] private int _entryTotalCount;
    public int EntryPageSize => TabPageSize;
    public int EntryTotalPages => Math.Max(1, (int)Math.Ceiling(EntryTotalCount / (double)EntryPageSize));
    public bool HasEntryPrevPage => EntryPageIndex > 1;
    public bool HasEntryNextPage => EntryPageIndex < EntryTotalPages;

    partial void OnEntryPageIndexChanged(int value)
    {
        OnPropertyChanged(nameof(HasEntryPrevPage));
        OnPropertyChanged(nameof(HasEntryNextPage));
    }

    partial void OnEntryTotalCountChanged(int value)
    {
        OnPropertyChanged(nameof(EntryTotalPages));
        OnPropertyChanged(nameof(HasEntryPrevPage));
        OnPropertyChanged(nameof(HasEntryNextPage));
        OnPropertyChanged(nameof(IsEntryRecentEmpty));
    }

    public ObservableCollection<AbnormalItem> AbnormalQueue { get; } = new();
    [ObservableProperty] private bool _isAbnormalBusy;
    [ObservableProperty] private int _abnormalPageIndex = 1;
    [ObservableProperty] private int _abnormalTotalCount;
    public int AbnormalPageSize => TabPageSize;
    public int AbnormalTotalPages => Math.Max(1, (int)Math.Ceiling(AbnormalTotalCount / (double)AbnormalPageSize));
    public bool HasAbnormalPrevPage => AbnormalPageIndex > 1;
    public bool HasAbnormalNextPage => AbnormalPageIndex < AbnormalTotalPages;

    partial void OnAbnormalPageIndexChanged(int value)
    {
        OnPropertyChanged(nameof(HasAbnormalPrevPage));
        OnPropertyChanged(nameof(HasAbnormalNextPage));
    }

    partial void OnAbnormalTotalCountChanged(int value)
    {
        OnPropertyChanged(nameof(AbnormalTotalPages));
        OnPropertyChanged(nameof(HasAbnormalPrevPage));
        OnPropertyChanged(nameof(HasAbnormalNextPage));
        OnPropertyChanged(nameof(IsAbnormalEmpty));
    }

    protected override void OnLookupCatalogSuspended()
    {
        using (SuppressReload())
        {
            DrugOptions.Clear();
            _drugCatalog = [];
            IsDrugSuggestOpen = false;
            DrugText = null;
            EnsureAllSpecOnly();
        }
    }

    protected override void OnPageAvailabilityChanged()
    {
        OnPropertyChanged(nameof(IsTrendEmpty));
        OnPropertyChanged(nameof(TrendEmptyText));
        OnPropertyChanged(nameof(TrendEmptyHint));
        OnPropertyChanged(nameof(IsTxnTrendEmpty));
        OnPropertyChanged(nameof(TxnTrendEmptyText));
        OnPropertyChanged(nameof(TxnTrendEmptyHint));
        OnPropertyChanged(nameof(IsTopClientsEmpty));
        OnPropertyChanged(nameof(TopClientsEmptyText));
        OnPropertyChanged(nameof(TopClientsEmptyHint));
        OnPropertyChanged(nameof(IsRecentTxnsEmpty));
        OnPropertyChanged(nameof(RecentTxnsEmptyText));
        OnPropertyChanged(nameof(RecentTxnsEmptyHint));
        OnPropertyChanged(nameof(IsEntryRecentEmpty));
        OnPropertyChanged(nameof(EntryRecentEmptyText));
        OnPropertyChanged(nameof(EntryRecentEmptyHint));
        OnPropertyChanged(nameof(AbnormalEmptyText));
        OnPropertyChanged(nameof(AbnormalEmptyHint));
        OnPropertyChanged(nameof(IsAbnormalEmpty));
        OnPropertyChanged(nameof(IsTxnPanelEmpty));
        OnPropertyChanged(nameof(TxnPanelEmptyText));
        OnPropertyChanged(nameof(TxnPanelEmptyHint));
    }

    public string TrendEmptyText => GetSectionEmptyTitle("期间无使用情况");
    public string TrendEmptyHint => GetSectionEmptyHint("当前筛选条件下没有追溯码使用记录");
    public string TxnTrendEmptyText => GetSectionEmptyTitle("期间无趋势");
    public string TxnTrendEmptyHint => GetSectionEmptyHint("当前筛选条件下没有取码事务趋势");
    public string TopClientsEmptyText => GetSectionEmptyTitle("暂无客户端使用记录");
    public string TopClientsEmptyHint => GetSectionEmptyHint("当前时间范围内没有任何客户端操作");
    public string RecentTxnsEmptyText => GetSectionEmptyTitle("期间无事务");
    public string RecentTxnsEmptyHint => GetSectionEmptyHint("当前筛选条件下没有取码事务记录");
    public string EntryRecentEmptyText => GetSectionEmptyTitle("暂无录入记录");
    public string EntryRecentEmptyHint => GetSectionEmptyHint("期间内未发生追溯码录入");
    public string TxnPanelEmptyText => GetSectionEmptyTitle("暂无事务数据");
    public string TxnPanelEmptyHint => GetSectionEmptyHint("当前筛选条件下没有取码事务记录");
    public string AbnormalEmptyText => GetSectionEmptyTitle("暂无异常队列");
    public string AbnormalEmptyHint => GetSectionEmptyHint("当前筛选条件下没有回滚/异常事务");

    public bool IsTrendEmpty => ShouldShowSectionEmpty(DrugTrend.Count == 0);
    public bool IsTxnTrendEmpty => ShouldShowSectionEmpty(TxnTrendTotalCount == 0);
    public bool IsTopClientsEmpty => ShouldShowSectionEmpty(TopClients.Count == 0);
    public bool IsRecentTxnsEmpty => ShouldShowSectionEmpty(TxnTotalCount == 0);
    public bool IsEntryRecentEmpty => ShouldShowSectionEmpty(EntryTotalCount == 0);
    public bool IsAbnormalEmpty => ShouldShowSectionEmpty(AbnormalTotalCount == 0);

    private DispatcherTimer? _debounce;
    private bool _debounceHooked;
    private int _suppressReloadCount;
    private bool IsReloadSuppressed => _suppressReloadCount > 0;

    private bool _firstLoadTriggered;
    private bool _filtersLoaded;

    public DashboardViewModel(IDashboardService dashboard, ILookupCatalogService lookup, IToastService toast,
        IClientAliasService clientAlias, PageNavigationService nav, InventoryOverviewViewModel inventoryOverview)
    {
        _dashboard = dashboard;
        _toast = toast;
        _clientAlias = clientAlias;
        _lookup = lookup;
        _nav = nav;
        _inventoryOverview = inventoryOverview;
        _dateRangeController = new RollingDateRangeController(() =>
            PostOnUi(HandleDateRangeDayChanged, DispatcherPriority.Background));

        Clients.Clear();
        Clients.Add(AllClients);

        DrugOptions.Clear();
        _drugCatalog = [];

        SpecOptions.Clear();
        SpecOptions.Add(AllSpec);

        using (SuppressReload())
        {
            var normalized = RollingDateRangeController.Normalize(FromDate, ToDate);
            FromDate = normalized.From;
            ToDate = normalized.To;
            DrugText = null;
            SelectedSpec = AllSpec;
            SelectedClient = AllClients;
            TrendMode = TrendModes.FirstOrDefault();
            ClientMetricMode = ClientMetricModes.FirstOrDefault();
            TxnPanelMode = TxnPanelModes.FirstOrDefault();
        }

        PostOnUi(() => _ = InitializeAsync(), DispatcherPriority.Loaded);

        _clientAlias.Changed += OnClientAliasChanged;
    }

    private sealed class ActionOnDispose : IDisposable
    {
        private Action? _action;
        public ActionOnDispose(Action action) => _action = action;

        public void Dispose()
        {
            var a = Interlocked.Exchange(ref _action, null);
            a?.Invoke();
        }
    }

    private async Task InitializeAsync()
    {
        if (_firstLoadTriggered)
        {
            return;
        }

        _firstLoadTriggered = true;

        try
        {
            if (!_filtersLoaded)
            {
                _filtersLoaded = true;

                if (!IsDbAccessBlocked(out _))
                {
                    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(8));
                    await ReloadDrugOptionsAsync(cts.Token);

                    if (!string.IsNullOrWhiteSpace(DrugText))
                    {
                        await ReloadSpecsAsync(NormalizeInput(DrugText)!);
                    }
                    else
                    {
                        await RunOnUiAsync(EnsureAllSpecOnly, DispatcherPriority.Background);
                    }
                }
            }
        }
        catch (System.Exception ex)
        {
            LogWarn("dashboard.init.filters_fail", "Failed to initialize dashboard filters", ex);
        }

        await ReloadNow();
    }

    private IDisposable SuppressReload()
    {
        _suppressReloadCount++;
        return new ActionOnDispose(() => _suppressReloadCount--);
    }

    private void ResetPagedIndexes()
    {
        using (SuppressReload())
        {
            TxnPageIndex = 1;
            TxnTrendPageIndex = 1;
            EntryPageIndex = 1;
            AbnormalPageIndex = 1;
        }
    }

    private void RequestReload()
    {
        if (IsReloadSuppressed)
        {
            return;
        }

        _debounce ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(180) };

        if (!_debounceHooked)
        {
            _debounce.Tick += OnDebounceTimerTick;
            _debounceHooked = true;
        }

        _debounce.Stop();
        _debounce.Start();
    }

    private Task ReloadNow() => RunLocalReloadAsync(_ => { }, RefreshAllAsync);

    private void OnDebounceTimerTick(object? sender, EventArgs e)
    {
        _debounce?.Stop();
        _ = ReloadNow();
    }

    private void EnsureCurrentTabDataLoaded()
    {
        switch (SelectedTabIndex)
        {
            case 1 when EntryRecent.Count == 0 && !IsEntryBusy:
                _ = ReloadEntryPageOnlyAsync();
                break;
            case 2 when !IsTxnBusy:
                if (IsTxnPanelTrendMode)
                {
                    if (TxnTrendRows.Count == 0)
                    {
                        _ = ReloadTxnTrendPageOnlyAsync();
                    }
                }
                else if (RecentTxns.Count == 0)
                {
                    _ = ReloadTxnPageOnlyAsync();
                }
                break;
            case 3 when AbnormalQueue.Count == 0 && !IsAbnormalBusy:
                _ = ReloadAbnormalPageOnlyAsync();
                break;
        }
    }

    protected override Task ReloadCoreAsync(CancellationToken ct)
        => RefreshAllAsync(ct);

    partial void OnFromDateChanged(DateTime? value)
    {
        ApplyDateRangeFromBoundary(value, updateFromBoundary: true);
    }

    partial void OnToDateChanged(DateTime? value)
    {
        ApplyDateRangeFromBoundary(value, updateFromBoundary: false);
    }

    // Keep date range valid and trigger a single reload path for both boundaries.
    private void ApplyDateRangeFromBoundary(DateTime? value, bool updateFromBoundary)
    {
        var today = DateTime.Today;

        if (value is not null && value.Value.Date > today)
        {
            var clamped = today;
            using (SuppressReload())
            {
                if (updateFromBoundary)
                {
                    FromDate = clamped;
                }
                else
                {
                    ToDate = clamped;
                }
            }

            value = clamped;
        }

        OnPropertyChanged(nameof(SectionHint));
        OnPropertyChanged(nameof(ToMaxDate));
        OnPropertyChanged(updateFromBoundary ? nameof(ToMinDate) : nameof(FromMaxDate));

        if (updateFromBoundary && value is not null && ToDate is not null && value.Value.Date > ToDate.Value.Date)
        {
            using (SuppressReload())
            {
                ToDate = value.Value.Date;
            }

            OnPropertyChanged(nameof(FromMaxDate));
        }
        else if (!updateFromBoundary && value is not null && FromDate is not null && value.Value.Date < FromDate.Value.Date)
        {
            using (SuppressReload())
            {
                FromDate = value.Value.Date;
            }

            OnPropertyChanged(nameof(ToMinDate));
        }

        if (IsReloadSuppressed)
        {
            return;
        }

        RequestReloadWithPagingReset();
    }

    [RelayCommand]
    private void ResetDateRange()
    {
        var defaults = RollingDateRangeController.Normalize(
            RollingDateRangeController.DefaultFromDate,
            RollingDateRangeController.DefaultToDate);
        using (SuppressReload())
        {
            FromDate = defaults.From;
            ToDate = defaults.To;
        }

        OnPropertyChanged(nameof(SectionHint));
        OnPropertyChanged(nameof(ToMinDate));
        OnPropertyChanged(nameof(FromMaxDate));
        OnPropertyChanged(nameof(ToMaxDate));
        RequestReloadWithPagingReset();
    }

    [RelayCommand(CanExecute = nameof(CanClearDrugSpecFilter))]
    private async Task ClearDrugSpecFilterAsync()
    {
        if (ShouldSkipTrigger("dashboard.filter.clear", 350))
        {
            return;
        }

        IsDrugSuggestOpen = false;
        ResetPagedIndexes();

        await RunOnUiAsync(() =>
        {
            using var _ = SuppressReload();
            DrugText = null;
            EnsureAllSpecOnly();
            SelectedClient = AllClients;
        }, DispatcherPriority.Background);

        await ReloadNow();
    }

    private bool CanClearDrugSpecFilter()
        => DrugAutoCompleteFilterPolicy.HasDrugText(DrugText);

    private void HandleDateRangeDayChanged()
    {
        var defaults = RollingDateRangeController.Normalize(
            RollingDateRangeController.DefaultFromDate,
            RollingDateRangeController.DefaultToDate);
        using (SuppressReload())
        {
            FromDate = defaults.From;
            ToDate = defaults.To;
        }

        OnPropertyChanged(nameof(SectionHint));
        OnPropertyChanged(nameof(ToMinDate));
        OnPropertyChanged(nameof(FromMaxDate));
        OnPropertyChanged(nameof(ToMaxDate));
        RequestReloadWithPagingReset();
    }

    private void RequestReloadWithPagingReset()
    {
        // Filters and date changes always reset paging to first page.
        ResetPagedIndexes();
        RequestReload();
    }

    partial void OnSelectedClientChanged(ClientInfo? value)
    {
        OnPropertyChanged(nameof(SectionHint));

        if (IsReloadSuppressed)
            return;

        ResetPagedIndexes();
        RequestReload();
    }

    public async Task HandleTrendRowSelectedAsync(TrendDrugItem? item)
    {
        if (_suppressRowSelectionAction || item is null)
        {
            return;
        }

        await ApplyDrugSpecFilterAndReloadAsync(item.Name, item.Sub);
    }

    public async Task HandleRecentTxnRowSelectedAsync(TxnItem? item)
    {
        if (_suppressRowSelectionAction || item is null)
        {
            return;
        }

        await ApplyDrugSpecFilterAndReloadAsync(item.DrugId, item.Spec);

        using (SuppressReload())
        {
            TxnPanelMode = TxnPanelModes.FirstOrDefault();
        }

        SelectedTabIndex = 2;

        await RunOnUiAsync(() =>
        {
            using var _ = SuppressReload();
            SelectedTxn = RecentTxns.FirstOrDefault(x =>
                x.Id == item.Id &&
                string.Equals(x.Title, item.Title, StringComparison.Ordinal) &&
                string.Equals(x.Time, item.Time, StringComparison.Ordinal) &&
                string.Equals(x.Qty, item.Qty, StringComparison.Ordinal)) ?? item;
        }, DispatcherPriority.Background);
    }

    public async Task HandleEntryRecentRowSelectedAsync(EntryRecentItem? item)
    {
        if (_suppressRowSelectionAction || item is null)
        {
            return;
        }

        await ApplyDrugSpecFilterAndReloadAsync(item.DrugId, item.Spec);

        using var _ = SuppressReload();
        SelectedTabIndex = 1;
    }

    public async Task HandleTopClientRowSelectedAsync(TopClientItem? item)
    {
        if (item is null)
        {
            return;
        }

        var target = FindClientOption(item.Client);
        if (target is null)
        {
            return;
        }

        await RunOnUiAsync(() =>
        {
            using var _ = SuppressReload();
            SelectedClient = target;
        });

        await ReloadNow();
    }

    public async Task HandleAbnormalRowSelectedAsync(AbnormalItem? item)
    {
        if (_suppressRowSelectionAction || item is null)
        {
            return;
        }

        var parsed = DashboardDrugSpecParser.TryParseFromAbnormalDetail(item.Detail);
        if (parsed is not null)
        {
            await ApplyDrugSpecFilterAndReloadAsync(parsed.Value.DrugId, parsed.Value.Spec);
        }

        if (DashboardDrugSpecParser.IsInventoryAbnormalTitle(item.Title))
        {
            _inventoryOverview.OpenMode(2);
            _nav.Navigate<InventoryOverviewViewModel>();
            return;
        }

        using var _ = SuppressReload();
        TxnPanelMode = TxnPanelModes.FirstOrDefault();
        SelectedTabIndex = 2;
    }

    public void SuppressRowSelectionActionScope(bool suppress)
        => _suppressRowSelectionAction = suppress;

    public void ClearBrowsingSelections()
    {
        _suppressRowSelectionAction = true;
        try
        {
            SelectedTrendItem = null;
            SelectedTxn = null;
            SelectedEntryRecent = null;
            SelectedAbnormal = null;
        }
        finally
        {
            _suppressRowSelectionAction = false;
        }
    }

    private DashboardFilter CurrentFilter
    {
        get
        {
            var range = CurrentRange;
            var raw = SelectedClient?.Raw;
            var client = string.IsNullOrWhiteSpace(raw) ? null : raw;
            var metric = TrendMode?.Title == "按事务次数"
                ? TrendMetric.Txn
                : TrendMetric.Qty;
            var drug = NormalizeInput(DrugText);
            var spec = string.IsNullOrWhiteSpace(SelectedSpec.Raw) ? null : SelectedSpec.Raw;
            return new DashboardFilter(range.From, range.To, client, drug, spec, metric);
        }
    }

    private DateRange CurrentRange
    {
        get
        {
            var from = DateOnly.FromDateTime(FromDate ?? DateTime.Today.AddDays(-6));
            var to = DateOnly.FromDateTime(ToDate ?? DateTime.Today);
            return new DateRange(from, to);
        }
    }

    private async Task RefreshAllAsync(CancellationToken ct)
    {
        var showTxnBusy = ShouldShowTxnBusy();
        var showEntryBusy = ShouldShowEntryBusy();
        var showAbnormalBusy = ShouldShowAbnormalBusy();
        var showAnyBusy = showTxnBusy || showEntryBusy || showAbnormalBusy;

        try
        {
            await RunLocalBusyAsync(
                ct,
                setBusy: v =>
                {
                    if (showTxnBusy)
                    {
                        IsTxnBusy = v;
                    }

                    if (showEntryBusy)
                    {
                        IsEntryBusy = v;
                    }

                    if (showAbnormalBusy)
                    {
                        IsAbnormalBusy = v;
                    }
                },
                showBusy: showAnyBusy,
                body: async () =>
            {
                if (DrugOptions.Count == 0)
                {
                    try
                    {
                        await ReloadDrugOptionsAsync(ct).ConfigureAwait(false);
                        if (!string.IsNullOrWhiteSpace(DrugText))
                        {
                            await ReloadSpecsAsync(NormalizeInput(DrugText)!).ConfigureAwait(false);
                        }
                    }
                    catch (System.Exception ex)
                    {
                        LogWarn("dashboard.reload.lazy_filters_fail", "Failed lazy loading filters during reload", ex);
                    }
                }

                var request = new DashboardLoadRequest(
                    Filter: CurrentFilter,
                    OverviewTopN: DefaultTopN,
                    EntryOverviewTopN: EntryOverviewTopN,
                    TxnPageIndex: TxnPageIndex,
                    TxnPageSize: TxnPageSize,
                    TxnTrendPageIndex: TxnTrendPageIndex,
                    TxnTrendPageSize: TxnTrendPageSize,
                    EntryPageIndex: EntryPageIndex,
                    EntryPageSize: EntryPageSize,
                    AbnormalPageIndex: AbnormalPageIndex,
                    AbnormalPageSize: AbnormalPageSize);

                var loaded = await _dashboard.LoadSnapshotAsync(request, ct).ConfigureAwait(false);

                var trendItems = BuildTrendItems(loaded.Trend);
                var recentOverviewItems = BuildRecentTxnsOverviewItems(loaded.TxnsOverview.Rows);
                var recentTxnPageItems = BuildRecentTxnsPageItems(loaded.TxnsPage.Rows, TxnPageIndex, TxnPageSize);
                var txnTrendPageItems = BuildTxnTrendPageItems(loaded.TxnTrendPage.Rows, TxnTrendPageIndex, TxnTrendPageSize);
                var entryOverviewItems = BuildEntryLogsOverviewItems(loaded.EntriesOverview.Rows);
                var entryPageItems = BuildEntryLogsPageItems(loaded.EntriesPage.Rows, EntryPageIndex, EntryPageSize);
                var topClientItems = BuildTopClientItems(loaded.TopClients);
                var abnormalItems = BuildAbnormalQueueItems(loaded.Abnormal.Rows, AbnormalPageIndex, AbnormalPageSize);

                await RunOnUiAsync(() =>
                {
                    ApplyClients(loaded.ClientNames);

                    ApplyKpi(loaded.Kpi);
                    ApplyTrend(trendItems);
                    ApplyRecentTxnsOverview(recentOverviewItems);
                    ApplyRecentTxnsPage(recentTxnPageItems, loaded.TxnsPage.TotalCount);
                    ApplyTxnTrendPage(txnTrendPageItems, loaded.TxnTrendPage.TotalCount);
                    ApplyEntryLogsOverview(entryOverviewItems);
                    ApplyEntryLogsPage(entryPageItems, loaded.EntriesPage.TotalCount);
                    ApplyTopClients(topClientItems);
                    ApplyAbnormalQueue(abnormalItems, loaded.Abnormal.TotalCount);

                    OnPropertyChanged(nameof(IsTrendEmpty));
                    OnPropertyChanged(nameof(IsTopClientsEmpty));
                    OnPropertyChanged(nameof(IsRecentTxnsEmpty));
                    OnPropertyChanged(nameof(IsEntryRecentEmpty));
                    OnPropertyChanged(nameof(IsAbnormalEmpty));
                    OnPropertyChanged(nameof(IsTxnPanelEmpty));
                }, DispatcherPriority.Background);
            });
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            LogError("dashboard.reload.fail", "Failed to reload dashboard", ex);
            if (ct.IsCancellationRequested || !ShouldShowOperationErrorToast(ex))
            {
                return;
            }

            PostOnUi(() => _toast.Error("概览加载失败", ex.Message));
        }
    }

    private bool ShouldShowTxnBusy()
        => RecentTxns.Count == 0 && RecentTxnsOverview.Count == 0 && DrugTrend.Count == 0 && TopClients.Count == 0;

    private bool ShouldShowEntryBusy()
        => EntryRecent.Count == 0 && EntryRecentOverview.Count == 0;

    private bool ShouldShowAbnormalBusy()
        => AbnormalQueue.Count == 0;

    private void ApplyClients(IReadOnlyList<string> list)
    {
        var selectedRaw = SelectedClient?.Raw ?? string.Empty;

        using (SuppressReload())
        {
            Clients.Clear();
            Clients.Add(AllClients);

            foreach (var raw in list.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct())
            {
                Clients.Add(ClientDisplayResolver.Resolve(raw, _clientAlias));
            }

            SelectedClient = Clients.FirstOrDefault(c => c.Raw == selectedRaw) ?? AllClients;
        }
    }

    private ClientInfo ResolveClient(string raw)
        => ClientDisplayResolver.Resolve(raw, _clientAlias);

    [RelayCommand]
    private async Task ApplyDrugFilterAsync()
    {
        if (ShouldSkipTrigger("dashboard.filter.apply", 350))
        {
            return;
        }

        IsDrugSuggestOpen = false;
        ResetPagedIndexes();

        var drug = NormalizeInput(DrugText);

        if (string.IsNullOrWhiteSpace(drug))
        {
            EnsureAllSpecOnly();
        }
        else
        {
            await ReloadSpecsAsync(drug);
            await RunOnUiAsync(() =>
            {
                using var _ = SuppressReload();
                SelectedSpec = AllSpec;
            }, DispatcherPriority.Background);
        }

        await ReloadNow();
    }

    private void ApplyKpi(DashboardKpiDto dto)
    {
        Kpi.AvailableRemain = dto.AvailableRemain.ToString("N0", CultureInfo.CurrentCulture);
        Kpi.PeriodUsed = dto.PeriodUsed.ToString("N0", CultureInfo.CurrentCulture);
        Kpi.LowStockCount = dto.LowStockCount.ToString("N0", CultureInfo.CurrentCulture);
        Kpi.Abnormal = dto.Abnormal.ToString("N0", CultureInfo.CurrentCulture);

        static double Pct(long num, long den)
        {
            if (den <= 0)
            {
                return 0;
            }

            var v = num * 100.0 / den;
            if (v < 0)
            {
                return 0;
            }

            if (v > 100)
            {
                return 100;
            }

            return v;
        }

        static double PctRemainHealth(long remain, long used)
        {
            var active = remain + used;
            if (active <= 0)
            {
                return remain > 0 ? 100 : 0;
            }

            return Pct(remain, active);
        }

        static double PctUsageIntensity(long remain, long used)
        {
            var active = remain + used;
            if (active <= 0)
            {
                return used > 0 ? 100 : 0;
            }

            return Pct(used, active);
        }

        Kpi.AvailableRemainPct = PctRemainHealth(dto.AvailableRemain, dto.PeriodUsed);
        Kpi.PeriodUsedPct = PctUsageIntensity(dto.AvailableRemain, dto.PeriodUsed);
        Kpi.AbnormalPct = Pct(dto.Abnormal, dto.TotalTxnCount);

        if (dto.SelectedPoolCount is { } n && dto.SelectedZeroRemainCount is { } m)
        {
            Kpi.LowStockPct = Pct(m, n);

            Kpi.LowStockCount = m.ToString("N0", CultureInfo.CurrentCulture);
        }
        else
        {
            Kpi.LowStockPct = Pct(dto.LowStockCount, dto.TotalDrugCount);
            Kpi.LowStockCount = dto.LowStockCount.ToString("N0", CultureInfo.CurrentCulture);
        }
    }

    private List<TrendDrugItem> BuildTrendItems(IReadOnlyList<TrendRowDto> rows)
    {
        var items = new List<TrendDrugItem>(rows.Count);
        foreach (var r in rows)
        {
            items.Add(new TrendDrugItem
            {
                Rank = r.Rank.ToString(CultureInfo.CurrentCulture),
                Name = r.Name,
                Sub = r.Sub,
                SourceText = BuildTrendSourceText(r.TopClientRaw, r.TopClientPct),
                ValueText = r.ValueText
            });
        }

        return items;
    }

    private void ApplyTrend(IReadOnlyList<TrendDrugItem> items)
    {
        DrugTrend.ReplaceAll(items);
        OnPropertyChanged(nameof(IsTrendEmpty));
    }

    private List<TrendDrugItem> BuildTxnTrendPageItems(
        IReadOnlyList<TrendRowDto> rows,
        int pageIndex,
        int pageSize)
    {
        var items = new List<TrendDrugItem>(rows.Count);
        var start = ((pageIndex - 1) * pageSize) + 1;
        var idx = 0;
        foreach (var r in rows)
        {
            items.Add(new TrendDrugItem
            {
                DisplayIndex = start + idx++,
                Rank = r.Rank.ToString(CultureInfo.CurrentCulture),
                Name = r.Name,
                Sub = r.Sub,
                SourceText = BuildTrendSourceText(r.TopClientRaw, r.TopClientPct),
                ValueText = r.ValueText
            });
        }

        return items;
    }

    private void ApplyTxnTrendPage(IReadOnlyList<TrendDrugItem> items, int totalCount)
    {
        TxnTrendRows.ReplaceAll(items);
        TxnTrendTotalCount = totalCount;
        OnPropertyChanged(nameof(IsTxnTrendEmpty));
        OnPropertyChanged(nameof(IsTxnPanelEmpty));
    }

    private List<TopClientItem> BuildTopClientItems(IReadOnlyList<(string Client, long Value)> rows)
    {
        var items = new List<TopClientItem>(rows.Count);
        var idx = 1;
        foreach (var r in rows)
        {
            var client = ResolveClient(r.Client);
            items.Add(new TopClientItem(
                Index: idx++,
                Client: client,
                Value: r.Value.ToString("N0", CultureInfo.CurrentCulture)
            ));
        }

        return items;
    }

    private void ApplyTopClients(IReadOnlyList<TopClientItem> items)
    {
        TopClients.ReplaceAll(items);
        OnPropertyChanged(nameof(IsTopClientsEmpty));
    }

    private List<TxnItem> BuildRecentTxnsOverviewItems(IReadOnlyList<TraceTxnDto> rows)
    {
        var items = new List<TxnItem>(rows.Count);
        var idx = 1;
        foreach (var t in rows)
        {
            items.Add(new TxnItem(
                DisplayIndex: idx++,
                Id: t.Id,
                Badge: t.Badge,
                DrugId: t.DrugId,
                Spec: t.Spec,
                Qty: t.Qty.ToString("N0", CultureInfo.CurrentCulture),
                Time: t.CreatedAt.ToLocalTime().ToString("MM-dd HH:mm:ss", CultureInfo.CurrentCulture),
                ClientDisplay: string.IsNullOrWhiteSpace(t.ClientName) ? "-" : t.ClientName
            ));
        }

        return items;
    }

    private void ApplyRecentTxnsOverview(IReadOnlyList<TxnItem> items)
        => RecentTxnsOverview.ReplaceAll(items);

    private List<TxnItem> BuildRecentTxnsPageItems(
        IReadOnlyList<TraceTxnDto> rows,
        int pageIndex,
        int pageSize)
    {
        var items = new List<TxnItem>(rows.Count);
        var start = ((pageIndex - 1) * pageSize) + 1;
        var idx = 0;
        foreach (var t in rows)
        {
            items.Add(new TxnItem(
                DisplayIndex: start + idx++,
                Id: t.Id,
                Badge: t.Badge,
                DrugId: t.DrugId,
                Spec: t.Spec,
                Qty: t.Qty.ToString("N0", CultureInfo.CurrentCulture),
                Time: t.CreatedAt.ToLocalTime().ToString("MM-dd HH:mm:ss", CultureInfo.CurrentCulture),
                ClientDisplay: string.IsNullOrWhiteSpace(t.ClientName) ? "-" : t.ClientName
            ));
        }

        return items;
    }

    private void ApplyRecentTxnsPage(IReadOnlyList<TxnItem> items, int totalCount)
    {
        RecentTxns.ReplaceAll(items);
        TxnTotalCount = totalCount;
        OnPropertyChanged(nameof(IsRecentTxnsEmpty));
        OnPropertyChanged(nameof(IsTxnPanelEmpty));
    }

    private List<EntryRecentItem> BuildEntryLogsOverviewItems(IReadOnlyList<TraceEntryLogDto> rows)
    {
        var items = new List<EntryRecentItem>(rows.Count);
        var idx = 1;
        foreach (var e in rows.OrderByDescending(x => x.EntryAt))
        {
            var client = ResolveClient(e.Client);
            items.Add(EntryRecentItem.From(e, client, idx++));
        }

        return items;
    }

    private void ApplyEntryLogsOverview(IReadOnlyList<EntryRecentItem> items)
        => EntryRecentOverview.ReplaceAll(items);

    private List<EntryRecentItem> BuildEntryLogsPageItems(
        IReadOnlyList<TraceEntryLogDto> rows,
        int pageIndex,
        int pageSize)
    {
        var items = new List<EntryRecentItem>(rows.Count);
        var start = ((pageIndex - 1) * pageSize) + 1;
        var idx = 0;
        foreach (var e in rows.OrderByDescending(x => x.EntryAt))
        {
            var client = ResolveClient(e.Client);
            items.Add(EntryRecentItem.From(e, client, start + idx++));
        }

        return items;
    }

    private void ApplyEntryLogsPage(IReadOnlyList<EntryRecentItem> items, int totalCount)
    {
        EntryRecent.ReplaceAll(items);
        EntryTotalCount = totalCount;
        OnPropertyChanged(nameof(IsEntryRecentEmpty));
    }

    private List<AbnormalItem> BuildAbnormalQueueItems(
        IReadOnlyList<AbnormalRowDto> rows,
        int pageIndex,
        int pageSize)
    {
        var items = new List<AbnormalItem>(rows.Count);
        var start = ((pageIndex - 1) * pageSize) + 1;
        var idx = 0;
        foreach (var row in rows)
        {
            items.Add(new AbnormalItem(
                DisplayIndex: start + idx++,
                Title: row.Title,
                Detail: row.Detail,
                ClientDisplay: string.IsNullOrWhiteSpace(row.ClientDisplay) ? "-" : row.ClientDisplay,
                Badge: row.Badge
            ));
        }

        return items;
    }

    private void ApplyAbnormalQueue(IReadOnlyList<AbnormalItem> items, int totalCount)
    {
        AbnormalQueue.ReplaceAll(items);
        AbnormalTotalCount = totalCount;
        OnPropertyChanged(nameof(IsAbnormalEmpty));
    }

    private static string BuildTrendSourceText(string? rawClient, decimal pct)
    {
        if (string.IsNullOrWhiteSpace(rawClient))
        {
            return "未知客户端";
        }

        var client = ClientParser.Parse(rawClient);

        var machine = string.IsNullOrWhiteSpace(client.Machine)
            ? client.Display
            : client.Machine;

        return $"{machine} 使用比例: {pct:0.#}%";
    }

    private static string BuildRangeMeta(DateRange range, ClientInfo? client)
    {
        var span = range.From == range.To
            ? range.From.ToString("yyyy-MM-dd", CultureInfo.CurrentCulture)
            : $"{range.From:yyyy-MM-dd} ~ {range.To:yyyy-MM-dd}";

        var clientText = (client is null || string.IsNullOrWhiteSpace(client.Raw))
            ? "全部客户端"
            : client.Display;

        return $"{span} · {clientText}";
    }

    private async Task ApplyDrugSpecFilterAndReloadAsync(string? drugId, string? spec)
    {
        var drug = NormalizeInput(drugId);
        var specText = NormalizeInput(spec);
        ResetPagedIndexes();

        await RunOnUiAsync(() =>
        {
            using var _ = SuppressReload();
            DrugText = drug;
        });

        if (string.IsNullOrWhiteSpace(drug))
        {
            await RunOnUiAsync(EnsureAllSpecOnly);
        }
        else
        {
            await ReloadSpecsAsync(drug);

            if (!string.IsNullOrWhiteSpace(specText))
            {
                await RunOnUiAsync(() =>
                {
                    using var _ = SuppressReload();
                    SelectedSpec = ResolveOrAddSpecOption(specText);
                });
            }
        }

        ClearBrowsingSelections();
        await ReloadNow();
    }

    private OptionItem ResolveOrAddSpecOption(string specText)
    {
        var match = SpecOptions.FirstOrDefault(x =>
            string.Equals(x.Raw, specText, StringComparison.OrdinalIgnoreCase));
        if (match is not null)
        {
            return match;
        }

        var forced = new OptionItem(specText, specText);
        SpecOptions.Add(forced);
        return forced;
    }

    private ClientInfo? FindClientOption(ClientInfo selected)
    {
        var raw = selected.Raw;
        if (!string.IsNullOrWhiteSpace(raw))
        {
            var hit = Clients.FirstOrDefault(c =>
                string.Equals(c.Raw, raw, StringComparison.OrdinalIgnoreCase));
            if (hit is not null)
            {
                return hit;
            }
        }

        var machine = NormalizeInput(selected.Machine ?? selected.Display);
        if (string.IsNullOrWhiteSpace(machine))
        {
            return null;
        }

        return Clients.FirstOrDefault(c =>
        {
            var cm = NormalizeInput(c.Machine ?? c.Display);
            return string.Equals(cm, machine, StringComparison.OrdinalIgnoreCase);
        });
    }

    [RelayCommand]
    private async Task FirstEntryPageAsync()
    {
        if (ShouldSkipTrigger("dashboard.entry.first", 180))
        {
            return;
        }

        if (!HasEntryPrevPage)
        {
            return;
        }

        EntryPageIndex = 1;
        await ReloadEntryPageOnlyAsync();
    }

    [RelayCommand]
    private async Task PrevEntryPageAsync()
    {
        if (ShouldSkipTrigger("dashboard.entry.prev", 180))
        {
            return;
        }

        if (!HasEntryPrevPage)
        {
            return;
        }

        EntryPageIndex--;
        await ReloadEntryPageOnlyAsync();
    }

    [RelayCommand]
    private async Task NextEntryPageAsync()
    {
        if (ShouldSkipTrigger("dashboard.entry.next", 180))
        {
            return;
        }

        if (!HasEntryNextPage)
        {
            return;
        }

        EntryPageIndex++;
        await ReloadEntryPageOnlyAsync();
    }

    [RelayCommand]
    private async Task LastEntryPageAsync()
    {
        if (ShouldSkipTrigger("dashboard.entry.last", 180))
        {
            return;
        }

        if (!HasEntryNextPage)
        {
            return;
        }

        EntryPageIndex = EntryTotalPages;
        await ReloadEntryPageOnlyAsync();
    }

    [RelayCommand]
    private async Task FirstTxnPageAsync()
    {
        if (ShouldSkipTrigger("dashboard.txn.first", 180))
        {
            return;
        }

        if (!HasTxnPrevPage)
        {
            return;
        }

        TxnPageIndex = 1;
        await ReloadTxnPageOnlyAsync();
    }

    [RelayCommand]
    private async Task PrevTxnPageAsync()
    {
        if (ShouldSkipTrigger("dashboard.txn.prev", 180))
        {
            return;
        }

        if (!HasTxnPrevPage)
        {
            return;
        }

        TxnPageIndex--;
        await ReloadTxnPageOnlyAsync();
    }

    [RelayCommand]
    private async Task NextTxnPageAsync()
    {
        if (ShouldSkipTrigger("dashboard.txn.next", 180))
        {
            return;
        }

        if (!HasTxnNextPage)
        {
            return;
        }

        TxnPageIndex++;
        await ReloadTxnPageOnlyAsync();
    }

    [RelayCommand]
    private async Task LastTxnPageAsync()
    {
        if (ShouldSkipTrigger("dashboard.txn.last", 180))
        {
            return;
        }

        if (!HasTxnNextPage)
        {
            return;
        }

        TxnPageIndex = TxnTotalPages;
        await ReloadTxnPageOnlyAsync();
    }

    [RelayCommand]
    private async Task FirstTxnTrendPageAsync()
    {
        if (ShouldSkipTrigger("dashboard.txntrend.first", 180))
        {
            return;
        }

        if (!HasTxnTrendPrevPage)
        {
            return;
        }

        TxnTrendPageIndex = 1;
        await ReloadTxnTrendPageOnlyAsync();
    }

    [RelayCommand]
    private async Task PrevTxnTrendPageAsync()
    {
        if (ShouldSkipTrigger("dashboard.txntrend.prev", 180))
        {
            return;
        }

        if (!HasTxnTrendPrevPage)
        {
            return;
        }

        TxnTrendPageIndex--;
        await ReloadTxnTrendPageOnlyAsync();
    }

    [RelayCommand]
    private async Task NextTxnTrendPageAsync()
    {
        if (ShouldSkipTrigger("dashboard.txntrend.next", 180))
        {
            return;
        }

        if (!HasTxnTrendNextPage)
        {
            return;
        }

        TxnTrendPageIndex++;
        await ReloadTxnTrendPageOnlyAsync();
    }

    [RelayCommand]
    private async Task LastTxnTrendPageAsync()
    {
        if (ShouldSkipTrigger("dashboard.txntrend.last", 180))
        {
            return;
        }

        if (!HasTxnTrendNextPage)
        {
            return;
        }

        TxnTrendPageIndex = TxnTrendTotalPages;
        await ReloadTxnTrendPageOnlyAsync();
    }

    [RelayCommand]
    private async Task FirstAbnormalPageAsync()
    {
        if (ShouldSkipTrigger("dashboard.abnormal.first", 180))
        {
            return;
        }

        if (!HasAbnormalPrevPage)
        {
            return;
        }

        AbnormalPageIndex = 1;
        await ReloadAbnormalPageOnlyAsync();
    }

    [RelayCommand]
    private async Task PrevAbnormalPageAsync()
    {
        if (ShouldSkipTrigger("dashboard.abnormal.prev", 180))
        {
            return;
        }

        if (!HasAbnormalPrevPage)
        {
            return;
        }

        AbnormalPageIndex--;
        await ReloadAbnormalPageOnlyAsync();
    }

    [RelayCommand]
    private async Task NextAbnormalPageAsync()
    {
        if (ShouldSkipTrigger("dashboard.abnormal.next", 180))
        {
            return;
        }

        if (!HasAbnormalNextPage)
        {
            return;
        }

        AbnormalPageIndex++;
        await ReloadAbnormalPageOnlyAsync();
    }

    [RelayCommand]
    private async Task LastAbnormalPageAsync()
    {
        if (ShouldSkipTrigger("dashboard.abnormal.last", 180))
        {
            return;
        }

        if (!HasAbnormalNextPage)
        {
            return;
        }

        AbnormalPageIndex = AbnormalTotalPages;
        await ReloadAbnormalPageOnlyAsync();
    }

    private async Task ReloadTxnPageOnlyAsync()
    {
        try
        {
            await RunLocalBusyAsync(
                CancellationToken.None,
                setBusy: v => IsTxnBusy = v,
                showBusy: true,
                body: async () =>
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                var page = await _dashboard.LoadTxnPageAsync(CurrentFilter, TxnPageIndex, TxnPageSize, cts.Token).ConfigureAwait(false);
                var items = BuildRecentTxnsPageItems(page.Rows, TxnPageIndex, TxnPageSize);
                await RunOnUiAsync(() =>
                {
                    ApplyRecentTxnsPage(items, page.TotalCount);
                    OnPropertyChanged(nameof(IsTxnPanelEmpty));
                }, DispatcherPriority.Background);
            });
        }
        catch (Exception ex)
        {
            LogError("dashboard.txn_page.reload_fail", "Failed to reload transaction page", ex);
            if (ShouldShowOperationErrorToast(ex))
            {
                PostOnUi(() => _toast.Error("事务列表加载失败", ex.Message));
            }
        }
    }

    private async Task ReloadTxnTrendPageOnlyAsync()
    {
        try
        {
            await RunLocalBusyAsync(
                CancellationToken.None,
                setBusy: v => IsTxnBusy = v,
                showBusy: true,
                body: async () =>
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                var page = await _dashboard.LoadTxnTrendPageAsync(CurrentFilter, TxnTrendPageIndex, TxnTrendPageSize, cts.Token).ConfigureAwait(false);
                var items = BuildTxnTrendPageItems(page.Rows, TxnTrendPageIndex, TxnTrendPageSize);
                await RunOnUiAsync(() =>
                {
                    ApplyTxnTrendPage(items, page.TotalCount);
                    OnPropertyChanged(nameof(IsTxnPanelEmpty));
                }, DispatcherPriority.Background);
            });
        }
        catch (Exception ex)
        {
            LogError("dashboard.txn_trend.reload_fail", "Failed to reload transaction trend page", ex);
            if (ShouldShowOperationErrorToast(ex))
            {
                PostOnUi(() => _toast.Error("事务趋势加载失败", ex.Message));
            }
        }
    }

    private async Task ReloadEntryPageOnlyAsync()
    {
        try
        {
            await RunLocalBusyAsync(
                CancellationToken.None,
                setBusy: v => IsEntryBusy = v,
                showBusy: true,
                body: async () =>
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                var page = await _dashboard.LoadEntryPageAsync(CurrentFilter, EntryPageIndex, EntryPageSize, cts.Token).ConfigureAwait(false);
                var items = BuildEntryLogsPageItems(page.Rows, EntryPageIndex, EntryPageSize);
                await RunOnUiAsync(() =>
                {
                    ApplyEntryLogsPage(items, page.TotalCount);
                }, DispatcherPriority.Background);
            });
        }
        catch (Exception ex)
        {
            LogError("dashboard.entry_page.reload_fail", "Failed to reload entry page", ex);
            if (ShouldShowOperationErrorToast(ex))
            {
                PostOnUi(() => _toast.Error("录入列表加载失败", ex.Message));
            }
        }
    }

    private async Task ReloadAbnormalPageOnlyAsync()
    {
        try
        {
            await RunLocalBusyAsync(
                CancellationToken.None,
                setBusy: v => IsAbnormalBusy = v,
                showBusy: true,
                body: async () =>
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                var page = await _dashboard.LoadAbnormalPageAsync(CurrentFilter, AbnormalPageIndex, AbnormalPageSize, cts.Token).ConfigureAwait(false);
                var items = BuildAbnormalQueueItems(page.Rows, AbnormalPageIndex, AbnormalPageSize);
                await RunOnUiAsync(() =>
                {
                    ApplyAbnormalQueue(items, page.TotalCount);
                }, DispatcherPriority.Background);
            });
        }
        catch (Exception ex)
        {
            LogError("dashboard.abnormal_page.reload_fail", "Failed to reload abnormal page", ex);
            if (ShouldShowOperationErrorToast(ex))
            {
                PostOnUi(() => _toast.Error("异常列表加载失败", ex.Message));
            }
        }
    }

    [RelayCommand]
    private void OpenTxnList()
    {
        if (ShouldSkipTrigger("dashboard.nav.txn", 250))
        {
            return;
        }

        SelectedTabIndex = 2;
    }

    [RelayCommand]
    private void OpenInventory()
    {
        if (ShouldSkipTrigger("dashboard.nav.inventory", 300))
        {
            return;
        }

        // Match sidebar navigation: reveal the cached page immediately. The inventory page
        // already owns refresh/dirty-state handling, so a dashboard jump must not force a
        // database reload on the UI navigation path.
        _inventoryOverview.OpenMode(0, forceReload: false);
        _nav.Navigate<InventoryOverviewViewModel>();
    }

    [RelayCommand]
    private void OpenAbnormal()
    {
        if (ShouldSkipTrigger("dashboard.nav.abnormal", 250))
        {
            return;
        }

        SelectedTabIndex = 3;
    }

    [RelayCommand]
    private void GoInputTab()
    {
        if (ShouldSkipTrigger("dashboard.nav.input", 300))
        {
            return;
        }

        _nav.Navigate<ScanCodeViewModel>();
    }

    [RelayCommand]
    private void OpenPeriodUsage()
    {
        if (ShouldSkipTrigger("dashboard.nav.period", 250))
        {
            return;
        }

        SelectedTabIndex = 2;
    }

    [RelayCommand]
    private void OpenLowStock()
    {
        if (ShouldSkipTrigger("dashboard.nav.lowstock", 300))
        {
            return;
        }

        _inventoryOverview.Keyword = null;
        _inventoryOverview.OpenMode(2);
        _nav.Navigate<InventoryOverviewViewModel>();
    }

    [RelayCommand]
    private void OpenInputHistory()
    {
        if (ShouldSkipTrigger("dashboard.nav.inputhistory", 250))
        {
            return;
        }

        SelectedTabIndex = 1;
    }

    [RelayCommand]
    private void OpenOverviewTab()
    {
        if (ShouldSkipTrigger("dashboard.nav.overview", 250))
        {
            return;
        }

        SelectedTabIndex = 0;
    }

    public override void Dispose()
    {
        SafeExecute(() => _dateRangeController.Dispose());
        SafeExecute(() => _clientAlias.Changed -= OnClientAliasChanged);

        if (_debounce is not null)
        {
            SafeExecute(() => _debounce.Stop());
            if (_debounceHooked)
            {
                SafeExecute(() => _debounce.Tick -= OnDebounceTimerTick);
            }

            _debounce = null;
            _debounceHooked = false;
        }

        base.Dispose();
    }

    private void SafeExecute(Action action)
    {
        try
        {
            action();
        }
        catch (System.Exception ex)
        {
            LogWarn("dashboard.dispose.safe_execute_fail", "Dispose cleanup action failed", ex);
        }
    }

    private void OnClientAliasChanged()
    {
        PostOnUi(() =>
        {
            var selectedRaw = SelectedClient?.Raw ?? string.Empty;

            // Local re-map for existing UI rows so alias changes are visible immediately.
            if (Clients.Count > 0)
            {
                ApplyClients(Clients.Select(c => c.Raw).Where(r => !string.IsNullOrWhiteSpace(r)).ToList());
            }

            if (!string.IsNullOrWhiteSpace(selectedRaw))
            {
                SelectedClient = Clients.FirstOrDefault(c => string.Equals(c.Raw, selectedRaw, StringComparison.OrdinalIgnoreCase)) ?? AllClients;
            }

            if (TopClients.Count > 0)
            {
                var remappedTop = TopClients
                    .Select(x => x with { Client = ResolveClient(x.Client.Raw) })
                    .ToList();
                TopClients.Clear();
                foreach (var item in remappedTop)
                {
                    TopClients.Add(item);
                }
            }

            if (EntryRecentOverview.Count > 0)
            {
                var remappedOverview = EntryRecentOverview
                    .Select(x => x.WithClient(ResolveClient(x.ClientRaw)))
                    .ToList();
                EntryRecentOverview.Clear();
                foreach (var item in remappedOverview)
                {
                    EntryRecentOverview.Add(item);
                }
            }

            if (EntryRecent.Count > 0)
            {
                var remappedPage = EntryRecent
                    .Select(x => x.WithClient(ResolveClient(x.ClientRaw)))
                    .ToList();
                EntryRecent.Clear();
                foreach (var item in remappedPage)
                {
                    EntryRecent.Add(item);
                }
            }

            OnPropertyChanged(nameof(SectionHint));
            RequestReload();
        });
    }

    public sealed record SimpleModeItem(string Title)
    {
        public override string ToString() => Title;
    }
}

public sealed partial class DashboardKpiModel : ObservableObject
{
    [ObservableProperty] private string _availableRemain = "0";
    [ObservableProperty] private string _periodUsed = "0";
    [ObservableProperty] private string _lowStockCount = "0";
    [ObservableProperty] private string _abnormal = "0";

    [ObservableProperty] private string _availableRemainHint = "当前库存中可用的追溯码数量";
    [ObservableProperty] private string _periodUsedHint = "区间内已使用的追溯码数量";
    [ObservableProperty] private string _abnormalHint = "区间内发生回滚/异常的事务数量";
    [ObservableProperty] private string _lowStockHint = "库存剩余量低于阈值的药品数量";

    [ObservableProperty] private double _availableRemainPct;
    [ObservableProperty] private double _periodUsedPct;
    [ObservableProperty] private double _abnormalPct;
    [ObservableProperty] private double _lowStockPct;
}

public sealed partial class TrendDrugItem : ObservableObject
{
    [ObservableProperty] private int _displayIndex;
    [ObservableProperty] private string _rank = "";
    [ObservableProperty] private string _name = "";
    [ObservableProperty] private string _sub = "";
    [ObservableProperty] private string _sourceText = "";
    [ObservableProperty] private string _valueText = "";

    public string SpecDisplay => DrugSpecDisplayHelper.NormalizeSpecLine(Name, Sub);

    partial void OnNameChanged(string value) => OnPropertyChanged(nameof(SpecDisplay));

    partial void OnSubChanged(string value) => OnPropertyChanged(nameof(SpecDisplay));
}

public sealed record TxnItem(
    int DisplayIndex,
    long Id,
    TxnBadge Badge,
    string DrugId,
    string Spec,
    string Qty,
    string Time,
    string ClientDisplay)
{
    public string Title => string.IsNullOrWhiteSpace(Spec) ? DrugId : $"{DrugId} {Spec}";
}
public sealed record AbnormalItem(int DisplayIndex, string Title, string Detail, string ClientDisplay, TxnBadge Badge);

public sealed record TopClientItem(int Index, ClientInfo Client, string Value)
{
    public string Name => Client.Display;

    public string ClientDisplay => Client.Display;
    public string? Machine => Client.Machine;
    public string? User => Client.User;
    public string? Ip => Client.Ip;
    public string? Os => Client.Os;
    public string? Ver => Client.Version;

    public string MetaText
    {
        get
        {
            var parts = new List<string>(4);
            if (!string.IsNullOrWhiteSpace(User))
            {
                parts.Add(User!);
            }

            if (!string.IsNullOrWhiteSpace(Ip))
            {
                parts.Add(Ip!);
            }

            if (!string.IsNullOrWhiteSpace(Os))
            {
                parts.Add(Os!);
            }

            if (!string.IsNullOrWhiteSpace(Ver))
            {
                parts.Add(Ver!);
            }

            return string.Join(" · ", parts);
        }
    }

    public bool HasMeta => !string.IsNullOrWhiteSpace(MetaText);
}

public sealed record EntryRecentItem(
    int DisplayIndex,
    TraceEntryState State,
    DateTimeOffset EntryAt,
    string EntryAtText,
    string DrugId,
    string Spec,
    int TotalAvailableQty,
    string Qty,
    string Source,
    string? Message,
    long? TxnId,
    string ClientRaw,
    string ClientMachine,
    string ClientDisplay,
    string? ClientIp,
    string? ClientOs,
    string? ClientVer
)
{
    public static EntryRecentItem From(TraceEntryLogDto e, ClientInfo client, int displayIndex = 0)
    {
        var result = (e.Result).Trim().ToLowerInvariant();
        var source = (e.Source).Trim().ToLowerInvariant();

        var state = result switch
        {
            "success" => TraceEntryState.Success,
            "partial" => TraceEntryState.Warning,
            "failed" => TraceEntryState.Failed,
            _ => TraceEntryState.Unknown
        };

        var qtyText = e.TotalAvailableQty.ToString("N0", CultureInfo.CurrentCulture);
        var atText = e.EntryAt.ToLocalTime().ToString("MM-dd HH:mm", CultureInfo.CurrentCulture);
        var sourceText = MapSource(source);
        var resultText = MapResult(result);
        var messageText = BuildMessage(e.Message, sourceText, resultText);

        return new EntryRecentItem(
            DisplayIndex: displayIndex,
            State: state,
            EntryAt: e.EntryAt,
            EntryAtText: atText,
            DrugId: e.DrugId,
            Spec: e.Spec,
            TotalAvailableQty: e.TotalAvailableQty,
            Qty: qtyText,
            Source: sourceText,
            Message: messageText,
            TxnId: e.TxnId,
            ClientRaw: e.Client,
            ClientMachine: client.Machine ?? client.Display,
            ClientDisplay: client.Display,
            ClientIp: client.Ip,
            ClientOs: client.Os,
            ClientVer: client.Version
        );
    }

    public EntryRecentItem WithClient(ClientInfo client)
        => this with
        {
            ClientMachine = client.Machine ?? client.Display,
            ClientDisplay = client.Display,
            ClientIp = client.Ip,
            ClientOs = client.Os,
            ClientVer = client.Version
        };

    private static string MapSource(string source)
        => source switch
        {
            "manual" => "手动录入",
            "batch" => "批量导入",
            "api" => "自动拉取",
            _ => "未知来源"
        };

    private static string MapResult(string result)
        => result switch
        {
            "success" => "成功",
            "partial" => "部分成功",
            "failed" => "失败",
            _ => "未知状态"
        };

    private static string BuildMessage(string? rawMessage, string sourceText, string resultText)
    {
        var parsed = ParseSummary(rawMessage);
        if (parsed is null)
        {
            return string.IsNullOrWhiteSpace(rawMessage)
                ? $"{sourceText} · {resultText}"
                : $"{sourceText} · {resultText} · {rawMessage}";
        }

        return
            $"{sourceText} · {resultText} · 总数 {parsed.Value.Total} · 成功 {parsed.Value.Valid} · 重复 {parsed.Value.Duplicate} · 无效 {parsed.Value.Invalid} · 写入 {parsed.Value.Inserted} · 跳过 {parsed.Value.Skipped}";
    }

    private static (int Total, int Valid, int Duplicate, int Invalid, int Inserted, int Skipped)? ParseSummary(string? rawMessage)
    {
        if (string.IsNullOrWhiteSpace(rawMessage))
        {
            return null;
        }

        var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var parts = rawMessage.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        foreach (var part in parts)
        {
            var idx = part.IndexOf('=');
            if (idx <= 0 || idx >= part.Length - 1)
            {
                continue;
            }

            var key = NormalizeSummaryKey(part[..idx].Trim());
            var val = part[(idx + 1)..].Trim();
            if (int.TryParse(val, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number))
            {
                map[key] = number;
            }
        }

        if (!map.TryGetValue("input", out var total) ||
            !map.TryGetValue("valid", out var valid) ||
            !map.TryGetValue("duplicate", out var duplicate) ||
            !map.TryGetValue("invalid", out var invalid) ||
            !map.TryGetValue("inserted", out var inserted) ||
            !map.TryGetValue("skipped", out var skipped))
        {
            return null;
        }

        return (total, valid, duplicate, invalid, inserted, skipped);
    }

    private static string NormalizeSummaryKey(string rawKey)
    {
        if (string.IsNullOrWhiteSpace(rawKey))
        {
            return string.Empty;
        }

        var key = rawKey.Trim().ToLowerInvariant();
        if (key.EndsWith(" input", StringComparison.Ordinal))
        {
            return "input";
        }

        return key;
    }
}
