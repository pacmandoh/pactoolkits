using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Diagnostics;
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
using PacToolkits.Application.Services;
using PacToolkits.Desktop.Avalonia.Common;
using PacToolkits.Desktop.Avalonia.Services.Infrastructure;
using PacToolkits.Desktop.Avalonia.Services.Integration;

namespace PacToolkits.Desktop.Avalonia.ViewModels.Pages;

public sealed record MsfxUpoutGridRow(
    string BillCode,
    string BillType,
    string BillTime,
    string DrugName,
    string PackageSpec,
    string PrepnSpec,
    long CodeCount,
    long PrepnCount,
    string ProduceBatchNo,
    string ExpireDate,
    string FromEntName,
    string FromRefUserId,
    string ProduceEntName,
    string LogisticsStatus,
    TraceEntryState State
);

public sealed record MsfxSubCodeGridRow(
    string BillCode,
    string DrugName,
    string PackageSpec,
    string PrepnSpec,
    string BatchNo,
    string Code,
    string Level1Code,
    string Level2Code,
    string Level3Code,
    string Level4Code,
    string Level5Code,
    TraceEntryState State
);

public sealed record MsfxAutoLogRow(
    string At,
    string Stage,
    string Message,
    TraceEntryState State
);

public sealed record MsfxAutoPullBatchGridRow(
    long BatchId,
    string SourceApi,
    string Window,
    string Status,
    int SuccessCount,
    int FailCount,
    string StartedAt,
    string FinishedAt,
    string ErrMsg,
    TraceEntryState State
);

public sealed record MsfxAutoMapQueueGridRow(
    int DisplayIndex,
    long StagingId,
    string LeafCode,
    string ProduceBatchNo,
    string SourceBillTime,
    string SourceBillCode,
    string SourceDrugNameRaw,
    string SourceSpecRaw,
    string SourceNameNorm,
    string SourceSpecNorm,
    string SourceCodeLevel1,
    string SourceCodeLevel2,
    string SourceCodeLevel3,
    string SourceCodeLevel4,
    string SourceCodeLevel5,
    string MapReasonCode,
    string MapReasonDetail,
    string MappedDrugId,
    string MappedSpec,
    string MapStatus,
    string CodeStatus,
    string UpdatedAt,
    TraceEntryState State,
    DateTimeOffset UpdatedAtRaw
);

public sealed partial class MsfxAutoTaskQueueGridRow : ObservableObject
{
    public MsfxAutoTaskQueueGridRow(
        long TaskId,
        string SourceBillCode,
        string BatchNos,
        string MappedDrugId,
        string MappedSpec,
        int TotalCodes,
        int CurrentCodeCount,
        string Target,
        string Status,
        string Progress,
        int RetryCount,
        string CreatedAt,
        string PickedAt,
        string FinishedAt,
        string ErrMsg,
        TraceEntryState State)
    {
        this.TaskId = TaskId;
        this.SourceBillCode = SourceBillCode;
        this.BatchNos = BatchNos;
        this.MappedDrugId = MappedDrugId;
        this.MappedSpec = MappedSpec;
        this.TotalCodes = TotalCodes;
        this.CurrentCodeCount = CurrentCodeCount;
        this.Target = Target;
        this.Status = Status;
        this.Progress = Progress;
        this.RetryCount = RetryCount;
        this.CreatedAt = CreatedAt;
        this.PickedAt = PickedAt;
        this.FinishedAt = FinishedAt;
        this.ErrMsg = ErrMsg;
        this.State = State;
    }

    public long TaskId { get; }
    public string SourceBillCode { get; }
    public string BatchNos { get; }
    public string MappedDrugId { get; }
    public string MappedSpec { get; }
    public int TotalCodes { get; }
    public int CurrentCodeCount { get; }
    public string Target { get; }
    public string Status { get; }
    public string Progress { get; }
    public int RetryCount { get; }
    public string CreatedAt { get; }
    public string PickedAt { get; }
    public string FinishedAt { get; }
    public string ErrMsg { get; }
    public TraceEntryState State { get; }

    [ObservableProperty] private bool _isChecked;
}

public enum TaskQueueBatchActionMode
{
    None = 0,
    Merge = 1,
    Remap = 2,
    Discard = 3,
    Reopen = 4
}

public sealed partial class MsfxLinkViewModel : AppPageBase
{
    private const int AutoLogMaxRows = 500;
    private const int CompactMapQueueRows = 120;
    private static readonly string[] PullBatchPageSizes = ["20", "50", "100"];
    private static readonly string[] UpoutPageSizes = ["20", "30", "50", "100"];
    private static readonly string[] SubcodePageSizes = ["100", "200", "500", "1000"];
    private static readonly string[] MapQueuePageSizes = ["120", "240", "500"];
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
    private readonly IMsfxSyncService _syncService;
    private readonly IAppConfigStore _configStore;
    private readonly ISensitiveOperationUnlockService _unlockService;
    private readonly IToastService _toast;
    private readonly IDialogService _dialog;
    private readonly DispatcherTimer _autoTimer;

    public override string DisplayName => "码上放心联调";
    public override string Icon => "CloudCog";
    public override int Index => 5;
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
    [ObservableProperty] private string _autoLastRunAtText = "--";
    [ObservableProperty] private string _autoNextRunAtText = "--";
    [ObservableProperty] private string _autoPullSummary = "批次：暂无";
    [ObservableProperty] private string _autoMapSummary = "映射：暂无";
    [ObservableProperty] private string _autoTaskSummary = "任务：暂无";
    [ObservableProperty] private string _autoRiskSummary = "异常：暂无";
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
    [ObservableProperty] private bool _hasPullBatchPrevPage;
    [ObservableProperty] private bool _hasPullBatchNextPage;
    [ObservableProperty] private string _mapQueuePageSize = "120";
    [ObservableProperty] private string _mapQueueMapStatusFilter = "ALL";
    [ObservableProperty] private string _mapQueueCodeStatusFilter = "ALL";
    [ObservableProperty] private string _mapQueueSearchScope = "全部字段";
    [ObservableProperty] private string _mapQueueKeyword = string.Empty;
    [ObservableProperty] private int _mapQueueTotalCount;
    [ObservableProperty] private string _mapQueueRangeText = "序号 --";
    [ObservableProperty] private bool _hasMapQueuePrevPage;
    [ObservableProperty] private bool _hasMapQueueNextPage;
    [ObservableProperty] private bool _isMapQueueLatestPage = true;
    [ObservableProperty] private int _mapQueuePage = 1;
    [ObservableProperty] private bool _isTaskQueueSearchPanelVisible;
    [ObservableProperty] private string _taskQueueStatusFilter = "ALL";
    [ObservableProperty] private string _taskQueueSearchScope = "全部字段";
    [ObservableProperty] private string _taskQueueKeyword = string.Empty;
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
    [ObservableProperty] private string _subcodeRefEntId = string.Empty;
    [ObservableProperty] private string _subcodePageSize = "200";
    [ObservableProperty] private int _subcodePage = 1;
    [ObservableProperty] private int _subcodeTotal;
    [ObservableProperty] private string _subcodeStatus = "请输入单据编码后查询子码";
    [ObservableProperty] private bool _isSubcodeBusy;
    [ObservableProperty] private MsfxAutoPullBatchGridRow? _selectedAutoPullBatchRow;
    [ObservableProperty] private MsfxAutoMapQueueGridRow? _selectedAutoMapQueueRow;
    [ObservableProperty] private MsfxAutoTaskQueueGridRow? _selectedAutoTaskQueueRow;
    [ObservableProperty] private MsfxAutoLogRow? _selectedAutoLogRow;

    public ObservableCollection<string> UpoutPageSizeOptions { get; } = new(UpoutPageSizes);
    public ObservableCollection<string> PullBatchPageSizeOptions { get; } = new(PullBatchPageSizes);
    public ObservableCollection<string> SubcodePageSizeOptions { get; } = new(SubcodePageSizes);
    public ObservableCollection<string> MapQueuePageSizeOptions { get; } = new(MapQueuePageSizes);
    public ObservableCollection<string> MapStatusFilterOptions { get; } = new(MapStatusFilters);
    public ObservableCollection<string> CodeStatusFilterOptions { get; } = new(CodeStatusFilters);
    public ObservableCollection<string> MapQueueSearchScopeOptions { get; } = new(MapQueueSearchScopes);
    public ObservableCollection<string> TaskQueueSearchScopeOptions { get; } = new(TaskQueueSearchScopes);
    public ObservableCollection<string> TaskQueueStatusFilterOptions { get; } = new(TaskQueueStatusFilters);
    public ObservableCollection<MsfxUpoutGridRow> UpoutRows { get; } = new();
    public ObservableCollection<MsfxSubCodeGridRow> SubCodeRows { get; } = new();
    public ObservableCollection<MsfxAutoLogRow> AutoLogs { get; } = new();
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
        TaskQueueBatchActionMode.Merge => "仅可勾选同药名、同规格任务；允许跨单据合并。",
        TaskQueueBatchActionMode.Remap => "勾选后会把任务退回映射结果队列重新处理。",
        TaskQueueBatchActionMode.Discard => "勾选后会把任务标成弃用，保留追溯但不再执行。",
        TaskQueueBatchActionMode.Reopen => "勾选后会把成功或弃用任务重新恢复到待执行。",
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
    public bool CanEnterTaskMergeMode => !IsAutoBoardBusy && AutoTaskQueueRows.Count(x => x.CurrentCodeCount > 0) >= 2;
    public bool CanEnterTaskRemapMode => !IsAutoBoardBusy && AutoTaskQueueRows.Any(x => !string.Equals(x.Status, "RUNNING", StringComparison.OrdinalIgnoreCase) && x.CurrentCodeCount > 0);
    public bool CanEnterTaskDiscardMode => !IsAutoBoardBusy && AutoTaskQueueRows.Any(x => x.CurrentCodeCount > 0 && (string.Equals(x.Status, "NEW", StringComparison.OrdinalIgnoreCase) || string.Equals(x.Status, "FAILED", StringComparison.OrdinalIgnoreCase)));
    public bool CanEnterTaskReopenMode => !IsAutoBoardBusy && AutoTaskQueueRows.Any(x => string.Equals(x.Status, "SUCCESS", StringComparison.OrdinalIgnoreCase) || string.Equals(x.Status, "DISCARDED", StringComparison.OrdinalIgnoreCase));
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
    public string PullBatchPageText => $"第 {PullBatchPage} / {PullBatchTotalPages} 页";
    public int MapQueueEffectivePageSize => IsMapPanelExpanded ? GetMapQueuePageSize() : CompactMapQueueRows;
    public int MapQueueTotalPages => Math.Max(1, (int)Math.Ceiling(MapQueueTotalCount / (double)Math.Max(1, MapQueueEffectivePageSize)));
    public string MapQueuePageText => $"第 {MapQueuePage} / {MapQueueTotalPages} 页";
    public bool HasUpoutPrevPage => UpoutPage > 1;
    public int UpoutTotalPages => Math.Max(1, (int)Math.Ceiling(UpoutTotal / (double)GetPageSize()));
    public bool HasUpoutNextPage => UpoutPage < UpoutTotalPages;
    public string UpoutPageText => $"第 {UpoutPage} / {UpoutTotalPages} 页";
    public bool HasSubcodePrevPage => SubcodePage > 1;
    public int SubcodeTotalPages => Math.Max(1, (int)Math.Ceiling(SubcodeTotal / (double)GetSubcodePageSize()));
    public bool HasSubcodeNextPage => SubcodePage < SubcodeTotalPages;
    public string SubcodePageText => $"第 {SubcodePage} / {SubcodeTotalPages} 页";
    public DateTime? UpoutFromMaxDate => UpoutToDate?.Date;
    public DateTime? UpoutToMinDate => UpoutFromDate?.Date;
    public DateTime? UpoutToMaxDate => DateTime.Today;
    public bool IsAutoExpanded => !string.IsNullOrWhiteSpace(AutoExpandedPanel);
    public bool IsPullPanelExpanded => string.Equals(AutoExpandedPanel, "PULL", StringComparison.OrdinalIgnoreCase);
    public bool IsMapPanelExpanded => string.Equals(AutoExpandedPanel, "MAP", StringComparison.OrdinalIgnoreCase);
    public bool IsTaskPanelExpanded => string.Equals(AutoExpandedPanel, "TASK", StringComparison.OrdinalIgnoreCase);
    public bool IsLogPanelExpanded => string.Equals(AutoExpandedPanel, "LOG", StringComparison.OrdinalIgnoreCase);
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
        "TASK" => "Syringe",
        "LOG" => "TextSearch",
        _ => "Expand"
    };
    private List<MsfxSubCodeGridRow> _allSubCodeRows = new();
    private List<MsfxAutoPullBatchGridRow> _allPullBatchRows = new();
    private List<MsfxAutoTaskQueueGridRow> _allTaskQueueRows = new();
    private List<MsfxAutoTaskQueueGridRow> _selectedAutoTaskQueueRowsSnapshot = new();
    private DateTimeOffset? _mapCursorUpdatedAt;
    private long? _mapCursorId;
    private string? _lastAutoLogSignature;
    private bool _isResettingMapQueueFilters;
    private readonly RollingDateRangeController _upoutDateRangeController;

    public MsfxLinkViewModel(
        IMsfxApiClient msfxApi,
        IMsfxSyncService syncService,
        IAppConfigStore configStore,
        ISensitiveOperationUnlockService unlockService,
        IToastService toast,
        IDialogService dialog)
    {
        _msfxApi = msfxApi;
        _syncService = syncService;
        _configStore = configStore;
        _unlockService = unlockService;
        _toast = toast;
        _dialog = dialog;
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

        var refEntId = (_configStore.Load().MsfxApi?.RefEntId ?? string.Empty).Trim();
        SubcodeRefEntId = refEntId;
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
            0 => RefreshAutoBoardAsync(),
            1 => QueryUpoutCoreAsync(resetPage: false),
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
        if (IsAutoEnabled)
            AutoNextRunAtText = DateTime.Now.AddMinutes(next).ToString("yyyy-MM-dd HH:mm:ss");
    }

    partial void OnIsAutoEnabledChanged(bool value)
    {
        if (value)
        {
            _autoTimer.Start();
            AutoStatus = "自动化监控运行中";
            AutoNextRunAtText = DateTime.Now.AddMinutes(Math.Max(1, AutoIntervalMinutes)).ToString("yyyy-MM-dd HH:mm:ss");
            AddAutoLog("调度", "已启用自动化监控", TraceEntryState.Success);
        }
        else
        {
            _autoTimer.Stop();
            AutoStatus = "自动化监控已停止";
            AutoNextRunAtText = "--";
            AddAutoLog("调度", "已停止自动化监控", TraceEntryState.Warning);
        }

        NotifyCommands(RunAutoOnceCommand, ClearAutoLogsCommand);
    }

    partial void OnIsAutoBusyChanged(bool value)
    {
        if (!value)
        {
            AutoRunProgressValue = 0;
            ShowAutoProgressPanel = false;
        }
        NotifyCommandsCoalesced("msfx.auto.busy.commands", () =>
            NotifyCommands(RunAutoOnceCommand, ClearAutoLogsCommand, RefreshAutoBoardCommand));
    }

    partial void OnIsPullPanelBusyChanged(bool value) => NotifyAutoBoardBusyStateChanged();

    partial void OnIsMapPanelBusyChanged(bool value) => NotifyAutoBoardBusyStateChanged();

    partial void OnIsTaskPanelBusyChanged(bool value) => NotifyAutoBoardBusyStateChanged();

    private void NotifyAutoBoardBusyStateChanged()
    {
        OnPropertyChanged(nameof(IsAutoBoardBusy));
        NotifyCommandsCoalesced("msfx.auto.board.commands", () =>
            NotifyCommands(RefreshAutoBoardCommand));
        PostOnUi(() => OnPropertyChanged(nameof(CanBatchReopenSelectedTasks)), DispatcherPriority.Background);
        PostOnUi(() => OnPropertyChanged(nameof(CanBatchDiscardSelectedTasks)), DispatcherPriority.Background);
        PostOnUi(() => OnPropertyChanged(nameof(CanBatchRemapSelectedTasks)), DispatcherPriority.Background);
        PostOnUi(() => OnPropertyChanged(nameof(CanBatchMergeSelectedTasks)), DispatcherPriority.Background);
        PostOnUi(() => OnPropertyChanged(nameof(CanSplitSelectedTasks)), DispatcherPriority.Background);
    }

    private bool ShouldShowPullPanelBusy() => AutoPullBatchRows.Count == 0;

    private bool ShouldShowMapPanelBusy() => AutoMapQueueRows.Count == 0;

    private bool ShouldShowTaskPanelBusy() => AutoTaskQueueRows.Count == 0;

    private Task RunMapPanelQueryAsync(Func<CancellationToken, Task> query)
        => RunLocalReloadAsync(
            _ => { },
            async ct =>
            {
                await RunLocalBusyAsync(
                    ct,
                    v => IsMapPanelBusy = v,
                    () => query(ct),
                    ShouldShowMapPanelBusy()).ConfigureAwait(false);
            });

    partial void OnUpoutPageChanged(int value)
    {
        var normalized = Math.Max(1, value);
        if (normalized != value)
            UpoutPage = normalized;

        OnPropertyChanged(nameof(UpoutPageText));
        OnPropertyChanged(nameof(HasUpoutPrevPage));
        OnPropertyChanged(nameof(HasUpoutNextPage));
    }

    partial void OnUpoutTotalChanged(long value)
    {
        OnPropertyChanged(nameof(UpoutTotalPages));
        OnPropertyChanged(nameof(UpoutPageText));
        OnPropertyChanged(nameof(HasUpoutNextPage));
    }

    partial void OnUpoutPageSizeChanged(string value)
    {
        OnPropertyChanged(nameof(UpoutTotalPages));
        OnPropertyChanged(nameof(UpoutPageText));
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
        OnPropertyChanged(nameof(SubcodePageText));
        OnPropertyChanged(nameof(HasSubcodePrevPage));
        OnPropertyChanged(nameof(HasSubcodeNextPage));
    }

    partial void OnSubcodeTotalChanged(int value)
    {
        OnPropertyChanged(nameof(SubcodeTotalPages));
        OnPropertyChanged(nameof(SubcodePageText));
        OnPropertyChanged(nameof(HasSubcodeNextPage));
    }

    partial void OnSubcodePageSizeChanged(string value)
    {
        OnPropertyChanged(nameof(SubcodeTotalPages));
        OnPropertyChanged(nameof(SubcodePageText));
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
        OnPropertyChanged(nameof(PullBatchPageText));
        OnPropertyChanged(nameof(HasPullBatchPrevPage));
        OnPropertyChanged(nameof(HasPullBatchNextPage));
    }

    partial void OnPullBatchTotalCountChanged(int value)
    {
        OnPropertyChanged(nameof(PullBatchTotalPages));
        OnPropertyChanged(nameof(PullBatchPageText));
        OnPropertyChanged(nameof(HasPullBatchNextPage));
    }

    partial void OnPullBatchPageSizeChanged(string value)
    {
        OnPropertyChanged(nameof(PullBatchTotalPages));
        OnPropertyChanged(nameof(PullBatchPageText));
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
        OnPropertyChanged(nameof(MapQueuePageText));
        if (string.Equals(value, "MAP", StringComparison.OrdinalIgnoreCase) && AutoMapQueueRows.Count == 0)
            _ = RefreshMapQueueLatestAsync();
    }

    partial void OnSelectedTabIndexChanged(int value)
    {
        OnPropertyChanged(nameof(RefreshCommand));
        if (value == 0)
            ClearAllDetailSelectionsSilent();
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
        OnPropertyChanged(nameof(MapQueuePageText));
        _ = RefreshMapQueueLatestAsync();
    }

    partial void OnMapQueueMapStatusFilterChanged(string value)
    {
        if (_isResettingMapQueueFilters)
            return;

        _mapCursorUpdatedAt = null;
        _mapCursorId = null;
        MapQueuePage = 1;
        _ = RefreshMapQueueLatestAsync();
    }

    partial void OnMapQueueCodeStatusFilterChanged(string value)
    {
        if (_isResettingMapQueueFilters)
            return;

        _mapCursorUpdatedAt = null;
        _mapCursorId = null;
        MapQueuePage = 1;
        _ = RefreshMapQueueLatestAsync();
    }

    partial void OnMapQueueSearchScopeChanged(string value)
    {
        if (_isResettingMapQueueFilters)
            return;

        _mapCursorUpdatedAt = null;
        _mapCursorId = null;
        MapQueuePage = 1;
        _ = RefreshMapQueueLatestAsync();
    }

    partial void OnMapQueueTotalCountChanged(int value)
    {
        OnPropertyChanged(nameof(MapQueueDisplayText));
        OnPropertyChanged(nameof(MapQueueTotalPages));
        OnPropertyChanged(nameof(MapQueuePageText));
    }

    partial void OnIsMapQueueLatestPageChanged(bool value)
    {
        OnPropertyChanged(nameof(MapQueueDisplayText));
        OnPropertyChanged(nameof(MapQueuePageText));
    }

    partial void OnMapQueuePageChanged(int value)
    {
        if (value < 1)
            MapQueuePage = 1;
        OnPropertyChanged(nameof(MapQueuePageText));
    }

    partial void OnTaskQueueSearchScopeChanged(string value)
    {
        if (IsTaskPanelExpanded)
            ApplyTaskQueueFilter();
    }

    partial void OnTaskQueueStatusFilterChanged(string value)
    {
        if (IsTaskPanelExpanded)
            ApplyTaskQueueFilter();
    }

    partial void OnTaskQueueKeywordChanged(string value)
    {
        if (string.IsNullOrWhiteSpace(value) && IsTaskPanelExpanded)
            ApplyTaskQueueFilter();
    }

    partial void OnSelectedAutoPullBatchRowChanged(MsfxAutoPullBatchGridRow? value)
    {
        // 详情仅由行头点击触发，单元格点击不弹窗
    }

    partial void OnSelectedAutoMapQueueRowChanged(MsfxAutoMapQueueGridRow? value)
    {
        // 详情仅由行头点击触发，单元格点击不弹窗
    }

    partial void OnSelectedAutoTaskQueueRowChanged(MsfxAutoTaskQueueGridRow? value)
    {
        OnPropertyChanged(nameof(CanEnterTaskMergeMode));
        OnPropertyChanged(nameof(CanEnterTaskRemapMode));
        OnPropertyChanged(nameof(CanEnterTaskDiscardMode));
        OnPropertyChanged(nameof(CanEnterTaskReopenMode));
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
        OnPropertyChanged(nameof(CanEnterTaskMergeMode));
        OnPropertyChanged(nameof(CanEnterTaskRemapMode));
        OnPropertyChanged(nameof(CanEnterTaskDiscardMode));
        OnPropertyChanged(nameof(CanEnterTaskReopenMode));
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
        ApplyTaskQueueFilter();
    }

    [RelayCommand]
    private void ClearTaskQueueSearch()
    {
        TaskQueueStatusFilter = "ALL";
        TaskQueueSearchScope = "全部字段";
        TaskQueueKeyword = string.Empty;
        ApplyTaskQueueFilter();
    }

    [RelayCommand]
    private void ToggleTaskQueueSearchPanel()
    {
        IsTaskQueueSearchPanelVisible = !IsTaskQueueSearchPanelVisible;
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
        // 仅用户显式触发详情时再弹窗，避免“立即巡检”过程中因选中变更自动弹出。
    }

    [RelayCommand]
    private async Task QueryUpoutAsync()
    {
        await QueryUpoutCoreAsync(resetPage: true).ConfigureAwait(false);
    }

    private async Task QueryUpoutCoreAsync(bool resetPage)
    {
        if (IsUpoutBusy)
        {
            return;
        }

        if (UpoutFromDate is null || UpoutToDate is null)
        {
            _toast.Warn("上游出库单查询", "请先选择开始和结束日期");
            return;
        }

        var fromDate = UpoutFromDate.Value.Date;
        var toDate = UpoutToDate.Value.Date;
        if (fromDate > toDate)
        {
            _toast.Warn("上游出库单查询", "开始日期不能晚于结束日期");
            return;
        }

        if (resetPage)
        {
            UpoutPage = 1;
        }

        IsUpoutBusy = true;
        try
        {
            var options = BuildMsfxOptions();
            var request = new MsfxListUpoutRequest(
                RefEntId: options.RefEntId,
                BeginDate: fromDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                EndDate: toDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                Page: UpoutPage,
                PageSize: GetPageSize());

            using var cts = CreateMsfxTimeout(options.TimeoutSeconds);
            var result = await _msfxApi.GetYljgListUpoutAsync(options, request, cts.Token).ConfigureAwait(false);
            if (!result.Call.Ok)
            {
                await RunOnUiAsync(() =>
                {
                    UpoutRows.Clear();
                    UpoutTotal = 0;
                    UpoutStatus = $"查询失败：{result.Call.BizCode} {result.Call.BizMessage}".Trim();
                    _toast.Error("上游出库单查询", UpoutStatus);
                });
                return;
            }

            var filtered = result.Items
                .Where(MatchUpoutFilter)
                .Select(x => new MsfxUpoutGridRow(
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
                    State: ParseUpoutState(x.Status)))
                .ToList();

            await RunOnUiAsync(() =>
            {
                UpoutRows.Clear();
                foreach (var row in filtered)
                {
                    UpoutRows.Add(row);
                }

                UpoutTotal = result.Total;
                UpoutStatus = $"查询成功：第 {UpoutPage} 页 / 返回 {result.Items.Count} 条 / 筛选后 {filtered.Count} 条 / 服务器总数 {result.Total}";
            });
        }
        catch (Exception ex)
        {
            await RunOnUiAsync(() =>
            {
                UpoutStatus = $"查询异常：{ex.Message}";
                _toast.Error("上游出库单查询", ex.Message);
            });
        }
        finally
        {
            await RunOnUiAsync(() => IsUpoutBusy = false);
        }
    }

    [RelayCommand]
    private async Task PrevUpoutPageAsync()
    {
        if (!HasUpoutPrevPage || IsUpoutBusy)
        {
            return;
        }

        UpoutPage -= 1;
        await QueryUpoutCoreAsync(resetPage: false);
    }

    [RelayCommand]
    private async Task NextUpoutPageAsync()
    {
        if (!HasUpoutNextPage || IsUpoutBusy)
        {
            return;
        }

        UpoutPage += 1;
        await QueryUpoutCoreAsync(resetPage: false);
    }

    [RelayCommand]
    private async Task QuerySubCodesAsync()
    {
        if (IsSubcodeBusy)
        {
            return;
        }

        var billCode = (SubcodeBillCode ?? string.Empty).Trim();
        if (billCode.Length == 0)
        {
            _toast.Warn("子码查询", "请先输入单据编码");
            return;
        }

        IsSubcodeBusy = true;
        try
        {
            var options = BuildMsfxOptions();
            var route = await ResolveBillRouteFromListUpoutAsync(options, billCode).ConfigureAwait(false);
            if (route is null || string.IsNullOrWhiteSpace(route.Value.FromRefUserId))
            {
                await RunOnUiAsync(() =>
                {
                    _allSubCodeRows.Clear();
                    SubCodeRows.Clear();
                    SubcodeTotal = 0;
                    SubcodePage = 1;
                    SubcodeStatus = "未从上游出库单查询到该单据的 from_ref_user_id";
                    _toast.Error("子码查询", SubcodeStatus);
                });
                return;
            }

            using var cts = CreateMsfxTimeout(options.TimeoutSeconds);
            var req = new MsfxListUpoutDetailRequest(
                RefEntId: options.RefEntId,
                BillCode: billCode,
                ToRefUserId: route.Value.ToRefUserId,
                FromRefUserId: route.Value.FromRefUserId);
            var detail = await _msfxApi.GetYljgListUpoutDetailAsync(options, req, cts.Token).ConfigureAwait(false);

            if (!detail.Call.Ok)
            {
                await RunOnUiAsync(() =>
                {
                    _allSubCodeRows.Clear();
                    SubCodeRows.Clear();
                    SubcodeTotal = 0;
                    SubcodePage = 1;
                    SubcodeStatus = $"查询失败：{detail.Call.BizCode} {detail.Call.BizMessage}".Trim();
                    _toast.Error("子码查询", SubcodeStatus);
                });
                return;
            }

            var rows = detail.DrugItems
                .SelectMany(drug => drug.TraceCodes.Select(code => new MsfxSubCodeGridRow(
                    BillCode: detail.BillCode,
                    DrugName: drug.PhysicName,
                    PackageSpec: drug.PackageSpec,
                    PrepnSpec: drug.PrepnSpec,
                    BatchNo: drug.ProduceBatchNo,
                    Code: code.Code,
                    Level1Code: code.Level1Code ?? string.Empty,
                    Level2Code: code.Level2Code ?? string.Empty,
                    Level3Code: code.Level3Code ?? string.Empty,
                    Level4Code: code.Level4Code ?? string.Empty,
                    Level5Code: code.Level5Code ?? string.Empty,
                    State: string.IsNullOrWhiteSpace(code.Level1Code) ? TraceEntryState.Warning : TraceEntryState.Success)))
                .ToList();

            await RunOnUiAsync(() =>
            {
                _allSubCodeRows = rows;
                SubcodeTotal = rows.Count;
                SubcodePage = 1;
                ApplySubCodePage();
                SubcodeStatus = $"子码查询完成：单据 {detail.BillCode}，共 {rows.Count} 条";
            });
        }
        catch (Exception ex)
        {
            await RunOnUiAsync(() =>
            {
                _allSubCodeRows.Clear();
                SubCodeRows.Clear();
                SubcodeTotal = 0;
                SubcodePage = 1;
                SubcodeStatus = $"查询异常：{ex.Message}";
                _toast.Error("子码查询", ex.Message);
            });
        }
        finally
        {
            await RunOnUiAsync(() => IsSubcodeBusy = false);
        }
    }

    private async Task<(string ToRefUserId, string FromRefUserId)?> ResolveBillRouteFromListUpoutAsync(
        MsfxApiOptions options,
        string billCode)
    {
        var windows = new[] { 7, 30, 90, 180, 365 };
        var end = DateTime.Today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        const int pageSize = 100;
        const int maxPages = 30;

        foreach (var days in windows)
        {
            var begin = DateTime.Today.AddDays(-(days - 1)).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            for (var page = 1; page <= maxPages; page++)
            {
                using var cts = CreateMsfxTimeout(options.TimeoutSeconds);
                var list = await _msfxApi.GetYljgListUpoutAsync(
                    options,
                    new MsfxListUpoutRequest(
                        RefEntId: options.RefEntId,
                        BeginDate: begin,
                        EndDate: end,
                        Page: page,
                        PageSize: pageSize),
                    cts.Token).ConfigureAwait(false);

                if (!list.Call.Ok)
                {
                    throw new InvalidOperationException($"上游出库单查询失败：{BuildApiErrorMessage(list.Call)}");
                }

                var found = list.Items.FirstOrDefault(x => string.Equals(x.BillCode, billCode, StringComparison.OrdinalIgnoreCase));
                if (found is not null)
                {
                    var toRef = NormalizeInput(found.ToRefUserId) ?? options.RefEntId;
                    var fromRef = NormalizeInput(found.FromRefUserId);
                    return fromRef is null ? null : (toRef, fromRef);
                }

                if (list.Items.Count < pageSize)
                {
                    break;
                }
            }
        }

        return null;
    }

    [RelayCommand]
    private async Task PrevSubcodePageAsync()
    {
        if (!HasSubcodePrevPage || IsSubcodeBusy)
        {
            return;
        }

        SubcodePage -= 1;
        await Task.CompletedTask;
    }

    [RelayCommand]
    private async Task NextSubcodePageAsync()
    {
        if (!HasSubcodeNextPage || IsSubcodeBusy)
        {
            return;
        }

        SubcodePage += 1;
        await Task.CompletedTask;
    }

    [RelayCommand]
    private void ClearSubcodeQuery()
    {
        SubcodeBillCode = string.Empty;
        _allSubCodeRows.Clear();
        SubCodeRows.Clear();
        SubcodeTotal = 0;
        SubcodePage = 1;
        SubcodeStatus = "请输入单据编码后查询子码";
    }

    [RelayCommand]
    private void ResetUpoutFilters()
    {
        var defaults = RollingDateRangeController.Normalize(
            RollingDateRangeController.DefaultFromDate,
            RollingDateRangeController.DefaultToDate);
        UpoutFromDate = defaults.From;
        UpoutToDate = defaults.To;
        UpoutBillCodeKeyword = string.Empty;
        UpoutDrugKeyword = string.Empty;
        UpoutFromEntKeyword = string.Empty;
        UpoutPage = 1;
        UpoutPageSize = "20";
        UpoutStatus = "筛选条件已重置";
    }

    private bool CanRunAutoOnce()
        => !IsAutoBusy;

    private bool CanRefreshAutoBoard()
        => !IsAutoBoardBusy && !IsAutoBusy;

    [RelayCommand(CanExecute = nameof(CanRunAutoOnce))]
    private async Task RunAutoOnceAsync()
    {
        await RunAutoOnceInternalAsync(showProgressPanel: true).ConfigureAwait(false);
    }

    private async Task RunAutoOnceInternalAsync(bool showProgressPanel)
    {
        ShowAutoProgressPanel = showProgressPanel;
        await RunLocalReloadAsync(
            setBusy: v => IsAutoBusy = v,
            action: RunAutoOnceCoreAsync);
    }

    private async Task RunAutoOnceCoreAsync(CancellationToken ct)
    {
        long batchId = 0;
        var batchFinalized = false;
        string? batchErrMsg = null;
        var succeedCount = 0;
        var failCount = 0;
        var window = default(MsfxPullWindow);
        var swTotal = Stopwatch.StartNew();
        long listApiMs = 0;
        long detailApiMs = 0;
        long ingestMs = 0;
        long mapMs = 0;
        long taskBuildMs = 0;
        try
        {
            var options = BuildMsfxOptions();
            SetAutoProgress(2, "准备巡检");
            window = await _syncService.LoadPullWindowAsync("listupout", ct).ConfigureAwait(false);
            AddAutoLog("任务", $"开始执行自动化拉取（{window.BeginAt:yyyy-MM-dd HH:mm:ss} ~ {window.EndAt:yyyy-MM-dd HH:mm:ss}）", TraceEntryState.Info);
            LogInfo("msfx.auto.run.start", "MSFX auto run started", new { window.BeginAt, window.EndAt });
            SetAutoProgress(5, $"拉取窗口 {window.BeginAt:MM-dd HH:mm} ~ {window.EndAt:MM-dd HH:mm}");

            var batch = await _syncService.StartMsfxPullBatchAsync("listupout", window.BeginAt, window.EndAt, ct)
                .ConfigureAwait(false);
            batchId = batch.BatchId;
            AddAutoLog("批次", $"拉取批次已创建：#{batchId}", TraceEntryState.Success);
            LogInfo("msfx.auto.batch.created", "MSFX pull batch created", new
            {
                batchId,
                sourceApi = "listupout",
                window.BeginAt,
                window.EndAt
            });
            SetAutoProgress(8, $"批次 #{batchId} 已创建");
            await RefreshAutoPullPanelCoreAsync(ct).ConfigureAwait(false);

            var begin = window.BeginAt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            var end = window.EndAt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            var page = 1;
            const int pageSize = 50;
            var totalApiRows = 0;
            var totalInboundRows = 0;
            var totalBills = 0;
            var detailSubCodes = 0;
            var retryQueuedCount = 0;
            var retrySucceededCount = 0;
            var retryFailedCount = 0;
            var watchQueuedCount = 0;
            var watchResolvedCount = 0;
            var watchDeferredCount = 0;
            var processedBillCodes = new HashSet<string>(StringComparer.Ordinal);
            var totalPagesEstimate = 1;
            var processedBills = 0;
            var expectedBills = 1;

            async Task<bool> TryIngestBillDetailAsync(
                string billCode,
                string? fromRefUserId,
                string? fromEntName,
                string? toRefUserId,
                string? billType,
                string? billTime,
                string? billUploadTime,
                string rawJson,
                bool fromRetry,
                bool fromWatch,
                string? watchStatus)
            {
                var normalizedToRef = string.IsNullOrWhiteSpace(toRefUserId) ? options.RefEntId : toRefUserId;
                var normalizedFromRef = NormalizeInput(fromRefUserId);
                async Task<bool> QueueRetryAndLogAsync(string err)
                {
                    if (fromWatch)
                    {
                        await _syncService.RescheduleBillWatchAsync(
                            sourceApi: "listupout",
                            billCode: billCode,
                            lastSeenStatus: watchStatus,
                            lastError: err,
                            ct: ct).ConfigureAwait(false);
                        watchDeferredCount++;
                        AddAutoLog("待确认补偿", $"单据 {billCode} 暂未就绪，已延后重查：{err}", TraceEntryState.Warning);
                        return false;
                    }

                    await _syncService.ScheduleBillRetryAsync(
                        sourceApi: "listupout",
                        billCode: billCode,
                        fromRefUserId: normalizedFromRef,
                        toRefUserId: normalizedToRef,
                        lastError: err,
                        ct: ct).ConfigureAwait(false);
                    retryQueuedCount++;
                    if (fromRetry)
                    {
                        retryFailedCount++;
                        AddAutoLog("重试", $"单据 {billCode} 仍失败：{err}", TraceEntryState.Warning);
                    }
                    else
                    {
                        AddAutoLog("子码解析", $"单据 {billCode} 失败，已入重试队列：{err}", TraceEntryState.Warning);
                    }

                    return false;
                }

                var billId = await _syncService.SaveInboundBillAsync(
                    batchId: batchId,
                    billCode: billCode,
                    billType: billType ?? string.Empty,
                    billTime: billTime ?? string.Empty,
                    billUploadTime: billUploadTime ?? string.Empty,
                    fromRefUserId: fromRefUserId ?? string.Empty,
                    fromEntName: fromEntName ?? string.Empty,
                    toRefUserId: normalizedToRef ?? string.Empty,
                    toUserId: string.Empty,
                    toUserName: string.Empty,
                    status: "2",
                    rawJson: rawJson,
                    ct: ct).ConfigureAwait(false);

                MsfxListUpoutDetailResult detail;
                try
                {
                    using var detailCts = CreateMsfxTimeout(options.TimeoutSeconds);
                    using var detailLinkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, detailCts.Token);
                    var swDetail = Stopwatch.StartNew();
                    detail = await _msfxApi.GetYljgListUpoutDetailAsync(options, new MsfxListUpoutDetailRequest(
                        RefEntId: options.RefEntId,
                        BillCode: billCode,
                        ToRefUserId: normalizedToRef,
                        FromRefUserId: normalizedFromRef), detailLinkedCts.Token).ConfigureAwait(false);
                    detailApiMs += swDetail.ElapsedMilliseconds;
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    return await QueueRetryAndLogAsync(ex.Message).ConfigureAwait(false);
                }

                if (!detail.Call.Ok)
                {
                    return await QueueRetryAndLogAsync(BuildApiErrorMessage(detail.Call)).ConfigureAwait(false);
                }

                var swIngest = Stopwatch.StartNew();
                var ingest = await _syncService.IngestMsfxBillDetailAsync(
                    billId,
                    billCode,
                    detail.DrugItems
                        .Select(d => (
                            d.PhysicName,
                            d.PackageSpec,
                            d.PrepnSpec,
                            d.ProduceBatchNo,
                            (IReadOnlyList<(string Code, string CodeLevel, string? Level1Code, string? Level2Code, string? Level3Code, string? Level4Code, string? Level5Code)>)d.TraceCodes
                                .Select(c => (c.Code, c.CodeLevel, c.Level1Code, c.Level2Code, c.Level3Code, c.Level4Code, c.Level5Code))
                                .ToList()))
                        .ToList(),
                    ct).ConfigureAwait(false);
                ingestMs += swIngest.ElapsedMilliseconds;

                succeedCount++;
                detailSubCodes += ingest.InsertedCodes;
                if (fromRetry)
                {
                    retrySucceededCount++;
                    await _syncService.MarkBillRetrySucceededAsync("listupout", billCode, ct).ConfigureAwait(false);
                    AddAutoLog("重试", $"单据 {billCode} 重试成功：药品 {ingest.InsertedItems}，码 {ingest.InsertedCodes}，新增 staging {ingest.InsertedStaging}", TraceEntryState.Success);
                }
                else if (fromWatch)
                {
                    watchResolvedCount++;
                    AddAutoLog("待确认补偿", $"单据 {billCode} 已转入库：药品 {ingest.InsertedItems}，码 {ingest.InsertedCodes}，新增 staging {ingest.InsertedStaging}", TraceEntryState.Success);
                }
                else
                {
                    var ingestState = ingest.InsertedStaging > 0 ? TraceEntryState.Success : TraceEntryState.Warning;
                    AddAutoLog("落库", $"单据 {billCode}：药品 {ingest.InsertedItems}，码 {ingest.InsertedCodes}，新增 staging {ingest.InsertedStaging}", ingestState);
                }

                await _syncService.MarkBillWatchResolvedAsync("listupout", billCode, ct).ConfigureAwait(false);

                return true;
            }

            var dueRetries = await _syncService.LoadDueBillRetriesAsync("listupout", 200, ct).ConfigureAwait(false);
            if (dueRetries.Count > 0)
            {
                AddAutoLog("重试", $"发现待重试单据 {dueRetries.Count} 条，优先处理", TraceEntryState.Info);
                foreach (var retry in dueRetries)
                {
                    ct.ThrowIfCancellationRequested();
                    if (!processedBillCodes.Add(retry.BillCode))
                    {
                        continue;
                    }

                    processedBills++;
                    expectedBills = Math.Max(expectedBills, processedBills + 1);
                    SetAutoProgress(
                        12 + Math.Min(20, processedBills * (20d / Math.Max(1, expectedBills))),
                        $"重试单据 {processedBills}/{Math.Max(1, expectedBills)}：{retry.BillCode}");

                    _ = await TryIngestBillDetailAsync(
                        billCode: retry.BillCode,
                        fromRefUserId: retry.FromRefUserId,
                        fromEntName: string.Empty,
                        toRefUserId: retry.ToRefUserId,
                        billType: string.Empty,
                        billTime: string.Empty,
                        billUploadTime: string.Empty,
                        rawJson: "{}",
                        fromRetry: true,
                        fromWatch: false,
                        watchStatus: null).ConfigureAwait(false);
                }
                await RefreshAutoPullPanelCoreAsync(ct).ConfigureAwait(false);
            }

            while (true)
            {
                using var cts = CreateMsfxTimeout(options.TimeoutSeconds);
                using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, cts.Token);
                var swList = Stopwatch.StartNew();
                var list = await _msfxApi.GetYljgListUpoutAsync(options, new MsfxListUpoutRequest(
                    RefEntId: options.RefEntId,
                    BeginDate: begin,
                    EndDate: end,
                    Page: page,
                    PageSize: pageSize), linkedCts.Token).ConfigureAwait(false);
                listApiMs += swList.ElapsedMilliseconds;

                if (!list.Call.Ok)
                {
                    failCount++;
                    throw new InvalidOperationException($"上游出库单拉取失败：{BuildApiErrorMessage(list.Call)}");
                }

                if (batchId > 0 && !string.IsNullOrWhiteSpace(list.Call.RequestId))
                {
                    await _syncService.UpdateMsfxPullBatchRequestIdAsync(batchId, list.Call.RequestId, ct).ConfigureAwait(false);
                }

                totalApiRows += list.Items.Count;
                totalPagesEstimate = Math.Max(1, (int)Math.Ceiling(list.Total / (double)pageSize));
                var inboundRows = list.Items.Where(x => string.Equals(x.Status, "2", StringComparison.Ordinal)).ToList();
                var watchRows = list.Items
                    .Where(x => !string.IsNullOrWhiteSpace(x.BillCode) && !string.Equals(x.Status, "2", StringComparison.Ordinal))
                    .GroupBy(x => x.BillCode)
                    .Select(g => g.First())
                    .ToList();
                totalInboundRows += inboundRows.Count;
                SetAutoProgress(
                    10 + Math.Min(30, page * (30d / totalPagesEstimate)),
                    $"上游单据第 {page}/{totalPagesEstimate} 页，API {list.Items.Count} 条，已入库 {inboundRows.Count} 条");

                AddAutoLog("上游出库单", $"第 {page} 页：API {list.Items.Count} 条，已入库(status=2) {inboundRows.Count} 条", TraceEntryState.Info);

                foreach (var watch in watchRows)
                {
                    ct.ThrowIfCancellationRequested();
                    await _syncService.WatchMsfxBillAsync(
                        sourceApi: "listupout",
                        billCode: watch.BillCode,
                        fromRefUserId: watch.FromRefUserId,
                        toRefUserId: watch.ToRefUserId,
                        fromEntName: watch.FromEntName,
                        billType: watch.BillType,
                        billTime: watch.BillTime,
                        billUploadTime: watch.BillUploadTime,
                        lastSeenStatus: watch.Status,
                        rawJson: System.Text.Json.JsonSerializer.Serialize(watch),
                        ct: ct).ConfigureAwait(false);
                    watchQueuedCount++;
                }

                var bills = inboundRows
                    .Where(x => !string.IsNullOrWhiteSpace(x.BillCode))
                    .GroupBy(x => x.BillCode)
                    .Select(g => g.First())
                    .ToList();

                totalBills += bills.Count;
                expectedBills = Math.Max(expectedBills, totalBills);
                foreach (var bill in bills)
                {
                    ct.ThrowIfCancellationRequested();
                    if (!processedBillCodes.Add(bill.BillCode))
                    {
                        continue;
                    }

                    processedBills++;
                    SetAutoProgress(
                        40 + Math.Min(45, processedBills * (45d / Math.Max(1, expectedBills))),
                        $"解析单据 {processedBills}/{Math.Max(1, expectedBills)}：{bill.BillCode}");

                    _ = await TryIngestBillDetailAsync(
                        billCode: bill.BillCode,
                        fromRefUserId: bill.FromRefUserId,
                        fromEntName: bill.FromEntName,
                        toRefUserId: bill.ToRefUserId,
                        billType: bill.BillType,
                        billTime: bill.BillTime,
                        billUploadTime: bill.BillUploadTime,
                        rawJson: System.Text.Json.JsonSerializer.Serialize(bill),
                        fromRetry: false,
                        fromWatch: false,
                        watchStatus: null).ConfigureAwait(false);
                }

                await RefreshAutoPullPanelCoreAsync(ct).ConfigureAwait(false);

                var loaded = page * pageSize;
                if (list.Items.Count == 0 || loaded >= list.Total)
                {
                    break;
                }

                page++;
            }

            var dueWatches = await _syncService.LoadDueBillWatchesAsync("listupout", 200, ct).ConfigureAwait(false);
            if (dueWatches.Count > 0)
            {
                AddAutoLog("待确认补偿", $"发现待确认单据 {dueWatches.Count} 条，开始补偿重查", TraceEntryState.Info);
                foreach (var watch in dueWatches)
                {
                    ct.ThrowIfCancellationRequested();
                    if (!processedBillCodes.Add(watch.BillCode))
                    {
                        continue;
                    }

                    processedBills++;
                    expectedBills = Math.Max(expectedBills, processedBills + 1);
                    SetAutoProgress(
                        60 + Math.Min(20, processedBills * (20d / Math.Max(1, expectedBills))),
                        $"补偿重查 {processedBills}/{Math.Max(1, expectedBills)}：{watch.BillCode}");

                    _ = await TryIngestBillDetailAsync(
                        billCode: watch.BillCode,
                        fromRefUserId: watch.FromRefUserId,
                        fromEntName: watch.FromEntName,
                        toRefUserId: watch.ToRefUserId,
                        billType: watch.BillType,
                        billTime: watch.BillTime,
                        billUploadTime: watch.BillUploadTime,
                        rawJson: watch.RawJson ?? "{}",
                        fromRetry: false,
                        fromWatch: true,
                        watchStatus: watch.LastSeenStatus).ConfigureAwait(false);
                }

                await RefreshAutoPullPanelCoreAsync(ct).ConfigureAwait(false);
            }

            var swMap = Stopwatch.StartNew();
            var mapBefore = await _syncService.LoadMappingStatusSnapshotAsync(ct).ConfigureAwait(false);
            AddAutoLog(
                "映射自检",
                $"执行前 PENDING {mapBefore.PendingCount}，MAPPED {mapBefore.MappedCount}，NEED_REVIEW {mapBefore.NeedReviewCount}，FAILED {mapBefore.FailedCount}，TOTAL {mapBefore.TotalCount}",
                TraceEntryState.Info);
            if (!await EnsureMsfxSensitiveOperationUnlockedAsync(
                    SensitiveOperationKind.MsfxMappingApply,
                    "自动映射",
                    batchId > 0 ? $"batch:{batchId}" : "auto-run",
                    "auto mapping apply before task build",
                    ct).ConfigureAwait(false))
            {
                throw new InvalidOperationException("MSFX 自动映射未解锁，已停止在映射写入前");
            }

            var map = await _syncService.ApplyMsfxMappingAsync(50000, ct).ConfigureAwait(false);
            var mapAfter = await _syncService.LoadMappingStatusSnapshotAsync(ct).ConfigureAwait(false);
            mapMs += swMap.ElapsedMilliseconds;
            SetAutoProgress(90, "执行自动映射");
            var mapState = map.ProcessedCount == 0
                ? TraceEntryState.Warning
                : map.ReviewCount > 0 ? TraceEntryState.Warning : TraceEntryState.Success;
            AddAutoLog("映射", $"处理 {map.ProcessedCount}，命中 {map.MappedCount}，待人工 {map.ReviewCount}", mapState);
            LogInfo("msfx.auto.map.summary", "MSFX auto mapping finished", new
            {
                map.ProcessedCount,
                map.MappedCount,
                map.ReviewCount
            });
            AddAutoLog(
                "映射自检",
                $"执行后 PENDING {mapAfter.PendingCount}，MAPPED {mapAfter.MappedCount}，NEED_REVIEW {mapAfter.NeedReviewCount}，FAILED {mapAfter.FailedCount}，TOTAL {mapAfter.TotalCount}",
                map.ProcessedCount == 0 ? TraceEntryState.Warning : TraceEntryState.Success);
            await RefreshAutoMapPanelCoreAsync(ct).ConfigureAwait(false);

            var swTask = Stopwatch.StartNew();
            var taskResult = await _syncService.BuildMsfxInjectTasksAsync(500, ct).ConfigureAwait(false);
            taskBuildMs += swTask.ElapsedMilliseconds;
            SetAutoProgress(96, "构建注入任务");
            var taskState = taskResult.CreatedTasks > 0 ? TraceEntryState.Success : TraceEntryState.Warning;
            AddAutoLog("建任务", $"创建任务 {taskResult.CreatedTasks}，下发码 {taskResult.TaskedCodes}", taskState);
            LogInfo("msfx.auto.task_build.summary", "MSFX inject tasks built", new
            {
                taskResult.CreatedTasks,
                taskResult.TaskedCodes
            });
            await RefreshAutoTaskPanelCoreAsync(ct).ConfigureAwait(false);

            var batchStatus = failCount > 0 ? "FAILED" : "SUCCESS";
            await _syncService.CompleteMsfxPullBatchAsync(batchId, batchStatus, succeedCount, failCount, null, CancellationToken.None)
                .ConfigureAwait(false);
            await _syncService.AdvanceMsfxPullCursorAsync("listupout", window.BeginAt, window.EndAt, batchId, batchStatus, CancellationToken.None)
                .ConfigureAwait(false);
            batchFinalized = true;
            await RefreshAutoPullPanelCoreAsync(ct).ConfigureAwait(false);

            SetAutoProgress(100, "巡检完成");
            AutoStatus = $"自动化拉取完成：API {totalApiRows}，已入库 {totalInboundRows}，单据 {totalBills}，码 {detailSubCodes}，重试成功 {retrySucceededCount}，重试失败 {retryFailedCount}，重试入队 {retryQueuedCount}，待确认入池 {watchQueuedCount}，补偿成功 {watchResolvedCount}，补偿延后 {watchDeferredCount}，新增任务 {taskResult.CreatedTasks}";
            AutoLastRunAtText = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            if (IsAutoEnabled)
            {
                AutoNextRunAtText = DateTime.Now.AddMinutes(Math.Max(1, AutoIntervalMinutes)).ToString("yyyy-MM-dd HH:mm:ss");
            }

            AddAutoLog(
                "性能",
                $"总耗时 {FormatElapsed(swTotal.Elapsed)}，列表API {FormatElapsed(listApiMs)}，详情API {FormatElapsed(detailApiMs)}，入库 {FormatElapsed(ingestMs)}，映射 {FormatElapsed(mapMs)}，建任务 {FormatElapsed(taskBuildMs)}",
                TraceEntryState.Info);
            LogInfo("msfx.auto.run.finish", "MSFX auto run finished", new
            {
                batchId,
                totalApiRows,
                totalInboundRows,
                totalBills,
                detailSubCodes,
                retryQueuedCount,
                retrySucceededCount,
                retryFailedCount,
                watchQueuedCount,
                watchResolvedCount,
                watchDeferredCount,
                createdTasks = taskResult.CreatedTasks,
                taskedCodes = taskResult.TaskedCodes,
                elapsedMs = swTotal.ElapsedMilliseconds
            });
            _toast.Success("码上放心自动化", $"完成：下发任务 {taskResult.CreatedTasks}，下发码 {taskResult.TaskedCodes}");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            batchErrMsg = ex.Message;
            SetAutoProgress(100, $"巡检失败：{ex.Message}");

            AddAutoLog("异常", ex.Message, TraceEntryState.Failed);
            LogError("msfx.auto.run.fail", "MSFX auto run failed", ex, new
            {
                batchId,
                windowBeginAt = window?.BeginAt,
                windowEndAt = window?.EndAt,
                succeedCount,
                failCount
            });
            AutoStatus = $"自动化拉取异常：{ex.Message}";
            if (ShouldShowOperationErrorToast(ex))
            {
                _toast.Error("自动化监控", ex.Message);
            }
        }
        finally
        {
            if (batchId > 0 && !batchFinalized)
            {
                try
                {
                    await _syncService.CompleteMsfxPullBatchAsync(
                        batchId,
                        "FAILED",
                        succeedCount,
                        Math.Max(failCount, 1),
                        string.IsNullOrWhiteSpace(batchErrMsg) ? "执行失败，详见运行日志" : batchErrMsg,
                        CancellationToken.None).ConfigureAwait(false);
                }
                catch (Exception finalizeEx)
                {
                    AddAutoLog("批次结算", $"批次#{batchId} 状态回写失败：{finalizeEx.Message}", TraceEntryState.Failed);
                    LogError("msfx.auto.batch_finalize.fail", "MSFX batch finalize failed", finalizeEx, new
                    {
                        batchId,
                        succeedCount,
                        failCount,
                        batchErrMsg
                    });
                }
            }

            await RefreshAutoBoardAsync().ConfigureAwait(false);
        }
    }

    [RelayCommand]
    private void ClearAutoLogs()
    {
        AutoLogs.Clear();
        _lastAutoLogSignature = null;
        AddAutoLog("日志", "日志已清空", TraceEntryState.Info);
    }

    [RelayCommand]
    private void ToggleAutoMonitor()
        => IsAutoEnabled = !IsAutoEnabled;

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
    private async Task PrevMapQueuePageAsync()
    {
        if (!HasMapQueuePrevPage || IsMapPanelBusy)
        {
            return;
        }

        await RunMapPanelQueryAsync(ct => RefreshMapQueueAsync(ct, olderPage: false)).ConfigureAwait(false);
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
    private async Task NextMapQueuePageAsync()
    {
        if (!HasMapQueueNextPage || IsMapPanelBusy)
        {
            return;
        }

        await RunMapPanelQueryAsync(ct => RefreshMapQueueAsync(ct, olderPage: true)).ConfigureAwait(false);
    }

    [RelayCommand]
    private async Task SearchMapQueueAsync()
    {
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
        OnPropertyChanged(nameof(MapQueuePageText));
        await RefreshMapQueueLatestAsync().ConfigureAwait(false);
    }

    private async Task<bool> EnsureMsfxSensitiveOperationUnlockedAsync(
        SensitiveOperationKind kind,
        string scene,
        string targetId,
        string reason,
        CancellationToken ct = default)
    {
        var operatorName = Environment.UserName;
        var ok = await _unlockService.RequestUnlockAsync(new SensitiveOperationRequest(
            Kind: kind,
            ScopeKey: UnlockScopes.SharedSensitiveOps,
            Scene: scene,
            PromptTitle: scene,
            PromptHint: $"{scene} 属于高风险 MSFX 操作。\n目标：{targetId}\n原因：{reason}\n请输入当前数据库密码以解锁。",
            OperatorName: operatorName,
            TargetId: targetId,
            Reason: reason), ct).ConfigureAwait(false);

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

    [RelayCommand]
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
            $"将重开选中的 {selectedRows.Count} 条 SUCCESS / DISCARDED 任务，并重置为可执行队列。确认继续？").ConfigureAwait(false);
        if (!ok)
        {
            return;
        }

        if (!await EnsureMsfxSensitiveOperationUnlockedAsync(
                SensitiveOperationKind.MsfxReopen,
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
                    var result = await _syncService.ReopenMsfxTaskAsync(
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

            await RefreshAutoTaskPanelCoreAsync(CancellationToken.None).ConfigureAwait(false);
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

        var ok = await _dialog.Confirm(
            "弃用注入任务",
            $"将弃用选中的 {selectedRows.Count} 条任务。弃用后 Agent 将不再执行这些任务。确认继续？").ConfigureAwait(false);
        if (!ok)
        {
            return;
        }

        if (!await EnsureMsfxSensitiveOperationUnlockedAsync(
                SensitiveOperationKind.MsfxDiscard,
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
                    var result = await _syncService.DiscardMsfxTaskAsync(
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

            await RefreshAutoTaskPanelCoreAsync(CancellationToken.None).ConfigureAwait(false);
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
            $"将把选中的 {selectedRows.Count} 条任务回退到映射结果队列，并等待重新映射。原任务会停止执行并保留审计记录。确认继续？").ConfigureAwait(false);
        if (!ok)
        {
            return;
        }

        if (!await EnsureMsfxSensitiveOperationUnlockedAsync(
                SensitiveOperationKind.MsfxRemap,
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
                    var result = await _syncService.RemapMsfxTaskAsync(
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
            await RefreshAutoTaskPanelCoreAsync(CancellationToken.None).ConfigureAwait(false);
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
            $"将把选中的 {selectedRows.Count} 条任务合并为 1 条执行任务，总码数约 {totalCodes} 条。允许跨 bill.code 合并，确认继续？").ConfigureAwait(false);
        if (!ok)
        {
            return;
        }

        if (!await EnsureMsfxSensitiveOperationUnlockedAsync(
                SensitiveOperationKind.MsfxMerge,
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
            var result = await _syncService.MergeMsfxTasksAsync(
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
            await RefreshAutoTaskPanelCoreAsync(CancellationToken.None).ConfigureAwait(false);
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

        var splitUnits = await _syncService.LoadMsfxTaskSplitUnitsAsync(taskRow.TaskId, CancellationToken.None).ConfigureAwait(false);
        var splitCodeRows = await _syncService.LoadMsfxTaskSplitCodeRowsAsync(taskRow.TaskId, CancellationToken.None).ConfigureAwait(false);
        var choice = await _dialog.ShowMsfxTaskSplitDialog(new MsfxTaskSplitDialogModel(
            TaskId: taskRow.TaskId,
            SourceBillCode: taskRow.SourceBillCode,
            Target: taskRow.Target,
            TotalCodes: taskRow.TotalCodes,
            SplitCodeRows: splitCodeRows)).ConfigureAwait(false);
        if (choice.Action == MsfxTaskSplitDialogAction.Cancel)
        {
            return;
        }

        var splitReason = choice.Action == MsfxTaskSplitDialogAction.CustomQuantity
            ? $"manual custom split from task queue: {choice.CustomQuantities}"
            : "manual split from task queue";
        if (!await EnsureMsfxSensitiveOperationUnlockedAsync(
                SensitiveOperationKind.MsfxSplit,
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
            if (choice.Action == MsfxTaskSplitDialogAction.CustomQuantity)
            {
                var customPlan = TryBuildCustomSplitPlan(splitUnits, choice.CustomQuantities, out var customError);
                if (customPlan is null)
                {
                    _toast.Warn("自定义拆分", customError ?? "拆分计划无效");
                    return;
                }

                var customResult = await _syncService.SplitMsfxTaskCustomAsync(
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
                var splitMode = choice.Action == MsfxTaskSplitDialogAction.ParentCluster ? "PARENT_CLUSTER" : "BATCH";
                var result = await _syncService.SplitMsfxTaskAsync(
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

            await RefreshAutoTaskPanelCoreAsync(CancellationToken.None).ConfigureAwait(false);
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

        try
        {
            var groups = await _syncService.LoadMappingBatchGroupsAsync(
                mapStatus: null,
                codeStatus: null,
                searchScope: "ALL",
                keyword: null,
                limit: 500,
                ct: CancellationToken.None).ConfigureAwait(false);

            if (groups.Count == 0)
            {
                _toast.Info("批量映射", "当前没有可处理分组");
                return;
            }

            var res = await _dialog.ShowMsfxMappingBatchDialog(new MsfxMappingBatchDialogModel(
                Groups: groups,
                MapStatusFilter: "ALL",
                CodeStatusFilter: "ALL",
                SearchScope: "全部字段",
                Keyword: string.Empty)).ConfigureAwait(false);

            if (res.Action == MsfxMappingBatchDialogAction.Cancel)
            {
                return;
            }

            if (res.Group is null)
            {
                _toast.Warn("批量映射", "请先在分组表中选择一条记录");
                return;
            }

            var group = res.Group;
            var action = res.Action switch
            {
                MsfxMappingBatchDialogAction.ApplyMap => "APPLY_MAP",
                MsfxMappingBatchDialogAction.DiscardTask => "APPLY_DISCARD",
                _ => "APPLY_MAP"
            };

            if ((res.Action == MsfxMappingBatchDialogAction.ApplyMap || res.Action == MsfxMappingBatchDialogAction.DiscardTask) &&
                (string.IsNullOrWhiteSpace(res.DrugId) || string.IsNullOrWhiteSpace(res.Spec)))
            {
                _toast.Warn("批量映射", "需要填写需映射的药品信息和规格信息");
                return;
            }

            var preview = await _syncService.PreviewMsfxMappingBatchAsync(
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
                _toast.Warn("批量映射", $"无可执行记录，将影响 {preview.CandidateCount} 条，阻塞 {preview.BlockedCount} 条");
                return;
            }

            var confirmMsg = res.Action switch
            {
                MsfxMappingBatchDialogAction.ApplyMap => $"分组“{group.SourceDrugNameRaw} / {group.SourceSpecRaw}”将影响 {preview.CandidateCount} 条，可执行 {preview.EligibleCount} 条，确认批量映射？",
                MsfxMappingBatchDialogAction.DiscardTask => $"分组“{group.SourceDrugNameRaw} / {group.SourceSpecRaw}”将影响 {preview.CandidateCount} 条，可执行 {preview.EligibleCount} 条，确认弃用任务？",
                _ => $"分组“{group.SourceDrugNameRaw} / {group.SourceSpecRaw}”将影响 {preview.CandidateCount} 条，可执行 {preview.EligibleCount} 条，确认处理？"
            };
            var ok = await _dialog.Confirm("批量映射", confirmMsg).ConfigureAwait(false);
            if (!ok)
            {
                return;
            }

            var mappingKind = res.Action == MsfxMappingBatchDialogAction.DiscardTask
                ? SensitiveOperationKind.MsfxDiscard
                : SensitiveOperationKind.MsfxMappingApply;
            var mappingReason = res.Action == MsfxMappingBatchDialogAction.DiscardTask
                ? "manual batch discard from mapping dialog"
                : "manual batch mapping apply from mapping dialog";
            if (!await EnsureMsfxSensitiveOperationUnlockedAsync(
                    mappingKind,
                    "批量映射",
                    $"{group.SourceDrugNameRaw}/{group.SourceSpecRaw}",
                    mappingReason).ConfigureAwait(false))
            {
                return;
            }

            var apply = await _syncService.ApplyMsfxMappingBatchAsync(
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
                _toast.Warn("批量映射", "本次未更新任何记录，请检查筛选条件或映射目标");
                return;
            }

            if (res.Action == MsfxMappingBatchDialogAction.ApplyMap && apply.AffectedCount > 0)
            {
                var built = await _syncService.BuildMsfxInjectTasksAsync(500, CancellationToken.None).ConfigureAwait(false);
                AddAutoLog("批量映射", $"分组处理 {apply.AffectedCount} 条，新增任务 {built.CreatedTasks}", TraceEntryState.Success);
                LogWarn("msfx.map.batch.apply", "MSFX batch mapping applied", null, new
                {
                    apply.AffectedCount,
                    built.CreatedTasks,
                    group.SourceDrugNameRaw,
                    group.SourceSpecRaw,
                    DrugId = res.DrugId,
                    Spec = res.Spec
                });
                _toast.Success("批量映射", $"已处理 {apply.AffectedCount} 条，新增任务 {built.CreatedTasks}");
            }
            else if (res.Action == MsfxMappingBatchDialogAction.DiscardTask)
            {
                AddAutoLog("批量映射", $"分组弃用 {apply.AffectedCount} 条，已进入弃用任务队列", apply.AffectedCount > 0 ? TraceEntryState.Discarded : TraceEntryState.Info);
                LogWarn("msfx.map.batch.discard", "MSFX batch mapping discarded into task queue", null, new
                {
                    apply.AffectedCount,
                    group.SourceDrugNameRaw,
                    group.SourceSpecRaw,
                    DrugId = res.DrugId,
                    Spec = res.Spec
                });
                _toast.Info("批量映射", $"已处理 {apply.AffectedCount} 条，并直接进入弃用任务队列");
            }
            else
            {
                _toast.Info("批量映射", $"已处理 {apply.AffectedCount} 条");
            }

            await RefreshAutoBoardAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _toast.Error("批量映射", ex.Message);
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
        return _dialog.ShowMsfxStateDetailDialog(new MsfxStateDetailDialogModel(
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
            new("目标药品/规格", row.Target),
            new("状态", row.Status),
            new("进度", row.Progress),
            new("重试次数", row.RetryCount.ToString(CultureInfo.InvariantCulture)),
            new("创建时间", row.CreatedAt),
            new("抢占时间", row.PickedAt),
            new("完成时间", row.FinishedAt),
            new("错误信息", string.IsNullOrWhiteSpace(row.ErrMsg) ? "--" : row.ErrMsg)
        };
        return _dialog.ShowMsfxStateDetailDialog(new MsfxStateDetailDialogModel(
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

        return _dialog.ShowMsfxStateDetailDialog(new MsfxStateDetailDialogModel(
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
        => RunLocalReloadAsync(_ => { }, RefreshAutoBoardCoreAsync);

    private async Task RefreshAutoBoardCoreAsync(CancellationToken ct)
    {
        try
        {
            MsfxAutoBoardSnapshot snap = default!;
            await RunLocalBusyAsync(
                ct,
                v => IsPullPanelBusy = v,
                async () => snap = await RefreshAutoPullPanelCoreAsync(ct).ConfigureAwait(false),
                ShouldShowPullPanelBusy()).ConfigureAwait(false);
            await RunLocalBusyAsync(
                ct,
                v => IsMapPanelBusy = v,
                () => RefreshAutoMapPanelCoreAsync(ct),
                ShouldShowMapPanelBusy()).ConfigureAwait(false);
            await RunLocalBusyAsync(
                ct,
                v => IsTaskPanelBusy = v,
                () => RefreshAutoTaskPanelCoreAsync(ct),
                ShouldShowTaskPanelBusy()).ConfigureAwait(false);
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
            if (ShouldShowOperationErrorToast(ex))
            {
                _toast.Error("刷新审计", ex.Message);
            }
        }
    }

    private async Task<MsfxAutoBoardSnapshot> RefreshAutoSummaryCoreAsync(CancellationToken ct)
    {
        var snap = await _syncService.LoadMsfxDashboardAsync(ct).ConfigureAwait(false);
        await RunOnUiAsync(() =>
        {
            AutoPullSummary = $"批次#{snap.LastBatchId} {snap.LastBatchStatus} 成功{snap.LastBatchSuccessCount}/失败{snap.LastBatchFailCount}";
            AutoMapSummary = $"待映射{snap.MapPendingCount} 已映射{snap.MapMappedCount} 待人工{snap.MapNeedReviewCount} 失败{snap.MapFailedCount}";
            AutoTaskSummary =
                $"NEW {snap.TaskNewCount} RUNNING {snap.TaskRunningCount} SUCCESS {snap.TaskSuccessCount} FAILED {snap.TaskFailedCount} DISCARDED {snap.TaskDiscardedCount}";
            AutoRiskSummary = $"staging失败{snap.StagingFailedCount} 重复码{snap.StagingDuplicateCount} 任务取消{snap.TaskCancelledCount} 批次失败{snap.LastBatchFailCount}";
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
        });

        return snap;
    }

    private async Task<MsfxAutoBoardSnapshot> RefreshAutoPullPanelCoreAsync(CancellationToken ct)
    {
        var snap = await RefreshAutoSummaryCoreAsync(ct).ConfigureAwait(false);
        var pullRows = await _syncService.LoadRecentPullBatchesAsync(500, ct).ConfigureAwait(false);
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

    private async Task<MsfxAutoBoardSnapshot> RefreshAutoMapPanelCoreAsync(CancellationToken ct)
    {
        var snap = await RefreshAutoSummaryCoreAsync(ct).ConfigureAwait(false);
        ResetMapQueueCursor();
        await RefreshMapQueueAsync(ct, olderPage: null).ConfigureAwait(false);
        return snap;
    }

    private async Task<MsfxAutoBoardSnapshot> RefreshAutoTaskPanelCoreAsync(CancellationToken ct)
    {
        var snap = await RefreshAutoSummaryCoreAsync(ct).ConfigureAwait(false);
        var taskRows = await _syncService.LoadInjectTaskQueueAsync(0, ct).ConfigureAwait(false);
        await RunOnUiAsync(() =>
        {
            var checkedIds = _allTaskQueueRows.Where(x => x.IsChecked).Select(x => x.TaskId).ToHashSet();
            _allTaskQueueRows = taskRows.Select(x => new MsfxAutoTaskQueueGridRow(
                    TaskId: x.TaskId,
                    SourceBillCode: x.SourceBillCode ?? "--",
                    BatchNos: string.IsNullOrWhiteSpace(x.BatchNos) ? "--" : x.BatchNos!,
                    MappedDrugId: x.MappedDrugId,
                    MappedSpec: x.MappedSpec,
                    TotalCodes: x.TotalCodes,
                    CurrentCodeCount: x.CurrentCodeCount,
                    Target: $"{x.MappedDrugId} / {x.MappedSpec}",
                    Status: x.Status,
                    Progress: $"{x.SuccessCodes}/{x.TotalCodes} 成功, 失败{x.FailedCodes}",
                    RetryCount: x.RetryCount,
                    CreatedAt: x.CreatedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss"),
                    PickedAt: x.PickedAt?.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss") ?? "--",
                    FinishedAt: x.FinishedAt?.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss") ?? "--",
                    ErrMsg: x.ErrMsg ?? string.Empty,
                    State: ToTaskState(x.Status)))
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

        AutoTaskQueueRows.Clear();
        foreach (var row in filtered)
        {
            AutoTaskQueueRows.Add(row);
        }

        OnPropertyChanged(nameof(CanEnterTaskMergeMode));
        OnPropertyChanged(nameof(CanEnterTaskRemapMode));
        OnPropertyChanged(nameof(CanEnterTaskDiscardMode));
        OnPropertyChanged(nameof(CanEnterTaskReopenMode));
    }

    private static bool ContainsTaskQueueIgnoreCase(string? text, string keyword)
        => !string.IsNullOrWhiteSpace(text)
           && text.Contains(keyword, StringComparison.OrdinalIgnoreCase);

    private static string NormalizeMergeKeyPart(string? value)
        => value?.Trim() ?? string.Empty;

    private static string BuildMergeKey(MsfxAutoTaskQueueGridRow row)
        => $"{NormalizeMergeKeyPart(row.MappedDrugId)}|{NormalizeMergeKeyPart(row.MappedSpec)}";

    private static (string[] GroupKeys, int[] BucketIndexes, string DisplayText)? TryBuildCustomSplitPlan(
        IReadOnlyList<MsfxInjectTaskSplitUnitRow> units,
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

    private async Task RefreshMapQueueAsync(CancellationToken ct, bool? olderPage)
    {
        var newer = olderPage.HasValue && !olderPage.Value;
        var hasCursor = _mapCursorUpdatedAt.HasValue && _mapCursorId.HasValue;
        DateTimeOffset? cursorAt = null;
        long? cursorId = null;

        if (olderPage == true && AutoMapQueueRows.Count > 0)
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
        }

        var pageSize = fullMapMode ? GetMapQueuePageSize() : CompactMapQueueRows;
        var page = await _syncService.LoadMappingQueuePageAsync(
            pageSize: pageSize,
            mapStatus: NormalizeFilterValue(MapQueueMapStatusFilter),
            codeStatus: NormalizeFilterValue(MapQueueCodeStatusFilter),
            searchScope: ResolveSearchScope(MapQueueSearchScope),
            keyword: NormalizeText(MapQueueKeyword),
            cursorUpdatedAt: cursorAt,
            cursorId: cursorId,
            newer: newer,
            ct: ct).ConfigureAwait(false);

        await RunOnUiAsync(() =>
        {
            AutoMapQueueRows.Clear();
            var pageSize = fullMapMode ? GetMapQueuePageSize() : CompactMapQueueRows;
            var requestedPage = Math.Max(1, MapQueuePage);
            if (fullMapMode)
            {
                if (olderPage == true)
                {
                    requestedPage = Math.Max(1, MapQueuePage + 1);
                }
                else if (olderPage == false)
                {
                    requestedPage = Math.Max(1, MapQueuePage - 1);
                }
            }

            var displayStart = ((requestedPage - 1) * pageSize) + 1;
            for (var i = 0; i < page.Rows.Count; i++)
            {
                var x = page.Rows[i];
                AutoMapQueueRows.Add(new MsfxAutoMapQueueGridRow(
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

            MapQueueTotalCount = page.TotalCount;
            HasMapQueuePrevPage = page.HasNewer;
            HasMapQueueNextPage = page.HasOlder;
            IsMapQueueLatestPage = !page.HasNewer;
            if (fullMapMode)
            {
                if (olderPage == true && page.Rows.Count > 0)
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
        SelectedAutoMapQueueRow = null;
        SelectedAutoTaskQueueRow = null;
        SelectedAutoLogRow = null;
        SetSelectedAutoTaskQueueRows(Array.Empty<MsfxAutoTaskQueueGridRow>());
    }

    private async void OnAutoTimerTick(object? sender, EventArgs e)
    {
        if (!IsAutoEnabled || IsAutoBusy)
        {
            return;
        }

        await RunAutoOnceInternalAsync(showProgressPanel: false);
    }

    private bool MatchUpoutFilter(MsfxListUpoutItem item)
    {
        var bill = NormalizeText(UpoutBillCodeKeyword);
        var drug = NormalizeText(UpoutDrugKeyword);
        var ent = NormalizeText(UpoutFromEntKeyword);

        if (!string.IsNullOrWhiteSpace(bill) && !ContainsIgnoreCase(item.BillCode, bill))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(drug) && !ContainsIgnoreCase(item.PhysicName, drug))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(ent) && !ContainsIgnoreCase(item.FromEntName, ent))
        {
            return false;
        }

        return true;
    }

    private static bool ContainsIgnoreCase(string? source, string value)
        => (source ?? string.Empty).Contains(value, StringComparison.OrdinalIgnoreCase);

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

    private void ApplyPullBatchPage()
    {
        var pageSize = GetPullBatchPageSize();
        var totalPages = Math.Max(1, (int)Math.Ceiling(PullBatchTotalCount / (double)pageSize));
        if (PullBatchPage > totalPages)
        {
            PullBatchPage = totalPages;
        }

        var page = Math.Max(1, PullBatchPage);
        var skip = (page - 1) * pageSize;
        var rows = _allPullBatchRows.Skip(skip).Take(pageSize).ToList();

        AutoPullBatchRows.Clear();
        foreach (var row in rows)
        {
            AutoPullBatchRows.Add(row);
        }

        HasPullBatchPrevPage = page > 1;
        HasPullBatchNextPage = page < totalPages;
    }

    private int GetMapQueuePageSize()
    {
        if (!int.TryParse(MapQueuePageSize, out var pageSize))
        {
            return 120;
        }

        return Math.Clamp(pageSize, 1, 500);
    }

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

    private static string? NormalizeFilterValue(string? value)
    {
        var text = (value ?? string.Empty).Trim();
        if (text.Length == 0 || string.Equals(text, "ALL", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return text;
    }

    private static string ResolveSearchScope(string? value)
    {
        var text = (value ?? string.Empty).Trim();
        return text switch
        {
            "最小包装码" => "TRACE",
            "单据编码" => "BILL",
            "药名/规格(原始)" => "SOURCE_RAW",
            "药名/规格(归一化)" => "SOURCE_NORM",
            "层级码(1-5)" => "LEVEL_CODE",
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
        }, DispatcherPriority.Background);
    }

    private static string FormatElapsed(TimeSpan elapsed)
        => $"{elapsed.TotalSeconds:F2}s";

    private static string FormatElapsed(long elapsedMs)
        => $"{elapsedMs / 1000d:F2}s";

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
        => OnPropertyChanged(nameof(IsAutoLogsEmpty));

    private void OnAutoPullBatchRowsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        => OnPropertyChanged(nameof(IsAutoPullBatchEmpty));

    private void OnAutoMapQueueRowsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        OnPropertyChanged(nameof(IsAutoMapQueueEmpty));
        OnPropertyChanged(nameof(MapQueueDisplayText));
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
        _upoutDateRangeController.Dispose();
        base.Dispose();
    }
}
