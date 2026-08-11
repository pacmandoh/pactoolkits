using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using global::Avalonia.Threading;
using PacToolkits.Application.Abstractions;
using PacToolkits.Application.DTOs;
using PacToolkits.Desktop.Avalonia.Diagnostics;
using PacToolkits.Desktop.Avalonia.Services.Workspace.Refresh;
using PacToolkits.Desktop.Avalonia.Ui.Collections;
using PacToolkits.Desktop.Avalonia.Ui.Formatting;
using PacToolkits.Desktop.Avalonia.Ui.State;
using PacToolkits.Desktop.Avalonia.ViewModels.Support.Catalog;

namespace PacToolkits.Desktop.Avalonia.ViewModels.Pages;

public sealed partial class MsfxLink
{
    private static readonly TimeSpan MappingQuantityTimeout = TimeSpan.FromSeconds(6);
    private const string MappingLogModule = "MsfxMapping";

    private readonly SearchInputDebouncer _mappingKeywordDebouncer = new(350);
    private readonly CancellationTokenSource _mappingLifetimeCts = new();
    private readonly MappingReloadGate _mappingReloadGate = new();
    private IReadOnlyList<OptionItem> _mappingDrugCatalog = [];
    private bool _mappingInitialized;
    private int _mappingDrugInputVersion;
    private int _mappingPreviewVersion;
    private Task? _mappingInputCommitTask;

    public ObservableCollection<MsfxMappingBatchGroupGridRow> MappingGroups { get; } = new();
    public ObservableCollection<OptionItem> MappingDrugOptions { get; } = new();
    public ObservableCollection<OptionItem> MappingSpecOptions { get; } = new();

    [ObservableProperty] private bool _isMappingWorkspace;
    [ObservableProperty] private bool _isMappingConfigBarVisible = true;
    [ObservableProperty] private bool _isMappingBusy;
    [ObservableProperty] private string _mappingKeyword = string.Empty;
    [ObservableProperty] private string _mappingDrugText = string.Empty;
    [ObservableProperty] private OptionItem? _mappingSelectedSpec;
    [ObservableProperty] private string? _mappingQuantityText;
    [ObservableProperty] private string _mappingPreviewText = "请勾选分组后自动预览";

    public bool IsQueueMonitorWorkspace => !IsMappingWorkspace;
    public string QueueWorkspaceToggleText => IsMappingWorkspace ? "队列监控" : "批量映射";
    public string QueueWorkspaceToggleIcon => IsMappingWorkspace ? "ListTree" : "Table2";
    public bool IsMappingGroupEmpty => MappingGroups.Count == 0;
    public bool HasMappingKeyword => !string.IsNullOrWhiteSpace(MappingKeyword);
    public bool IsMappingSpecSelected => MappingSelectedSpec is not null;
    public int MappingSelectedCount => MappingGroups.Count(static row => row.IsSelected);
    public IReadOnlyList<MsfxMappingBatchGroupGridRow> SelectedMappingGroups =>
        MappingGroups.Where(static row => row.IsSelected).ToArray();
    public string MappingQuantityDisplay =>
        string.IsNullOrWhiteSpace(MappingQuantityText) ? "自动" : MappingQuantityText;
    public string MappingGroupCountText => $"共 {MappingGroups.Count} 组";

    partial void OnIsMappingConfigBarVisibleChanged(bool value)
    {
        OnPropertyChanged(nameof(MappingConfigBarToggleIconKind));
        OnPropertyChanged(nameof(MappingConfigBarToggleToolTip));
    }

    // 批量映射 / 弃用任务：弃用路径仍会先写 mapped_drug 再建 DISCARDED 任务，与 APPLY_MAP 同门槛
    private bool CanSubmitMappingGroup()
        => !IsMappingBusy
           && MappingSelectedCount > 0
           && !string.IsNullOrWhiteSpace(NormalizeText(MappingDrugText))
           && !string.IsNullOrWhiteSpace(NormalizeText(MappingSelectedSpec?.Raw));

    private bool CanDiscardMappingGroup()
        => CanSubmitMappingGroup();

    [RelayCommand]
    private async Task ToggleQueueWorkspaceAsync()
    {
        if (IsAutoBoardBusy || IsMappingBusy)
        {
            return;
        }

        if (IsMappingWorkspace)
        {
            IsMappingWorkspace = false;
            return;
        }

        if (IsTaskQueueBatchModeActive)
        {
            _toast.Warn("批量操作", "请先确认或取消当前批量操作");
            return;
        }

        if (!_mappingInitialized)
        {
            await RefreshMappingWorkspaceAsync().ConfigureAwait(false);
        }

        await RunOnUiAsync(() => IsMappingWorkspace = true).ConfigureAwait(false);
    }

    private Task RefreshMappingWorkspaceAsync()
        => RunLocalReloadAsync(
            v => IsMappingBusy = v,
            ReloadMappingWorkspaceCoreAsync);

    [RelayCommand]
    private void ClearMappingSearch()
    {
        _mappingKeywordDebouncer.Cancel();
        MappingKeyword = string.Empty;
    }

    [RelayCommand]
    private Task ApplyMappingDrugCommitAsync()
        => RunMappingInputCommitAsync(async () =>
        {
            var input = NormalizeText(MappingDrugText);
            if (input is null)
            {
                await RunOnUiAsync(ClearMappingTarget);
                await RefreshMappingPreviewSafeAsync(_mappingLifetimeCts.Token).ConfigureAwait(false);
                return;
            }

            var version = Interlocked.Increment(ref _mappingDrugInputVersion);
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(_mappingLifetimeCts.Token);
            cts.CancelAfter(DrugCatalogRefresh.Timeout);
            var (canonical, specs) = await LookupOptions.ResolveDrugAndSpecsAsync(
                _lookup,
                input,
                cts.Token).ConfigureAwait(false);
            if (version != Volatile.Read(ref _mappingDrugInputVersion)
                || string.IsNullOrWhiteSpace(canonical))
            {
                return;
            }

            await RunOnUiAsync(() =>
            {
                MappingDrugText = canonical;
                OptionCollectionHelper.ReplaceRaw(
                    MappingSpecOptions,
                    specs,
                    StringComparison.Ordinal);
                MappingSelectedSpec = MappingSpecOptions.FirstOrDefault();
            }).ConfigureAwait(false);
        });

    [RelayCommand(CanExecute = nameof(CanSubmitMappingGroup))]
    private Task ApplyMappingGroupAsync()
        => ExecuteMappingGroupAsync(discard: false);

    [RelayCommand(CanExecute = nameof(CanDiscardMappingGroup))]
    private Task DiscardMappingGroupAsync()
        => ExecuteMappingGroupAsync(discard: true);

    private bool CanClearMappingDrugSpec()
        => AutoCompleteFilter.HasDrugText(MappingDrugText);

    [RelayCommand(CanExecute = nameof(CanClearMappingDrugSpec))]
    private void ClearMappingDrugSpec()
        => ClearMappingTarget();

    partial void OnIsMappingWorkspaceChanged(bool value)
    {
        OnPropertyChanged(nameof(IsQueueMonitorWorkspace));
        OnPropertyChanged(nameof(ShowQueueSearchHeaderAction));
        OnPropertyChanged(nameof(ShowMappingConfigHeaderAction));
        OnPropertyChanged(nameof(QueueWorkspaceToggleText));
        OnPropertyChanged(nameof(QueueWorkspaceToggleIcon));
        if (!value)
        {
            _mappingKeywordDebouncer.Cancel();
            _mappingReloadGate.Invalidate();
        }
    }

    partial void OnIsMappingBusyChanged(bool value)
    {
        RefreshMappingCommands();
        RefreshOpsUnlockCommands();
        RefreshQueueTabCommand.NotifyCanExecuteChanged();
    }

    public void SyncMappingGroupSelection()
    {
        OnPropertyChanged(nameof(MappingSelectedCount));
        OnPropertyChanged(nameof(SelectedMappingGroups));
        RefreshMappingCommands();
        if (_mappingInitialized)
        {
            ObserveDetached(
                RefreshMappingPreviewSafeAsync(_mappingLifetimeCts.Token),
                "msfx.mapping.preview.detached.fail");
        }
    }

    partial void OnMappingKeywordChanged(string value)
    {
        OnPropertyChanged(nameof(HasMappingKeyword));
        if (!_mappingInitialized)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(value))
        {
            _mappingKeywordDebouncer.Cancel();
            ObserveDetached(
                ReloadMappingGroupsSafeAsync(_mappingLifetimeCts.Token),
                "msfx.mapping.search.detached.fail");
            return;
        }

        _mappingKeywordDebouncer.Schedule(
            () => ReloadMappingGroupsSafeAsync(_mappingLifetimeCts.Token));
    }

    partial void OnMappingDrugTextChanged(string value)
    {
        if (_mappingDrugCatalog.Count > 0)
        {
            AutoCompleteFilter.RefreshVisibleOptions(
                MappingDrugOptions,
                _mappingDrugCatalog,
                value);
        }

        if (string.IsNullOrWhiteSpace(value))
        {
            MappingSpecOptions.Clear();
            MappingSelectedSpec = null;
            MappingQuantityText = null;
        }

        ClearMappingDrugSpecCommand.NotifyCanExecuteChanged();
        RefreshMappingCommands();
    }

    partial void OnMappingSelectedSpecChanged(OptionItem? value)
    {
        OnPropertyChanged(nameof(IsMappingSpecSelected));
        RefreshMappingCommands();
        if (_mappingInitialized)
        {
            ObserveDetached(
                RefreshMappingTargetAsync(_mappingLifetimeCts.Token),
                "msfx.mapping.target.detached.fail");
        }
    }

    partial void OnMappingQuantityTextChanged(string? value)
        => OnPropertyChanged(nameof(MappingQuantityDisplay));

    private async Task ReloadMappingWorkspaceCoreAsync(CancellationToken ct)
    {
        if (!_mappingInitialized)
        {
            await Task.WhenAll(
                ReloadMappingGroupsAsync(ct),
                RefreshDrugCatalogAsync(ct)).ConfigureAwait(false);
            _mappingInitialized = true;
            await RefreshMappingPreviewSafeAsync(ct).ConfigureAwait(false);
            return;
        }

        await ReloadMappingGroupsAsync(ct).ConfigureAwait(false);
    }

    private async Task ReloadMappingGroupsSafeAsync(CancellationToken ct)
    {
        try
        {
            await ReloadMappingGroupsAsync(ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            AppLog.Warn(
                MappingLogModule,
                "msfx.mapping.groups.reload_fail",
                "Failed to reload mapping groups",
                ex);
            if (CanToastError(ex))
            {
                _toast.Error("批量映射", ex.Message);
            }
        }
    }

    private async Task ReloadMappingGroupsAsync(CancellationToken ct)
    {
        var epoch = _mappingReloadGate.BeginReload();
        var groups = await _syncService.GetMappingBatchGroupsAsync(
            mapStatus: null,
            codeStatus: null,
            searchScope: "ALL",
            keyword: NormalizeText(MappingKeyword),
            limit: 500,
            ct: ct).ConfigureAwait(false);
        if (ct.IsCancellationRequested || !_mappingReloadGate.IsCurrent(epoch))
        {
            return;
        }

        await RunOnUiAsync(() => ReplaceMappingGroups(groups)).ConfigureAwait(false);
        await RefreshMappingPreviewSafeAsync(ct).ConfigureAwait(false);
    }

    private void ReplaceMappingGroups(IReadOnlyList<MsfxMappingBatchGroupRow> groups)
    {
        var selectedKeys = MappingGroups
            .Where(static row => row.IsSelected)
            .Select(MappingGroupKey)
            .ToHashSet(StringComparer.Ordinal);

        MappingGroups.Clear();
        foreach (var group in groups)
        {
            var row = new MsfxMappingBatchGroupGridRow(group);
            row.IsSelected = selectedKeys.Contains(MappingGroupKey(row));
            MappingGroups.Add(row);
        }

        OnPropertyChanged(nameof(IsMappingGroupEmpty));
        OnPropertyChanged(nameof(MappingGroupCountText));
        SyncMappingGroupSelection();
    }

    public void ReloadAfterDrugIndexChange()
    {
        if (!_mappingInitialized)
        {
            return;
        }

        PostOnUi(
            () => ObserveDetached(
                RefreshDrugCatalogAsync(_mappingLifetimeCts.Token, forceRefresh: true),
                "msfx.mapping.catalog.reload.detached.fail"),
            DispatcherPriority.Background);
    }

    private async Task RefreshDrugCatalogAsync(CancellationToken ct, bool forceRefresh = false)
    {
        if (IsLookupCatalogSuspended())
        {
            await RunOnUiAsync(() =>
            {
                _mappingDrugCatalog = [];
                MappingDrugOptions.Clear();
                ClearMappingTarget();
            }).ConfigureAwait(false);
            return;
        }

        try
        {
            var catalog = await DrugCatalogRefresh.LoadAsync(_lookup, forceRefresh, ct)
                .ConfigureAwait(false);
            await RunOnUiAsync(() =>
            {
                _mappingDrugCatalog = catalog;
                AutoCompleteFilter.RefreshVisibleOptions(
                    MappingDrugOptions,
                    _mappingDrugCatalog,
                    MappingDrugText);

                if (DrugCatalogRefresh.IsMissing(catalog, NormalizeText(MappingDrugText)))
                {
                    ClearMappingTarget();
                }
            }).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            LogWarn(
                "msfx.mapping.catalog.reload_fail",
                "Failed to refresh mapping drug catalog",
                ex);
        }
    }

    private async Task RefreshMappingTargetAsync(CancellationToken ct)
    {
        var drug = NormalizeText(MappingDrugText);
        var spec = NormalizeText(MappingSelectedSpec?.Raw);
        if (drug is null || spec is null)
        {
            await RunOnUiAsync(() => MappingQuantityText = null).ConfigureAwait(false);
            await RefreshMappingPreviewSafeAsync(ct).ConfigureAwait(false);
            return;
        }

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(MappingQuantityTimeout);
        var quantity = await _lookup.GetQtyAsync(drug, spec, cts.Token).ConfigureAwait(false);
        await RunOnUiAsync(() => MappingQuantityText = quantity?.ToString()).ConfigureAwait(false);
        await RefreshMappingPreviewSafeAsync(ct).ConfigureAwait(false);
    }

    private async Task RefreshMappingPreviewSafeAsync(CancellationToken ct)
    {
        try
        {
            await RefreshMappingPreviewAsync(ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            AppLog.Warn(
                MappingLogModule,
                "msfx.mapping.preview_fail",
                "Failed to refresh mapping preview",
                ex);
        }
    }

    private async Task RefreshMappingPreviewAsync(CancellationToken ct)
    {
        var version = Interlocked.Increment(ref _mappingPreviewVersion);
        var groups = SelectedMappingGroups;
        if (groups.Count == 0)
        {
            await RunOnUiAsync(() => MappingPreviewText = "请勾选分组后自动预览").ConfigureAwait(false);
            return;
        }

        var preview = await PreviewMappingGroupsAsync(
            groups,
            "APPLY_MAP",
            NormalizeText(MappingDrugText),
            NormalizeText(MappingSelectedSpec?.Raw),
            ct).ConfigureAwait(false);
        if (ct.IsCancellationRequested || version != Volatile.Read(ref _mappingPreviewVersion))
        {
            return;
        }

        await RunOnUiAsync(() =>
        {
            MappingPreviewText =
                $"将影响 {preview.CandidateCount} 条，可执行 {preview.EligibleCount} 条，阻塞 {preview.BlockedCount} 条";
        }).ConfigureAwait(false);
    }

    private async Task ExecuteMappingGroupAsync(bool discard)
    {
        await FlushMappingInputAsync().ConfigureAwait(false);
        var groups = SelectedMappingGroups;
        var drug = NormalizeText(MappingDrugText);
        var spec = NormalizeText(MappingSelectedSpec?.Raw);
        var scene = discard ? "批量弃用" : "批量映射";
        if (groups.Count == 0)
        {
            _toast.Warn(scene, "请先在分组表中勾选记录");
            return;
        }

        if (drug is null || spec is null)
        {
            _toast.Warn(scene, "需要填写需映射的药品信息和规格信息");
            return;
        }

        await RunOnUiAsync(() => IsMappingBusy = true).ConfigureAwait(false);
        EnterManualMsfxWrite();
        try
        {
            var action = discard ? "APPLY_DISCARD" : "APPLY_MAP";
            var preview = await PreviewMappingGroupsAsync(
                groups,
                action,
                drug,
                spec,
                _mappingLifetimeCts.Token).ConfigureAwait(false);
            if (preview.EligibleCount <= 0)
            {
                _toast.Warn(scene, $"无可执行记录，将影响 {preview.CandidateCount} 条，阻塞 {preview.BlockedCount} 条");
                return;
            }

            var groupLabel = groups.Count == 1
                ? $"分组“{DrugLabel.Format(groups[0].SourceDrugNameRaw, groups[0].SourceSpecRaw)}”"
                : $"已勾选的 {groups.Count} 个分组";
            var confirmMessage = discard
                ? $"{groupLabel}将影响 {preview.CandidateCount} 条，可执行 {preview.EligibleCount} 条，确认弃用任务？"
                : $"{groupLabel}将影响 {preview.CandidateCount} 条，可执行 {preview.EligibleCount} 条，确认批量映射？";
            var confirmed = discard
                ? await _dialog.ConfirmDestructive(scene, confirmMessage).ConfigureAwait(false)
                : await _dialog.Confirm(scene, confirmMessage).ConfigureAwait(false);
            if (!confirmed)
            {
                return;
            }

            var operation = discard
                ? SensitiveOpKind.MsfxDiscard
                : SensitiveOpKind.MsfxMappingApply;
            var reason = discard
                ? "manual batch discard from mapping workspace"
                : "manual batch mapping apply from mapping workspace";
            if (!await RequireUnlockAsync(
                    operation,
                    scene,
                    groups.Count == 1
                        ? $"{groups[0].SourceDrugNameRaw}/{groups[0].SourceSpecRaw}"
                        : $"{groups.Count} mapping groups",
                    reason).ConfigureAwait(false))
            {
                return;
            }

            var affectedCount = 0;
            foreach (var group in groups)
            {
                var result = await _syncService.ApplyMappingBatchByGroupAsync(
                    mapStatus: null,
                    codeStatus: null,
                    searchScope: "ALL",
                    keyword: null,
                    groupSourceDrugNameRaw: group.SourceDrugNameRaw,
                    groupSourceSpecRaw: group.SourceSpecRaw,
                    groupSourceNameNorm: group.SourceNameNorm,
                    groupSourceSpecNorm: group.SourceSpecNorm,
                    action: action,
                    drugId: drug,
                    spec: spec,
                    ct: _mappingLifetimeCts.Token).ConfigureAwait(false);
                affectedCount += result.AffectedCount;
            }

            if (affectedCount <= 0)
            {
                _toast.Warn(scene, "本次未更新任何记录，请检查筛选条件或映射目标");
                return;
            }

            if (discard)
            {
                AddAutoLog(scene, $"分组弃用 {affectedCount} 条，已进入弃用任务队列", TraceEntryState.Discarded);
                LogInfo("msfx.map.batch.discard", "MSFX batch mapping discarded into task queue", new
                {
                    AffectedCount = affectedCount,
                    GroupCount = groups.Count,
                    DrugId = drug,
                    Spec = spec
                });
                _toast.Success(scene, $"已处理 {affectedCount} 条，并直接进入弃用任务队列");
            }
            else
            {
                var built = await _syncService.BuildInjectsAsync(
                    500,
                    _mappingLifetimeCts.Token).ConfigureAwait(false);
                AddAutoLog(scene, $"分组处理 {affectedCount} 条，新增任务 {built.CreatedTasks}", TraceEntryState.Success);
                LogInfo("msfx.map.batch.apply", "MSFX batch mapping applied", new
                {
                    AffectedCount = affectedCount,
                    built.CreatedTasks,
                    GroupCount = groups.Count,
                    DrugId = drug,
                    Spec = spec
                });
                _toast.Success(scene, $"已处理 {affectedCount} 条，新增任务 {built.CreatedTasks}");
            }

            await RunOnUiAsync(ClearMappingTarget).ConfigureAwait(false);

            // 单次队列 Tab 重载：映射分组与监控板（含任务队列）；勿拆成两路 RunLocalReload 互抢
            await RunLocalReloadAsync(_ => { }, ReloadQueueTabCoreAsync).ConfigureAwait(false);
            if (WorkspacePageRefresh.RefreshSucceeded(this))
            {
                _dirtyRefresh.Clear(this);
            }
        }
        catch (OperationCanceledException) when (_mappingLifetimeCts.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _toast.Error(scene, ex.Message);
        }
        finally
        {
            // 先清 mapping busy，再 Exit：TryRefreshIfDirty 走 RefreshQueueTab，CanExecute 依赖 !IsMappingBusy
            await RunOnUiAsync(() => IsMappingBusy = false).ConfigureAwait(false);
            ExitManualMsfxWrite();
        }
    }

    private async Task RunMappingInputCommitAsync(Func<Task> action)
    {
        var task = action();
        _mappingInputCommitTask = task;
        try
        {
            await task.ConfigureAwait(false);
        }
        finally
        {
            if (ReferenceEquals(_mappingInputCommitTask, task))
            {
                _mappingInputCommitTask = null;
            }
        }
    }

    private async Task FlushMappingInputAsync()
    {
        if (_mappingInputCommitTask is { } pending)
        {
            await pending.ConfigureAwait(false);
        }
    }

    private void ClearMappingTarget()
    {
        MappingDrugText = string.Empty;
        MappingSpecOptions.Clear();
        MappingSelectedSpec = null;
        MappingQuantityText = null;
    }

    private void RefreshMappingCommands()
        => RefreshCommands(
            ApplyMappingGroupCommand,
            DiscardMappingGroupCommand,
            ClearMappingDrugSpecCommand);

    private async Task<MsfxMappingBatchPreview> PreviewMappingGroupsAsync(
        IReadOnlyList<MsfxMappingBatchGroupGridRow> groups,
        string action,
        string? drug,
        string? spec,
        CancellationToken ct)
    {
        var candidateCount = 0;
        var eligibleCount = 0;
        var blockedCount = 0;
        foreach (var group in groups)
        {
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
                drugId: drug,
                spec: spec,
                ct: ct).ConfigureAwait(false);
            candidateCount += preview.CandidateCount;
            eligibleCount += preview.EligibleCount;
            blockedCount += preview.BlockedCount;
        }

        return new MsfxMappingBatchPreview(candidateCount, eligibleCount, blockedCount);
    }

    private static string MappingGroupKey(MsfxMappingBatchGroupGridRow row)
        => $"{row.SourceDrugNameRaw}|{row.SourceSpecRaw}|{row.SourceNameNorm}|{row.SourceSpecNorm}";

    protected override void OnLookupCatalogSuspended()
    {
        _mappingDrugCatalog = [];
        MappingDrugOptions.Clear();
        ClearMappingTarget();
    }

    private void DisposeMappingWorkspace()
    {
        _mappingKeywordDebouncer.Dispose();
        _mappingReloadGate.Invalidate();
        _mappingLifetimeCts.Cancel();
        _mappingLifetimeCts.Dispose();
    }

    internal sealed class MappingReloadGate
    {
        private int _epoch;

        public int BeginReload() => Interlocked.Increment(ref _epoch);

        public void Invalidate() => Interlocked.Increment(ref _epoch);

        public bool IsCurrent(int epoch) => epoch == Volatile.Read(ref _epoch);
    }
}
