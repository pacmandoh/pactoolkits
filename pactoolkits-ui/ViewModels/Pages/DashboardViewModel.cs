using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Material.Icons;
using pactoolkits_ui.Common;
using pactoolkits_ui.Contracts;
using pactoolkits_ui.Repositories;
using pactoolkits_ui.Services;

namespace pactoolkits_ui.ViewModels.Pages;

public sealed record OptionItem(string Raw, string Display)
{
    public override string ToString() => Display;
}

public sealed partial class DashboardViewModel : AppPageBase
{
    private const int DefaultTopN = 10;
    private const int EntryOverviewTopN = 6;
    private const int TabPageSize = 50;

    public override string DisplayName => "概览";
    public override MaterialIconKind Icon => MaterialIconKind.ViewDashboard;
    public override int Index => 0;

    private readonly IDashboardRepo _repo;
    private readonly IToastService _toast;
    private readonly IClientAliasService _clientAlias;
    private readonly ILookupCatalogService _lookup;
    private readonly PageNavigationService _nav;
    private readonly InventoryOverviewViewModel _inventoryOverview;
    private bool _suppressRowSelectionAction;

    [ObservableProperty] private int _selectedTabIndex;
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

    [ObservableProperty] private DateTime? _fromDate = DateTime.Today.AddDays(-6);
    [ObservableProperty] private DateTime? _toDate = DateTime.Today;

    public DateTime? FromMaxDate => ToDate?.Date;
    public DateTime? ToMinDate => FromDate?.Date;

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

    private static readonly OptionItem AllSpec = new("", "全部规格");

    [ObservableProperty] private string? _drugText;


    [ObservableProperty] private OptionItem _selectedSpec = AllSpec;

    [ObservableProperty] private bool _isDrugSuggestOpen;

    partial void OnDrugTextChanged(string? value)
    {
        var drug = NormalizeInput(value);
        if (string.IsNullOrWhiteSpace(drug))
        {
            IsDrugSuggestOpen = false;
            EnsureAllSpecOnly();
            return;
        }

        IsDrugSuggestOpen = true;

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
        var list = await _lookup.GetDrugIdsAsync(ct).ConfigureAwait(false);

        await RunOnUiAsync(() =>
        {
            using (SuppressReload())
            {
                OptionCollectionHelper.ReplaceRaw(DrugOptions, list, StringComparison.Ordinal);

                if (SpecOptions.Count == 0)
                    SpecOptions.Add(AllSpec);
            }
        }, DispatcherPriority.Background);
    }

    private async Task ReloadSpecsAsync(string drug)
    {
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(6));
            var ct = cts.Token;

            IReadOnlyList<string> specs = Array.Empty<string>();

            if (!string.IsNullOrWhiteSpace(drug))
            {
                specs = await _lookup.GetSpecsByDrugAsync(drug, ct).ConfigureAwait(false);
            }

            await RunOnUiAsync(() =>
            {
                using (SuppressReload())
                {
                    var prevRaw = SelectedSpec.Raw;

                    OptionCollectionHelper.ReplaceRaw(SpecOptions, specs, StringComparison.Ordinal);
                    if (SpecOptions.Count == 0 || !string.IsNullOrWhiteSpace(SpecOptions[0].Raw))
                        SpecOptions.Insert(0, AllSpec);

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
    [ObservableProperty] private TxnItem? _selectedTxn;
    [ObservableProperty] private EntryRecentItem? _selectedEntryRecent;
    [ObservableProperty] private AbnormalItem? _selectedAbnormal;

    public IReadOnlyList<GridRowActionRule> GridRowActionRules { get; } = new[]
    {
        new GridRowActionRule("TrendGrid", GridInteractionType.Browsing, "点击行：写入顶部 DrugId/Spec 筛选并刷新"),
        new GridRowActionRule("RecentTxnGrid", GridInteractionType.Browsing, "点击行：切到事务 Tab 并高亮该事务"),
        new GridRowActionRule("EntryRecentGrid", GridInteractionType.Browsing, "点击行：切到录入 Tab，并按该药品规格预填筛选"),
        new GridRowActionRule("TopClientsList", GridInteractionType.Browsing, "展示排行信息（不可点击）"),
        new GridRowActionRule("AbnormalGrid", GridInteractionType.Browsing, "点击行：按异常类型跳转事务/库存，并带药品规格筛选")
    };

    public ObservableCollection<SimpleModeItem> TrendModes { get; } = new()
    {
        new("按使用量"),
        new("按事务次数")
    };

    [ObservableProperty] private SimpleModeItem? _trendMode;
    partial void OnTrendModeChanged(SimpleModeItem? value) => RequestReload();

    public ObservableCollection<TopClientItem> TopClients { get; } = new();

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

    public bool IsTrendEmpty => DrugTrend.Count == 0;
    public bool IsTxnTrendEmpty => TxnTrendTotalCount == 0;
    public bool IsTopClientsEmpty => TopClients.Count == 0;
    public bool IsRecentTxnsEmpty => TxnTotalCount == 0;
    public bool IsEntryRecentEmpty => EntryTotalCount == 0;
    public bool IsAbnormalEmpty => AbnormalTotalCount == 0;

    private DispatcherTimer? _debounce;
    private bool _debounceHooked;
    private int _suppressReloadCount;
    private bool IsReloadSuppressed => _suppressReloadCount > 0;

    private bool _firstLoadTriggered;
    private bool _filtersLoaded;

    public DashboardViewModel(IDashboardRepo repo, ILookupCatalogService lookup, IToastService toast,
        IClientAliasService clientAlias, PageNavigationService nav, InventoryOverviewViewModel inventoryOverview)
    {
        _repo = repo;
        _toast = toast;
        _clientAlias = clientAlias;
        _lookup = lookup;
        _nav = nav;
        _inventoryOverview = inventoryOverview;

        Clients.Clear();
        Clients.Add(AllClients);

        DrugOptions.Clear();

        SpecOptions.Clear();
        SpecOptions.Add(AllSpec);

        using (SuppressReload())
        {
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
        if (_firstLoadTriggered) return;
        _firstLoadTriggered = true;

        try
        {
            if (!_filtersLoaded)
            {
                _filtersLoaded = true;
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(8));
                await ReloadDrugOptionsAsync(cts.Token);

                if (!string.IsNullOrWhiteSpace(DrugText))
                    await ReloadSpecsAsync(NormalizeInput(DrugText)!);
                else
                    await RunOnUiAsync(EnsureAllSpecOnly, DispatcherPriority.Background);
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
        if (IsReloadSuppressed) return;

        _debounce ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(180) };

        if (!_debounceHooked)
        {
            _debounce.Tick += (_, _) =>
            {
                _debounce?.Stop();
                _ = ReloadNow();
            };
            _debounceHooked = true;
        }

        _debounce.Stop();
        _debounce.Start();
    }

    private Task ReloadNow() => RunLocalReloadAsync(_ => { }, RefreshAllAsync);

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
                        _ = ReloadTxnTrendPageOnlyAsync();
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
        OnPropertyChanged(nameof(SectionHint));
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
            return;

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
            return;

        await ApplyDrugSpecFilterAndReloadAsync(item.Name, item.Sub);
    }

    public async Task HandleRecentTxnRowSelectedAsync(TxnItem? item)
    {
        if (_suppressRowSelectionAction || item is null)
            return;

        var parsed = TryParseDrugSpecFromTxnTitle(item.Title);
        if (parsed is not null)
            await ApplyDrugSpecFilterAndReloadAsync(parsed.Value.DrugId, parsed.Value.Spec);

        using (SuppressReload())
            TxnPanelMode = TxnPanelModes.FirstOrDefault();

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
            return;

        await ApplyDrugSpecFilterAndReloadAsync(item.DrugId, item.Spec);

        using var _ = SuppressReload();
        SelectedTabIndex = 1;
    }

    public async Task HandleTopClientRowSelectedAsync(TopClientItem? item)
    {
        if (item is null)
            return;

        var target = FindClientOption(item.Client);
        if (target is null)
            return;

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
            return;

        var parsed = TryParseDrugSpecFromAbnormalDetail(item.Detail);
        if (parsed is not null)
            await ApplyDrugSpecFilterAndReloadAsync(parsed.Value.DrugId, parsed.Value.Spec);

        if (IsInventoryAbnormal(item))
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

    private DashboardQuery BuildQuery(int topN)
    {
        var range = CurrentRange;
        var from = range.From;
        var to = range.To;

        var raw = SelectedClient?.Raw;
        var client = string.IsNullOrWhiteSpace(raw) ? null : raw;

        var metric = TrendMode?.Title == "按事务次数"
            ? TrendMetric.Txn
            : TrendMetric.Qty;

        var drug = NormalizeInput(DrugText);

        var spec = string.IsNullOrWhiteSpace(SelectedSpec.Raw) ? null : SelectedSpec.Raw;

        return new DashboardQuery(
            Range: new DateRange(from, to),
            ClientName: client,
            DrugId: drug,
            Spec: spec,
            TopN: topN,
            TrendMetric: metric
        );
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
        var qTop = BuildQuery(DefaultTopN);
        var qPaged = BuildQuery(0);
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
                    if (showTxnBusy) IsTxnBusy = v;
                    if (showEntryBusy) IsEntryBusy = v;
                    if (showAbnormalBusy) IsAbnormalBusy = v;
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
                            await ReloadSpecsAsync(NormalizeInput(DrugText)!).ConfigureAwait(false);
                    }
                    catch (System.Exception ex)
                    {
                        LogWarn("dashboard.reload.lazy_filters_fail", "Failed lazy loading filters during reload", ex);
                    }
                }

                var clientNames = await _repo.GetClientNamesAsync(ct).ConfigureAwait(false);

                var kpiTask = _repo.GetKpisAsync(qTop, ct);
                var trendTask = _repo.GetTrendPageAsync(qTop, page: 1, pageSize: qTop.TopN, ct);
                var txnOverviewTask = _repo.GetRecentTxnsPageAsync(qTop, page: 1, pageSize: DefaultTopN, ct);
                var txnPageTask = _repo.GetRecentTxnsPageAsync(qPaged, page: TxnPageIndex, pageSize: TxnPageSize, ct);
                var txnTrendPageTask = _repo.GetTrendPageAsync(qPaged, page: TxnTrendPageIndex, pageSize: TxnTrendPageSize, ct);
                var entryOverviewTask = _repo.GetEntryLogsPageAsync(qTop, page: 1, pageSize: EntryOverviewTopN, ct);
                var entryPageTask = _repo.GetEntryLogsPageAsync(qPaged, page: EntryPageIndex, pageSize: EntryPageSize, ct);
                var topClientsTask = _repo.GetClientsAsync(qTop, ct);
                var abnormalTask = _repo.GetAbnormalQueuePageAsync(qPaged, page: AbnormalPageIndex, pageSize: AbnormalPageSize, ct);

                await Task.WhenAll(
                        kpiTask,
                        trendTask,
                        txnOverviewTask,
                        txnPageTask,
                        txnTrendPageTask,
                        entryOverviewTask,
                        entryPageTask,
                        topClientsTask,
                        abnormalTask)
                    .ConfigureAwait(false);

                var loaded = (
                    clientNames,
                    kpi: await kpiTask.ConfigureAwait(false),
                    trend: (await trendTask.ConfigureAwait(false)).Rows,
                    txnsOverview: await txnOverviewTask.ConfigureAwait(false),
                    txnsPage: await txnPageTask.ConfigureAwait(false),
                    txnTrendPage: await txnTrendPageTask.ConfigureAwait(false),
                    entriesOverview: await entryOverviewTask.ConfigureAwait(false),
                    entriesPage: await entryPageTask.ConfigureAwait(false),
                    topClients: await topClientsTask.ConfigureAwait(false),
                    abnormal: await abnormalTask.ConfigureAwait(false));

                await RunOnUiAsync(() =>
                {
                    ApplyClients(loaded.clientNames);

                    ApplyKpi(loaded.kpi);
                    ApplyTrend(loaded.trend);
                    ApplyRecentTxnsOverview(loaded.txnsOverview.Rows);
                    ApplyRecentTxnsPage(loaded.txnsPage.Rows, loaded.txnsPage.TotalCount);
                    ApplyTxnTrendPage(loaded.txnTrendPage.Rows, loaded.txnTrendPage.TotalCount);
                    ApplyEntryLogsOverview(loaded.entriesOverview.Rows);
                    ApplyEntryLogsPage(loaded.entriesPage.Rows, loaded.entriesPage.TotalCount);
                    ApplyTopClients(loaded.topClients);
                    ApplyAbnormalQueue(loaded.abnormal.Rows, loaded.abnormal.TotalCount);

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
            if (!IsDbConnected || ct.IsCancellationRequested)
                return;

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
                Clients.Add(ParseClientWithAlias(raw));

            SelectedClient = Clients.FirstOrDefault(c => c.Raw == selectedRaw) ?? AllClients;
        }
    }

    private ClientInfo ParseClientWithAlias(string raw)
    {
        var ci = ClientParser.Parse(raw);
        var machine = (ci.Machine ?? raw).Trim();

        var display = _clientAlias.Resolve(machine);
        if (string.IsNullOrWhiteSpace(display))
            display = machine.Length > 0 ? machine : (ci.Display);

        return new ClientInfo(
            Raw: ci.Raw,
            Display: display,
            Machine: ci.Machine,
            User: ci.User,
            Ip: ci.Ip,
            Os: ci.Os,
            Version: ci.Version
        );
    }

    [RelayCommand]
    private async Task ApplyDrugFilterAsync()
    {
        if (ShouldSkipTrigger("dashboard.filter.apply", 350))
            return;

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
        }

        await ReloadNow();
    }

    [RelayCommand]
    private async Task ClearDrugSpecFilterAsync()
    {
        if (ShouldSkipTrigger("dashboard.filter.clear", 350))
            return;

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

    private void ApplyKpi(DashboardKpiDto dto)
    {
        Kpi.AvailableRemain = dto.AvailableRemain.ToString("N0", CultureInfo.CurrentCulture);
        Kpi.PeriodUsed = dto.PeriodUsed.ToString("N0", CultureInfo.CurrentCulture);
        Kpi.LowStockCount = dto.LowStockCount.ToString("N0", CultureInfo.CurrentCulture);
        Kpi.Abnormal = dto.Abnormal.ToString("N0", CultureInfo.CurrentCulture);

        static double Pct(long num, long den)
        {
            if (den <= 0) return 0;
            var v = num * 100.0 / den;
            if (v < 0) return 0;
            if (v > 100) return 100;
            return v;
        }

        Kpi.AvailableRemainPct = Pct(dto.AvailableRemain, dto.TotalQty);
        Kpi.PeriodUsedPct = Pct(dto.PeriodUsed, dto.TotalQty);
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

    private void ApplyTrend(IReadOnlyList<TrendRowDto> rows)
    {
        DrugTrend.Clear();
        foreach (var r in rows)
        {
            DrugTrend.Add(new TrendDrugItem
            {
                Rank = r.Rank.ToString(CultureInfo.CurrentCulture),
                Name = r.Name,
                Sub = r.Sub,
                SourceText = BuildTrendSourceText(r.TopClientRaw, r.TopClientPct),
                ValueText = r.ValueText
            });
        }

        OnPropertyChanged(nameof(IsTrendEmpty));
    }

    private void ApplyTxnTrendPage(IReadOnlyList<TrendRowDto> rows, int totalCount)
    {
        TxnTrendRows.Clear();
        TxnTrendTotalCount = totalCount;
        foreach (var r in rows)
        {
            TxnTrendRows.Add(new TrendDrugItem
            {
                Rank = r.Rank.ToString(CultureInfo.CurrentCulture),
                Name = r.Name,
                Sub = r.Sub,
                SourceText = BuildTrendSourceText(r.TopClientRaw, r.TopClientPct),
                ValueText = r.ValueText
            });
        }

        OnPropertyChanged(nameof(IsTxnTrendEmpty));
        OnPropertyChanged(nameof(IsTxnPanelEmpty));
    }

    private void ApplyTopClients(IReadOnlyList<(string Client, long Value)> rows)
    {
        TopClients.Clear();

        var idx = 1;
        foreach (var r in rows)
        {
            var client = ParseClientWithAlias(r.Client);
            TopClients.Add(new TopClientItem(
                Index: idx++,
                Client: client,
                Value: r.Value.ToString("N0", CultureInfo.CurrentCulture)
            ));
        }

        OnPropertyChanged(nameof(IsTopClientsEmpty));
    }

    private void ApplyRecentTxnsOverview(IReadOnlyList<TraceTxnDto> rows)
    {
        RecentTxnsOverview.Clear();
        foreach (var t in rows)
        {
            var item = new TxnItem(
                Id: t.Id,
                Badge: t.Badge,
                Title: t.Title,
                Qty: t.Qty.ToString("N0", CultureInfo.CurrentCulture),
                Time: t.CreatedAt.ToLocalTime().ToString("MM-dd HH:mm:ss", CultureInfo.CurrentCulture),
                ClientDisplay: string.IsNullOrWhiteSpace(t.ClientName) ? "-" : t.ClientName
            );
            RecentTxnsOverview.Add(item);
        }
    }

    private void ApplyRecentTxnsPage(IReadOnlyList<TraceTxnDto> rows, int totalCount)
    {
        RecentTxns.Clear();
        TxnTotalCount = totalCount;
        foreach (var t in rows)
        {
            var item = new TxnItem(
                Id: t.Id,
                Badge: t.Badge,
                Title: t.Title,
                Qty: t.Qty.ToString("N0", CultureInfo.CurrentCulture),
                Time: t.CreatedAt.ToLocalTime().ToString("MM-dd HH:mm:ss", CultureInfo.CurrentCulture),
                ClientDisplay: string.IsNullOrWhiteSpace(t.ClientName) ? "-" : t.ClientName
            );

            RecentTxns.Add(item);
        }

        OnPropertyChanged(nameof(IsRecentTxnsEmpty));
        OnPropertyChanged(nameof(IsTxnPanelEmpty));
    }

    private void ApplyEntryLogsOverview(IReadOnlyList<TraceEntryLogDto> rows)
    {
        EntryRecentOverview.Clear();
        foreach (var e in rows.OrderByDescending(x => x.EntryAt))
        {
            var client = ParseClientWithAlias(e.Client);
            EntryRecentOverview.Add(EntryRecentItem.From(e, client));
        }
    }

    private void ApplyEntryLogsPage(IReadOnlyList<TraceEntryLogDto> rows, int totalCount)
    {
        EntryRecent.Clear();
        EntryTotalCount = totalCount;
        foreach (var e in rows.OrderByDescending(x => x.EntryAt))
        {
            var client = ParseClientWithAlias(e.Client);
            EntryRecent.Add(EntryRecentItem.From(e, client));
        }

        OnPropertyChanged(nameof(IsEntryRecentEmpty));
    }

    private void ApplyAbnormalQueue(IReadOnlyList<AbnormalRowDto> rows, int totalCount)
    {
        AbnormalQueue.Clear();
        AbnormalTotalCount = totalCount;
        foreach (var row in rows)
        {
            AbnormalQueue.Add(new AbnormalItem(
                Title: row.Title,
                Detail: row.Detail,
                ClientDisplay: string.IsNullOrWhiteSpace(row.ClientDisplay) ? "-" : row.ClientDisplay,
                Badge: row.Badge
            ));
        }

        OnPropertyChanged(nameof(IsAbnormalEmpty));
    }

    private static string BuildTrendSourceText(string? rawClient, decimal pct)
    {
        if (string.IsNullOrWhiteSpace(rawClient))
            return "未知客户端";

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
                    var match = SpecOptions.FirstOrDefault(x =>
                        string.Equals(x.Raw, specText, StringComparison.OrdinalIgnoreCase));
                    if (match is not null)
                        SelectedSpec = match;
                });
            }
        }

        await ReloadNow();
    }

    private ClientInfo? FindClientOption(ClientInfo selected)
    {
        var raw = selected.Raw;
        if (!string.IsNullOrWhiteSpace(raw))
        {
            var hit = Clients.FirstOrDefault(c =>
                string.Equals(c.Raw, raw, StringComparison.OrdinalIgnoreCase));
            if (hit is not null)
                return hit;
        }

        var machine = NormalizeInput(selected.Machine ?? selected.Display);
        if (string.IsNullOrWhiteSpace(machine))
            return null;

        return Clients.FirstOrDefault(c =>
        {
            var cm = NormalizeInput(c.Machine ?? c.Display);
            return string.Equals(cm, machine, StringComparison.OrdinalIgnoreCase);
        });
    }

    private static bool IsInventoryAbnormal(AbnormalItem item)
    {
        var title = NormalizeInput(item.Title) ?? string.Empty;
        return title.Contains("库存", StringComparison.OrdinalIgnoreCase) ||
               title.Contains("告紧", StringComparison.OrdinalIgnoreCase) ||
               title.Contains("low", StringComparison.OrdinalIgnoreCase);
    }

    private static (string DrugId, string Spec)? TryParseDrugSpecFromAbnormalDetail(string? detail)
    {
        var s = NormalizeInput(detail);
        if (string.IsNullOrWhiteSpace(s))
            return null;

        var head = s.Split('·', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)[0];
        if (string.IsNullOrWhiteSpace(head))
            return null;

        var idx = head.LastIndexOf(' ');
        if (idx <= 0 || idx >= head.Length - 1)
            return null;

        var drug = NormalizeInput(head[..idx]);
        var spec = NormalizeInput(head[(idx + 1)..]);
        if (string.IsNullOrWhiteSpace(drug) || string.IsNullOrWhiteSpace(spec))
            return null;

        return (drug, spec);
    }

    private static (string DrugId, string Spec)? TryParseDrugSpecFromTxnTitle(string? title)
    {
        var s = NormalizeInput(title);
        if (string.IsNullOrWhiteSpace(s))
            return null;

        var idx = s.LastIndexOf(' ');
        if (idx <= 0 || idx >= s.Length - 1)
            return null;

        var drug = NormalizeInput(s[..idx]);
        var spec = NormalizeInput(s[(idx + 1)..]);
        if (string.IsNullOrWhiteSpace(drug) || string.IsNullOrWhiteSpace(spec))
            return null;

        return (drug, spec);
    }

    [RelayCommand]
    private async Task FirstEntryPageAsync()
    {
        if (ShouldSkipTrigger("dashboard.entry.first", 180))
            return;

        if (!HasEntryPrevPage) return;
        EntryPageIndex = 1;
        await ReloadEntryPageOnlyAsync();
    }

    [RelayCommand]
    private async Task PrevEntryPageAsync()
    {
        if (ShouldSkipTrigger("dashboard.entry.prev", 180))
            return;

        if (!HasEntryPrevPage) return;
        EntryPageIndex--;
        await ReloadEntryPageOnlyAsync();
    }

    [RelayCommand]
    private async Task NextEntryPageAsync()
    {
        if (ShouldSkipTrigger("dashboard.entry.next", 180))
            return;

        if (!HasEntryNextPage) return;
        EntryPageIndex++;
        await ReloadEntryPageOnlyAsync();
    }

    [RelayCommand]
    private async Task FirstTxnPageAsync()
    {
        if (ShouldSkipTrigger("dashboard.txn.first", 180))
            return;

        if (!HasTxnPrevPage) return;
        TxnPageIndex = 1;
        await ReloadTxnPageOnlyAsync();
    }

    [RelayCommand]
    private async Task PrevTxnPageAsync()
    {
        if (ShouldSkipTrigger("dashboard.txn.prev", 180))
            return;

        if (!HasTxnPrevPage) return;
        TxnPageIndex--;
        await ReloadTxnPageOnlyAsync();
    }

    [RelayCommand]
    private async Task NextTxnPageAsync()
    {
        if (ShouldSkipTrigger("dashboard.txn.next", 180))
            return;

        if (!HasTxnNextPage) return;
        TxnPageIndex++;
        await ReloadTxnPageOnlyAsync();
    }

    [RelayCommand]
    private async Task FirstTxnTrendPageAsync()
    {
        if (ShouldSkipTrigger("dashboard.txntrend.first", 180))
            return;

        if (!HasTxnTrendPrevPage) return;
        TxnTrendPageIndex = 1;
        await ReloadTxnTrendPageOnlyAsync();
    }

    [RelayCommand]
    private async Task PrevTxnTrendPageAsync()
    {
        if (ShouldSkipTrigger("dashboard.txntrend.prev", 180))
            return;

        if (!HasTxnTrendPrevPage) return;
        TxnTrendPageIndex--;
        await ReloadTxnTrendPageOnlyAsync();
    }

    [RelayCommand]
    private async Task NextTxnTrendPageAsync()
    {
        if (ShouldSkipTrigger("dashboard.txntrend.next", 180))
            return;

        if (!HasTxnTrendNextPage) return;
        TxnTrendPageIndex++;
        await ReloadTxnTrendPageOnlyAsync();
    }

    [RelayCommand]
    private async Task FirstAbnormalPageAsync()
    {
        if (ShouldSkipTrigger("dashboard.abnormal.first", 180))
            return;

        if (!HasAbnormalPrevPage) return;
        AbnormalPageIndex = 1;
        await ReloadAbnormalPageOnlyAsync();
    }

    [RelayCommand]
    private async Task PrevAbnormalPageAsync()
    {
        if (ShouldSkipTrigger("dashboard.abnormal.prev", 180))
            return;

        if (!HasAbnormalPrevPage) return;
        AbnormalPageIndex--;
        await ReloadAbnormalPageOnlyAsync();
    }

    [RelayCommand]
    private async Task NextAbnormalPageAsync()
    {
        if (ShouldSkipTrigger("dashboard.abnormal.next", 180))
            return;

        if (!HasAbnormalNextPage) return;
        AbnormalPageIndex++;
        await ReloadAbnormalPageOnlyAsync();
    }

    private async Task ReloadTxnPageOnlyAsync()
    {
        var q = BuildQuery(0);
        try
        {
            await RunLocalBusyAsync(
                CancellationToken.None,
                setBusy: v => IsTxnBusy = v,
                showBusy: true,
                body: async () =>
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                var page = await _repo.GetRecentTxnsPageAsync(q, TxnPageIndex, TxnPageSize, cts.Token).ConfigureAwait(false);
                await RunOnUiAsync(() =>
                {
                    ApplyRecentTxnsPage(page.Rows, page.TotalCount);
                    OnPropertyChanged(nameof(IsTxnPanelEmpty));
                }, DispatcherPriority.Background);
            });
        }
        catch (Exception ex)
        {
            LogError("dashboard.txn_page.reload_fail", "Failed to reload transaction page", ex);
            if (IsDbConnected)
                PostOnUi(() => _toast.Error("事务列表加载失败", ex.Message));
        }
    }

    private async Task ReloadTxnTrendPageOnlyAsync()
    {
        var q = BuildQuery(0);
        try
        {
            await RunLocalBusyAsync(
                CancellationToken.None,
                setBusy: v => IsTxnBusy = v,
                showBusy: true,
                body: async () =>
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                var page = await _repo.GetTrendPageAsync(q, TxnTrendPageIndex, TxnTrendPageSize, cts.Token).ConfigureAwait(false);
                await RunOnUiAsync(() =>
                {
                    ApplyTxnTrendPage(page.Rows, page.TotalCount);
                    OnPropertyChanged(nameof(IsTxnPanelEmpty));
                }, DispatcherPriority.Background);
            });
        }
        catch (Exception ex)
        {
            LogError("dashboard.txn_trend.reload_fail", "Failed to reload transaction trend page", ex);
            if (IsDbConnected)
                PostOnUi(() => _toast.Error("事务趋势加载失败", ex.Message));
        }
    }

    private async Task ReloadEntryPageOnlyAsync()
    {
        var q = BuildQuery(DefaultTopN);
        try
        {
            await RunLocalBusyAsync(
                CancellationToken.None,
                setBusy: v => IsEntryBusy = v,
                showBusy: true,
                body: async () =>
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                var page = await _repo.GetEntryLogsPageAsync(q, EntryPageIndex, EntryPageSize, cts.Token).ConfigureAwait(false);
                await RunOnUiAsync(() =>
                {
                    ApplyEntryLogsPage(page.Rows, page.TotalCount);
                }, DispatcherPriority.Background);
            });
        }
        catch (Exception ex)
        {
            LogError("dashboard.entry_page.reload_fail", "Failed to reload entry page", ex);
            if (IsDbConnected)
                PostOnUi(() => _toast.Error("录入列表加载失败", ex.Message));
        }
    }

    private async Task ReloadAbnormalPageOnlyAsync()
    {
        var q = BuildQuery(DefaultTopN);
        try
        {
            await RunLocalBusyAsync(
                CancellationToken.None,
                setBusy: v => IsAbnormalBusy = v,
                showBusy: true,
                body: async () =>
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                var page = await _repo.GetAbnormalQueuePageAsync(q, AbnormalPageIndex, AbnormalPageSize, cts.Token).ConfigureAwait(false);
                await RunOnUiAsync(() =>
                {
                    ApplyAbnormalQueue(page.Rows, page.TotalCount);
                }, DispatcherPriority.Background);
            });
        }
        catch (Exception ex)
        {
            LogError("dashboard.abnormal_page.reload_fail", "Failed to reload abnormal page", ex);
            if (IsDbConnected)
                PostOnUi(() => _toast.Error("异常列表加载失败", ex.Message));
        }
    }

    [RelayCommand]
    private void Export()
    {
    }

    [RelayCommand]
    private void OpenTxnList()
    {
        if (ShouldSkipTrigger("dashboard.nav.txn", 250))
            return;

        SelectedTabIndex = 2;
    }

    [RelayCommand]
    private void OpenInventory()
    {
        if (ShouldSkipTrigger("dashboard.nav.inventory", 300))
            return;

        _inventoryOverview.OpenMode(0);
        _nav.Navigate<InventoryOverviewViewModel>();
    }

    [RelayCommand]
    private void OpenAbnormal()
    {
        if (ShouldSkipTrigger("dashboard.nav.abnormal", 250))
            return;

        SelectedTabIndex = 3;
    }

    [RelayCommand]
    private void GoInputTab()
    {
        if (ShouldSkipTrigger("dashboard.nav.input", 300))
            return;

        _nav.Navigate<ScanCodeViewModel>();
    }

    [RelayCommand]
    private void OpenPeriodUsage()
    {
        if (ShouldSkipTrigger("dashboard.nav.period", 250))
            return;

        SelectedTabIndex = 2;
    }

    [RelayCommand]
    private void OpenLowStock()
    {
        if (ShouldSkipTrigger("dashboard.nav.lowstock", 300))
            return;

        _inventoryOverview.OpenMode(2);
        _nav.Navigate<InventoryOverviewViewModel>();
    }

    [RelayCommand]
    private void OpenInputHistory()
    {
        if (ShouldSkipTrigger("dashboard.nav.inputhistory", 250))
            return;

        SelectedTabIndex = 1;
    }

    [RelayCommand]
    private void OpenOverviewTab()
    {
        if (ShouldSkipTrigger("dashboard.nav.overview", 250))
            return;

        SelectedTabIndex = 0;
    }

    public override void Dispose()
    {
        SafeExecute(() => _clientAlias.Changed -= OnClientAliasChanged);

        if (_debounce is not null)
        {
            SafeExecute(() => _debounce.Stop());

            _debounce = null;
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
                ApplyClients(Clients.Select(c => c.Raw).Where(r => !string.IsNullOrWhiteSpace(r)).ToList());

            if (!string.IsNullOrWhiteSpace(selectedRaw))
                SelectedClient = Clients.FirstOrDefault(c => string.Equals(c.Raw, selectedRaw, StringComparison.OrdinalIgnoreCase)) ?? AllClients;

            if (TopClients.Count > 0)
            {
                var remappedTop = TopClients
                    .Select(x => x with { Client = ParseClientWithAlias(x.Client.Raw) })
                    .ToList();
                TopClients.Clear();
                foreach (var item in remappedTop)
                    TopClients.Add(item);
            }

            if (EntryRecentOverview.Count > 0)
            {
                var remappedOverview = EntryRecentOverview
                    .Select(x => x.WithClient(ParseClientWithAlias(x.ClientRaw)))
                    .ToList();
                EntryRecentOverview.Clear();
                foreach (var item in remappedOverview)
                    EntryRecentOverview.Add(item);
            }

            if (EntryRecent.Count > 0)
            {
                var remappedPage = EntryRecent
                    .Select(x => x.WithClient(ParseClientWithAlias(x.ClientRaw)))
                    .ToList();
                EntryRecent.Clear();
                foreach (var item in remappedPage)
                    EntryRecent.Add(item);
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
    [ObservableProperty] private string _periodUsedHint = "统计区间内已使用的追溯码数量";
    [ObservableProperty] private string _abnormalHint = "统计区间内发生回滚/异常的事务数量";
    [ObservableProperty] private string _lowStockHint = "库存剩余量低于阈值的药品数量";

    [ObservableProperty] private double _availableRemainPct;
    [ObservableProperty] private double _periodUsedPct;
    [ObservableProperty] private double _abnormalPct;
    [ObservableProperty] private double _lowStockPct;
}

public sealed partial class TrendDrugItem : ObservableObject
{
    [ObservableProperty] private string _rank = "";
    [ObservableProperty] private string _name = "";
    [ObservableProperty] private string _sub = "";
    [ObservableProperty] private string _sourceText = "";
    [ObservableProperty] private string _valueText = "";
}

public sealed record TxnItem(long Id, TxnBadge Badge, string Title, string Qty, string Time, string ClientDisplay);
public sealed record AbnormalItem(string Title, string Detail, string ClientDisplay, TxnBadge Badge);

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
            if (!string.IsNullOrWhiteSpace(User)) parts.Add(User!);
            if (!string.IsNullOrWhiteSpace(Ip)) parts.Add(Ip!);
            if (!string.IsNullOrWhiteSpace(Os)) parts.Add(Os!);
            if (!string.IsNullOrWhiteSpace(Ver)) parts.Add(Ver!);
            return string.Join(" · ", parts);
        }
    }

    public bool HasMeta => !string.IsNullOrWhiteSpace(MetaText);
}

public sealed record EntryRecentItem(
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
    public static EntryRecentItem From(TraceEntryLogDto e, ClientInfo client)
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
            return string.IsNullOrWhiteSpace(rawMessage)
                ? $"{sourceText} · {resultText}"
                : $"{sourceText} · {resultText} · {rawMessage}";

        return
            $"{sourceText} · {resultText} · 总数 {parsed.Value.Total} · 成功 {parsed.Value.Valid} · 重复 {parsed.Value.Duplicate} · 无效 {parsed.Value.Invalid} · 写入 {parsed.Value.Inserted} · 跳过 {parsed.Value.Skipped}";
    }

    private static (int Total, int Valid, int Duplicate, int Invalid, int Inserted, int Skipped)? ParseSummary(string? rawMessage)
    {
        if (string.IsNullOrWhiteSpace(rawMessage))
            return null;

        var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var parts = rawMessage.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        foreach (var part in parts)
        {
            var idx = part.IndexOf('=');
            if (idx <= 0 || idx >= part.Length - 1)
                continue;

            var key = NormalizeSummaryKey(part[..idx].Trim());
            var val = part[(idx + 1)..].Trim();
            if (int.TryParse(val, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number))
                map[key] = number;
        }

        if (!map.TryGetValue("input", out var total) ||
            !map.TryGetValue("valid", out var valid) ||
            !map.TryGetValue("duplicate", out var duplicate) ||
            !map.TryGetValue("invalid", out var invalid) ||
            !map.TryGetValue("inserted", out var inserted) ||
            !map.TryGetValue("skipped", out var skipped))
            return null;

        return (total, valid, duplicate, invalid, inserted, skipped);
    }

    private static string NormalizeSummaryKey(string rawKey)
    {
        if (string.IsNullOrWhiteSpace(rawKey))
            return string.Empty;

        var key = rawKey.Trim().ToLowerInvariant();
        if (key.EndsWith(" input", StringComparison.Ordinal))
            return "input";
        return key;
    }
}

public enum GridInteractionType
{
    Browsing = 0,
    Editing = 1
}

public sealed record GridRowActionRule(string GridKey, GridInteractionType Type, string Action);
