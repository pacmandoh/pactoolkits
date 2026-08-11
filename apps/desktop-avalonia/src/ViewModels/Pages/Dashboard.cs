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
using PacToolkits.Desktop.Avalonia.Services.Workspace;

namespace PacToolkits.Desktop.Avalonia.ViewModels.Pages;

/// <summary>
/// 协调概览页 KPI、趋势、录入、事务和异常数据，以及日期、客户筛选与跨页导航
/// </summary>
public sealed partial class Dashboard : AppPageBase
{
    private const int DefaultTopN = 10;
    private const int EntryOverviewTopN = 6;
    private static readonly int[] TabPageSizeOptionValues = [20, 50, 100];

    public override string DisplayName => "概览";
    public override string Icon => "LayoutPanelLeft";
    public override int Index => 0;
    protected override bool AutoRefreshOnDbDisconnected => true;
    protected override bool AutoRefreshOnDbReconnected => true;

    private readonly IDashboardService _dashboard;
    private readonly IToastService _toast;
    private readonly IClientAliasService _clientAlias;
    private readonly ILookupCatalogService _lookup;
    private readonly PageNavigationService _nav;
    private readonly InventoryOverview _inventoryOverview;
    private readonly WorkspaceDirtyRefresh _dirtyRefresh;
    private int _rowSelectionSuppressDepth;
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

        QueueTabPageReload(force: _dirtyRefresh.IsDirty(this));
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
        QueueTabPageReload(force: true);
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
            ResetSpecToAll();
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
            AutoCompleteFilter.RefreshVisibleOptions(DrugOptions, _drugCatalog, searchText);
        }
    }

    partial void OnSelectedSpecChanged(OptionItem value)
    {
        if (IsReloadSuppressed)
            return;

        RequestReloadWithPagingReset();
    }

    private void ResetSpecToAll()
    {
        using (SuppressReload())
        {
            SpecOptions.Clear();
            SpecOptions.Add(AllSpec);
            SelectedSpec = AllSpec;
        }
    }

    private async Task RefreshDrugCatalogAsync(CancellationToken ct, bool forceRefresh = false)
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
                    ResetSpecToAll();
                }
            }, DispatcherPriority.Background);
            return;
        }

        var list = await DrugCatalogRefresh.LoadAsync(_lookup, forceRefresh, ct).ConfigureAwait(false);

        await RunOnUiAsync(() =>
        {
            using (SuppressReload())
            {
                _drugCatalog = list;
                AutoCompleteFilter.RefreshVisibleOptions(DrugOptions, _drugCatalog, DrugText);

                if (SpecOptions.Count == 0)
                {
                    SpecOptions.Add(AllSpec);
                }

                if (DrugCatalogRefresh.IsMissing(list, NormalizeInput(DrugText)))
                {
                    DrugText = null;
                    IsDrugSuggestOpen = false;
                    ResetSpecToAll();
                }
            }
        }, DispatcherPriority.Background);
    }

    public void ReloadAfterDrugIndexChange()
    {
        PostOnUi(async () =>
        {
            try
            {
                await RefreshDrugCatalogAsync(CancellationToken.None, forceRefresh: true).ConfigureAwait(false);

                var drug = NormalizeInput(DrugText);
                if (!string.IsNullOrWhiteSpace(drug))
                {
                    await ReloadSpecsAsync(drug).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                LogWarn("dashboard.catalog.reload_fail", "Failed to reload drug catalog after drug-index change", ex);
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
                specs = await LookupOptions.GetSpecsAsync(_lookup, drug, ct).ConfigureAwait(false);
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

    public int KpiProgressReplayTrigger { get; private set; }

    public string SectionHint => BuildRangeMeta(CurrentRange, SelectedClient);

    public ObservableCollection<TrendDrugItem> DrugTrend { get; } = new();
    public ObservableCollection<TrendDrugItem> ChartDrugTrend { get; } = new();
    public ObservableCollection<TxnItem> ChartTxns { get; } = new();
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

    [ObservableProperty] private bool _isTxnChartVisible;
    public string TxnViewToggleText => IsTxnChartVisible ? "数据框" : "图表";
    public string TxnViewToggleIcon => IsTxnChartVisible ? "Table2" : "ChartNoAxesCombined";

    partial void OnIsTxnChartVisibleChanged(bool value)
    {
        OnPropertyChanged(nameof(TxnViewToggleText));
        OnPropertyChanged(nameof(TxnViewToggleIcon));
    }

    [ObservableProperty] private bool _isClientChartVisible;
    public string ClientViewToggleText => IsClientChartVisible ? "数据框" : "图表";
    public string ClientViewToggleIcon => IsClientChartVisible ? "Table2" : "ChartPie";

    partial void OnIsClientChartVisibleChanged(bool value)
    {
        OnPropertyChanged(nameof(ClientViewToggleText));
        OnPropertyChanged(nameof(ClientViewToggleIcon));
        OnPropertyChanged(nameof(IsClientPanelEmpty));
    }

    [ObservableProperty] private bool _isEntryChartVisible;
    public string EntryViewToggleText => IsEntryChartVisible ? "数据框" : "图表";
    public string EntryViewToggleIcon => IsEntryChartVisible ? "Table2" : "ChartNoAxesGantt";

    partial void OnIsEntryChartVisibleChanged(bool value)
    {
        OnPropertyChanged(nameof(EntryViewToggleText));
        OnPropertyChanged(nameof(EntryViewToggleIcon));
        OnPropertyChanged(nameof(IsEntryPanelEmpty));
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
    public ObservableCollection<TopClientItem> ChartClients { get; } = new();
    public ObservableCollection<EntryChartItem> EntryChartRows { get; } = new();
    private DistributionScope? _distributionScope;
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
            ObserveDetached(ReloadTxnTrendPageOnlyAsync(), "txn_trend.reload.detached.fail");
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
        OnPropertyChanged(nameof(IsEntryPanelEmpty));
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
            ResetSpecToAll();
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
        OnPropertyChanged(nameof(IsClientPanelEmpty));
        OnPropertyChanged(nameof(TopClientsEmptyText));
        OnPropertyChanged(nameof(TopClientsEmptyHint));
        OnPropertyChanged(nameof(IsRecentTxnsEmpty));
        OnPropertyChanged(nameof(RecentTxnsEmptyText));
        OnPropertyChanged(nameof(RecentTxnsEmptyHint));
        OnPropertyChanged(nameof(IsEntryRecentEmpty));
        OnPropertyChanged(nameof(IsEntryPanelEmpty));
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

    public bool IsTrendEmpty => ShowSectionEmpty(DrugTrend.Count == 0);
    public bool IsTxnTrendEmpty => ShowSectionEmpty(TxnTrendTotalCount == 0);
    public bool IsTopClientsEmpty => ShowSectionEmpty(TopClients.Count == 0);
    public bool IsClientPanelEmpty => ShowSectionEmpty(
        IsClientChartVisible ? ChartClients.Count == 0 : TopClients.Count == 0);
    public bool IsRecentTxnsEmpty => ShowSectionEmpty(TxnTotalCount == 0);
    public bool IsEntryRecentEmpty => ShowSectionEmpty(EntryTotalCount == 0);
    public bool IsEntryPanelEmpty => ShowSectionEmpty(
        IsEntryChartVisible ? EntryChartRows.Count == 0 : EntryTotalCount == 0);
    public bool IsAbnormalEmpty => ShowSectionEmpty(AbnormalTotalCount == 0);

    private DispatcherTimer? _debounce;
    private bool _debounceHooked;
    private int _suppressReloadCount;
    private bool IsReloadSuppressed => _suppressReloadCount > 0;

    private bool _firstLoadTriggered;
    private bool _filtersLoaded;

    public Dashboard(IDashboardService dashboard, ILookupCatalogService lookup, IToastService toast,
        IClientAliasService clientAlias, PageNavigationService nav, InventoryOverview inventoryOverview,
        WorkspaceDirtyRefresh dirtyRefresh)
    {
        _dashboard = dashboard;
        _toast = toast;
        _clientAlias = clientAlias;
        _lookup = lookup;
        _nav = nav;
        _inventoryOverview = inventoryOverview;
        _dirtyRefresh = dirtyRefresh;
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

        PostOnUi(() => ObserveDetached(InitializeAsync(), "init.detached.fail"), DispatcherPriority.Loaded);

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
                    await RefreshDrugCatalogAsync(cts.Token);

                    if (!string.IsNullOrWhiteSpace(DrugText))
                    {
                        await ReloadSpecsAsync(NormalizeInput(DrugText)!);
                    }
                    else
                    {
                        await RunOnUiAsync(ResetSpecToAll, DispatcherPriority.Background);
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
        ObserveDetached(ReloadNow(), "reload.detached.fail");
    }

    private void QueueTabPageReload(bool force = false)
    {
        switch (SelectedTabIndex)
        {
            case 1 when force || (EntryRecent.Count == 0 && !IsEntryBusy):
                ObserveDetached(ReloadEntryPageOnlyAsync(), "entry.reload.detached.fail");
                break;
            case 2:
                if (!force && IsTxnBusy)
                {
                    break;
                }

                if (IsTxnPanelTrendMode)
                {
                    if (force || TxnTrendRows.Count == 0)
                    {
                        ObserveDetached(ReloadTxnTrendPageOnlyAsync(), "txn_trend.reload.detached.fail");
                    }
                }
                else if (force || RecentTxns.Count == 0)
                {
                    ObserveDetached(ReloadTxnPageOnlyAsync(), "txn.reload.detached.fail");
                }

                break;
            case 3 when force || (AbnormalQueue.Count == 0 && !IsAbnormalBusy):
                ObserveDetached(ReloadAbnormalPageOnlyAsync(), "abnormal.reload.detached.fail");
                break;
        }
    }

    protected override Task ReloadCoreAsync(CancellationToken ct)
        => RefreshAllAsync(ct);

    public override Task OnPageActivatedAsync(CancellationToken ct = default)
    {
        PostOnUi(BumpKpiProgressReplay);
        return base.OnPageActivatedAsync(ct);
    }

    protected override void OnReloadFinished()
    {
        base.OnReloadFinished();
        if (WorkspacePageRefresh.RefreshSucceeded(this))
        {
            _dirtyRefresh.Clear(this);
        }

        if (!IsSignalReload)
        {
            PostOnUi(BumpKpiProgressReplay);
        }
    }

    private void BumpKpiProgressReplay()
    {
        KpiProgressReplayTrigger++;
        OnPropertyChanged(nameof(KpiProgressReplayTrigger));
    }

    partial void OnFromDateChanged(DateTime? value)
    {
        ApplyDateRangeFromBoundary(value, updateFromBoundary: true);
    }

    partial void OnToDateChanged(DateTime? value)
    {
        ApplyDateRangeFromBoundary(value, updateFromBoundary: false);
    }

    // 统一校正日期区间并通过同一重载路径应用起止边界变化
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
        if (SkipTrigger("dashboard.filter.clear", 350))
        {
            return;
        }

        IsDrugSuggestOpen = false;
        ResetPagedIndexes();

        await RunOnUiAsync(() =>
        {
            using var _ = SuppressReload();
            DrugText = null;
            ResetSpecToAll();
            SelectedClient = AllClients;
        }, DispatcherPriority.Background);

        await ReloadNow();
    }

    private bool CanClearDrugSpecFilter()
        => AutoCompleteFilter.HasDrugText(DrugText);

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
        // 筛选条件或日期变化后从第一页重新加载
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

    public async Task OpenTrendDrugAsync(TrendDrugItem? item)
    {
        if (IsRowSelectionActionSuppressed || item is null)
        {
            return;
        }

        await ApplyDrugSpecFilterAndReloadAsync(item.Name, item.Sub);
    }

    public async Task OpenTxnAsync(TxnItem? item)
    {
        if (IsRowSelectionActionSuppressed || item is null)
        {
            return;
        }

        await ApplyDrugSpecFilterAndReloadAsync(item.DrugId, item.Spec);

        using (SuppressReload())
        {
            TxnPanelMode = TxnPanelModes.FirstOrDefault();
            SelectedTabIndex = 2;
        }

        await RunOnUiAsync(() =>
        {
            using var _ = SuppressReload();
            SelectedTxn = RecentTxns.FirstOrDefault(x =>
                string.Equals(x.DrugId, item.DrugId, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(x.Spec, item.Spec, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(x.Time, item.Time, StringComparison.Ordinal) &&
                string.Equals(x.Qty, item.Qty, StringComparison.Ordinal));
        }, DispatcherPriority.Background);
    }

    public async Task OpenEntryAsync(EntryRecentItem? item)
    {
        if (IsRowSelectionActionSuppressed || item is null)
        {
            return;
        }

        await ApplyDrugSpecFilterAndReloadAsync(item.DrugId, item.Spec);

        using (SuppressReload())
        {
            SelectedTabIndex = 1;
        }
    }

    public async Task OpenClientAsync(TopClientItem? item)
    {
        if (IsRowSelectionActionSuppressed || item is null)
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

    public async Task OpenAbnormalAsync(AbnormalItem? item)
    {
        if (IsRowSelectionActionSuppressed || item is null)
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
            _nav.Navigate<InventoryOverview>();
            return;
        }

        using (SuppressReload())
        {
            TxnPanelMode = TxnPanelModes.FirstOrDefault();
            SelectedTabIndex = 2;
        }
    }

    public RowSelectionSuppressScope BeginRowSelectionSuppress()
        => new(this);

    private bool IsRowSelectionActionSuppressed => _rowSelectionSuppressDepth > 0;

    public void ClearBrowsingSelections()
    {
        SelectedTrendItem = null;
        SelectedTxn = null;
        SelectedEntryRecent = null;
        SelectedAbnormal = null;
    }

    public sealed class RowSelectionSuppressScope : IDisposable
    {
        private readonly Dashboard _vm;
        private bool _disposed;

        internal RowSelectionSuppressScope(Dashboard vm)
        {
            _vm = vm;
            _vm._rowSelectionSuppressDepth++;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            if (_vm._rowSelectionSuppressDepth > 0)
            {
                _vm._rowSelectionSuppressDepth--;
            }
        }
    }

    private DashboardFilter CurrentFilter
    {
        get
        {
            var range = CurrentRange;
            var metric = TrendMode?.Title == "按事务次数"
                ? TrendMetric.Txn
                : TrendMetric.Qty;
            var drug = NormalizeInput(DrugText);
            var spec = string.IsNullOrWhiteSpace(SelectedSpec.Raw) ? null : SelectedSpec.Raw;
            return new DashboardFilter(
                range.From,
                range.To,
                ResolveSelectedClientMachines(),
                drug,
                spec,
                metric);
        }
    }

    private IReadOnlyList<string>? ResolveSelectedClientMachines()
    {
        var selected = SelectedClient;
        if (selected is null || string.IsNullOrWhiteSpace(selected.Raw))
        {
            return null;
        }

        var machines = selected.MachineKeys;
        return machines.Count == 0 ? null : machines;
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
        var showTxnBusy = ShowTxnBusy();
        var showEntryBusy = ShowEntryBusy();
        var showAbnormalBusy = ShowAbnormalBusy();
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
                if (DrugOptions.Count == 0 || _dirtyRefresh.IsDirty(this))
                {
                    try
                    {
                        await RefreshDrugCatalogAsync(ct).ConfigureAwait(false);
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

                var filter = CurrentFilter;
                var distributionScope = new DistributionScope(
                    new DateRange(filter.From, filter.To),
                    filter.DrugId,
                    NormalizeInput(filter.Spec));
                var request = new DashboardRequest(
                    Filter: filter,
                    RefreshDistributions: IsSignalReload
                                          || _dirtyRefresh.IsDirty(this)
                                          || _distributionScope != distributionScope,
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

                var loaded = await _dashboard.GetSnapshotAsync(request, ct).ConfigureAwait(false);

                var trendItems = BuildTrendItems(loaded.Trend);
                var chartTrendItems = BuildTrendItems(loaded.ChartTrend);
                var recentOverviewItems = BuildRecentTxnsOverviewItems(loaded.TxnsOverview.Rows);
                var chartTxnItems = BuildRecentTxnsOverviewItems(loaded.ChartTxns);
                var recentTxnPageItems = BuildRecentTxnsPageItems(loaded.TxnsPage.Rows, TxnPageIndex, TxnPageSize);
                var txnTrendPageItems = BuildTxnTrendPageItems(loaded.TxnTrendPage.Rows);
                var entryOverviewItems = BuildEntryLogsOverviewItems(loaded.EntriesOverview.Rows);
                var entryPageItems = BuildEntryLogsPageItems(loaded.EntriesPage.Rows, EntryPageIndex, EntryPageSize);
                var topClientItems = BuildTopClientItems(loaded.TopClients);
                var chartClientItems = BuildTopClientItems(loaded.ChartClients);
                var entryChartItems = BuildEntryChartItems(loaded.EntryChart);
                var abnormalItems = BuildAbnormalQueueItems(loaded.Abnormal.Rows, AbnormalPageIndex, AbnormalPageSize);

                await RunOnUiAsync(() =>
                {
                    ApplyClients(loaded.ClientNames);

                    ApplyKpi(loaded.Kpi);
                    ApplyTrend(trendItems);
                    ChartDrugTrend.ReplaceAll(chartTrendItems);
                    ApplyRecentTxnsOverview(recentOverviewItems);
                    ChartTxns.ReplaceAll(chartTxnItems);
                    ApplyRecentTxnsPage(recentTxnPageItems, loaded.TxnsPage.TotalCount);
                    ApplyTxnTrendPage(txnTrendPageItems, loaded.TxnTrendPage.TotalCount);
                    ApplyEntryLogsOverview(entryOverviewItems);
                    ApplyEntryLogsPage(entryPageItems, loaded.EntriesPage.TotalCount);
                    ApplyTopClients(topClientItems);
                    if (loaded.DistributionsRefreshed)
                    {
                        ChartClients.ReplaceAll(chartClientItems);
                        EntryChartRows.ReplaceAll(entryChartItems);
                        _distributionScope = distributionScope;
                        OnPropertyChanged(nameof(IsClientPanelEmpty));
                        OnPropertyChanged(nameof(IsEntryPanelEmpty));
                    }
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
            if (ct.IsCancellationRequested)
            {
                throw;
            }

            if (!CanToastError(ex))
            {
                throw;
            }

            PostOnUi(() => _toast.Error("概览加载失败", ex.Message));
            throw;
        }
    }

    private void FinishTabReloadFail(Exception ex, string toastTitle)
    {
        if (CanToastError(ex))
        {
            PostOnUi(() => _toast.Error(toastTitle, ex.Message));
        }

        // 分页命令已通过后台任务观察器记录异常，此处仅更新失败状态
    }

    private bool ShowTxnBusy()
        => RecentTxns.Count == 0 && RecentTxnsOverview.Count == 0 && DrugTrend.Count == 0 && TopClients.Count == 0;

    private bool ShowEntryBusy()
        => EntryRecent.Count == 0 && EntryRecentOverview.Count == 0;

    private bool ShowAbnormalBusy()
        => AbnormalQueue.Count == 0;

    private void ApplyClients(IReadOnlyList<string> list, bool refreshAliases = false)
    {
        var rawClients = list
            .Where(raw => !string.IsNullOrWhiteSpace(raw))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var currentKnown = Clients
            .Skip(1)
            .SelectMany(static client => client.MachineKeys)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (!refreshAliases
            && currentKnown.SequenceEqual(rawClients, StringComparer.OrdinalIgnoreCase))
        {
            return;
        }

        var selectedRaw = SelectedClient?.Raw ?? string.Empty;
        var options = ClientDisplayResolver.OptionsByDisplay(rawClients, _clientAlias);

        using (SuppressReload())
        {
            Clients.Clear();
            Clients.Add(AllClients);
            foreach (var option in options)
            {
                Clients.Add(option);
            }

            SelectedClient = FindClientOptionByRaw(selectedRaw) ?? AllClients;
        }
    }

    private ClientInfo? FindClientOptionByRaw(string? selectedRaw)
    {
        if (string.IsNullOrWhiteSpace(selectedRaw))
        {
            return null;
        }

        var resolved = ResolveClient(selectedRaw);
        var machine = resolved.MachineKeys.FirstOrDefault() ?? selectedRaw.Trim();
        return Clients.FirstOrDefault(client =>
            !string.IsNullOrWhiteSpace(client.Raw)
            && (string.Equals(client.Raw, selectedRaw, StringComparison.OrdinalIgnoreCase)
                || client.ContainsMachine(machine)
                || string.Equals(client.Display, resolved.Display, StringComparison.OrdinalIgnoreCase)));
    }

    private ClientInfo ResolveClient(string raw)
        => ClientDisplayResolver.Resolve(raw, _clientAlias);

    [RelayCommand]
    private async Task ApplyDrugFilterAsync()
    {
        if (SkipTrigger("dashboard.filter.apply", 350))
        {
            return;
        }

        IsDrugSuggestOpen = false;
        ResetPagedIndexes();

        var drug = NormalizeInput(DrugText);

        if (string.IsNullOrWhiteSpace(drug))
        {
            ResetSpecToAll();
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
            var clientDisplay = ResolveTrendClient(r.TopClientRaw);
            items.Add(new TrendDrugItem
            {
                DisplayIndex = r.Rank,
                Name = r.Name,
                Sub = r.Sub,
                ClientDisplay = clientDisplay,
                SourceText = BuildTrendSourceText(clientDisplay, r.TopClientPct),
                UsagePercentText = $"{r.TopClientPct:0.#}%",
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

    private List<TrendDrugItem> BuildTxnTrendPageItems(IReadOnlyList<TrendRowDto> rows)
    {
        var items = new List<TrendDrugItem>(rows.Count);
        foreach (var r in rows)
        {
            var clientDisplay = ResolveTrendClient(r.TopClientRaw);
            items.Add(new TrendDrugItem
            {
                DisplayIndex = r.Rank,
                Name = r.Name,
                Sub = r.Sub,
                ClientDisplay = clientDisplay,
                SourceText = BuildTrendSourceText(clientDisplay, r.TopClientPct),
                UsagePercentText = $"{r.TopClientPct:0.#}%",
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
        var aggregated = ClientDisplayResolver.AggregateByDisplay(
            rows.Select(row => (row.Client, row.Value)),
            _clientAlias);
        var items = new List<TopClientItem>(aggregated.Count);
        var idx = 1;
        foreach (var row in aggregated)
        {
            items.Add(new TopClientItem(
                Index: idx++,
                Client: row.Client,
                Value: row.Value.ToString("N0", CultureInfo.CurrentCulture)));
        }

        return items;
    }

    private void ApplyTopClients(IReadOnlyList<TopClientItem> items)
    {
        TopClients.ReplaceAll(items);
        OnPropertyChanged(nameof(IsTopClientsEmpty));
        OnPropertyChanged(nameof(IsClientPanelEmpty));
    }

    private List<EntryChartItem> BuildEntryChartItems(IReadOnlyList<EntryChartRowDto> rows)
        => rows.Select(row =>
            {
                var client = ResolveClient(row.ClientRaw);
                return new EntryChartItem(row.ClientRaw, client.Display, row.State, row.Count);
            })
            .ToList();

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
                CreatedAt: t.CreatedAt,
                Time: t.CreatedAt.ToLocalTime().ToString("MM-dd HH:mm:ss", CultureInfo.CurrentCulture),
                ClientDisplay: ResolveClientText(t.ClientRaw)
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
                CreatedAt: t.CreatedAt,
                Time: t.CreatedAt.ToLocalTime().ToString("MM-dd HH:mm:ss", CultureInfo.CurrentCulture),
                ClientDisplay: ResolveClientText(t.ClientRaw)
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
                ClientDisplay: ResolveClientText(row.ClientRaw),
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

    private string ResolveTrendClient(string? rawClient)
        => string.IsNullOrWhiteSpace(rawClient) ? "未知客户端" : ResolveClient(rawClient).Display;

    private readonly record struct DistributionScope(DateRange Range, string? DrugId, string? Spec);

    private static string BuildTrendSourceText(string clientDisplay, decimal pct)
        => $"{clientDisplay} 使用比例：{pct:0.#}%";

    private string ResolveClientText(string? raw)
        => string.IsNullOrWhiteSpace(raw) ? "-" : ResolveClient(raw).Display;

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
        using var _ = BeginRowSelectionSuppress();
        var drug = NormalizeInput(drugId);
        var specText = NormalizeInput(spec);
        ResetPagedIndexes();

        await RunOnUiAsync(() =>
        {
            using var __ = SuppressReload();
            DrugText = drug;
        });

        if (string.IsNullOrWhiteSpace(drug))
        {
            await RunOnUiAsync(ResetSpecToAll);
        }
        else
        {
            await ReloadSpecsAsync(drug);

            if (!string.IsNullOrWhiteSpace(specText))
            {
                await RunOnUiAsync(() =>
                {
                    using var __ = SuppressReload();
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
        var byRaw = FindClientOptionByRaw(selected.Raw);
        if (byRaw is not null)
        {
            return byRaw;
        }

        var machine = NormalizeInput(selected.Machine ?? selected.Display);
        if (string.IsNullOrWhiteSpace(machine))
        {
            return null;
        }

        return FindClientOptionByRaw(machine);
    }

    [RelayCommand]
    private void OpenTxnList()
    {
        if (SkipTrigger("dashboard.nav.txn", 250))
        {
            return;
        }

        SelectedTabIndex = 2;
    }

    [RelayCommand]
    private void OpenInventory()
    {
        if (SkipTrigger("dashboard.nav.inventory", 300))
        {
            return;
        }

        // 导航时先展示缓存内容；库存页自行处理待刷新状态，避免阻塞界面切换
        _inventoryOverview.OpenMode(0, forceReload: false);
        _nav.Navigate<InventoryOverview>();
    }

    [RelayCommand]
    private void OpenAbnormal()
    {
        if (SkipTrigger("dashboard.nav.abnormal", 250))
        {
            return;
        }

        SelectedTabIndex = 3;
    }

    [RelayCommand]
    private void GoInputTab()
    {
        if (SkipTrigger("dashboard.nav.input", 300))
        {
            return;
        }

        _nav.Navigate<ScanCode>();
    }

    [RelayCommand]
    private void OpenPeriodUsage()
    {
        if (SkipTrigger("dashboard.nav.period", 250))
        {
            return;
        }

        SelectedTabIndex = 2;
    }

    [RelayCommand]
    private void OpenLowStock()
    {
        if (SkipTrigger("dashboard.nav.lowstock", 300))
        {
            return;
        }

        _inventoryOverview.Keyword = null;
        _inventoryOverview.OpenMode(2);
        _nav.Navigate<InventoryOverview>();
    }

    [RelayCommand]
    private void OpenInputHistory()
    {
        if (SkipTrigger("dashboard.nav.inputhistory", 250))
        {
            return;
        }

        SelectedTabIndex = 1;
    }

    [RelayCommand]
    private void OpenOverviewTab()
    {
        if (SkipTrigger("dashboard.nav.overview", 250))
        {
            return;
        }

        SelectedTabIndex = 0;
    }

    protected override void DisposeCore()
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

        base.DisposeCore();
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
        // 别名合并会同时影响设备筛选和图表聚合，因此统一执行一次重载
        PostOnUi(RequestReload);
    }
}
