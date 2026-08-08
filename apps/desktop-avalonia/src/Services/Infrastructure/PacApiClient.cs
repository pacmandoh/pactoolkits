#if false
// 暂缓接入：迁到 API 变更流前不编译；配置形态届时另定
using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using PacToolkits.Application.Abstractions;

namespace PacToolkits.Desktop.Avalonia.Services.Infrastructure;

/// <summary>
/// API HTTP：换票与带 Bearer 的请求
///
/// 不写明文 Key 到日志；baseUrl / apiKey 由构造注入（发布配置形态另定）
/// </summary>
public sealed class PacApiClient : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly string _baseUrl;
    private readonly string _apiKey;
    private readonly string _headerName;
    private readonly IAppLogger _logger;
    private readonly HttpClient _http = new() { Timeout = Timeout.InfiniteTimeSpan };
    private readonly SemaphoreSlim _tokenGate = new(1, 1);

    private string? _accessToken;
    private DateTimeOffset _tokenExpiresAt;

    public PacApiClient(string baseUrl, string apiKey, IAppLogger logger, string headerName = "X-Api-Key")
    {
        _baseUrl = (baseUrl ?? string.Empty).Trim().TrimEnd('/');
        _apiKey = (apiKey ?? string.Empty).Trim();
        _headerName = string.IsNullOrWhiteSpace(headerName) ? "X-Api-Key" : headerName.Trim();
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public bool IsConfigured
        => !string.IsNullOrWhiteSpace(_baseUrl) && !string.IsNullOrWhiteSpace(_apiKey);

    public async Task<HttpResponseMessage> SendAsync(
        Func<HttpRequestMessage> createRequest,
        CancellationToken ct)
    {
        await EnsureTokenAsync(ct).ConfigureAwait(false);
        using var request = createRequest();
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);
        var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct)
            .ConfigureAwait(false);

        if (response.StatusCode != System.Net.HttpStatusCode.Unauthorized)
        {
            return response;
        }

        response.Dispose();
        await EnsureTokenAsync(ct, forceRefresh: true).ConfigureAwait(false);
        using var retry = createRequest();
        retry.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);
        return await _http.SendAsync(retry, HttpCompletionOption.ResponseHeadersRead, ct)
            .ConfigureAwait(false);
    }

    public Uri Resolve(string relativePath)
        => new Uri($"{_baseUrl}/{relativePath.TrimStart('/')}");

    public void Dispose()
    {
        _http.Dispose();
        _tokenGate.Dispose();
    }

    private async Task EnsureTokenAsync(CancellationToken ct, bool forceRefresh = false)
    {
        if (!forceRefresh
            && !string.IsNullOrEmpty(_accessToken)
            && _tokenExpiresAt > DateTimeOffset.UtcNow.AddMinutes(1))
        {
            return;
        }

        await _tokenGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (!forceRefresh
                && !string.IsNullOrEmpty(_accessToken)
                && _tokenExpiresAt > DateTimeOffset.UtcNow.AddMinutes(1))
            {
                return;
            }

            if (!IsConfigured)
            {
                throw new InvalidOperationException("Pac API baseUrl/apiKey is not configured");
            }

            using var request = new HttpRequestMessage(HttpMethod.Post, Resolve("/v1/auth/token"));
            request.Headers.TryAddWithoutValidation(_headerName, _apiKey);

            using var response = await _http.SendAsync(request, ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                _logger.Warn(
                    "PacApi",
                    "token.fail",
                    $"Token exchange failed with HTTP {(int)response.StatusCode}");
                response.EnsureSuccessStatusCode();
            }

            await using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            var body = await JsonSerializer.DeserializeAsync<TokenResponse>(stream, JsonOptions, ct)
                .ConfigureAwait(false)
                ?? throw new InvalidOperationException("empty token response");

            _accessToken = body.AccessToken;
            if (body.ExpiresIn <= 0)
            {
                throw new InvalidOperationException("token response expiresIn must be > 0");
            }

            _tokenExpiresAt = DateTimeOffset.UtcNow.AddSeconds(body.ExpiresIn);
            _logger.Info("PacApi", "token.ok", "API access token refreshed");
        }
        finally
        {
            _tokenGate.Release();
        }
    }

    private sealed record TokenResponse(string AccessToken, string TokenType, int ExpiresIn, string ClientId);
}
#endif
