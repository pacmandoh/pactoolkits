// 暂缓 DI：未注册；正式启用时用 IHttpClientFactory 注册 pac-api / pac-sse / 换票客户端
using System;
using System.Net;
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
/// 换票 / 普通 API / SSE 分三个 HttpClient；后两者经 <see cref="JwtHandler"/> 附加 Bearer
/// 不写明文 Key 到日志；baseUrl / apiKey 由构造注入（发布配置形态另定）
/// </summary>
public sealed class PacApiClient : IDisposable
{
    public static readonly TimeSpan ApiTimeout = TimeSpan.FromSeconds(30);

    private static readonly HttpRequestOptionsKey<bool> ForceTokenRefreshKey = new("pac.forceTokenRefresh");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly string _baseUrl;
    private readonly string _apiKey;
    private readonly string _headerName;
    private readonly IAppLogger _logger;

    private readonly HttpClient _tokenHttp;
    private readonly HttpClient _apiHttp;
    private readonly HttpClient _sseHttp;

    private readonly SemaphoreSlim _tokenGate = new(1, 1);

    private string? _accessToken;
    private DateTimeOffset _tokenExpiresAt;

    public PacApiClient(string baseUrl, string apiKey, IAppLogger logger, string headerName = "X-Api-Key")
        : this(
            baseUrl,
            apiKey,
            logger,
            headerName,
            new HttpClientHandler(),
            new HttpClientHandler(),
            new HttpClientHandler())
    {
    }

    /// <summary>单测注入各 HttpClient 的 InnerHandler</summary>
    internal PacApiClient(
        string baseUrl,
        string apiKey,
        IAppLogger logger,
        string headerName,
        HttpMessageHandler tokenHandler,
        HttpMessageHandler apiInner,
        HttpMessageHandler sseInner)
    {
        _baseUrl = (baseUrl ?? string.Empty).Trim().TrimEnd('/');
        _apiKey = (apiKey ?? string.Empty).Trim();
        _headerName = string.IsNullOrWhiteSpace(headerName) ? "X-Api-Key" : headerName.Trim();
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        ArgumentNullException.ThrowIfNull(tokenHandler);
        ArgumentNullException.ThrowIfNull(apiInner);
        ArgumentNullException.ThrowIfNull(sseInner);

        _tokenHttp = new HttpClient(tokenHandler) { Timeout = ApiTimeout };
        _apiHttp = new HttpClient(new JwtHandler(this) { InnerHandler = apiInner })
        {
            Timeout = ApiTimeout,
        };
        _sseHttp = new HttpClient(new JwtHandler(this) { InnerHandler = sseInner })
        {
            Timeout = Timeout.InfiniteTimeSpan,
        };
    }

    public bool IsConfigured
        => !string.IsNullOrWhiteSpace(_baseUrl) && !string.IsNullOrWhiteSpace(_apiKey);

    internal TimeSpan TokenHttpTimeout => _tokenHttp.Timeout;

    internal TimeSpan ApiHttpTimeout => _apiHttp.Timeout;

    internal TimeSpan SseHttpTimeout => _sseHttp.Timeout;

    public Task<HttpResponseMessage> SendAsync(
        Func<HttpRequestMessage> createRequest,
        CancellationToken ct)
        => SendOnAsync(_apiHttp, createRequest, ct);

    /// <summary>SSE：HttpClient 无限 Timeout，结束连接靠 <paramref name="ct"/></summary>
    public Task<HttpResponseMessage> SendSseAsync(
        Func<HttpRequestMessage> createRequest,
        CancellationToken ct)
        => SendOnAsync(_sseHttp, createRequest, ct);

    public Uri Resolve(string relativePath)
        => new($"{_baseUrl}/{relativePath.TrimStart('/')}");

    public void Dispose()
    {
        _tokenHttp.Dispose();
        _apiHttp.Dispose();
        _sseHttp.Dispose();
        _tokenGate.Dispose();
    }

    private async Task<HttpResponseMessage> SendOnAsync(
        HttpClient http,
        Func<HttpRequestMessage> createRequest,
        CancellationToken ct)
    {
        using var request = createRequest();
        var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct)
            .ConfigureAwait(false);

        if (response.StatusCode != HttpStatusCode.Unauthorized)
        {
            return response;
        }

        response.Dispose();
        using var retry = createRequest();
        retry.Options.Set(ForceTokenRefreshKey, true);
        return await http.SendAsync(retry, HttpCompletionOption.ResponseHeadersRead, ct)
            .ConfigureAwait(false);
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

            using var response = await _tokenHttp.SendAsync(request, ct).ConfigureAwait(false);
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

    /// <summary>为出站请求附加 Bearer；401 重试经 ForceTokenRefreshKey 强制换票</summary>
    private sealed class JwtHandler : DelegatingHandler
    {
        private readonly PacApiClient _client;

        public JwtHandler(PacApiClient client)
        {
            _client = client;
        }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var force = request.Options.TryGetValue(ForceTokenRefreshKey, out var refresh) && refresh;
            await _client.EnsureTokenAsync(cancellationToken, forceRefresh: force).ConfigureAwait(false);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _client._accessToken);
            return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }
    }

    private sealed record TokenResponse(string AccessToken, string TokenType, int ExpiresIn, string ClientId);
}
