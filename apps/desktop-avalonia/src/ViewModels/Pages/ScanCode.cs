using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
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

namespace PacToolkits.Desktop.Avalonia.ViewModels.Pages;

public sealed partial class ScanCode : AppPageBase
{
    private static readonly TimeSpan LookupTimeout = TimeSpan.FromSeconds(8);
    private static readonly TimeSpan QtyLookupTimeout = TimeSpan.FromSeconds(6);
    private static readonly TimeSpan SubmitTimeout = TimeSpan.FromSeconds(20);
    private static readonly Lazy<string> CachedClientRaw = new(BuildClientRaw);

    public override string DisplayName => "追溯码录入";
    public override string Icon => "ScanBarcode";
    public override int Index => 3;
    protected override bool AutoRefreshOnDbDisconnected => true;
    protected override bool AutoRefreshOnDbReconnected => true;

    private readonly ILookupCatalogService _lookup;
    private readonly IScanCodeService _scanCode;
    private readonly ITraceCodeRuleService _traceCodeRule;
    private readonly IToastService _toast;
    private IRelayCommand?[]? _notifiableCommands;

    public ObservableCollection<OptionItem> DrugOptions { get; } = new();
    public ObservableCollection<OptionItem> SpecOptions { get; } = new();
    private IReadOnlyList<OptionItem> _drugCatalog = [];
    public ObservableCollection<AutoFetchTaskItem> AutoTasks { get; } = new();
    public ObservableCollection<AutoFetchRunItem> RecentRuns { get; } = new();
    public ObservableCollection<AutoFetchRetryItem> RetryQueue { get; } = new();

    private readonly HashSet<string> _existingPoolCodes = new(StringComparer.Ordinal);
    private CancellationTokenSource? _poolCheckCts;
    private string _poolCheckInFlightKey = string.Empty;
    private string _lastCompletedPoolCheckKey = string.Empty;
    private static readonly TimeSpan PoolCheckDebounce = TimeSpan.FromMilliseconds(450);
    private readonly SearchInputDebouncer _statsDebouncer = new(300);
    private TraceCodeLineKind[] _lineKinds = [];
    private TraceCodeDetailedAnalysis _cachedDetailed =
        new(0, 0, 0, 0, 0, Array.Empty<string>());
    protected override void OnLookupCatalogSuspended()
    {
        DrugOptions.Clear();
        _drugCatalog = [];
        SpecOptions.Clear();
        IsDrugSuggestOpen = false;
        DrugText = null;
        SelectedSpec = null;
        SelectedQtyText = null;
        IsSpecSelected = false;
    }

    public int ActiveAutoTaskCount => IsAutoFetchRunning ? 1 : 0;

    public string AutoFetchSuccessRateText
    {
        get
        {
            var completedCount = RecentRuns.Count(item =>
                item.State is AutoFetchState.Succeeded or AutoFetchState.Failed);
            if (completedCount == 0)
            {
                return "—";
            }

            var successCount = RecentRuns.Count(item =>
                item.State == AutoFetchState.Succeeded);
            return $"{Math.Round(successCount * 100.0 / completedCount):F0}%";
        }
    }

    public string AutoFetchNextRunText
        => IsAutoFetchEnabled ? "下一轮：预计 2 分钟内" : "启用后开始调度";

    public bool IsTraceCodeInputEnabled =>
        CanOperateUi()
        && !string.IsNullOrWhiteSpace(NormalizeInput(DrugText))
        && SelectedSpec is not null
        && !string.IsNullOrWhiteSpace(SelectedQtyText);

    public bool IsTraceCodeInputBlocked => !IsTraceCodeInputEnabled;

    public bool IsEntryContextEmpty => IsTraceCodeInputBlocked;

    public string SelectedDrugDisplay =>
        string.IsNullOrWhiteSpace(NormalizeInput(DrugText)) ? "未选择" : NormalizeInput(DrugText)!;

    public string SelectedSpecDisplay => SelectedSpec?.Display ?? "未选择";

    public string SelectedQtyDisplay =>
        string.IsNullOrWhiteSpace(SelectedQtyText) ? "—" : SelectedQtyText!;

    public int TraceCodeRuleLength => _traceCodeRule.Current.RequiredLength;

    public string TraceCodeRulePattern => _traceCodeRule.Current.Pattern;

    public double ValidRatePct =>
        TotalCodeCount > 0 ? Math.Round(ValidCodeCount * 100.0 / TotalCodeCount, 1) : 0;

    public bool ShowValidRate => TotalCodeCount > 0;

    public IReadOnlyList<TraceCodeLineKind> LineKinds => _lineKinds;

    public string EntryEmptyText => "请先完成药品选择";

    public string EntryEmptyHint => "在上方筛选栏选择药品与规格后，即可开始批量录入追溯码";

    [ObservableProperty] private int _selectedTabIndex;

    public bool IsAutoFetchTab => SelectedTabIndex == 1;

    public bool IsManualEntryTab => SelectedTabIndex == 0;

    partial void OnSelectedTabIndexChanged(int value)
    {
        OnPropertyChanged(nameof(IsAutoFetchTab));
        OnPropertyChanged(nameof(IsManualEntryTab));
    }

    [ObservableProperty] private string? _drugText;
    [ObservableProperty] private OptionItem? _selectedSpec;
    [ObservableProperty] private string _traceCodesText = string.Empty;
    [ObservableProperty] private bool _isDrugSuggestOpen;
    [ObservableProperty] private string? _selectedQtyText;
    [ObservableProperty] private bool _isSpecSelected;
    [ObservableProperty] private int _totalCodeCount;
    [ObservableProperty] private int _validCodeCount;
    [ObservableProperty] private int _duplicateCodeCount;
    [ObservableProperty] private int _poolSkipCount;
    [ObservableProperty] private int _invalidCodeCount;
    [ObservableProperty] private TraceCodeHighlightFilter _highlightFilter;
    [ObservableProperty] private bool _isAutoFetchEnabled;
    [ObservableProperty] private bool _isAutoFetchRunning;
    [ObservableProperty] private int _contextStatusLevel;
    [ObservableProperty] private string _status = "请选择药品与规格";

    public ScanCode(
        ILookupCatalogService lookup,
        IScanCodeService scanCode,
        ITraceCodeRuleService traceCodeRule,
        IToastService toast)
    {
        _lookup = lookup;
        _scanCode = scanCode;
        _traceCodeRule = traceCodeRule;
        _toast = toast;
        RecentRuns.CollectionChanged += OnRecentRunsChanged;
        RetryQueue.CollectionChanged += OnRetryQueueChanged;
        SeedAutoFetchPanel();
        _traceCodeRule.Changed += OnTraceCodeRuleChanged;

        PostOnUi(() => ObserveDetached(ReloadAsync(), "reload.detached.fail"), DispatcherPriority.Background);
    }

    private Task ReloadAsync() => RefreshPageAsync();

    protected override Task ReloadCoreAsync(CancellationToken ct)
        => ReloadLookupAsync(ct);

    protected override void OnReloadFinished()
    {
        ReschedulePoolCheckAfterReload();
        RefreshPageCommands();
    }

    private void ReschedulePoolCheckAfterReload()
    {
        if (string.IsNullOrWhiteSpace(TraceCodesText))
        {
            return;
        }

        _existingPoolCodes.Clear();
        _poolCheckInFlightKey = string.Empty;
        _lastCompletedPoolCheckKey = string.Empty;
        _poolCheckCts?.Cancel();
        RecalcCodeStats(TraceCodesText);
    }

    private bool CanOperateUi() => !IsBusy;

    public void ReloadAfterDrugIndexChange()
    {
        PostOnUi(async () =>
        {
            try
            {
                if (IsLookupCatalogSuspended())
                {
                    OnLookupCatalogSuspended();
                    return;
                }

                using var cts = new CancellationTokenSource(LookupTimeout);
                var drugs = await LookupOptions.GetDrugOptionsAsync(
                    _lookup,
                    cts.Token,
                    forceRefresh: true).ConfigureAwait(false);
                await RunOnUiAsync(() =>
                {
                    var currentDrug = NormalizeInput(DrugText);
                    _drugCatalog = drugs;
                    AutoCompleteFilter.RefreshVisibleOptions(DrugOptions, _drugCatalog, DrugText);

                    if (string.IsNullOrWhiteSpace(currentDrug))
                    {
                        return;
                    }

                    var stillExists = _drugCatalog.Any(x =>
                        string.Equals(x.Raw, currentDrug, StringComparison.OrdinalIgnoreCase));
                    if (!stillExists)
                    {
                        DrugText = null;
                        SpecOptions.Clear();
                        SelectedSpec = null;
                        SelectedQtyText = null;
                        IsSpecSelected = false;
                        UpdateStatus("当前药品已不存在，请重新选择", 2);
                    }
                }, DispatcherPriority.Background);
            }
            catch (System.Exception ex)
            {
                LogWarn("scan.notify_drug_index.fail", "Failed to refresh drug options after drug-index change", ex);
            }
        }, DispatcherPriority.Background);
    }

    public async Task PrefillFromInventoryAsync(string? drugId, string? spec)
    {
        var drug = NormalizeInput(drugId);
        if (string.IsNullOrWhiteSpace(drug))
        {
            return;
        }

        await RunOnUiAsync(() =>
        {
            SelectedTabIndex = 0;
            DrugText = drug;
            IsDrugSuggestOpen = false;
        }, DispatcherPriority.Background);

        using var specCts = new CancellationTokenSource(LookupTimeout);
        await ReloadSpecsByDrugAsync(drug, specCts.Token).ConfigureAwait(false);

        var specText = NormalizeInput(spec);
        if (!string.IsNullOrWhiteSpace(specText))
        {
            await RunOnUiAsync(() =>
            {
                var hit = SpecOptions.FirstOrDefault(x =>
                    string.Equals(NormalizeInput(x.Raw), specText, StringComparison.OrdinalIgnoreCase));
                if (hit is not null)
                {
                    SelectedSpec = hit;
                }
            }, DispatcherPriority.Background);

            try
            {
                using var qtyCts = new CancellationTokenSource(QtyLookupTimeout);
                var qty = await _lookup.GetQtyAsync(drug, specText, qtyCts.Token, forceRefresh: true).ConfigureAwait(false);
                await RunOnUiAsync(() =>
                {
                    SelectedQtyText = qty?.ToString();
                }, DispatcherPriority.Background);
            }
            catch (System.Exception ex)
            {
                LogWarn("scan.prefill.qty_read.fail", "Failed to read qty during prefill", ex);
            }
        }

        await RefreshQtyAndContextStatusAsync().ConfigureAwait(false);
    }

    partial void OnDrugTextChanged(string? value)
    {
        RefreshDrugOptionsOrder(value);

        var drug = NormalizeInput(value);
        IsDrugSuggestOpen = !string.IsNullOrWhiteSpace(drug);

        if (string.IsNullOrWhiteSpace(drug))
        {
            SpecOptions.Clear();
            SelectedSpec = null;
            SelectedQtyText = null;
            IsSpecSelected = false;
            UpdateStatus(_drugCatalog.Count == 0
                ? "drug_index 为空，请先维护药品信息"
                : "请选择药品与规格", 0);
        }

        RefreshPageCommands();
    }

    private void RefreshDrugOptionsOrder(string? searchText)
    {
        if (_drugCatalog.Count == 0)
        {
            return;
        }

        AutoCompleteFilter.RefreshVisibleOptions(DrugOptions, _drugCatalog, searchText);
    }

    partial void OnSelectedQtyTextChanged(string? value)
        => RefreshPageCommands();

    partial void OnSelectedSpecChanged(OptionItem? value)
    {
        IsSpecSelected = value is not null;
        ObserveDetached(RefreshQtyAndContextStatusAsync(), "qty.refresh.detached.fail");
        RefreshPageCommands();
    }

    partial void OnTraceCodesTextChanged(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            RecalcCodeStats(value);
            RefreshCodeCommands();
            return;
        }

        _statsDebouncer.Schedule(RecalcCodeStatsDebounced);
    }

    partial void OnStatusChanged(string value)
        => RefreshEntryPresentation();

    [RelayCommand]
    private async Task ApplyDrugFilterAsync()
    {
        if (SkipTrigger())
        {
            return;
        }

        IsDrugSuggestOpen = false;

        var drug = NormalizeInput(DrugText);
        if (string.IsNullOrWhiteSpace(drug))
        {
            SpecOptions.Clear();
            SelectedSpec = null;
            SelectedQtyText = null;
            UpdateStatus("请输入药品名", 0);
            return;
        }

        try
        {
            using var cts = new CancellationTokenSource(LookupTimeout);
            var canonicalDrug = await _lookup.ResolveCanonicalDrugIdAsync(drug, cts.Token).ConfigureAwait(false);

            if (string.IsNullOrWhiteSpace(canonicalDrug))
            {
                var isDeprecated = await _lookup.IsDeprecatedDrugIdAsync(drug, cts.Token).ConfigureAwait(false);
                await RunOnUiAsync(() =>
                {
                    SpecOptions.Clear();
                    SelectedSpec = null;
                    SelectedQtyText = null;
                    IsSpecSelected = false;
                    UpdateStatus(isDeprecated ? "药品已被弃用" : "药品信息中无该药品", 2);
                });
                return;
            }

            DrugText = canonicalDrug;
            drug = canonicalDrug;
            await ReloadSpecsByDrugAsync(drug, cts.Token).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            LogError("scan.specs.load_fail", "Failed to load specs for selected drug", ex);
            await RunOnUiAsync(() =>
            {
                UpdateStatus($"加载规格失败：{ex.Message}", 2);
                if (CanToastError(ex))
                {
                    _toast.Error("规格加载失败", ex.Message);
                }
            });
        }
    }

    private bool CanSubmit()
        => CanOperateUi()
           && !string.IsNullOrWhiteSpace(NormalizeInput(DrugText))
           && SelectedSpec is not null
           && !string.IsNullOrWhiteSpace(SelectedQtyText)
           && ValidCodeCount > 0;

    private bool CanClearDrugSpecFilter()
        => CanOperateUi() && AutoCompleteFilter.HasDrugText(DrugText);

    [RelayCommand(CanExecute = nameof(CanClearDrugSpecFilter))]
    private void ClearDrugSpecFilter()
    {
        if (SkipTrigger())
        {
            return;
        }

        IsDrugSuggestOpen = false;
        DrugText = null;
        SpecOptions.Clear();
        SelectedSpec = null;
        SelectedQtyText = null;
        IsSpecSelected = false;
        UpdateStatus(DrugOptions.Count == 0
            ? "药品信息为空，请先维护药品信息"
            : "请选择药品与规格", 0);
        RefreshPageCommands();
    }

    [RelayCommand(CanExecute = nameof(CanSubmit))]
    private async Task SubmitAsync()
    {
        if (SkipTrigger())
        {
            return;
        }

        try
        {
            await RunReloadAsync(async ct =>
            {
                try
                {
                    var drug = NormalizeInput(DrugText);
                    var spec = NormalizeInput(SelectedSpec?.Raw);
                    _statsDebouncer.Cancel();
                    await EnsurePoolCheckAsync(ct).ConfigureAwait(false);
                    CodeAnalysis analysis = default!;
                    await RunOnUiAsync(() => analysis = ToCodeAnalysis(_cachedDetailed)).ConfigureAwait(false);
                    var codes = analysis.ValidUniqueCodes;

                    if (string.IsNullOrWhiteSpace(drug) || string.IsNullOrWhiteSpace(spec))
                    {
                        _toast.Warn("追溯码录入", "请先选择药品与规格");
                        return;
                    }

                    if (codes.Count == 0)
                    {
                        _toast.Warn("追溯码录入", "未检测到可入库的有效追溯码");
                        return;
                    }

                    using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    timeoutCts.CancelAfter(SubmitTimeout);

                    var submit = await _scanCode.SubmitAsync(
                        new ScanCodeSubmitRequest(
                            DrugId: drug,
                            Spec: spec,
                            ValidUniqueCodes: codes,
                            Analysis: analysis,
                            ClientRaw: CachedClientRaw.Value),
                        timeoutCts.Token).ConfigureAwait(false);

                    if (!submit.DrugFound)
                    {
                        await RunOnUiAsync(() =>
                        {
                            _toast.Error("追溯码录入", "药品/规格不存在，请检查选择项");
                            Status = "录入失败：药品/规格不存在";
                        });
                        return;
                    }

                    Exception? logWriteError = submit.EntryMessage.StartsWith("insert ok but log failed", StringComparison.Ordinal)
                        ? new InvalidOperationException(submit.EntryMessage)
                        : null;
                    if (logWriteError is not null)
                    {
                        LogWarn("scan.entry_log.write_fail", "trace_entry_log write failed after submit", logWriteError);
                    }

                    var result = submit.Insert;

                    await RunOnUiAsync(() =>
                    {
                        SelectedQtyText = submit.QtyPerTrace.ToString();
                        Status = $"处理 {result.RequestedCount} 条，成功 {result.InsertedCount} 条，跳过 {result.SkippedCount} 条";
                        var summary = BuildSubmitToastSummary(
                            drug,
                            spec,
                            submit.QtyPerTrace,
                            analysis,
                            PoolSkipCount,
                            result);

                        if (logWriteError is not null)
                        {
                            Status = $"录入成功，但日志写入失败：{logWriteError.Message}";
                            _toast.Error("录入日志", $"trace_entry_log 写入失败：{logWriteError.Message}");
                        }
                        else if (result.InsertedCount > 0)
                        {
                            _toast.Success("追溯码录入", summary);
                        }
                        else
                        {
                            _toast.Warn("追溯码录入 · 无新增记录", summary);
                        }
                    });
                }
                finally
                {
                    await RunOnUiAsync(() => { TraceCodesText = string.Empty; }).ConfigureAwait(false);
                }
            }, onFinished: RefreshPageCommands);
        }
        catch (Exception ex)
        {
            LogError("scan.submit.fail", "Submit trace codes failed", ex);
            await RunOnUiAsync(() =>
            {
                Status = $"录入失败：{ex.Message}";
                if (CanToastError(ex))
                {
                    _toast.Error("追溯码录入失败", ex.Message);
                }
            });
        }
    }

    private bool CanClearCodes()
        => CanOperateUi() && !string.IsNullOrWhiteSpace(TraceCodesText);

    [RelayCommand]
    private void SelectHighlight(string? key)
    {
        var next = HighlightFilterKeys.FromKey(key);
        HighlightFilter = HighlightFilter == next ? TraceCodeHighlightFilter.None : next;
    }

    private void RecalcCodeStatsDebounced()
    {
        Dispatcher.UIThread.Post(() =>
        {
            RecalcCodeStats(TraceCodesText);
            RefreshCodeCommands();
        });
    }

    private void RefreshCodeCommands()
    {
        RefreshCommands([SubmitCommand, ClearCodesCommand]);
        OnPropertyChanged(nameof(ValidRatePct));
        OnPropertyChanged(nameof(ShowValidRate));
    }

    [RelayCommand(CanExecute = nameof(CanClearCodes))]
    private void ClearCodes()
    {
        if (SkipTrigger())
        {
            return;
        }

        TraceCodesText = string.Empty;
        HighlightFilter = TraceCodeHighlightFilter.None;
        _statsDebouncer.Cancel();
        _existingPoolCodes.Clear();
        _poolCheckInFlightKey = string.Empty;
        _lastCompletedPoolCheckKey = string.Empty;
        _poolCheckCts?.Cancel();
        SetLineKinds([]);
        Status = "已清空输入框";
        RefreshCodeCommands();
    }

    [RelayCommand]
    private void RequireDrugSpec()
    {
        if (SkipTrigger())
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(NormalizeInput(DrugText))
            && SelectedSpec is not null
            && !string.IsNullOrWhiteSpace(SelectedQtyText))
        {
            return;
        }

        _toast.Warn("追溯码录入", "请先选择药品、规格并确认单条数量");
    }

    [RelayCommand(CanExecute = nameof(CanStartAutoFetch))]
    private void StartAutoFetch()
    {
        if (SkipTrigger())
        {
            return;
        }

        if (!IsAutoFetchEnabled)
        {
            _toast.Warn("自动拉取", "请先启用自动拉取开关");
            return;
        }

        if (IsAutoFetchRunning)
        {
            _toast.Info("自动拉取", "任务已在运行中");
            return;
        }

        IsAutoFetchRunning = true;
        SetAutoTaskState("增量追踪任务", AutoFetchState.Running);
        _toast.Info("自动拉取", "已启动样板任务");

        AddRecentRun(new AutoFetchRunItem(
            Name: "增量追踪任务",
            StartedAtText: NowText(),
            State: AutoFetchState.Running));
    }

    [RelayCommand]
    private void OpenAutoFetchSettings()
    {
        if (SkipTrigger())
        {
            return;
        }

        _toast.Info("自动拉取", "参数入口将在执行器接入时启用");
    }

    partial void OnIsAutoFetchEnabledChanged(bool value)
    {
        var stoppedRunning = !value && IsAutoFetchRunning;
        if (!value && IsAutoFetchRunning)
        {
            IsAutoFetchRunning = false;
            SetAutoTaskState("增量追踪任务", AutoFetchState.Paused);
        }

        OnPropertyChanged(nameof(AutoFetchNextRunText));
        if (!stoppedRunning)
        {
            StartAutoFetchCommand.NotifyCanExecuteChanged();
        }
    }

    partial void OnIsAutoFetchRunningChanged(bool value)
    {
        OnPropertyChanged(nameof(ActiveAutoTaskCount));
        StartAutoFetchCommand.NotifyCanExecuteChanged();
        StopAutoFetchCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand(CanExecute = nameof(CanStopAutoFetch))]
    private void StopAutoFetch()
    {
        if (SkipTrigger())
        {
            return;
        }

        if (!IsAutoFetchRunning)
        {
            _toast.Info("自动拉取", "当前没有运行中的任务");
            return;
        }

        IsAutoFetchRunning = false;
        SetAutoTaskState("增量追踪任务", AutoFetchState.Paused);
        _toast.Info("自动拉取", "已停止样板任务");

        AddRecentRun(new AutoFetchRunItem(
            Name: "增量追踪任务",
            StartedAtText: NowText(),
            State: AutoFetchState.Stopped));
    }

    [RelayCommand(CanExecute = nameof(CanRetryFailed))]
    private void RetryFailed()
    {
        if (SkipTrigger())
        {
            return;
        }

        if (RetryQueue.Count == 0)
        {
            _toast.Info("重试队列", "没有待重试任务");
            return;
        }

        var item = RetryQueue[0];
        RetryQueue.RemoveAt(0);
        AddRecentRun(new AutoFetchRunItem(
            Name: item.Name,
            StartedAtText: NowText(),
            State: AutoFetchState.Retrying));

        _toast.Info("重试队列", $"已重试：{item.Name}");
    }

    private bool CanStartAutoFetch()
        => CanOperateUi() && IsAutoFetchEnabled;

    private bool CanStopAutoFetch()
        => CanOperateUi() && IsAutoFetchRunning;

    private bool CanRetryFailed()
        => CanOperateUi() && RetryQueue.Count > 0;

    private async Task ReloadLookupAsync(CancellationToken ct)
    {
        if (IsLookupCatalogSuspended())
        {
            await RunOnUiAsync(() => OnLookupCatalogSuspended(), DispatcherPriority.Background);
            return;
        }

        var drugs = await LookupOptions.GetDrugOptionsAsync(_lookup, ct).ConfigureAwait(false);

        await RunOnUiAsync(() =>
        {
            _drugCatalog = drugs;
            AutoCompleteFilter.RefreshVisibleOptions(DrugOptions, _drugCatalog, DrugText);

            var currentDrug = NormalizeInput(DrugText);
            if (!string.IsNullOrWhiteSpace(currentDrug))
            {
                var stillExists = _drugCatalog.Any(x =>
                    string.Equals(x.Raw, currentDrug, StringComparison.OrdinalIgnoreCase));
                if (stillExists)
                {
                    return;
                }
            }

            DrugText = null;
            SpecOptions.Clear();
            SelectedSpec = null;
            SelectedQtyText = null;
            IsSpecSelected = false;
            UpdateStatus(_drugCatalog.Count == 0
                ? "药品信息为空，请先维护药品信息"
                : "请选择药品与规格", 0);
        }, DispatcherPriority.Background);
    }

    private async Task ReloadSpecsByDrugAsync(string drugId, CancellationToken ct)
    {
        var specs = await LookupOptions.GetSpecsAsync(_lookup, drugId, ct).ConfigureAwait(false);
        string? selectedSpecRaw = null;

        await RunOnUiAsync(() =>
        {
            OptionCollectionHelper.ReplaceRaw(SpecOptions, specs, StringComparison.Ordinal);

            if (SpecOptions.Count == 0)
            {
                SelectedSpec = null;
                SelectedQtyText = null;
                IsSpecSelected = false;
                UpdateStatus("该药品暂无可用规格", 2);
                return;
            }

            SelectedSpec = SpecOptions[0];
            IsSpecSelected = true;
            selectedSpecRaw = SelectedSpec.Raw;
        }, DispatcherPriority.Background);

        await RefreshQtyAsync().ConfigureAwait(false);

        await RunOnUiAsync(() =>
        {
            var spec = NormalizeInput(selectedSpecRaw);
            var qty = NormalizeInput(SelectedQtyText);

            if (string.IsNullOrWhiteSpace(spec))
            {
                UpdateStatus($"当前药品：{DrugLabel.Format(drugId, "请选择规格")}", 0);
                return;
            }

            var (message, level) = BuildCurrentDrugStatus(drugId, spec, qty);
            UpdateStatus(message, level);
        }, DispatcherPriority.Background);
    }

    private async Task RefreshQtyAsync()
    {
        var drug = NormalizeInput(DrugText);
        var spec = NormalizeInput(SelectedSpec?.Raw);

        if (string.IsNullOrWhiteSpace(drug) || string.IsNullOrWhiteSpace(spec))
        {
            await RunOnUiAsync(() => SelectedQtyText = null);
            return;
        }

        try
        {
            using var cts = new CancellationTokenSource(QtyLookupTimeout);
            var qty = await _lookup.GetQtyAsync(drug, spec, cts.Token).ConfigureAwait(false);

            await RunOnUiAsync(() =>
            {
                SelectedQtyText = qty?.ToString();
            }, DispatcherPriority.Background);
        }
        catch
        {
            await RunOnUiAsync(() => SelectedQtyText = null);
        }
    }

    private async Task RefreshQtyAndContextStatusAsync()
    {
        var drug = NormalizeInput(DrugText);
        var spec = NormalizeInput(SelectedSpec?.Raw);
        int? qty = null;
        if (!string.IsNullOrWhiteSpace(drug) && !string.IsNullOrWhiteSpace(spec))
        {
            try
            {
                using var cts = new CancellationTokenSource(QtyLookupTimeout);
                qty = await _lookup.GetQtyAsync(drug, spec, cts.Token).ConfigureAwait(false);
            }
            catch
            {
                qty = null;
            }
        }

        await RunOnUiAsync(() =>
        {
            SelectedQtyText = qty?.ToString();
            var qtyText = NormalizeInput(SelectedQtyText);

            if (string.IsNullOrWhiteSpace(drug) || string.IsNullOrWhiteSpace(spec))
            {
                return;
            }

            var (message, level) = BuildCurrentDrugStatus(drug, spec, qtyText);
            UpdateStatus(message, level);
        });
    }

    private static (string Message, int Level) BuildCurrentDrugStatus(string drugId, string spec, string? qtyText)
    {
        if (string.IsNullOrWhiteSpace(qtyText))
        {
            return ($"当前药品：{DrugLabel.Format(drugId, spec)}（未找到单盒数量）", 2);
        }

        if (int.TryParse(qtyText, out var parsedQty))
        {
            return ($"当前药品：{DrugLabel.WithQty(drugId, spec, parsedQty)}", 1);
        }

        return ($"当前药品：{DrugLabel.Format(drugId, spec)}", 1);
    }

    private void UpdateStatus(string message, int level)
    {
        Status = message;
        ContextStatusLevel = level;
    }

    private void RefreshPageCommands()
    {
        RefreshEntryPresentation();
        RefreshCommands(GetNotifiableCommands());
    }

    private void RefreshEntryPresentation()
    {
        OnPropertyChanged(nameof(IsTraceCodeInputEnabled));
        OnPropertyChanged(nameof(IsTraceCodeInputBlocked));
        OnPropertyChanged(nameof(IsEntryContextEmpty));
        OnPropertyChanged(nameof(SelectedDrugDisplay));
        OnPropertyChanged(nameof(SelectedSpecDisplay));
        OnPropertyChanged(nameof(SelectedQtyDisplay));
        OnPropertyChanged(nameof(TraceCodeRuleLength));
        OnPropertyChanged(nameof(TraceCodeRulePattern));
        OnPropertyChanged(nameof(ValidRatePct));
        OnPropertyChanged(nameof(ShowValidRate));
    }

    private IRelayCommand?[] GetNotifiableCommands()
        => _notifiableCommands ??=
        [
            SubmitCommand,
            ClearDrugSpecFilterCommand,
            ClearCodesCommand,
            StartAutoFetchCommand,
            StopAutoFetchCommand,
            RetryFailedCommand,
            OpenAutoFetchSettingsCommand,
            RequireDrugSpecCommand
        ];

    private void OnRecentRunsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        OnPropertyChanged(nameof(AutoFetchSuccessRateText));
    }

    private void OnRetryQueueChanged(object? sender, NotifyCollectionChangedEventArgs e)
        => RetryFailedCommand.NotifyCanExecuteChanged();

    private void SeedAutoFetchPanel()
    {
        AutoTasks.Clear();
        AutoTasks.Add(new AutoFetchTaskItem("全量拉取任务", "每 20 分钟", AutoFetchState.Ready, "拉取全部待入库追溯码并去重"));
        AutoTasks.Add(new AutoFetchTaskItem("增量追踪任务", "每 2 分钟", AutoFetchState.Ready, "仅拉取最近窗口变化数据"));
        AutoTasks.Add(new AutoFetchTaskItem("回补任务", "每日 02:00", AutoFetchState.Ready, "补偿前一日失败批次"));
        AutoTasks.Add(new AutoFetchTaskItem("账号会话保活", "每 10 分钟", AutoFetchState.Ready, "刷新上游会话并校验授权状态"));
        AutoTasks.Add(new AutoFetchTaskItem("一致性校验", "每日 03:30", AutoFetchState.Ready, "核对拉取批次与本地回填结果"));

        RecentRuns.Clear();
        RecentRuns.Add(new AutoFetchRunItem("增量追踪任务", DateTime.Now.AddMinutes(-18).ToString("yyyy-MM-dd HH:mm:ss"), AutoFetchState.Succeeded));
        RecentRuns.Add(new AutoFetchRunItem("全量拉取任务", DateTime.Now.AddHours(-2).ToString("yyyy-MM-dd HH:mm:ss"), AutoFetchState.Succeeded));
        RecentRuns.Add(new AutoFetchRunItem("账号会话保活", DateTime.Now.AddHours(-3).ToString("yyyy-MM-dd HH:mm:ss"), AutoFetchState.Succeeded));
        RecentRuns.Add(new AutoFetchRunItem("增量追踪任务", DateTime.Now.AddHours(-5).ToString("yyyy-MM-dd HH:mm:ss"), AutoFetchState.Failed));
        RecentRuns.Add(new AutoFetchRunItem("回补任务", DateTime.Now.AddHours(-8).ToString("yyyy-MM-dd HH:mm:ss"), AutoFetchState.Succeeded));

        RetryQueue.Clear();
        RetryQueue.Add(new AutoFetchRetryItem("增量追踪任务", "网络抖动（超时）", "第 1 次"));
        RetryQueue.Add(new AutoFetchRetryItem("回补任务", "上游返回空 token", "第 2 次"));
        RetryQueue.Add(new AutoFetchRetryItem("一致性校验", "批次摘要暂未生成", "第 1 次"));

        IsAutoFetchEnabled = false;
        IsAutoFetchRunning = false;
    }

    private void SetAutoTaskState(string name, AutoFetchState state)
    {
        for (var i = 0; i < AutoTasks.Count; i++)
        {
            var item = AutoTasks[i];
            if (!string.Equals(item.Name, name, StringComparison.Ordinal))
            {
                continue;
            }

            AutoTasks[i] = item with { State = state };
            return;
        }
    }

    private void AddRecentRun(AutoFetchRunItem item)
    {
        RecentRuns.Insert(0, item);
        while (RecentRuns.Count > 8)
        {
            RecentRuns.RemoveAt(RecentRuns.Count - 1);
        }
    }

    private static string NowText() => DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

    private void RecalcCodeStats(string? text)
    {
        var result = AnalyzeAndApply(text);
        SchedulePoolCheck(result.PoolCheckCandidates);
    }

    private TraceCodeAnalysisResult AnalyzeAndApply(string? text)
    {
        var result = TraceCodeAnalyzer.Analyze(text, CurrentValidationRule(), _existingPoolCodes);
        ApplyDetailedStats(result.Detailed, result.LineKinds);
        return result;
    }

    private TraceCodeValidationRule CurrentValidationRule()
    {
        var rule = _traceCodeRule.Current;
        return new TraceCodeValidationRule(rule.RequiredLength, rule.Pattern);
    }

    private async Task EnsurePoolCheckAsync(CancellationToken ct)
    {
        _poolCheckCts?.Cancel();
        TraceCodeAnalysisResult snapshot = default!;
        await RunOnUiAsync(() => snapshot = AnalyzeAndApply(TraceCodesText)).ConfigureAwait(false);

        var candidates = snapshot.PoolCheckCandidates;
        if (candidates.Count == 0 || !IsDbConnected)
        {
            await RunOnUiAsync(() =>
            {
                _existingPoolCodes.Clear();
                _poolCheckInFlightKey = string.Empty;
                _lastCompletedPoolCheckKey = string.Empty;
                AnalyzeAndApply(TraceCodesText);
            }).ConfigureAwait(false);
            return;
        }

        var key = BuildPoolCheckKey(candidates);
        if (string.Equals(key, _lastCompletedPoolCheckKey, StringComparison.Ordinal))
        {
            return;
        }

        var existing = await _scanCode.FindExistingTraceCodesAsync(candidates, ct).ConfigureAwait(false);
        await RunOnUiAsync(() =>
        {
            _existingPoolCodes.Clear();
            foreach (var code in existing)
            {
                _existingPoolCodes.Add(code);
            }

            _lastCompletedPoolCheckKey = key;
            _poolCheckInFlightKey = string.Empty;
            AnalyzeAndApply(TraceCodesText);
        }).ConfigureAwait(false);
    }

    private static CodeAnalysis ToCodeAnalysis(TraceCodeDetailedAnalysis detailed)
        => new(
            detailed.Total,
            detailed.Invalid,
            detailed.ScanDuplicate,
            detailed.ValidUniqueCodes);

    private void ApplyDetailedStats(TraceCodeDetailedAnalysis detailed, TraceCodeLineKind[] lineKinds)
    {
        _cachedDetailed = detailed;
        TotalCodeCount = detailed.Total;
        ValidCodeCount = detailed.Valid;
        DuplicateCodeCount = detailed.ScanDuplicate;
        PoolSkipCount = detailed.PoolDuplicate;
        InvalidCodeCount = detailed.Invalid;
        SetLineKinds(lineKinds);
    }

    private void SetLineKinds(TraceCodeLineKind[] lineKinds)
    {
        if (LineKindsEqual(_lineKinds, lineKinds))
        {
            return;
        }

        _lineKinds = lineKinds;
        OnPropertyChanged(nameof(LineKinds));
    }

    private static bool LineKindsEqual(TraceCodeLineKind[] left, TraceCodeLineKind[] right)
    {
        if (ReferenceEquals(left, right))
        {
            return true;
        }

        if (left.Length != right.Length)
        {
            return false;
        }

        for (var i = 0; i < left.Length; i++)
        {
            if (left[i] != right[i])
            {
                return false;
            }
        }

        return true;
    }

    private void SchedulePoolCheck(IReadOnlyList<string> candidateCodes)
    {
        var key = BuildPoolCheckKey(candidateCodes);
        if (string.Equals(key, _poolCheckInFlightKey, StringComparison.Ordinal)
            || string.Equals(key, _lastCompletedPoolCheckKey, StringComparison.Ordinal))
        {
            return;
        }

        _poolCheckInFlightKey = key;
        _poolCheckCts?.Cancel();
        _poolCheckCts?.Dispose();
        var cts = new CancellationTokenSource();
        _poolCheckCts = cts;
        ObserveDetached(RunPoolCheckAsync(key, candidateCodes, cts.Token), "pool.check.detached.fail");
    }

    private static string BuildPoolCheckKey(IReadOnlyList<string> candidateCodes)
        => candidateCodes.Count == 0
            ? string.Empty
            : string.Join('\n', candidateCodes.OrderBy(static x => x, StringComparer.Ordinal));

    private async Task RunPoolCheckAsync(
        string key,
        IReadOnlyList<string> candidateCodes,
        CancellationToken ct)
    {
        try
        {
            await Task.Delay(PoolCheckDebounce, ct).ConfigureAwait(false);
            if (candidateCodes.Count == 0 || !IsDbConnected)
            {
                await RunOnUiAsync(() =>
                {
                    _existingPoolCodes.Clear();
                    _poolCheckInFlightKey = string.Empty;
                    _lastCompletedPoolCheckKey = string.Empty;
                    AnalyzeAndApply(TraceCodesText);
                    RefreshCodeCommands();
                }).ConfigureAwait(false);
                return;
            }

            var existing = await _scanCode.FindExistingTraceCodesAsync(candidateCodes, ct).ConfigureAwait(false);
            await RunOnUiAsync(() =>
            {
                _existingPoolCodes.Clear();
                foreach (var code in existing)
                {
                    _existingPoolCodes.Add(code);
                }

                _lastCompletedPoolCheckKey = key;
                _poolCheckInFlightKey = string.Empty;
                AnalyzeAndApply(TraceCodesText);
                RefreshCodeCommands();
            }).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            await RunOnUiAsync(() => _poolCheckInFlightKey = string.Empty).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            LogWarn("scan.pool_check.fail", "Failed to pre-check trace codes in pool", ex);
            await RunOnUiAsync(() => _poolCheckInFlightKey = string.Empty).ConfigureAwait(false);
        }
    }

    private void OnTraceCodeRuleChanged()
    {
        RefreshEntryPresentation();
        RecalcCodeStats(TraceCodesText);
    }

    public override void Dispose()
    {
        _statsDebouncer.Dispose();
        _poolCheckCts?.Cancel();
        _poolCheckCts?.Dispose();
        _traceCodeRule.Changed -= OnTraceCodeRuleChanged;
        RecentRuns.CollectionChanged -= OnRecentRunsChanged;
        RetryQueue.CollectionChanged -= OnRetryQueueChanged;
        base.Dispose();
    }

    private static string BuildSubmitToastSummary(
        string drug,
        string spec,
        int qtyPerTrace,
        CodeAnalysis analysis,
        int poolSkipCount,
        ScanCodeInsertResult result)
    {
        var skipped = poolSkipCount + result.SkippedCount;
        return string.Join(
            '\n',
            DrugLabel.WithQty(drug, spec, qtyPerTrace),
            $"总数 {analysis.Total} · 有效 {analysis.ValidUniqueCodes.Count} · 重复 {analysis.Duplicate}",
            $"写入 {result.InsertedCount} · 跳过 {skipped} · 无效 {analysis.Invalid}");
    }

    private static string BuildClientRaw()
    {
        var machine = Environment.MachineName;
        var user = Environment.UserName;
        var ip = ResolveLocalIpv4();
        var os = Environment.OSVersion.VersionString;
        var ver = Assembly.GetEntryAssembly()?.GetName().Version?.ToString()
                  ?? Assembly.GetExecutingAssembly().GetName().Version?.ToString()
                  ?? "unknown";
        return $"{machine} | {user} | ip={ip} | os={os} | ver={ver}";
    }

    private static string ResolveLocalIpv4()
    {
        try
        {
            var host = Dns.GetHostEntry(Dns.GetHostName());
            var ip = host.AddressList
                .FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(a));
            return ip?.ToString() ?? "unknown";
        }
        catch
        {
            return "unknown";
        }
    }
}

public enum AutoFetchState
{
    Ready,
    Running,
    Succeeded,
    Failed,
    RetryPending,
    Retrying,
    Paused,
    Stopped
}

internal static class AutoFetchStateText
{
    public static string Get(AutoFetchState state)
        => state switch
        {
            AutoFetchState.Ready => "就绪",
            AutoFetchState.Running => "运行中",
            AutoFetchState.Succeeded => "成功",
            AutoFetchState.Failed => "失败",
            AutoFetchState.RetryPending => "待重试",
            AutoFetchState.Retrying => "重试中",
            AutoFetchState.Paused => "已暂停",
            AutoFetchState.Stopped => "已停止",
            _ => "未知"
        };
}

public sealed record AutoFetchTaskItem(string Name, string Schedule, AutoFetchState State, string Detail)
{
    public string StateText => AutoFetchStateText.Get(State);
}

public sealed record AutoFetchRunItem(string Name, string StartedAtText, AutoFetchState State)
{
    public string StateText => AutoFetchStateText.Get(State);
}

public sealed record AutoFetchRetryItem(
    string Name,
    string Reason,
    string RetryCountText,
    AutoFetchState State = AutoFetchState.RetryPending)
{
    public string StateText => AutoFetchStateText.Get(State);
}
