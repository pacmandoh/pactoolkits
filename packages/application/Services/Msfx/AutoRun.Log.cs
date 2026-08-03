using PacToolkits.Application.Abstractions;
using PacToolkits.Application.DTOs;

namespace PacToolkits.Application.Services.Msfx;

internal static class AutoRunLog
{
    public static void Progress(IMsfxAutoRunObserver observer, double value, string status)
        => observer.Report(new MsfxAutoRunUpdate(Math.Clamp(value, 0, 100), status));

    public static void Log(
        IMsfxAutoRunObserver observer,
        string stage,
        string message,
        TraceEntryState state)
        => observer.Report(new MsfxAutoRunUpdate(Stage: stage, Message: message, State: state));

    public static IMsfxAutoRunObserver WrapMonotonic(IMsfxAutoRunObserver inner)
        => new MonotonicObserver(inner);

    public static CancellationTokenSource CreateTimeout(int seconds)
        => new(TimeSpan.FromSeconds(Math.Clamp(seconds, 3, 120)));

    public static string? Normalize(string? value)
    {
        var normalized = (value ?? string.Empty).Trim();
        return normalized.Length == 0 ? null : normalized;
    }

    public static string FormatElapsed(long milliseconds) => $"{milliseconds / 1000d:F2}s";

    public static string BuildApiErrorMessage(MsfxApiCallResult call)
    {
        var parts = new List<string>(4);
        var business = $"{call.BizCode} {call.BizMessage}".Trim();
        if (!string.IsNullOrWhiteSpace(business))
        {
            parts.Add(business);
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
            parts.Add(text.Length > 220 ? text[..220] : text);
        }

        return parts.Count == 0 ? "未知错误" : string.Join("；", parts.Distinct(StringComparer.Ordinal));
    }

    private sealed class MonotonicObserver(IMsfxAutoRunObserver inner) : IMsfxAutoRunObserver
    {
        private double _progress;

        public bool IsManualWriteActive => inner.IsManualWriteActive;

        public void Report(MsfxAutoRunUpdate update)
        {
            if (update.Progress is { } value)
            {
                _progress = Math.Max(_progress, Math.Clamp(value, 0, 100));
                update = update with { Progress = _progress };
            }

            inner.Report(update);
        }

        public Task DataChangedAsync(MsfxAutoRunData data, CancellationToken ct)
            => inner.DataChangedAsync(data, ct);
    }
}
