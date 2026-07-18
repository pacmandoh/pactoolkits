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
using PacToolkits.Application.Services.Msfx;
using PacToolkits.Desktop.Avalonia.Common;
using PacToolkits.Desktop.Avalonia.Services.Infrastructure;
using PacToolkits.Desktop.Avalonia.Services.Presentation;
using PacToolkits.Desktop.Avalonia.Services.Workspace;

namespace PacToolkits.Desktop.Avalonia.ViewModels.Pages;

public sealed partial class MsfxLink : AppPageBase
{
    private const int AutoLogMaxRows = 500;
    private static readonly string[] PullBatchPageSizes = ["20", "50", "100"];
    private static readonly string[] UpoutPageSizes = ["20", "30", "50", "100"];
    private static readonly string[] SubcodePageSizes = ["100", "200", "500", "1000"];
    private static readonly string[] MapQueuePageSizes = ["120", "240", "500"];
    private static readonly string[] TaskQueuePageSizes = ["20", "50", "100"];
    private static readonly string[] AutoLogPageSizes = ["20", "50", "100"];
    private readonly IMsfxApiClient _msfxApi;
    private readonly ISyncService _syncService;
    private readonly ILookupCatalogService _lookup;
    private readonly IMsfxAutoRunService _autoRun;
    private readonly IAppConfigStore _configStore;
    private readonly ISensitiveUnlockService _unlockService;
    private readonly IToastService _toast;
    private readonly IDialogService _dialog;
    private readonly IBackgroundTaskRunner _backgroundTasks;
    private readonly WorkspaceDirtyRefresh _dirtyRefresh;
    private readonly DispatcherTimer _autoTimer;
    private readonly DispatcherTimer _unlockStatusTimer;
    private readonly CancellationTokenSource _autoRunLifetimeCts = new();
    private int _autoRunRunning;
    private int _manualMsfxWriteDepth;
    private bool _interruptedPullBatchesRecovered;

    private bool IsManualMsfxWriteActive => _manualMsfxWriteDepth > 0;

    public override string DisplayName => "码上放心联调";
    public override string Icon => "CloudCog";
    public override int Index => 5;
    public override string FunctionAreaId => ShellFunctionAreas.AutomationId;
    public override ICommand? RefreshCommand => SelectedTabIndex switch
    {
        0 => RefreshAutoBoardCommand,
        1 when IsMappingWorkspace => RefreshMappingWorkspaceCommand,
        1 => RefreshAutoBoardCommand,
        _ => null
    };
    protected override bool AutoRefreshOnDbDisconnected => true;
    protected override bool AutoRefreshOnDbReconnected => true;
    protected override bool CanAutoRefreshFromDbSignal() => SelectedTabIndex <= 1 && base.CanAutoRefreshFromDbSignal();

    [ObservableProperty] private int _selectedTabIndex;
    [ObservableProperty] private int _upstreamQueryMode;
    [ObservableProperty] private bool _isRunConfigBarVisible = true;

    [ObservableProperty] private bool _isAutoEnabled;
    [ObservableProperty] private int _autoIntervalMinutes = 30;
    [ObservableProperty] private bool _isAutoBusy;
    [ObservableProperty] private bool _showAutoProgress;
    [ObservableProperty] private bool _isPullPanelBusy;
    [ObservableProperty] private bool _isMapPanelBusy;
    [ObservableProperty] private bool _isTaskPanelBusy;

    public bool IsAutoBoardBusy => IsPullPanelBusy || IsMapPanelBusy || IsTaskPanelBusy;
    [ObservableProperty] private string _autoStatus = "未启动";
    [ObservableProperty] private double _autoRunProgressValue;
    [ObservableProperty] private string _autoLastRunAtText = "尚未巡检";
    [ObservableProperty] private string? _autoLastRunAtTip;
    [ObservableProperty] private string _autoPullSummary = "批次：暂无";
    [ObservableProperty] private int _autoMapPendingCount;
    [ObservableProperty] private int _autoMapMappedCount;
    [ObservableProperty] private int _autoMapNeedReviewCount;
    [ObservableProperty] private int _autoMapFailedCount;
    [ObservableProperty] private int _autoTaskNewCount;
    [ObservableProperty] private int _autoTaskRunningCount;
    [ObservableProperty] private int _autoTaskSuccessCount;
    [ObservableProperty] private int _autoTaskFailedCount;
    [ObservableProperty] private int _autoTaskDiscardedCount;
    [ObservableProperty] private TraceEntryState _autoPullState = TraceEntryState.Info;
    [ObservableProperty] private TraceEntryState _autoMapState = TraceEntryState.Info;
    [ObservableProperty] private TraceEntryState _autoTaskState = TraceEntryState.Info;
    [ObservableProperty] private TraceEntryState _autoRiskState = TraceEntryState.Info;
    [ObservableProperty] private string _pullBatchPageSize = "20";
    [ObservableProperty] private int _pullBatchPage = 1;
    [ObservableProperty] private int _pullBatchTotalCount;
    [ObservableProperty] private string _mapQueuePageSize = "120";
    [ObservableProperty] private int _mapQueueTotalCount;
    [ObservableProperty] private bool _mapQueueHasNewer;
    [ObservableProperty] private bool _mapQueueHasOlder;
    [ObservableProperty] private int _mapQueuePage = 1;
    [ObservableProperty] private bool _isQueueSearchBarVisible = true;
    [ObservableProperty] private string _queueSearchKeyword = string.Empty;
    [ObservableProperty] private string _taskQueuePageSize = "50";
    [ObservableProperty] private int _taskQueuePage = 1;
    [ObservableProperty] private string _autoLogPageSize = "50";
    [ObservableProperty] private int _autoLogPage = 1;
    [ObservableProperty] private TaskQueueBatchActionMode _taskQueueBatchMode;

    [ObservableProperty] private DateTime? _upoutFromDate = DateTime.Today.AddDays(-6);
    [ObservableProperty] private DateTime? _upoutToDate = DateTime.Today;
    [ObservableProperty] private string _upstreamKeyword = string.Empty;
    [ObservableProperty] private string _upoutPageSize = "20";
    [ObservableProperty] private int _upoutPage = 1;
    [ObservableProperty] private long _upoutTotal;
    [ObservableProperty] private string _upoutStatus = "请设置日期后查询";
    [ObservableProperty] private bool _isUpoutBusy;

    [ObservableProperty] private string _subcodePageSize = "200";
    [ObservableProperty] private int _subcodePage = 1;
    [ObservableProperty] private int _subcodeTotal;
    [ObservableProperty] private bool _isSubcodeBusy;
    [ObservableProperty] private MsfxAutoPullBatchGridRow? _selectedAutoPullBatchRow;
    [ObservableProperty] private MsfxAutoLogRow? _selectedAutoLogRow;

    public ObservableCollection<string> UpoutPageSizeOptions { get; } = new(UpoutPageSizes);
    public ObservableCollection<string> PullBatchPageSizeOptions { get; } = new(PullBatchPageSizes);
    public ObservableCollection<string> SubcodePageSizeOptions { get; } = new(SubcodePageSizes);
    public ObservableCollection<string> MapQueuePageSizeOptions { get; } = new(MapQueuePageSizes);
    public ObservableCollection<string> TaskQueuePageSizeOptions { get; } = new(TaskQueuePageSizes);
    public ObservableCollection<string> AutoLogPageSizeOptions { get; } = new(AutoLogPageSizes);
    public ObservableCollection<MsfxUpoutGridRow> UpoutRows { get; } = new();
    public ObservableCollection<MsfxSubCodeGridRow> SubCodeRows { get; } = new();
    public ObservableCollection<MsfxAutoLogRow> AutoLogs { get; } = new();
    public ObservableCollection<MsfxAutoLogRow> AutoLogPageRows { get; } = new();
    public ObservableCollection<MsfxAutoPullBatchGridRow> AutoPullBatchRows { get; } = new();
    public ObservableCollection<MsfxAutoMapQueueGridRow> AutoMapQueueRows { get; } = new();
    public ObservableCollection<MsfxAutoTaskQueueGridRow> AutoTaskQueueRows { get; } = new();
    public IReadOnlyList<MsfxAutoTaskQueueGridRow> SelectedAutoTaskQueueRowsSnapshot => _selectedAutoTaskQueueRowsSnapshot;
    public bool IsTaskQueueBatchModeActive => TaskQueueBatchMode != TaskQueueBatchActionMode.None;
    public bool ShowQueueToolbar => IsQueueSearchBarVisible || IsTaskQueueBatchModeActive;
    public string TaskQueueBatchModeTitle => TaskQueueBatchMode switch
    {
        TaskQueueBatchActionMode.Merge => "选择要合并的任务",
        TaskQueueBatchActionMode.Remap => "选择要重新映射的任务",
        TaskQueueBatchActionMode.Discard => "选择要弃用的任务",
        TaskQueueBatchActionMode.Reopen => "选择要重开的任务",
        TaskQueueBatchActionMode.Split => "选择要拆分的任务",
        _ => string.Empty
    };
    public string TaskQueueBatchModeHint => TaskQueueBatchMode switch
    {
        TaskQueueBatchActionMode.Merge => "仅可勾选同药名、同规格任务；允许跨单据合并",
        TaskQueueBatchActionMode.Remap => "勾选后会把任务退回映射结果队列重新处理",
        TaskQueueBatchActionMode.Discard => "勾选后会把任务标成弃用，保留追溯但不再执行",
        TaskQueueBatchActionMode.Reopen => "勾选后会把成功或弃用任务重新恢复到待执行",
        TaskQueueBatchActionMode.Split => "仅可勾选一条码数大于 1 的可编排任务",
        _ => string.Empty
    };
    public string TaskQueueBatchConfirmText => TaskQueueBatchMode switch
    {
        TaskQueueBatchActionMode.Merge => "确认合并",
        TaskQueueBatchActionMode.Remap => "确认重新映射",
        TaskQueueBatchActionMode.Discard => "确认弃用",
        TaskQueueBatchActionMode.Reopen => "确认重开",
        TaskQueueBatchActionMode.Split => "确认拆分",
        _ => "确认"
    };
    public bool CanConfirmTaskQueueBatchAction => TaskQueueBatchMode switch
    {
        TaskQueueBatchActionMode.Merge => CanBatchMergeSelectedTasks,
        TaskQueueBatchActionMode.Remap => CanBatchRemapSelectedTasks,
        TaskQueueBatchActionMode.Discard => CanBatchDiscardSelectedTasks,
        TaskQueueBatchActionMode.Reopen => CanBatchReopenSelectedTasks,
        TaskQueueBatchActionMode.Split => CanSplitSelectedTasks,
        _ => false
    };
    public bool CanMergeTasks => !IsAutoBoardBusy && _filteredTaskQueueRows.Count(x => x.CurrentCodeCount > 0) >= 2;
    public bool CanRemapTasks => !IsAutoBoardBusy && _filteredTaskQueueRows.Any(x => !string.Equals(x.Status, "RUNNING", StringComparison.OrdinalIgnoreCase) && x.CurrentCodeCount > 0);
    public bool CanDiscardTasks => !IsAutoBoardBusy && _filteredTaskQueueRows.Any(x => x.CurrentCodeCount > 0 && (string.Equals(x.Status, "NEW", StringComparison.OrdinalIgnoreCase) || string.Equals(x.Status, "FAILED", StringComparison.OrdinalIgnoreCase)));
    public bool CanReopenTasks => !IsAutoBoardBusy && _filteredTaskQueueRows.Any(x => string.Equals(x.Status, "SUCCESS", StringComparison.OrdinalIgnoreCase) || string.Equals(x.Status, "DISCARDED", StringComparison.OrdinalIgnoreCase));
    public bool CanBatchReopenSelectedTasks => !IsAutoBoardBusy
                                               && SelectedAutoTaskQueueRowsSnapshot.Any(x =>
                                                   string.Equals(x.Status, "SUCCESS", StringComparison.OrdinalIgnoreCase)
                                                   || string.Equals(x.Status, "DISCARDED", StringComparison.OrdinalIgnoreCase));
    public bool CanBatchDiscardSelectedTasks => !IsAutoBoardBusy
                                                && SelectedAutoTaskQueueRowsSnapshot.Any(x =>
                                                    string.Equals(x.Status, "NEW", StringComparison.OrdinalIgnoreCase)
                                                    || string.Equals(x.Status, "FAILED", StringComparison.OrdinalIgnoreCase));
    public bool CanBatchRemapSelectedTasks => !IsAutoBoardBusy
                                              && SelectedAutoTaskQueueRowsSnapshot.Any(x =>
                                                  !string.Equals(x.Status, "RUNNING", StringComparison.OrdinalIgnoreCase));
    public bool CanBatchMergeSelectedTasks => !IsAutoBoardBusy
                                              && SelectedAutoTaskQueueRowsSnapshot.Count >= 2
                                              && SelectedAutoTaskQueueRowsSnapshot.All(x =>
                                                  x.CurrentCodeCount > 0
                                                  && (
                                                  string.Equals(x.Status, "NEW", StringComparison.OrdinalIgnoreCase)
                                                  || string.Equals(x.Status, "FAILED", StringComparison.OrdinalIgnoreCase)
                                                  || string.Equals(x.Status, "DISCARDED", StringComparison.OrdinalIgnoreCase)
                                                  || string.Equals(x.Status, "CANCELLED", StringComparison.OrdinalIgnoreCase)))
                                              && SelectedAutoTaskQueueRowsSnapshot
                                                  .Select(BuildMergeKey)
                                                  .Distinct(StringComparer.OrdinalIgnoreCase)
                                                  .Count() == 1;
    public bool CanSplitTasks => !IsAutoBoardBusy && _filteredTaskQueueRows.Any(CanSplitTask);
    public bool CanSplitSelectedTasks => !IsAutoBoardBusy
                                         && TaskQueueBatchMode == TaskQueueBatchActionMode.Split
                                         && SelectedAutoTaskQueueRowsSnapshot.Count == 1
                                         && CanSplitTask(SelectedAutoTaskQueueRowsSnapshot[0]);
    public int TaskQueueSelectedCount => SelectedAutoTaskQueueRowsSnapshot.Count;
    public int TaskQueuePagerSelectedCount => IsTaskQueueBatchModeActive ? TaskQueueSelectedCount : -1;

    public bool IsUpoutEmpty => UpoutRows.Count == 0;
    public bool IsSubCodeEmpty => SubCodeRows.Count == 0;
    public bool IsAutoLogsEmpty => AutoLogs.Count == 0;
    public int AutoLogCount => AutoLogs.Count;
    public bool IsLastRunSuccess => AutoPullState == TraceEntryState.Success;
    public bool IsLastRunWarning => AutoPullState is TraceEntryState.Warning or TraceEntryState.Info or TraceEntryState.Unknown;
    public bool IsLastRunFailed => AutoPullState == TraceEntryState.Failed;
    public bool IsAutoPullBatchEmpty => AutoPullBatchRows.Count == 0;
    public bool IsAutoMapQueueEmpty => AutoMapQueueRows.Count == 0;
    public bool HasActiveMapQueueFilter => !string.IsNullOrWhiteSpace(QueueSearchKeyword) || _mapQueueStatusFilters.Count > 0;
    public bool HasActiveTaskQueueFilter => !string.IsNullOrWhiteSpace(QueueSearchKeyword) || _taskQueueStatusFilters.Count > 0;
    public bool IsMapPendingFilterActive => _mapQueueStatusFilters.Contains("PENDING");
    public bool IsMapMappedFilterActive => _mapQueueStatusFilters.Contains("MAPPED");
    public bool IsMapNeedReviewFilterActive => _mapQueueStatusFilters.Contains("NEED_REVIEW");
    public bool IsMapFailedFilterActive => _mapQueueStatusFilters.Contains("FAILED");
    public bool IsTaskNewFilterActive => _taskQueueStatusFilters.Contains("NEW");
    public bool IsTaskRunningFilterActive => _taskQueueStatusFilters.Contains("RUNNING");
    public bool IsTaskSuccessFilterActive => _taskQueueStatusFilters.Contains("SUCCESS");
    public bool IsTaskFailedFilterActive => _taskQueueStatusFilters.Contains("FAILED");
    public bool IsTaskDiscardedFilterActive => _taskQueueStatusFilters.Contains("DISCARDED");
    public bool IsAutoTaskQueueEmpty => AutoTaskQueueRows.Count == 0;
    public int PullBatchTotalPages => Math.Max(1, (int)Math.Ceiling(PullBatchTotalCount / (double)GetPullBatchPageSize()));
    public int MapQueueEffectivePageSize => GetMapQueueQueryPageSize();
    public int MapQueueTotalPages => Math.Max(1, (int)Math.Ceiling(MapQueueTotalCount / (double)Math.Max(1, MapQueueEffectivePageSize)));
    public int TaskQueueFilteredCount => _filteredTaskQueueRows.Count;
    public int TaskQueueTotalPages => Math.Max(1, (int)Math.Ceiling(TaskQueueFilteredCount / (double)GetTaskQueuePageSize()));
    public int AutoLogTotalPages => Math.Max(1, (int)Math.Ceiling(AutoLogs.Count / (double)GetAutoLogPageSize()));
    public bool HasUpoutPrevPage => UpoutPage > 1;
    public int UpoutTotalPages => Math.Max(1, (int)Math.Ceiling(UpoutTotal / (double)GetPageSize()));
    public bool HasUpoutNextPage => UpoutPage < UpoutTotalPages;
    public bool HasSubcodePrevPage => SubcodePage > 1;
    public int SubcodeTotalPages => Math.Max(1, (int)Math.Ceiling(SubcodeTotal / (double)GetSubcodePageSize()));
    public bool HasSubcodeNextPage => SubcodePage < SubcodeTotalPages;
    public bool HasPullBatchPrevPage => PullBatchPage > 1;
    public bool HasPullBatchNextPage => PullBatchPage < PullBatchTotalPages;
    public bool HasTaskQueuePrevPage => TaskQueuePage > 1;
    public bool HasTaskQueueNextPage => TaskQueuePage < TaskQueueTotalPages;
    public bool HasAutoLogPrevPage => AutoLogPage > 1;
    public bool HasAutoLogNextPage => AutoLogPage < AutoLogTotalPages;
    public DateTime? UpoutFromMaxDate => UpoutToDate?.Date;
    public DateTime? UpoutToMinDate => UpoutFromDate?.Date;
    public DateTime? UpoutToMaxDate => DateTime.Today;
    public bool IsRunPage => SelectedTabIndex == 0;
    public bool IsQueuePage => SelectedTabIndex == 1;
    public bool IsUpstreamPage => SelectedTabIndex == 2;
    public bool IsUpoutQueryMode => UpstreamQueryMode == 0;
    public bool IsSubcodeQueryMode => UpstreamQueryMode == 1;
    public bool HasActiveUpstreamSearch => !string.IsNullOrWhiteSpace(UpstreamKeyword);
    private List<MsfxSubCodeGridRow> _allSubCodeRows = new();
    private List<MsfxAutoPullBatchGridRow> _allPullBatchRows = new();
    private List<MsfxAutoTaskQueueGridRow> _allTaskQueueRows = new();
    private List<MsfxAutoTaskQueueGridRow> _filteredTaskQueueRows = new();
    private List<MsfxAutoTaskQueueGridRow> _selectedAutoTaskQueueRowsSnapshot = new();
    private List<MsfxUpoutGridRow> _allUpoutRows = new();
    private readonly HashSet<string> _mapQueueStatusFilters = new(StringComparer.Ordinal);
    private readonly HashSet<string> _taskQueueStatusFilters = new(StringComparer.Ordinal);
    private long _upoutLastServerTotal;
    private DateTimeOffset? _mapCursorUpdatedAt;
    private long? _mapCursorId;
    private string? _lastAutoLogSignature;
    private readonly SearchInputDebouncer _queueSearchDebouncer = new(350);
    private readonly SearchInputDebouncer _upoutFilterDebouncer = new(300);
    private readonly RollingDateRangeController _upoutDateRangeController;
    private bool _suppressQueueSearchRefresh;
    private bool _syncingTaskQueuePageRows;

    public MsfxLink(
        IMsfxApiClient msfxApi,
        ISyncService syncService,
        ILookupCatalogService lookup,
        IMsfxAutoRunService autoRun,
        IAppConfigStore configStore,
        ISensitiveUnlockService unlockService,
        IToastService toast,
        IDialogService dialog,
        IBackgroundTaskRunner backgroundTasks,
        WorkspaceDirtyRefresh dirtyRefresh)
    {
        _msfxApi = msfxApi;
        _syncService = syncService;
        _lookup = lookup;
        _autoRun = autoRun;
        _configStore = configStore;
        _unlockService = unlockService;
        _toast = toast;
        _dialog = dialog;
        _backgroundTasks = backgroundTasks;
        _dirtyRefresh = dirtyRefresh;
        _upoutDateRangeController = new RollingDateRangeController(() =>
            PostOnUi(HandleUpoutDateRangeDayChanged, DispatcherPriority.Background));

        _autoTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMinutes(Math.Max(1, AutoIntervalMinutes))
        };
        _autoTimer.Tick += OnAutoTimerTick;
        _unlockStatusTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _unlockStatusTimer.Tick += OnUnlockStatusTimerTick;
        RefreshOpsUnlock();

        AutoLogs.CollectionChanged += OnAutoLogsCollectionChanged;
        AutoPullBatchRows.CollectionChanged += OnAutoPullBatchRowsCollectionChanged;
        AutoMapQueueRows.CollectionChanged += OnAutoMapQueueRowsCollectionChanged;
        AutoTaskQueueRows.CollectionChanged += OnAutoTaskQueueRowsCollectionChanged;
        UpoutRows.CollectionChanged += OnUpoutRowsCollectionChanged;
        SubCodeRows.CollectionChanged += OnSubCodeRowsCollectionChanged;

        var normalized = RollingDateRangeController.Normalize(UpoutFromDate, UpoutToDate);
        UpoutFromDate = normalized.From;
        UpoutToDate = normalized.To;

        PostOnUi(() =>
        {
            AddAutoLog("初始化", "联调页面已加载，自动化监控待启动", TraceEntryState.Info);
        }, DispatcherPriority.Background);
    }

    protected override Task ReloadCoreAsync(CancellationToken ct)
    {
        return SelectedTabIndex switch
        {
            0 => RefreshAutoBoardAsync(ct),
            1 when IsMappingWorkspace => ReloadMappingWorkspaceCoreAsync(ct),
            1 => RefreshAutoBoardAsync(ct),
            2 when IsSubcodeQueryMode => QuerySubCodesAsync(null, null),
            2 => QueryUpoutAsync(resetPage: false),
            _ => Task.CompletedTask
        };
    }

    partial void OnUpoutFromDateChanged(DateTime? value)
        => ApplyUpoutDateRangeBoundary(value, updateFromBoundary: true);

    partial void OnUpoutToDateChanged(DateTime? value)
        => ApplyUpoutDateRangeBoundary(value, updateFromBoundary: false);

    private void ApplyUpoutDateRangeBoundary(DateTime? value, bool updateFromBoundary)
    {
        var today = DateTime.Today;
        var safeValue = value?.Date;
        if (safeValue is not null && safeValue.Value > today)
        {
            safeValue = today;
            if (updateFromBoundary)
            {
                UpoutFromDate = safeValue;
            }
            else
            {
                UpoutToDate = safeValue;
            }

            return;
        }

        if (updateFromBoundary && safeValue is not null && UpoutToDate is not null && safeValue.Value > UpoutToDate.Value.Date)
        {
            UpoutToDate = safeValue.Value;
            return;
        }

        if (!updateFromBoundary && safeValue is not null && UpoutFromDate is not null && safeValue.Value < UpoutFromDate.Value.Date)
        {
            UpoutFromDate = safeValue.Value;
            return;
        }

        OnPropertyChanged(nameof(UpoutFromMaxDate));
        OnPropertyChanged(nameof(UpoutToMinDate));
        OnPropertyChanged(nameof(UpoutToMaxDate));
    }

    private void HandleUpoutDateRangeDayChanged()
    {
        var defaults = RollingDateRangeController.Normalize(
            RollingDateRangeController.DefaultFromDate,
            RollingDateRangeController.DefaultToDate);
        UpoutFromDate = defaults.From;
        UpoutToDate = defaults.To;
        OnPropertyChanged(nameof(UpoutFromMaxDate));
        OnPropertyChanged(nameof(UpoutToMinDate));
        OnPropertyChanged(nameof(UpoutToMaxDate));
    }

    partial void OnAutoIntervalMinutesChanged(int value)
    {
        var next = Math.Clamp(value, 1, 720);
        if (next != value)
            AutoIntervalMinutes = next;

        _autoTimer.Interval = TimeSpan.FromMinutes(next);
    }

    partial void OnIsAutoEnabledChanged(bool value)
    {
        if (value)
        {
            _autoTimer.Start();
            AutoStatus = "自动化监控运行中";
            AddAutoLog("调度", "已启用自动化监控", TraceEntryState.Success);
        }
        else
        {
            _autoTimer.Stop();
            AutoStatus = "自动化监控已停止";
            AddAutoLog("调度", "已停止自动化监控", TraceEntryState.Warning);
        }

        RefreshCommands(RunAutoOnceCommand, ClearAutoLogsCommand);
    }

    partial void OnIsAutoBusyChanged(bool value)
    {
        if (!value)
        {
            AutoRunProgressValue = 0;
            ShowAutoProgress = false;
        }
        RefreshCommandsCoalesced("msfx.auto.busy.commands", () =>
            RefreshCommands(RunAutoOnceCommand, ClearAutoLogsCommand, RefreshAutoBoardCommand));
    }

    partial void OnIsPullPanelBusyChanged(bool value) => RefreshAutoBoardBusy();

    partial void OnIsMapPanelBusyChanged(bool value) => RefreshAutoBoardBusy();

    partial void OnIsTaskPanelBusyChanged(bool value) => RefreshAutoBoardBusy();

    partial void OnAutoPullStateChanged(TraceEntryState value)
    {
        OnPropertyChanged(nameof(IsLastRunSuccess));
        OnPropertyChanged(nameof(IsLastRunWarning));
        OnPropertyChanged(nameof(IsLastRunFailed));
    }

    private void RefreshAutoBoardBusy()
    {
        OnPropertyChanged(nameof(IsAutoBoardBusy));
        RefreshOpsUnlockCommands();
        RefreshCommandsCoalesced("msfx.auto.board.commands", () =>
            RefreshCommands(RefreshAutoBoardCommand));
        PostOnUi(() => OnPropertyChanged(nameof(CanBatchReopenSelectedTasks)), DispatcherPriority.Background);
        PostOnUi(() => OnPropertyChanged(nameof(CanBatchDiscardSelectedTasks)), DispatcherPriority.Background);
        PostOnUi(() => OnPropertyChanged(nameof(CanBatchRemapSelectedTasks)), DispatcherPriority.Background);
        PostOnUi(() => OnPropertyChanged(nameof(CanBatchMergeSelectedTasks)), DispatcherPriority.Background);
        PostOnUi(() => OnPropertyChanged(nameof(CanMergeTasks)), DispatcherPriority.Background);
        PostOnUi(() => OnPropertyChanged(nameof(CanRemapTasks)), DispatcherPriority.Background);
        PostOnUi(() => OnPropertyChanged(nameof(CanDiscardTasks)), DispatcherPriority.Background);
        PostOnUi(() => OnPropertyChanged(nameof(CanReopenTasks)), DispatcherPriority.Background);
        PostOnUi(() => OnPropertyChanged(nameof(CanSplitTasks)), DispatcherPriority.Background);
        PostOnUi(() => OnPropertyChanged(nameof(CanSplitSelectedTasks)), DispatcherPriority.Background);
    }

    private bool ShowPullPanelBusy() => AutoPullBatchRows.Count == 0;

    private bool ShowMapPanelBusy() => AutoMapQueueRows.Count == 0;

    private bool ShowTaskPanelBusy() => AutoTaskQueueRows.Count == 0;

    private Task RunMapPanelQueryAsync(Func<CancellationToken, Task> query)
        => RunLocalReloadAsync(
            _ => { },
            async ct =>
            {
                await RunLocalBusyAsync(
                    ct,
                    v => IsMapPanelBusy = v,
                    () => query(ct),
                    ShowMapPanelBusy()).ConfigureAwait(false);
            });

    partial void OnUpoutPageChanged(int value)
    {
        var normalized = Math.Max(1, value);
        if (normalized != value)
            UpoutPage = normalized;

        OnPropertyChanged(nameof(HasUpoutPrevPage));
        OnPropertyChanged(nameof(HasUpoutNextPage));
    }

    partial void OnUpoutTotalChanged(long value)
    {
        OnPropertyChanged(nameof(UpoutTotalPages));
        OnPropertyChanged(nameof(HasUpoutNextPage));
    }

    partial void OnUpoutPageSizeChanged(string value)
    {
        OnPropertyChanged(nameof(UpoutTotalPages));
        OnPropertyChanged(nameof(HasUpoutNextPage));
    }

    partial void OnSubcodePageChanged(int value)
    {
        var normalized = Math.Max(1, value);
        if (normalized != value)
        {
            SubcodePage = normalized;
            return;
        }

        OnPropertyChanged(nameof(HasSubcodePrevPage));
        OnPropertyChanged(nameof(HasSubcodeNextPage));
        ApplySubCodePage();
    }

    partial void OnSubcodeTotalChanged(int value)
    {
        OnPropertyChanged(nameof(SubcodeTotalPages));
        OnPropertyChanged(nameof(HasSubcodeNextPage));
    }

    partial void OnSubcodePageSizeChanged(string value)
    {
        OnPropertyChanged(nameof(SubcodeTotalPages));
        if (SubcodePage != 1)
        {
            SubcodePage = 1;
        }

        ApplySubCodePage();
        OnPropertyChanged(nameof(HasSubcodeNextPage));
    }

    partial void OnPullBatchPageChanged(int value)
    {
        var normalized = Math.Max(1, value);
        if (normalized != value)
        {
            PullBatchPage = normalized;
            return;
        }

        OnPropertyChanged(nameof(HasPullBatchPrevPage));
        OnPropertyChanged(nameof(HasPullBatchNextPage));
        ApplyPullBatchPage();
    }

    partial void OnPullBatchTotalCountChanged(int value)
    {
        OnPropertyChanged(nameof(PullBatchTotalPages));
        OnPropertyChanged(nameof(HasPullBatchNextPage));
    }

    partial void OnPullBatchPageSizeChanged(string value)
    {
        OnPropertyChanged(nameof(PullBatchTotalPages));
        if (PullBatchPage != 1)
        {
            PullBatchPage = 1;
        }

        ApplyPullBatchPage();
        OnPropertyChanged(nameof(HasPullBatchNextPage));
    }

    partial void OnSelectedTabIndexChanged(int value)
    {
        OnPropertyChanged(nameof(RefreshCommand));
        OnPropertyChanged(nameof(IsRunPage));
        OnPropertyChanged(nameof(IsQueuePage));
        OnPropertyChanged(nameof(IsUpstreamPage));
        OnPropertyChanged(nameof(ShowQueueSearchHeaderAction));
        OnPropertyChanged(nameof(ShowMappingConfigHeaderAction));
        OnPropertyChanged(nameof(IsUpoutQueryMode));
        OnPropertyChanged(nameof(IsSubcodeQueryMode));
        OnPropertyChanged(nameof(ShowUnlock));
        OnPropertyChanged(nameof(ShowLock));
        RefreshOpsUnlock();
        if (value <= 1)
        {
            ClearAllDetailSelectionsSilent();
            _dirtyRefresh.TryRefreshIfDirty(this);
        }
    }

    partial void OnUpstreamQueryModeChanged(int value)
    {
        OnPropertyChanged(nameof(IsUpoutQueryMode));
        OnPropertyChanged(nameof(IsSubcodeQueryMode));
        UpstreamKeyword = value == 0 ? _upoutKeyword : _subcodeKeyword;
        OnPropertyChanged(nameof(HasActiveUpstreamSearch));
    }

    partial void OnMapQueuePageSizeChanged(string value)
    {
        _mapCursorUpdatedAt = null;
        _mapCursorId = null;
        MapQueuePage = 1;
        OnPropertyChanged(nameof(MapQueueEffectivePageSize));
        OnPropertyChanged(nameof(MapQueueTotalPages));
        ObserveDetached(RefreshMapQueueLatestAsync(), "map_queue.refresh.detached.fail");
    }

    partial void OnMapQueueTotalCountChanged(int value)
        => OnPropertyChanged(nameof(MapQueueTotalPages));

    partial void OnMapQueuePageChanged(int value)
    {
        if (value < 1)
            MapQueuePage = 1;
    }

    partial void OnQueueSearchKeywordChanged(string value)
    {
        OnPropertyChanged(nameof(HasActiveMapQueueFilter));
        OnPropertyChanged(nameof(HasActiveTaskQueueFilter));

        if (_suppressQueueSearchRefresh)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(value))
        {
            _queueSearchDebouncer.Cancel();
            ObserveDetached(SearchQueueAsync(), "queue.search.detached.fail");
            return;
        }

        _queueSearchDebouncer.Schedule(async () =>
            await Dispatcher.UIThread.InvokeAsync(SearchQueueAsync));
    }

    partial void OnTaskQueuePageSizeChanged(string value)
    {
        TaskQueuePage = 1;
        ApplyTaskQueueFilter();
    }

    partial void OnTaskQueuePageChanged(int value)
    {
        if (value < 1)
        {
            TaskQueuePage = 1;
            return;
        }

        ApplyTaskQueuePage();
    }

    partial void OnAutoLogPageSizeChanged(string value)
    {
        AutoLogPage = 1;
        ApplyAutoLogPage();
    }

    partial void OnAutoLogPageChanged(int value)
    {
        if (value < 1)
        {
            AutoLogPage = 1;
            return;
        }

        ApplyAutoLogPage();
    }

    partial void OnSelectedAutoPullBatchRowChanged(MsfxAutoPullBatchGridRow? value)
    {
        // 详情仅由行头点击触发，单元格点击不弹窗
    }

    partial void OnTaskQueueBatchModeChanged(TaskQueueBatchActionMode value)
    {
        if (value == TaskQueueBatchActionMode.None)
            ClearTaskQueueChecks();

        OnPropertyChanged(nameof(IsTaskQueueBatchModeActive));
        OnPropertyChanged(nameof(ShowQueueToolbar));
        OnPropertyChanged(nameof(TaskQueueBatchModeTitle));
        OnPropertyChanged(nameof(TaskQueueBatchModeHint));
        OnPropertyChanged(nameof(TaskQueueBatchConfirmText));
        OnPropertyChanged(nameof(CanConfirmTaskQueueBatchAction));
        OnPropertyChanged(nameof(TaskQueuePagerSelectedCount));
        OnPropertyChanged(nameof(CanSplitSelectedTasks));
    }

    partial void OnIsQueueSearchBarVisibleChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowQueueToolbar));
        OnPropertyChanged(nameof(QueueSearchBarToggleIconKind));
        OnPropertyChanged(nameof(QueueSearchBarToggleToolTip));
    }

    public void SetSelectedAutoTaskQueueRows(IReadOnlyList<MsfxAutoTaskQueueGridRow> rows)
    {
        _selectedAutoTaskQueueRowsSnapshot = rows
            .Where(static x => x is not null)
            .Distinct()
            .ToList();
        OnPropertyChanged(nameof(SelectedAutoTaskQueueRowsSnapshot));
        OnPropertyChanged(nameof(CanMergeTasks));
        OnPropertyChanged(nameof(CanRemapTasks));
        OnPropertyChanged(nameof(CanDiscardTasks));
        OnPropertyChanged(nameof(CanReopenTasks));
        OnPropertyChanged(nameof(CanBatchReopenSelectedTasks));
        OnPropertyChanged(nameof(CanBatchDiscardSelectedTasks));
        OnPropertyChanged(nameof(CanBatchRemapSelectedTasks));
        OnPropertyChanged(nameof(CanBatchMergeSelectedTasks));
        OnPropertyChanged(nameof(CanSplitSelectedTasks));
        OnPropertyChanged(nameof(CanConfirmTaskQueueBatchAction));
        OnPropertyChanged(nameof(TaskQueueSelectedCount));
        OnPropertyChanged(nameof(TaskQueuePagerSelectedCount));
    }

    public void SyncAutoTaskQueueSelection()
    {
        if (_syncingTaskQueuePageRows)
        {
            return;
        }

        if (!IsTaskQueueBatchModeActive)
        {
            SetSelectedAutoTaskQueueRows(Array.Empty<MsfxAutoTaskQueueGridRow>());
            return;
        }

        var visibleRowsByTaskId = AutoTaskQueueRows.ToDictionary(static row => row.TaskId);
        foreach (var row in _allTaskQueueRows)
        {
            if (visibleRowsByTaskId.TryGetValue(row.TaskId, out var visibleRow))
            {
                row.IsSelected = visibleRow.IsSelected;
            }
        }

        SetSelectedAutoTaskQueueRows(_filteredTaskQueueRows.Where(x => x.IsSelected).ToArray());
    }

    [RelayCommand]
    private Task FirstTaskQueuePageAsync()
    {
        if (TaskQueuePage <= 1)
        {
            return Task.CompletedTask;
        }

        TaskQueuePage = 1;
        return Task.CompletedTask;
    }

    [RelayCommand]
    private Task PrevTaskQueuePageAsync()
    {
        if (!HasTaskQueuePrevPage)
        {
            return Task.CompletedTask;
        }

        TaskQueuePage -= 1;
        return Task.CompletedTask;
    }

    [RelayCommand]
    private Task NextTaskQueuePageAsync()
    {
        if (!HasTaskQueueNextPage)
        {
            return Task.CompletedTask;
        }

        TaskQueuePage += 1;
        return Task.CompletedTask;
    }

    [RelayCommand]
    private Task LastTaskQueuePageAsync()
    {
        if (!HasTaskQueueNextPage)
        {
            return Task.CompletedTask;
        }

        TaskQueuePage = TaskQueueTotalPages;
        return Task.CompletedTask;
    }

    [RelayCommand]
    private Task FirstAutoLogPageAsync()
    {
        if (AutoLogPage <= 1)
        {
            return Task.CompletedTask;
        }

        AutoLogPage = 1;
        return Task.CompletedTask;
    }

    [RelayCommand]
    private Task PrevAutoLogPageAsync()
    {
        if (!HasAutoLogPrevPage)
        {
            return Task.CompletedTask;
        }

        AutoLogPage -= 1;
        return Task.CompletedTask;
    }

    [RelayCommand]
    private Task NextAutoLogPageAsync()
    {
        if (!HasAutoLogNextPage)
        {
            return Task.CompletedTask;
        }

        AutoLogPage += 1;
        return Task.CompletedTask;
    }

    [RelayCommand]
    private Task LastAutoLogPageAsync()
    {
        if (!HasAutoLogNextPage)
        {
            return Task.CompletedTask;
        }

        AutoLogPage = AutoLogTotalPages;
        return Task.CompletedTask;
    }

    [RelayCommand]
    private void BeginMergeTaskSelection()
        => EnterTaskQueueBatchMode(TaskQueueBatchActionMode.Merge);

    [RelayCommand]
    private void BeginRemapTaskSelection()
        => EnterTaskQueueBatchMode(TaskQueueBatchActionMode.Remap);

    [RelayCommand]
    private void BeginDiscardTaskSelection()
        => EnterTaskQueueBatchMode(TaskQueueBatchActionMode.Discard);

    [RelayCommand]
    private void BeginReopenTaskSelection()
        => EnterTaskQueueBatchMode(TaskQueueBatchActionMode.Reopen);

    [RelayCommand]
    private void BeginSplitTaskSelection()
        => EnterTaskQueueBatchMode(TaskQueueBatchActionMode.Split);

    [RelayCommand]
    private async Task ConfirmTaskQueueBatchActionAsync()
    {
        switch (TaskQueueBatchMode)
        {
            case TaskQueueBatchActionMode.Merge:
                await MergeSelectedTaskAsync().ConfigureAwait(false);
                break;
            case TaskQueueBatchActionMode.Remap:
                await RemapSelectedTaskAsync().ConfigureAwait(false);
                break;
            case TaskQueueBatchActionMode.Discard:
                await DiscardSelectedTaskAsync().ConfigureAwait(false);
                break;
            case TaskQueueBatchActionMode.Reopen:
                await ReopenSelectedTaskAsync().ConfigureAwait(false);
                break;
            case TaskQueueBatchActionMode.Split:
                await SplitSelectedTaskAsync().ConfigureAwait(false);
                break;
        }
    }

    [RelayCommand]
    private void CancelTaskQueueBatchSelection()
        => TaskQueueBatchMode = TaskQueueBatchActionMode.None;

    private void EnterTaskQueueBatchMode(TaskQueueBatchActionMode mode)
    {
        if (IsAutoBoardBusy)
        {
            return;
        }

        ClearTaskQueueChecks();
        TaskQueueBatchMode = mode;
    }

    private void ClearTaskQueueChecks()
    {
        foreach (var row in _allTaskQueueRows)
        {
            row.IsSelected = false;
        }

        _syncingTaskQueuePageRows = true;
        try
        {
            foreach (var row in AutoTaskQueueRows)
            {
                row.IsSelected = false;
            }
        }
        finally
        {
            _syncingTaskQueuePageRows = false;
        }

        SetSelectedAutoTaskQueueRows(Array.Empty<MsfxAutoTaskQueueGridRow>());
    }

    private static bool CanSplitTask(MsfxAutoTaskQueueGridRow row)
        => row.CurrentCodeCount > 1
           && (string.Equals(row.Status, "NEW", StringComparison.OrdinalIgnoreCase)
               || string.Equals(row.Status, "FAILED", StringComparison.OrdinalIgnoreCase)
               || string.Equals(row.Status, "DISCARDED", StringComparison.OrdinalIgnoreCase)
               || string.Equals(row.Status, "CANCELLED", StringComparison.OrdinalIgnoreCase));

    partial void OnSelectedAutoLogRowChanged(MsfxAutoLogRow? value)
    {
        // 仅用户显式触发详情时再弹窗，避免“立即巡检”过程中因选中变更自动弹出
    }

}
