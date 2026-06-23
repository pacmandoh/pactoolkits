using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using global::Avalonia.Threading;
using PacToolkits.Application.Abstractions;
using PacToolkits.Application.DTOs;
using PacToolkits.Application.TextSearch;
using PacToolkits.Desktop.Avalonia.Common;
using PacToolkits.Desktop.Avalonia.Services.Infrastructure;

namespace PacToolkits.Desktop.Avalonia.ViewModels.Pages;

public sealed partial class StockRowItem : ObservableObject
{
    public StockRowItem(
        int rowNo,
        string drugId,
        string spec,
        string traceCode,
        int qty,
        int remain,
        int status,
        bool isLow,
        bool isDeprecated)
    {
        RowNo = rowNo;
        DrugId = drugId;
        Spec = spec;
        TraceCode = traceCode;
        Qty = qty;
        Remain = remain;
        Status = status;
        IsLow = isLow;
        IsDeprecated = isDeprecated;
    }

    [ObservableProperty] private int _rowNo;

    [ObservableProperty] private string _drugId;
    [ObservableProperty] private string _spec;
    [ObservableProperty] private string _traceCode;
    [ObservableProperty] private int _qty;
    [ObservableProperty] private int _remain;
    [ObservableProperty] private int _status;
    [ObservableProperty] private bool _isLow;
    [ObservableProperty] private bool _isDeprecated;
}

public sealed record DrugSpecAggRowItem(
    int RowNo,
    string DrugId,
    string Spec,
    long CodeCount,
    long QtySum,
    long RemainSum,
    long WeekUsed,
    decimal Threshold,
    bool IsLow,
    bool IsDeprecated
);

public sealed record LowStockRowItem(
    int RowNo,
    string DrugId,
    string Spec,
    long RemainSum,
    decimal Threshold,
    bool IsLow
);

public sealed record MissingStockRowItem(
    int RowNo,
    string DrugId,
    string Spec,
    string? Note
);

public sealed record StockReassignPreviewRowItem(
    string CurrentDrugId,
    string CurrentSpec,
    int CurrentQty,
    int CurrentRemain,
    string TargetDrugId,
    string TargetSpec,
    int TargetQty,
    string TraceCode
);

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
    private readonly ISensitiveOperationUnlockService _unlockService;
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

    public bool IsStockEmpty => ShouldShowSectionEmpty(StockRows.Count == 0);
    public bool IsAggEmpty => ShouldShowSectionEmpty(DrugSpecRows.Count == 0);
    public bool IsLowEmpty => ShouldShowSectionEmpty(LowStockRows.Count == 0);
    public bool IsMissingEmpty => ShouldShowSectionEmpty(MissingStockRows.Count == 0);
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
        ISensitiveOperationUnlockService unlockService,
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
        _unlockStatusTimer.Tick += OnUnlockStatusTimerTick;
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

        DrugAutoCompleteCatalogHelper.RefreshVisibleOptions(
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

    private bool CanApplyReassignDrugFilter()
        => IsReassignPanelVisible && CanOperateUi();

    [RelayCommand(CanExecute = nameof(CanApplyReassignDrugFilter))]
    private async Task ApplyReassignDrugFilterAsync()
    {
        var drug = NormalizeInput(ReassignDrugText);
        if (string.IsNullOrWhiteSpace(drug))
        {
            ReassignSpecOptions.Clear();
            ReassignSelectedSpec = null;
            ReassignTargetDrugId = null;
            ReassignTargetSpec = null;
            ReassignQtyText = null;
            IsReassignSpecSelected = false;
            return;
        }

        // Same context repeated by Enter should not rebuild options and trigger qty flicker.
        if (string.Equals(drug, NormalizeInput(ReassignTargetDrugId), StringComparison.OrdinalIgnoreCase) &&
            ReassignSpecOptions.Count > 0 &&
            ReassignSelectedSpec is not null)
        {
            IsReassignDrugSuggestOpen = false;
            return;
        }

        try
        {
            using var cts = new CancellationTokenSource(LookupTimeout);
            var canonical = await _lookup.ResolveCanonicalDrugIdAsync(drug, cts.Token).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(canonical))
            {
                var isDeprecated = await _lookup.IsDeprecatedDrugIdAsync(drug, cts.Token).ConfigureAwait(false);
                await RunOnUiAsync(() =>
                {
                    ReassignSpecOptions.Clear();
                    ReassignSelectedSpec = null;
                    ReassignQtyText = null;
                    IsReassignSpecSelected = false;
                    ReassignTargetDrugId = null;
                    ReassignTargetSpec = null;
                    ReassignPreviewText = isDeprecated
                        ? "纠错上下文：药品已被弃用"
                        : "纠错上下文：药品不存在，请重新输入";
                });
                return;
            }

            await RunOnUiAsync(() =>
            {
                ReassignDrugText = canonical;
                IsReassignDrugSuggestOpen = false;
                ReassignTargetDrugId = canonical;
            });
            await LoadReassignSpecsByDrugAsync(canonical);
            if (ReassignSpecOptions.Count == 0)
            {
                await RunOnUiAsync(
                    () => ReassignPreviewText = "纠错上下文：该药品暂无规格");
            }
        }
        catch (Exception ex)
        {
            LogError("inventory.reassign_context.load_fail", "Failed to load reassign context", ex);
            await RunOnUiAsync(
                () => ReassignPreviewText = $"纠错上下文加载失败：{ex.Message}");
        }
    }

    private async Task EnsureReassignDrugOptionsAsync()
    {
        if (IsLookupCatalogSuspended())
        {
            await RunOnUiAsync(OnLookupCatalogSuspended);
            return;
        }

        if (_reassignDrugCatalog.Count > 0)
        {
            return;
        }

        try
        {
            using var cts = new CancellationTokenSource(LookupTimeout);
            var drugs = await LookupOptionLoader.LoadDrugOptionsAsync(_lookup, cts.Token).ConfigureAwait(false);
            await RunOnUiAsync(() =>
            {
                _reassignDrugCatalog = drugs;
                DrugAutoCompleteCatalogHelper.RefreshVisibleOptions(
                    ReassignDrugOptions,
                    _reassignDrugCatalog,
                    ReassignDrugText);
            });
        }
        catch (System.Exception ex)
        {
            LogWarn("inventory.reassign_drug_options.load_fail", "Failed to load reassign drug options", ex);
        }
    }

    private async Task RefreshReassignQtyAsync()
    {
        var drug = NormalizeInput(ReassignTargetDrugId);
        var spec = NormalizeInput(ReassignTargetSpec);
        if (string.IsNullOrWhiteSpace(drug) || string.IsNullOrWhiteSpace(spec))
        {
            ReassignQtyText = null;
            return;
        }

        try
        {
            using var cts = new CancellationTokenSource(LookupTimeout);
            var canonicalDrug = await _lookup.ResolveCanonicalDrugIdAsync(drug, cts.Token).ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(canonicalDrug))
            {
                drug = canonicalDrug;
            }

            var qty = await _lookup.GetQtyAsync(drug, spec, cts.Token).ConfigureAwait(false);
            await RunOnUiAsync(() =>
            {
                ReassignTargetDrugId = drug;
                ReassignQtyText = qty?.ToString(CultureInfo.InvariantCulture);
            });
        }
        catch
        {
            await RunOnUiAsync(() =>
            {
                ReassignQtyText = null;
            });
        }
    }

    private async Task TryAutoResolveReassignContextAsync(string? drugText)
    {
        var drug = NormalizeInput(drugText);
        if (string.IsNullOrWhiteSpace(drug))
        {
            return;
        }

        await EnsureReassignDrugOptionsAsync();

        string? canonical = null;
        foreach (var opt in ReassignDrugOptions)
        {
            if (string.Equals(opt.Raw, drug, StringComparison.OrdinalIgnoreCase))
            {
                canonical = opt.Raw;
                break;
            }
        }

        if (string.IsNullOrWhiteSpace(canonical))
        {
            try
            {
                using var cts = new CancellationTokenSource(LookupTimeout);
                canonical = await _lookup.ResolveCanonicalDrugIdAsync(drug, cts.Token).ConfigureAwait(false);
            }
            catch
            {
                canonical = null;
            }
        }

        if (string.IsNullOrWhiteSpace(canonical))
        {
            return;
        }

        await LoadReassignSpecsByDrugAsync(canonical);
    }

    private async Task LoadReassignSpecsByDrugAsync(string canonicalDrug)
    {
        try
        {
            using var cts = new CancellationTokenSource(LookupTimeout);
            var specs = await LookupOptionLoader.LoadSpecsAsync(_lookup, canonicalDrug, cts.Token).ConfigureAwait(false);
            await RunOnUiAsync(() =>
            {
                ReassignDrugText = canonicalDrug;
                IsReassignDrugSuggestOpen = false;
                ReassignTargetDrugId = canonicalDrug;
                OptionCollectionHelper.ReplaceRaw(ReassignSpecOptions, specs, StringComparison.Ordinal);
                if (ReassignSpecOptions.Count == 0)
                {
                    ReassignSelectedSpec = null;
                    ReassignTargetSpec = null;
                    ReassignQtyText = null;
                    IsReassignSpecSelected = false;
                    return;
                }

                ReassignSelectedSpec = ReassignSpecOptions[0];
            });
        }
        catch (System.Exception ex)
        {
            LogWarn("inventory.reassign_specs.refresh_fail", "Failed to refresh reassign specs", ex);
        }
    }

    private bool CanOperateUi() => !IsUiBusy;

    private bool CanLocalRefresh()
        => CanOperateUi()
           && DateTimeOffset.UtcNow >= _suppressAutoRefreshUntilUtc;

    private bool CanRequestUnlockCore()
        => CanOperateUi()
           && IsDetailMode;

    [RelayCommand(CanExecute = nameof(CanRequestUnlockCore))]
    private async Task RequestUnlockAsync()
    {
        await EnsureUnlockedAsync("库存编辑与药品纠错");
    }

    private bool CanLockOperationsCore()
        => CanOperateUi()
           && IsDetailMode
           && IsOperationUnlocked;

    [RelayCommand(CanExecute = nameof(CanLockOperationsCore))]
    private async Task LockOperationsAsync()
    {
        if (IsStockEditEnabled)
        {
            await ToggleStockEditMode();
        }

        IsReassignPanelVisible = false;
        ReassignPreviewText = null;
        ReassignPreviewRows.Clear();
        OnPropertyChanged(nameof(IsReassignPreviewEmpty));
        _unlockService.Lock(UnlockScopeKey);
        RefreshUnlockState();
        StopUnlockStatusTimerIfNeeded();
        Status = "库存安全会话：已手动锁定";
    }

    private bool CanToggleStockEditMode() => CanOperateUi() && IsDetailMode;

    [RelayCommand(CanExecute = nameof(CanToggleStockEditMode))]
    private async Task ToggleStockEditMode()
    {
        if (ShouldSkipTrigger("inventory.stock.edit", 250))
        {
            return;
        }

        if (IsStockEditEnabled)
        {
            BuildPendingEditsFromSnapshot();
            var (savedCount, failedCount, lastError) = (0, 0, (string?)null);
            if (_pendingStockEdits.Count > 0)
            {
                (savedCount, failedCount, lastError) = await ApplyPendingStockEditsAsync();
            }

            if (savedCount > 0)
            {
                // Keep current viewport/scroll stable after row-level edits:
                // defer watermark-driven full reload, then reconcile silently.
                SuppressExternalAutoRefresh(TimeSpan.FromSeconds(7));
                ScheduleSilentCurrentPageReconcile(TimeSpan.FromSeconds(5));
            }

            IsStockEditEnabled = false;
            Status = "库存明细：已退出编辑模式";
            if (failedCount > 0)
            {
                var reason = string.IsNullOrWhiteSpace(lastError) ? "请检查输入值与唯一性约束" : lastError!;
                _toast.Error("库存明细编辑", $"保存失败 {failedCount} 项，成功 {savedCount} 项：{reason}");
            }
            else if (savedCount > 0)
            {
                _toast.Success("库存明细编辑", $"保存成功 {savedCount} 项");
            }

            return;
        }

        if (!await EnsureUnlockedAsync("库存明细编辑"))
        {
            return;
        }

        _pendingStockEdits.Clear();
        CaptureStockEditSnapshotFromCurrentRows();
        IsStockEditEnabled = true;
        Status = "库存明细：已进入编辑模式";
        _lastModeIndex = ModeIndex;
    }

    public async Task OpenScanCodeByRowAsync(string? drugId, string? spec)
    {
        if (!IsLowMode && !IsMissingMode)
        {
            return;
        }

        var d = NormalizeInput(drugId);
        var s = NormalizeInput(spec);
        if (string.IsNullOrWhiteSpace(d) || string.IsNullOrWhiteSpace(s))
        {
            return;
        }

        _nav.Navigate<ScanCodeViewModel>();
        await _scanCode.PrefillFromInventoryAsync(d, s);
    }

    [RelayCommand(CanExecute = nameof(CanToggleReassignPanel))]
    private Task ToggleReassignPanelAsync()
    {
        if (ShouldSkipTrigger("inventory.reassign.panel", 250))
        {
            return Task.CompletedTask;
        }

        if (!CanToggleReassignPanel)
        {
            return Task.CompletedTask;
        }

        return ToggleReassignPanelInnerAsync();
    }

    private async Task ToggleReassignPanelInnerAsync()
    {
        if (!IsReassignPanelVisible && !await EnsureUnlockedAsync("药品纠错"))
        {
            return;
        }

        IsReassignPanelVisible = !IsReassignPanelVisible;
        if (IsReassignPanelVisible)
        {
            ReassignReason = null;
            ReassignDrugText = null;
            ReassignSelectedSpec = null;
            ReassignTargetDrugId = null;
            ReassignTargetSpec = null;
            ReassignQtyText = null;
            IsReassignDrugSuggestOpen = false;
            IsReassignSpecSelected = false;
            ReassignPreviewText = null;
            ReassignScopeIndex = 0;
            ReassignPreviewRows.Clear();
            OnPropertyChanged(nameof(IsReassignPreviewEmpty));
        }
    }

    [RelayCommand(CanExecute = nameof(CanPreviewReassign))]
    private async Task PreviewReassignAsync()
    {
        if (!CanPreviewReassign)
        {
            return;
        }

        var targetDrug = NormalizeInput(ReassignTargetDrugId);
        var targetSpec = NormalizeInput(ReassignTargetSpec);
        if (string.IsNullOrWhiteSpace(targetDrug) || string.IsNullOrWhiteSpace(targetSpec))
        {
            return;
        }

        await SetReassignBusyAsync(true);
        try
        {
            if (IsSingleReassignScope)
            {
                var selectedRows = GetEffectiveSelectedRows();
                if (selectedRows.Count == 0)
                {
                    return;
                }

                using var existsCts = new CancellationTokenSource(LookupTimeout);
                var targetExists = await _inventory.TargetDrugSpecExistsAsync(targetDrug, targetSpec, existsCts.Token);
                if (!targetExists)
                {
                    ReassignPreviewText = "预览结果：目标药品/规格不存在，无法纠错";
                    return;
                }

                var targetQtyResolved = int.TryParse(NormalizeInput(ReassignQtyText), out var parsedQty)
                    ? parsedQty
                    : 0;

                var singleScopePreviewRows = new List<StockReassignPreviewRowItem>(selectedRows.Count);
                var willChangeCount = 0;
                foreach (var row in selectedRows)
                {
                    var willChange = !string.Equals(row.DrugId, targetDrug, StringComparison.Ordinal)
                                     || !string.Equals(row.Spec, targetSpec, StringComparison.Ordinal)
                                     || row.Qty != targetQtyResolved;
                    if (willChange)
                    {
                        willChangeCount++;
                    }

                    singleScopePreviewRows.Add(new StockReassignPreviewRowItem(
                        CurrentDrugId: row.DrugId,
                        CurrentSpec: row.Spec,
                        CurrentQty: row.Qty,
                        CurrentRemain: row.Remain,
                        TargetDrugId: targetDrug,
                        TargetSpec: targetSpec,
                        TargetQty: targetQtyResolved,
                        TraceCode: row.TraceCode));
                }

                ReassignPreviewRows.ReplaceAll(singleScopePreviewRows);
                OnPropertyChanged(nameof(IsReassignPreviewEmpty));

                if (willChangeCount <= 0)
                {
                    ReassignPreviewText = "预览结果：目标与当前一致，无需纠错";
                    return;
                }

                ReassignPreviewText =
                    $"预览结果：选中行命中 {selectedRows.Count.ToString(CultureInfo.InvariantCulture)} 条，可变更 {willChangeCount.ToString(CultureInfo.InvariantCulture)} 条；目标 {targetDrug}/{targetSpec}，目标数量 {targetQtyResolved.ToString(CultureInfo.InvariantCulture)}";
                return;
            }

            var kw = NormalizeInput(Keyword);
            if (string.IsNullOrWhiteSpace(kw))
            {
                ReassignPreviewText = "预览结果：批量纠错需要先输入筛选关键字";
                return;
            }

            var targetQtyResolvedForFilter = int.TryParse(NormalizeInput(ReassignQtyText), out var parsedQtyForFilter)
                ? parsedQtyForFilter
                : 0;
            var preview = await _inventory.PreviewReassignByKeywordAsync(
                kw,
                targetDrug,
                targetSpec,
                targetQtyResolvedForFilter,
                30,
                default);
            var scopeText = $"筛选批量（关键字：{kw}）";

            var previewRows = new List<StockReassignPreviewRowItem>(preview.Samples.Count);
            foreach (var row in preview.Samples)
            {
                previewRows.Add(new StockReassignPreviewRowItem(
                    CurrentDrugId: row.DrugId,
                    CurrentSpec: row.Spec,
                    CurrentQty: row.Qty,
                    CurrentRemain: row.Remain,
                    TargetDrugId: targetDrug,
                    TargetSpec: targetSpec,
                    TargetQty: targetQtyResolvedForFilter,
                    TraceCode: row.TraceCode));
            }

            ReassignPreviewRows.ReplaceAll(previewRows);
            OnPropertyChanged(nameof(IsReassignPreviewEmpty));

            if (preview.MatchCount <= 0)
            {
                ReassignPreviewText = $"预览结果：{scopeText} 未命中任何记录";
                return;
            }

            if (!preview.TargetExists)
            {
                ReassignPreviewText = "预览结果：目标药品/规格不存在，无法纠错";
                return;
            }

            if (preview.IsNoopTarget)
            {
                ReassignPreviewText = "预览结果：目标与当前一致，无需纠错";
                return;
            }

            ReassignPreviewText = IsSingleReassignScope
                ? $"预览结果：{scopeText} 命中 {preview.MatchCount.ToString(CultureInfo.InvariantCulture)} 条，可变更 {preview.WillChangeCount.ToString(CultureInfo.InvariantCulture)} 条；当前 {preview.CurrentDrugId}/{preview.CurrentSpec} -> 目标 {targetDrug}/{targetSpec}"
                : $"预览结果：{scopeText} 命中 {preview.MatchCount.ToString(CultureInfo.InvariantCulture)} 条，可变更 {preview.WillChangeCount.ToString(CultureInfo.InvariantCulture)} 条；目标 {targetDrug}/{targetSpec}（下方显示前 {ReassignPreviewRows.Count.ToString(CultureInfo.InvariantCulture)} 条）";

            if (IsFilterReassignScope && preview.WillChangeCount > _inventory.LargeBatchReassignConfirmThreshold)
            {
                ReassignPreviewText +=
                    $"注意：可变更数量超过 {_inventory.LargeBatchReassignConfirmThreshold.ToString(CultureInfo.InvariantCulture)} 条，提交时会触发二次确认";
            }
        }
        catch (Exception ex)
        {
            LogError("inventory.reassign.preview_fail", "Failed to preview reassign operation", ex);
            ReassignPreviewText = $"预览失败：{ex.Message}";
            _toast.Error("药品纠错预览", ex.Message);
        }
        finally
        {
            await SetReassignBusyAsync(false);
        }
    }

    [RelayCommand(CanExecute = nameof(CanApplyReassign))]
    private async Task ApplyReassignAsync()
    {
        if (!CanApplyReassign)
        {
            return;
        }

        var targetDrug = NormalizeInput(ReassignTargetDrugId);
        var targetSpec = NormalizeInput(ReassignTargetSpec);
        var qtyText = NormalizeInput(ReassignQtyText);
        var reason = NormalizeInput(ReassignReason);
        if (string.IsNullOrWhiteSpace(targetDrug)
            || string.IsNullOrWhiteSpace(targetSpec)
            || !int.TryParse(qtyText, out var targetQty)
            || targetQty <= 0
            || string.IsNullOrWhiteSpace(reason))
        {
            return;
        }

        if (!await EnsureUnlockedAsync("药品纠错提交"))
        {
            return;
        }

        var confirmMessage = IsSingleReassignScope
            ? $"将选中追溯码纠错到 {targetDrug}/{targetSpec}，是否继续？"
            : $"将“当前筛选关键字”命中的库存批量纠错到 {targetDrug}/{targetSpec}，是否继续？";
        var ok = await _dialog.Confirm("确认纠错", confirmMessage);
        if (!ok)
        {
            return;
        }

        var isSingleScope = IsSingleReassignScope;
        var selectedTraceCodes = Array.Empty<string>();
        var batchKeyword = !isSingleScope ? NormalizeInput(Keyword) : null;

        if (isSingleScope)
        {
            selectedTraceCodes = GetEffectiveSelectedRows()
                .Select(x => NormalizeInput(x.TraceCode))
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.Ordinal)
                .Cast<string>()
                .ToArray();
        }

        await SetReassignBusyAsync(true);
        try
        {
            var operatorName = $"{Environment.UserName}@{Environment.MachineName}";
            StockReassignApplyResultDto result;
            if (isSingleScope)
            {
                var selectedRows = GetEffectiveSelectedRows();
                if (selectedRows.Count == 0)
                {
                    return;
                }

                var traceCodes = selectedTraceCodes;

                if (traceCodes.Length == 0)
                {
                    return;
                }

                var context = new StockReassignContext(
                    targetDrug,
                    targetSpec,
                    targetQty,
                    reason,
                    operatorName,
                    "inventory_ui");

                result = await _inventory.ReassignByTraceCodesAsync(traceCodes, context, default);
            }
            else
            {
                var kw = NormalizeInput(Keyword);
                if (string.IsNullOrWhiteSpace(kw))
                {
                    _toast.Warn("药品纠错", "批量纠错需要先输入筛选关键字");
                    return;
                }

                var guardPreview = await _inventory.PreviewReassignByKeywordAsync(
                    kw,
                    targetDrug,
                    targetSpec,
                    targetQty,
                    1,
                    default);
                if (guardPreview.WillChangeCount > _inventory.LargeBatchReassignConfirmThreshold)
                {
                    var secondOk = await _dialog.Confirm(
                        "批量纠错二次确认",
                        $"本次可变更 {guardPreview.WillChangeCount.ToString(CultureInfo.InvariantCulture)} 条，已超过阈值 {_inventory.LargeBatchReassignConfirmThreshold.ToString(CultureInfo.InvariantCulture)}请再次确认是否提交");
                    if (!secondOk)
                    {
                        return;
                    }
                }

                result = await _inventory.ReassignByKeywordAsync(
                    kw,
                    new StockReassignContext(
                        targetDrug,
                        targetSpec,
                        targetQty,
                        reason,
                        operatorName,
                        "inventory_ui"),
                    default);
            }

            _toast.Success("药品纠错", $"纠错成功 {result.AffectedRows} 条，审计ID={result.AuditId}");
            ReassignPreviewText = $"提交成功：影响 {result.AffectedRows} 条，审计ID={result.AuditId}";
            IsReassignPanelVisible = false;
            ReassignReason = null;
            ReassignPreviewRows.Clear();
            OnPropertyChanged(nameof(IsReassignPreviewEmpty));

            if (!isSingleScope)
            {
                // Batch mode: keyword usually becomes stale after reassignment.
                Keyword = null;
            }

            var updatedRows = ApplyReassignToCurrentDetailRows(
                isSingleScope,
                targetDrug,
                targetSpec,
                selectedTraceCodes,
                targetQty,
                batchKeyword);
            SuppressExternalAutoRefresh(TimeSpan.FromSeconds(7));
            ScheduleSilentCurrentPageReconcile(TimeSpan.FromSeconds(5));
            Status = updatedRows.Count > 0
                ? $"库存明细：本页已同步 {updatedRows.Count} 行（未整页刷新）"
                : "库存明细：纠错已提交（当前页无可同步行）";
        }
        catch (Exception ex)
        {
            LogError("inventory.reassign.apply_fail", "Failed to apply reassign operation", ex);
            _toast.Error("药品纠错", ex.Message);
            ReassignPreviewText = $"提交失败：{ex.Message}";
        }
        finally
        {
            await SetReassignBusyAsync(false);
        }
    }

    public Task CommitStockCellEditAsync(StockRowItem? row, string? header, string? newValue)
    {
        if (row is null || !IsStockEditEnabled || !IsDetailMode)
        {
            return Task.CompletedTask;
        }

        BuildPendingEditsFromSnapshot();
        Status = _pendingStockEdits.Count > 0
            ? $"库存明细：已暂存变更 {_pendingStockEdits.Count} 项"
            : "库存明细：未检测到变更";
        NotifyPendingChangesState();

        return Task.CompletedTask;
    }

    public void NotifyReadonlyStockColumnEditAttempt(string? header)
    {
        if (!IsStockEditEnabled || !IsDetailMode)
        {
            return;
        }

        if (ShouldSkipTrigger("inventory.stock.readonly_column_edit", 1200))
        {
            return;
        }

        var col = string.IsNullOrWhiteSpace(header) ? "该列" : header;
        _toast.Warn("库存明细编辑", $"{col}不可直接编辑，请使用药品纠错");
    }

    public async Task DeleteSelectedStockRowsAsync(StockRowItem? contextRow)
    {
        if (!IsDetailMode)
        {
            return;
        }

        if (!IsStockEditEnabled)
        {
            _toast.Warn("库存明细删除", "请先进入编辑模式，再执行删除");
            return;
        }

        if (!await EnsureUnlockedAsync("库存明细删除"))
        {
            return;
        }

        var selectedRows = GetEffectiveSelectedRows();
        if (selectedRows.Count == 0 && contextRow is not null)
        {
            selectedRows.Add(contextRow);
        }

        var traceCodes = selectedRows
            .Select(x => NormalizeInput(x.TraceCode))
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.Ordinal)
            .Cast<string>()
            .ToArray();
        if (traceCodes.Length == 0)
        {
            _toast.Warn("库存明细删除", "未找到可删除的追溯码");
            return;
        }

        var ok = await _dialog.Confirm(
            "确认删除",
            $"将删除 {traceCodes.Length.ToString(CultureInfo.InvariantCulture)} 条库存明细记录，操作不可撤销是否继续？");
        if (!ok)
        {
            return;
        }

        try
        {
            var wasEditing = IsStockEditEnabled;
            SuppressExternalAutoRefresh(TimeSpan.FromSeconds(8));
            await RunOnUiAsync(() => IsDetailBusy = true);
            var affected = await _inventory.DeleteStockByTraceCodesAsync(traceCodes, default);
            await ReloadAsync(preserveEditSession: wasEditing);

            await RunOnUiAsync(() =>
            {
                if (wasEditing)
                {
                    IsStockEditEnabled = true;
                    _pendingStockEdits.Clear();
                    CaptureStockEditSnapshotFromCurrentRows();
                }

                _toast.Success("库存明细删除", $"删除成功 {affected.ToString(CultureInfo.InvariantCulture)} 条");
                Status = $"库存明细：已删除 {affected.ToString(CultureInfo.InvariantCulture)} 条";
            });
        }
        catch (Exception ex)
        {
            LogError("inventory.stock.delete_fail", "Failed to delete stock rows", ex);
            await RunOnUiAsync(() => _toast.Error("库存明细删除", ex.Message));
        }
        finally
        {
            await RunOnUiAsync(() => IsDetailBusy = false);
        }
    }

    private async Task<(int SavedCount, int FailedCount, string? LastError)> ApplyPendingStockEditsAsync()
    {
        var edits = new PendingStockEdit[_pendingStockEdits.Count];
        _pendingStockEdits.CopyTo(edits, 0);
        _pendingStockEdits.Clear();
        if (edits.Length == 0)
        {
            return (0, 0, null);
        }

        var batchResult = await _inventory.ApplyStockCellEditsAsync(
            edits.Select(e => new StockCellEditRequest(e.MatchTraceCode, e.ColumnHeader, e.NewValue)).ToArray(),
            default).ConfigureAwait(false);

        if (batchResult.FailedCount > 0)
        {
            await ReloadAsync();
        }

        return (batchResult.SavedCount, batchResult.FailedCount, batchResult.LastError);
    }

    private void BuildPendingEditsFromSnapshot()
    {
        _pendingStockEdits.Clear();
        foreach (var row in StockRows)
        {
            if (!_stockEditSnapshotByRow.TryGetValue(row.RowNo, out var snap))
            {
                continue;
            }

            var oldTrace = (snap.TraceCode ?? string.Empty).Trim();
            var newTrace = (row.TraceCode ?? string.Empty).Trim();
            var traceChanged = !string.Equals(oldTrace, newTrace, StringComparison.Ordinal);

            if (traceChanged)
            {
                _pendingStockEdits.Add(new PendingStockEdit(
                    MatchTraceCode: oldTrace,
                    ColumnHeader: "追溯码",
                    NewValue: newTrace,
                    DrugId: row.DrugId,
                    Spec: row.Spec));
            }

            var matchTrace = traceChanged ? newTrace : oldTrace;

            if (snap.Remain != row.Remain)
            {
                _pendingStockEdits.Add(new PendingStockEdit(
                    MatchTraceCode: matchTrace,
                    ColumnHeader: "剩余",
                    NewValue: row.Remain.ToString(),
                    DrugId: row.DrugId,
                    Spec: row.Spec));
            }
        }
    }

    private void CaptureStockEditSnapshotFromCurrentRows()
    {
        _stockEditSnapshotByRow.Clear();
        foreach (var row in StockRows)
        {
            _stockEditSnapshotByRow[row.RowNo] = new StockEditSnapshot(
                TraceCode: row.TraceCode,
                Remain: row.Remain);
        }
    }

    private bool HasPendingStockChanges()
        => _pendingStockEdits.Count > 0;

    private int GetPendingStockChangeCount()
        => _pendingStockEdits.Count;

    private async Task<bool> EnsureUnlockedAsync(string scene)
    {
        var hint = "敏感操作提示：验证仅在本地进行，不会上传密码\n请输入数据库密码以解锁库存敏感操作";
        var ok = await _unlockService.EnsureUnlockedAsync(
            UnlockScopeKey,
            scene,
            "身份验证",
            hint);
        RefreshUnlockState();
        return ok;
    }

    private void RefreshUnlockState()
    {
        var wasUnlocked = IsOperationUnlocked;
        _unlockService.Refresh(UnlockScopeKey);
        var snap = _unlockService.GetSnapshot(UnlockScopeKey);

        IsOperationUnlocked = snap.IsUnlocked;
        OperationUnlockExpiresAtUtc = snap.ExpiresAtUtc;
        OperationUnlockFailedAttempts = snap.FailedAttempts;
        OperationUnlockCooldownUntilUtc = snap.CooldownUntilUtc;

        if (wasUnlocked && !IsOperationUnlocked)
        {
            if (IsStockEditEnabled)
            {
                AbandonPendingStockEditsIfNeeded();
            }

            IsReassignPanelVisible = false;
            ReassignPreviewRows.Clear();
            OnPropertyChanged(nameof(IsReassignPreviewEmpty));
            if (IsDetailMode)
            {
                Status = "库存安全会话已过期，请重新验证";
            }
        }

        if (IsOperationUnlocked || OperationUnlockCooldownUntilUtc > DateTimeOffset.UtcNow)
        {
            StartUnlockStatusTimerIfNeeded();
        }
        else
        {
            StopUnlockStatusTimerIfNeeded();
        }
    }

    private void StartUnlockStatusTimerIfNeeded()
    {
        if (!_unlockStatusTimer.IsEnabled)
        {
            _unlockStatusTimer.Start();
        }
    }

    private void StopUnlockStatusTimerIfNeeded()
    {
        if (_unlockStatusTimer.IsEnabled)
        {
            _unlockStatusTimer.Stop();
        }
    }

    private void OnUnlockStatusTimerTick(object? sender, EventArgs e)
        => RefreshUnlockState();

    public void SyncUnlockStateForUi()
        => RefreshUnlockState();

    private static async Task SetReassignBusyAsync(bool value, InventoryOverviewViewModel vm)
    {
        await RunOnUiAsync(() => vm.IsReassignBusy = value);
    }

    private Task SetReassignBusyAsync(bool value)
        => SetReassignBusyAsync(value, this);

    private List<StockRowItem> GetEffectiveSelectedRows()
    {
        if (_selectedStockRows.Count > 0)
        {
            return _selectedStockRows.DistinctBy(x => x.RowNo).ToList();
        }

        return SelectedStockRow is null
            ? new List<StockRowItem>()
            : new List<StockRowItem> { SelectedStockRow };
    }

    private IReadOnlyList<StockRowItem> ApplyReassignToCurrentDetailRows(
        bool singleScope,
        string targetDrug,
        string targetSpec,
        IReadOnlyCollection<string> selectedTraceCodes,
        int targetQty,
        string? batchKeyword)
    {
        if (!IsDetailMode || StockRows.Count == 0)
        {
            return Array.Empty<StockRowItem>();
        }

        var changedRows = new List<StockRowItem>();
        if (singleScope)
        {
            var traces = selectedTraceCodes
                .Select(NormalizeInput)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Cast<string>()
                .ToHashSet(StringComparer.Ordinal);

            foreach (var row in StockRows)
            {
                var trace = NormalizeInput(row.TraceCode);
                if (string.IsNullOrWhiteSpace(trace) || !traces.Contains(trace!))
                {
                    continue;
                }

                ApplyReassignToRow(row, targetDrug, targetSpec, targetQty);
                changedRows.Add(row);
            }
        }
        else
        {
            var kw = NormalizeInput(batchKeyword);
            if (!string.IsNullOrWhiteSpace(kw))
            {
                foreach (var row in StockRows)
                {
                    if (!MatchesKeyword(row, kw!))
                    {
                        continue;
                    }

                    // Keep semantics aligned with backend update predicate.
                    if (string.Equals(NormalizeInput(row.DrugId), targetDrug, StringComparison.Ordinal) &&
                        string.Equals(NormalizeInput(row.Spec), targetSpec, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    ApplyReassignToRow(row, targetDrug, targetSpec, targetQty);
                    changedRows.Add(row);
                }
            }
        }

        if (changedRows.Count > 0)
        {
            SetSelectedStockRows(changedRows);
            SelectedStockRow ??= changedRows[0];
        }

        OnPropertyChanged(nameof(IsStockEmpty));
        return changedRows;
    }

    private static void ApplyReassignToRow(
        StockRowItem row,
        string targetDrug,
        string targetSpec,
        int targetQty)
    {
        row.DrugId = targetDrug;
        row.Spec = targetSpec;
        row.Qty = targetQty;
        row.Remain = Math.Min(row.Remain, targetQty);
        row.IsLow = row.Remain < row.Qty;
    }

    private static bool MatchesKeyword(StockRowItem row, string keyword)
        => TextSearchHelper.MatchesAny(keyword, row.DrugId, row.Spec, row.TraceCode);

    private void AbandonPendingStockEditsIfNeeded()
    {
        if (!IsStockEditEnabled)
        {
            return;
        }

        BuildPendingEditsFromSnapshot();
        if (_pendingStockEdits.Count <= 0)
        {
            IsStockEditEnabled = false;
            return;
        }

        RevertStockRowsFromSnapshot();
        IsStockEditEnabled = false;
        _pendingStockEdits.Clear();
        Status = "库存明细：检测到操作切换，未保存编辑已丢弃";
        NotifyPendingChangesState();
    }

    private void RevertStockRowsFromSnapshot()
    {
        foreach (var row in StockRows)
        {
            if (!_stockEditSnapshotByRow.TryGetValue(row.RowNo, out var snap))
            {
                continue;
            }

            row.TraceCode = snap.TraceCode;
            row.Remain = snap.Remain;
        }
    }

    partial void OnStatusChanged(string? value)
        => OnPropertyChanged(nameof(ShowInventoryStatus));

    partial void OnModeIndexChanged(int value)
    {
        if (value != _lastModeIndex && IsStockEditEnabled && HasPendingStockChanges())
            AbandonPendingStockEditsIfNeeded();

        if (value != 0)
        {
            IsReassignPanelVisible = false;
            ReassignPreviewText = null;
            ReassignPreviewRows.Clear();
            OnPropertyChanged(nameof(IsReassignPreviewEmpty));

            if (!string.IsNullOrWhiteSpace(Status)
                && Status.Contains("库存安全会话", StringComparison.Ordinal))
            {
                Status = null;
            }
        }

        _lastModeIndex = value;

        OnPropertyChanged(nameof(IsDetailMode));
        OnPropertyChanged(nameof(IsAggMode));
        OnPropertyChanged(nameof(IsLowMode));
        OnPropertyChanged(nameof(IsMissingMode));
        OnPropertyChanged(nameof(CanEnableStockEdit));
        OnPropertyChanged(nameof(CanDisableStockEdit));
        OnPropertyChanged(nameof(CanRequestUnlock));
        OnPropertyChanged(nameof(CanLockOperations));
        OnPropertyChanged(nameof(ShowRequestUnlock));
        OnPropertyChanged(nameof(ShowLockOperations));
        OnPropertyChanged(nameof(UnlockStatusText));
        OnPropertyChanged(nameof(ShowUnlockStatus));
        OnPropertyChanged(nameof(CanToggleReassignPanel));
        OnPropertyChanged(nameof(EditSessionStateText));
        OnPropertyChanged(nameof(ShowEditSessionState));

        if (PageIndex != 1)
            PageIndex = 1;

        RefreshPagingState();
        RefreshUnlockState();
        _ = ReloadAsync();
    }

    partial void OnPageIndexChanged(int value)
    {
        RefreshPagingState();
        NotifyAllCommands();
    }

    partial void OnPageSizeChanged(int value)
    {
        if (value <= 0)
        {
            return;
        }

        if (PageIndex != 1)
        {
            PageIndex = 1;
        }

        RefreshPagingState();
        NotifyAllCommands();
        _ = ReloadAsync();
    }

    partial void OnKeywordChanged(string? value)
    {
        if (IsFilterReassignScope)
        {
            ReassignPreviewText = null;
            ReassignPreviewRows.Clear();
            OnPropertyChanged(nameof(IsReassignPreviewEmpty));
            NotifyAllCommands();
            return;
        }

        NotifyAllCommands();

        if (string.IsNullOrWhiteSpace(value))
        {
            _keywordSearchDebouncer.Cancel();
            AbandonPendingStockEditsIfNeeded();
            PageIndex = 1;
            _ = ReloadAsync();
            return;
        }

        _keywordSearchDebouncer.Schedule(async () =>
            await Dispatcher.UIThread.InvokeAsync(async () =>
            {
                AbandonPendingStockEditsIfNeeded();
                PageIndex = 1;
                await ReloadAsync().ConfigureAwait(true);
            }));
    }

    partial void OnTotalCountChanged(int value)
    {
        RefreshPagingState();
    }

    [RelayCommand]
    private async Task SearchAsync()
    {
        if (ShouldSkipTrigger(milliseconds: 350))
        {
            return;
        }

        _keywordSearchDebouncer.Cancel();
        AbandonPendingStockEditsIfNeeded();

        PageIndex = 1;
        await ReloadAsync();
    }

    [RelayCommand]
    private Task ClearSearchAsync()
    {
        if (ShouldSkipTrigger(milliseconds: 350))
        {
            return Task.CompletedTask;
        }

        _keywordSearchDebouncer.Cancel();
        AbandonPendingStockEditsIfNeeded();
        Keyword = null;
        return Task.CompletedTask;
    }

    [RelayCommand(CanExecute = nameof(CanGoFirstPage))]
    private async Task FirstPageAsync()
    {
        if (ShouldSkipTrigger("inventory.page.first", 180))
        {
            return;
        }

        if (!CanGoFirstPage())
        {
            return;
        }

        AbandonPendingStockEditsIfNeeded();

        PageIndex = 1;
        await ReloadAsync();
    }

    [RelayCommand(CanExecute = nameof(CanGoPrevPage))]
    private async Task PrevPageAsync()
    {
        if (ShouldSkipTrigger("inventory.page.prev", 180))
        {
            return;
        }

        if (!CanGoPrevPage())
        {
            return;
        }

        AbandonPendingStockEditsIfNeeded();

        PageIndex--;
        await ReloadAsync();
    }

    [RelayCommand(CanExecute = nameof(CanGoNextPage))]
    private async Task NextPageAsync()
    {
        if (ShouldSkipTrigger("inventory.page.next", 180))
        {
            return;
        }

        if (!CanGoNextPage())
        {
            return;
        }

        AbandonPendingStockEditsIfNeeded();

        PageIndex++;
        await ReloadAsync();
    }

    private bool CanGoFirstPage() => CanOperateUi() && HasPrevPage;
    private bool CanGoPrevPage() => CanOperateUi() && HasPrevPage;
    private bool CanGoNextPage() => CanOperateUi() && HasNextPage;
    private bool CanGoLastPage() => CanOperateUi() && HasNextPage;

    [RelayCommand(CanExecute = nameof(CanGoLastPage))]
    private async Task LastPageAsync()
    {
        if (ShouldSkipTrigger("inventory.page.last", 180))
        {
            return;
        }

        if (!CanGoLastPage())
        {
            return;
        }

        AbandonPendingStockEditsIfNeeded();

        PageIndex = TotalPages;
        await ReloadAsync();
    }

    private void SuppressExternalAutoRefresh(TimeSpan duration)
    {
        var until = DateTimeOffset.UtcNow + duration;
        if (until > _suppressAutoRefreshUntilUtc)
        {
            _suppressAutoRefreshUntilUtc = until;
        }

        NotifyAllCommands();
    }

    public bool ShouldDeferExternalRefreshForTopic(string? topic)
    {
        if (DateTimeOffset.UtcNow >= _suppressAutoRefreshUntilUtc)
        {
            return false;
        }

        var key = (topic ?? string.Empty).Trim().ToLowerInvariant();
        return key is "inventory" or "trace_pool" or "trace_txn" or "trace_txn_item" or "";
    }

    private void ScheduleSilentCurrentPageReconcile(TimeSpan delay)
    {
        _silentReconcileCts?.Cancel();
        _silentReconcileCts?.Dispose();
        _silentReconcileCts = new CancellationTokenSource();

        var page = PageIndex;
        var keyword = NormalizeInput(Keyword);
        _ = RunSilentCurrentPageReconcileAsync(delay, page, keyword, _silentReconcileCts.Token);
    }

    private async Task RunSilentCurrentPageReconcileAsync(
        TimeSpan delay,
        int page,
        string? keyword,
        CancellationToken ct)
    {
        try
        {
            await Task.Delay(delay, ct).ConfigureAwait(false);
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(LookupTimeout);
            var pageResult = await _inventory.GetStockPageAsync(keyword, page, PageSize, timeoutCts.Token).ConfigureAwait(false);
            var rebuiltRows = BuildStockRowItems(pageResult.Rows, ((page - 1) * PageSize) + 1);

            await RunOnUiAsync(() =>
            {
                if (ct.IsCancellationRequested)
                {
                    return;
                }

                if (!IsDetailMode || page != PageIndex)
                {
                    return;
                }

                if (!string.Equals(NormalizeInput(Keyword), keyword, StringComparison.Ordinal))
                {
                    return;
                }

                // Silent reconcile: keep in-place update only when row count is unchanged.
                // If count changed, rebuild the page rows to avoid stale tail rows.
                if (StockRows.Count != pageResult.Rows.Count)
                {
                    ClearStockSelection();
                    ApplyStockRowsInPlace(rebuiltRows);
                    OnPropertyChanged(nameof(IsStockEmpty));
                }
                else
                {
                    for (var i = 0; i < StockRows.Count; i++)
                    {
                        var dst = StockRows[i];
                        var src = pageResult.Rows[i];
                        dst.DrugId = src.DrugId;
                        dst.Spec = src.Spec;
                        dst.TraceCode = src.TraceCode;
                        dst.Qty = src.Qty;
                        dst.Remain = src.Remain;
                        dst.Status = src.Status;
                        dst.IsLow = src.IsLow;
                        dst.IsDeprecated = src.IsDeprecated;
                    }
                }

                TotalCount = pageResult.TotalCount;
                Status = $"库存明细：{TotalCount} 行（第 {PageIndex}/{TotalPages} 页）";
            }, DispatcherPriority.Background);
        }
        catch (OperationCanceledException)
        {
        }
        catch (System.Exception ex)
        {
            LogWarn("inventory.external_refresh.reconcile_fail", "Failed to reconcile current detail page after external change", ex);
        }
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
            AbandonPendingStockEditsIfNeeded();
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
            NotifyPendingChangesState();
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

    private void NotifyPendingChangesState()
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
        StopUnlockStatusTimerIfNeeded();
        _unlockStatusTimer.Tick -= OnUnlockStatusTimerTick;
        _silentReconcileCts?.Cancel();
        _silentReconcileCts?.Dispose();
        _silentReconcileCts = null;
        _keywordSearchDebouncer.Dispose();
        base.Dispose();
    }

    public void NotifyDrugIndexChanged()
    {
        PostOnUi(() => _ = ReloadAsync(), DispatcherPriority.Background);
    }

    private sealed record PendingStockEdit(
        string MatchTraceCode,
        string ColumnHeader,
        string NewValue,
        string DrugId,
        string Spec);

    private sealed record StockEditSnapshot(string TraceCode, int Remain);
}
