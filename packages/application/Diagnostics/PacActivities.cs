using System.Diagnostics;

namespace PacToolkits.Application.Diagnostics;

/// <summary>Desktop、API、Agents 共用的 ActivitySource 名；不用 OTel SDK，进程内采样后写进日志</summary>
public static class PacActivities
{
    public const string DesktopName = "PacToolkits.Desktop";
    public const string ApiName = "PacToolkits.Api";
    public const string AgentsName = "PacToolkits.Agents";

    public static ActivitySource Desktop { get; } = new(DesktopName);
    public static ActivitySource Api { get; } = new(ApiName);
    public static ActivitySource Agents { get; } = new(AgentsName);

    private static int _listening;

    /// <summary>
    /// 注册进程内采样；否则 StartActivity 和 ASP.NET 请求 Activity 经常是 null
    ///
    /// 听 PacToolkits.*、Microsoft.AspNetCore（入站 traceparent）、System.Net.Http
    /// </summary>
    public static void EnsureListening()
    {
        if (Interlocked.Exchange(ref _listening, 1) == 1)
        {
            return;
        }

        ActivitySource.AddActivityListener(new ActivityListener
        {
            ShouldListenTo = static source =>
                source.Name.StartsWith("PacToolkits", StringComparison.Ordinal)
                || source.Name.StartsWith("Microsoft.AspNetCore", StringComparison.Ordinal)
                || string.Equals(source.Name, "System.Net.Http", StringComparison.Ordinal),
            Sample = static (ref ActivityCreationOptions<ActivityContext> _)
                => ActivitySamplingResult.AllDataAndRecorded,
        });
    }
}
