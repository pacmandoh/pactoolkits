using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;

namespace PacToolkits.Desktop.Avalonia.Services.Infrastructure.Api;

/// <summary>HttpClient 附加 Bearer；经 IServiceProvider 晚取 PacApiClient，避免和 Handler 互相依赖</summary>
internal sealed class PacApiJwtHandler(IServiceProvider services) : DelegatingHandler
{
    private PacApiClient? _client;
    private IPacApiContractGate? _gate;

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var skipGate = request.Options.TryGetValue(PacApiClient.SkipContractGateKey, out var skip) && skip;
        if (!skipGate)
        {
            _gate ??= services.GetRequiredService<IPacApiContractGate>();
            await _gate.EnsureCompatibleAsync(cancellationToken).ConfigureAwait(false);
        }

        var client = _client ??= services.GetRequiredService<PacApiClient>();
        var force = request.Options.TryGetValue(PacApiClient.ForceTokenRefreshKey, out var refresh) && refresh;
        _ = request.Options.TryGetValue(PacApiClient.StaleAccessTokenKey, out string? stale);
        await client.EnsureTokenAsync(cancellationToken, forceRefresh: force, staleAccessToken: stale)
            .ConfigureAwait(false);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", client.CurrentAccessToken);
        return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
    }
}
