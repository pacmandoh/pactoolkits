using PacToolkits.Application.DTOs;

namespace PacToolkits.Desktop.Avalonia.Ui.Formatting;

public static class HighlightFilterKeys
{
    public static TraceCodeHighlightFilter FromKey(string? key)
        => key switch
        {
            "valid" => TraceCodeHighlightFilter.Valid,
            "duplicate" => TraceCodeHighlightFilter.Duplicate,
            "pool" => TraceCodeHighlightFilter.PoolSkip,
            "invalid" => TraceCodeHighlightFilter.Invalid,
            "total" => TraceCodeHighlightFilter.All,
            _ => TraceCodeHighlightFilter.None
        };
}
