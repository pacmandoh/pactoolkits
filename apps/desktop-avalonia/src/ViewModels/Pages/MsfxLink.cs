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
    private const int MapQueuePreviewPageSize = 20;
    private const int TaskQueuePreviewPageSize = 15;
    private const int AutoLogPreviewPageSize = 15;
    private const int PullBatchPreviewPageSize = 10;
    private static readonly string[] AutoLogPageSizes = ["20", "50", "100"];
    private static readonly string[] MapStatusFilters = ["ALL", "PENDING", "MAPPED", "NEED_REVIEW", "FAILED"];
    private static readonly string[] CodeStatusFilters = ["ALL", "NEW", "TASKED", "FAILED"];
    private static readonly string[] MapQueueSearchScopes =
    [
        "全部字段",
        "最小包装码",
        "单据编码",
        "原始药/规",
        "校正药/规",
        "层级码",
        "映射目标",
        "原因信息"
    ];
    private static readonly string[] TaskQueueSearchScopes =
    [
        "全部字段",
        "单据编号",
        "药品",
        "规格"
    ];
    private static readonly string[] TaskQueueStatusFilters =
    [
        "ALL",
        "NEW",
        "RUNNING",
        "SUCCESS",
        "FAILED",
        "DISCARDED",
        "CANCELLED"
    ];
    private readonly IMsfxApiClient _msfxApi;
    private readonly ISyncService _syncService;
    private readonly IMsfxAutoRunService _autoRun;
    private readonly IAppConfigStore _configStore;
    private readonly ISensitiveUnlockService _unlockService;
    private readonly IToastService _toast;
    private readonly IDialogService _dialog;
    private readonly IBackgroundTaskRunner _backgroundTasks;
    private readonly WorkspaceDirtyRefresh _dirtyRefresh;
    private readonly DispatcherTimer _autoTimer;
    private int _autoTimerTickRunning;
    private int _manualMsfxWriteDepth;

    private bool IsManualMsfxWriteActive => _manualMsfxWriteDepth > 0;

    public override string DisplayName => "码上放心联调";
    public override string Icon => "CloudCog";
    public override int Index => 5;
    public override string FunctionAreaId => ShellFunctionAreas.AutomationId;
    public override ICommand? RefreshCommand => SelectedTabIndex == 0 ? RefreshAutoBoardCommand : null;
    protected override bool AutoRefreshOnDbDisconnected => true;
    protected override bool AutoRefreshOnDbReconnected => true;
    protected override bool CanAutoRefreshFromDbSignal() => SelectedTabIndex == 0 && base.CanAutoRefreshFromDbSignal();

    [ObservableProperty] private int _selectedTabIndex;

    [ObservableProperty] private bool _isAutoEnabled;
    [ObservableProperty] private int _autoIntervalMinutes = 30;
    [ObservableProperty] private bool _isAutoBusy;
    [ObservableProperty] private bool _showAutoProgressPanel;
    [ObservableProperty] private bool _isPullPanelBusy;
    [ObservableProperty] private bool _isMapPanelBusy;
    [ObservableProperty] private bool _isTaskPanelBusy;

    public bool IsAutoBoardBusy => IsPullPanelBusy || IsMapPanelBusy || IsTaskPanelBusy;
    [ObservableProperty] private string _autoStatus = "未启动";
    [ObservableProperty] private double _autoRunProgressValue;
    [ObservableProperty] private string _autoLastRunAtText = "尚未巡检";
    [ObservableProperty] private string? _autoLastRunAtTip;
    [ObservableProperty] private string _autoPullSummary = "批次：暂无";
    [ObservableProperty] private string _autoMapSummary = "映射：暂无";
    [ObservableProperty] private int _autoTaskNewCount;
    [ObservableProperty] private int _autoTaskRunningCount;
    [ObservableProperty] private int _autoTaskSuccessCount;
    [ObservableProperty] private int _autoTaskFailedCount;
    [ObservableProperty] private int _autoTaskDiscardedCount;
    [ObservableProperty] private TraceEntryState _autoPullState = TraceEntryState.Info;
    [ObservableProperty] private TraceEntryState _autoMapState = TraceEntryState.Info;
    [ObservableProperty] private TraceEntryState _autoTaskState = TraceEntryState.Info;
    [ObservableProperty] private TraceEntryState _autoRiskState = TraceEntryState.Info;
    [ObservableProperty] private string _autoExpandedPanel = string.Empty;
    [ObservableProperty] private string _pullBatchPageSize = "20";
    [ObservableProperty] private int _pullBatchPage = 1;
    [ObservableProperty] private int _pullBatchTotalCount;
    [ObservableProperty] private string _mapQueuePageSize = "120";
    [ObservableProperty] private string _mapQueueMapStatusFilter = "ALL";
    [ObservableProperty] private string _mapQueueCodeStatusFilter = "ALL";
    [ObservableProperty] private string _mapQueueSearchScope = "全部字段";
    [ObservableProperty] private string _mapQueueKeyword = string.Empty;
    [ObservableProperty] private int _mapQueueTotalCount;
    [ObservableProperty] private string _mapQueueRangeText = "序号 --";
    [ObservableProperty] private bool _mapQueueHasNewer;
    [ObservableProperty] private bool _mapQueueHasOlder;
    [ObservableProperty] private int _mapQueuePage = 1;
    [ObservableProperty] private bool _isMapQueueSearchPanelVisible;
    [ObservableProperty] private bool _isTaskQueueSearchPanelVisible;
    [ObservableProperty] private string _taskQueueStatusFilter = "ALL";
    [ObservableProperty] private string _taskQueueSearchScope = "全部字段";
    [ObservableProperty] private string _taskQueueKeyword = string.Empty;
    [ObservableProperty] private string _taskQueuePageSize = "50";
    [ObservableProperty] private int _taskQueuePage = 1;
    [ObservableProperty] private string _autoLogPageSize = "50";
    [ObservableProperty] private int _autoLogPage = 1;
    [ObservableProperty] private TaskQueueBatchActionMode _taskQueueBatchMode;

    [ObservableProperty] private DateTime? _upoutFromDate = DateTime.Today.AddDays(-6);
    [ObservableProperty] private DateTime? _upoutToDate = DateTime.Today;
    [ObservableProperty] private string _upoutBillCodeKeyword = string.Empty;
    [ObservableProperty] private string _upoutDrugKeyword = string.Empty;
    [ObservableProperty] private string _upoutFromEntKeyword = string.Empty;
    [ObservableProperty] private string _upoutPageSize = "20";
    [ObservableProperty] private int _upoutPage = 1;
    [ObservableProperty] private long _upoutTotal;
    [ObservableProperty] private string _upoutStatus = "请设置日期后查询";
    [ObservableProperty] private bool _isUpoutBusy;

    [ObservableProperty] private string _subcodeBillCode = string.Empty;
    [ObservableProperty] private string _subcodePageSize = "200";
    [ObservableProperty] private int _subcodePage = 1;
    [ObservableProperty] private int _subcodeTotal;
    [ObservableProperty] private string _subcodeStatus = "请输入单据编码后查询子码";
    [ObservableProperty] private bool _isSubcodeBusy;
    [ObservableProperty] private MsfxAutoPullBatchGridRow? _selectedAutoPullBatchRow;
    [ObservableProperty] private MsfxAutoTaskQueueGridRow? _selectedAutoTaskQueueRow;
    [ObservableProperty] private MsfxAutoLogRow? _selectedAutoLogRow;

    public ObservableCollection<string> UpoutPageSizeOptions { get; } = new(UpoutPageSizes);
    public ObservableCollection<string> PullBatchPageSizeOptions { get; } = new(PullBatchPageSizes);
    public ObservableCollection<string> SubcodePageSizeOptions { get; } = new(SubcodePageSizes);
    public ObservableCollection<string> MapQueuePageSizeOptions { get; } = new(MapQueuePageSizes);
    public ObservableCollection<string> TaskQueuePageSizeOptions { get; } = new(TaskQueuePageSizes);
    public ObservableCollection<string> AutoLogPageSizeOptions { get; } = new(AutoLogPageSizes);
    public ObservableCollection<string> MapStatusFilterOptions { get; } = new(MapStatusFilters);
    public ObservableCollection<string> CodeStatusFilterOptions { get; } = new(CodeStatusFilters);
    public ObservableCollection<string> MapQueueSearchScopeOptions { get; } = new(MapQueueSearchScopes);
    public ObservableCollection<string> TaskQueueSearchScopeOptions { get; } = new(TaskQueueSearchScopes);
    public ObservableCollection<string> TaskQueueStatusFilterOptions { get; } = new(TaskQueueStatusFilters);
    public ObservableCollection<MsfxUpoutGridRow> UpoutRows { get; } = new();
    public ObservableCollection<MsfxSubCodeGridRow> SubCodeRows { get; } = new();
    public ObservableCollection<MsfxAutoLogRow> AutoLogs { get; } = new();
    public ObservableCollection<MsfxAutoLogRow> AutoLogPageRows { get; } = new();
    public ObservableCollection<MsfxAutoPullBatchGridRow> AutoPullBatchRows { get; } = new();
    public ObservableCollection<MsfxAutoMapQueueGridRow> AutoMapQueueRows { get; } = new();
    public ObservableCollection<MsfxAutoTaskQueueGridRow> AutoTaskQueueRows { get; } = new();
    public IReadOnlyList<MsfxAutoTaskQueueGridRow> SelectedAutoTaskQueueRowsSnapshot => _selectedAutoTaskQueueRowsSnapshot;
    public bool IsTaskQueueBatchModeActive => TaskQueueBatchMode != TaskQueueBatchActionMode.None;
    public string TaskQueueBatchModeTitle => TaskQueueBatchMode switch
    {
        TaskQueueBatchActionMode.Merge => "选择要合并的任务",
        TaskQueueBatchActionMode.Remap => "选择要重新映射的任务",
        TaskQueueBatchActionMode.Discard => "选择要弃用的任务",
        TaskQueueBatchActionMode.Reopen => "选择要重开的任务",
        _ => string.Empty
    };
    public string TaskQueueBatchModeHint => TaskQueueBatchMode switch
    {
        TaskQueueBatchActionMode.Merge => "仅可勾选同药名、同规格任务；允许跨单据合并",
        TaskQueueBatchActionMode.Remap => "勾选后会把任务退回映射结果队列重新处理",
        TaskQueueBatchActionMode.Discard => "勾选后会把任务标成弃用，保留追溯但不再执行",
        TaskQueueBatchActionMode.Reopen => "勾选后会把成功或弃用任务重新恢复到待执行",
        _ => string.Empty
    };
    public string TaskQueueBatchConfirmText => TaskQueueBatchMode switch
    {
        TaskQueueBatchActionMode.Merge => "确认合并",
        TaskQueueBatchActionMode.Remap => "确认重新映射",
        TaskQueueBatchActionMode.Discard => "确认弃用",
        TaskQueueBatchActionMode.Reopen => "确认重开",
        _ => "确认"
    };
    public bool CanConfirmTaskQueueBatchAction => TaskQueueBatchMode switch
    {
        TaskQueueBatchActionMode.Merge => CanBatchMergeSelectedTasks,
        TaskQueueBatchActionMode.Remap => CanBatchRemapSelectedTasks,
        TaskQueueBatchActionMode.Discard => CanBatchDiscardSelectedTasks,
        TaskQueueBatchActionMode.Reopen => CanBatchReopenSelectedTasks,
        _ => false
    };
    public bool CanMergeTasks => !IsAutoBoardBusy && AutoTaskQueueRows.Count(x => x.CurrentCodeCount > 0) >= 2;
    public bool CanRemapTasks => !IsAutoBoardBusy && AutoTaskQueueRows.Any(x => !string.Equals(x.Status, "RUNNING", StringComparison.OrdinalIgnoreCase) && x.CurrentCodeCount > 0);
    public bool CanDiscardTasks => !IsAutoBoardBusy && AutoTaskQueueRows.Any(x => x.CurrentCodeCount > 0 && (string.Equals(x.Status, "NEW", StringComparison.OrdinalIgnoreCase) || string.Equals(x.Status, "FAILED", StringComparison.OrdinalIgnoreCase)));
    public bool CanReopenTasks => !IsAutoBoardBusy && AutoTaskQueueRows.Any(x => string.Equals(x.Status, "SUCCESS", StringComparison.OrdinalIgnoreCase) || string.Equals(x.Status, "DISCARDED", StringComparison.OrdinalIgnoreCase));
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
    public bool CanSplitSelectedTasks => !IsAutoBoardBusy
                                         && !IsTaskQueueBatchModeActive
                                         && SelectedAutoTaskQueueRow is not null
                                         && SelectedAutoTaskQueueRow.CurrentCodeCount > 1
                                         && (string.Equals(SelectedAutoTaskQueueRow.Status, "NEW", StringComparison.OrdinalIgnoreCase)
                                             || string.Equals(SelectedAutoTaskQueueRow.Status, "FAILED", StringComparison.OrdinalIgnoreCase)
                                             || string.Equals(SelectedAutoTaskQueueRow.Status, "DISCARDED", StringComparison.OrdinalIgnoreCase)
                                             || string.Equals(SelectedAutoTaskQueueRow.Status, "CANCELLED", StringComparison.OrdinalIgnoreCase));

    public bool IsUpoutEmpty => UpoutRows.Count == 0;
    public bool IsSubCodeEmpty => SubCodeRows.Count == 0;
    public bool IsAutoLogsEmpty => AutoLogs.Count == 0;
    public bool IsAutoPullBatchEmpty => AutoPullBatchRows.Count == 0;
    public bool IsAutoMapQueueEmpty => AutoMapQueueRows.Count == 0;
    public bool IsAutoTaskQueueEmpty => AutoTaskQueueRows.Count == 0;
    public string MapQueueDisplayText => $"显示 {AutoMapQueueRows.Count} / 总 {MapQueueTotalCount}";
    public int PullBatchTotalPages => Math.Max(1, (int)Math.Ceiling(PullBatchTotalCount / (double)GetPullBatchPageSize()));
    public int MapQueueEffectivePageSize => GetMapQueueQueryPageSize();
    public int MapQueueTotalPages => Math.Max(1, (int)Math.Ceiling(MapQueueTotalCount / (double)Math.Max(1, MapQueueEffectivePageSize)));
    public string MapQueuePagerStatusText => $"{MapQueueDisplayText} · {MapQueueRangeText}";
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
    public bool IsAutoExpanded => !string.IsNullOrWhiteSpace(AutoExpandedPanel);
    public bool IsPullPanelExpanded => string.Equals(AutoExpandedPanel, "PULL", StringComparison.OrdinalIgnoreCase);
    public bool IsMapPanelExpanded => string.Equals(AutoExpandedPanel, "MAP", StringComparison.OrdinalIgnoreCase);
    public bool IsTaskPanelExpanded => string.Equals(AutoExpandedPanel, "TASK", StringComparison.OrdinalIgnoreCase);
    public bool IsLogPanelExpanded => string.Equals(AutoExpandedPanel, "LOG", StringComparison.OrdinalIgnoreCase);
    public bool IsUpstreamTab => SelectedTabIndex == 1;
    public bool IsSubcodeTab => SelectedTabIndex == 2;
    public string AutoExpandedPanelTitle => AutoExpandedPanel switch
    {
        "PULL" => "拉取批次明细",
        "MAP" => "映射结果队列",
        "TASK" => "Agent 执行队列",
        "LOG" => "自动化运行审计日志",
        _ => "全屏查看"
    };
    public string AutoExpandedPanelIcon => AutoExpandedPanel switch
    {
        "PULL" => "PackageSearch",
        "MAP" => "Waypoints",
        "TASK" => "Bone",
        "LOG" => "TextSearch",
        _ => "Expand"
    };
    private List<MsfxSubCodeGridRow> _allSubCodeRows = new();
    private List<MsfxAutoPullBatchGridRow> _allPullBatchRows = new();
    private List<MsfxAutoTaskQueueGridRow> _allTaskQueueRows = new();
    private List<MsfxAutoTaskQueueGridRow> _filteredTaskQueueRows = new();
    private List<MsfxAutoTaskQueueGridRow> _selectedAutoTaskQueueRowsSnapshot = new();
    private List<MsfxUpoutGridRow> _allUpoutRows = new();
    private long _upoutLastServerTotal;
    private DateTimeOffset? _mapCursorUpdatedAt;
    private long? _mapCursorId;
    private string? _lastAutoLogSignature;
    private bool _isResettingMapQueueFilters;
    private readonly SearchInputDebouncer _mapQueueSearchDebouncer = new(450);
    private readonly SearchInputDebouncer _taskQueueSearchDebouncer = new(300);
    private readonly SearchInputDebouncer _upoutFilterDebouncer = new(300);
    private readonly RollingDateRangeController _upoutDateRangeController;

    public MsfxLink(
        IMsfxApiClient msfxApi,
        ISyncService syncService,
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
            1 => QueryUpoutAsync(resetPage: false),
            2 => QuerySubCodesAsync(),
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
            ShowAutoProgressPanel = false;
        }
        RefreshCommandsCoalesced("msfx.auto.busy.commands", () =>
            RefreshCommands(RunAutoOnceCommand, ClearAutoLogsCommand, RefreshAutoBoardCommand));
    }

    partial void OnIsPullPanelBusyChanged(bool value) => RefreshAutoBoardBusy();

    partial void OnIsMapPanelBusyChanged(bool value) => RefreshAutoBoardBusy();

    partial void OnIsTaskPanelBusyChanged(bool value) => RefreshAutoBoardBusy();

    private void RefreshAutoBoardBusy()
    {
        OnPropertyChanged(nameof(IsAutoBoardBusy));
        RefreshCommandsCoalesced("msfx.auto.board.commands", () =>
            RefreshCommands(RefreshAutoBoardCommand));
        PostOnUi(() => OnPropertyChanged(nameof(CanBatchReopenSelectedTasks)), DispatcherPriority.Background);
        PostOnUi(() => OnPropertyChanged(nameof(CanBatchDiscardSelectedTasks)), DispatcherPriority.Background);
        PostOnUi(() => OnPropertyChanged(nameof(CanBatchRemapSelectedTasks)), DispatcherPriority.Background);
        PostOnUi(() => OnPropertyChanged(nameof(CanBatchMergeSelectedTasks)), DispatcherPriority.Background);
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

        ApplySubCodePage();
        OnPropertyChanged(nameof(HasSubcodePrevPage));
        OnPropertyChanged(nameof(HasSubcodeNextPage));
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
            return;
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

        ApplyPullBatchPage();
        OnPropertyChanged(nameof(HasPullBatchPrevPage));
        OnPropertyChanged(nameof(HasPullBatchNextPage));
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
            return;
        }

        ApplyPullBatchPage();
        OnPropertyChanged(nameof(HasPullBatchNextPage));
    }

    partial void OnAutoExpandedPanelChanged(string value)
    {
        OnPropertyChanged(nameof(IsAutoExpanded));
        OnPropertyChanged(nameof(IsPullPanelExpanded));
        OnPropertyChanged(nameof(IsMapPanelExpanded));
        OnPropertyChanged(nameof(IsTaskPanelExpanded));
        OnPropertyChanged(nameof(IsLogPanelExpanded));
        OnPropertyChanged(nameof(AutoExpandedPanelTitle));
        OnPropertyChanged(nameof(AutoExpandedPanelIcon));
        OnPropertyChanged(nameof(MapQueueEffectivePageSize));
        OnPropertyChanged(nameof(MapQueueTotalPages));
        ApplyPullBatchPage();
        ApplyTaskQueuePage();
        ApplyAutoLogPage();
        ObserveDetached(RefreshMapQueueLatestAsync(), "map_queue.refresh.detached.fail");
    }

    partial void OnSelectedTabIndexChanged(int value)
    {
        OnPropertyChanged(nameof(RefreshCommand));
        OnPropertyChanged(nameof(IsUpstreamTab));
        OnPropertyChanged(nameof(IsSubcodeTab));
        if (value == 0)
        {
            ClearAllDetailSelectionsSilent();
            _dirtyRefresh.TryRefreshIfDirty(this);
        }
    }

    partial void OnMapQueuePageSizeChanged(string value)
    {
        if (_isResettingMapQueueFilters)
            return;

        _mapCursorUpdatedAt = null;
        _mapCursorId = null;
        MapQueuePage = 1;
        OnPropertyChanged(nameof(MapQueueEffectivePageSize));
        OnPropertyChanged(nameof(MapQueueTotalPages));
        ObserveDetached(RefreshMapQueueLatestAsync(), "map_queue.refresh.detached.fail");
    }

    partial void OnMapQueueMapStatusFilterChanged(string value)
    {
        if (_isResettingMapQueueFilters)
            return;

        _mapCursorUpdatedAt = null;
        _mapCursorId = null;
        MapQueuePage = 1;
        ObserveDetached(RefreshMapQueueLatestAsync(), "map_queue.refresh.detached.fail");
    }

    partial void OnMapQueueCodeStatusFilterChanged(string value)
    {
        if (_isResettingMapQueueFilters)
            return;

        _mapCursorUpdatedAt = null;
        _mapCursorId = null;
        MapQueuePage = 1;
        ObserveDetached(RefreshMapQueueLatestAsync(), "map_queue.refresh.detached.fail");
    }

    partial void OnMapQueueSearchScopeChanged(string value)
    {
        if (_isResettingMapQueueFilters)
            return;

        _mapCursorUpdatedAt = null;
        _mapCursorId = null;
        MapQueuePage = 1;
        ObserveDetached(RefreshMapQueueLatestAsync(), "map_queue.refresh.detached.fail");
    }

    partial void OnMapQueueTotalCountChanged(int value)
    {
        OnPropertyChanged(nameof(MapQueueDisplayText));
        OnPropertyChanged(nameof(MapQueuePagerStatusText));
        OnPropertyChanged(nameof(MapQueueTotalPages));
    }

    partial void OnMapQueueRangeTextChanged(string value)
    {
        OnPropertyChanged(nameof(MapQueuePagerStatusText));
    }

    partial void OnMapQueuePageChanged(int value)
    {
        if (value < 1)
            MapQueuePage = 1;
    }

    partial void OnTaskQueueSearchScopeChanged(string value)
    {
        TaskQueuePage = 1;
        ApplyTaskQueueFilter();
    }

    partial void OnTaskQueueStatusFilterChanged(string value)
    {
        TaskQueuePage = 1;
        ApplyTaskQueueFilter();
    }

    partial void OnTaskQueueKeywordChanged(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            _taskQueueSearchDebouncer.Cancel();
            TaskQueuePage = 1;
            ApplyTaskQueueFilter();
            return;
        }

        _taskQueueSearchDebouncer.Schedule(async () =>
            await Dispatcher.UIThread.InvokeAsync(SearchTaskQueue));
    }

    partial void OnMapQueueKeywordChanged(string value)
    {
        if (_isResettingMapQueueFilters)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(value))
        {
            _mapQueueSearchDebouncer.Cancel();
            ObserveDetached(SearchMapQueueAsync(), "map_queue.search.detached.fail");
            return;
        }

        _mapQueueSearchDebouncer.Schedule(async () =>
            await Dispatcher.UIThread.InvokeAsync(SearchMapQueueAsync));
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

    partial void OnSelectedAutoTaskQueueRowChanged(MsfxAutoTaskQueueGridRow? value)
    {
        OnPropertyChanged(nameof(CanMergeTasks));
        OnPropertyChanged(nameof(CanRemapTasks));
        OnPropertyChanged(nameof(CanDiscardTasks));
        OnPropertyChanged(nameof(CanReopenTasks));
        OnPropertyChanged(nameof(CanBatchReopenSelectedTasks));
        OnPropertyChanged(nameof(CanBatchDiscardSelectedTasks));
        OnPropertyChanged(nameof(CanBatchRemapSelectedTasks));
        OnPropertyChanged(nameof(CanBatchMergeSelectedTasks));
        OnPropertyChanged(nameof(CanSplitSelectedTasks));
    }

    partial void OnTaskQueueBatchModeChanged(TaskQueueBatchActionMode value)
    {
        if (value == TaskQueueBatchActionMode.None)
            ClearTaskQueueChecks();

        OnPropertyChanged(nameof(IsTaskQueueBatchModeActive));
        OnPropertyChanged(nameof(TaskQueueBatchModeTitle));
        OnPropertyChanged(nameof(TaskQueueBatchModeHint));
        OnPropertyChanged(nameof(TaskQueueBatchConfirmText));
        OnPropertyChanged(nameof(CanConfirmTaskQueueBatchAction));
        OnPropertyChanged(nameof(CanSplitSelectedTasks));
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
    }

    public void SyncCheckedAutoTaskQueueRows()
    {
        if (!IsTaskQueueBatchModeActive)
        {
            SetSelectedAutoTaskQueueRows(Array.Empty<MsfxAutoTaskQueueGridRow>());
            return;
        }

        SetSelectedAutoTaskQueueRows(AutoTaskQueueRows.Where(x => x.IsChecked).ToArray());
    }

    [RelayCommand]
    private void SearchTaskQueue()
    {
        _taskQueueSearchDebouncer.Cancel();
        TaskQueuePage = 1;
        ApplyTaskQueueFilter();
    }

    [RelayCommand]
    private void ClearTaskQueueSearch()
    {
        TaskQueueStatusFilter = "ALL";
        TaskQueueSearchScope = "全部字段";
        TaskQueueKeyword = string.Empty;
        TaskQueuePage = 1;
        ApplyTaskQueueFilter();
    }

    [RelayCommand]
    private void ToggleTaskQueueSearchPanel()
    {
        IsTaskQueueSearchPanelVisible = !IsTaskQueueSearchPanelVisible;
    }

    [RelayCommand]
    private void ToggleMapQueueSearchPanel()
    {
        IsMapQueueSearchPanelVisible = !IsMapQueueSearchPanelVisible;
    }

    [RelayCommand]
    private Task FirstTaskQueuePageAsync()
    {
        if (TaskQueuePage <= 1)
        {
            return Task.CompletedTask;
        }

        TaskQueuePage = 1;
        ApplyTaskQueuePage();
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
        ApplyTaskQueuePage();
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
        ApplyTaskQueuePage();
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
        ApplyTaskQueuePage();
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
        ApplyAutoLogPage();
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
        ApplyAutoLogPage();
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
        ApplyAutoLogPage();
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
        ApplyAutoLogPage();
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

        SelectedAutoTaskQueueRow = null;
        ClearTaskQueueChecks();
        TaskQueueBatchMode = mode;
    }

    private void ClearTaskQueueChecks()
    {
        foreach (var row in _allTaskQueueRows)
        {
            row.IsChecked = false;
        }

        foreach (var row in AutoTaskQueueRows)
        {
            row.IsChecked = false;
        }

        SetSelectedAutoTaskQueueRows(Array.Empty<MsfxAutoTaskQueueGridRow>());
    }

    partial void OnSelectedAutoLogRowChanged(MsfxAutoLogRow? value)
    {
        // 仅用户显式触发详情时再弹窗，避免“立即巡检”过程中因选中变更自动弹出
    }

}
