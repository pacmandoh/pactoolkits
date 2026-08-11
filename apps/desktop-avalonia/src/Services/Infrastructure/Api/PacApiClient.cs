using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using PacToolkits.Application.Abstractions;
using PacToolkits.Application.Diagnostics;
using PacToolkits.Application.DTOs;
using PacToolkits.Application.Serialization;

namespace PacToolkits.Desktop.Avalonia.Services.Infrastructure.Api;

/// <summary>
/// API HTTP：换票与带 Bearer 的请求
///
/// 换票、普通 API、SSE 分三个 HttpClient；后两者经 Jwt 附加 Bearer
/// 不写明文 Key 到日志；baseUrl 与 apiKey 由配置或构造注入
/// </summary>
public sealed class PacApiClient : IDisposable
{
    public const string TokenClientName = "pac-token";
    public const string ApiClientName = "pac-api";
    public const string SseClientName = "pac-sse";

    // 含多次 GET 重试；单次约 10s，外层总超时须大于重试合计
    public static readonly TimeSpan ApiTimeout = TimeSpan.FromSeconds(45);

    /// <summary>单次响应正文上限；超出抛 PacApiException（code=response_too_large）</summary>
    public const long MaxResponseBytes = 4L * 1024 * 1024;

    internal static readonly HttpRequestOptionsKey<bool> ForceTokenRefreshKey = new("pac.forceTokenRefresh");
    internal static readonly HttpRequestOptionsKey<string> StaleAccessTokenKey = new("pac.staleAccessToken");
    // 查 /v1/system/info 时跳过 contract 检查，否则 Handler 会再次进 Gate
    internal static readonly HttpRequestOptionsKey<bool> SkipContractGateKey = new("pac.skipContractGate");

    private readonly string _baseUrl;
    private readonly string _apiKey;
    private readonly string _headerName;
    private readonly IAppLogger _logger;
    private readonly TimeProvider _time;
    private readonly bool _ownsHttp;

    private readonly HttpClient _tokenHttp;
    private readonly HttpClient _apiHttp;
    private readonly HttpClient _sseHttp;

    private readonly SemaphoreSlim _tokenGate = new(1, 1);

    // 整份 Token 一次发布，避免 accessToken 与 RefreshAt 撕裂
    private TokenState? _token;

    // 提前量：寿命 10%（约在 90% 处换票），且最多提前 1 分钟；短票也能复用
    private static readonly TimeSpan MaxRefreshEarly = TimeSpan.FromMinutes(1);

    private sealed record TokenState(
        string AccessToken,
        DateTimeOffset ExpiresAt,
        DateTimeOffset RefreshAt);

    public PacApiClient(
        IHttpClientFactory httpClientFactory,
        IOptions<PacApiOptions> options,
        IAppLogger logger,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(httpClientFactory);
        ArgumentNullException.ThrowIfNull(options);
        var o = options.Value ?? new PacApiOptions();
        _baseUrl = (o.BaseUrl ?? string.Empty).Trim().TrimEnd('/');
        _apiKey = (o.ApiKey ?? string.Empty).Trim();
        _headerName = string.IsNullOrWhiteSpace(o.HeaderName) ? "X-Api-Key" : o.HeaderName.Trim();
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _time = timeProvider ?? TimeProvider.System;
        _ownsHttp = false;
        _tokenHttp = httpClientFactory.CreateClient(TokenClientName);
        _apiHttp = httpClientFactory.CreateClient(ApiClientName);
        _sseHttp = httpClientFactory.CreateClient(SseClientName);
    }

    public PacApiClient(string baseUrl, string apiKey, IAppLogger logger, string headerName = "X-Api-Key")
        : this(
            baseUrl,
            apiKey,
            logger,
            headerName,
            new HttpClientHandler(),
            new HttpClientHandler(),
            new HttpClientHandler(),
            TimeProvider.System)
    {
    }

    internal PacApiClient(
        string baseUrl,
        string apiKey,
        IAppLogger logger,
        string headerName,
        HttpMessageHandler tokenHandler,
        HttpMessageHandler apiInner,
        HttpMessageHandler sseInner,
        TimeProvider? timeProvider = null)
    {
        _baseUrl = (baseUrl ?? string.Empty).Trim().TrimEnd('/');
        _apiKey = (apiKey ?? string.Empty).Trim();
        _headerName = string.IsNullOrWhiteSpace(headerName) ? "X-Api-Key" : headerName.Trim();
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _time = timeProvider ?? TimeProvider.System;
        ArgumentNullException.ThrowIfNull(tokenHandler);
        ArgumentNullException.ThrowIfNull(apiInner);
        ArgumentNullException.ThrowIfNull(sseInner);

        _ownsHttp = true;
        _tokenHttp = new HttpClient(tokenHandler) { Timeout = ApiTimeout };
        _apiHttp = new HttpClient(new NestedJwtHandler(this) { InnerHandler = apiInner })
        {
            Timeout = ApiTimeout,
        };
        _sseHttp = new HttpClient(new NestedJwtHandler(this) { InnerHandler = sseInner })
        {
            Timeout = Timeout.InfiniteTimeSpan,
        };
    }

    public bool IsConfigured
        => !string.IsNullOrWhiteSpace(_baseUrl) && !string.IsNullOrWhiteSpace(_apiKey);

    internal string? CurrentAccessToken => Volatile.Read(ref _token)?.AccessToken;

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

    /// <summary>发送并反序列化 JSON；出站失败包成 <see cref="PacApiException"/></summary>
    public Task<T?> GetJsonAsync<T>(
        Func<HttpRequestMessage> createRequest,
        JsonTypeInfo<T> typeInfo,
        CancellationToken ct)
        => SendJsonAsync(HttpMethod.Get, createRequest, typeInfo, ct, commandId: null);

    /// <summary>POST JSON；附带 <see cref="PacApiHeaders.CommandId"/></summary>
    public Task<T?> PostJsonAsync<T>(
        Func<HttpRequestMessage> createRequest,
        JsonTypeInfo<T> typeInfo,
        CancellationToken ct,
        Guid? commandId = null)
        => SendJsonAsync(HttpMethod.Post, createRequest, typeInfo, ct, commandId ?? Guid.NewGuid());

    /// <summary>PUT JSON；附带 CommandId</summary>
    public Task<T?> PutJsonAsync<T>(
        Func<HttpRequestMessage> createRequest,
        JsonTypeInfo<T> typeInfo,
        CancellationToken ct,
        Guid? commandId = null)
        => SendJsonAsync(HttpMethod.Put, createRequest, typeInfo, ct, commandId ?? Guid.NewGuid());

    /// <summary>DELETE；附带 CommandId</summary>
    public Task DeleteAsync(
        Func<HttpRequestMessage> createRequest,
        CancellationToken ct,
        Guid? commandId = null)
        => SendWriteNoContentAsync(HttpMethod.Delete, createRequest, ct, commandId ?? Guid.NewGuid());

    /// <summary>
    /// 设置 <see cref="PacApiHeaders.CommandId"/>
    ///
    /// 显式 commandId 覆盖已有头，发送前幂等键固定；Guid.Empty 拒绝
    /// </summary>
    public static Guid ApplyCommandId(HttpRequestMessage request, Guid? commandId = null)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (commandId == Guid.Empty)
        {
            throw new ArgumentException("CommandId must be non-empty", nameof(commandId));
        }

        if (commandId is null
            && request.Headers.TryGetValues(PacApiHeaders.CommandId, out var values))
        {
            foreach (var value in values)
            {
                if (Guid.TryParse(value, out var existing) && existing != Guid.Empty)
                {
                    return existing;
                }
            }
        }

        var id = commandId ?? Guid.NewGuid();
        request.Headers.Remove(PacApiHeaders.CommandId);
        request.Headers.TryAddWithoutValidation(PacApiHeaders.CommandId, id.ToString("D"));
        return id;
    }

    /// <summary>非 2xx 抛 <see cref="PacApiException"/>（含 code、traceId、可选 currentVersion）</summary>
    public Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken ct = default)
        => EnsureSuccessAsync(response, _time, _logger, ct);

    public static async Task EnsureSuccessAsync(
        HttpResponseMessage response,
        TimeProvider time,
        CancellationToken ct = default)
        => await EnsureSuccessAsync(response, time, logger: null, ct).ConfigureAwait(false);

    public static async Task EnsureSuccessAsync(
        HttpResponseMessage response,
        TimeProvider time,
        IAppLogger? logger,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(response);
        ArgumentNullException.ThrowIfNull(time);
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        throw await CreateExceptionAsync(response, time, logger, ct).ConfigureAwait(false);
    }

    public static Task<PacApiException> CreateExceptionAsync(
        HttpResponseMessage response,
        TimeProvider time,
        CancellationToken ct = default)
        => CreateExceptionAsync(response, time, logger: null, ct);

    public static async Task<PacApiException> CreateExceptionAsync(
        HttpResponseMessage response,
        TimeProvider time,
        IAppLogger? logger,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(response);
        ArgumentNullException.ThrowIfNull(time);
        // HTTP 状态行是权威；正文 status 只作诊断，不覆盖
        var status = (int)response.StatusCode;
        string? title = response.ReasonPhrase;
        string? detail = null;
        string? code = null;
        string? traceId = null;
        long? currentVersion = null;
        TimeSpan? retryAfter = null;

        if (response.Headers.RetryAfter?.Delta is { } delta)
        {
            retryAfter = delta;
        }
        else if (response.Headers.RetryAfter?.Date is { } date)
        {
            var left = date - time.GetUtcNow();
            if (left > TimeSpan.Zero)
            {
                retryAfter = left;
            }
        }

        try
        {
            var text = await ReadResponseTextLimitedAsync(response, ct).ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(text))
            {
                using var doc = JsonDocument.Parse(text);
                var root = doc.RootElement;
                if (root.ValueKind == JsonValueKind.Object)
                {
                    title = ReadString(root, "title") ?? title;
                    detail = ReadString(root, "detail");
                    code = ReadString(root, "code");
                    traceId = ReadString(root, "traceId");
                    currentVersion = ReadInt64(root, "currentVersion");

                    if (root.TryGetProperty("status", out var statusEl)
                        && statusEl.TryGetInt32(out var bodyStatus)
                        && bodyStatus > 0
                        && bodyStatus != status)
                    {
                        logger?.Warn(
                            "PacApi",
                            "problem.status_mismatch",
                            $"HTTP {status} differs from problem.status {bodyStatus}");
                    }
                }
                else
                {
                    detail = text;
                }
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (PacApiException)
        {
            throw;
        }
        catch (Exception)
        {
            // 仅容忍响应正文解析失败；调用方取消与体积超限已在上方重抛
        }

        var problem = new PacApiProblem(status, code, title, detail, traceId, retryAfter, currentVersion);
        return status == 409
            ? new PacApiConflictException(problem)
            : new PacApiException(problem);
    }

    private static string? ReadString(JsonElement root, string name)
        => root.TryGetProperty(name, out var el) && el.ValueKind == JsonValueKind.String
            ? el.GetString()
            : null;

    private static long? ReadInt64(JsonElement root, string name)
        => root.TryGetProperty(name, out var el)
           && el.ValueKind == JsonValueKind.Number
           && el.TryGetInt64(out var n)
            ? n
            : null;

    public void Dispose()
    {
        if (_ownsHttp)
        {
            _tokenHttp.Dispose();
            _apiHttp.Dispose();
            _sseHttp.Dispose();
        }

        _tokenGate.Dispose();
    }

    private async Task<T?> SendJsonAsync<T>(
        HttpMethod method,
        Func<HttpRequestMessage> createRequest,
        JsonTypeInfo<T> typeInfo,
        CancellationToken ct,
        Guid? commandId)
    {
        ArgumentNullException.ThrowIfNull(method);
        ArgumentNullException.ThrowIfNull(createRequest);
        ArgumentNullException.ThrowIfNull(typeInfo);

        try
        {
            using var response = await SendAsync(
                    () => BuildRequest(method, createRequest, commandId),
                    ct)
                .ConfigureAwait(false);
            await EnsureSuccessAsync(response, ct).ConfigureAwait(false);
            if (response.StatusCode == HttpStatusCode.NoContent
                || response.Content.Headers.ContentLength == 0)
            {
                return default;
            }

            return await ReadJsonLimitedAsync(response, typeInfo, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ShouldWrapOutbound(ex, ct))
        {
            throw WrapOutbound(ex, ct);
        }
    }

    private async Task SendWriteNoContentAsync(
        HttpMethod method,
        Func<HttpRequestMessage> createRequest,
        CancellationToken ct,
        Guid commandId)
    {
        ArgumentNullException.ThrowIfNull(method);
        ArgumentNullException.ThrowIfNull(createRequest);

        try
        {
            using var response = await SendAsync(
                    () => BuildRequest(method, createRequest, commandId),
                    ct)
                .ConfigureAwait(false);
            await EnsureSuccessAsync(response, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ShouldWrapOutbound(ex, ct))
        {
            throw WrapOutbound(ex, ct);
        }
    }

    private static HttpRequestMessage BuildRequest(
        HttpMethod method,
        Func<HttpRequestMessage> createRequest,
        Guid? commandId)
    {
        var request = createRequest();
        request.Method = method;
        if (commandId is { } id)
        {
            ApplyCommandId(request, id);
        }

        return request;
    }

    private static async Task<T?> ReadJsonLimitedAsync<T>(
        HttpResponseMessage response,
        JsonTypeInfo<T> typeInfo,
        CancellationToken ct)
    {
        await using var stream = await OpenLimitedContentStreamAsync(response, ct).ConfigureAwait(false);
        return await JsonSerializer.DeserializeAsync(stream, typeInfo, ct).ConfigureAwait(false);
    }

    private static async Task<string> ReadResponseTextLimitedAsync(
        HttpResponseMessage response,
        CancellationToken ct)
    {
        await using var stream = await OpenLimitedContentStreamAsync(response, ct).ConfigureAwait(false);
        using var reader = new StreamReader(stream);
        return await reader.ReadToEndAsync(ct).ConfigureAwait(false);
    }

    private static async Task<Stream> OpenLimitedContentStreamAsync(
        HttpResponseMessage response,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(response);
        if (response.Content.Headers.ContentLength is { } length && length > MaxResponseBytes)
        {
            throw CreateResponseTooLargeException(length);
        }

        var source = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        return new LimitedReadStream(source, MaxResponseBytes);
    }

    private static PacApiException CreateResponseTooLargeException(long? observedLength)
        => new(
            new PacApiProblem(
                Status: 502,
                Code: "response_too_large",
                Title: observedLength is { } n
                    ? $"API response exceeds {MaxResponseBytes} bytes (Content-Length {n})"
                    : $"API response exceeds {MaxResponseBytes} bytes",
                Detail: null,
                TraceId: PacTrace.CurrentTraceId,
                RetryAfter: null));

    private async Task<HttpResponseMessage> SendOnAsync(
        HttpClient http,
        Func<HttpRequestMessage> createRequest,
        CancellationToken ct)
    {
        try
        {
            using var request = createRequest();
            var method = request.Method;
            var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct)
                .ConfigureAwait(false);

            if (response.StatusCode != HttpStatusCode.Unauthorized)
            {
                return response;
            }

            // 401 且 Bearer 对应当前缓存票时清票；写命令不重放，GET/HEAD 换票后重放
            var staleAccessToken = request.Headers.Authorization?.Parameter;
            InvalidateAccessTokenIfCurrent(staleAccessToken);

            if (!AllowsUnauthorizedRetry(method))
            {
                return response;
            }

            response.Dispose();
            using var retry = createRequest();
            retry.Options.Set(ForceTokenRefreshKey, true);
            if (!string.IsNullOrEmpty(staleAccessToken))
            {
                retry.Options.Set(StaleAccessTokenKey, staleAccessToken);
            }

            return await http.SendAsync(retry, HttpCompletionOption.ResponseHeadersRead, ct)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ShouldWrapOutbound(ex, ct))
        {
            // 断网等不要以裸 HttpRequestException 离开 PacApiClient
            throw WrapOutbound(ex, ct);
        }
    }

    /// <summary>GET/HEAD 在 401 后换票重放；写命令不重放</summary>
    internal static bool AllowsUnauthorizedRetry(HttpMethod method)
        => HttpMethod.Get.Equals(method) || HttpMethod.Head.Equals(method);

    internal static bool ShouldWrapOutbound(Exception ex, CancellationToken ct)
        => ShouldWrapOutbound(ex, ct.IsCancellationRequested);

    private static bool ShouldWrapOutbound(Exception ex, bool cancellationRequested)
    {
        if (ex is PacApiException)
        {
            return false;
        }

        // 调用方取消原样抛出；超时（含无 TimeoutException 内层的 TaskCanceled）要包装
        if (ex is OperationCanceledException)
        {
            return !cancellationRequested;
        }

        if (ex is HttpRequestException or IOException or System.Net.Sockets.SocketException)
        {
            return true;
        }

        if (ex is TimeoutException)
        {
            return true;
        }

        var fullName = ex.GetType().FullName;
        return fullName is not null
               && (fullName.Contains("TimeoutRejectedException", StringComparison.Ordinal)
                   || fullName.Contains("BrokenCircuitException", StringComparison.Ordinal));
    }

    internal static PacApiException WrapOutbound(Exception ex, CancellationToken ct)
        => WrapOutbound(ex, ct.IsCancellationRequested);

    private static PacApiException WrapOutbound(Exception ex, bool cancellationRequested)
    {
        var isTimeout = !cancellationRequested
                        && (ex is TimeoutException
                            || ex is OperationCanceledException
                            || LooksLikeTimeout(ex));
        if (isTimeout)
        {
            return new PacApiException(
                new PacApiProblem(
                    Status: 504,
                    Code: "timeout",
                    Title: string.IsNullOrWhiteSpace(ex.Message) ? "API request timed out" : ex.Message,
                    Detail: null,
                    TraceId: PacTrace.CurrentTraceId,
                    RetryAfter: null),
                ex);
        }

        return new PacApiException(
            new PacApiProblem(
                Status: 503,
                Code: "transport",
                Title: string.IsNullOrWhiteSpace(ex.Message) ? "API unreachable" : ex.Message,
                Detail: null,
                TraceId: PacTrace.CurrentTraceId,
                RetryAfter: null),
            ex);
    }

    private static bool LooksLikeTimeout(Exception ex)
    {
        var fullName = ex.GetType().FullName;
        return fullName is not null
               && fullName.Contains("TimeoutRejectedException", StringComparison.Ordinal);
    }

    internal async Task EnsureTokenAsync(
        CancellationToken ct,
        bool forceRefresh = false,
        string? staleAccessToken = null)
    {
        if (!forceRefresh && HasFreshAccessToken())
        {
            return;
        }

        await _tokenGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var held = ReadFreshToken();
            if (held is not null)
            {
                // 非强制；或强制但当前票已不是触发 401 的那张（并发已换过）
                if (!forceRefresh
                    || (!string.IsNullOrEmpty(staleAccessToken)
                        && !string.Equals(held.AccessToken, staleAccessToken, StringComparison.Ordinal)))
                {
                    return;
                }
            }

            if (!IsConfigured)
            {
                throw new InvalidOperationException("Pac API baseUrl/apiKey is not configured");
            }

            using var activity = PacActivities.Desktop.StartActivity("pacapi.token");
            using var request = new HttpRequestMessage(HttpMethod.Post, Resolve("/v1/auth/token"));
            request.Headers.TryAddWithoutValidation(_headerName, _apiKey);

            try
            {
                // 发送与读正文都包一层；正文中途断流也要变成 PacApiException
                using var response = await _tokenHttp.SendAsync(request, ct).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                {
                    _logger.Warn(
                        "PacApi",
                        "token.fail",
                        $"Token exchange failed with HTTP {(int)response.StatusCode}");
                    await EnsureSuccessAsync(response, _time, _logger, ct).ConfigureAwait(false);
                }

                var body = await ReadJsonLimitedAsync(
                        response,
                        PacJsonContext.Default.PacApiTokenResponse,
                        ct)
                    .ConfigureAwait(false)
                    ?? throw new InvalidOperationException("empty token response");

                if (string.IsNullOrWhiteSpace(body.AccessToken))
                {
                    throw new InvalidOperationException("token response accessToken is empty");
                }

                if (string.IsNullOrWhiteSpace(body.TokenType)
                    || !string.Equals(body.TokenType, "Bearer", StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException("token response tokenType must be Bearer");
                }

                if (body.ExpiresIn <= 0)
                {
                    throw new InvalidOperationException("token response expiresIn must be > 0");
                }

                var issuedAt = _time.GetUtcNow();
                Volatile.Write(
                    ref _token,
                    new TokenState(
                        body.AccessToken,
                        issuedAt.AddSeconds(body.ExpiresIn),
                        ComputeRefreshAt(issuedAt, body.ExpiresIn)));
                _logger.Info("PacApi", "token.ok", "API access token refreshed");
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex) when (ShouldWrapOutbound(ex, ct))
            {
                throw WrapOutbound(ex, ct);
            }
        }
        finally
        {
            _tokenGate.Release();
        }
    }

    private bool HasFreshAccessToken()
        => ReadFreshToken() is not null;

    private TokenState? ReadFreshToken()
    {
        var token = Volatile.Read(ref _token);
        if (token is null || string.IsNullOrEmpty(token.AccessToken))
        {
            return null;
        }

        return token.RefreshAt > _time.GetUtcNow() ? token : null;
    }

    /// <summary>Bearer 对应当前缓存票时清掉；换票失败后缓存为空</summary>
    private void InvalidateAccessTokenIfCurrent(string? accessToken)
    {
        if (string.IsNullOrEmpty(accessToken))
        {
            return;
        }

        var current = Volatile.Read(ref _token);
        if (current is null
            || !string.Equals(current.AccessToken, accessToken, StringComparison.Ordinal))
        {
            return;
        }

        _ = Interlocked.CompareExchange(ref _token, null, current);
    }

    /// <summary>按 expiresIn 算下次换票点；短票按比例，长票提前量封顶 1 分钟</summary>
    internal static DateTimeOffset ComputeRefreshAt(DateTimeOffset issuedAt, int expiresInSeconds)
    {
        var lifetime = TimeSpan.FromSeconds(expiresInSeconds);
        var earlyTicks = Math.Min(lifetime.Ticks / 10, MaxRefreshEarly.Ticks);
        return issuedAt + lifetime - TimeSpan.FromTicks(earlyTicks);
    }

    // 单测内嵌 Jwt；App DI 用 PacApiJwtHandler
    private sealed class NestedJwtHandler : DelegatingHandler
    {
        private readonly PacApiClient _client;

        public NestedJwtHandler(PacApiClient client)
        {
            _client = client;
        }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var force = request.Options.TryGetValue(ForceTokenRefreshKey, out var refresh) && refresh;
            _ = request.Options.TryGetValue(StaleAccessTokenKey, out string? stale);
            await _client.EnsureTokenAsync(cancellationToken, forceRefresh: force, staleAccessToken: stale)
                .ConfigureAwait(false);
            var accessToken = Volatile.Read(ref _client._token)?.AccessToken;
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            // 内嵌 Handler 无 System.Net.Http 诊断层；自建客户端 span 再注入
            using var activity = PacActivities.Desktop.StartActivity("pacapi.http", ActivityKind.Client);
            PacTrace.Inject(request, activity);
            return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>读正文时截断超限，避免无 Content-Length 时吃满内存</summary>
    private sealed class LimitedReadStream : Stream
    {
        private readonly Stream _inner;
        private readonly long _maxBytes;
        private long _read;

        public LimitedReadStream(Stream inner, long maxBytes)
        {
            _inner = inner ?? throw new ArgumentNullException(nameof(inner));
            _maxBytes = maxBytes;
        }

        public override bool CanRead => _inner.CanRead;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush() => _inner.Flush();

        public override int Read(byte[] buffer, int offset, int count)
        {
            var n = _inner.Read(buffer, offset, count);
            Track(n);
            return n;
        }

        public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            var n = await _inner.ReadAsync(buffer.AsMemory(offset, count), cancellationToken).ConfigureAwait(false);
            Track(n);
            return n;
        }

        public override async ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            var n = await _inner.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            Track(n);
            return n;
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _inner.Dispose();
            }

            base.Dispose(disposing);
        }

        public override async ValueTask DisposeAsync()
        {
            await _inner.DisposeAsync().ConfigureAwait(false);
            await base.DisposeAsync().ConfigureAwait(false);
        }

        private void Track(int n)
        {
            if (n <= 0)
            {
                return;
            }

            _read += n;
            if (_read > _maxBytes)
            {
                throw CreateResponseTooLargeException(observedLength: null);
            }
        }
    }
}
