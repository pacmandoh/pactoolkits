using System.Collections.Generic;
using PacToolkits.Application.DTOs;

namespace PacToolkits.Desktop.Avalonia.Services.Infrastructure.Dialogs;

public enum MsfxTaskSplitAction
{
    Cancel = 0,
    ParentCluster = 1,
    Batch = 2,
    CustomQuantity = 3
}

public sealed record MsfxTaskSplitArgs(
    long TaskId,
    string SourceBillCode,
    string Target,
    int TotalCodes,
    IReadOnlyList<MsfxInjectSplitCodeRow> SplitCodeRows);

public sealed record MsfxTaskSplitResult(
    MsfxTaskSplitAction Action,
    string? CustomQuantities = null);

public sealed record InfoDetailItem(string Label, string Value);

public sealed record InfoDetailArgs(
    string Header,
    string SubHeader,
    IReadOnlyList<InfoDetailItem> Items);

public sealed record BarcodePreviewDetailArgs(
    string Header,
    string SubHeader,
    byte[] PngBytes,
    IReadOnlyList<InfoDetailItem> Items);

public sealed record AppInfoArgs(
    string Version,
    string Desktop,
    string Agents,
    string MinApiContract,
    string MaxApiContract,
    string ReleaseDate);

public sealed record MsfxStateDetailArgs(
    string Header,
    string SubHeader,
    TraceEntryState State,
    string HighlightTitle,
    string HighlightMessage,
    IReadOnlyList<InfoDetailItem> Items);

public sealed record DrugKeyFixPreviewArgs(
    string SourceKeyDisplay,
    string TargetKeyDisplay,
    string TracePoolAffectedDisplay,
    string TraceTxnAffectedDisplay,
    string MsfxAffectedDisplay,
    string TargetExistsDisplay);
