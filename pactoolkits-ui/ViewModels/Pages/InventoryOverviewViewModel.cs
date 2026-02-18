using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Material.Icons;
using pactoolkits_ui.Common;
using pactoolkits_ui.Contracts;
using pactoolkits_ui.Services;
using pactoolkits_ui.Repositories;

namespace pactoolkits_ui.ViewModels.Pages;

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
        bool isLow)
    {
        RowNo = rowNo;
        DrugId = drugId;
        Spec = spec;
        TraceCode = traceCode;
        Qty = qty;
        Remain = remain;
        Status = status;
        IsLow = isLow;
    }

    public int RowNo { get; }

    [ObservableProperty] private string _drugId;
    [ObservableProperty] private string _spec;
    [ObservableProperty] private string _traceCode;
    [ObservableProperty] private int _qty;
    [ObservableProperty] private int _remain;
    [ObservableProperty] private int _status;
    [ObservableProperty] private bool _isLow;
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
    bool IsLow
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
    private const int FixedPageSize = 50;
    private const int LargeBatchReassignConfirmThreshold = 500;
    private static readonly TimeSpan LookupTimeout = TimeSpan.FromSeconds(8);
    private static readonly TimeSpan UnlockSessionDuration = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan UnlockCooldownDuration = TimeSpan.FromMinutes(1);
    private const int UnlockFailedAttemptThreshold = 5;

    public override string DisplayName => "追溯码库存";
    public override MaterialIconKind Icon => MaterialIconKind.PackageVariant;
    public override int Index => 1;
    public override ICommand RefreshCommand => _localRefreshCommand;

    private readonly IInventoryOverviewRepo _repo;
    private readonly ILookupCatalogService _lookup;
    private readonly IDrugIndexRepo _drugIndexRepo;
    private readonly IDbConfigService _dbConfig;
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

    [ObservableProperty] private int _modeIndex;
    [ObservableProperty] private string? _keyword;
    [ObservableProperty] private string? _status;
    [ObservableProperty] private int _pageIndex = 1;
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
    private readonly DispatcherTimer _unlockStatusTimer;
    private bool _isUnlockPromptActive;
    partial void OnIsDetailBusyChanged(bool value) => NotifyAllCommands();
    partial void OnIsAggBusyChanged(bool value) => NotifyAllCommands();
    partial void OnIsLowBusyChanged(bool value) => NotifyAllCommands();
    partial void OnIsMissingBusyChanged(bool value) => NotifyAllCommands();
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
        NotifyAllCommands();
    }

    partial void OnOperationUnlockFailedAttemptsChanged(int value)
        => OnPropertyChanged(nameof(UnlockStatusText));

    partial void OnOperationUnlockCooldownUntilUtcChanged(DateTimeOffset value)
    {
        OnPropertyChanged(nameof(UnlockStatusText));
        OnPropertyChanged(nameof(CanRequestUnlock));
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
    public bool CanToggleReassignPanel
        => IsDetailMode
           && !IsStockEditEnabled
           && !IsReassignBusy;
    public bool CanPreviewReassign
        => IsReassignPanelVisible
           && (IsSingleReassignScope
               ? (_selectedStockRows.Count > 0 || SelectedStockRow is not null)
               : !string.IsNullOrWhiteSpace(NormalizeInput(Keyword)))
           && !string.IsNullOrWhiteSpace(NormalizeInput(ReassignTargetDrugId))
           && !string.IsNullOrWhiteSpace(NormalizeInput(ReassignTargetSpec))
           && int.TryParse(NormalizeInput(ReassignQtyText), out var previewQty)
           && previewQty > 0
           && !IsReassignBusy;
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
                return string.Empty;

            return IsOperationUnlocked ? "已解锁" : "未解锁";
        }
    }
    public bool ShowUnlockStatus => IsDetailMode;
    public bool ShowEditSessionState => IsDetailMode && IsStockEditEnabled;
    public bool IsStockEmpty => StockRows.Count == 0;
    public bool IsAggEmpty => DrugSpecRows.Count == 0;
    public bool IsLowEmpty => LowStockRows.Count == 0;
    public bool IsMissingEmpty => MissingStockRows.Count == 0;
    public bool IsPagedMode => ModeIndex is 0 or 1 or 2 or 3;
    public int PageSize => FixedPageSize;
    public int TotalPages => Math.Max(1, (int)Math.Ceiling(TotalCount / (double)PageSize));
    public bool HasPrevPage => IsPagedMode && PageIndex > 1;
    public bool HasNextPage => IsPagedMode && PageIndex < TotalPages;
    public bool HasPendingChanges => IsStockEditEnabled && _pendingStockEdits.Count > 0;
    public IReadOnlyList<StockRowItem> SelectedStockRowsSnapshot => _selectedStockRowsSnapshot;

    public InventoryOverviewViewModel(
        IInventoryOverviewRepo repo,
        ILookupCatalogService lookup,
        IDrugIndexRepo drugIndexRepo,
        IDbConfigService dbConfig,
        IToastService toast,
        IDialogService dialog,
        PageNavigationService nav,
        ScanCodeViewModel scanCode)
    {
        _repo = repo;
        _lookup = lookup;
        _drugIndexRepo = drugIndexRepo;
        _dbConfig = dbConfig;
        _toast = toast;
        _dialog = dialog;
        _nav = nav;
        _scanCode = scanCode;
        _localRefreshCommand = new AsyncRelayCommand(() => ReloadAsync(force: true), CanLocalRefresh);
        _unlockStatusTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _unlockStatusTimer.Tick += (_, _) => RefreshUnlockState();

        _dbConfig.Applied += OnDbApplied;
        _lastModeIndex = ModeIndex;

        PostOnUi(() => _ = ReloadAsync(force: false), DispatcherPriority.Background);
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
            _selectedStockRows.Add(row);
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
        => NotifyAllCommands();

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
        => IsReassignPanelVisible && !IsReassignBusy;

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
                await RunOnUiAsync(() =>
                {
                    ReassignSpecOptions.Clear();
                    ReassignSelectedSpec = null;
                    ReassignQtyText = null;
                    IsReassignSpecSelected = false;
                    ReassignTargetDrugId = null;
                    ReassignTargetSpec = null;
                    ReassignPreviewText = "纠错上下文：药品不存在，请重新输入";
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
        if (ReassignDrugOptions.Count > 0)
            return;

        try
        {
            using var cts = new CancellationTokenSource(LookupTimeout);
            var drugs = await _lookup.GetDrugIdsAsync(cts.Token).ConfigureAwait(false);
            await RunOnUiAsync(() =>
            {
                ReplaceOptions(ReassignDrugOptions, drugs);
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
                drug = canonicalDrug;

            var qty = await _lookup.GetQtyAsync(drug, spec, cts.Token).ConfigureAwait(false);
            await RunOnUiAsync(() =>
            {
                ReassignTargetDrugId = drug;
                ReassignQtyText = qty is null ? null : qty.Value.ToString(CultureInfo.InvariantCulture);
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

    private static void ReplaceOptions(ObservableCollection<OptionItem> target, IReadOnlyList<string> raws)
        => OptionCollectionHelper.ReplaceRaw(target, raws, StringComparison.Ordinal);

    private async Task TryAutoResolveReassignContextAsync(string? drugText)
    {
        var drug = NormalizeInput(drugText);
        if (string.IsNullOrWhiteSpace(drug))
            return;

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
            return;

        await LoadReassignSpecsByDrugAsync(canonical);
    }

    private async Task LoadReassignSpecsByDrugAsync(string canonicalDrug)
    {
        try
        {
            using var cts = new CancellationTokenSource(LookupTimeout);
            var specs = await _lookup.GetSpecsByDrugAsync(canonicalDrug, cts.Token).ConfigureAwait(false);
            await RunOnUiAsync(() =>
            {
                ReassignDrugText = canonicalDrug;
                IsReassignDrugSuggestOpen = false;
                ReassignTargetDrugId = canonicalDrug;
                ReplaceOptions(ReassignSpecOptions, specs);
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

    private bool CanLocalRefresh()
        => !IsBusy
           && !IsDetailBusy
           && !IsAggBusy
           && !IsLowBusy
           && !IsMissingBusy
           && !IsReassignBusy
           && DateTimeOffset.UtcNow >= _suppressAutoRefreshUntilUtc;

    private bool CanRequestUnlockCore()
        => !IsBusy
           && IsDetailMode
           && !IsDetailBusy
           && !IsReassignBusy;

    [RelayCommand(CanExecute = nameof(CanRequestUnlockCore))]
    private async Task RequestUnlockAsync()
    {
        await EnsureUnlockedAsync("库存编辑与药品纠错");
    }

    private bool CanLockOperationsCore()
        => !IsBusy
           && IsDetailMode
           && IsOperationUnlocked;

    [RelayCommand(CanExecute = nameof(CanLockOperationsCore))]
    private async Task LockOperationsAsync()
    {
        if (IsStockEditEnabled)
            await ToggleStockEditMode();

        IsReassignPanelVisible = false;
        ReassignPreviewText = null;
        ReassignPreviewRows.Clear();
        OnPropertyChanged(nameof(IsReassignPreviewEmpty));
        IsOperationUnlocked = false;
        OperationUnlockExpiresAtUtc = DateTimeOffset.MinValue;
        StopUnlockStatusTimerIfNeeded();
        Status = "库存安全会话：已手动锁定";
        OnPropertyChanged(nameof(UnlockStatusText));
    }

    private bool CanToggleStockEditMode() => !IsBusy && IsDetailMode && !IsDetailBusy;

    [RelayCommand(CanExecute = nameof(CanToggleStockEditMode))]
    private async Task ToggleStockEditMode()
    {
        if (ShouldSkipTrigger("inventory.stock.edit", 250))
            return;

        if (IsStockEditEnabled)
        {
            BuildPendingEditsFromSnapshot();
            var (savedCount, failedCount, lastError) = (0, 0, (string?)null);
            if (_pendingStockEdits.Count > 0)
                (savedCount, failedCount, lastError) = await ApplyPendingStockEditsAsync();

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
            return;

        _pendingStockEdits.Clear();
        CaptureStockEditSnapshotFromCurrentRows();
        IsStockEditEnabled = true;
        Status = "库存明细：已进入编辑模式";
        _lastModeIndex = ModeIndex;
    }

    public async Task OpenScanCodeByRowAsync(string? drugId, string? spec)
    {
        if (!IsLowMode && !IsMissingMode)
            return;

        var d = NormalizeInput(drugId);
        var s = NormalizeInput(spec);
        if (string.IsNullOrWhiteSpace(d) || string.IsNullOrWhiteSpace(s))
            return;

        _nav.Navigate<ScanCodeViewModel>();
        await _scanCode.PrefillFromInventoryAsync(d, s);
    }

    [RelayCommand(CanExecute = nameof(CanToggleReassignPanel))]
    private Task ToggleReassignPanelAsync()
    {
        if (ShouldSkipTrigger("inventory.reassign.panel", 250))
            return Task.CompletedTask;

        if (!CanToggleReassignPanel)
            return Task.CompletedTask;
        
        return ToggleReassignPanelInnerAsync();
    }

    private async Task ToggleReassignPanelInnerAsync()
    {
        if (!IsReassignPanelVisible && !await EnsureUnlockedAsync("药品纠错"))
            return;

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
            return;

        var targetDrug = NormalizeInput(ReassignTargetDrugId);
        var targetSpec = NormalizeInput(ReassignTargetSpec);
        if (string.IsNullOrWhiteSpace(targetDrug) || string.IsNullOrWhiteSpace(targetSpec))
            return;

        await SetReassignBusyAsync(true);
        try
        {
            if (IsSingleReassignScope)
            {
                var selectedRows = GetEffectiveSelectedRows();
                if (selectedRows.Count == 0)
                    return;

                using var existsCts = new CancellationTokenSource(LookupTimeout);
                var targetExists = await _drugIndexRepo.GetByKeyAsync(targetDrug, targetSpec, existsCts.Token);
                if (targetExists is null)
                {
                    ReassignPreviewText = "预览结果：目标药品/规格不存在，无法纠错";
                    return;
                }

                var targetQtyResolved = int.TryParse(NormalizeInput(ReassignQtyText), out var parsedQty)
                    ? parsedQty
                    : 0;

                ReassignPreviewRows.Clear();
                var willChangeCount = 0;
                foreach (var row in selectedRows)
                {
                    var willChange = !string.Equals(row.DrugId, targetDrug, StringComparison.Ordinal)
                                     || !string.Equals(row.Spec, targetSpec, StringComparison.Ordinal)
                                     || row.Qty != targetQtyResolved;
                    if (willChange)
                        willChangeCount++;

                    ReassignPreviewRows.Add(new StockReassignPreviewRowItem(
                        CurrentDrugId: row.DrugId,
                        CurrentSpec: row.Spec,
                        CurrentQty: row.Qty,
                        CurrentRemain: row.Remain,
                        TargetDrugId: targetDrug,
                        TargetSpec: targetSpec,
                        TargetQty: targetQtyResolved,
                        TraceCode: row.TraceCode));
                }
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

            var preview = await _repo.PreviewStockReassignByKeywordAsync(kw, targetDrug, targetSpec, 30, default);
            var scopeText = $"筛选批量（关键字：{kw}）";

            ReassignPreviewRows.Clear();
            var targetQtyResolvedForFilter = int.TryParse(NormalizeInput(ReassignQtyText), out var parsedQtyForFilter)
                ? parsedQtyForFilter
                : 0;
            foreach (var row in preview.Samples)
            {
                ReassignPreviewRows.Add(new StockReassignPreviewRowItem(
                    CurrentDrugId: row.DrugId,
                    CurrentSpec: row.Spec,
                    CurrentQty: row.Qty,
                    CurrentRemain: row.Remain,
                    TargetDrugId: targetDrug,
                    TargetSpec: targetSpec,
                    TargetQty: targetQtyResolvedForFilter,
                    TraceCode: row.TraceCode));
            }
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

            if (IsFilterReassignScope && preview.WillChangeCount > LargeBatchReassignConfirmThreshold)
            {
                ReassignPreviewText +=
                    $"。注意：可变更数量超过 {LargeBatchReassignConfirmThreshold.ToString(CultureInfo.InvariantCulture)} 条，提交时会触发二次确认";
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
            return;

        var targetDrug = NormalizeInput(ReassignTargetDrugId);
        var targetSpec = NormalizeInput(ReassignTargetSpec);
        var qtyText = NormalizeInput(ReassignQtyText);
        var reason = NormalizeInput(ReassignReason);
        if (string.IsNullOrWhiteSpace(targetDrug)
            || string.IsNullOrWhiteSpace(targetSpec)
            || !int.TryParse(qtyText, out var targetQty)
            || targetQty <= 0
            || string.IsNullOrWhiteSpace(reason))
            return;

        if (!await EnsureUnlockedAsync("药品纠错提交"))
            return;

        var confirmMessage = IsSingleReassignScope
            ? $"将选中追溯码纠错到 {targetDrug}/{targetSpec}，是否继续？"
            : $"将“当前筛选关键字”命中的库存批量纠错到 {targetDrug}/{targetSpec}，是否继续？";
        var ok = await _dialog.Confirm("确认纠错", confirmMessage);
        if (!ok)
            return;

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
                    return;

                var traceCodes = selectedTraceCodes;

                if (traceCodes.Length == 0)
                    return;

                var affected = 0;
                long auditId = 0;
                foreach (var traceCode in traceCodes)
                {
                    var one = await _repo.ReassignStockByTraceCodeAsync(
                        traceCode!,
                        targetDrug,
                        targetSpec,
                        targetQty,
                        reason,
                        operatorName,
                        "inventory_ui",
                        default);
                    affected += one.AffectedRows;
                    if (auditId == 0)
                        auditId = one.AuditId;
                }

                result = new StockReassignApplyResultDto(affected, auditId);
            }
            else
            {
                var kw = NormalizeInput(Keyword);
                if (string.IsNullOrWhiteSpace(kw))
                {
                    _toast.Warn("药品纠错", "批量纠错需要先输入筛选关键字");
                    return;
                }

                var guardPreview = await _repo.PreviewStockReassignByKeywordAsync(kw, targetDrug, targetSpec, 1, default);
                if (guardPreview.WillChangeCount > LargeBatchReassignConfirmThreshold)
                {
                    var secondOk = await _dialog.Confirm(
                        "批量纠错二次确认",
                        $"本次可变更 {guardPreview.WillChangeCount.ToString(CultureInfo.InvariantCulture)} 条，已超过阈值 {LargeBatchReassignConfirmThreshold.ToString(CultureInfo.InvariantCulture)}。请再次确认是否提交。");
                    if (!secondOk)
                        return;
                }

                result = await _repo.ReassignStockByKeywordAsync(
                    kw,
                    targetDrug,
                    targetSpec,
                    targetQty,
                    reason,
                    operatorName,
                    "inventory_ui",
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
            return Task.CompletedTask;

        BuildPendingEditsFromSnapshot();
        Status = _pendingStockEdits.Count > 0
            ? $"库存明细：已暂存变更 {_pendingStockEdits.Count} 项"
            : "库存明细：未检测到变更";
        NotifyPendingChangesState();

        return Task.CompletedTask;
    }

    public async Task DeleteSelectedStockRowsAsync(StockRowItem? contextRow)
    {
        if (!IsDetailMode)
            return;

        if (!IsStockEditEnabled)
        {
            _toast.Warn("库存明细删除", "请先进入编辑模式，再执行删除");
            return;
        }

        if (!await EnsureUnlockedAsync("库存明细删除"))
            return;

        var selectedRows = GetEffectiveSelectedRows();
        if (selectedRows.Count == 0 && contextRow is not null)
            selectedRows.Add(contextRow);

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
            $"将删除 {traceCodes.Length.ToString(CultureInfo.InvariantCulture)} 条库存明细记录，操作不可撤销。是否继续？");
        if (!ok)
            return;

        try
        {
            var wasEditing = IsStockEditEnabled;
            SuppressExternalAutoRefresh(TimeSpan.FromSeconds(8));
            await RunOnUiAsync(() => IsDetailBusy = true);
            var affected = await _repo.DeleteStockByTraceCodesAsync(traceCodes, default);
            await ReloadAsync(force: true, preserveEditSession: wasEditing);

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
            return (0, 0, null);

        var savedCount = 0;
        var failedCount = 0;
        string? lastError = null;

        foreach (var edit in edits)
        {
            try
            {
                await _repo.UpdateStockCellAsync(edit.MatchTraceCode, edit.ColumnHeader, edit.NewValue, default).ConfigureAwait(false);
                savedCount++;
            }
            catch (Exception ex)
            {
                LogWarn("inventory.stock.batch_update.one_fail", "Failed one stock cell update in batch", ex, new
                {
                    edit.DrugId,
                    edit.Spec,
                    edit.ColumnHeader
                });
                failedCount++;
                lastError = $"{edit.DrugId}/{edit.Spec} {edit.ColumnHeader}: {ex.Message}";
            }
        }

        if (failedCount > 0)
            await ReloadAsync(force: true);

        return (savedCount, failedCount, lastError);
    }

    private void BuildPendingEditsFromSnapshot()
    {
        _pendingStockEdits.Clear();
        foreach (var row in StockRows)
        {
            if (!_stockEditSnapshotByRow.TryGetValue(row.RowNo, out var snap))
                continue;

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
        if (_isUnlockPromptActive)
            return false;

        RefreshUnlockState();

        if (IsOperationUnlocked)
            return true;

        var expectedPassword = NormalizeInput(_dbConfig.Current.Password);
        if (string.IsNullOrWhiteSpace(expectedPassword))
        {
            _toast.Error(scene, "当前未配置数据库密码，无法执行该操作");
            return false;
        }

        var now = DateTimeOffset.UtcNow;
        if (OperationUnlockCooldownUntilUtc > now)
        {
            var left = OperationUnlockCooldownUntilUtc - now;
            _toast.Warn(scene, $"验证冷却中，请在 {Math.Max(1, (int)Math.Ceiling(left.TotalSeconds))} 秒后重试");
            return false;
        }

        string? input;
        try
        {
            _isUnlockPromptActive = true;
            var hintPrefix = "敏感操作提示：验证仅在本地进行，不会上传密码。";
            var hint = OperationUnlockFailedAttempts <= 0
                ? $"{hintPrefix}\n请输入数据库密码以解锁库存敏感操作"
                : $"{hintPrefix}\n请输入数据库密码（已失败 {OperationUnlockFailedAttempts} 次）";
            input = NormalizeInput(await _dialog.PromptInventoryUnlockPassword("身份验证", hint));
        }
        finally
        {
            _isUnlockPromptActive = false;
        }

        if (string.IsNullOrWhiteSpace(input))
            return false;

        if (!string.Equals(input, expectedPassword, StringComparison.Ordinal))
        {
            OperationUnlockFailedAttempts++;
            if (OperationUnlockFailedAttempts >= UnlockFailedAttemptThreshold)
            {
                OperationUnlockCooldownUntilUtc = DateTimeOffset.UtcNow + UnlockCooldownDuration;
                OperationUnlockFailedAttempts = 0;
                _toast.Error(scene, $"密码连续错误过多，已锁定 {UnlockCooldownDuration.TotalSeconds.ToString(CultureInfo.InvariantCulture)} 秒");
            }
            else
            {
                _toast.Error(scene, $"密码错误，还可重试 {UnlockFailedAttemptThreshold - OperationUnlockFailedAttempts} 次");
            }

            RefreshUnlockState();
            return false;
        }

        IsOperationUnlocked = true;
        OperationUnlockFailedAttempts = 0;
        OperationUnlockCooldownUntilUtc = DateTimeOffset.MinValue;
        OperationUnlockExpiresAtUtc = DateTimeOffset.UtcNow + UnlockSessionDuration;
        StartUnlockStatusTimerIfNeeded();
        RefreshUnlockState();
        _toast.Success(scene, "验证通过，已解锁敏感操作");
        return true;
    }

    private void RefreshUnlockState()
    {
        var now = DateTimeOffset.UtcNow;

        if (OperationUnlockCooldownUntilUtc != DateTimeOffset.MinValue && OperationUnlockCooldownUntilUtc <= now)
            OperationUnlockCooldownUntilUtc = DateTimeOffset.MinValue;

        if (IsOperationUnlocked && OperationUnlockExpiresAtUtc <= now)
        {
            IsOperationUnlocked = false;
            OperationUnlockExpiresAtUtc = DateTimeOffset.MinValue;
            if (IsStockEditEnabled)
                AbandonPendingStockEditsIfNeeded();

            IsReassignPanelVisible = false;
            ReassignPreviewRows.Clear();
            OnPropertyChanged(nameof(IsReassignPreviewEmpty));
            Status = "库存安全会话已过期，请重新验证";
        }

        if (IsOperationUnlocked || OperationUnlockCooldownUntilUtc > now)
            StartUnlockStatusTimerIfNeeded();
        else
            StopUnlockStatusTimerIfNeeded();

        OnPropertyChanged(nameof(UnlockStatusText));
        OnPropertyChanged(nameof(CanRequestUnlock));
        OnPropertyChanged(nameof(CanLockOperations));
    }

    private void StartUnlockStatusTimerIfNeeded()
    {
        if (!_unlockStatusTimer.IsEnabled)
            _unlockStatusTimer.Start();
    }

    private void StopUnlockStatusTimerIfNeeded()
    {
        if (_unlockStatusTimer.IsEnabled)
            _unlockStatusTimer.Stop();
    }

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
            return _selectedStockRows.DistinctBy(x => x.RowNo).ToList();

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
            return Array.Empty<StockRowItem>();

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
                    continue;

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
                        continue;

                    // Keep semantics aligned with backend update predicate.
                    if (string.Equals(NormalizeInput(row.DrugId), targetDrug, StringComparison.Ordinal) &&
                        string.Equals(NormalizeInput(row.Spec), targetSpec, StringComparison.Ordinal))
                        continue;

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
    {
        return ContainsIgnoreCase(row.DrugId, keyword)
               || ContainsIgnoreCase(row.Spec, keyword)
               || ContainsIgnoreCase(row.TraceCode, keyword);
    }

    private static bool ContainsIgnoreCase(string? source, string keyword)
    {
        return !string.IsNullOrWhiteSpace(source)
               && source.Contains(keyword, StringComparison.OrdinalIgnoreCase);
    }

    private void AbandonPendingStockEditsIfNeeded()
    {
        if (!IsStockEditEnabled)
            return;

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
                continue;

            row.TraceCode = snap.TraceCode;
            row.Remain = snap.Remain;
        }
    }

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
        }

        _lastModeIndex = value;

        OnPropertyChanged(nameof(IsDetailMode));
        OnPropertyChanged(nameof(IsAggMode));
        OnPropertyChanged(nameof(IsLowMode));
        OnPropertyChanged(nameof(IsMissingMode));
        OnPropertyChanged(nameof(CanEnableStockEdit));
        OnPropertyChanged(nameof(CanDisableStockEdit));
        OnPropertyChanged(nameof(CanToggleReassignPanel));
        OnPropertyChanged(nameof(EditSessionStateText));
        OnPropertyChanged(nameof(ShowEditSessionState));

        if (PageIndex != 1)
            PageIndex = 1;

        RefreshPagingState();
        RefreshUnlockState();
        _ = ReloadAsync(force: false);
    }

    partial void OnPageIndexChanged(int value)
    {
        RefreshPagingState();
        NotifyAllCommands();
    }

    partial void OnKeywordChanged(string? value)
    {
        if (IsFilterReassignScope)
        {
            ReassignPreviewText = null;
            ReassignPreviewRows.Clear();
            OnPropertyChanged(nameof(IsReassignPreviewEmpty));
        }

        NotifyAllCommands();
    }

    partial void OnTotalCountChanged(int value)
    {
        RefreshPagingState();
    }

    [RelayCommand]
    private async Task SearchAsync()
    {
        if (ShouldSkipTrigger(milliseconds: 350))
            return;
        AbandonPendingStockEditsIfNeeded();

        PageIndex = 1;
        await ReloadAsync(force: true);
    }

    [RelayCommand]
    private async Task ClearSearchAsync()
    {
        if (ShouldSkipTrigger(milliseconds: 350))
            return;
        AbandonPendingStockEditsIfNeeded();

        Keyword = null;
        PageIndex = 1;
        await ReloadAsync(force: true);
    }

    [RelayCommand(CanExecute = nameof(CanGoFirstPage))]
    private async Task FirstPageAsync()
    {
        if (ShouldSkipTrigger("inventory.page.first", 180))
            return;

        if (!CanGoFirstPage())
            return;

        AbandonPendingStockEditsIfNeeded();

        PageIndex = 1;
        await ReloadAsync(force: true);
    }

    [RelayCommand(CanExecute = nameof(CanGoPrevPage))]
    private async Task PrevPageAsync()
    {
        if (ShouldSkipTrigger("inventory.page.prev", 180))
            return;

        if (!CanGoPrevPage())
            return;

        AbandonPendingStockEditsIfNeeded();

        PageIndex--;
        await ReloadAsync(force: true);
    }

    [RelayCommand(CanExecute = nameof(CanGoNextPage))]
    private async Task NextPageAsync()
    {
        if (ShouldSkipTrigger("inventory.page.next", 180))
            return;

        if (!CanGoNextPage())
            return;

        AbandonPendingStockEditsIfNeeded();

        PageIndex++;
        await ReloadAsync(force: true);
    }

    private bool CanGoFirstPage() => !IsBusy && HasPrevPage;
    private bool CanGoPrevPage() => !IsBusy && HasPrevPage;
    private bool CanGoNextPage() => !IsBusy && HasNextPage;

    private void SuppressExternalAutoRefresh(TimeSpan duration)
    {
        var until = DateTimeOffset.UtcNow + duration;
        if (until > _suppressAutoRefreshUntilUtc)
            _suppressAutoRefreshUntilUtc = until;

        NotifyAllCommands();
    }

    public bool ShouldDeferExternalRefreshForTopic(string? topic)
    {
        if (DateTimeOffset.UtcNow >= _suppressAutoRefreshUntilUtc)
            return false;

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
            var pageResult = await _repo.GetStockPageAsync(keyword, page, PageSize, timeoutCts.Token).ConfigureAwait(false);

            await RunOnUiAsync(() =>
            {
                if (ct.IsCancellationRequested)
                    return;
                if (!IsDetailMode || page != PageIndex)
                    return;
                if (!string.Equals(NormalizeInput(Keyword), keyword, StringComparison.Ordinal))
                    return;

                // Silent reconcile: update current rows in place to avoid scroll/selection shake.
                var count = Math.Min(StockRows.Count, pageResult.Rows.Count);
                for (var i = 0; i < count; i++)
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
        => ReloadBodyAsync(force: true, ct);

    protected override void OnReloadFinished()
        => NotifyAllCommands();

    private Task ReloadAsync(bool force, bool preserveEditSession = false)
    {
        if (IsStockEditEnabled && !preserveEditSession)
            AbandonPendingStockEditsIfNeeded();

        var mode = ModeIndex;
        return RunLocalReloadAsync(
            setBusy: v => SetModeBusy(mode, v),
            action: ct => ReloadBodyAsync(force, ct),
            onFinished: NotifyAllCommands);
    }

    private async Task ReloadBodyAsync(bool force, CancellationToken ct)
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
        var page = await _repo
            .GetStockPageAsync(keyword, PageIndex, PageSize, ct)
            .ConfigureAwait(false);

        var start = ((PageIndex - 1) * PageSize) + 1;

        await RunOnUiAsync(() =>
        {
            StockRows.Clear();

            var rowNo = start;
            foreach (var row in page.Rows)
            {
                StockRows.Add(new StockRowItem(
                    rowNo: rowNo++,
                    drugId: row.DrugId,
                    spec: row.Spec,
                    traceCode: row.TraceCode,
                    qty: row.Qty,
                    remain: row.Remain,
                    status: row.Status,
                    isLow: row.IsLow));
            }

            TotalCount = page.TotalCount;
            Status = $"库存明细：{TotalCount} 行（第 {PageIndex}/{TotalPages} 页）";
            OnPropertyChanged(nameof(IsStockEmpty));
        });
    }

    private async Task ReloadAggModeAsync(string? keyword, CancellationToken ct)
    {
        var page = await _repo
            .GetDrugSpecAggPageAsync(keyword, PageIndex, PageSize, ct)
            .ConfigureAwait(false);

        var start = ((PageIndex - 1) * PageSize) + 1;

        await RunOnUiAsync(() =>
        {
            DrugSpecRows.Clear();

            var rowNo = start;
            foreach (var row in page.Rows)
            {
                DrugSpecRows.Add(new DrugSpecAggRowItem(
                    RowNo: rowNo++,
                    DrugId: row.DrugId,
                    Spec: row.Spec,
                    CodeCount: row.CodeCount,
                    QtySum: row.QtySum,
                    RemainSum: row.RemainSum,
                    WeekUsed: row.WeekUsed,
                    Threshold: row.Threshold,
                    IsLow: row.IsLow));
            }

            TotalCount = page.TotalCount;
            Status = $"按药品+规格汇总：{TotalCount} 行（第 {PageIndex}/{TotalPages} 页）";
            OnPropertyChanged(nameof(IsAggEmpty));
        });
    }

    private async Task ReloadLowModeAsync(
        string? keyword,
        CancellationToken ct)
    {
        var page = await _repo
            .GetLowStockPageAsync(keyword, PageIndex, PageSize, ct)
            .ConfigureAwait(false);

        var start = ((PageIndex - 1) * PageSize) + 1;

        await RunOnUiAsync(() =>
        {
            LowStockRows.Clear();
            var rowNo = start;
            foreach (var row in page.Rows)
            {
                LowStockRows.Add(new LowStockRowItem(
                    RowNo: rowNo++,
                    DrugId: row.DrugId,
                    Spec: row.Spec,
                    RemainSum: row.RemainSum,
                    Threshold: row.Threshold,
                    IsLow: row.IsLow));
            }

            TotalCount = page.TotalCount;
            Status = $"低库存：{TotalCount} 项（第 {PageIndex}/{TotalPages} 页）";
            OnPropertyChanged(nameof(IsLowEmpty));
        });
    }

    private async Task ReloadMissingModeAsync(
        string? keyword,
        CancellationToken ct)
    {
        var page = await _repo
            .GetMissingInventoryPageAsync(keyword, PageIndex, PageSize, ct)
            .ConfigureAwait(false);

        var start = ((PageIndex - 1) * PageSize) + 1;

        await RunOnUiAsync(() =>
        {
            MissingStockRows.Clear();

            var rowNo = start;
            foreach (var row in page.Rows)
            {
                MissingStockRows.Add(new MissingStockRowItem(
                    RowNo: rowNo++,
                    DrugId: row.DrugId,
                    Spec: row.Spec,
                    Note: row.Note));
            }

            TotalCount = page.TotalCount;
            Status = $"缺失：{TotalCount} 项（第 {PageIndex}/{TotalPages} 页）";
            OnPropertyChanged(nameof(IsMissingEmpty));
        });
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

        PostOnUi(() => _ = ReloadAsync(force: true), DispatcherPriority.Background);
    }

    private void NotifyAllCommands()
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            PostOnUi(NotifyAllCommands, DispatcherPriority.Background);
            return;
        }

        NotifyCommands(
            _localRefreshCommand,
            SearchCommand,
            ClearSearchCommand,
            FirstPageCommand,
            PrevPageCommand,
            NextPageCommand,
            RequestUnlockCommand,
            LockOperationsCommand,
            ToggleStockEditModeCommand,
            ToggleReassignPanelCommand,
            ApplyReassignDrugFilterCommand,
            PreviewReassignCommand,
            ApplyReassignCommand);
        _localRefreshCommand.NotifyCanExecuteChanged();
        NotifyPendingChangesState();
    }

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

        ModeIndex = next;
        if (forceReload)
            _ = ReloadAsync(force: true);
    }

    public override void Dispose()
    {
        _dbConfig.Applied -= OnDbApplied;
        StopUnlockStatusTimerIfNeeded();
        _silentReconcileCts?.Cancel();
        _silentReconcileCts?.Dispose();
        _silentReconcileCts = null;
        base.Dispose();
    }

    public void NotifyDrugIndexChanged()
    {
        PostOnUi(() => _ = ReloadAsync(force: true), DispatcherPriority.Background);
    }

    private sealed record PendingStockEdit(
        string MatchTraceCode,
        string ColumnHeader,
        string NewValue,
        string DrugId,
        string Spec);

    private sealed record StockEditSnapshot(string TraceCode, int Remain);
}
