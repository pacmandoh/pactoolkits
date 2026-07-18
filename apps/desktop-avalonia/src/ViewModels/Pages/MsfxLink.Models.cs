using System;
using CommunityToolkit.Mvvm.ComponentModel;
using PacToolkits.Application.DTOs;
using PacToolkits.Desktop.Avalonia.Common;

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
    string ToRefUserId,
    string ProduceEntName,
    string LogisticsStatus,
    TraceEntryState State
)
{
    public int DisplayIndex { get; init; }
}

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
)
{
    public int DisplayIndex { get; init; }
}

public sealed partial class MsfxAutoLogRow : ObservableObject
{
    public MsfxAutoLogRow(string at, string stage, string message, TraceEntryState state)
    {
        _at = at;
        _stage = stage;
        _message = message;
        _state = state;
    }

    [ObservableProperty] private string _at;
    [ObservableProperty] private string _stage;
    [ObservableProperty] private string _message;
    [ObservableProperty] private TraceEntryState _state;
    [ObservableProperty] private int _displayIndex;

    public MsfxAutoLogRow CreatePageRow(int displayIndex)
        => new(At, Stage, Message, State) { DisplayIndex = displayIndex };

    public void UpdateFrom(MsfxAutoLogRow source)
    {
        At = source.At;
        Stage = source.Stage;
        Message = source.Message;
        State = source.State;
        DisplayIndex = source.DisplayIndex;
    }
}

public sealed partial class MsfxAutoPullBatchGridRow : ObservableObject
{
    public MsfxAutoPullBatchGridRow(
        long batchId,
        string sourceApi,
        string window,
        string status,
        int successCount,
        int failCount,
        string startedAt,
        string finishedAt,
        string errMsg,
        TraceEntryState state)
    {
        _batchId = batchId;
        _sourceApi = sourceApi;
        _window = window;
        _status = status;
        _successCount = successCount;
        _failCount = failCount;
        _startedAt = startedAt;
        _finishedAt = finishedAt;
        _errMsg = errMsg;
        _state = state;
    }

    [ObservableProperty] private long _batchId;
    [ObservableProperty] private string _sourceApi;
    [ObservableProperty] private string _window;
    [ObservableProperty] private string _status;
    [ObservableProperty] private int _successCount;
    [ObservableProperty] private int _failCount;
    [ObservableProperty] private string _startedAt;
    [ObservableProperty] private string _finishedAt;
    [ObservableProperty] private string _errMsg;
    [ObservableProperty] private TraceEntryState _state;
    [ObservableProperty] private int _displayIndex;

    public MsfxAutoPullBatchGridRow CreatePageRow(int displayIndex)
        => new(
            BatchId,
            SourceApi,
            Window,
            Status,
            SuccessCount,
            FailCount,
            StartedAt,
            FinishedAt,
            ErrMsg,
            State)
        {
            DisplayIndex = displayIndex
        };

    public void UpdateFrom(MsfxAutoPullBatchGridRow source)
    {
        BatchId = source.BatchId;
        SourceApi = source.SourceApi;
        Window = source.Window;
        Status = source.Status;
        SuccessCount = source.SuccessCount;
        FailCount = source.FailCount;
        StartedAt = source.StartedAt;
        FinishedAt = source.FinishedAt;
        ErrMsg = source.ErrMsg;
        State = source.State;
        DisplayIndex = source.DisplayIndex;
    }
}

public sealed partial class MsfxAutoMapQueueGridRow : ObservableObject
{
    public MsfxAutoMapQueueGridRow(
        int displayIndex,
        long stagingId,
        string leafCode,
        string produceBatchNo,
        string sourceBillTime,
        string sourceBillCode,
        string sourceDrugNameRaw,
        string sourceSpecRaw,
        string sourceNameNorm,
        string sourceSpecNorm,
        string sourceCodeLevel1,
        string sourceCodeLevel2,
        string sourceCodeLevel3,
        string sourceCodeLevel4,
        string sourceCodeLevel5,
        string mapReasonCode,
        string mapReasonDetail,
        string mappedDrugId,
        string mappedSpec,
        string mapStatus,
        string codeStatus,
        string updatedAt,
        TraceEntryState state,
        DateTimeOffset updatedAtRaw)
    {
        _displayIndex = displayIndex;
        _stagingId = stagingId;
        _leafCode = leafCode;
        _produceBatchNo = produceBatchNo;
        _sourceBillTime = sourceBillTime;
        _sourceBillCode = sourceBillCode;
        _sourceDrugNameRaw = sourceDrugNameRaw;
        _sourceSpecRaw = sourceSpecRaw;
        _sourceNameNorm = sourceNameNorm;
        _sourceSpecNorm = sourceSpecNorm;
        _sourceCodeLevel1 = sourceCodeLevel1;
        _sourceCodeLevel2 = sourceCodeLevel2;
        _sourceCodeLevel3 = sourceCodeLevel3;
        _sourceCodeLevel4 = sourceCodeLevel4;
        _sourceCodeLevel5 = sourceCodeLevel5;
        _mapReasonCode = mapReasonCode;
        _mapReasonDetail = mapReasonDetail;
        _mappedDrugId = mappedDrugId;
        _mappedSpec = mappedSpec;
        _mapStatus = mapStatus;
        _codeStatus = codeStatus;
        _updatedAt = updatedAt;
        _state = state;
        _updatedAtRaw = updatedAtRaw;
    }

    [ObservableProperty] private int _displayIndex;
    [ObservableProperty] private long _stagingId;
    [ObservableProperty] private string _leafCode;
    [ObservableProperty] private string _produceBatchNo;
    [ObservableProperty] private string _sourceBillTime;
    [ObservableProperty] private string _sourceBillCode;
    [ObservableProperty] private string _sourceDrugNameRaw;
    [ObservableProperty] private string _sourceSpecRaw;
    [ObservableProperty] private string _sourceNameNorm;
    [ObservableProperty] private string _sourceSpecNorm;
    [ObservableProperty] private string _sourceCodeLevel1;
    [ObservableProperty] private string _sourceCodeLevel2;
    [ObservableProperty] private string _sourceCodeLevel3;
    [ObservableProperty] private string _sourceCodeLevel4;
    [ObservableProperty] private string _sourceCodeLevel5;
    [ObservableProperty] private string _mapReasonCode;
    [ObservableProperty] private string _mapReasonDetail;
    [ObservableProperty] private string _mappedDrugId;
    [ObservableProperty] private string _mappedSpec;
    [ObservableProperty] private string _mapStatus;
    [ObservableProperty] private string _codeStatus;
    [ObservableProperty] private string _updatedAt;
    [ObservableProperty] private TraceEntryState _state;
    [ObservableProperty] private DateTimeOffset _updatedAtRaw;

    public void UpdateFrom(MsfxAutoMapQueueGridRow source)
    {
        DisplayIndex = source.DisplayIndex;
        StagingId = source.StagingId;
        LeafCode = source.LeafCode;
        ProduceBatchNo = source.ProduceBatchNo;
        SourceBillTime = source.SourceBillTime;
        SourceBillCode = source.SourceBillCode;
        SourceDrugNameRaw = source.SourceDrugNameRaw;
        SourceSpecRaw = source.SourceSpecRaw;
        SourceNameNorm = source.SourceNameNorm;
        SourceSpecNorm = source.SourceSpecNorm;
        SourceCodeLevel1 = source.SourceCodeLevel1;
        SourceCodeLevel2 = source.SourceCodeLevel2;
        SourceCodeLevel3 = source.SourceCodeLevel3;
        SourceCodeLevel4 = source.SourceCodeLevel4;
        SourceCodeLevel5 = source.SourceCodeLevel5;
        MapReasonCode = source.MapReasonCode;
        MapReasonDetail = source.MapReasonDetail;
        MappedDrugId = source.MappedDrugId;
        MappedSpec = source.MappedSpec;
        MapStatus = source.MapStatus;
        CodeStatus = source.CodeStatus;
        UpdatedAt = source.UpdatedAt;
        State = source.State;
        UpdatedAtRaw = source.UpdatedAtRaw;
    }
}

public sealed partial class MsfxMappingBatchGroupGridRow : ObservableObject, ISelectableRow
{
    public MsfxMappingBatchGroupGridRow(MsfxMappingBatchGroupRow source)
    {
        Source = source;
    }

    public MsfxMappingBatchGroupRow Source { get; }
    public string SourceBillTimes => Source.SourceBillTimes;
    public string SourceBillCodes => Source.SourceBillCodes;
    public string SourceDrugNameRaw => Source.SourceDrugNameRaw;
    public string SourceSpecRaw => Source.SourceSpecRaw;
    public string SourceNameNorm => Source.SourceNameNorm;
    public string SourceSpecNorm => Source.SourceSpecNorm;
    public int TotalCount => Source.TotalCount;
    public int PendingCount => Source.PendingCount;
    public int NeedReviewCount => Source.NeedReviewCount;
    public int FailedCount => Source.FailedCount;

    [ObservableProperty] private bool _isSelected;
}

public sealed partial class MsfxAutoTaskQueueGridRow : ObservableObject, ISelectableRow
{
    public MsfxAutoTaskQueueGridRow(
        long taskId,
        string sourceBillCode,
        string batchNos,
        string mappedDrugId,
        string mappedSpec,
        int totalCodes,
        int currentCodeCount,
        string target,
        string status,
        string progress,
        int retryCount,
        string createdAt,
        string pickedAt,
        string finishedAt,
        string errMsg,
        TraceEntryState state)
    {
        _taskId = taskId;
        _sourceBillCode = sourceBillCode;
        _batchNos = batchNos;
        _mappedDrugId = mappedDrugId;
        _mappedSpec = mappedSpec;
        _totalCodes = totalCodes;
        _currentCodeCount = currentCodeCount;
        _target = target;
        _status = status;
        _progress = progress;
        _retryCount = retryCount;
        _createdAt = createdAt;
        _pickedAt = pickedAt;
        _finishedAt = finishedAt;
        _errMsg = errMsg;
        _state = state;
    }

    [ObservableProperty] private long _taskId;
    [ObservableProperty] private string _sourceBillCode;
    [ObservableProperty] private string _batchNos;
    [ObservableProperty] private string _mappedDrugId;
    [ObservableProperty] private string _mappedSpec;
    [ObservableProperty] private int _totalCodes;
    [ObservableProperty] private int _currentCodeCount;
    [ObservableProperty] private string _target;
    [ObservableProperty] private string _status;
    [ObservableProperty] private string _progress;
    [ObservableProperty] private int _retryCount;
    [ObservableProperty] private string _createdAt;
    [ObservableProperty] private string _pickedAt;
    [ObservableProperty] private string _finishedAt;
    [ObservableProperty] private string _errMsg;
    [ObservableProperty] private TraceEntryState _state;
    [ObservableProperty] private int _displayIndex;
    [ObservableProperty] private bool _isSelected;

    public MsfxAutoTaskQueueGridRow CreatePageRow(int displayIndex)
        => new(
            TaskId,
            SourceBillCode,
            BatchNos,
            MappedDrugId,
            MappedSpec,
            TotalCodes,
            CurrentCodeCount,
            Target,
            Status,
            Progress,
            RetryCount,
            CreatedAt,
            PickedAt,
            FinishedAt,
            ErrMsg,
            State)
        {
            DisplayIndex = displayIndex,
            IsSelected = IsSelected
        };

    public void UpdateFrom(MsfxAutoTaskQueueGridRow source)
    {
        TaskId = source.TaskId;
        SourceBillCode = source.SourceBillCode;
        BatchNos = source.BatchNos;
        MappedDrugId = source.MappedDrugId;
        MappedSpec = source.MappedSpec;
        TotalCodes = source.TotalCodes;
        CurrentCodeCount = source.CurrentCodeCount;
        Target = source.Target;
        Status = source.Status;
        Progress = source.Progress;
        RetryCount = source.RetryCount;
        CreatedAt = source.CreatedAt;
        PickedAt = source.PickedAt;
        FinishedAt = source.FinishedAt;
        ErrMsg = source.ErrMsg;
        State = source.State;
        DisplayIndex = source.DisplayIndex;
        IsSelected = source.IsSelected;
    }
}

public enum TaskQueueBatchActionMode
{
    None = 0,
    Merge = 1,
    Remap = 2,
    Discard = 3,
    Reopen = 4,
    Split = 5
}
