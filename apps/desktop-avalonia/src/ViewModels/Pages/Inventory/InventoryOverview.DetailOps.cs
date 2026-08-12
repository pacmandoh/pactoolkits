using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
using global::Avalonia.Threading;
using PacToolkits.Application.DTOs;
using PacToolkits.Application.TextSearch;
using PacToolkits.Desktop.Avalonia.Services.Infrastructure.Dialogs;
using PacToolkits.Desktop.Avalonia.Services.Infrastructure.Security;
using PacToolkits.Desktop.Avalonia.Services.Workspace.Inventory;
using PacToolkits.Desktop.Avalonia.Services.Workspace.Refresh;
using PacToolkits.Desktop.Avalonia.Ui.Collections;
using PacToolkits.Desktop.Avalonia.Ui.Formatting;
using PacToolkits.Desktop.Avalonia.ViewModels.Support.Catalog;

namespace PacToolkits.Desktop.Avalonia.ViewModels.Pages;

public sealed partial class InventoryOverview : AppPageBase
{
    private bool CanApplyDrugFilter()
        => IsReassignOpen && CanOperateUi();

    private bool CanClearDrugSpecFilter()
        => IsReassignOpen && CanOperateUi() && AutoCompleteFilter.HasDrugText(DrugText);

    [RelayCommand(CanExecute = nameof(CanClearDrugSpecFilter))]
    private void ClearDrugSpecFilter()
    {
        if (SkipTrigger("inventory.reassign.filter.clear", 250))
        {
            return;
        }

        IsDrugSuggestOpen = false;
        DrugText = null;
        ResetDrugSpecSelection();
        RefreshPageCommands();
    }

    [RelayCommand(CanExecute = nameof(CanApplyDrugFilter))]
    private async Task ApplyDrugFilterAsync()
    {
        if (SkipTrigger("inventory.reassign.filter.apply", 350))
        {
            return;
        }

        IsDrugSuggestOpen = false;
        var drug = NormalizeInput(DrugText);
        if (string.IsNullOrWhiteSpace(drug))
        {
            ResetDrugSpecSelection();
            return;
        }

        // 同一上下文重复提交时复用现有选项，避免数量显示闪烁
        if (string.Equals(drug, NormalizeInput(TargetDrugId), StringComparison.OrdinalIgnoreCase) &&
            SpecOptions.Count > 0 &&
            SelectedSpec is not null)
        {
            return;
        }

        using var _ = BeginReassignContextSync();
        try
        {
            using var cts = new CancellationTokenSource(LookupTimeout);
            var (canonical, specs) = await LookupOptions
                .ResolveDrugAndSpecsAsync(_lookup, drug, cts.Token)
                .ConfigureAwait(false);

            if (string.IsNullOrWhiteSpace(canonical))
            {
                var isDeprecated = await _lookup.IsDeprecatedDrugIdAsync(drug, cts.Token).ConfigureAwait(false);
                await RunOnUiAsync(() =>
                {
                    ResetSpecSelection();
                    _toast.Warn(
                        "药品纠错",
                        isDeprecated ? "药品已被弃用" : "药品不存在，请重新输入");
                });
                return;
            }

            await RunOnUiAsync(() =>
            {
                DrugText = canonical;
                TargetDrugId = canonical;
            });
            await SyncSpecsAsync(canonical, specs);
            if (SpecOptions.Count == 0)
            {
                await RunOnUiAsync(() => _toast.Warn("药品纠错", "该药品暂无规格"));
            }
        }
        catch (Exception ex)
        {
            LogError("inventory.reassign_context.load_fail", "Failed to load reassign context", ex);
            await RunOnUiAsync(() => _toast.Error("药品纠错", $"上下文加载失败：{ex.Message}"));
        }
    }

    private void ResetSpecSelection()
    {
        SpecOptions.Clear();
        SelectedSpec = null;
        TargetSpec = null;
        QtyText = null;
        IsSpecSelected = false;
    }

    private void ResetDrugSpecSelection()
    {
        ResetSpecSelection();
        TargetDrugId = null;
        ClearPreviewMessaging();
        PreviewRows.Clear();
        NotifyPreviewStateChanged();
    }

    private async Task SyncSpecsAsync(string canonicalDrug, IReadOnlyList<string> specs)
    {
        await RunOnUiAsync(() =>
        {
            DrugText = canonicalDrug;
            IsDrugSuggestOpen = false;
            TargetDrugId = canonicalDrug;
            OptionCollectionHelper.ReplaceRaw(SpecOptions, specs, StringComparison.Ordinal);
            if (SpecOptions.Count == 0)
            {
                ResetSpecSelection();
                return;
            }

            SelectedSpec = SpecOptions[0];
        });

        await SyncQtyAsync();
    }

    private async Task SyncDrugCatalogAsync()
    {
        if (IsLookupCatalogSuspended())
        {
            await RunOnUiAsync(OnLookupCatalogSuspended);
            return;
        }

        if (_drugCatalog.Count > 0)
        {
            return;
        }

        await RefreshDrugCatalogAsync(forceRefresh: false);
    }

    private async Task RefreshDrugCatalogAsync(bool forceRefresh)
    {
        if (IsLookupCatalogSuspended())
        {
            await RunOnUiAsync(OnLookupCatalogSuspended);
            return;
        }

        try
        {
            var drugs = await DrugCatalogRefresh.LoadAsync(_lookup, forceRefresh).ConfigureAwait(false);
            await RunOnUiAsync(() =>
            {
                _drugCatalog = drugs;
                AutoCompleteFilter.RefreshVisibleOptions(
                    DrugOptions,
                    _drugCatalog,
                    DrugText);

                if (DrugCatalogRefresh.IsMissing(drugs, NormalizeInput(DrugText)))
                {
                    DrugText = null;
                    ResetDrugSpecSelection();
                }
            });
        }
        catch (System.Exception ex)
        {
            LogWarn("inventory.reassign_drug_options.load_fail", "Failed to load reassign drug options", ex);
        }
    }

    private async Task SyncQtyAsync()
    {
        using var _ = BeginReassignContextSync();

        var drug = NormalizeInput(TargetDrugId);
        var spec = NormalizeInput(TargetSpec);
        if (string.IsNullOrWhiteSpace(drug) || string.IsNullOrWhiteSpace(spec))
        {
            await RunOnUiAsync(() => QtyText = null);
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
                TargetDrugId = drug;
                QtyText = qty?.ToString(CultureInfo.InvariantCulture);
            });
        }
        catch
        {
            await RunOnUiAsync(() => QtyText = null);
        }
    }

    private bool CanOperateUi() => !IsUiBusy;

    private bool CanLocalRefresh()
        => CanOperateUi() && !IsStockEditEnabled && CanPage;

    private bool CanUnlock()
        => CanOperateUi()
           && IsDetailMode;

    [RelayCommand(CanExecute = nameof(CanUnlock))]
    private async Task UnlockAsync()
    {
        await RequireUnlockAsync("库存编辑与药品纠错");
    }

    private bool CanLock()
        => CanOperateUi()
           && IsDetailMode
           && IsOpsUnlocked;

    [RelayCommand(CanExecute = nameof(CanLock))]
    private async Task LockAsync()
    {
        if (IsStockEditEnabled)
        {
            await ToggleStockEdit();
        }

        IsReassignOpen = false;
        SetReassignPreviewLive(false);
        ClearPreviewMessaging();
        PreviewRows.Clear();
        NotifyPreviewStateChanged();
        _unlockService.Lock(OpsScope);
        RefreshOpsUnlock();
        StopUnlockTimer();
    }

    private bool CanToggleStockEdit() => CanOperateUi() && IsDetailMode;

    [RelayCommand(CanExecute = nameof(CanToggleStockEdit))]
    private async Task ToggleStockEdit()
    {
        if (SkipTrigger("inventory.stock.edit", 250))
        {
            return;
        }

        if (IsStockEditEnabled)
        {
            if (StockRows.Any(static row => row.HasTraceCodeValidationError))
            {
                _toast.Warn("库存明细编辑", "请先修正追溯码格式错误");
                return;
            }

            CollectStockEdits();
            var savedCount = 0;
            var failedCount = 0;
            var conflictCount = 0;
            string? lastError = null;

            _stockEditSaveInFlight = true;
            CancelStockEditRemoteReconcile();
            try
            {
                if (_pendingStockEdits.Count > 0)
                {
                    var batch = await SaveStockEditsAsync();
                    savedCount = batch.SavedCount;
                    failedCount = batch.FailedCount;
                    conflictCount = batch.Conflicts.Count;
                    lastError = batch.LastError;

                    if (conflictCount > 0)
                    {
                        var choice = await _dialog.Alert(
                            AlertBuilder<bool?>.Create(
                                    "保存冲突",
                                    $"有 {conflictCount.ToString(CultureInfo.InvariantCulture)} 行已被其他终端修改")
                                .SaveConflict("放弃修改", "强制保存"));

                        if (choice is null)
                        {
                            CollectStockEdits();
                            NotifyEditState();
                            return;
                        }

                        if (choice == false)
                        {
                            // 冲突批未落库，放弃即整页重载
                            ClearRemoteStockAttention();
                            IsStockEditEnabled = false;
                            _flushRefreshAfterStockEdit = false;
                            await ReloadAsync();
                            return;
                        }

                        var forced = await ForceSaveConflictsAsync(batch.Conflicts);
                        savedCount += forced.SavedCount;
                        failedCount += forced.FailedCount;
                        conflictCount = forced.Conflicts.Count;
                        lastError = forced.LastError ?? lastError;
                        if (forced.FailedCount > 0 || forced.Conflicts.Count > 0)
                        {
                            await ReloadAsync(preserveEdit: true);
                        }
                    }
                    else if (failedCount > 0)
                    {
                        await ReloadAsync();
                    }
                }
            }
            catch (Exception ex)
            {
                CollectStockEdits();
                LogError("inventory.stock.edit_fail", "Failed to save stock row edits", ex);
                _toast.Error("库存明细编辑", ex.Message);
                NotifyEditState();
                return;
            }
            finally
            {
                _stockEditSaveInFlight = false;
            }

            ClearRemoteStockAttention();
            IsStockEditEnabled = false;
            if (savedCount > 0)
            {
                _flushRefreshAfterStockEdit = false;
                PauseAutoRefresh(PostWriteAutoRefreshPause);
                ReconcilePageLater(TimeSpan.Zero);
            }
            else
            {
                FlushDeferredStockRefreshIfNeeded();
            }

            if (failedCount > 0 || conflictCount > 0)
            {
                var reason = string.IsNullOrWhiteSpace(lastError) ? "请检查输入值与唯一性约束" : lastError!;
                var conflictHint = conflictCount > 0
                    ? $"（含并发冲突 {conflictCount.ToString(CultureInfo.InvariantCulture)} 项）"
                    : string.Empty;
                var hardHint = failedCount > 0
                    ? $"失败 {failedCount.ToString(CultureInfo.InvariantCulture)} 项"
                    : "未全部保存";
                _toast.Error(
                    "库存明细编辑",
                    $"{hardHint}{conflictHint}，成功 {savedCount.ToString(CultureInfo.InvariantCulture)} 项：{reason}");
            }
            else if (savedCount > 0)
            {
                _toast.Success(
                    "库存明细编辑",
                    $"保存成功 {savedCount.ToString(CultureInfo.InvariantCulture)} 项");
            }

            return;
        }

        if (!await RequireUnlockAsync("库存明细编辑"))
        {
            return;
        }

        _pendingStockEdits.Clear();
        SnapshotStockRows();
        EnableStockTraceCodeValidation();
        IsStockEditEnabled = true;
        _lastModeIndex = ModeIndex;
    }

    private async Task<StockRowEditBatchResult> ForceSaveConflictsAsync(
        IReadOnlyList<StockRowEditConflict> conflicts)
    {
        var forceEdits = new List<StockRowEditRequest>(conflicts.Count);
        foreach (var conflict in conflicts)
        {
            if (conflict.Current is null)
            {
                continue;
            }

            forceEdits.Add(new StockRowEditRequest(
                conflict.MatchTraceCode,
                conflict.Current.Version,
                conflict.NewTraceCode,
                conflict.NewRemain));
        }

        if (forceEdits.Count == 0)
        {
            return new StockRowEditBatchResult(
                0,
                conflicts.Count,
                "冲突行缺少服务端快照，无法强制保存",
                Array.Empty<StockRowEditSaved>(),
                conflicts);
        }

        var forced = await _inventory.ApplyStockRowEditsAsync(
            forceEdits,
            CurrentTraceCodeRule(),
            default).ConfigureAwait(true);

        await RunOnUiAsync(() => AcceptSavedStockEditsLocally(forced.Saved));
        return forced;
    }

    private TraceCodeValidationRule CurrentTraceCodeRule()
        => new(
            _traceCodeRule.Current.RequiredLength,
            _traceCodeRule.Current.Pattern);

    private void EnableStockTraceCodeValidation()
    {
        var rule = CurrentTraceCodeRule();
        foreach (var row in StockRows)
        {
            row.EnableTraceCodeEditValidation(rule);
        }
    }

    private void DisableStockTraceCodeValidation()
    {
        foreach (var row in StockRows)
        {
            row.DisableTraceCodeEditValidation();
        }
    }

    private void OnTraceCodeRuleChanged()
    {
        if (!IsStockEditEnabled)
        {
            return;
        }

        var rule = CurrentTraceCodeRule();
        foreach (var row in StockRows)
        {
            row.RefreshTraceCodeEditRule(rule);
        }
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

        _nav.Navigate<ScanCode>();
        await _scanCode.PrefillFromInventoryAsync(d, s);
    }

    [RelayCommand(CanExecute = nameof(CanToggleReassign))]
    private async Task ToggleReassignAsync()
    {
        if (SkipTrigger("inventory.reassign.panel", 250))
        {
            return;
        }

        if (!CanToggleReassign)
        {
            return;
        }

        if (!IsReassignOpen && !await RequireUnlockAsync("药品纠错"))
        {
            return;
        }

        IsReassignOpen = !IsReassignOpen;
        if (IsReassignOpen)
        {
            ScopeIndex = 0;
            ResetReassignForm();
        }
    }

    private void ResetReassignForm()
    {
        CorrectionReason = null;
        DrugText = null;
        SelectedSpec = null;
        TargetDrugId = null;
        TargetSpec = null;
        QtyText = null;
        IsDrugSuggestOpen = false;
        IsSpecSelected = false;
        SetReassignPreviewLive(false);
        ClearPreviewMessaging();
        PreviewRows.Clear();
        NotifyPreviewStateChanged();
    }

    [RelayCommand(CanExecute = nameof(CanTogglePreview))]
    private async Task PreviewReassignAsync()
    {
        if (_reassignPreviewLive)
        {
            ExitReassignPreview();
            return;
        }

        if (!CanPreview)
        {
            return;
        }

        await PreviewReassignAsync(showBusy: false);
    }

    private void QueueReassignPreviewRefresh()
    {
        if (!_reassignPreviewLive || !IsReassignOpen)
        {
            return;
        }

        if (!CanPreview)
        {
            ExitReassignPreview();
            return;
        }

        _previewRefreshCts?.Cancel();
        _previewRefreshCts?.Dispose();
        _previewRefreshCts = new CancellationTokenSource();
        var token = _previewRefreshCts.Token;
        ObserveDetached(RefreshReassignPreviewDebouncedAsync(token), "reassign.preview.detached.fail");
    }

    private async Task RefreshReassignPreviewDebouncedAsync(CancellationToken token)
    {
        try
        {
            await Task.Delay(200, token).ConfigureAwait(false);
            if (token.IsCancellationRequested)
            {
                return;
            }

            var canPreview = false;
            await RunOnUiAsync(() => canPreview = CanPreview);
            if (!canPreview)
            {
                await RunOnUiAsync(ExitReassignPreview);
                return;
            }

            await PreviewReassignAsync(showBusy: false).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task PreviewReassignAsync(bool showBusy)
    {
        var skip = false;
        var isLiveRefresh = false;
        var isSingleScope = false;
        var isFilterScope = false;
        string? targetDrug = null;
        string? targetSpec = null;
        string? qtyText = null;
        string? keyword = null;
        List<StockRowSelection> selectedRows = [];

        await RunOnUiAsync(() =>
        {
            targetDrug = NormalizeInput(TargetDrugId);
            targetSpec = NormalizeInput(TargetSpec);
            if (string.IsNullOrWhiteSpace(targetDrug) || string.IsNullOrWhiteSpace(targetSpec))
            {
                skip = true;
                return;
            }

            isLiveRefresh = !showBusy && _reassignPreviewLive;
            isSingleScope = IsSingleScope;
            isFilterScope = IsFilterScope;
            qtyText = QtyText;
            keyword = Keyword;
            if (isSingleScope)
            {
                selectedRows = GetEffectiveSelectedRows();
            }
        });

        if (skip)
        {
            return;
        }

        if (showBusy)
        {
            await SetPanelBusyAsync(true);
        }

        try
        {
            if (isSingleScope)
            {
                if (selectedRows.Count == 0)
                {
                    if (isLiveRefresh)
                    {
                        await RunOnUiAsync(ExitReassignPreview);
                    }

                    return;
                }

                var targetChanged = !string.Equals(_lastValidatedPreviewTargetDrug, targetDrug, StringComparison.Ordinal)
                                    || !string.Equals(_lastValidatedPreviewTargetSpec, targetSpec, StringComparison.Ordinal);
                if (!isLiveRefresh || targetChanged)
                {
                    using var existsCts = new CancellationTokenSource(LookupTimeout);
                    var targetExists = await _inventory
                        .TargetDrugSpecExistsAsync(targetDrug!, targetSpec!, existsCts.Token)
                        .ConfigureAwait(false);
                    if (!targetExists)
                    {
                        await RunOnUiAsync(() => _toast.Warn("药品纠错预览", "目标药品/规格不存在"));
                        return;
                    }

                    await RunOnUiAsync(() =>
                    {
                        _lastValidatedPreviewTargetDrug = targetDrug;
                        _lastValidatedPreviewTargetSpec = targetSpec;
                    });
                }

                var targetQtyResolved = int.TryParse(NormalizeInput(qtyText), out var parsedQty)
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
                        TargetDrugId: targetDrug!,
                        TargetSpec: targetSpec!,
                        TargetQty: targetQtyResolved,
                        TargetRemain: ResolveTargetRemain(row.Remain, targetQtyResolved),
                        TraceCode: row.TraceCode));
                }

                var selectedCount = selectedRows.Count;
                await RunOnUiAsync(() =>
                {
                    if (!IsReassignOpen || !IsSingleScope || GetEffectiveSelectedRows().Count == 0)
                    {
                        return;
                    }

                    ApplyPreviewRows(singleScopePreviewRows);
                    PreviewStatsText =
                        $"命中 {selectedCount.ToString(CultureInfo.InvariantCulture)} 条 · 可变更 {willChangeCount.ToString(CultureInfo.InvariantCulture)} 条";
                    PreviewNoticeText = null;
                    EnsureReassignPreviewLive();

                    if (willChangeCount <= 0)
                    {
                        _toast.Warn("药品纠错预览", "目标与当前一致，无需纠错");
                    }
                });

                return;
            }

            var kw = NormalizeInput(keyword);
            if (string.IsNullOrWhiteSpace(kw))
            {
                await RunOnUiAsync(() => _toast.Warn("药品纠错预览", "批量纠错需先输入筛选关键字"));
                return;
            }

            var targetQtyResolvedForFilter = int.TryParse(NormalizeInput(qtyText), out var parsedQtyForFilter)
                ? parsedQtyForFilter
                : 0;
            var preview = await _inventory.PreviewReassignByKeywordAsync(
                kw,
                targetDrug!,
                targetSpec!,
                targetQtyResolvedForFilter,
                30,
                default).ConfigureAwait(false);

            var previewRows = new List<StockReassignPreviewRowItem>(preview.Samples.Count);
            foreach (var row in preview.Samples)
            {
                previewRows.Add(new StockReassignPreviewRowItem(
                    CurrentDrugId: row.DrugId,
                    CurrentSpec: row.Spec,
                    CurrentQty: row.Qty,
                    CurrentRemain: row.Remain,
                    TargetDrugId: targetDrug!,
                    TargetSpec: targetSpec!,
                    TargetQty: targetQtyResolvedForFilter,
                    TargetRemain: ResolveTargetRemain(row.Remain, targetQtyResolvedForFilter),
                    TraceCode: row.TraceCode));
            }

            await RunOnUiAsync(() =>
            {
                if (!IsReassignOpen)
                {
                    return;
                }

                if (preview.MatchCount <= 0)
                {
                    _toast.Warn("药品纠错预览", $"关键字「{kw}」未命中记录");
                    return;
                }

                if (!preview.TargetExists)
                {
                    _toast.Warn("药品纠错预览", "目标药品/规格不存在");
                    return;
                }

                ApplyPreviewRows(previewRows);

                if (preview.IsNoopTarget)
                {
                    PreviewStatsText =
                        $"命中 {preview.MatchCount.ToString(CultureInfo.InvariantCulture)} 条 · 可变更 0 条";
                    PreviewNoticeText = null;
                    EnsureReassignPreviewLive();
                    _toast.Warn("药品纠错预览", "目标与当前一致，无需纠错");
                    return;
                }

                PreviewStatsText =
                    $"命中 {preview.MatchCount.ToString(CultureInfo.InvariantCulture)} 条 · 可变更 {preview.WillChangeCount.ToString(CultureInfo.InvariantCulture)} 条";

                PreviewNoticeText = isFilterScope && preview.WillChangeCount > _inventory.LargeBatchReassignConfirmThreshold
                    ? $"超过 {_inventory.LargeBatchReassignConfirmThreshold.ToString(CultureInfo.InvariantCulture)} 条，提交时需二次确认"
                    : null;
                EnsureReassignPreviewLive();
            });
        }
        catch (Exception ex)
        {
            LogError("inventory.reassign.preview_fail", "Failed to preview reassign operation", ex);
            await RunOnUiAsync(() =>
            {
                if (!isLiveRefresh)
                {
                    ResetPreviewContent();
                }

                _toast.Error("药品纠错预览", ex.Message);
            });
        }
        finally
        {
            if (showBusy)
            {
                await SetPanelBusyAsync(false);
                await RunOnUiAsync(RefreshPageCommands);
            }
        }
    }

    [RelayCommand(CanExecute = nameof(CanApply))]
    private async Task ApplyReassignAsync()
    {
        if (!CanApply)
        {
            return;
        }

        var targetDrug = NormalizeInput(TargetDrugId);
        var targetSpec = NormalizeInput(TargetSpec);
        var qtyText = NormalizeInput(QtyText);
        var reason = NormalizeInput(CorrectionReason);
        if (string.IsNullOrWhiteSpace(targetDrug)
            || string.IsNullOrWhiteSpace(targetSpec)
            || !int.TryParse(qtyText, out var targetQty)
            || targetQty <= 0
            || string.IsNullOrWhiteSpace(reason))
        {
            return;
        }

        if (!await RequireUnlockAsync("药品纠错提交"))
        {
            return;
        }

        var targetLabel = DrugLabel.WithQty(targetDrug, targetSpec, targetQty);
        var confirmMessage = IsSingleScope
            ? $"将选中追溯码纠错到：{targetLabel}，是否继续？"
            : $"将“当前筛选关键字”命中的库存批量纠错到：{targetLabel}，是否继续？";
        var ok = await _dialog.ConfirmDestructive("确认纠错", confirmMessage);
        if (!ok)
        {
            return;
        }

        var isSingleScope = IsSingleScope;
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

        await SetPanelBusyAsync(true);
        try
        {
            var operatorName = $"{Environment.UserName}@{Environment.MachineName}";
            const string auditSource = "inventory_desktop";
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
                    auditSource);

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
                    var secondOk = await _dialog.ConfirmDestructive(
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
                        auditSource),
                    default);
            }

            _toast.Success("药品纠错", $"纠错成功 {result.AffectedRows} 条，审计ID={result.AuditId}");

            if (!isSingleScope)
            {
                PageIndex = 1;
                await ReloadAsync();
            }
            else
            {
                var updatedRows = ApplyTargetToDetailRows(
                    singleScope: true,
                    targetDrug,
                    targetSpec,
                    selectedTraceCodes,
                    targetQty,
                    batchKeyword: null);
                if (updatedRows.Count > 0)
                {
                    PauseAutoRefresh(PostWriteAutoRefreshPause);
                    ReconcilePageLater(TimeSpan.Zero);
                }
            }

            ResetReassignForm();
            ClearReassignChecks();
        }
        catch (Exception ex)
        {
            LogError("inventory.reassign.apply_fail", "Failed to apply reassign operation", ex);
            _toast.Error("药品纠错", ex.Message);
        }
        finally
        {
            await SetPanelBusyAsync(false);
        }
    }

    public Task CommitStockCellEditAsync(StockRowItem? row)
    {
        if (row is null || !IsStockEditEnabled || !IsDetailMode)
        {
            return Task.CompletedTask;
        }

        CollectStockEdits();
        NotifyEditState();
        return Task.CompletedTask;
    }

    public void WarnReadonlyStockEdit(string? header)
    {
        if (!IsStockEditEnabled || !IsDetailMode)
        {
            return;
        }

        if (SkipTrigger("inventory.stock.readonly_column_edit", 1200))
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

        if (!await RequireUnlockAsync("库存明细删除"))
        {
            return;
        }

        var selectedRows = GetEffectiveSelectedRows();
        if (selectedRows.Count == 0 && contextRow is not null)
        {
            selectedRows.Add(StockRowSelection.From(contextRow));
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

        var ok = await _dialog.ConfirmDestructive(
            "确认删除",
            $"将删除 {traceCodes.Length.ToString(CultureInfo.InvariantCulture)} 条库存明细记录，操作不可撤销是否继续？");
        if (!ok)
        {
            return;
        }

        try
        {
            var wasEditing = IsStockEditEnabled;
            PauseAutoRefresh(PostWriteAutoRefreshPause);
            await RunOnUiAsync(() => IsDetailBusy = true);
            var affected = await _inventory.DeleteStockByTraceCodesAsync(traceCodes, default);
            await ReloadAsync(preserveEdit: wasEditing);

            await RunOnUiAsync(() =>
            {
                if (wasEditing)
                {
                    IsStockEditEnabled = true;
                    _pendingStockEdits.Clear();
                    SnapshotStockRows();
                    EnableStockTraceCodeValidation();
                }

                _toast.Success("库存明细删除", $"删除成功 {affected.ToString(CultureInfo.InvariantCulture)} 条");
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

    private async Task<StockRowEditBatchResult> SaveStockEditsAsync()
    {
        var edits = new StockRowEditRequest[_pendingStockEdits.Count];
        _pendingStockEdits.CopyTo(edits, 0);
        _pendingStockEdits.Clear();
        if (edits.Length == 0)
        {
            return new StockRowEditBatchResult(
                0, 0, null, Array.Empty<StockRowEditSaved>(), Array.Empty<StockRowEditConflict>());
        }

        var batchResult = await _inventory.ApplyStockRowEditsAsync(
            edits,
            CurrentTraceCodeRule(),
            default).ConfigureAwait(true);

        await RunOnUiAsync(() => AcceptSavedStockEditsLocally(batchResult.Saved));
        return batchResult;
    }

    private void AcceptSavedStockEditsLocally(IReadOnlyList<StockRowEditSaved> savedRows)
    {
        if (savedRows.Count == 0)
        {
            return;
        }

        var byTrace = new Dictionary<string, StockRowItem>(StringComparer.Ordinal);
        foreach (var row in StockRows)
        {
            if (_stockEditSnapshotByRow.TryGetValue(row.RowNo, out var snap)
                && !string.IsNullOrWhiteSpace(snap.TraceCode))
            {
                byTrace.TryAdd(snap.TraceCode, row);
            }

            if (!string.IsNullOrWhiteSpace(row.TraceCode))
            {
                byTrace.TryAdd(row.TraceCode, row);
            }
        }

        foreach (var saved in savedRows)
        {
            if (!byTrace.TryGetValue(saved.MatchTraceCode, out var row))
            {
                continue;
            }

            if (saved.NewTraceCode is not null)
            {
                row.TraceCode = saved.NewTraceCode;
            }

            if (saved.NewRemain is not null)
            {
                row.Remain = saved.NewRemain.Value;
            }

            row.Version = saved.NewVersion;
            _stockEditSnapshotByRow[row.RowNo] = new StockEditSnapshot(
                TraceCode: row.TraceCode,
                Remain: row.Remain,
                Version: saved.NewVersion);
        }
    }

    private void CollectStockEdits()
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
            var remainChanged = snap.Remain != row.Remain;
            if (!traceChanged && !remainChanged)
            {
                continue;
            }

            _pendingStockEdits.Add(new StockRowEditRequest(
                MatchTraceCode: oldTrace,
                ExpectedVersion: snap.Version,
                NewTraceCode: traceChanged ? newTrace : null,
                NewRemain: remainChanged ? row.Remain : null));
        }
    }

    private void SnapshotStockRows()
    {
        ClearRemoteStockAttention();
        _stockEditSnapshotByRow.Clear();
        foreach (var row in StockRows)
        {
            _stockEditSnapshotByRow[row.RowNo] = new StockEditSnapshot(
                TraceCode: row.TraceCode,
                Remain: row.Remain,
                Version: row.Version);
        }
    }

    private async Task<bool> RequireUnlockAsync(string scene)
    {
        var ok = await _unlockService.RequireUnlockAsync(
            OpsScope,
            scene,
            "身份验证",
            UnlockScopes.SharedOpsHint);
        RefreshOpsUnlock();
        return ok;
    }

    private void RefreshOpsUnlock()
    {
        var wasUnlocked = IsOpsUnlocked;
        _unlockService.Refresh(OpsScope);
        var snap = _unlockService.GetSnapshot(OpsScope);

        IsOpsUnlocked = snap.IsUnlocked;
        _opsCooldownUntilUtc = snap.CooldownUntilUtc;

        if (wasUnlocked && !IsOpsUnlocked)
        {
            if (IsStockEditEnabled)
            {
                DiscardStockEdits();
                FlushDeferredStockRefreshIfNeeded();
            }

            IsReassignOpen = false;
            SetReassignPreviewLive(false);
            ClearPreviewMessaging();
            PreviewRows.Clear();
            NotifyPreviewStateChanged();
        }

        if (IsOpsUnlocked || _opsCooldownUntilUtc > DateTimeOffset.UtcNow)
        {
            StartUnlockTimer();
        }
        else
        {
            StopUnlockTimer();
        }
    }

    private void StartUnlockTimer()
    {
        if (!_unlockStatusTimer.IsEnabled)
        {
            _unlockStatusTimer.Start();
        }
    }

    private void StopUnlockTimer()
    {
        if (_unlockStatusTimer.IsEnabled)
        {
            _unlockStatusTimer.Stop();
        }
    }

    private void OnUnlockTimerTick(object? sender, EventArgs e)
        => RefreshOpsUnlock();

    private static async Task SetPanelBusyAsync(bool value, InventoryOverview vm)
    {
        await RunOnUiAsync(() => vm.IsPanelBusy = value);
    }

    private Task SetPanelBusyAsync(bool value)
        => SetPanelBusyAsync(value, this);

    private List<StockRowSelection> GetEffectiveSelectedRows()
        => _selectedStockRowsByTrace.Values.ToList();

    private IReadOnlyList<StockRowItem> ApplyTargetToDetailRows(
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

                ApplyTargetToRow(row, targetDrug, targetSpec, targetQty);
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

                    // 与后端 update 谓词语义对齐
                    if (string.Equals(NormalizeInput(row.DrugId), targetDrug, StringComparison.Ordinal) &&
                        string.Equals(NormalizeInput(row.Spec), targetSpec, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    ApplyTargetToRow(row, targetDrug, targetSpec, targetQty);
                    changedRows.Add(row);
                }
            }
        }

        OnPropertyChanged(nameof(IsStockEmpty));
        return changedRows;
    }

    private static void ApplyTargetToRow(
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

    private void DiscardStockEdits()
    {
        if (!IsStockEditEnabled)
        {
            return;
        }

        CollectStockEdits();
        if (_pendingStockEdits.Count > 0)
        {
            RevertStockRowsFromSnapshot();
        }

        if (_remoteStockBaselineByTrace.Count > 0)
        {
            ApplyRemoteStockBaselinesToRows();
        }

        IsStockEditEnabled = false;
        _pendingStockEdits.Clear();
        ClearRemoteStockAttention();
        NotifyEditState();
    }

    private void ClearRemoteStockAttention()
    {
        _remoteStockBaselineByTrace.Clear();
        _remoteStockMissingAttention = false;
    }

    private void ApplyRemoteStockBaselinesToRows()
    {
        if (_remoteStockBaselineByTrace.Count == 0)
        {
            return;
        }

        foreach (var row in StockRows)
        {
            if (!_stockEditSnapshotByRow.TryGetValue(row.RowNo, out var snap))
            {
                continue;
            }

            if (!_remoteStockBaselineByTrace.TryGetValue(snap.TraceCode, out var remote)
                && !_remoteStockBaselineByTrace.TryGetValue(row.TraceCode, out remote))
            {
                continue;
            }

            ApplyServerStockRow(row, remote, metaOnly: false);
            _stockEditSnapshotByRow[row.RowNo] = new StockEditSnapshot(
                TraceCode: remote.TraceCode,
                Remain: remote.Remain,
                Version: remote.Version);
        }

        _remoteStockBaselineByTrace.Clear();
        NotifyEditState();
    }

    private static void ApplyServerStockRow(StockRowItem row, TracePoolStockRowDto server, bool metaOnly)
    {
        row.DrugId = server.DrugId;
        row.Spec = server.Spec;
        row.Qty = server.Qty;
        row.Status = server.Status;
        row.IsDeprecated = server.IsDeprecated;
        if (metaOnly)
        {
            // remain 仍是本地草稿；IsLow 跟本地 remain（明细口径 remain==0）
            row.IsLow = row.Remain == 0;
            return;
        }

        row.TraceCode = server.TraceCode;
        row.Remain = server.Remain;
        row.Version = server.Version;
        row.IsLow = server.IsLow;
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

    partial void OnModeIndexChanged(int value)
    {
        if (value != _lastModeIndex && IsStockEditEnabled)
        {
            DiscardStockEdits();
        }

        if (value != 0)
        {
            IsReassignOpen = false;
            SetReassignPreviewLive(false);
            ClearPreviewMessaging();
            PreviewRows.Clear();
            NotifyPreviewStateChanged();
        }

        _lastModeIndex = value;

        OnPropertyChanged(nameof(IsDetailMode));
        OnPropertyChanged(nameof(IsAggMode));
        OnPropertyChanged(nameof(IsLowMode));
        OnPropertyChanged(nameof(IsMissingMode));
        OnPropertyChanged(nameof(CanEnableStockEdit));
        OnPropertyChanged(nameof(CanDisableStockEdit));
        OnPropertyChanged(nameof(ShowUnlock));
        OnPropertyChanged(nameof(ShowLock));
        OnPropertyChanged(nameof(ShowOpenReassign));
        OnPropertyChanged(nameof(ShowCloseReassign));
        OnPropertyChanged(nameof(CanToggleReassign));
        OnPropertyChanged(nameof(EditStateText));
        OnPropertyChanged(nameof(ShowEditState));

        if (PageIndex != 1)
            PageIndex = 1;

        RefreshPagingState();
        RefreshOpsUnlock();
        // 不通时勿先亮 Busy：Reload 会跳过/等待，onFinished 可能被后续重载顶掉，Busy 会卡住
        ObserveDetached(ReloadAsync(), "reload.detached.fail");
    }

    partial void OnPageIndexChanged(int value)
    {
        RefreshPagingState();
        RefreshPageCommands();
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
        RefreshPageCommands();
        ObserveDetached(ReloadAsync(), "reload.detached.fail");
    }

    private bool UsesKeywordForBatchReassignOnly => IsReassignOpen && IsFilterScope;

    partial void OnKeywordChanged(string? value)
    {
        OnPropertyChanged(nameof(HasActiveKeyword));

        if (UsesKeywordForBatchReassignOnly)
        {
            if (_reassignPreviewLive)
            {
                QueueReassignPreviewRefresh();
            }
            else
            {
                ClearPreviewMessaging();
                PreviewRows.Clear();
                NotifyPreviewStateChanged();
            }

            RefreshPageCommands();

            if (string.IsNullOrWhiteSpace(value))
            {
                _keywordSearchDebouncer.Cancel();
                DiscardStockEdits();
                PageIndex = 1;
                ObserveDetached(ReloadQuietAsync(), "reload.quiet.detached.fail");
                return;
            }

            _keywordSearchDebouncer.Schedule(async () =>
                await Dispatcher.UIThread.InvokeAsync(async () =>
                {
                    DiscardStockEdits();
                    PageIndex = 1;
                    await ReloadQuietAsync().ConfigureAwait(true);
                }));
            return;
        }

        RefreshPageCommands();

        if (string.IsNullOrWhiteSpace(value))
        {
            _keywordSearchDebouncer.Cancel();
            DiscardStockEdits();
            PageIndex = 1;
            ObserveDetached(ReloadAsync(), "reload.detached.fail");
            return;
        }

        _keywordSearchDebouncer.Schedule(async () =>
            await Dispatcher.UIThread.InvokeAsync(async () =>
            {
                DiscardStockEdits();
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
        if (SkipTrigger(milliseconds: 350))
        {
            return;
        }

        _keywordSearchDebouncer.Cancel();

        if (UsesKeywordForBatchReassignOnly)
        {
            DiscardStockEdits();
            PageIndex = 1;
            await ReloadQuietAsync();
            if (_reassignPreviewLive)
            {
                await PreviewReassignAsync(showBusy: false);
            }

            return;
        }

        DiscardStockEdits();

        PageIndex = 1;
        await ReloadAsync();
    }

    [RelayCommand]
    private Task ClearSearchAsync()
    {
        if (SkipTrigger(milliseconds: 350))
        {
            return Task.CompletedTask;
        }

        Keyword = null;
        return Task.CompletedTask;
    }

    [RelayCommand(CanExecute = nameof(CanGoFirstPage))]
    private async Task FirstPageAsync()
    {
        if (SkipTrigger("inventory.page.first", 180))
        {
            return;
        }

        if (!CanGoFirstPage())
        {
            return;
        }

        DiscardStockEdits();

        PageIndex = 1;
        await ReloadAsync();
    }

    [RelayCommand(CanExecute = nameof(CanGoPrevPage))]
    private async Task PrevPageAsync()
    {
        if (SkipTrigger("inventory.page.prev", 180))
        {
            return;
        }

        if (!CanGoPrevPage())
        {
            return;
        }

        DiscardStockEdits();

        PageIndex--;
        await ReloadAsync();
    }

    [RelayCommand(CanExecute = nameof(CanGoNextPage))]
    private async Task NextPageAsync()
    {
        if (SkipTrigger("inventory.page.next", 180))
        {
            return;
        }

        if (!CanGoNextPage())
        {
            return;
        }

        DiscardStockEdits();

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
        if (SkipTrigger("inventory.page.last", 180))
        {
            return;
        }

        if (!CanGoLastPage())
        {
            return;
        }

        DiscardStockEdits();

        PageIndex = TotalPages;
        await ReloadAsync();
    }

    private void PauseAutoRefresh(TimeSpan duration)
    {
        var until = DateTimeOffset.UtcNow + duration;
        if (until > _suppressAutoRefreshUntilUtc)
        {
            _suppressAutoRefreshUntilUtc = until;
        }
    }

    public bool DeferRefreshTopic(string? topic)
    {
        if (!WorkspaceTopicRefresh.DeferInventory(topic))
        {
            return false;
        }

        // 编辑中推迟 LISTEN 立刻刷新；记一笔，退出编辑后再补刷
        if (IsStockEditEnabled)
        {
            _flushRefreshAfterStockEdit = true;
            return true;
        }

        return DateTimeOffset.UtcNow < _suppressAutoRefreshUntilUtc;
    }

    private void FlushDeferredStockRefreshIfNeeded()
    {
        if (!_flushRefreshAfterStockEdit)
        {
            return;
        }

        _flushRefreshAfterStockEdit = false;
        ObserveDetached(ReloadAsync(), "reload.deferred_after_edit.fail");
    }

    /// <summary>
    /// 编辑中收到 inventory/trace_*：静默拉当前页，标他端变更，不 Discard、不关编辑
    /// </summary>
    public Task ReconcileRemoteDuringStockEditAsync()
    {
        if (!IsStockEditEnabled || !IsDetailMode || _stockEditSaveInFlight)
        {
            return Task.CompletedTask;
        }

        CancelStockEditRemoteReconcile();
        _stockEditRemoteCts = new CancellationTokenSource();
        var page = PageIndex;
        var keyword = NormalizeInput(Keyword);
        var epoch = Volatile.Read(ref _detailStockEpoch);
        return RunStockEditRemoteReconcileAsync(page, keyword, epoch, _stockEditRemoteCts.Token);
    }

    private async Task RunStockEditRemoteReconcileAsync(
        int page,
        string? keyword,
        int epoch,
        CancellationToken ct)
    {
        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(LookupTimeout);
            var pageResult = await _inventory
                .GetStockPageAsync(keyword, page, PageSize, timeoutCts.Token)
                .ConfigureAwait(false);

            await RunOnUiAsync(() =>
            {
                if (!InventorySilentReconcilePolicy.CanApply(
                        epoch,
                        Volatile.Read(ref _detailStockEpoch),
                        IsPageReloadActive,
                        IsStockEditEnabled,
                        IsDetailMode,
                        page,
                        PageIndex,
                        keyword,
                        NormalizeInput(Keyword),
                        ct.IsCancellationRequested,
                        requireStockEditEnabled: true))
                {
                    return;
                }

                ApplyStockEditRemoteReconcile(pageResult.Rows);
            }, DispatcherPriority.Background);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            LogWarn(
                "inventory.stock_edit.remote_reconcile_fail",
                "Failed to reconcile stock page during edit after remote change",
                ex);
        }
    }

    private void ApplyStockEditRemoteReconcile(IReadOnlyList<TracePoolStockRowDto> serverRows)
    {
        _remoteStockMissingAttention = false;
        var serverByTrace = new Dictionary<string, TracePoolStockRowDto>(serverRows.Count, StringComparer.Ordinal);
        foreach (var row in serverRows)
        {
            if (!string.IsNullOrWhiteSpace(row.TraceCode))
            {
                serverByTrace[row.TraceCode] = row;
            }
        }

        var toastRemoteChanged = false;
        var toastRemoteMissing = false;

        foreach (var local in StockRows)
        {
            if (!_stockEditSnapshotByRow.TryGetValue(local.RowNo, out var snap))
            {
                continue;
            }

            TracePoolStockRowDto? server = null;
            if (serverByTrace.TryGetValue(snap.TraceCode, out var bySnap))
            {
                server = bySnap;
            }
            else if (serverByTrace.TryGetValue(local.TraceCode, out var byCurrent))
            {
                server = byCurrent;
            }

            var action = InventoryStockEditRemotePolicy.Decide(
                snap.Version,
                snap.TraceCode,
                snap.Remain,
                local.TraceCode,
                local.Remain,
                server?.Version);

            switch (action)
            {
                case InventoryStockEditRemotePolicy.Action.None:
                    break;

                case InventoryStockEditRemotePolicy.Action.MissingSilent:
                    _remoteStockBaselineByTrace.Remove(snap.TraceCode);
                    break;

                case InventoryStockEditRemotePolicy.Action.MissingNotify:
                    _remoteStockBaselineByTrace.Remove(snap.TraceCode);
                    _remoteStockMissingAttention = true;
                    toastRemoteMissing = true;
                    break;

                case InventoryStockEditRemotePolicy.Action.KeepDraftBaseline when server is not null:
                    _remoteStockBaselineByTrace[snap.TraceCode] = server;
                    ApplyServerStockRow(local, server, metaOnly: true);
                    toastRemoteChanged = true;
                    break;

                case InventoryStockEditRemotePolicy.Action.SyncAll when server is not null:
                    _remoteStockBaselineByTrace.Remove(snap.TraceCode);
                    if (!string.Equals(local.TraceCode, snap.TraceCode, StringComparison.Ordinal))
                    {
                        _remoteStockBaselineByTrace.Remove(local.TraceCode);
                    }

                    ApplyServerStockRow(local, server, metaOnly: false);
                    _stockEditSnapshotByRow[local.RowNo] = new StockEditSnapshot(
                        TraceCode: server.TraceCode,
                        Remain: server.Remain,
                        Version: server.Version);
                    break;
            }
        }

        if (toastRemoteMissing)
        {
            _toast.Warn("库存明细编辑", "正在编辑的行已在其它终端删除，保存时可能失败");
        }
        else if (toastRemoteChanged)
        {
            _toast.Warn("库存明细编辑", "正在编辑的行已在其它终端修改，保存时可能冲突");
        }

        NotifyEditState();
    }

    private void ReconcilePageLater(TimeSpan delay)
    {
        _silentReconcileCts?.Cancel();
        _silentReconcileCts?.Dispose();
        _silentReconcileCts = new CancellationTokenSource();

        var page = PageIndex;
        var keyword = NormalizeInput(Keyword);
        var epoch = Volatile.Read(ref _detailStockEpoch);
        ObserveDetached(
            RunSilentCurrentPageReconcileAsync(delay, page, keyword, epoch, _silentReconcileCts.Token),
            "reconcile.detached.fail");
    }

    private async Task RunSilentCurrentPageReconcileAsync(
        TimeSpan delay,
        int page,
        string? keyword,
        int epoch,
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
                if (!InventorySilentReconcilePolicy.CanApply(
                        epoch,
                        Volatile.Read(ref _detailStockEpoch),
                        IsPageReloadActive,
                        IsStockEditEnabled,
                        IsDetailMode,
                        page,
                        PageIndex,
                        keyword,
                        NormalizeInput(Keyword),
                        ct.IsCancellationRequested))
                {
                    return;
                }

                // 静默对账：追溯码按索引仍对齐时原地改字段；条数或行序变了则重建，避免写到错行
                if (HasSameTraceCodeOrder(StockRows, rebuiltRows))
                {
                    for (var i = 0; i < StockRows.Count; i++)
                    {
                        var dst = StockRows[i];
                        var src = pageResult.Rows[i];
                        dst.RowNo = ((page - 1) * PageSize) + i + 1;
                        dst.DrugId = src.DrugId;
                        dst.Spec = src.Spec;
                        dst.TraceCode = src.TraceCode;
                        dst.Qty = src.Qty;
                        dst.Remain = src.Remain;
                        dst.Status = src.Status;
                        dst.Version = src.Version;
                        dst.IsLow = src.IsLow;
                        dst.IsDeprecated = src.IsDeprecated;
                    }
                }
                else
                {
                    ClearReassignChecks();
                    StockRows.ReplaceAll(rebuiltRows);
                    OnPropertyChanged(nameof(IsStockEmpty));
                }

                TotalCount = pageResult.TotalCount;
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

    private static bool HasSameTraceCodeOrder(
        IReadOnlyList<StockRowItem> current,
        IReadOnlyList<StockRowItem> server)
    {
        if (current.Count != server.Count)
        {
            return false;
        }

        for (var i = 0; i < current.Count; i++)
        {
            if (!string.Equals(current[i].TraceCode, server[i].TraceCode, StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    private static int ResolveTargetRemain(int currentRemain, int targetQty)
        => Math.Min(currentRemain, targetQty);

}
