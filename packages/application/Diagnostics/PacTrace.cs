using System.Diagnostics;

namespace PacToolkits.Application.Diagnostics;

/// <summary>Desktop 与 API 之间传 W3C traceparent；不用 OTel SDK</summary>
public static class PacTrace
{
    public const string HeaderName = "traceparent";

    public static string? FormatTraceParent(Activity? activity = null)
    {
        activity ??= Activity.Current;
        if (activity is null || activity.IdFormat != ActivityIdFormat.W3C)
        {
            return null;
        }

        return activity.Id;
    }

    public static void Inject(HttpRequestMessage request, Activity? activity = null)
    {
        ArgumentNullException.ThrowIfNull(request);
        var value = FormatTraceParent(activity);
        if (value is null || request.Headers.Contains(HeaderName))
        {
            return;
        }

        request.Headers.TryAddWithoutValidation(HeaderName, value);
    }

    public static string? CurrentTraceId
        => Activity.Current?.TraceId.ToString();

    public static string? CurrentSpanId
        => Activity.Current?.SpanId.ToString();
}
