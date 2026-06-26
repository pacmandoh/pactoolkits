using System.Collections.Generic;
using PacToolkits.Application.DTOs;

namespace PacToolkits.Desktop.Avalonia.Services.Infrastructure;

public enum MsfxMappingBatchDialogAction
{
    Cancel = 0,
    DiscardTask = 1,
    ApplyMap = 2
}

public sealed record MsfxMappingBatchDialogResult(
    MsfxMappingBatchDialogAction Action,
    MsfxMappingBatchGroupRow? Group,
    string DrugId,
    string Spec);

public enum MsfxTaskSplitDialogAction
{
    Cancel = 0,
    ParentCluster = 1,
    Batch = 2,
    CustomQuantity = 3
}

public sealed record MsfxTaskSplitDialogModel(
    long TaskId,
    string SourceBillCode,
    string Target,
    int TotalCodes,
    IReadOnlyList<MsfxInjectTaskSplitCodeRow> SplitCodeRows);

public sealed record MsfxTaskSplitDialogResult(
    MsfxTaskSplitDialogAction Action,
    string? CustomQuantities = null);

public sealed record InfoDetailItem(string Label, string Value);

public sealed record InfoDetailDialogModel(
    string Header,
    string SubHeader,
    IReadOnlyList<InfoDetailItem> Items);

public sealed record MsfxMappingBatchDialogModel(
    IReadOnlyList<MsfxMappingBatchGroupRow> Groups,
    string MapStatusFilter,
    string CodeStatusFilter,
    string SearchScope,
    string Keyword);

public sealed record MsfxStateDetailDialogModel(
    string Header,
    string SubHeader,
    TraceEntryState State,
    string HighlightTitle,
    string HighlightMessage,
    IReadOnlyList<InfoDetailItem> Items);

public sealed record DrugKeyFixPreviewDialogModel(
    string SourceKeyDisplay,
    string TargetKeyDisplay,
    string TracePoolAffectedDisplay,
    string TraceTxnAffectedDisplay,
    string TargetExistsDisplay);
