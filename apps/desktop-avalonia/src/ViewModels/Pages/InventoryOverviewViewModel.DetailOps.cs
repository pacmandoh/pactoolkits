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
using PacToolkits.Desktop.Avalonia.Common;

namespace PacToolkits.Desktop.Avalonia.ViewModels.Pages;

public sealed partial class InventoryOverviewViewModel : AppPageBase
{
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

    private async Task LoadReassignDrugsAsync()
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
                AutoCompleteFilter.RefreshVisibleOptions(
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

        await LoadReassignDrugsAsync();

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

    private bool CanRequestUnlock()
        => CanOperateUi()
           && IsDetailMode;

    [RelayCommand(CanExecute = nameof(CanRequestUnlock))]
    private async Task RequestUnlockAsync()
    {
        await RequireUnlockAsync("库存编辑与药品纠错");
    }

    private bool CanLockOperations()
        => CanOperateUi()
           && IsDetailMode
           && IsOperationUnlocked;

    [RelayCommand(CanExecute = nameof(CanLockOperations))]
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
        StopUnlockTimer();
    }

    private bool CanToggleStockEditMode() => CanOperateUi() && IsDetailMode;

    [RelayCommand(CanExecute = nameof(CanToggleStockEditMode))]
    private async Task ToggleStockEditMode()
    {
        if (SkipTrigger("inventory.stock.edit", 250))
        {
            return;
        }

        if (IsStockEditEnabled)
        {
            CollectStockEdits();
            var (savedCount, failedCount, lastError) = (0, 0, (string?)null);
            if (_pendingStockEdits.Count > 0)
            {
                (savedCount, failedCount, lastError) = await SaveStockEditsAsync();
            }

            if (savedCount > 0)
            {
                // Keep current viewport/scroll stable after row-level edits:
                // defer watermark-driven full reload, then reconcile silently.
                PauseAutoRefresh(TimeSpan.FromSeconds(7));
                ReconcilePageLater(TimeSpan.FromSeconds(5));
            }

            IsStockEditEnabled = false;
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

        if (!await RequireUnlockAsync("库存明细编辑"))
        {
            return;
        }

        _pendingStockEdits.Clear();
        SnapshotStockRows();
        IsStockEditEnabled = true;
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
        if (SkipTrigger("inventory.reassign.panel", 250))
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
        if (!IsReassignPanelVisible && !await RequireUnlockAsync("药品纠错"))
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

        if (!await RequireUnlockAsync("药品纠错提交"))
        {
            return;
        }

        var confirmMessage = IsSingleReassignScope
            ? $"将选中追溯码纠错到 {targetDrug}/{targetSpec}，是否继续？"
            : $"将“当前筛选关键字”命中的库存批量纠错到 {targetDrug}/{targetSpec}，是否继续？";
        var ok = await _dialog.ConfirmDestructive("确认纠错", confirmMessage);
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
            PauseAutoRefresh(TimeSpan.FromSeconds(7));
            ReconcilePageLater(TimeSpan.FromSeconds(5));
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

        CollectStockEdits();
        RefreshPendingChanges();

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
            PauseAutoRefresh(TimeSpan.FromSeconds(8));
            await RunOnUiAsync(() => IsDetailBusy = true);
            var affected = await _inventory.DeleteStockByTraceCodesAsync(traceCodes, default);
            await ReloadAsync(preserveEditSession: wasEditing);

            await RunOnUiAsync(() =>
            {
                if (wasEditing)
                {
                    IsStockEditEnabled = true;
                    _pendingStockEdits.Clear();
                    SnapshotStockRows();
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

    private async Task<(int SavedCount, int FailedCount, string? LastError)> SaveStockEditsAsync()
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

    private void SnapshotStockRows()
    {
        _stockEditSnapshotByRow.Clear();
        foreach (var row in StockRows)
        {
            _stockEditSnapshotByRow[row.RowNo] = new StockEditSnapshot(
                TraceCode: row.TraceCode,
                Remain: row.Remain);
        }
    }

    private bool HasStockEdits
        => _pendingStockEdits.Count > 0;

    private int StockEditCount
        => _pendingStockEdits.Count;

    private async Task<bool> RequireUnlockAsync(string scene)
    {
        var hint = "敏感操作提示：验证仅在本地进行，不会上传密码\n请输入数据库密码以解锁库存敏感操作";
        var ok = await _unlockService.RequireUnlockAsync(
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
        _operationUnlockCooldownUntilUtc = snap.CooldownUntilUtc;

        if (wasUnlocked && !IsOperationUnlocked)
        {
            if (IsStockEditEnabled)
            {
                DiscardStockEdits();
            }

            IsReassignPanelVisible = false;
            ReassignPreviewRows.Clear();
            OnPropertyChanged(nameof(IsReassignPreviewEmpty));
        }

        if (IsOperationUnlocked || _operationUnlockCooldownUntilUtc > DateTimeOffset.UtcNow)
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

    private void DiscardStockEdits()
    {
        if (!IsStockEditEnabled)
        {
            return;
        }

        CollectStockEdits();
        if (_pendingStockEdits.Count <= 0)
        {
            IsStockEditEnabled = false;
            return;
        }

        RevertStockRowsFromSnapshot();
        IsStockEditEnabled = false;
        _pendingStockEdits.Clear();
        RefreshPendingChanges();
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
        if (value != _lastModeIndex && IsStockEditEnabled && HasStockEdits)
            DiscardStockEdits();

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
        OnPropertyChanged(nameof(ShowRequestUnlock));
        OnPropertyChanged(nameof(ShowLockOperations));
        OnPropertyChanged(nameof(UnlockStatusText));
        OnPropertyChanged(nameof(CanToggleReassignPanel));
        OnPropertyChanged(nameof(EditSessionStateText));
        OnPropertyChanged(nameof(ShowEditSessionState));

        if (PageIndex != 1)
            PageIndex = 1;

        RefreshPagingState();
        RefreshUnlockState();
        SetModeBusy(value, true);
        _ = ReloadAsync();
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
        _ = ReloadAsync();
    }

    partial void OnKeywordChanged(string? value)
    {
        OnPropertyChanged(nameof(HasActiveKeyword));

        if (IsFilterReassignScope)
        {
            ReassignPreviewText = null;
            ReassignPreviewRows.Clear();
            OnPropertyChanged(nameof(IsReassignPreviewEmpty));
            RefreshPageCommands();
            return;
        }

        RefreshPageCommands();

        if (string.IsNullOrWhiteSpace(value))
        {
            _keywordSearchDebouncer.Cancel();
            DiscardStockEdits();
            PageIndex = 1;
            _ = ReloadAsync();
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

        RefreshPageCommands();
    }

    public bool DeferExternalRefreshForTopic(string? topic)
    {
        if (DateTimeOffset.UtcNow >= _suppressAutoRefreshUntilUtc)
        {
            return false;
        }

        var key = (topic ?? string.Empty).Trim().ToLowerInvariant();
        return key is "inventory" or "trace_pool" or "trace_txn" or "trace_txn_item" or "";
    }

    private void ReconcilePageLater(TimeSpan delay)
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

}
