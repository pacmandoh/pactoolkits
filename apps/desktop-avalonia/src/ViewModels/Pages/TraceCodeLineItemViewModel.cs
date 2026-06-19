using PacToolkits.Application.DTOs;

namespace PacToolkits.Desktop.Avalonia.ViewModels.Pages;

public sealed class TraceCodeLineItemViewModel
{
    public TraceCodeLineItemViewModel(TraceCodeLineAnalysis line)
    {
        DisplayText = line.Raw;
        Status = line.Status;
    }

    public string DisplayText { get; }

    public TraceCodeLineStatus Status { get; }

    public bool IsValid => Status == TraceCodeLineStatus.Valid;

    public bool IsBlank => Status == TraceCodeLineStatus.Blank;

    public bool IsScanDuplicate => Status == TraceCodeLineStatus.ScanDuplicate;

    public bool IsPoolDuplicate => Status == TraceCodeLineStatus.PoolDuplicate;

    public bool IsDuplicate => Status is TraceCodeLineStatus.ScanDuplicate or TraceCodeLineStatus.PoolDuplicate;

    public bool IsInvalid => Status == TraceCodeLineStatus.Invalid;

    public bool ShowStatusHint => Status is TraceCodeLineStatus.ScanDuplicate
        or TraceCodeLineStatus.PoolDuplicate
        or TraceCodeLineStatus.Invalid;

    public string StatusHint => Status switch
    {
        TraceCodeLineStatus.ScanDuplicate => "本批重复",
        TraceCodeLineStatus.PoolDuplicate => "已入库",
        TraceCodeLineStatus.Invalid => "不合规则",
        _ => string.Empty
    };
}
