using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
using global::Avalonia.Threading;
using PacToolkits.Application.Abstractions;
using PacToolkits.Application.DTOs;
using PacToolkits.Application.TextSearch;
using PacToolkits.Desktop.Avalonia.Common;
using PacToolkits.Desktop.Avalonia.Services.Infrastructure;

namespace PacToolkits.Desktop.Avalonia.ViewModels.Pages;

public sealed partial class MsfxLink : AppPageBase
{
    private bool CanRunAutoOnce()
        => !IsAutoBusy && !IsManualMsfxWriteActive;

    private bool CanRefreshAutoBoard()
        => !IsAutoBoardBusy && !IsAutoBusy;

    [RelayCommand(CanExecute = nameof(CanRunAutoOnce))]
    private async Task RunAutoOnceAsync()
    {
        await QueueAutoOnceAsync(showProgressPanel: true).ConfigureAwait(false);
    }

    private async Task QueueAutoOnceAsync(bool showProgressPanel)
    {
        ShowAutoProgressPanel = showProgressPanel;
        await RunLocalReloadAsync(
            setBusy: v => IsAutoBusy = v,
            action: RunAutoOnceWorkAsync);
    }

    private async Task RunAutoOnceWorkAsync(CancellationToken ct)
    {
        try
        {
            var options = BuildMsfxOptions();
            LogInfo("msfx.auto.run.start", "MSFX auto run started");

            var result = await _autoRun.RunAsync(
                new MsfxAutoRunRequest(options),
                new AutoRunObserver(this),
                ct).ConfigureAwait(false);

            SetAutoProgress(100, BuildAutoRunStatus(result));
            LogInfo("msfx.auto.run.finish", "MSFX auto run finished", new
            {
                result.BatchId,
                result.ApiRows,
                result.InboundRows,
                result.Bills,
                result.Codes,
                result.RetryQueued,
                result.RetrySucceeded,
                result.RetryFailed,
                result.WatchQueued,
                result.WatchResolved,
                result.WatchDeferred,
                result.MapProcessed,
                result.MapMatched,
                result.MapReview,
                result.CreatedTasks,
                result.TaskedCodes,
                result.ElapsedMs
            });
            _toast.Success("码上放心自动化", $"完成：下发任务 {result.CreatedTasks}，下发码 {result.TaskedCodes}");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            SetAutoProgress(100, $"自动化拉取异常：{ex.Message}");
            AddAutoLog("异常", ex.Message, TraceEntryState.Failed);
            LogError("msfx.auto.run.fail", "MSFX auto run failed", ex);
            if (CanToastError(ex))
            {
                _toast.Error("自动化监控", ex.Message);
            }
        }
        finally
        {
            await RefreshAutoBoardAsync().ConfigureAwait(false);
        }
    }

    private sealed class AutoRunObserver(MsfxLink owner) : IMsfxAutoRunObserver
    {
        public bool IsManualWriteActive => owner.IsManualMsfxWriteActive;

        public void Report(MsfxAutoRunUpdate update)
        {
            if (update.Progress is { } progress && update.Status is { } status)
            {
                owner.SetAutoProgress(progress, status);
            }

            if (update.Stage is { } stage
                && update.Message is { } message
                && update.State is { } state)
            {
                owner.AddAutoLog(stage, message, state);
            }
        }

        public Task DataChangedAsync(MsfxAutoRunData data, CancellationToken ct)
            => data switch
            {
                MsfxAutoRunData.PullAudit => owner.RefreshPullPanelAsync(ct),
                MsfxAutoRunData.MappingQueue => owner.RefreshMapPanelAsync(ct),
                MsfxAutoRunData.TaskQueue => owner.RefreshTaskPanelAsync(ct),
                _ => Task.CompletedTask
            };

        public Task<bool> AuthorizeMappingAsync(long batchId, CancellationToken ct)
            => owner.RequireUnlockAsync(
                SensitiveOpKind.MsfxMappingApply,
                "自动映射",
                batchId > 0 ? $"batch:{batchId}" : "auto-run",
                "auto mapping apply before task build",
                ct);
    }

    private static string BuildAutoRunStatus(MsfxAutoRunResult result)
        => $"自动化拉取完成：API {result.ApiRows}，已入库 {result.InboundRows}，单据 {result.Bills}，码 {result.Codes}，" +
           $"重试成功 {result.RetrySucceeded}，重试失败 {result.RetryFailed}，重试入队 {result.RetryQueued}，" +
           $"待确认入池 {result.WatchQueued}，补偿成功 {result.WatchResolved}，补偿延后 {result.WatchDeferred}，" +
           $"新增任务 {result.CreatedTasks}";

    [RelayCommand]
    private void ClearAutoLogs()
    {
        AutoLogs.Clear();
        _lastAutoLogSignature = null;
        AutoLogPage = 1;
        ApplyAutoLogPage();
        AddAutoLog("日志", "日志已清空", TraceEntryState.Info);
    }

    [RelayCommand]
    private void ToggleAutoPanelExpand(string panelKey)
    {
        var key = (panelKey ?? string.Empty).Trim().ToUpperInvariant();
        if (key.Length == 0)
        {
            return;
        }

        AutoExpandedPanel = string.Equals(AutoExpandedPanel, key, StringComparison.OrdinalIgnoreCase)
            ? string.Empty
            : key;
    }

    [RelayCommand]
    private async Task FirstMapQueuePageAsync()
    {
        if (MapQueuePage <= 1 && !MapQueueHasNewer)
        {
            return;
        }

        ResetMapQueueCursor();
        await RunMapPanelQueryAsync(ct => RefreshMapQueueAsync(ct, olderPage: null)).ConfigureAwait(false);
    }

    [RelayCommand]
    private async Task PrevMapQueuePageAsync()
    {
        if (!MapQueueHasNewer || IsMapPanelBusy)
        {
            return;
        }

        await RunMapPanelQueryAsync(ct => RefreshMapQueueAsync(ct, olderPage: false)).ConfigureAwait(false);
    }

    [RelayCommand]
    private Task FirstPullBatchPageAsync()
    {
        if (!HasPullBatchPrevPage)
        {
            return Task.CompletedTask;
        }

        PullBatchPage = 1;
        return Task.CompletedTask;
    }

    [RelayCommand]
    private Task PrevPullBatchPageAsync()
    {
        if (!HasPullBatchPrevPage)
        {
            return Task.CompletedTask;
        }

        PullBatchPage -= 1;
        return Task.CompletedTask;
    }

    [RelayCommand]
    private Task NextPullBatchPageAsync()
    {
        if (!HasPullBatchNextPage)
        {
            return Task.CompletedTask;
        }

        PullBatchPage += 1;
        return Task.CompletedTask;
    }

    [RelayCommand]
    private Task LastPullBatchPageAsync()
    {
        if (!HasPullBatchNextPage)
        {
            return Task.CompletedTask;
        }

        PullBatchPage = PullBatchTotalPages;
        return Task.CompletedTask;
    }

    [RelayCommand]
    private async Task NextMapQueuePageAsync()
    {
        if (!MapQueueHasOlder || IsMapPanelBusy)
        {
            return;
        }

        await RunMapPanelQueryAsync(ct => RefreshMapQueueAsync(ct, olderPage: true)).ConfigureAwait(false);
    }

    [RelayCommand]
    private async Task LastMapQueuePageAsync()
    {
        if (!MapQueueHasOlder || IsMapPanelBusy)
        {
            return;
        }

        await RunMapPanelQueryAsync(ct => RefreshMapQueueAsync(ct, olderPage: null, seekLastPage: true)).ConfigureAwait(false);
    }

    [RelayCommand]
    private async Task SearchMapQueueAsync()
    {
        _mapQueueSearchDebouncer.Cancel();
        ResetMapQueueCursor();
        await RefreshMapQueueLatestAsync().ConfigureAwait(false);
    }

    [RelayCommand]
    private async Task ResetMapQueueFiltersAsync()
    {
        if (MapQueuePageSize == "120" &&
            string.Equals(MapQueueMapStatusFilter, "ALL", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(MapQueueCodeStatusFilter, "ALL", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(MapQueueSearchScope, "全部字段", StringComparison.OrdinalIgnoreCase) &&
            string.IsNullOrWhiteSpace(MapQueueKeyword))
        {
            ResetMapQueueCursor();
            await RefreshMapQueueLatestAsync().ConfigureAwait(false);
            return;
        }

        _isResettingMapQueueFilters = true;
        try
        {
            _mapQueueSearchDebouncer.Cancel();
            MapQueuePageSize = "120";
            MapQueueMapStatusFilter = "ALL";
            MapQueueCodeStatusFilter = "ALL";
            MapQueueSearchScope = "全部字段";
            MapQueueKeyword = string.Empty;
            ResetMapQueueCursor();
        }
        finally
        {
            _isResettingMapQueueFilters = false;
        }

        OnPropertyChanged(nameof(MapQueueEffectivePageSize));
        OnPropertyChanged(nameof(MapQueueTotalPages));
        await RefreshMapQueueLatestAsync().ConfigureAwait(false);
    }

    private async Task<bool> RequireUnlockAsync(
        SensitiveOpKind kind,
        string scene,
        string targetId,
        string reason,
        CancellationToken ct = default)
    {
        var operatorName = Environment.UserName;
        var ok = await _unlockService.RequestUnlockAsync(new SensitiveOpRequest(
            Kind: kind,
            ScopeKey: UnlockScopes.SharedOps,
            Scene: scene,
            PromptTitle: scene,
            PromptHint: $"{scene} 属于高风险 MSFX 操作\n目标：{targetId}\n原因：{reason}\n请输入当前数据库密码以解锁",
            OperatorName: operatorName,
            TargetId: targetId,
            Reason: reason,
            NotifySuccess: false), ct).ConfigureAwait(false);

        var audit = new
        {
            operationKind = kind.ToString(),
            operatorName,
            targetId,
            reason,
            timestamp = DateTimeOffset.UtcNow,
            unlocked = ok
        };

        if (ok)
        {
            LogWarn("msfx.sensitive.unlock.granted", "MSFX sensitive operation unlocked", null, audit);
            AddAutoLog("敏感操作", $"{scene} 已解锁：{targetId}", TraceEntryState.Warning);
        }
        else
        {
            LogWarn("msfx.sensitive.unlock.cancelled", "MSFX sensitive operation cancelled before database write", null, audit);
            AddAutoLog("敏感操作", $"{scene} 已取消：{targetId}", TraceEntryState.Info);
        }

        return ok;
    }

    private void EnterManualMsfxWrite()
    {
        Interlocked.Increment(ref _manualMsfxWriteDepth);
        RefreshCommands(RunAutoOnceCommand);
    }

    private void ExitManualMsfxWrite()
    {
        Interlocked.Decrement(ref _manualMsfxWriteDepth);
        RefreshCommands(RunAutoOnceCommand);
    }

    private async Task ReopenSelectedTaskAsync()
    {
        var selectedRows = SelectedAutoTaskQueueRowsSnapshot
            .Where(x => string.Equals(x.Status, "SUCCESS", StringComparison.OrdinalIgnoreCase)
                     || string.Equals(x.Status, "DISCARDED", StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (selectedRows.Count == 0)
        {
            _toast.Warn("任务重开", "请先选择至少一条 SUCCESS 或 DISCARDED 任务");
            return;
        }

        if (IsAutoBoardBusy)
        {
            return;
        }

        var ok = await _dialog.Confirm(
            "重开注入任务",
            $"将重开选中的 {selectedRows.Count} 条 SUCCESS / DISCARDED 任务，并重置为可执行队列，确认继续？").ConfigureAwait(false);
        if (!ok)
        {
            return;
        }

        if (!await RequireUnlockAsync(
                SensitiveOpKind.MsfxReopen,
                "任务重开",
                string.Join(",", selectedRows.Select(x => x.TaskId)),
                "manual reopen from desktop").ConfigureAwait(false))
        {
            return;
        }

        try
        {
            IsTaskPanelBusy = true;
            var opName = Environment.UserName;
            var successCount = 0;
            var failedCount = 0;

            foreach (var taskRow in selectedRows)
            {
                try
                {
                    var result = await _syncService.ReopenInjectAsync(
                        taskRow.TaskId,
                        opName,
                        "manual reopen from desktop",
                        CancellationToken.None).ConfigureAwait(false);
                    successCount += 1;
                    AddAutoLog("任务重开", $"任务 #{result.TaskId} 已重开，状态={result.Status}，总码数={result.TotalCodes}", TraceEntryState.Warning);
                    LogWarn("msfx.task.reopen.success", "MSFX inject task reopened", null, new
                    {
                        result.TaskId,
                        result.Status,
                        result.TotalCodes,
                        operatorName = opName
                    });
                }
                catch (Exception ex)
                {
                    failedCount += 1;
                    AddAutoLog("任务重开", $"任务 #{taskRow.TaskId} 重开失败：{ex.Message}", TraceEntryState.Failed);
                    LogError("msfx.task.reopen.fail", "MSFX inject task reopen failed", ex, new
                    {
                        taskRow.TaskId,
                        operatorName = opName
                    });
                }
            }

            if (failedCount == 0)
            {
                _toast.Success("任务重开", $"成功 {successCount} 条，失败 {failedCount} 条");
            }
            else
            {
                _toast.Warn("任务重开", $"成功 {successCount} 条，失败 {failedCount} 条");
            }

            await RefreshTaskPanelAsync(CancellationToken.None).ConfigureAwait(false);
        }
        finally
        {
            await RunOnUiAsync(() =>
            {
                IsTaskPanelBusy = false;
                TaskQueueBatchMode = TaskQueueBatchActionMode.None;
            });
        }
    }

    private async Task DiscardSelectedTaskAsync()
    {
        var selectedRows = SelectedAutoTaskQueueRowsSnapshot
            .Where(x => string.Equals(x.Status, "NEW", StringComparison.OrdinalIgnoreCase)
                     || string.Equals(x.Status, "FAILED", StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (selectedRows.Count == 0)
        {
            _toast.Warn("任务弃用", "请先选择至少一条 NEW 或 FAILED 任务");
            return;
        }

        if (IsAutoBoardBusy)
        {
            return;
        }

        var ok = await _dialog.ConfirmDestructive(
            "弃用注入任务",
            $"将弃用选中的 {selectedRows.Count} 条任务，弃用后 Agent 将不再执行这些任务，确认继续？").ConfigureAwait(false);
        if (!ok)
        {
            return;
        }

        if (!await RequireUnlockAsync(
                SensitiveOpKind.MsfxDiscard,
                "任务弃用",
                string.Join(",", selectedRows.Select(x => x.TaskId)),
                "manual discard from desktop").ConfigureAwait(false))
        {
            return;
        }

        try
        {
            IsTaskPanelBusy = true;
            var opName = Environment.UserName;
            var successCount = 0;
            var failedCount = 0;

            foreach (var taskRow in selectedRows)
            {
                try
                {
                    var result = await _syncService.DiscardInjectAsync(
                        taskRow.TaskId,
                        opName,
                        "manual discard from desktop",
                        CancellationToken.None).ConfigureAwait(false);
                    successCount += 1;
                    AddAutoLog("任务弃用", $"任务 #{result.TaskId} 已弃用，状态={result.Status}，总码数={result.TotalCodes}", TraceEntryState.Info);
                    LogWarn("msfx.task.discard.success", "MSFX inject task discarded", null, new
                    {
                        result.TaskId,
                        result.Status,
                        result.TotalCodes,
                        operatorName = opName
                    });
                }
                catch (Exception ex)
                {
                    failedCount += 1;
                    AddAutoLog("任务弃用", $"任务 #{taskRow.TaskId} 弃用失败：{ex.Message}", TraceEntryState.Failed);
                    LogError("msfx.task.discard.fail", "MSFX inject task discard failed", ex, new
                    {
                        taskRow.TaskId,
                        operatorName = opName
                    });
                }
            }

            if (failedCount == 0)
            {
                _toast.Success("任务弃用", $"成功 {successCount} 条，失败 {failedCount} 条");
            }
            else
            {
                _toast.Warn("任务弃用", $"成功 {successCount} 条，失败 {failedCount} 条");
            }

            await RefreshTaskPanelAsync(CancellationToken.None).ConfigureAwait(false);
        }
        finally
        {
            await RunOnUiAsync(() =>
            {
                IsTaskPanelBusy = false;
                TaskQueueBatchMode = TaskQueueBatchActionMode.None;
            });
        }
    }

    private async Task RemapSelectedTaskAsync()
    {
        var selectedRows = SelectedAutoTaskQueueRowsSnapshot
            .Where(x => !string.Equals(x.Status, "RUNNING", StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (selectedRows.Count == 0)
        {
            _toast.Warn("重新映射", "请先选择至少一条非 RUNNING 任务");
            return;
        }

        if (IsAutoBoardBusy)
        {
            return;
        }

        var ok = await _dialog.Confirm(
            "回退到映射队列",
            $"将把选中的 {selectedRows.Count} 条任务回退到映射结果队列，并等待重新映射，原任务会停止执行并保留审计记录，确认继续？").ConfigureAwait(false);
        if (!ok)
        {
            return;
        }

        if (!await RequireUnlockAsync(
                SensitiveOpKind.MsfxRemap,
                "重新映射",
                string.Join(",", selectedRows.Select(x => x.TaskId)),
                "manual remap from task queue").ConfigureAwait(false))
        {
            return;
        }

        try
        {
            IsTaskPanelBusy = true;
            var opName = Environment.UserName;
            var successCount = 0;
            var failedCount = 0;
            var resetStagingCount = 0;

            foreach (var taskRow in selectedRows)
            {
                try
                {
                    var result = await _syncService.RemapInjectAsync(
                        taskRow.TaskId,
                        opName,
                        "manual remap from task queue",
                        CancellationToken.None).ConfigureAwait(false);
                    successCount += 1;
                    resetStagingCount += result.ResetStagingCount;
                    AddAutoLog("重新映射", $"任务 #{result.TaskId} 已回退到映射队列，状态={result.Status}，回退码数={result.ResetStagingCount}", TraceEntryState.Warning);
                    LogWarn("msfx.task.remap.success", "MSFX inject task returned to mapping queue", null, new
                    {
                        result.TaskId,
                        result.Status,
                        result.TotalCodes,
                        result.ResetStagingCount,
                        operatorName = opName
                    });
                }
                catch (Exception ex)
                {
                    failedCount += 1;
                    AddAutoLog("重新映射", $"任务 #{taskRow.TaskId} 回退失败：{ex.Message}", TraceEntryState.Failed);
                    LogError("msfx.task.remap.fail", "MSFX inject task return to mapping queue failed", ex, new
                    {
                        taskRow.TaskId,
                        operatorName = opName
                    });
                }
            }

            if (failedCount == 0)
            {
                _toast.Success("重新映射", $"成功 {successCount} 条，回退码 {resetStagingCount} 条");
            }
            else
            {
                _toast.Warn("重新映射", $"成功 {successCount} 条，失败 {failedCount} 条，回退码 {resetStagingCount} 条");
            }

            ResetMapQueueCursor();
            await RefreshMapQueueLatestAsync().ConfigureAwait(false);
            await RefreshTaskPanelAsync(CancellationToken.None).ConfigureAwait(false);
        }
        finally
        {
            await RunOnUiAsync(() =>
            {
                IsTaskPanelBusy = false;
                TaskQueueBatchMode = TaskQueueBatchActionMode.None;
            });
        }
    }

    private async Task MergeSelectedTaskAsync()
    {
        var selectedRows = SelectedAutoTaskQueueRowsSnapshot
            .Where(x => string.Equals(x.Status, "NEW", StringComparison.OrdinalIgnoreCase)
                     || string.Equals(x.Status, "FAILED", StringComparison.OrdinalIgnoreCase)
                     || string.Equals(x.Status, "DISCARDED", StringComparison.OrdinalIgnoreCase)
                     || string.Equals(x.Status, "CANCELLED", StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (selectedRows.Count < 2)
        {
            _toast.Warn("合并任务", "请至少选择两条可编排任务");
            return;
        }

        var keys = selectedRows
            .Select(x => $"{x.MappedDrugId}|{x.MappedSpec}")
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (keys.Count != 1)
        {
            _toast.Warn("合并任务", "仅支持同药名、同规格的任务合并");
            return;
        }

        if (IsAutoBoardBusy)
        {
            return;
        }

        var totalCodes = selectedRows.Sum(x => x.TotalCodes);
        var ok = await _dialog.Confirm(
            "合并任务",
            $"将把选中的 {selectedRows.Count} 条任务合并为 1 条执行任务，总码数约 {totalCodes} 条，允许跨 bill.code 合并，确认继续？").ConfigureAwait(false);
        if (!ok)
        {
            return;
        }

        if (!await RequireUnlockAsync(
                SensitiveOpKind.MsfxMerge,
                "合并任务",
                string.Join(",", selectedRows.Select(x => x.TaskId)),
                "manual merge from task queue").ConfigureAwait(false))
        {
            return;
        }

        try
        {
            IsTaskPanelBusy = true;
            var opName = Environment.UserName;
            var result = await _syncService.MergeInjectsAsync(
                selectedRows.Select(x => x.TaskId).ToArray(),
                opName,
                "manual merge from task queue",
                CancellationToken.None).ConfigureAwait(false);

            AddAutoLog("任务合并", $"新任务 #{result.TaskId} 已创建，合并 {result.MergedTaskCount} 条任务，总码数={result.TotalCodes}", TraceEntryState.Warning);
            LogWarn("msfx.task.merge.success", "MSFX inject tasks merged", null, new
            {
                result.TaskId,
                result.Status,
                result.TotalCodes,
                result.MergedTaskCount,
                operatorName = opName
            });
            _toast.Success("合并任务", $"已合并 {result.MergedTaskCount} 条任务，生成新任务 #{result.TaskId}");
            await RefreshTaskPanelAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            AddAutoLog("任务合并", $"合并失败：{ex.Message}", TraceEntryState.Failed);
            LogError("msfx.task.merge.fail", "MSFX inject tasks merge failed", ex, new { operatorName = Environment.UserName });
            _toast.Error("合并任务", ex.Message);
        }
        finally
        {
            await RunOnUiAsync(() =>
            {
                IsTaskPanelBusy = false;
                TaskQueueBatchMode = TaskQueueBatchActionMode.None;
            });
        }
    }

    [RelayCommand]
    private async Task SplitSelectedTaskAsync()
    {
        var taskRow = SelectedAutoTaskQueueRow;
        if (taskRow is null
            || taskRow.CurrentCodeCount <= 1
            || !(string.Equals(taskRow.Status, "NEW", StringComparison.OrdinalIgnoreCase)
                 || string.Equals(taskRow.Status, "FAILED", StringComparison.OrdinalIgnoreCase)
                 || string.Equals(taskRow.Status, "DISCARDED", StringComparison.OrdinalIgnoreCase)
                 || string.Equals(taskRow.Status, "CANCELLED", StringComparison.OrdinalIgnoreCase)))
        {
            _toast.Warn("拆分任务", "请选择一条码数大于 1 的可编排任务");
            return;
        }

        if (IsAutoBoardBusy)
        {
            return;
        }

        var splitUnits = await _syncService.GetInjectSplitUnitsAsync(taskRow.TaskId, CancellationToken.None).ConfigureAwait(false);
        var splitCodeRows = await _syncService.GetInjectSplitCodeRowsAsync(taskRow.TaskId, CancellationToken.None).ConfigureAwait(false);
        var choice = await _dialog.ShowMsfxTaskSplit(new MsfxTaskSplitArgs(
            TaskId: taskRow.TaskId,
            SourceBillCode: taskRow.SourceBillCode,
            Target: taskRow.Target,
            TotalCodes: taskRow.TotalCodes,
            SplitCodeRows: splitCodeRows)).ConfigureAwait(false);
        if (choice.Action == MsfxTaskSplitAction.Cancel)
        {
            return;
        }

        var splitReason = choice.Action == MsfxTaskSplitAction.CustomQuantity
            ? $"manual custom split from task queue: {choice.CustomQuantities}"
            : "manual split from task queue";
        if (!await RequireUnlockAsync(
                SensitiveOpKind.MsfxSplit,
                "拆分任务",
                taskRow.TaskId.ToString(CultureInfo.InvariantCulture),
                splitReason).ConfigureAwait(false))
        {
            return;
        }

        try
        {
            IsTaskPanelBusy = true;
            var opName = Environment.UserName;
            if (choice.Action == MsfxTaskSplitAction.CustomQuantity)
            {
                var customPlan = TryBuildCustomSplitPlan(splitUnits, choice.CustomQuantities, out var customError);
                if (customPlan is null)
                {
                    _toast.Warn("自定义拆分", customError ?? "拆分计划无效");
                    return;
                }

                var customResult = await _syncService.SplitInjectCustomAsync(
                    taskRow.TaskId,
                    customPlan.Value.GroupKeys,
                    customPlan.Value.BucketIndexes,
                    opName,
                    $"manual custom split from task queue: {customPlan.Value.DisplayText}",
                    CancellationToken.None).ConfigureAwait(false);

                AddAutoLog("任务拆分", $"任务 #{taskRow.TaskId} 已按自定义数量拆分，生成 {customResult.CreatedTasks} 条任务，总码数={customResult.TotalCodes}", TraceEntryState.Warning);
                LogWarn("msfx.task.split.custom.success", "MSFX inject task custom split", null, new
                {
                    taskRow.TaskId,
                    customResult.CreatedTasks,
                    customResult.TotalCodes,
                    customResult.BucketCount,
                    customPlan.Value.DisplayText,
                    operatorName = opName
                });
                _toast.Success("自定义拆分", $"已按 {customPlan.Value.DisplayText} 生成 {customResult.CreatedTasks} 条任务");
            }
            else
            {
                var splitMode = choice.Action == MsfxTaskSplitAction.ParentCluster ? "PARENT_CLUSTER" : "BATCH";
                var result = await _syncService.SplitInjectAsync(
                    taskRow.TaskId,
                    splitMode,
                    opName,
                    "manual split from task queue",
                    CancellationToken.None).ConfigureAwait(false);

                var splitText = string.Equals(result.SplitMode, "BATCH", StringComparison.OrdinalIgnoreCase)
                    ? "按批号"
                    : "按父码簇";
                AddAutoLog("任务拆分", $"任务 #{taskRow.TaskId} 已{splitText}拆分，生成 {result.CreatedTasks} 条任务，总码数={result.TotalCodes}", TraceEntryState.Warning);
                LogWarn("msfx.task.split.success", "MSFX inject task split", null, new
                {
                    taskRow.TaskId,
                    result.CreatedTasks,
                    result.TotalCodes,
                    result.SplitMode,
                    operatorName = opName
                });
                _toast.Success("拆分任务", $"已生成 {result.CreatedTasks} 条任务");
            }

            await RefreshTaskPanelAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            AddAutoLog("任务拆分", $"任务 #{taskRow.TaskId} 拆分失败：{ex.Message}", TraceEntryState.Failed);
            LogError("msfx.task.split.fail", "MSFX inject task split failed", ex, new
            {
                taskRow.TaskId,
                operatorName = Environment.UserName
            });
            _toast.Error("拆分任务", ex.Message);
        }
        finally
        {
            await RunOnUiAsync(() => IsTaskPanelBusy = false);
        }
    }

    [RelayCommand]
    private async Task OpenMapBatchDialogAsync()
    {
        if (IsAutoBoardBusy)
        {
            return;
        }

        EnterManualMsfxWrite();
        var batchScene = "批量映射";
        try
        {
            var groups = await _syncService.GetMappingBatchGroupsAsync(
                mapStatus: FilterInput.Norm(MapQueueMapStatusFilter),
                codeStatus: FilterInput.Norm(MapQueueCodeStatusFilter),
                searchScope: ResolveSearchScope(MapQueueSearchScope),
                keyword: NormalizeText(MapQueueKeyword),
                limit: 500,
                ct: CancellationToken.None).ConfigureAwait(false);

            var res = await _dialog.ShowMsfxMappingBatch(new MsfxMappingBatchArgs(
                Groups: groups,
                MapStatusFilter: MapQueueMapStatusFilter,
                CodeStatusFilter: MapQueueCodeStatusFilter,
                SearchScope: MapQueueSearchScope,
                Keyword: MapQueueKeyword ?? string.Empty)).ConfigureAwait(false);

            if (res.Action == MsfxMappingBatchAction.Cancel)
            {
                return;
            }

            batchScene = res.Action == MsfxMappingBatchAction.DiscardTask
                ? "批量弃用"
                : "批量映射";

            if (res.Group is null)
            {
                _toast.Warn(batchScene, "请先在分组表中选择一条记录");
                return;
            }

            var group = res.Group;
            var action = res.Action switch
            {
                MsfxMappingBatchAction.ApplyMap => "APPLY_MAP",
                MsfxMappingBatchAction.DiscardTask => "APPLY_DISCARD",
                _ => "APPLY_MAP"
            };

            if ((res.Action == MsfxMappingBatchAction.ApplyMap || res.Action == MsfxMappingBatchAction.DiscardTask) &&
                (string.IsNullOrWhiteSpace(res.DrugId) || string.IsNullOrWhiteSpace(res.Spec)))
            {
                _toast.Warn(batchScene, "需要填写需映射的药品信息和规格信息");
                return;
            }

            var preview = await _syncService.PreviewMappingBatchByGroupAsync(
                mapStatus: null,
                codeStatus: null,
                searchScope: "ALL",
                keyword: null,
                groupSourceDrugNameRaw: group.SourceDrugNameRaw,
                groupSourceSpecRaw: group.SourceSpecRaw,
                groupSourceNameNorm: group.SourceNameNorm,
                groupSourceSpecNorm: group.SourceSpecNorm,
                action: action,
                drugId: res.DrugId,
                spec: res.Spec,
                ct: CancellationToken.None).ConfigureAwait(false);

            if (preview.EligibleCount <= 0)
            {
                _toast.Warn(batchScene, $"无可执行记录，将影响 {preview.CandidateCount} 条，阻塞 {preview.BlockedCount} 条");
                return;
            }

            var confirmTitle = batchScene;
            var groupLabel = DrugLabel.Format(group.SourceDrugNameRaw, group.SourceSpecRaw);
            var confirmMsg = res.Action switch
            {
                MsfxMappingBatchAction.ApplyMap => $"分组“{groupLabel}”将影响 {preview.CandidateCount} 条，可执行 {preview.EligibleCount} 条，确认批量映射？",
                MsfxMappingBatchAction.DiscardTask => $"分组“{groupLabel}”将影响 {preview.CandidateCount} 条，可执行 {preview.EligibleCount} 条，确认弃用任务？",
                _ => $"分组“{groupLabel}”将影响 {preview.CandidateCount} 条，可执行 {preview.EligibleCount} 条，确认处理？"
            };
            var ok = res.Action == MsfxMappingBatchAction.DiscardTask
                ? await _dialog.ConfirmDestructive(confirmTitle, confirmMsg).ConfigureAwait(false)
                : await _dialog.Confirm(confirmTitle, confirmMsg).ConfigureAwait(false);
            if (!ok)
            {
                return;
            }

            var mappingKind = res.Action == MsfxMappingBatchAction.DiscardTask
                ? SensitiveOpKind.MsfxDiscard
                : SensitiveOpKind.MsfxMappingApply;
            var mappingReason = res.Action == MsfxMappingBatchAction.DiscardTask
                ? "manual batch discard from mapping dialog"
                : "manual batch mapping apply from mapping dialog";
            var unlockScene = confirmTitle;
            if (!await RequireUnlockAsync(
                    mappingKind,
                    unlockScene,
                    $"{group.SourceDrugNameRaw}/{group.SourceSpecRaw}",
                    mappingReason).ConfigureAwait(false))
            {
                return;
            }

            var apply = await _syncService.ApplyMappingBatchByGroupAsync(
                mapStatus: null,
                codeStatus: null,
                searchScope: "ALL",
                keyword: null,
                groupSourceDrugNameRaw: group.SourceDrugNameRaw,
                groupSourceSpecRaw: group.SourceSpecRaw,
                groupSourceNameNorm: group.SourceNameNorm,
                groupSourceSpecNorm: group.SourceSpecNorm,
                action: action,
                drugId: res.DrugId,
                spec: res.Spec,
                ct: CancellationToken.None).ConfigureAwait(false);

            if (apply.AffectedCount <= 0)
            {
                _toast.Warn(confirmTitle, "本次未更新任何记录，请检查筛选条件或映射目标");
                return;
            }

            if (res.Action == MsfxMappingBatchAction.ApplyMap && apply.AffectedCount > 0)
            {
                var built = await _syncService.BuildInjectsAsync(500, CancellationToken.None).ConfigureAwait(false);
                AddAutoLog(confirmTitle, $"分组处理 {apply.AffectedCount} 条，新增任务 {built.CreatedTasks}", TraceEntryState.Success);
                LogInfo("msfx.map.batch.apply", "MSFX batch mapping applied", new
                {
                    apply.AffectedCount,
                    built.CreatedTasks,
                    group.SourceDrugNameRaw,
                    group.SourceSpecRaw,
                    DrugId = res.DrugId,
                    Spec = res.Spec
                });
                _toast.Success(confirmTitle, $"已处理 {apply.AffectedCount} 条，新增任务 {built.CreatedTasks}");
            }
            else if (res.Action == MsfxMappingBatchAction.DiscardTask)
            {
                AddAutoLog(confirmTitle, $"分组弃用 {apply.AffectedCount} 条，已进入弃用任务队列", TraceEntryState.Discarded);
                LogInfo("msfx.map.batch.discard", "MSFX batch mapping discarded into task queue", new
                {
                    apply.AffectedCount,
                    group.SourceDrugNameRaw,
                    group.SourceSpecRaw,
                    DrugId = res.DrugId,
                    Spec = res.Spec
                });
                _toast.Success(confirmTitle, $"已处理 {apply.AffectedCount} 条，并直接进入弃用任务队列");
            }
            else
            {
                _toast.Info(confirmTitle, $"已处理 {apply.AffectedCount} 条");
            }

            await RefreshAutoBoardAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _toast.Error(batchScene, ex.Message);
        }
        finally
        {
            await RunOnUiAsync(ExitManualMsfxWrite);
        }
    }

    [RelayCommand]
    private Task ShowPullBatchDetailAsync(MsfxAutoPullBatchGridRow? row)
    {
        if (row is null)
        {
            return Task.CompletedTask;
        }

        var items = new List<InfoDetailItem>
        {
            new("批次ID", row.BatchId.ToString(CultureInfo.InvariantCulture)),
            new("源接口", row.SourceApi),
            new("日期窗口", row.Window),
            new("状态", row.Status),
            new("成功/失败", $"{row.SuccessCount}/{row.FailCount}"),
            new("开始时间", row.StartedAt),
            new("结束时间", row.FinishedAt),
            new("错误信息", string.IsNullOrWhiteSpace(row.ErrMsg) ? "--" : row.ErrMsg)
        };
        return _dialog.ShowMsfxStateDetail(new MsfxStateDetailArgs(
            Header: "拉取批次详情",
            SubHeader: "批次执行与结果审计",
            State: row.State,
            HighlightTitle: $"批次 #{row.BatchId} / {row.Status}",
            HighlightMessage: string.IsNullOrWhiteSpace(row.ErrMsg)
                ? "批次已完成，无错误信息"
                : row.ErrMsg,
            Items: items));
    }

    [RelayCommand]
    private Task ShowTaskQueueDetailAsync(MsfxAutoTaskQueueGridRow? row)
    {
        if (row is null)
        {
            return Task.CompletedTask;
        }

        var items = new List<InfoDetailItem>
        {
            new("任务ID", row.TaskId.ToString(CultureInfo.InvariantCulture)),
            new("单据编码", row.SourceBillCode),
            new("目标药品/规格", DrugLabel.Format(row.MappedDrugId, row.MappedSpec)),
            new("状态", row.Status),
            new("进度", row.Progress),
            new("重试次数", row.RetryCount.ToString(CultureInfo.InvariantCulture)),
            new("创建时间", row.CreatedAt),
            new("抢占时间", row.PickedAt),
            new("完成时间", row.FinishedAt),
            new("错误信息", string.IsNullOrWhiteSpace(row.ErrMsg) ? "--" : row.ErrMsg)
        };
        return _dialog.ShowMsfxStateDetail(new MsfxStateDetailArgs(
            Header: "Agent 任务详情",
            SubHeader: "注入执行状态与错误信息",
            State: row.State,
            HighlightTitle: $"任务 #{row.TaskId} / {row.Status}",
            HighlightMessage: string.IsNullOrWhiteSpace(row.ErrMsg)
                ? "任务执行中或已完成，无错误信息"
                : row.ErrMsg,
            Items: items));
    }

    [RelayCommand]
    private Task ShowAutoLogDetailAsync(MsfxAutoLogRow? row)
    {
        if (row is null)
        {
            return Task.CompletedTask;
        }

        return _dialog.ShowMsfxStateDetail(new MsfxStateDetailArgs(
            Header: "运行日志详情",
            SubHeader: "自动化执行链路事件",
            State: row.State,
            HighlightTitle: row.Stage,
            HighlightMessage: row.Message,
            Items: new List<InfoDetailItem>
            {
                new("时间", row.At),
                new("阶段", row.Stage),
                new("级别", row.State.ToString())
            }));
    }

    [RelayCommand(CanExecute = nameof(CanRefreshAutoBoard))]
    private Task RefreshAutoBoardAsync()
        => RunLocalReloadAsync(_ => { }, RefreshAutoBoardAsync);

    private async Task RefreshAutoBoardAsync(CancellationToken ct)
    {
        try
        {
            MsfxAutoBoardSnapshot snap = default!;
            await RunLocalBusyAsync(
                ct,
                v => IsPullPanelBusy = v,
                async () => snap = await RefreshPullPanelAsync(ct).ConfigureAwait(false),
                ShowPullPanelBusy()).ConfigureAwait(false);
            await RunLocalBusyAsync(
                ct,
                v => IsMapPanelBusy = v,
                () => RefreshMapPanelAsync(ct),
                ShowMapPanelBusy()).ConfigureAwait(false);
            await RunLocalBusyAsync(
                ct,
                v => IsTaskPanelBusy = v,
                () => RefreshTaskPanelAsync(ct),
                ShowTaskPanelBusy()).ConfigureAwait(false);
            await RunOnUiAsync(ClearAllDetailSelectionsSilent);

            if (snap.MapPendingCount > 0 && snap.MapMappedCount == 0 && snap.TaskNewCount == 0 && snap.TaskRunningCount == 0)
            {
                AddAutoLog("诊断", $"存在待映射 {snap.MapPendingCount} 条但无映射命中，任务队列为空请检查映射函数或字段归一化", TraceEntryState.Warning);
                LogWarn("msfx.audit.diagnose.pending_without_match", "MSFX diagnostics found pending rows without mapping hit", null, new
                {
                    snap.MapPendingCount,
                    snap.MapMappedCount,
                    snap.TaskNewCount,
                    snap.TaskRunningCount
                });
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            AddAutoLog("审计", $"刷新数据库概览失败：{ex.Message}", TraceEntryState.Warning);
            LogWarn("msfx.audit.snapshot.refresh_fail", "MSFX snapshot refresh failed", ex);
            if (CanToastError(ex))
            {
                _toast.Error("刷新审计", ex.Message);
            }

            throw;
        }
    }

    private async Task<MsfxAutoBoardSnapshot> RefreshAutoSummaryAsync(CancellationToken ct)
    {
        var snap = await _syncService.GetAutoBoardSnapshotAsync(ct).ConfigureAwait(false);
        await RunOnUiAsync(() =>
        {
            AutoPullSummary = $"批次#{snap.LastBatchId} {snap.LastBatchStatus} 成功{snap.LastBatchSuccessCount}/失败{snap.LastBatchFailCount}";
            AutoMapSummary = $"待映射{snap.MapPendingCount} 已映射{snap.MapMappedCount} 待人工{snap.MapNeedReviewCount} 失败{snap.MapFailedCount}";
            AutoTaskNewCount = snap.TaskNewCount;
            AutoTaskRunningCount = snap.TaskRunningCount;
            AutoTaskSuccessCount = snap.TaskSuccessCount;
            AutoTaskFailedCount = snap.TaskFailedCount;
            AutoTaskDiscardedCount = snap.TaskDiscardedCount;

            AutoPullState = ToBatchState(snap.LastBatchStatus);
            AutoMapState = snap.MapFailedCount > 0
                ? TraceEntryState.Failed
                : (snap.MapNeedReviewCount > 0 || snap.MapPendingCount > 0) ? TraceEntryState.Warning
                : snap.MapMappedCount > 0 ? TraceEntryState.Success : TraceEntryState.Info;
            AutoTaskState = snap.TaskFailedCount > 0
                ? TraceEntryState.Failed
                : (snap.TaskRunningCount > 0 || snap.TaskNewCount > 0) ? TraceEntryState.Warning
                : snap.TaskSuccessCount > 0 ? TraceEntryState.Success : TraceEntryState.Info;
            AutoRiskState = (snap.StagingFailedCount + snap.StagingDuplicateCount + snap.TaskCancelledCount + snap.LastBatchFailCount) > 0
                ? TraceEntryState.Warning
                : TraceEntryState.Success;
            AutoLastRunAtText = FormatAutoLastRunDisplay(snap);
            AutoLastRunAtTip = FormatAutoLastRunTip(snap);
        });

        return snap;
    }

    private async Task<MsfxAutoBoardSnapshot> RefreshPullPanelAsync(CancellationToken ct)
    {
        var snap = await RefreshAutoSummaryAsync(ct).ConfigureAwait(false);
        var pullRows = await _syncService.GetRecentPullBatchesAsync(500, ct).ConfigureAwait(false);
        await RunOnUiAsync(() =>
        {
            _allPullBatchRows = pullRows.Select(x => new MsfxAutoPullBatchGridRow(
                    BatchId: x.BatchId,
                    SourceApi: x.SourceApi,
                    Window: $"{(x.BeginDate?.ToString("yyyy-MM-dd") ?? "--")} ~ {(x.EndDate?.ToString("yyyy-MM-dd") ?? "--")}",
                    Status: x.Status,
                    SuccessCount: x.SuccessCount,
                    FailCount: x.FailCount,
                    StartedAt: x.StartedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss"),
                    FinishedAt: x.FinishedAt?.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss") ?? "--",
                    ErrMsg: x.ErrMsg ?? string.Empty,
                    State: ToBatchState(x.Status))).ToList();
            PullBatchTotalCount = _allPullBatchRows.Count;
            if (PullBatchPage > PullBatchTotalPages)
            {
                PullBatchPage = PullBatchTotalPages;
            }

            ApplyPullBatchPage();
        });
        return snap;
    }

    private async Task<MsfxAutoBoardSnapshot> RefreshMapPanelAsync(CancellationToken ct)
    {
        var snap = await RefreshAutoSummaryAsync(ct).ConfigureAwait(false);
        ResetMapQueueCursor();
        await RefreshMapQueueAsync(ct, olderPage: null).ConfigureAwait(false);
        return snap;
    }

    private async Task<MsfxAutoBoardSnapshot> RefreshTaskPanelAsync(CancellationToken ct)
    {
        var snap = await RefreshAutoSummaryAsync(ct).ConfigureAwait(false);
        var taskRows = await _syncService.GetInjectQueueAsync(0, ct).ConfigureAwait(false);
        await RunOnUiAsync(() =>
        {
            var checkedIds = _allTaskQueueRows.Where(x => x.IsChecked).Select(x => x.TaskId).ToHashSet();
            _allTaskQueueRows = taskRows.Select(x => new MsfxAutoTaskQueueGridRow(
                    taskId: x.TaskId,
                    sourceBillCode: x.SourceBillCode ?? "--",
                    batchNos: string.IsNullOrWhiteSpace(x.BatchNos) ? "--" : x.BatchNos!,
                    mappedDrugId: x.MappedDrugId,
                    mappedSpec: x.MappedSpec,
                    totalCodes: x.TotalCodes,
                    currentCodeCount: x.CurrentCodeCount,
                    target: $"{x.MappedDrugId} / {x.MappedSpec}",
                    status: x.Status,
                    progress: $"{x.SuccessCodes}/{x.TotalCodes} 成功, 失败{x.FailedCodes}",
                    retryCount: x.RetryCount,
                    createdAt: x.CreatedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss"),
                    pickedAt: x.PickedAt?.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss") ?? "--",
                    finishedAt: x.FinishedAt?.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss") ?? "--",
                    errMsg: x.ErrMsg ?? string.Empty,
                    state: ToTaskState(x.Status)))
                .ToList();
            foreach (var row in _allTaskQueueRows)
            {
                row.IsChecked = checkedIds.Contains(row.TaskId);
            }

            ApplyTaskQueueFilter();
            SyncCheckedAutoTaskQueueRows();
        });
        return snap;
    }

    private void ApplyTaskQueueFilter()
    {
        var keyword = TaskQueueKeyword?.Trim() ?? string.Empty;
        IEnumerable<MsfxAutoTaskQueueGridRow> filtered = _allTaskQueueRows;
        if (!string.Equals(TaskQueueStatusFilter, "ALL", StringComparison.OrdinalIgnoreCase))
        {
            filtered = filtered.Where(row => string.Equals(row.Status, TaskQueueStatusFilter, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(keyword))
        {
            filtered = filtered.Where(row => TaskQueueSearchScope switch
            {
                "单据编号" => ContainsTaskQueueIgnoreCase(row.SourceBillCode, keyword),
                "药品" => ContainsTaskQueueIgnoreCase(row.MappedDrugId, keyword),
                "规格" => ContainsTaskQueueIgnoreCase(row.MappedSpec, keyword),
                _ => ContainsTaskQueueIgnoreCase(row.SourceBillCode, keyword)
                     || ContainsTaskQueueIgnoreCase(row.MappedDrugId, keyword)
                     || ContainsTaskQueueIgnoreCase(row.MappedSpec, keyword)
                     || ContainsTaskQueueIgnoreCase(row.BatchNos, keyword)
                     || ContainsTaskQueueIgnoreCase(row.Status, keyword)
            });
        }

        _filteredTaskQueueRows = filtered.ToList();
        if (TaskQueuePage > TaskQueueTotalPages)
        {
            TaskQueuePage = TaskQueueTotalPages;
        }

        ApplyTaskQueuePage();

        OnPropertyChanged(nameof(CanMergeTasks));
        OnPropertyChanged(nameof(CanRemapTasks));
        OnPropertyChanged(nameof(CanDiscardTasks));
        OnPropertyChanged(nameof(CanReopenTasks));
        OnPropertyChanged(nameof(TaskQueueTotalPages));
        OnPropertyChanged(nameof(TaskQueueFilteredCount));
    }

    private void ApplyTaskQueuePage()
    {
        var pageSize = GetTaskQueueEffectivePageSize();
        var totalPages = TaskQueueTotalPages;
        if (TaskQueuePage > totalPages)
        {
            TaskQueuePage = totalPages;
        }

        var page = Math.Max(1, TaskQueuePage);
        var skip = (page - 1) * pageSize;
        var rows = _filteredTaskQueueRows.Skip(skip).Take(pageSize).ToList();

        AutoTaskQueueRows.ResetContents(rows);

        OnPropertyChanged(nameof(TaskQueueTotalPages));
    }

    private void ApplyAutoLogPage()
    {
        var pageSize = GetAutoLogEffectivePageSize();
        var totalPages = AutoLogTotalPages;
        if (AutoLogPage > totalPages)
        {
            AutoLogPage = totalPages;
        }

        var page = Math.Max(1, AutoLogPage);
        var skip = (page - 1) * pageSize;
        var rows = AutoLogs.Skip(skip).Take(pageSize).ToList();

        AutoLogPageRows.ResetContents(rows);

        OnPropertyChanged(nameof(AutoLogTotalPages));
        OnPropertyChanged(nameof(IsAutoLogsEmpty));
    }

    private int GetTaskQueuePageSize()
    {
        if (!int.TryParse(TaskQueuePageSize, out var pageSize))
        {
            return 50;
        }

        return Math.Clamp(pageSize, 10, 500);
    }

    private int GetTaskQueueEffectivePageSize()
        => IsTaskPanelExpanded ? GetTaskQueuePageSize() : TaskQueuePreviewPageSize;

    private int GetAutoLogPageSize()
    {
        if (!int.TryParse(AutoLogPageSize, out var pageSize))
        {
            return 50;
        }

        return Math.Clamp(pageSize, 10, 500);
    }

    private int GetAutoLogEffectivePageSize()
        => IsLogPanelExpanded ? GetAutoLogPageSize() : AutoLogPreviewPageSize;

    private static bool ContainsTaskQueueIgnoreCase(string? text, string keyword)
        => TextSearchHelper.Matches(keyword, text);

    private static string NormalizeMergeKeyPart(string? value)
        => value?.Trim() ?? string.Empty;

    private static string BuildMergeKey(MsfxAutoTaskQueueGridRow row)
        => $"{NormalizeMergeKeyPart(row.MappedDrugId)}|{NormalizeMergeKeyPart(row.MappedSpec)}";

    private static (string[] GroupKeys, int[] BucketIndexes, string DisplayText)? TryBuildCustomSplitPlan(
        IReadOnlyList<MsfxInjectSplitUnitRow> units,
        string? rawText,
        out string? error)
    {
        error = null;
        var tokens = (rawText ?? string.Empty)
            .Split(new[] { ',', '，', ';', '；', ' ', '\n', '\r', '\t' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (tokens.Length < 2)
        {
            error = "请至少输入两组数量，例如：400,400";
            return null;
        }

        var targets = new List<int>(tokens.Length);
        foreach (var token in tokens)
        {
            if (!int.TryParse(token, out var qty) || qty <= 0)
            {
                error = $"数量“{token}”无效";
                return null;
            }
            targets.Add(qty);
        }

        var sourceUnits = units?
            .Where(x => x.CodeCount > 0 && !string.IsNullOrWhiteSpace(x.GroupKey))
            .Select(x => (x.GroupKey, x.CodeCount))
            .ToList() ?? new List<(string GroupKey, int CodeCount)>();
        if (sourceUnits.Count == 0)
        {
            error = "当前任务没有可拆分的父码簇";
            return null;
        }

        var totalCodes = sourceUnits.Sum(x => x.CodeCount);
        if (targets.Sum() != totalCodes)
        {
            error = $"自定义数量总和 {targets.Sum()} 与当前总码数 {totalCodes} 不一致";
            return null;
        }

        List<(string GroupKey, int BucketIndex)>? assignments;
        if (sourceUnits.All(x => x.CodeCount == 1))
        {
            assignments = BuildSequentialAssignments(sourceUnits.Select(x => x.GroupKey).ToList(), targets);
        }
        else
        {
            assignments = BuildBacktrackingAssignments(sourceUnits, targets);
        }

        if (assignments is null)
        {
            error = "当前父码簇组合无法精确匹配这组自定义数量，请调整分组数量";
            return null;
        }

        return (
            assignments.Select(x => x.GroupKey).ToArray(),
            assignments.Select(x => x.BucketIndex).ToArray(),
            string.Join(" + ", targets));
    }

    private static List<(string GroupKey, int BucketIndex)> BuildSequentialAssignments(
        IReadOnlyList<string> groupKeys,
        IReadOnlyList<int> targets)
    {
        var result = new List<(string GroupKey, int BucketIndex)>(groupKeys.Count);
        var cursor = 0;
        for (var bucket = 0; bucket < targets.Count; bucket++)
        {
            for (var i = 0; i < targets[bucket]; i++)
            {
                result.Add((groupKeys[cursor], bucket + 1));
                cursor++;
            }
        }

        return result;
    }

    private static List<(string GroupKey, int BucketIndex)>? BuildBacktrackingAssignments(
        IReadOnlyList<(string GroupKey, int CodeCount)> sourceUnits,
        IReadOnlyList<int> targets)
    {
        var ordered = sourceUnits
            .OrderByDescending(x => x.CodeCount)
            .ThenBy(x => x.GroupKey, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var remaining = targets.ToArray();
        var placed = new int[ordered.Count];

        bool Dfs(int index)
        {
            if (index >= ordered.Count)
            {
                return remaining.All(x => x == 0);
            }

            var unit = ordered[index];
            var triedRemaining = new HashSet<int>();
            for (var bucket = 0; bucket < remaining.Length; bucket++)
            {
                if (remaining[bucket] < unit.CodeCount)
                {
                    continue;
                }

                if (!triedRemaining.Add(remaining[bucket]))
                {
                    continue;
                }

                remaining[bucket] -= unit.CodeCount;
                placed[index] = bucket + 1;
                if (Dfs(index + 1))
                {
                    return true;
                }

                remaining[bucket] += unit.CodeCount;
                placed[index] = 0;
            }

            return false;
        }

        if (!Dfs(0))
        {
            return null;
        }

        return ordered.Select((unit, idx) => (unit.GroupKey, placed[idx])).ToList();
    }

    private Task RefreshMapQueueLatestAsync()
    {
        if (IsAutoBoardBusy || SelectedTabIndex != 0)
        {
            return Task.CompletedTask;
        }

        return RunMapPanelQueryAsync(ct => RefreshMapQueueAsync(ct, olderPage: null));
    }

    private void ResetMapQueueCursor()
    {
        _mapCursorUpdatedAt = null;
        _mapCursorId = null;
        MapQueuePage = 1;
    }

    private async Task RefreshMapQueueAsync(CancellationToken ct, bool? olderPage, bool seekLastPage = false)
    {
        var newer = olderPage.HasValue && !olderPage.Value;
        var hasCursor = _mapCursorUpdatedAt.HasValue && _mapCursorId.HasValue;
        DateTimeOffset? cursorAt = null;
        long? cursorId = null;

        if (seekLastPage)
        {
            newer = false;
            cursorAt = null;
            cursorId = null;
        }
        else if (olderPage == true && AutoMapQueueRows.Count > 0)
        {
            var last = AutoMapQueueRows[^1];
            cursorAt = last.UpdatedAtRaw;
            cursorId = last.StagingId;
        }
        else if (olderPage == false && AutoMapQueueRows.Count > 0)
        {
            var first = AutoMapQueueRows[0];
            cursorAt = first.UpdatedAtRaw;
            cursorId = first.StagingId;
        }
        else if (olderPage is null && hasCursor)
        {
            cursorAt = _mapCursorUpdatedAt;
            cursorId = _mapCursorId;
            newer = false;
        }

        if (olderPage is null && !hasCursor)
        {
            cursorAt = null;
            cursorId = null;
        }

        var fullMapMode = IsMapPanelExpanded;
        if (!fullMapMode)
        {
            cursorAt = null;
            cursorId = null;
            newer = false;
            olderPage = null;
            seekLastPage = false;
        }

        var pageSize = GetMapQueueQueryPageSize();
        var page = await _syncService.GetMappingQueuePageAsync(
            pageSize: pageSize,
            mapStatus: FilterInput.Norm(MapQueueMapStatusFilter),
            codeStatus: FilterInput.Norm(MapQueueCodeStatusFilter),
            searchScope: ResolveSearchScope(MapQueueSearchScope),
            keyword: NormalizeText(MapQueueKeyword),
            cursorUpdatedAt: cursorAt,
            cursorId: cursorId,
            newer: newer,
            seekLastPage: seekLastPage,
            ct: ct).ConfigureAwait(false);

        await RunOnUiAsync(() =>
        {
            var pageSize = GetMapQueuePageSize();
            MapQueueTotalCount = page.TotalCount;
            var totalPages = Math.Max(1, (int)Math.Ceiling(page.TotalCount / (double)Math.Max(1, pageSize)));
            var requestedPage = Math.Max(1, MapQueuePage);
            if (fullMapMode)
            {
                if (seekLastPage)
                {
                    requestedPage = totalPages;
                }
                else if (olderPage == true)
                {
                    requestedPage = Math.Max(1, MapQueuePage + 1);
                }
                else if (olderPage == false)
                {
                    requestedPage = Math.Max(1, MapQueuePage - 1);
                }
            }

            var displayStart = ((requestedPage - 1) * pageSize) + 1;
            var gridRows = new List<MsfxAutoMapQueueGridRow>(page.Rows.Count);
            for (var i = 0; i < page.Rows.Count; i++)
            {
                var x = page.Rows[i];
                gridRows.Add(new MsfxAutoMapQueueGridRow(
                    DisplayIndex: displayStart + i,
                    StagingId: x.StagingId,
                    LeafCode: x.LeafCode,
                    ProduceBatchNo: string.IsNullOrWhiteSpace(x.ProduceBatchNo) ? "--" : x.ProduceBatchNo!,
                    SourceBillTime: string.IsNullOrWhiteSpace(x.SourceBillTime) ? "--" : x.SourceBillTime!,
                    SourceBillCode: x.SourceBillCode ?? "--",
                    SourceDrugNameRaw: x.SourceDrugNameRaw ?? "--",
                    SourceSpecRaw: x.SourceSpecRaw ?? "--",
                    SourceNameNorm: x.SourceNameNorm ?? "--",
                    SourceSpecNorm: x.SourceSpecNorm ?? "--",
                    SourceCodeLevel1: x.SourceCodeLevel1 ?? "--",
                    SourceCodeLevel2: x.SourceCodeLevel2 ?? "--",
                    SourceCodeLevel3: x.SourceCodeLevel3 ?? "--",
                    SourceCodeLevel4: x.SourceCodeLevel4 ?? "--",
                    SourceCodeLevel5: x.SourceCodeLevel5 ?? "--",
                    MapReasonCode: x.MapReasonCode ?? "--",
                    MapReasonDetail: x.MapReasonDetail ?? "--",
                    MappedDrugId: x.MappedDrugId ?? "--",
                    MappedSpec: x.MappedSpec ?? "--",
                    MapStatus: x.MapStatus,
                    CodeStatus: x.CodeStatus,
                    UpdatedAt: x.UpdatedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss"),
                    State: ToMapState(x.MapStatus),
                    UpdatedAtRaw: x.UpdatedAt));
            }

            AutoMapQueueRows.ResetContents(gridRows);

            MapQueueHasNewer = page.HasNewer;
            MapQueueHasOlder = page.HasOlder;
            if (fullMapMode)
            {
                if (seekLastPage)
                {
                    MapQueuePage = totalPages;
                }
                else if (olderPage == true && page.Rows.Count > 0)
                {
                    MapQueuePage += 1;
                }
                else if (olderPage == false && page.Rows.Count > 0)
                {
                    MapQueuePage = Math.Max(1, MapQueuePage - 1);
                }
                else if (olderPage is null)
                {
                    MapQueuePage = 1;
                }
            }
            else
            {
                MapQueuePage = 1;
            }

            if (AutoMapQueueRows.Count > 0)
            {
                var first = AutoMapQueueRows[0];
                _mapCursorUpdatedAt = first.UpdatedAtRaw;
                _mapCursorId = first.StagingId;

                var start = displayStart;
                var end = start + AutoMapQueueRows.Count - 1;
                MapQueueRangeText = $"序号 {start}-{end}";
            }
            else if (MapQueueTotalCount > 0)
            {
                _mapCursorUpdatedAt = null;
                _mapCursorId = null;
                var end = Math.Min(displayStart + pageSize - 1, MapQueueTotalCount);
                MapQueueRangeText = $"序号 {displayStart}-{end}";
            }
            else
            {
                _mapCursorUpdatedAt = null;
                _mapCursorId = null;
                MapQueueRangeText = "序号 --";
            }

            ClearAllDetailSelectionsSilent();
        });
    }

    private void ClearAllDetailSelectionsSilent()
    {
        SelectedAutoPullBatchRow = null;
        SelectedAutoTaskQueueRow = null;
        SelectedAutoLogRow = null;
        SetSelectedAutoTaskQueueRows(Array.Empty<MsfxAutoTaskQueueGridRow>());
    }

    private void OnAutoTimerTick(object? sender, EventArgs e)
    {
        if (!IsAutoEnabled || IsAutoBusy || IsManualMsfxWriteActive)
        {
            return;
        }

        if (Interlocked.CompareExchange(ref _autoTimerTickRunning, 1, 0) != 0)
        {
            return;
        }

        _backgroundTasks.RunDetached(
            async _ =>
            {
                try
                {
                    await QueueAutoOnceAsync(showProgressPanel: false).ConfigureAwait(false);
                }
                finally
                {
                    Interlocked.Exchange(ref _autoTimerTickRunning, 0);
                }
            },
            module: "MsfxLink",
            eventName: "msfx.auto.timer_tick.fail");
    }

    private static MsfxUpoutGridRow MapUpoutRow(MsfxListUpoutItem x)
        => new(
            BillCode: x.BillCode,
            BillType: x.BillType,
            BillTime: x.BillTime,
            DrugName: x.PhysicName,
            PackageSpec: x.PkgSpec,
            PrepnSpec: x.PrepnSpec,
            CodeCount: x.CodeCount,
            PrepnCount: x.PrepnCount,
            ProduceBatchNo: x.ProduceBatchNo,
            ExpireDate: x.ExpireDate,
            FromEntName: x.FromEntName,
            FromRefUserId: x.FromRefUserId,
            ProduceEntName: x.ProduceEntName,
            LogisticsStatus: string.IsNullOrWhiteSpace(x.LogisticsStatus) ? x.Status : x.LogisticsStatus,
            State: ParseUpoutState(x.Status));

    private void ScheduleUpoutFilter()
    {
        if (_allUpoutRows.Count == 0)
        {
            return;
        }

        var hasKeyword = !string.IsNullOrWhiteSpace(UpoutBillCodeKeyword)
                         || !string.IsNullOrWhiteSpace(UpoutDrugKeyword)
                         || !string.IsNullOrWhiteSpace(UpoutFromEntKeyword);
        if (!hasKeyword)
        {
            _upoutFilterDebouncer.Cancel();
            ApplyUpoutFilter();
            return;
        }

        _upoutFilterDebouncer.Schedule(async () =>
            await Dispatcher.UIThread.InvokeAsync(ApplyUpoutFilter));
    }

    private void ApplyUpoutFilter()
    {
        if (_allUpoutRows.Count == 0)
        {
            return;
        }

        var filtered = _allUpoutRows.Where(MatchUpoutFilter).ToList();
        UpoutRows.Clear();
        foreach (var row in filtered)
        {
            UpoutRows.Add(row);
        }

        UpoutTotal = _upoutLastServerTotal;
        UpoutStatus =
            $"第 {UpoutPage} 页 / 本页 {_allUpoutRows.Count} 条 / 筛选后 {filtered.Count} 条 / 服务器总数 {_upoutLastServerTotal}";
    }

    private bool MatchUpoutFilter(MsfxUpoutGridRow row)
    {
        var bill = NormalizeText(UpoutBillCodeKeyword);
        var drug = NormalizeText(UpoutDrugKeyword);
        var ent = NormalizeText(UpoutFromEntKeyword);

        if (!string.IsNullOrWhiteSpace(bill) && !TextSearchHelper.Matches(bill, row.BillCode))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(drug) && !TextSearchHelper.Matches(drug, row.DrugName))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(ent) && !TextSearchHelper.Matches(ent, row.FromEntName))
        {
            return false;
        }

        return true;
    }

    private int GetPageSize()
    {
        if (!int.TryParse(UpoutPageSize, out var pageSize))
        {
            return 20;
        }

        return Math.Clamp(pageSize, 1, 200);
    }

    private int GetSubcodePageSize()
    {
        if (!int.TryParse(SubcodePageSize, out var pageSize))
        {
            return 200;
        }

        return Math.Clamp(pageSize, 20, 2000);
    }

    private int GetPullBatchPageSize()
    {
        if (!int.TryParse(PullBatchPageSize, out var pageSize))
        {
            return 20;
        }

        return Math.Clamp(pageSize, 10, 500);
    }

    private int GetPullBatchEffectivePageSize()
        => IsPullPanelExpanded ? GetPullBatchPageSize() : PullBatchPreviewPageSize;

    private void ApplyPullBatchPage()
    {
        var pageSize = GetPullBatchEffectivePageSize();
        var totalPages = Math.Max(1, (int)Math.Ceiling(PullBatchTotalCount / (double)pageSize));
        if (PullBatchPage > totalPages)
        {
            PullBatchPage = totalPages;
        }

        var page = Math.Max(1, PullBatchPage);
        var skip = (page - 1) * pageSize;
        var rows = _allPullBatchRows.Skip(skip).Take(pageSize).ToList();

        AutoPullBatchRows.ResetContents(rows);
    }

    private int GetMapQueuePageSize()
    {
        if (!int.TryParse(MapQueuePageSize, out var pageSize))
        {
            return 120;
        }

        return Math.Clamp(pageSize, 1, 500);
    }

    private int GetMapQueueQueryPageSize()
        => IsMapPanelExpanded ? GetMapQueuePageSize() : MapQueuePreviewPageSize;

    private void ApplySubCodePage()
    {
        var pageSize = GetSubcodePageSize();
        var totalPages = Math.Max(1, (int)Math.Ceiling(SubcodeTotal / (double)pageSize));
        if (SubcodePage > totalPages)
        {
            SubcodePage = totalPages;
            return;
        }

        var offset = (SubcodePage - 1) * pageSize;
        SubCodeRows.Clear();
        if (offset < _allSubCodeRows.Count)
        {
            var endExclusive = Math.Min(offset + pageSize, _allSubCodeRows.Count);
            for (var i = offset; i < endExclusive; i++)
            {
                SubCodeRows.Add(_allSubCodeRows[i]);
            }
        }

        OnPropertyChanged(nameof(HasSubcodePrevPage));
        OnPropertyChanged(nameof(HasSubcodeNextPage));
    }

    private MsfxApiOptions BuildMsfxOptions()
    {
        var options = _configStore.Load().MsfxApi ?? new MsfxApiOptions();
        if (string.IsNullOrWhiteSpace(options.RefEntId))
        {
            throw new InvalidOperationException("请先在设置页面配置接收企业 RefEntId");
        }

        return options;
    }

    private static string? NormalizeText(string? value)
    {
        var text = (value ?? string.Empty).Trim();
        return text.Length == 0 ? null : text;
    }

    private static string ResolveSearchScope(string? value)
    {
        var text = (value ?? string.Empty).Trim();
        return text switch
        {
            "最小包装码" => "TRACE",
            "单据编码" => "BILL",
            "原始药/规" or "药名/规格(原始)" => "SOURCE_RAW",
            "校正药/规" or "药名/规格(归一化)" => "SOURCE_NORM",
            "层级码" or "层级码(1-5)" => "LEVEL_CODE",
            "映射目标" => "TARGET",
            "原因信息" => "REASON",
            _ => "ALL"
        };
    }

    private static CancellationTokenSource CreateMsfxTimeout(int seconds)
        => new(TimeSpan.FromSeconds(Math.Clamp(seconds, 3, 120)));

    private void SetAutoProgress(double value, string status)
    {
        var v = Math.Clamp(value, 0, 100);
        PostOnUi(() =>
        {
            AutoRunProgressValue = v;
            AutoStatus = status;
        }, DispatcherPriority.Background);
    }

    private void AddAutoLog(string stage, string message, TraceEntryState state)
    {
        var normalizedStage = (stage ?? string.Empty).Trim();
        var normalizedMessage = (message ?? string.Empty).Trim();
        var signature = $"{normalizedStage}|{normalizedMessage}|{state}";
        if (string.Equals(_lastAutoLogSignature, signature, StringComparison.Ordinal))
        {
            return;
        }

        var row = new MsfxAutoLogRow(
            At: DateTime.Now.ToString("HH:mm:ss"),
            Stage: normalizedStage,
            Message: normalizedMessage,
            State: state);

        PostOnUi(() =>
        {
            _lastAutoLogSignature = signature;
            AutoLogs.Insert(0, row);
            while (AutoLogs.Count > AutoLogMaxRows)
            {
                AutoLogs.RemoveAt(AutoLogs.Count - 1);
            }

            if (AutoLogPage != 1)
            {
                AutoLogPage = 1;
            }

            ApplyAutoLogPage();
        }, DispatcherPriority.Background);
    }

    private static string FormatAutoLastRunDisplay(MsfxAutoBoardSnapshot snap)
    {
        if (snap.LastBatchId <= 0)
        {
            return "尚未巡检";
        }

        var status = (snap.LastBatchStatus ?? string.Empty).Trim().ToUpperInvariant();
        if (snap.LastBatchFinishedAt is { } finished)
        {
            var stamp = finished.ToLocalTime().ToString("MM-dd HH:mm");
            return status is "FAILED" ? $"{stamp} 失败" : stamp;
        }

        if (status is "RUNNING" && snap.LastBatchStartedAt is { } runningStarted)
        {
            return $"{runningStarted.ToLocalTime():MM-dd HH:mm} 进行中";
        }

        if (snap.LastBatchStartedAt is { } started)
        {
            return started.ToLocalTime().ToString("MM-dd HH:mm");
        }

        return "尚未巡检";
    }

    private static string? FormatAutoLastRunTip(MsfxAutoBoardSnapshot snap)
    {
        if (snap.LastBatchId <= 0)
        {
            return "暂无自动化巡检记录，执行一次巡检或点击刷新审计后显示最近批次时间";
        }

        var status = string.IsNullOrWhiteSpace(snap.LastBatchStatus) ? "UNKNOWN" : snap.LastBatchStatus.Trim().ToUpperInvariant();
        var started = snap.LastBatchStartedAt?.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss") ?? "--";
        var finished = snap.LastBatchFinishedAt?.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss") ?? "--";
        return $"批次 #{snap.LastBatchId} · {status} · 成功 {snap.LastBatchSuccessCount} / 失败 {snap.LastBatchFailCount}\n开始 {started} · 结束 {finished}";
    }

    private static TraceEntryState ToBatchState(string status)
        => (status ?? string.Empty).Trim().ToUpperInvariant() switch
        {
            "SUCCESS" => TraceEntryState.Success,
            "FAILED" => TraceEntryState.Failed,
            "RUNNING" => TraceEntryState.Warning,
            _ => TraceEntryState.Info
        };

    private static TraceEntryState ToMapState(string status)
        => (status ?? string.Empty).Trim().ToUpperInvariant() switch
        {
            "MAPPED" => TraceEntryState.Success,
            "PENDING" => TraceEntryState.Warning,
            "NEED_REVIEW" => TraceEntryState.ManualReview,
            "FAILED" => TraceEntryState.Failed,
            _ => TraceEntryState.Info
        };

    private static TraceEntryState ToTaskState(string status)
        => (status ?? string.Empty).Trim().ToUpperInvariant() switch
        {
            "SUCCESS" => TraceEntryState.Success,
            "RUNNING" => TraceEntryState.Warning,
            "NEW" => TraceEntryState.Warning,
            "FAILED" => TraceEntryState.Failed,
            "CANCELLED" => TraceEntryState.Failed,
            "DISCARDED" => TraceEntryState.Discarded,
            _ => TraceEntryState.Info
        };

    private static TraceEntryState ParseUpoutState(string? status)
        => (status ?? string.Empty).Trim() switch
        {
            "2" => TraceEntryState.Success,
            "1" => TraceEntryState.Warning,
            "0" => TraceEntryState.Failed,
            _ => TraceEntryState.Info
        };

    private static string BuildApiErrorMessage(MsfxApiCallResult call)
    {
        var parts = new List<string>(4);
        var biz = $"{call.BizCode} {call.BizMessage}".Trim();
        if (!string.IsNullOrWhiteSpace(biz))
        {
            parts.Add(biz);
        }

        if (!string.IsNullOrWhiteSpace(call.Summary))
        {
            parts.Add(call.Summary.Trim());
        }

        if (!string.IsNullOrWhiteSpace(call.RequestId))
        {
            parts.Add($"request_id={call.RequestId.Trim()}");
        }

        if (parts.Count == 0 && !string.IsNullOrWhiteSpace(call.ResponseText))
        {
            var text = call.ResponseText.Trim().Replace("\r", " ").Replace("\n", " ");
            if (text.Length > 220)
            {
                text = text[..220] + "...";
            }

            parts.Add(text);
        }

        return parts.Count == 0 ? "未知错误" : string.Join(" | ", parts);
    }

    private void OnAutoLogsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        OnPropertyChanged(nameof(IsAutoLogsEmpty));
        OnPropertyChanged(nameof(AutoLogTotalPages));
        ApplyAutoLogPage();
    }

    private void OnAutoPullBatchRowsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        => OnPropertyChanged(nameof(IsAutoPullBatchEmpty));

    private void OnAutoMapQueueRowsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        OnPropertyChanged(nameof(IsAutoMapQueueEmpty));
        OnPropertyChanged(nameof(MapQueueDisplayText));
        OnPropertyChanged(nameof(MapQueuePagerStatusText));
    }

    private void OnAutoTaskQueueRowsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        => OnPropertyChanged(nameof(IsAutoTaskQueueEmpty));

    private void OnUpoutRowsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        OnPropertyChanged(nameof(IsUpoutEmpty));
        OnPropertyChanged(nameof(HasUpoutNextPage));
    }

    private void OnSubCodeRowsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        => OnPropertyChanged(nameof(IsSubCodeEmpty));

    public override void Dispose()
    {
        _autoTimer.Stop();
        _autoTimer.Tick -= OnAutoTimerTick;
        AutoLogs.CollectionChanged -= OnAutoLogsCollectionChanged;
        AutoPullBatchRows.CollectionChanged -= OnAutoPullBatchRowsCollectionChanged;
        AutoMapQueueRows.CollectionChanged -= OnAutoMapQueueRowsCollectionChanged;
        AutoTaskQueueRows.CollectionChanged -= OnAutoTaskQueueRowsCollectionChanged;
        UpoutRows.CollectionChanged -= OnUpoutRowsCollectionChanged;
        SubCodeRows.CollectionChanged -= OnSubCodeRowsCollectionChanged;
        _mapQueueSearchDebouncer.Dispose();
        _taskQueueSearchDebouncer.Dispose();
        _upoutFilterDebouncer.Dispose();
        _upoutDateRangeController.Dispose();
        base.Dispose();
    }
}
