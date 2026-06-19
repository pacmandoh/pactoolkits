using System;
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
using PacToolkits.Desktop.Avalonia.Services.Application;
using PacToolkits.Desktop.Avalonia.Services.Infrastructure;

namespace PacToolkits.Desktop.Avalonia.ViewModels.Pages;

public sealed partial class ScanCodeViewModel : AppPageBase
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
    public ObservableCollection<AutoFetchTaskItem> AutoTasks { get; } = new();
    public ObservableCollection<AutoFetchRunItem> RecentRuns { get; } = new();
    public ObservableCollection<AutoFetchRetryItem> RetryQueue { get; } = new();
    protected override void OnLookupCatalogSuspended()
    {
        DrugOptions.Clear();
        SpecOptions.Clear();
        IsDrugSuggestOpen = false;
        DrugText = null;
        SelectedSpec = null;
        SelectedQtyText = null;
        IsSpecSelected = false;
    }

    protected override void OnPageAvailabilityChanged()
    {
        OnPropertyChanged(nameof(IsAutoTasksEmpty));
        OnPropertyChanged(nameof(AutoTasksEmptyText));
        OnPropertyChanged(nameof(AutoTasksEmptyHint));
        OnPropertyChanged(nameof(IsRecentRunsEmpty));
        OnPropertyChanged(nameof(RecentRunsEmptyText));
        OnPropertyChanged(nameof(RecentRunsEmptyHint));
        OnPropertyChanged(nameof(IsRetryQueueEmpty));
        OnPropertyChanged(nameof(RetryQueueEmptyText));
        OnPropertyChanged(nameof(RetryQueueEmptyHint));
    }

    public string AutoTasksEmptyText => GetSectionEmptyTitle("暂无任务");
    public string AutoTasksEmptyHint => GetSectionEmptyHint("当前没有自动拉取任务");
    public string RecentRunsEmptyText => GetSectionEmptyTitle("暂无执行记录");
    public string RecentRunsEmptyHint => GetSectionEmptyHint("当前没有任务执行历史");
    public string RetryQueueEmptyText => GetSectionEmptyTitle("暂无重试项");
    public string RetryQueueEmptyHint => GetSectionEmptyHint("当前没有失败重试任务");

    public bool IsAutoTasksEmpty => ShouldShowSectionEmpty(AutoTasks.Count == 0);
    public bool IsRecentRunsEmpty => ShouldShowSectionEmpty(RecentRuns.Count == 0);
    public bool IsRetryQueueEmpty => ShouldShowSectionEmpty(RetryQueue.Count == 0);

    [ObservableProperty] private int _selectedTabIndex;
    [ObservableProperty] private string? _drugText;
    [ObservableProperty] private OptionItem? _selectedSpec;
    [ObservableProperty] private string _traceCodesText = string.Empty;
    [ObservableProperty] private bool _isDrugSuggestOpen;
    [ObservableProperty] private string? _selectedQtyText;
    [ObservableProperty] private bool _isSpecSelected;
    [ObservableProperty] private int _inputLineCount;
    [ObservableProperty] private int _totalCodeCount;
    [ObservableProperty] private int _validCodeCount;
    [ObservableProperty] private int _duplicateCodeCount;
    [ObservableProperty] private int _invalidCodeCount;
    [ObservableProperty] private bool _isAutoFetchEnabled;
    [ObservableProperty] private bool _isAutoFetchRunning;
    [ObservableProperty] private int _contextStatusLevel;
    [ObservableProperty] private string _status = "请选择药品与规格";
    [ObservableProperty] private string _autoFetchStatus = "自动拉取能力准备中：将支持账号、任务与回填策略配置";

    public ScanCodeViewModel(
        ILookupCatalogService lookup,
        IScanCodeService scanCode,
        ITraceCodeRuleService traceCodeRule,
        IToastService toast)
    {
        _lookup = lookup;
        _scanCode = scanCode;
        _traceCodeRule = traceCodeRule;
        _toast = toast;
        AutoTasks.CollectionChanged += OnAutoTasksChanged;
        RecentRuns.CollectionChanged += OnRecentRunsChanged;
        RetryQueue.CollectionChanged += OnRetryQueueChanged;
        SeedAutoFetchPanel();
        _traceCodeRule.Changed += OnTraceCodeRuleChanged;

        PostOnUi(() => _ = RefreshPageAsync(), DispatcherPriority.Background);
    }

    protected override Task ReloadCoreAsync(CancellationToken ct)
        => ReloadLookupAsync(ct);

    protected override void OnReloadFinished()
        => NotifyActionCommands();

    private bool CanOperateUi() => !IsBusy;

    public void NotifyDrugIndexChanged()
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

                _lookup.InvalidateDrugCatalog();
                using var cts = new CancellationTokenSource(LookupTimeout);
                var drugs = await LookupOptionLoader.LoadDrugOptionsAsync(
                    _lookup,
                    cts.Token,
                    forceRefresh: true).ConfigureAwait(false);
                await RunOnUiAsync(() =>
                {
                    var currentDrug = NormalizeInput(DrugText);
                    OptionCollectionHelper.Replace(DrugOptions, drugs, StringComparison.Ordinal);

                    if (string.IsNullOrWhiteSpace(currentDrug))
                    {
                        return;
                    }

                    var stillExists = DrugOptions.Any(x =>
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
        var drug = NormalizeInput(value);
        IsDrugSuggestOpen = !string.IsNullOrWhiteSpace(drug);

        if (string.IsNullOrWhiteSpace(drug))
        {
            SpecOptions.Clear();
            SelectedSpec = null;
            SelectedQtyText = null;
            IsSpecSelected = false;
            UpdateStatus(DrugOptions.Count == 0
                ? "drug_index 为空，请先维护药品信息"
                : "请选择药品与规格", 0);
        }

        NotifyActionCommands();
    }

    partial void OnSelectedSpecChanged(OptionItem? value)
    {
        IsSpecSelected = value is not null;
        _ = RefreshQtyAndContextStatusAsync();
        NotifyActionCommands();
    }

    partial void OnTraceCodesTextChanged(string value)
    {
        RecalcCodeStats(value);
        NotifyActionCommands();
    }

    [RelayCommand]
    private async Task ApplyDrugFilterAsync()
    {
        if (ShouldSkipTrigger())
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
                if (ShouldShowOperationErrorToast(ex))
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
        => CanOperateUi()
           && (!string.IsNullOrWhiteSpace(NormalizeInput(DrugText))
               || SelectedSpec is not null
               || !string.IsNullOrWhiteSpace(SelectedQtyText));

    [RelayCommand(CanExecute = nameof(CanSubmit))]
    private async Task SubmitAsync()
    {
        if (ShouldSkipTrigger())
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
                    var analysis = AnalyzeCodes(TraceCodesText);
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
                        var summary =
                            $"{drug}/{spec} · 总数 {analysis.Total} · 有效 {analysis.ValidUniqueCodes.Count} · 重复 {analysis.Duplicate} · 无效 {analysis.Invalid} · 写入 {result.InsertedCount} · 跳过 {result.SkippedCount}";

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
                            _toast.Warn("追溯码录入", $"无新增记录：{summary}");
                        }
                    });
                }
                finally
                {
                    await RunOnUiAsync(() => { TraceCodesText = string.Empty; }).ConfigureAwait(false);
                }
            }, onFinished: NotifyActionCommands);
        }
        catch (Exception ex)
        {
            LogError("scan.submit.fail", "Submit trace codes failed", ex);
            await RunOnUiAsync(() =>
            {
                Status = $"录入失败：{ex.Message}";
                if (ShouldShowOperationErrorToast(ex))
                {
                    _toast.Error("追溯码录入失败", ex.Message);
                }
            });
        }
    }

    private bool CanClearCodes()
        => CanOperateUi() && !string.IsNullOrWhiteSpace(TraceCodesText);

    [RelayCommand(CanExecute = nameof(CanClearCodes))]
    private void ClearCodes()
    {
        if (ShouldSkipTrigger())
        {
            return;
        }

        TraceCodesText = string.Empty;
        Status = "已清空输入框";
    }

    [RelayCommand(CanExecute = nameof(CanClearDrugSpecFilter))]
    private void ClearDrugSpecFilter()
    {
        if (ShouldSkipTrigger())
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
        NotifyActionCommands();
    }

    [RelayCommand]
    private void EnsureEditorContext()
    {
        if (ShouldSkipTrigger())
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
        if (ShouldSkipTrigger())
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
        AutoFetchStatus = "模拟任务已启动：正在拉取并解析码上放心数据";
        _toast.Info("自动拉取", "已启动模拟任务");

        AddRecentRun(new AutoFetchRunItem(
            Name: "全量拉取任务",
            StartedAtText: NowText(),
            Result: "RUNNING",
            Detail: "任务已排队，等待后端接入"));
    }

    [RelayCommand]
    private void OpenAutoFetchSettings()
    {
        if (ShouldSkipTrigger())
        {
            return;
        }

        AutoFetchStatus = "参数配置面板准备中：将支持账号、时间窗、拉取频率和失败重试";
        _toast.Info("自动拉取", "参数配置面板预留中");
    }

    partial void OnIsAutoFetchEnabledChanged(bool value)
    {
        if (!value && IsAutoFetchRunning)
            IsAutoFetchRunning = false;

        AutoFetchStatus = value
            ? "自动拉取已启用：可开始任务调度"
            : "自动拉取已关闭：当前不会执行自动任务";

        NotifyActionCommands();
    }

    [RelayCommand(CanExecute = nameof(CanStopAutoFetch))]
    private void StopAutoFetch()
    {
        if (ShouldSkipTrigger())
        {
            return;
        }

        if (!IsAutoFetchRunning)
        {
            _toast.Info("自动拉取", "当前没有运行中的任务");
            return;
        }

        IsAutoFetchRunning = false;
        AutoFetchStatus = "任务已停止：等待下次手动启动";
        _toast.Info("自动拉取", "已停止模拟任务");

        AddRecentRun(new AutoFetchRunItem(
            Name: "全量拉取任务",
            StartedAtText: NowText(),
            Result: "STOPPED",
            Detail: "由用户手动停止"));
    }

    [RelayCommand(CanExecute = nameof(CanRetryFailed))]
    private void RetryFailed()
    {
        if (ShouldSkipTrigger())
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
            Result: "RETRYING",
            Detail: $"已触发重试：{item.Reason}"));

        AutoFetchStatus = $"已触发重试：{item.Name}";
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

        var drugs = await LookupOptionLoader.LoadDrugOptionsAsync(_lookup, ct).ConfigureAwait(false);

        await RunOnUiAsync(() =>
        {
            OptionCollectionHelper.Replace(DrugOptions, drugs, StringComparison.Ordinal);

            var currentDrug = NormalizeInput(DrugText);
            if (!string.IsNullOrWhiteSpace(currentDrug))
            {
                var stillExists = DrugOptions.Any(x =>
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
            UpdateStatus(DrugOptions.Count == 0
                ? "药品信息为空，请先维护药品信息"
                : "请选择药品与规格", 0);
        }, DispatcherPriority.Background);
    }

    private async Task ReloadSpecsByDrugAsync(string drugId, CancellationToken ct)
    {
        var specs = await LookupOptionLoader.LoadSpecsAsync(_lookup, drugId, ct).ConfigureAwait(false);
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
                UpdateStatus($"当前药品：{drugId}（请选择规格）", 0);
                return;
            }

            if (string.IsNullOrWhiteSpace(qty))
            {
                UpdateStatus($"当前药品：{drugId} / {spec}（未找到单条数量）", 2);
                return;
            }

            UpdateStatus($"当前药品：{drugId} / {spec}", 1);
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

            if (string.IsNullOrWhiteSpace(qtyText))
            {
                UpdateStatus($"当前药品：{drug} / {spec}（未找到单条数量）", 2);
                return;
            }

            UpdateStatus($"当前药品：{drug} / {spec}", 1);
        });
    }

    private void UpdateStatus(string message, int level)
    {
        Status = message;
        ContextStatusLevel = level;
    }

    private void NotifyActionCommands()
        => NotifyCommands(GetNotifiableCommands());

    private IRelayCommand?[] GetNotifiableCommands()
        => _notifiableCommands ??=
        [
            ClearDrugSpecFilterCommand,
            SubmitCommand,
            ClearCodesCommand,
            StartAutoFetchCommand,
            StopAutoFetchCommand,
            RetryFailedCommand,
            OpenAutoFetchSettingsCommand,
            EnsureEditorContextCommand
        ];

    private void OnAutoTasksChanged(object? sender, NotifyCollectionChangedEventArgs e)
        => OnPropertyChanged(nameof(IsAutoTasksEmpty));

    private void OnRecentRunsChanged(object? sender, NotifyCollectionChangedEventArgs e)
        => OnPropertyChanged(nameof(IsRecentRunsEmpty));

    private void OnRetryQueueChanged(object? sender, NotifyCollectionChangedEventArgs e)
        => OnPropertyChanged(nameof(IsRetryQueueEmpty));

    private void SeedAutoFetchPanel()
    {
        AutoTasks.Clear();
        AutoTasks.Add(new AutoFetchTaskItem("全量拉取任务", "每 20 分钟", "IDLE", "拉取全部待入库追溯码并去重"));
        AutoTasks.Add(new AutoFetchTaskItem("增量追踪任务", "每 2 分钟", "IDLE", "仅拉取最近窗口变化数据"));
        AutoTasks.Add(new AutoFetchTaskItem("回补任务", "每日 02:00", "IDLE", "补偿前一日失败批次"));

        RecentRuns.Clear();
        RecentRuns.Add(new AutoFetchRunItem("增量追踪任务", DateTime.Now.AddMinutes(-18).ToString("yyyy-MM-dd HH:mm:ss"), "SUCCESS", "处理 224 条，写入 220 条，跳过 4 条"));
        RecentRuns.Add(new AutoFetchRunItem("全量拉取任务", DateTime.Now.AddHours(-2).ToString("yyyy-MM-dd HH:mm:ss"), "SUCCESS", "处理 3,102 条，写入 3,050 条，跳过 52 条"));

        RetryQueue.Clear();
        RetryQueue.Add(new AutoFetchRetryItem("增量追踪任务", "网络抖动（超时）", "第 1 次"));
        RetryQueue.Add(new AutoFetchRetryItem("回补任务", "上游返回空 token", "第 2 次"));

        IsAutoFetchEnabled = false;
        IsAutoFetchRunning = false;
    }

    private void AddRecentRun(AutoFetchRunItem item)
    {
        RecentRuns.Insert(0, item);
        while (RecentRuns.Count > 8)
        {
            RecentRuns.RemoveAt(RecentRuns.Count - 1);
        }

        NotifyActionCommands();
    }

    private static string NowText() => DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

    private void RecalcCodeStats(string? text)
    {
        var analysis = AnalyzeCodes(text);
        InputLineCount = analysis.Total;
        TotalCodeCount = analysis.Total;
        ValidCodeCount = analysis.ValidUniqueCodes.Count;
        DuplicateCodeCount = analysis.Duplicate;
        InvalidCodeCount = analysis.Invalid;
    }

    private CodeAnalysis AnalyzeCodes(string? text)
    {
        var rule = _traceCodeRule.Current;
        return TraceCodeAnalyzer.Analyze(text, new TraceCodeValidationRule(rule.RequiredLength, rule.Pattern));
    }

    private void OnTraceCodeRuleChanged()
        => RecalcCodeStats(TraceCodesText);

    public override void Dispose()
    {
        _traceCodeRule.Changed -= OnTraceCodeRuleChanged;
        AutoTasks.CollectionChanged -= OnAutoTasksChanged;
        RecentRuns.CollectionChanged -= OnRecentRunsChanged;
        RetryQueue.CollectionChanged -= OnRetryQueueChanged;
        base.Dispose();
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

public sealed record AutoFetchTaskItem(string Name, string Schedule, string State, string Detail);
public sealed record AutoFetchRunItem(string Name, string StartedAtText, string Result, string Detail);
public sealed record AutoFetchRetryItem(string Name, string Reason, string RetryCountText);
