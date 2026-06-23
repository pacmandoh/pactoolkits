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
using PacToolkits.Desktop.Avalonia.Services.Infrastructure;

namespace PacToolkits.Desktop.Avalonia.ViewModels.Pages;

public sealed partial class InventoryOverviewViewModel : AppPageBase
{
    private static readonly TimeSpan LookupTimeout = TimeSpan.FromSeconds(8);
    private const string UnlockScopeKey = UnlockScopes.SharedSensitiveOps;
    private static readonly int[] PageSizeOptionValues = [20, 50, 100];

    public override string DisplayName => "追溯码库存";
    public override string Icon => "Package";
    public override int Index => 1;
    public override ICommand RefreshCommand => _localRefreshCommand;
    protected override bool AutoRefreshOnDbDisconnected => true;
    protected override bool AutoRefreshOnDbReconnected => true;

    private readonly IInventoryOverviewService _inventory;
    private readonly ILookupCatalogService _lookup;
    private readonly IDbConfigService _dbConfig;
    private readonly ISensitiveUnlockService _unlockService;
    private readonly IToastService _toast;
    private readonly IDialogService _dialog;
    private readonly PageNavigationService _nav;
    private readonly ScanCodeViewModel _scanCode;
    private readonly AsyncRelayCommand _localRefreshCommand;

    public ObservableCollection<StockRowItem> StockRows { get; } = new();
    public ObservableCollection<DrugSpecAggRowItem> DrugSpecRows { get; } = new();
    public ObservableCollection<LowStockRowItem> LowStockRows { get; } = new();
    public ObservableCollection<MissingStockRowItem> MissingStockRows { get; } = new();
    public ObservableCollection<StockReassignPreviewRowItem> ReassignPreviewRows { get; } = new();
    public ObservableCollection<OptionItem> ReassignDrugOptions { get; } = new();
    public ObservableCollection<OptionItem> ReassignSpecOptions { get; } = new();
    private IReadOnlyList<OptionItem> _reassignDrugCatalog = [];
    public ObservableCollection<int> PageSizeOptions { get; } = new(PageSizeOptionValues);

    [ObservableProperty] private int _modeIndex;
    [ObservableProperty] private string? _keyword;
    [ObservableProperty] private bool _isSearchPanelVisible = false;
    [ObservableProperty] private string? _status;
    [ObservableProperty] private int _pageIndex = 1;
    [ObservableProperty] private int _pageSize = 50;
    [ObservableProperty] private int _totalCount;
    [ObservableProperty] private bool _isDetailBusy;
    [ObservableProperty] private bool _isAggBusy;
    [ObservableProperty] private bool _isLowBusy;
    [ObservableProperty] private bool _isMissingBusy;
    [ObservableProperty] private bool _isStockEditEnabled;
    [ObservableProperty] private bool _isOperationUnlocked;
    [ObservableProperty] private DateTimeOffset _operationUnlockExpiresAtUtc;
    [ObservableProperty] private int _operationUnlockFailedAttempts;
    [ObservableProperty] private DateTimeOffset _operationUnlockCooldownUntilUtc;
    [ObservableProperty] private StockRowItem? _selectedStockRow;
    [ObservableProperty] private int _stockRowsRevision;
    [ObservableProperty] private bool _isReassignPanelVisible;
    [ObservableProperty] private string? _reassignDrugText;
    [ObservableProperty] private OptionItem? _reassignSelectedSpec;
    [ObservableProperty] private bool _isReassignDrugSuggestOpen;
    [ObservableProperty] private bool _isReassignSpecSelected;
    [ObservableProperty] private string? _reassignQtyText;
    [ObservableProperty] private string? _reassignTargetDrugId;
    [ObservableProperty] private string? _reassignTargetSpec;
    [ObservableProperty] private string? _reassignReason;
    [ObservableProperty] private string? _reassignPreviewText;
    [ObservableProperty] private bool _isReassignBusy;
    [ObservableProperty] private int _reassignScopeIndex;
    private readonly Collection<PendingStockEdit> _pendingStockEdits = new();
    private readonly Dictionary<int, StockEditSnapshot> _stockEditSnapshotByRow = new();
    private readonly Collection<StockRowItem> _selectedStockRows = new();
    private IReadOnlyList<StockRowItem> _selectedStockRowsSnapshot = Array.Empty<StockRowItem>();
    private int _lastModeIndex;
    private DateTimeOffset _suppressAutoRefreshUntilUtc = DateTimeOffset.MinValue;
    private CancellationTokenSource? _silentReconcileCts;
    private readonly SearchInputDebouncer _keywordSearchDebouncer = new(450);
    private readonly DispatcherTimer _unlockStatusTimer;
    private IRelayCommand?[]? _notifiableCommands;
    partial void OnIsDetailBusyChanged(bool value)
    {
        OnPropertyChanged(nameof(IsUiBusy));
        NotifyAllCommands();
    }
    partial void OnIsAggBusyChanged(bool value)
    {
        OnPropertyChanged(nameof(IsUiBusy));
        NotifyAllCommands();
    }
    partial void OnIsLowBusyChanged(bool value)
    {
        OnPropertyChanged(nameof(IsUiBusy));
        NotifyAllCommands();
    }
    partial void OnIsMissingBusyChanged(bool value)
    {
        OnPropertyChanged(nameof(IsUiBusy));
        NotifyAllCommands();
    }
    partial void OnIsStockEditEnabledChanged(bool value)
    {
        if (value)
        {
            IsReassignPanelVisible = false;
            ReassignPreviewText = null;
            ReassignPreviewRows.Clear();
            OnPropertyChanged(nameof(IsReassignPreviewEmpty));
        }
        OnPropertyChanged(nameof(CanEnableStockEdit));
        OnPropertyChanged(nameof(CanDisableStockEdit));
        OnPropertyChanged(nameof(CanRequestUnlock));
        OnPropertyChanged(nameof(CanLockOperations));
        OnPropertyChanged(nameof(ShowRequestUnlock));
        OnPropertyChanged(nameof(ShowLockOperations));
        OnPropertyChanged(nameof(UnlockStatusText));
        OnPropertyChanged(nameof(ShowUnlockStatus));
        OnPropertyChanged(nameof(CanToggleReassignPanel));
        NotifyAllCommands();
    }

    partial void OnIsOperationUnlockedChanged(bool value)
    {
        OnPropertyChanged(nameof(UnlockStatusText));
        OnPropertyChanged(nameof(CanRequestUnlock));
        OnPropertyChanged(nameof(CanLockOperations));
        OnPropertyChanged(nameof(ShowRequestUnlock));
        OnPropertyChanged(nameof(ShowLockOperations));
        NotifyAllCommands();
    }

    partial void OnOperationUnlockFailedAttemptsChanged(int value)
        => OnPropertyChanged(nameof(UnlockStatusText));

    partial void OnOperationUnlockCooldownUntilUtcChanged(DateTimeOffset value)
    {
        OnPropertyChanged(nameof(UnlockStatusText));
        OnPropertyChanged(nameof(CanRequestUnlock));
        OnPropertyChanged(nameof(ShowRequestUnlock));
    }

    public bool IsDetailMode => ModeIndex == 0;
    public bool IsAggMode => ModeIndex == 1;
    public bool IsLowMode => ModeIndex == 2;
    public bool IsMissingMode => ModeIndex == 3;
    public bool IsSingleReassignScope => ReassignScopeIndex == 0;
    public bool IsFilterReassignScope => ReassignScopeIndex == 1;
    public bool SuppressGridClearInReassignDialog => IsReassignPanelVisible && IsSingleReassignScope;
    public bool IsReassignPreviewEmpty => ReassignPreviewRows.Count == 0;
    public bool HasReassignPreviewText => !string.IsNullOrWhiteSpace(ReassignPreviewText);
    public bool CanEnableStockEdit => IsDetailMode && !IsStockEditEnabled;
    public bool CanDisableStockEdit => IsDetailMode && IsStockEditEnabled;
    public bool CanRequestUnlock => IsDetailMode && !IsOperationUnlocked;
    public bool CanLockOperations => IsDetailMode && IsOperationUnlocked;
    public bool ShowRequestUnlock => IsDetailMode && !IsOperationUnlocked;
    public bool ShowLockOperations => IsDetailMode && IsOperationUnlocked;
    public bool CanToggleReassignPanel
        => IsDetailMode
           && !IsStockEditEnabled
           && CanOperateUi();
    public bool CanPreviewReassign
        => IsReassignPanelVisible
           && (IsSingleReassignScope
               ? (_selectedStockRows.Count > 0 || SelectedStockRow is not null)
               : !string.IsNullOrWhiteSpace(NormalizeInput(Keyword)))
           && !string.IsNullOrWhiteSpace(NormalizeInput(ReassignTargetDrugId))
           && !string.IsNullOrWhiteSpace(NormalizeInput(ReassignTargetSpec))
           && int.TryParse(NormalizeInput(ReassignQtyText), out var previewQty)
           && previewQty > 0
           && CanOperateUi();
    public bool CanApplyReassign
        => CanPreviewReassign
           && !string.IsNullOrWhiteSpace(NormalizeInput(ReassignReason))
           && int.TryParse(NormalizeInput(ReassignQtyText), out var qty)
           && qty > 0;
    public string EditSessionStateText
        => !IsDetailMode
            ? string.Empty
            : IsStockEditEnabled
                ? (HasPendingChanges ? "有未提交变更" : "编辑中")
                : string.Empty;
    public string UnlockStatusText
    {
        get
        {
            if (!IsDetailMode)
            {
                return string.Empty;
            }

            return IsOperationUnlocked ? "已解锁" : "未解锁";
        }
    }
    public bool ShowUnlockStatus => IsDetailMode;

    public bool ShowInventoryStatus => !string.IsNullOrWhiteSpace(Status);
    public bool ShowEditSessionState => IsDetailMode && IsStockEditEnabled;
    protected override void OnLookupCatalogSuspended()
    {
        ReassignDrugOptions.Clear();
        _reassignDrugCatalog = [];
        ReassignSpecOptions.Clear();
        IsReassignDrugSuggestOpen = false;
        ReassignDrugText = null;
        ReassignTargetDrugId = null;
        ReassignSelectedSpec = null;
        ReassignTargetSpec = null;
        ReassignQtyText = null;
        IsReassignSpecSelected = false;
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
    public bool IsUiBusy => IsBusy || IsDetailBusy || IsAggBusy || IsLowBusy || IsMissingBusy || IsReassignBusy;
    public bool IsPagedMode => ModeIndex is 0 or 1 or 2 or 3;
    public int TotalPages => Math.Max(1, (int)Math.Ceiling(TotalCount / (double)PageSize));
    public bool HasPrevPage => IsPagedMode && PageIndex > 1;
    public bool HasNextPage => IsPagedMode && PageIndex < TotalPages;
    public bool HasPendingChanges => IsStockEditEnabled && _pendingStockEdits.Count > 0;
    public IReadOnlyList<StockRowItem> SelectedStockRowsSnapshot => _selectedStockRowsSnapshot;

    public InventoryOverviewViewModel(
        IInventoryOverviewService inventory,
        ILookupCatalogService lookup,
        IDbConfigService dbConfig,
        ISensitiveUnlockService unlockService,
        IToastService toast,
        IDialogService dialog,
        PageNavigationService nav,
        ScanCodeViewModel scanCode)
    {
        _inventory = inventory;
        _lookup = lookup;
        _dbConfig = dbConfig;
        _unlockService = unlockService;
        _toast = toast;
        _dialog = dialog;
        _nav = nav;
        _scanCode = scanCode;
        _localRefreshCommand = new AsyncRelayCommand(() => ReloadAsync(), CanLocalRefresh);
        _unlockStatusTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _unlockStatusTimer.Tick += OnUnlockTimerTick;
        _unlockService.StateChanged += OnUnlockScopeChanged;
        RefreshUnlockState();

        _dbConfig.Applied += OnDbApplied;
        _lastModeIndex = ModeIndex;

        PostOnUi(() => _ = ReloadAsync(), DispatcherPriority.Background);
    }

    [RelayCommand]
    private void ToggleSearchPanel()
    {
        IsSearchPanelVisible = !IsSearchPanelVisible;
    }

    partial void OnSelectedStockRowChanged(StockRowItem? value)
    {
        if (IsSingleReassignScope)
        {
            ReassignPreviewText = null;
            ReassignPreviewRows.Clear();
            OnPropertyChanged(nameof(IsReassignPreviewEmpty));
        }

        NotifyAllCommands();
    }

    public void SetSelectedStockRows(IReadOnlyList<StockRowItem> rows)
    {
        _selectedStockRows.Clear();
        foreach (var row in rows)
        {
            _selectedStockRows.Add(row);
        }

        _selectedStockRowsSnapshot = _selectedStockRows.ToArray();
        OnPropertyChanged(nameof(SelectedStockRowsSnapshot));

        if (IsSingleReassignScope)
        {
            ReassignPreviewText = null;
            ReassignPreviewRows.Clear();
            OnPropertyChanged(nameof(IsReassignPreviewEmpty));
        }

        NotifyAllCommands();
    }

    private void ClearStockSelection()
    {
        SelectedStockRow = null;
        _selectedStockRows.Clear();
        _selectedStockRowsSnapshot = Array.Empty<StockRowItem>();
        OnPropertyChanged(nameof(SelectedStockRowsSnapshot));
    }

    private void ApplyStockRowsInPlace(IReadOnlyList<StockRowItem> items)
    {
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

        StockRowsRevision++;
    }

    partial void OnIsReassignPanelVisibleChanged(bool value)
    {
        if (!value)
        {
            ReassignPreviewRows.Clear();
            OnPropertyChanged(nameof(IsReassignPreviewEmpty));
        }
        else
        {
            _ = EnsureReassignDrugOptionsAsync();
        }

        OnPropertyChanged(nameof(SuppressGridClearInReassignDialog));
        NotifyAllCommands();
    }

    partial void OnReassignDrugTextChanged(string? value)
    {
        RefreshReassignDrugOptionsOrder(value);

        var drug = NormalizeInput(value);
        IsReassignDrugSuggestOpen = !string.IsNullOrWhiteSpace(drug);
        if (string.IsNullOrWhiteSpace(drug))
        {
            ReassignTargetDrugId = null;
            ReassignSelectedSpec = null;
            ReassignTargetSpec = null;
            ReassignQtyText = null;
            IsReassignSpecSelected = false;
        }
        NotifyAllCommands();
    }

    private void RefreshReassignDrugOptionsOrder(string? searchText)
    {
        if (_reassignDrugCatalog.Count == 0)
        {
            return;
        }

        AutoCompleteFilter.RefreshVisibleOptions(
            ReassignDrugOptions,
            _reassignDrugCatalog,
            searchText);
    }

    partial void OnReassignSelectedSpecChanged(OptionItem? value)
    {
        IsReassignSpecSelected = value is not null;
        ReassignTargetSpec = NormalizeInput(value?.Raw);
        _ = RefreshReassignQtyAsync();
        NotifyAllCommands();
    }

    partial void OnReassignTargetDrugIdChanged(string? value)
    {
        ReassignPreviewText = null;
        ReassignPreviewRows.Clear();
        OnPropertyChanged(nameof(IsReassignPreviewEmpty));
        NotifyAllCommands();
    }

    partial void OnReassignTargetSpecChanged(string? value)
    {
        ReassignPreviewText = null;
        ReassignPreviewRows.Clear();
        OnPropertyChanged(nameof(IsReassignPreviewEmpty));
        NotifyAllCommands();
    }

    partial void OnReassignReasonChanged(string? value)
        => NotifyAllCommands();

    partial void OnReassignQtyTextChanged(string? value)
        => NotifyAllCommands();

    partial void OnReassignPreviewTextChanged(string? value)
    {
        OnPropertyChanged(nameof(HasReassignPreviewText));
        NotifyAllCommands();
    }

    partial void OnIsReassignBusyChanged(bool value)
    {
        OnPropertyChanged(nameof(IsUiBusy));
        NotifyAllCommands();
    }

    protected override void OnBusyChanged(bool isBusy)
        => OnPropertyChanged(nameof(IsUiBusy));

    partial void OnReassignScopeIndexChanged(int value)
    {
        OnPropertyChanged(nameof(IsSingleReassignScope));
        OnPropertyChanged(nameof(IsFilterReassignScope));
        OnPropertyChanged(nameof(SuppressGridClearInReassignDialog));
        ReassignPreviewText = null;
        ReassignPreviewRows.Clear();
        OnPropertyChanged(nameof(IsReassignPreviewEmpty));
        NotifyAllCommands();
    }

    protected override Task ReloadCoreAsync(CancellationToken ct)
        => ReloadBodyAsync(ct);

    public override Task OnPageDeactivatedAsync(CancellationToken ct = default)
    {
        CancelSilentReconcile();
        return base.OnPageDeactivatedAsync(ct);
    }

    private void CancelSilentReconcile()
    {
        _silentReconcileCts?.Cancel();
        _silentReconcileCts?.Dispose();
        _silentReconcileCts = null;
    }

    protected override void OnReloadFinished()
        => NotifyAllCommands();

    private Task ReloadAsync(bool preserveEditSession = false)
    {
        if (IsStockEditEnabled && !preserveEditSession)
        {
            DiscardStockEdits();
        }

        var mode = ModeIndex;
        return RunLocalReloadAsync(
            setBusy: v => SetModeBusy(mode, v),
            action: ct => ReloadBodyAsync(ct),
            onFinished: NotifyAllCommands);
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
            // DataGrid retains its selected index while ReplaceAll swaps every row instance.
            // Clearing both selection channels prevents that stale index from selecting an
            // unrelated row (commonly the final row on a 50-row page).
            ClearStockSelection();
            ApplyStockRowsInPlace(items);
            TotalCount = page.TotalCount;
            Status = $"库存明细：{TotalCount} 行（第 {PageIndex}/{TotalPages} 页）";
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
            Status = $"按药品+规格汇总：{TotalCount} 行（第 {PageIndex}/{TotalPages} 页）";
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
            Status = $"低库存：{TotalCount} 项（第 {PageIndex}/{TotalPages} 页）";
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
            Status = $"缺失：{TotalCount} 项（第 {PageIndex}/{TotalPages} 页）";
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

    private void OnDbApplied(object? sender, EventArgs e)
    {
        PageIndex = 1;

        PostOnUi(() => _ = ReloadAsync(), DispatcherPriority.Background);
    }

    private void OnUnlockScopeChanged(string scopeKey)
    {
        if (!string.Equals(scopeKey, UnlockScopeKey, StringComparison.Ordinal))
        {
            return;
        }

        PostOnUi(RefreshUnlockState, DispatcherPriority.Background);
    }

    private void NotifyAllCommands()
    {
        NotifyCommandsCoalesced("inventory.notify_commands", () =>
        {
            NotifyCommands(GetNotifiableCommands());
            RefreshPendingChanges();
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
            RequestUnlockCommand,
            LockOperationsCommand,
            ToggleStockEditModeCommand,
            ToggleReassignPanelCommand,
            ApplyReassignDrugFilterCommand,
            PreviewReassignCommand,
            ApplyReassignCommand
        ];

    private void RefreshPendingChanges()
    {
        OnPropertyChanged(nameof(HasPendingChanges));
        OnPropertyChanged(nameof(EditSessionStateText));
        OnPropertyChanged(nameof(ShowEditSessionState));
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
            _ = ReloadAsync();
        }
    }

    public override void Dispose()
    {
        _dbConfig.Applied -= OnDbApplied;
        _unlockService.StateChanged -= OnUnlockScopeChanged;
        StopUnlockTimer();
        _unlockStatusTimer.Tick -= OnUnlockTimerTick;
        _silentReconcileCts?.Cancel();
        _silentReconcileCts?.Dispose();
        _silentReconcileCts = null;
        _keywordSearchDebouncer.Dispose();
        base.Dispose();
    }

    public void ReloadAfterDrugIndexChange()
    {
        PostOnUi(() => _ = ReloadAsync(), DispatcherPriority.Background);
    }
}
