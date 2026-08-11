using System.Diagnostics;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using PacToolkits.Application.Diagnostics;

namespace PacToolkits.Desktop.Avalonia.Services.Infrastructure.Api;

/// <summary>
/// 出站请求挂专属客户端 Activity（pacapi.http）再写 traceparent
///
/// 服务端父 span 是 pacapi.http，不是页面 Activity；自定义 PrimaryHandler 时没有 System.Net.Http 诊断层
/// </summary>
internal sealed class PacApiTraceHandler : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        using var activity = PacActivities.Desktop.StartActivity("pacapi.http", ActivityKind.Client);
        PacTrace.Inject(request, activity);
        return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
    }
}
