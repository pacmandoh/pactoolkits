using System;
using CommunityToolkit.Mvvm.ComponentModel;
using PacToolkits.Application.DTOs;

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
