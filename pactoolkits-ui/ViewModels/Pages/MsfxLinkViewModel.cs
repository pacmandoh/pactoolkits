using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using pactoolkits_ui.Common;
using pactoolkits_ui.Contracts;
using pactoolkits_ui.Repositories;
using pactoolkits_ui.Services.Infrastructure;
using pactoolkits_ui.Services.Integration;
using pactoolkits_ui.Views.Dialogs;

namespace pactoolkits_ui.ViewModels.Pages;

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

public sealed record MsfxAutoTaskQueueGridRow(
    long TaskId,
    string SourceBillCode,
    string Target,
    string Status,
    string Progress,
    int RetryCount,
    string CreatedAt,
    string PickedAt,
    string FinishedAt,
    string ErrMsg,
    TraceEntryState State
);

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
    private readonly IMsfxApiClient _msfxApi;
    private readonly IMsfxSyncRepo _syncRepo;
    private readonly IAppConfigStore _configStore;
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
    [ObservableProperty] private bool _isAutoBoardBusy;
    [ObservableProperty] private string _autoStatus = "未启动";
    [ObservableProperty] private double _autoRunProgressValue;
    [ObservableProperty] private string _autoLastRunAtText = "--";
    [ObservableProperty] private string _autoNextRunAtText = "--";
    [ObservableProperty] private string _autoPullSummary = "批次：暂无";
    [ObservableProperty] private string _autoMapSummary = "映射：暂无";
    [ObservableProperty] private string _autoTaskSummary = "任务：暂无";
    [ObservableProperty] private string _autoRiskSummary = "异常：暂无";
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
    public ObservableCollection<MsfxUpoutGridRow> UpoutRows { get; } = new();
    public ObservableCollection<MsfxSubCodeGridRow> SubCodeRows { get; } = new();
    public ObservableCollection<MsfxAutoLogRow> AutoLogs { get; } = new();
    public ObservableCollection<MsfxAutoPullBatchGridRow> AutoPullBatchRows { get; } = new();
    public ObservableCollection<MsfxAutoMapQueueGridRow> AutoMapQueueRows { get; } = new();
    public ObservableCollection<MsfxAutoTaskQueueGridRow> AutoTaskQueueRows { get; } = new();
    public IReadOnlyList<MsfxAutoTaskQueueGridRow> SelectedAutoTaskQueueRowsSnapshot => _selectedAutoTaskQueueRowsSnapshot;
    public bool CanBatchReopenSelectedTasks => !IsAutoBoardBusy
                                               && SelectedAutoTaskQueueRowsSnapshot.Any(x =>
                                                   string.Equals(x.Status, "SUCCESS", StringComparison.OrdinalIgnoreCase));
    public bool CanBatchDiscardSelectedTasks => !IsAutoBoardBusy
                                                && SelectedAutoTaskQueueRowsSnapshot.Any(x =>
                                                    string.Equals(x.Status, "NEW", StringComparison.OrdinalIgnoreCase)
                                                    || string.Equals(x.Status, "FAILED", StringComparison.OrdinalIgnoreCase));

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
    private List<MsfxAutoTaskQueueGridRow> _selectedAutoTaskQueueRowsSnapshot = new();
    private DateTimeOffset? _mapCursorUpdatedAt;
    private long? _mapCursorId;
    private string? _lastAutoLogSignature;
    private bool _isResettingMapQueueFilters;
    private readonly RollingDateRangeController _upoutDateRangeController;

    public MsfxLinkViewModel(
        IMsfxApiClient msfxApi,
        IMsfxSyncRepo syncRepo,
        IAppConfigStore configStore,
        IToastService toast,
        IDialogService dialog)
    {
        _msfxApi = msfxApi;
        _syncRepo = syncRepo;
        _configStore = configStore;
        _toast = toast;
        _dialog = dialog;
        _upoutDateRangeController = new RollingDateRangeController(() =>
            PostOnUi(HandleUpoutDateRangeDayChanged, DispatcherPriority.Background));

        _autoTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMinutes(Math.Max(1, AutoIntervalMinutes))
        };
        _autoTimer.Tick += OnAutoTimerTick;

        AutoLogs.CollectionChanged += (_, _) => OnPropertyChanged(nameof(IsAutoLogsEmpty));
        AutoPullBatchRows.CollectionChanged += (_, _) => OnPropertyChanged(nameof(IsAutoPullBatchEmpty));
        AutoMapQueueRows.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(IsAutoMapQueueEmpty));
            OnPropertyChanged(nameof(MapQueueDisplayText));
        };
        AutoTaskQueueRows.CollectionChanged += (_, _) => OnPropertyChanged(nameof(IsAutoTaskQueueEmpty));
        UpoutRows.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(IsUpoutEmpty));
            OnPropertyChanged(nameof(HasUpoutNextPage));
        };
        SubCodeRows.CollectionChanged += (_, _) => OnPropertyChanged(nameof(IsSubCodeEmpty));

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
                UpoutFromDate = safeValue;
            else
                UpoutToDate = safeValue;
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

    partial void OnIsAutoBoardBusyChanged(bool value)
    {
        NotifyCommandsCoalesced("msfx.auto.board.commands", () =>
            NotifyCommands(RefreshAutoBoardCommand));
        PostOnUi(() => OnPropertyChanged(nameof(CanBatchReopenSelectedTasks)), DispatcherPriority.Background);
        PostOnUi(() => OnPropertyChanged(nameof(CanBatchDiscardSelectedTasks)), DispatcherPriority.Background);
    }

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

    partial void OnSelectedAutoPullBatchRowChanged(MsfxAutoPullBatchGridRow? value)
    {
        // 详情仅由行头点击触发，单元格点击不弹窗
    }

    partial void OnSelectedAutoMapQueueRowChanged(MsfxAutoMapQueueGridRow? value)
    {
        // 详情仅由行头点击触发，单元格点击不弹窗
    }

    partial void OnSelectedAutoTaskQueueRowChanged(MsfxAutoTaskQueueGridRow? value)
        => OnPropertyChanged(nameof(CanBatchReopenSelectedTasks));

    public void SetSelectedAutoTaskQueueRows(IReadOnlyList<MsfxAutoTaskQueueGridRow> rows)
    {
        _selectedAutoTaskQueueRowsSnapshot = rows
            .Where(static x => x is not null)
            .Distinct()
            .ToList();
        OnPropertyChanged(nameof(SelectedAutoTaskQueueRowsSnapshot));
        OnPropertyChanged(nameof(CanBatchReopenSelectedTasks));
        OnPropertyChanged(nameof(CanBatchDiscardSelectedTasks));
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
            return;

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
            UpoutPage = 1;

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
                    UpoutRows.Add(row);

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
            return;

        UpoutPage -= 1;
        await QueryUpoutCoreAsync(resetPage: false);
    }

    [RelayCommand]
    private async Task NextUpoutPageAsync()
    {
        if (!HasUpoutNextPage || IsUpoutBusy)
            return;

        UpoutPage += 1;
        await QueryUpoutCoreAsync(resetPage: false);
    }

    [RelayCommand]
    private async Task QuerySubCodesAsync()
    {
        if (IsSubcodeBusy)
            return;

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
                    throw new InvalidOperationException($"上游出库单查询失败：{BuildApiErrorMessage(list.Call)}");

                var found = list.Items.FirstOrDefault(x => string.Equals(x.BillCode, billCode, StringComparison.OrdinalIgnoreCase));
                if (found is not null)
                {
                    var toRef = NormalizeInput(found.ToRefUserId) ?? options.RefEntId;
                    var fromRef = NormalizeInput(found.FromRefUserId);
                    return fromRef is null ? null : (toRef, fromRef);
                }

                if (list.Items.Count < pageSize)
                    break;
            }
        }

        return null;
    }

    [RelayCommand]
    private async Task PrevSubcodePageAsync()
    {
        if (!HasSubcodePrevPage || IsSubcodeBusy)
            return;

        SubcodePage -= 1;
        await Task.CompletedTask;
    }

    [RelayCommand]
    private async Task NextSubcodePageAsync()
    {
        if (!HasSubcodeNextPage || IsSubcodeBusy)
            return;

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
            window = await _syncRepo.GetPullWindowAsync("listupout", ct).ConfigureAwait(false);
            AddAutoLog("任务", $"开始执行自动化拉取（{window.BeginAt:yyyy-MM-dd HH:mm:ss} ~ {window.EndAt:yyyy-MM-dd HH:mm:ss}）", TraceEntryState.Info);
            LogInfo("msfx.auto.run.start", "MSFX auto run started", new { window.BeginAt, window.EndAt });
            SetAutoProgress(5, $"拉取窗口 {window.BeginAt:MM-dd HH:mm} ~ {window.EndAt:MM-dd HH:mm}");

            var batch = await _syncRepo.StartPullBatchAsync("listupout", window.BeginAt, window.EndAt, ct)
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
                        await _syncRepo.RescheduleBillWatchAsync(
                            sourceApi: "listupout",
                            billCode: billCode,
                            lastSeenStatus: watchStatus,
                            lastError: err,
                            ct: ct).ConfigureAwait(false);
                        watchDeferredCount++;
                        AddAutoLog("待确认补偿", $"单据 {billCode} 暂未就绪，已延后重查：{err}", TraceEntryState.Warning);
                        return false;
                    }

                    await _syncRepo.UpsertBillRetryAsync(
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

                var billId = await _syncRepo.UpsertInboundBillAsync(
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
                    return await QueueRetryAndLogAsync(BuildApiErrorMessage(detail.Call)).ConfigureAwait(false);

                var swIngest = Stopwatch.StartNew();
                var ingest = await _syncRepo.IngestUpoutDetailAsync(
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
                    await _syncRepo.MarkBillRetrySucceededAsync("listupout", billCode, ct).ConfigureAwait(false);
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

                await _syncRepo.MarkBillWatchResolvedAsync("listupout", billCode, ct).ConfigureAwait(false);

                return true;
            }

            var dueRetries = await _syncRepo.GetDueBillRetriesAsync("listupout", 200, ct).ConfigureAwait(false);
            if (dueRetries.Count > 0)
            {
                AddAutoLog("重试", $"发现待重试单据 {dueRetries.Count} 条，优先处理", TraceEntryState.Info);
                foreach (var retry in dueRetries)
                {
                    ct.ThrowIfCancellationRequested();
                    if (!processedBillCodes.Add(retry.BillCode))
                        continue;
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
                    await _syncRepo.UpdatePullBatchRequestIdAsync(batchId, list.Call.RequestId, ct).ConfigureAwait(false);
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
                    await _syncRepo.UpsertBillWatchAsync(
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
                        continue;
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
                    break;
                page++;
            }

            var dueWatches = await _syncRepo.GetDueBillWatchesAsync("listupout", 200, ct).ConfigureAwait(false);
            if (dueWatches.Count > 0)
            {
                AddAutoLog("待确认补偿", $"发现待确认单据 {dueWatches.Count} 条，开始补偿重查", TraceEntryState.Info);
                foreach (var watch in dueWatches)
                {
                    ct.ThrowIfCancellationRequested();
                    if (!processedBillCodes.Add(watch.BillCode))
                        continue;

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
            var mapBefore = await _syncRepo.GetMappingStatusSnapshotAsync(ct).ConfigureAwait(false);
            AddAutoLog(
                "映射自检",
                $"执行前 PENDING {mapBefore.PendingCount}，MAPPED {mapBefore.MappedCount}，NEED_REVIEW {mapBefore.NeedReviewCount}，FAILED {mapBefore.FailedCount}，TOTAL {mapBefore.TotalCount}",
                TraceEntryState.Info);
            var map = await _syncRepo.ApplyMappingAsync(50000, ct).ConfigureAwait(false);
            var mapAfter = await _syncRepo.GetMappingStatusSnapshotAsync(ct).ConfigureAwait(false);
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
            var taskResult = await _syncRepo.BuildInjectTasksAsync(500, ct).ConfigureAwait(false);
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
            await _syncRepo.FinishPullBatchAsync(batchId, batchStatus, succeedCount, failCount, null, CancellationToken.None)
                .ConfigureAwait(false);
            await _syncRepo.AdvancePullCursorAsync("listupout", window.BeginAt, window.EndAt, batchId, batchStatus, CancellationToken.None)
                .ConfigureAwait(false);
            batchFinalized = true;
            await RefreshAutoPullPanelCoreAsync(ct).ConfigureAwait(false);

            SetAutoProgress(100, "巡检完成");
            AutoStatus = $"自动化拉取完成：API {totalApiRows}，已入库 {totalInboundRows}，单据 {totalBills}，码 {detailSubCodes}，重试成功 {retrySucceededCount}，重试失败 {retryFailedCount}，重试入队 {retryQueuedCount}，待确认入池 {watchQueuedCount}，补偿成功 {watchResolvedCount}，补偿延后 {watchDeferredCount}，新增任务 {taskResult.CreatedTasks}";
            AutoLastRunAtText = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            if (IsAutoEnabled)
                AutoNextRunAtText = DateTime.Now.AddMinutes(Math.Max(1, AutoIntervalMinutes)).ToString("yyyy-MM-dd HH:mm:ss");
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
            MarkDbDisconnectedOnTransportError(ex);
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
            if (IsDbConnected)
                _toast.Error("自动化监控", ex.Message);
        }
        finally
        {
            if (batchId > 0 && !batchFinalized)
            {
                try
                {
                    await _syncRepo.FinishPullBatchAsync(
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
            return;

        AutoExpandedPanel = string.Equals(AutoExpandedPanel, key, StringComparison.OrdinalIgnoreCase)
            ? string.Empty
            : key;
    }

    [RelayCommand]
    private async Task PrevMapQueuePageAsync()
    {
        if (!HasMapQueuePrevPage || IsAutoBoardBusy)
            return;

        await RunLocalReloadAsync(
            setBusy: v => IsAutoBoardBusy = v,
            action: ct => RefreshMapQueueAsync(ct, olderPage: false));
    }

    [RelayCommand]
    private Task PrevPullBatchPageAsync()
    {
        if (!HasPullBatchPrevPage)
            return Task.CompletedTask;

        PullBatchPage -= 1;
        return Task.CompletedTask;
    }

    [RelayCommand]
    private Task NextPullBatchPageAsync()
    {
        if (!HasPullBatchNextPage)
            return Task.CompletedTask;

        PullBatchPage += 1;
        return Task.CompletedTask;
    }

    [RelayCommand]
    private async Task NextMapQueuePageAsync()
    {
        if (!HasMapQueueNextPage || IsAutoBoardBusy)
            return;

        await RunLocalReloadAsync(
            setBusy: v => IsAutoBoardBusy = v,
            action: ct => RefreshMapQueueAsync(ct, olderPage: true));
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

    [RelayCommand]
    private async Task ReopenSelectedTaskAsync()
    {
        var selectedRows = SelectedAutoTaskQueueRowsSnapshot
            .Where(x => string.Equals(x.Status, "SUCCESS", StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (selectedRows.Count == 0)
        {
            _toast.Warn("任务重开", "请先选择至少一条 SUCCESS 任务");
            return;
        }

        if (IsAutoBoardBusy)
            return;

        var ok = await _dialog.Confirm(
            "重开注入任务",
            $"将重开选中的 {selectedRows.Count} 条 SUCCESS 任务，并重置为可执行队列。确认继续？").ConfigureAwait(false);
        if (!ok)
            return;

        try
        {
            IsAutoBoardBusy = true;
            var opName = Environment.UserName;
            var successCount = 0;
            var failedCount = 0;

            foreach (var taskRow in selectedRows)
            {
                try
                {
                    var result = await _syncRepo.ReopenInjectTaskAsync(
                        taskRow.TaskId,
                        opName,
                        "manual reopen from ui",
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
                _toast.Success("任务重开", $"成功 {successCount} 条，失败 {failedCount} 条");
            else
                _toast.Warn("任务重开", $"成功 {successCount} 条，失败 {failedCount} 条");

            await RefreshAutoTaskPanelCoreAsync(CancellationToken.None).ConfigureAwait(false);
        }
        finally
        {
            await RunOnUiAsync(() => IsAutoBoardBusy = false);
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
            return;

        var ok = await _dialog.Confirm(
            "弃用注入任务",
            $"将弃用选中的 {selectedRows.Count} 条任务。弃用后 Agent 将不再执行这些任务。确认继续？").ConfigureAwait(false);
        if (!ok)
            return;

        try
        {
            IsAutoBoardBusy = true;
            var opName = Environment.UserName;
            var successCount = 0;
            var failedCount = 0;

            foreach (var taskRow in selectedRows)
            {
                try
                {
                    var result = await _syncRepo.DiscardInjectTaskAsync(
                        taskRow.TaskId,
                        opName,
                        "manual discard from ui",
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
                _toast.Success("任务弃用", $"成功 {successCount} 条，失败 {failedCount} 条");
            else
                _toast.Warn("任务弃用", $"成功 {successCount} 条，失败 {failedCount} 条");

            await RefreshAutoTaskPanelCoreAsync(CancellationToken.None).ConfigureAwait(false);
        }
        finally
        {
            await RunOnUiAsync(() => IsAutoBoardBusy = false);
        }
    }

    [RelayCommand]
    private async Task OpenMapBatchDialogAsync()
    {
        if (IsAutoBoardBusy)
            return;

        try
        {
            var groups = await _syncRepo.GetMappingBatchGroupsAsync(
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
                return;

            if (res.Group is null)
            {
                _toast.Warn("批量映射", "请先在分组表中选择一条记录");
                return;
            }

            var group = res.Group;
            var action = res.Action == MsfxMappingBatchDialogAction.ApplyMap ? "APPLY_MAP" : "MARK_REVIEW";

            if (res.Action == MsfxMappingBatchDialogAction.ApplyMap &&
                (string.IsNullOrWhiteSpace(res.DrugId) || string.IsNullOrWhiteSpace(res.Spec)))
            {
                _toast.Warn("批量映射", "批量映射需要填写 drug_id 和 spec");
                return;
            }

            var preview = await _syncRepo.PreviewMappingBatchByGroupAsync(
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

            var confirmMsg = res.Action == MsfxMappingBatchDialogAction.ApplyMap
                ? $"分组“{group.SourceDrugNameRaw} / {group.SourceSpecRaw}”将影响 {preview.CandidateCount} 条，可执行 {preview.EligibleCount} 条，确认批量映射？"
                : $"分组“{group.SourceDrugNameRaw} / {group.SourceSpecRaw}”将影响 {preview.CandidateCount} 条，可执行 {preview.EligibleCount} 条，确认转待人工？";
            var ok = await _dialog.Confirm("批量映射", confirmMsg).ConfigureAwait(false);
            if (!ok)
                return;

            var apply = await _syncRepo.ApplyMappingBatchByGroupAsync(
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
                var built = await _syncRepo.BuildInjectTasksAsync(500, CancellationToken.None).ConfigureAwait(false);
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
            else
            {
                AddAutoLog("批量映射", $"分组转待人工 {apply.AffectedCount} 条", apply.AffectedCount > 0 ? TraceEntryState.Warning : TraceEntryState.Info);
                LogWarn("msfx.map.batch.mark_review", "MSFX batch mapping marked review", null, new
                {
                    apply.AffectedCount,
                    group.SourceDrugNameRaw,
                    group.SourceSpecRaw
                });
                _toast.Info("批量映射", $"已转待人工 {apply.AffectedCount} 条");
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
            return Task.CompletedTask;

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
    private Task ShowMapQueueDetailAsync(MsfxAutoMapQueueGridRow? row)
    {
        if (row is null)
            return Task.CompletedTask;

        return ShowMapQueueDialogAndHandleAsync(row);
    }

    private async Task ShowMapQueueDialogAndHandleAsync(MsfxAutoMapQueueGridRow row)
    {
        var res = await _dialog.ShowMsfxMappingDetailDialog(new MsfxMappingDetailDialogModel(
            TraceCode: row.LeafCode,
            SourceBillCode: row.SourceBillCode,
            SourceDrug: row.SourceDrugNameRaw,
            SourceSpec: row.SourceSpecRaw,
            Normalized: $"{row.SourceNameNorm} / {row.SourceSpecNorm}",
            MappedTarget: $"{row.MappedDrugId} / {row.MappedSpec}",
            MapStatus: row.MapStatus,
            CodeStatus: row.CodeStatus,
            UpdatedAt: row.UpdatedAt,
            IsReadOnly: string.Equals(row.MapStatus, "MAPPED", StringComparison.OrdinalIgnoreCase))).ConfigureAwait(false);

        if (res.Action == MsfxMappingDialogAction.MarkReview)
        {
            await _syncRepo.MarkNeedReviewAsync(row.StagingId, CancellationToken.None).ConfigureAwait(false);
            AddAutoLog("映射", $"staging {row.StagingId} 已标记 NEED_REVIEW", TraceEntryState.Warning);
            LogWarn("msfx.map.manual.mark_review", "MSFX staging marked NEED_REVIEW", null, new
            {
                row.StagingId
            });
            await RefreshAutoBoardAsync().ConfigureAwait(false);
            return;
        }

        if (res.Action == MsfxMappingDialogAction.ApplyMap)
        {
            if (string.IsNullOrWhiteSpace(res.DrugId) || string.IsNullOrWhiteSpace(res.Spec))
            {
                _toast.Warn("手动映射", "drug_id 与 spec 不能为空");
                return;
            }

            var ok = await _syncRepo.ApplyManualMappingAsync(row.StagingId, res.DrugId, res.Spec, CancellationToken.None).ConfigureAwait(false);
            if (!ok)
            {
                _toast.Error("手动映射", "映射失败，目标药品规格不存在或保存失败");
                return;
            }

            var built = await _syncRepo.BuildInjectTasksAsync(200, CancellationToken.None).ConfigureAwait(false);
            AddAutoLog("映射", $"staging {row.StagingId} 手动映射成功，新增任务 {built.CreatedTasks}", TraceEntryState.Success);
            LogWarn("msfx.map.manual.apply", "MSFX manual mapping applied", null, new
            {
                row.StagingId,
                DrugId = res.DrugId,
                Spec = res.Spec,
                built.CreatedTasks
            });
            await RefreshAutoBoardAsync().ConfigureAwait(false);
        }
    }

    [RelayCommand]
    private Task ShowTaskQueueDetailAsync(MsfxAutoTaskQueueGridRow? row)
    {
        if (row is null)
            return Task.CompletedTask;

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
            return Task.CompletedTask;
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
    private async Task RefreshAutoBoardAsync()
    {
        await RunLocalReloadAsync(
            setBusy: v => IsAutoBoardBusy = v,
            action: RefreshAutoBoardCoreAsync);
    }

    private async Task RefreshAutoBoardCoreAsync(CancellationToken ct)
    {
        try
        {
            var snap = await RefreshAutoPullPanelCoreAsync(ct).ConfigureAwait(false);
            await RefreshAutoMapPanelCoreAsync(ct).ConfigureAwait(false);
            await RefreshAutoTaskPanelCoreAsync(ct).ConfigureAwait(false);
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
            MarkDbDisconnectedOnTransportError(ex);
            AddAutoLog("审计", $"刷新数据库概览失败：{ex.Message}", TraceEntryState.Warning);
            LogWarn("msfx.audit.snapshot.refresh_fail", "MSFX snapshot refresh failed", ex);
            if (IsDbConnected)
                _toast.Error("刷新审计", ex.Message);
        }
    }

    private async Task<MsfxAutoBoardSnapshot> RefreshAutoSummaryCoreAsync(CancellationToken ct)
    {
        var snap = await _syncRepo.GetAutoBoardSnapshotAsync(ct).ConfigureAwait(false);
        await RunOnUiAsync(() =>
        {
            AutoPullSummary = $"批次#{snap.LastBatchId} {snap.LastBatchStatus} 成功{snap.LastBatchSuccessCount}/失败{snap.LastBatchFailCount}";
            AutoMapSummary = $"待映射{snap.MapPendingCount} 已映射{snap.MapMappedCount} 待人工{snap.MapNeedReviewCount} 失败{snap.MapFailedCount}";
            AutoTaskSummary =
                $"NEW {snap.TaskNewCount} RUNNING {snap.TaskRunningCount} SUCCESS {snap.TaskSuccessCount} FAILED {snap.TaskFailedCount} DISCARDED {snap.TaskDiscardedCount}";
            AutoRiskSummary = $"staging失败{snap.StagingFailedCount} 重复码{snap.StagingDuplicateCount} 任务取消{snap.TaskCancelledCount} 批次失败{snap.LastBatchFailCount}";

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
        var pullRows = await _syncRepo.GetRecentPullBatchesAsync(500, ct).ConfigureAwait(false);
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
                PullBatchPage = PullBatchTotalPages;
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
        var taskRows = await _syncRepo.GetInjectTaskQueueAsync(120, ct).ConfigureAwait(false);
        await RunOnUiAsync(() =>
        {
            AutoTaskQueueRows.Clear();
            foreach (var x in taskRows)
            {
                AutoTaskQueueRows.Add(new MsfxAutoTaskQueueGridRow(
                    TaskId: x.TaskId,
                    SourceBillCode: x.SourceBillCode ?? "--",
                    Target: $"{x.MappedDrugId} / {x.MappedSpec}",
                    Status: x.Status,
                    Progress: $"{x.SuccessCodes}/{x.TotalCodes} 成功, 失败{x.FailedCodes}",
                    RetryCount: x.RetryCount,
                    CreatedAt: x.CreatedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss"),
                    PickedAt: x.PickedAt?.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss") ?? "--",
                    FinishedAt: x.FinishedAt?.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss") ?? "--",
                    ErrMsg: x.ErrMsg ?? string.Empty,
                    State: ToTaskState(x.Status)));
            }
        });
        return snap;
    }

    private async Task RefreshMapQueueLatestAsync()
    {
        if (IsAutoBoardBusy || SelectedTabIndex != 0)
            return;

        await RunLocalReloadAsync(
            setBusy: v => IsAutoBoardBusy = v,
            action: ct => RefreshMapQueueAsync(ct, olderPage: null));
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
        var page = await _syncRepo.GetMappingQueuePageAsync(
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
                    requestedPage = Math.Max(1, MapQueuePage + 1);
                else if (olderPage == false)
                    requestedPage = Math.Max(1, MapQueuePage - 1);
            }

            var displayStart = ((requestedPage - 1) * pageSize) + 1;
            for (var i = 0; i < page.Rows.Count; i++)
            {
                var x = page.Rows[i];
                AutoMapQueueRows.Add(new MsfxAutoMapQueueGridRow(
                    DisplayIndex: displayStart + i,
                    StagingId: x.StagingId,
                    LeafCode: x.LeafCode,
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
                    MapQueuePage += 1;
                else if (olderPage == false && page.Rows.Count > 0)
                    MapQueuePage = Math.Max(1, MapQueuePage - 1);
                else if (olderPage is null)
                    MapQueuePage = 1;
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
            return;

        await RunAutoOnceInternalAsync(showProgressPanel: false);
    }

    private bool MatchUpoutFilter(MsfxListUpoutItem item)
    {
        var bill = NormalizeText(UpoutBillCodeKeyword);
        var drug = NormalizeText(UpoutDrugKeyword);
        var ent = NormalizeText(UpoutFromEntKeyword);

        if (!string.IsNullOrWhiteSpace(bill) && !ContainsIgnoreCase(item.BillCode, bill))
            return false;
        if (!string.IsNullOrWhiteSpace(drug) && !ContainsIgnoreCase(item.PhysicName, drug))
            return false;
        if (!string.IsNullOrWhiteSpace(ent) && !ContainsIgnoreCase(item.FromEntName, ent))
            return false;

        return true;
    }

    private static bool ContainsIgnoreCase(string? source, string value)
        => (source ?? string.Empty).Contains(value, StringComparison.OrdinalIgnoreCase);

    private int GetPageSize()
    {
        if (!int.TryParse(UpoutPageSize, out var pageSize))
            return 20;

        return Math.Clamp(pageSize, 1, 200);
    }

    private int GetSubcodePageSize()
    {
        if (!int.TryParse(SubcodePageSize, out var pageSize))
            return 200;

        return Math.Clamp(pageSize, 20, 2000);
    }

    private int GetPullBatchPageSize()
    {
        if (!int.TryParse(PullBatchPageSize, out var pageSize))
            return 20;

        return Math.Clamp(pageSize, 10, 500);
    }

    private void ApplyPullBatchPage()
    {
        var pageSize = GetPullBatchPageSize();
        var totalPages = Math.Max(1, (int)Math.Ceiling(PullBatchTotalCount / (double)pageSize));
        if (PullBatchPage > totalPages)
            PullBatchPage = totalPages;

        var page = Math.Max(1, PullBatchPage);
        var skip = (page - 1) * pageSize;
        var rows = _allPullBatchRows.Skip(skip).Take(pageSize).ToList();

        AutoPullBatchRows.Clear();
        foreach (var row in rows)
            AutoPullBatchRows.Add(row);

        HasPullBatchPrevPage = page > 1;
        HasPullBatchNextPage = page < totalPages;
    }

    private int GetMapQueuePageSize()
    {
        if (!int.TryParse(MapQueuePageSize, out var pageSize))
            return 120;

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
                SubCodeRows.Add(_allSubCodeRows[i]);
        }

        OnPropertyChanged(nameof(HasSubcodePrevPage));
        OnPropertyChanged(nameof(HasSubcodeNextPage));
    }

    private MsfxApiOptions BuildMsfxOptions()
    {
        var options = _configStore.Load().MsfxApi ?? new MsfxApiOptions();
        if (string.IsNullOrWhiteSpace(options.RefEntId))
            throw new InvalidOperationException("请先在设置页面配置接收企业 RefEntId");

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
            return null;
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
            return;

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
                AutoLogs.RemoveAt(AutoLogs.Count - 1);
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
            "NEED_REVIEW" => TraceEntryState.Warning,
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
            parts.Add(biz);
        if (!string.IsNullOrWhiteSpace(call.Summary))
            parts.Add(call.Summary.Trim());
        if (!string.IsNullOrWhiteSpace(call.RequestId))
            parts.Add($"request_id={call.RequestId.Trim()}");

        if (parts.Count == 0 && !string.IsNullOrWhiteSpace(call.ResponseText))
        {
            var text = call.ResponseText.Trim().Replace("\r", " ").Replace("\n", " ");
            if (text.Length > 220)
                text = text[..220] + "...";
            parts.Add(text);
        }

        return parts.Count == 0 ? "未知错误" : string.Join(" | ", parts);
    }

    public override void Dispose()
    {
        _autoTimer.Stop();
        _autoTimer.Tick -= OnAutoTimerTick;
        _upoutDateRangeController.Dispose();
        base.Dispose();
    }
}
