using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using global::Avalonia.Threading;
using PacToolkits.Application.Abstractions;
using PacToolkits.Application.DTOs;
using PacToolkits.Desktop.Avalonia.Common;
using PacToolkits.Desktop.Avalonia.Contracts;
using PacToolkits.Desktop.Avalonia.Services.Infrastructure;

namespace PacToolkits.Desktop.Avalonia.ViewModels.Pages;

/// <summary>
/// 协调追溯码库存明细、规格汇总、低库存与缺失列表，以及编辑、改派和导入导出
/// </summary>
public sealed partial class InventoryOverview : AppPageBase, IInventoryRefreshPage
{
    private static readonly TimeSpan LookupTimeout = TimeSpan.FromSeconds(8);
    // 写库后短暂停住 LISTEN→全量 Reload；静默对账立刻拉
    private static readonly TimeSpan PostWriteAutoRefreshPause = TimeSpan.FromSeconds(2);
    private const string OpsScope = UnlockScopes.SharedOps;
    private static readonly int[] PageSizeOptionValues = [20, 50, 100];

    public override string DisplayName => "追溯码库存";
    public override string Icon => "Barcode";
    public override int Index => 1;
    public override ICommand RefreshCommand => _localRefreshCommand;
    public override ICommand ImportCommand => _importCommand;
    public override ICommand ExportCommand => _exportCommand;
    protected override bool AutoRefreshOnDbDisconnected => true;
    protected override bool AutoRefreshOnDbReconnected => true;

    private readonly IInventoryOverviewService _inventory;
    private readonly ILookupCatalogService _lookup;
    private readonly ITraceCodeRuleService _traceCodeRule;
    private readonly IDbConfigNotifier _dbConfigNotifier;
    private readonly ISensitiveUnlockService _unlockService;
    private readonly IToastService _toast;
    private readonly IDialogService _dialog;
    private readonly PageNavigationService _nav;
    private readonly ScanCode _scanCode;
    private readonly AsyncRelayCommand _localRefreshCommand;
    private readonly AsyncRelayCommand _importCommand;
    private readonly AsyncRelayCommand _exportCommand;

    public ObservableCollection<StockRowItem> StockRows { get; } = new();
    public ObservableCollection<DrugSpecAggRowItem> DrugSpecRows { get; } = new();
    public ObservableCollection<LowStockRowItem> LowStockRows { get; } = new();
    public ObservableCollection<MissingStockRowItem> MissingStockRows { get; } = new();
    public ObservableCollection<StockReassignPreviewRowItem> PreviewRows { get; } = new();
    public ObservableCollection<OptionItem> DrugOptions { get; } = new();
    public ObservableCollection<OptionItem> SpecOptions { get; } = new();
    private IReadOnlyList<OptionItem> _drugCatalog = [];
    public ObservableCollection<int> PageSizeOptions { get; } = new(PageSizeOptionValues);

    [ObservableProperty] private int _modeIndex;
    [ObservableProperty] private string? _keyword;
    [ObservableProperty] private int _pageIndex = 1;
    [ObservableProperty] private int _pageSize = 50;
    [ObservableProperty] private int _totalCount;
    [ObservableProperty] private bool _isDetailBusy;
    [ObservableProperty] private bool _isAggBusy;
    [ObservableProperty] private bool _isLowBusy;
    [ObservableProperty] private bool _isMissingBusy;
    [ObservableProperty] private bool _isDetailGridMounted;
    [ObservableProperty] private bool _isAggGridMounted;
    [ObservableProperty] private bool _isLowGridMounted;
    [ObservableProperty] private bool _isMissingGridMounted;
    [ObservableProperty] private bool _isStockEditEnabled;
    [ObservableProperty] private bool _isOpsUnlocked;
    private DateTimeOffset _opsCooldownUntilUtc;
    [ObservableProperty] private bool _isReassignOpen;
    [ObservableProperty] private string? _drugText;
    [ObservableProperty] private OptionItem? _selectedSpec;
    [ObservableProperty] private bool _isDrugSuggestOpen;
    [ObservableProperty] private bool _isSpecSelected;
    [ObservableProperty] private string? _qtyText;
    [ObservableProperty] private string? _targetDrugId;
    [ObservableProperty] private string? _targetSpec;
    [ObservableProperty] private string? _correctionReason;
    [ObservableProperty] private string? _previewStatsText;
    [ObservableProperty] private string? _previewNoticeText;
    [ObservableProperty] private bool _isPanelBusy;
    [ObservableProperty] private int _scopeIndex;
    private bool _reassignPreviewLive;
    private int _reassignContextSyncDepth;
    private string? _lastValidatedPreviewTargetDrug;
    private string? _lastValidatedPreviewTargetSpec;
    private CancellationTokenSource? _previewRefreshCts;
    private readonly Collection<StockRowEditRequest> _pendingStockEdits = new();
    private readonly Dictionary<int, StockEditSnapshot> _stockEditSnapshotByRow = new();
    private readonly Dictionary<string, StockRowSelection> _selectedStockRowsByTrace = new(StringComparer.Ordinal);
    private IReadOnlyList<StockRowSelection> _selectedStockRowsSnapshot = Array.Empty<StockRowSelection>();
    private int _lastModeIndex;
    private DateTimeOffset _suppressAutoRefreshUntilUtc = DateTimeOffset.MinValue;
    private bool _flushRefreshAfterStockEdit;
    private CancellationTokenSource? _silentReconcileCts;
    private CancellationTokenSource? _stockEditRemoteCts;
    private readonly Dictionary<string, TracePoolStockRowDto> _remoteStockBaselineByTrace = new(StringComparer.Ordinal);
    private bool _remoteStockMissingAttention;
    private bool _stockEditSaveInFlight;
    private int _detailStockEpoch;
    private readonly SearchInputDebouncer _keywordSearchDebouncer = new(450);
    private readonly DispatcherTimer _unlockStatusTimer;
    private IRelayCommand?[]? _notifiableCommands;
    partial void OnIsDetailBusyChanged(bool value)
        => NotifySectionPendingChanged();

    partial void OnIsAggBusyChanged(bool value)
        => NotifySectionPendingChanged();

    partial void OnIsLowBusyChanged(bool value)
        => NotifySectionPendingChanged();

    partial void OnIsMissingBusyChanged(bool value)
        => NotifySectionPendingChanged();

    partial void OnIsDetailGridMountedChanged(bool value)
        => NotifySectionPendingChanged();

    partial void OnIsAggGridMountedChanged(bool value)
        => NotifySectionPendingChanged();

    partial void OnIsLowGridMountedChanged(bool value)
        => NotifySectionPendingChanged();

    partial void OnIsMissingGridMountedChanged(bool value)
        => NotifySectionPendingChanged();
    partial void OnIsStockEditEnabledChanged(bool value)
    {
        if (value)
        {
            IsReassignOpen = false;
            ClearPreviewMessaging();
            PreviewRows.Clear();
            NotifyPreviewStateChanged();
        }
        else
        {
            DisableStockTraceCodeValidation();
        }

        OnPropertyChanged(nameof(CanEnableStockEdit));
        OnPropertyChanged(nameof(CanDisableStockEdit));
        OnPropertyChanged(nameof(ShowUnlock));
        OnPropertyChanged(nameof(ShowLock));
        OnPropertyChanged(nameof(CanToggleReassign));
        NotifyEditState();
        RefreshPageCommands();
    }

    partial void OnIsOpsUnlockedChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowUnlock));
        OnPropertyChanged(nameof(ShowLock));
        RefreshPageCommands();
    }

    public bool IsDetailMode => ModeIndex == 0;
    public bool IsAggMode => ModeIndex == 1;
    public bool IsLowMode => ModeIndex == 2;
    public bool IsMissingMode => ModeIndex == 3;
    public bool IsSingleScope => ScopeIndex == 0;
    public bool IsFilterScope => ScopeIndex == 1;
    public bool IsPreviewEmpty => PreviewRows.Count == 0;
    public bool HasPreviewStatsText => !string.IsNullOrWhiteSpace(PreviewStatsText);
    public bool HasPreviewNoticeText => !string.IsNullOrWhiteSpace(PreviewNoticeText);
    public bool ShowReassignPreview => _reassignPreviewLive;
    public string ReassignPreviewToggleText => _reassignPreviewLive ? "关闭预览" : "预览影响";
    public bool ShowReassignRowSelection => IsReassignOpen && IsSingleScope && IsDetailMode;
    public int StockReassignSelectedCount => _selectedStockRowsByTrace.Count;
    public int StockPagerSelectedCount => ShowReassignRowSelection ? StockReassignSelectedCount : -1;
    public bool CanEnableStockEdit => IsDetailMode && !IsStockEditEnabled;
    public bool CanDisableStockEdit => IsDetailMode && IsStockEditEnabled;
    public bool ShowUnlock => IsDetailMode && !IsOpsUnlocked;
    public bool ShowLock => IsDetailMode && IsOpsUnlocked;
    public bool CanToggleReassign
        => IsDetailMode
           && !IsStockEditEnabled
           && CanOperateUi();
    public bool CanPreview
        => IsReassignOpen
           && (IsSingleScope
               ? GetEffectiveSelectedRows().Count > 0
               : !string.IsNullOrWhiteSpace(NormalizeInput(Keyword)))
           && !string.IsNullOrWhiteSpace(NormalizeInput(TargetDrugId))
           && !string.IsNullOrWhiteSpace(NormalizeInput(TargetSpec))
           && int.TryParse(NormalizeInput(QtyText), out var previewQty)
           && previewQty > 0
           && CanOperateUi();
    public bool CanTogglePreview
        => IsReassignOpen
           && CanOperateUi()
           && (_reassignPreviewLive || CanPreview);
    public bool CanApply
        => CanPreview
           && !string.IsNullOrWhiteSpace(NormalizeInput(CorrectionReason))
           && int.TryParse(NormalizeInput(QtyText), out var qty)
           && qty > 0;
    public string EditStateText
        => !IsDetailMode
            ? string.Empty
            : IsStockEditEnabled
                ? HasRemoteStockChange
                    ? "有他端变更"
                    : HasPendingChanges
                        ? "有未提交变更"
                        : "编辑中"
                : string.Empty;
    public bool HasActiveKeyword => !string.IsNullOrWhiteSpace(NormalizeInput(Keyword));

    public bool ShowEditState => IsDetailMode && IsStockEditEnabled;
    protected override void OnLookupCatalogSuspended()
    {
        DrugOptions.Clear();
        _drugCatalog = [];
        SpecOptions.Clear();
        IsDrugSuggestOpen = false;
        DrugText = null;
        TargetDrugId = null;
        SelectedSpec = null;
        TargetSpec = null;
        QtyText = null;
        IsSpecSelected = false;
    }

    protected override void OnPageAvailabilityChanged()
    {
        OnPropertyChanged(nameof(IsStockEmpty));
        OnPropertyChanged(nameof(StockEmptyText));
        OnPropertyChanged(nameof(StockEmptyHint));
        OnPropertyChanged(nameof(IsAggEmpty));
        OnPropertyChanged(nameof(AggEmptyText));
        OnPropertyChanged(nameof(AggEmptyHint));
        OnPropertyChanged(nameof(IsLowEmpty));
        OnPropertyChanged(nameof(LowEmptyText));
        OnPropertyChanged(nameof(LowEmptyHint));
        OnPropertyChanged(nameof(IsMissingEmpty));
        OnPropertyChanged(nameof(MissingEmptyText));
        OnPropertyChanged(nameof(MissingEmptyHint));
        NotifySectionPendingChanged();
    }

    public string StockEmptyText => GetSectionEmptyTitle("暂无库存明细");
    public string StockEmptyHint => GetSectionEmptyHint("当前筛选条件下没有库存明细");
    public string AggEmptyText => GetSectionEmptyTitle("暂无汇总数据");
    public string AggEmptyHint => GetSectionEmptyHint("当前筛选条件下没有汇总数据");
    public string LowEmptyText => GetSectionEmptyTitle("暂无低库存药品");
    public string LowEmptyHint => GetSectionEmptyHint("当前筛选条件下没有低库存药品");
    public string MissingEmptyText => GetSectionEmptyTitle("暂无缺失药品");
    public string MissingEmptyHint => GetSectionEmptyHint("当前筛选条件下没有缺失药品");

    public bool IsStockEmpty => ShowSectionEmpty(StockRows.Count == 0);
    public bool IsAggEmpty => ShowSectionEmpty(DrugSpecRows.Count == 0);
    public bool IsLowEmpty => ShowSectionEmpty(LowStockRows.Count == 0);
    public bool IsMissingEmpty => ShowSectionEmpty(MissingStockRows.Count == 0);
    public bool IsDetailSectionPending =>
        IsSectionPending || IsDetailBusy || (IsDetailMode && !IsDetailGridMounted);

    public bool IsAggSectionPending =>
        IsSectionPending || IsAggBusy || (IsAggMode && !IsAggGridMounted);

    public bool IsLowSectionPending =>
        IsSectionPending || IsLowBusy || (IsLowMode && !IsLowGridMounted);

    public bool IsMissingSectionPending =>
        IsSectionPending || IsMissingBusy || (IsMissingMode && !IsMissingGridMounted);
    public bool IsUiBusy => IsBusy || IsPanelBusy;
    public bool IsPagedMode => ModeIndex is 0 or 1 or 2 or 3;
    public int TotalPages => Math.Max(1, (int)Math.Ceiling(TotalCount / (double)PageSize));
    public bool HasPrevPage => IsPagedMode && PageIndex > 1;
    public bool HasNextPage => IsPagedMode && PageIndex < TotalPages;
    public bool HasPendingChanges => IsStockEditEnabled && _pendingStockEdits.Count > 0;
    public bool HasRemoteStockChange
        => IsStockEditEnabled
           && (_remoteStockBaselineByTrace.Count > 0 || _remoteStockMissingAttention);
    public bool HasEditAttention => HasPendingChanges || HasRemoteStockChange;
    public IReadOnlyList<StockRowSelection> SelectedStockRowsSnapshot => _selectedStockRowsSnapshot;

    public InventoryOverview(
        IInventoryOverviewService inventory,
        ILookupCatalogService lookup,
        ITraceCodeRuleService traceCodeRule,
        IDbConfigNotifier dbConfigNotifier,
        ISensitiveUnlockService unlockService,
        IToastService toast,
        IDialogService dialog,
        PageNavigationService nav,
        ScanCode scanCode)
    {
        _inventory = inventory;
        _lookup = lookup;
        _traceCodeRule = traceCodeRule;
        _dbConfigNotifier = dbConfigNotifier;
        _unlockService = unlockService;
        _toast = toast;
        _dialog = dialog;
        _nav = nav;
        _scanCode = scanCode;
        _localRefreshCommand = new AsyncRelayCommand(() => ReloadAsync(), CanLocalRefresh);
        _importCommand = new AsyncRelayCommand(ImportAsync, CanOperateUi);
        _exportCommand = new AsyncRelayCommand(ExportAsync, CanOperateUi);
        _unlockStatusTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _unlockStatusTimer.Tick += OnUnlockTimerTick;
        _unlockService.StateChanged += OnUnlockChanged;
        _traceCodeRule.Changed += OnTraceCodeRuleChanged;
        RefreshOpsUnlock();

        _dbConfigNotifier.Applied += OnDbApplied;
        _lastModeIndex = ModeIndex;

        PostOnUi(() => ObserveDetached(ReloadAsync(), "reload.detached.fail"), DispatcherPriority.Background);
    }

    private async Task ImportAsync()
    {
        if (SkipTrigger())
        {
            return;
        }

        await _dialog.Warn("未实现", "导入功能稍后接入格式选择/打开路径");
    }

    private async Task ExportAsync()
    {
        if (SkipTrigger())
        {
            return;
        }

        await _dialog.Warn("未实现", "导出功能稍后接入格式选择/保存路径");
    }

    public void SyncReassignSelectionFromRows()
    {
        if (!IsReassignOpen || !IsSingleScope || IsReassignContextSyncing)
        {
            return;
        }

        foreach (var row in StockRows)
        {
            var key = SelectionKey(row);
            if (key is null)
            {
                continue;
            }

            if (row.IsSelected)
            {
                _selectedStockRowsByTrace[key] = StockRowSelection.From(row);
            }
            else
            {
                _selectedStockRowsByTrace.Remove(key);
            }
        }

        RefreshReassignSelectionCounts();
        QueueReassignPreviewRefresh();
    }

    private void RestoreReassignChecksAfterPageLoad()
    {
        if (!IsReassignOpen || !IsSingleScope)
        {
            return;
        }

        foreach (var row in StockRows)
        {
            var key = SelectionKey(row);
            if (key is null)
            {
                row.IsSelected = false;
                continue;
            }

            if (_selectedStockRowsByTrace.ContainsKey(key))
            {
                row.IsSelected = true;
                _selectedStockRowsByTrace[key] = StockRowSelection.From(row);
            }
            else
            {
                row.IsSelected = false;
            }
        }

        RefreshReassignSelectionCounts();
    }

    private void RefreshReassignSelectionCounts()
    {
        _selectedStockRowsSnapshot = _selectedStockRowsByTrace.Values.ToArray();
        OnPropertyChanged(nameof(SelectedStockRowsSnapshot));
        OnPropertyChanged(nameof(StockReassignSelectedCount));
        OnPropertyChanged(nameof(StockPagerSelectedCount));
        RefreshPageCommands();
    }

    private static string? SelectionKey(StockRowItem row)
    {
        var trace = NormalizeInput(row.TraceCode);
        return string.IsNullOrWhiteSpace(trace) ? null : trace;
    }

    private void ClearReassignChecks()
    {
        foreach (var row in StockRows)
        {
            row.IsSelected = false;
        }

        _selectedStockRowsByTrace.Clear();
        _selectedStockRowsSnapshot = Array.Empty<StockRowSelection>();
        OnPropertyChanged(nameof(SelectedStockRowsSnapshot));
        OnPropertyChanged(nameof(StockReassignSelectedCount));
        OnPropertyChanged(nameof(StockPagerSelectedCount));
    }

    private void ApplyStockRowsInPlace(IReadOnlyList<StockRowItem> items)
    {
        if (IsReassignOpen && IsSingleScope)
        {
            foreach (var row in StockRows)
            {
                row.IsSelected = false;
            }
        }

        var sharedCount = Math.Min(StockRows.Count, items.Count);
        for (var i = 0; i < sharedCount; i++)
        {
            var target = StockRows[i];
            var source = items[i];
            target.RowNo = source.RowNo;
            target.DrugId = source.DrugId;
            target.Spec = source.Spec;
            target.TraceCode = source.TraceCode;
            target.Qty = source.Qty;
            target.Remain = source.Remain;
            target.Status = source.Status;
            target.Version = source.Version;
            target.IsLow = source.IsLow;
            target.IsDeprecated = source.IsDeprecated;
        }

        while (StockRows.Count > items.Count)
        {
            StockRows.RemoveAt(StockRows.Count - 1);
        }

        for (var i = StockRows.Count; i < items.Count; i++)
        {
            StockRows.Add(items[i]);
        }
    }

    partial void OnIsReassignOpenChanged(bool value)
    {
        if (!value)
        {
            SetReassignPreviewLive(false);
            _previewRefreshCts?.Cancel();
            ClearPreviewMessaging();
            PreviewRows.Clear();
            OnPropertyChanged(nameof(IsPreviewEmpty));
            ClearReassignChecks();
        }
        else
        {
            SetReassignPreviewLive(false);
            OnPropertyChanged(nameof(ShowReassignRowSelection));
            OnPropertyChanged(nameof(StockPagerSelectedCount));
            ObserveDetached(SyncDrugCatalogAsync(), "catalog.sync.detached.fail");
        }

        OnPropertyChanged(nameof(ShowReassignRowSelection));
        OnPropertyChanged(nameof(StockPagerSelectedCount));
        OnPropertyChanged(nameof(ShowOpenReassign));
        OnPropertyChanged(nameof(ShowCloseReassign));
        RefreshPageCommands();
    }

    partial void OnDrugTextChanged(string? value)
    {
        ClearDrugSpecFilterCommand.NotifyCanExecuteChanged();
        RefreshDrugOptionsOrder(value);

        var drug = NormalizeInput(value);
        IsDrugSuggestOpen = !string.IsNullOrWhiteSpace(drug);
        if (string.IsNullOrWhiteSpace(drug))
        {
            SpecOptions.Clear();
            TargetDrugId = null;
            SelectedSpec = null;
            TargetSpec = null;
            QtyText = null;
            IsSpecSelected = false;
        }

        RefreshPageCommands();
    }

    private void RefreshDrugOptionsOrder(string? searchText)
    {
        if (_drugCatalog.Count == 0)
        {
            return;
        }

        AutoCompleteFilter.RefreshVisibleOptions(
            DrugOptions,
            _drugCatalog,
            searchText);
    }

    partial void OnSelectedSpecChanged(OptionItem? value)
    {
        IsSpecSelected = value is not null;
        TargetSpec = NormalizeInput(value?.Raw);
        if (IsReassignContextSyncing)
        {
            return;
        }

        ObserveDetached(SyncQtyAsync(), "qty.sync.detached.fail");
    }

    partial void OnTargetDrugIdChanged(string? value)
    {
        if (IsReassignContextSyncing)
        {
            return;
        }

        RefreshPageCommands();
        QueueReassignPreviewRefresh();
    }

    partial void OnTargetSpecChanged(string? value)
    {
        if (IsReassignContextSyncing)
        {
            return;
        }

        RefreshPageCommands();
        QueueReassignPreviewRefresh();
    }

    partial void OnCorrectionReasonChanged(string? value)
        => RefreshPageCommands();

    partial void OnQtyTextChanged(string? value)
    {
        if (IsReassignContextSyncing)
        {
            return;
        }

        RefreshPageCommands();
        QueueReassignPreviewRefresh();
    }

    partial void OnPreviewStatsTextChanged(string? value)
        => OnPropertyChanged(nameof(HasPreviewStatsText));

    partial void OnPreviewNoticeTextChanged(string? value)
        => OnPropertyChanged(nameof(HasPreviewNoticeText));

    partial void OnIsPanelBusyChanged(bool value)
    {
        OnPropertyChanged(nameof(IsUiBusy));
        RefreshPageCommands();
    }

    protected override void OnBusyChanged(bool isBusy)
    {
        OnPropertyChanged(nameof(IsUiBusy));
        RefreshPageCommands();
    }

    partial void OnScopeIndexChanged(int value)
    {
        OnPropertyChanged(nameof(IsSingleScope));
        OnPropertyChanged(nameof(IsFilterScope));
        OnPropertyChanged(nameof(ShowReassignRowSelection));
        OnPropertyChanged(nameof(StockPagerSelectedCount));
        SetReassignPreviewLive(false);
        _previewRefreshCts?.Cancel();
        ClearPreviewMessaging();
        PreviewRows.Clear();
        OnPropertyChanged(nameof(IsPreviewEmpty));
        RefreshPageCommands();
    }

    protected override Task ReloadCoreAsync(CancellationToken ct)
        => ReloadBodyAsync(ct);

    public override Task OnPageDeactivatedAsync(CancellationToken ct = default)
    {
        CancelSilentReconcile();
        CancelStockEditRemoteReconcile();
        return base.OnPageDeactivatedAsync(ct);
    }

    private void CancelSilentReconcile()
    {
        _silentReconcileCts?.Cancel();
        _silentReconcileCts?.Dispose();
        _silentReconcileCts = null;
    }

    private void CancelStockEditRemoteReconcile()
    {
        _stockEditRemoteCts?.Cancel();
        _stockEditRemoteCts?.Dispose();
        _stockEditRemoteCts = null;
    }

    private void BeginStockReload()
    {
        CancelSilentReconcile();
        CancelStockEditRemoteReconcile();
        Interlocked.Increment(ref _detailStockEpoch);
    }

    protected override void OnReloadFinished()
        => RefreshPageCommands();

    private void NotifySectionPendingChanged()
    {
        OnPropertyChanged(nameof(IsDetailSectionPending));
        OnPropertyChanged(nameof(IsAggSectionPending));
        OnPropertyChanged(nameof(IsLowSectionPending));
        OnPropertyChanged(nameof(IsMissingSectionPending));
    }

    private Task ReloadAsync(bool preserveEdit = false)
    {
        _flushRefreshAfterStockEdit = false;

        if (IsStockEditEnabled && !preserveEdit)
        {
            DiscardStockEdits();
        }

        BeginStockReload();

        var mode = ModeIndex;
        return RunLocalReloadAsync(
            setBusy: v => SetModeBusy(mode, v),
            action: ct => ReloadBodyAsync(ct),
            onFinished: () =>
            {
                SetModeBusy(ModeIndex, false);
                RefreshPageCommands();
            });
    }

    // 静默重载不显示区块加载状态，避免筛选刷新中断批量改派操作
    private Task ReloadQuietAsync(bool preserveEdit = false)
    {
        _flushRefreshAfterStockEdit = false;

        if (IsStockEditEnabled && !preserveEdit)
        {
            DiscardStockEdits();
        }

        BeginStockReload();

        return RunLocalReloadAsync(
            setBusy: _ => { },
            action: ct => ReloadBodyAsync(ct),
            onFinished: RefreshPageCommands);
    }

    private async Task ReloadBodyAsync(CancellationToken ct)
    {
        var kw = NormalizeInput(Keyword);

        switch (ModeIndex)
        {
            case 0:
                await ReloadDetailModeAsync(kw, ct);
                return;
            case 1:
                await ReloadAggModeAsync(kw, ct);
                return;
            case 2:
                await ReloadLowModeAsync(kw, ct);
                return;
            default:
                await ReloadMissingModeAsync(kw, ct);
                return;
        }
    }

    private async Task ReloadDetailModeAsync(string? keyword, CancellationToken ct)
    {
        var page = await _inventory
            .GetStockPageAsync(keyword, PageIndex, PageSize, ct)
            .ConfigureAwait(false);

        var start = ((PageIndex - 1) * PageSize) + 1;
        var items = BuildStockRowItems(page.Rows, start);

        await RunOnUiAsync(() =>
        {
            // 翻页会重置 DataGrid 原生选择，因此按追溯码恢复业务选择状态
            if (IsReassignOpen && IsSingleScope)
            {
                using var _ = BeginReassignContextSync();
                ApplyStockRowsInPlace(items);
                RestoreReassignChecksAfterPageLoad();
            }
            else
            {
                ApplyStockRowsInPlace(items);
            }

            TotalCount = page.TotalCount;
            OnPropertyChanged(nameof(IsStockEmpty));
        });
    }

    private async Task ReloadAggModeAsync(string? keyword, CancellationToken ct)
    {
        var page = await _inventory
            .GetDrugSpecAggPageAsync(keyword, PageIndex, PageSize, ct)
            .ConfigureAwait(false);

        var start = ((PageIndex - 1) * PageSize) + 1;
        var items = BuildDrugSpecAggRowItems(page.Rows, start);

        await RunOnUiAsync(() =>
        {
            DrugSpecRows.ReplaceAll(items);
            TotalCount = page.TotalCount;
            OnPropertyChanged(nameof(IsAggEmpty));
        });
    }

    private async Task ReloadLowModeAsync(
        string? keyword,
        CancellationToken ct)
    {
        var page = await _inventory
            .GetLowStockPageAsync(keyword, PageIndex, PageSize, ct)
            .ConfigureAwait(false);

        var start = ((PageIndex - 1) * PageSize) + 1;
        var items = BuildLowStockRowItems(page.Rows, start);

        await RunOnUiAsync(() =>
        {
            LowStockRows.ReplaceAll(items);
            TotalCount = page.TotalCount;
            OnPropertyChanged(nameof(IsLowEmpty));
        });
    }

    private async Task ReloadMissingModeAsync(
        string? keyword,
        CancellationToken ct)
    {
        var page = await _inventory
            .GetMissingInventoryPageAsync(keyword, PageIndex, PageSize, ct)
            .ConfigureAwait(false);

        var start = ((PageIndex - 1) * PageSize) + 1;
        var items = BuildMissingStockRowItems(page.Rows, start);

        await RunOnUiAsync(() =>
        {
            MissingStockRows.ReplaceAll(items);
            TotalCount = page.TotalCount;
            OnPropertyChanged(nameof(IsMissingEmpty));
        });
    }

    private static List<StockRowItem> BuildStockRowItems(
        IReadOnlyList<TracePoolStockRowDto> rows,
        int startRowNo)
    {
        var items = new List<StockRowItem>(rows.Count);
        var rowNo = startRowNo;
        foreach (var row in rows)
        {
            items.Add(new StockRowItem(
                rowNo: rowNo++,
                drugId: row.DrugId,
                spec: row.Spec,
                traceCode: row.TraceCode,
                qty: row.Qty,
                remain: row.Remain,
                status: row.Status,
                version: row.Version,
                isLow: row.IsLow,
                isDeprecated: row.IsDeprecated));
        }

        return items;
    }

    private static List<DrugSpecAggRowItem> BuildDrugSpecAggRowItems(
        IReadOnlyList<TracePoolDrugSpecAggDto> rows,
        int startRowNo)
    {
        var items = new List<DrugSpecAggRowItem>(rows.Count);
        var rowNo = startRowNo;
        foreach (var row in rows)
        {
            items.Add(new DrugSpecAggRowItem(
                RowNo: rowNo++,
                DrugId: row.DrugId,
                Spec: row.Spec,
                CodeCount: row.CodeCount,
                QtySum: row.QtySum,
                RemainSum: row.RemainSum,
                WeekUsed: row.WeekUsed,
                Threshold: row.Threshold,
                IsLow: row.IsLow,
                IsDeprecated: row.IsDeprecated));
        }

        return items;
    }

    private static List<LowStockRowItem> BuildLowStockRowItems(
        IReadOnlyList<LowStockRowDto> rows,
        int startRowNo)
    {
        var items = new List<LowStockRowItem>(rows.Count);
        var rowNo = startRowNo;
        foreach (var row in rows)
        {
            items.Add(new LowStockRowItem(
                RowNo: rowNo++,
                DrugId: row.DrugId,
                Spec: row.Spec,
                RemainSum: row.RemainSum,
                Threshold: row.Threshold,
                IsLow: row.IsLow));
        }

        return items;
    }

    private static List<MissingStockRowItem> BuildMissingStockRowItems(
        IReadOnlyList<MissingInventoryRowDto> rows,
        int startRowNo)
    {
        var items = new List<MissingStockRowItem>(rows.Count);
        var rowNo = startRowNo;
        foreach (var row in rows)
        {
            items.Add(new MissingStockRowItem(
                RowNo: rowNo++,
                DrugId: row.DrugId,
                Spec: row.Spec,
                Note: row.Note));
        }

        return items;
    }

    private void SetModeBusy(int mode, bool busy)
    {
        IsDetailBusy = mode == 0 && busy;
        IsAggBusy = mode == 1 && busy;
        IsLowBusy = mode == 2 && busy;
        IsMissingBusy = mode == 3 && busy;
    }

    internal void MarkDetailGridMounted() => IsDetailGridMounted = true;

    internal void MarkAggGridMounted() => IsAggGridMounted = true;

    internal void MarkLowGridMounted() => IsLowGridMounted = true;

    internal void MarkMissingGridMounted() => IsMissingGridMounted = true;

    private void OnDbApplied(object? sender, EventArgs e)
    {
        PageIndex = 1;

        PostOnUi(() => ObserveDetached(ReloadAsync(), "reload.detached.fail"), DispatcherPriority.Background);
    }

    private void OnUnlockChanged(string scopeKey)
    {
        if (!string.Equals(scopeKey, OpsScope, StringComparison.Ordinal))
        {
            return;
        }

        PostOnUi(RefreshOpsUnlock, DispatcherPriority.Background);
    }

    private void RefreshPageCommands()
    {
        RefreshCommandsCoalesced("inventory.refresh_commands", () =>
        {
            RefreshCommands(GetNotifiableCommands());
            NotifyEditState();
        });
    }

    private IRelayCommand?[] GetNotifiableCommands()
        => _notifiableCommands ??=
        [
            _localRefreshCommand,
            SearchCommand,
            ClearSearchCommand,
            FirstPageCommand,
            PrevPageCommand,
            NextPageCommand,
            LastPageCommand,
            UnlockCommand,
            LockCommand,
            ToggleStockEditCommand,
            ToggleReassignCommand,
            ApplyDrugFilterCommand,
            ClearDrugSpecFilterCommand,
            PreviewReassignCommand,
            ApplyReassignCommand,
            _importCommand,
            _exportCommand
        ];

    private bool IsReassignContextSyncing => _reassignContextSyncDepth > 0;

    private ReassignContextSyncScope BeginReassignContextSync()
        => new(this);

    private sealed class ReassignContextSyncScope : IDisposable
    {
        private readonly InventoryOverview _vm;
        private bool _disposed;

        public ReassignContextSyncScope(InventoryOverview vm)
        {
            _vm = vm;
            _vm._reassignContextSyncDepth++;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            if (_vm._reassignContextSyncDepth > 0)
            {
                _vm._reassignContextSyncDepth--;
            }

            if (_vm._reassignContextSyncDepth == 0)
            {
                _vm.RefreshPageCommands();
                _vm.QueueReassignPreviewRefresh();
            }
        }
    }

    private void EnsureReassignPreviewLive()
    {
        if (!_reassignPreviewLive)
        {
            SetReassignPreviewLive(true);
            return;
        }

        OnPropertyChanged(nameof(HasPreviewStatsText));
        OnPropertyChanged(nameof(HasPreviewNoticeText));
        NotifyPreviewStateChanged();
    }

    private void SetReassignPreviewLive(bool live)
    {
        if (_reassignPreviewLive == live)
        {
            return;
        }

        _reassignPreviewLive = live;
        OnPropertyChanged(nameof(ShowReassignPreview));
        OnPropertyChanged(nameof(ReassignPreviewToggleText));
        PreviewReassignCommand.NotifyCanExecuteChanged();
    }

    private void ExitReassignPreview()
    {
        _previewRefreshCts?.Cancel();
        _previewRefreshCts?.Dispose();
        _previewRefreshCts = null;
        SetReassignPreviewLive(false);
        ResetPreviewContent();
        NotifyPreviewStateChanged();
        RefreshPageCommands();
    }

    private void ResetPreviewContent()
    {
        ClearPreviewMessaging();
        PreviewRows.Clear();
        _lastValidatedPreviewTargetDrug = null;
        _lastValidatedPreviewTargetSpec = null;
        OnPropertyChanged(nameof(IsPreviewEmpty));
    }

    private void ApplyPreviewRows(IReadOnlyList<StockReassignPreviewRowItem> rows)
    {
        PreviewRows.ReplaceAll(rows);
        OnPropertyChanged(nameof(IsPreviewEmpty));
    }

    private void NotifyPreviewStateChanged()
    {
        OnPropertyChanged(nameof(IsPreviewEmpty));
        OnPropertyChanged(nameof(ShowReassignPreview));
        OnPropertyChanged(nameof(ReassignPreviewToggleText));
        PreviewReassignCommand.NotifyCanExecuteChanged();
    }

    private void ClearPreviewMessaging()
    {
        PreviewStatsText = null;
        PreviewNoticeText = null;
    }

    private void NotifyEditState()
    {
        OnPropertyChanged(nameof(HasPendingChanges));
        OnPropertyChanged(nameof(HasRemoteStockChange));
        OnPropertyChanged(nameof(HasEditAttention));
        OnPropertyChanged(nameof(EditStateText));
        OnPropertyChanged(nameof(ShowEditState));
    }

    private void RefreshPagingState()
    {
        OnPropertyChanged(nameof(IsPagedMode));
        OnPropertyChanged(nameof(PageSize));
        OnPropertyChanged(nameof(TotalPages));
        OnPropertyChanged(nameof(HasPrevPage));
        OnPropertyChanged(nameof(HasNextPage));
        OnPropertyChanged(nameof(IsStockEmpty));
        OnPropertyChanged(nameof(IsAggEmpty));
        OnPropertyChanged(nameof(IsLowEmpty));
        OnPropertyChanged(nameof(IsMissingEmpty));
    }

    public void OpenMode(int mode, bool forceReload = true)
    {
        var next = mode switch
        {
            < 0 => 0,
            > 3 => 3,
            _ => mode
        };

        var modeChanged = ModeIndex != next;
        ModeIndex = next;
        if (forceReload && !modeChanged)
        {
            ObserveDetached(ReloadAsync(), "reload.detached.fail");
        }
    }

    public override void Dispose()
    {
        _dbConfigNotifier.Applied -= OnDbApplied;
        _unlockService.StateChanged -= OnUnlockChanged;
        _traceCodeRule.Changed -= OnTraceCodeRuleChanged;
        StopUnlockTimer();
        _unlockStatusTimer.Tick -= OnUnlockTimerTick;
        _silentReconcileCts?.Cancel();
        _silentReconcileCts?.Dispose();
        _silentReconcileCts = null;
        CancelStockEditRemoteReconcile();
        _keywordSearchDebouncer.Dispose();
        base.Dispose();
    }

    public void ReloadAfterDrugIndexChange()
    {
        PostOnUi(() =>
        {
            ObserveDetached(ReloadAsync(), "reload.detached.fail");
            ObserveDetached(RefreshDrugCatalogAsync(forceRefresh: true), "catalog.refresh.detached.fail");
        }, DispatcherPriority.Background);
    }
}
