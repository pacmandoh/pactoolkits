using System;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using PacToolkits.Application.Abstractions;
using PacToolkits.Application.DTOs;
using PacToolkits.Application.Serialization;
using PacToolkits.Desktop.Avalonia.Services.Infrastructure.Versioning;

namespace PacToolkits.Desktop.Avalonia.Services.Infrastructure.Api;

/// <summary>Shell 侧 PacAPI 探测结果（仅已配置时才有意义）</summary>
public enum ApiAvailabilityState
{
    Connecting,
    Ready,
    Unavailable,
    ContractBlocked,
    ServerDatabaseBlocked,
    SchemaBlocked,
}

/// <summary>最近一次探测结果；FirstCheckCompleted 表示首检已结束（含失败）</summary>
public sealed record ApiAvailabilitySnapshot(
    ApiAvailabilityState State,
    string? Detail,
    DateTimeOffset CheckedAt,
    bool FirstCheckCompleted);

/// <summary>周期 / 按需探测 /v1/system/info 与 /v1/system/status，供 Shell 连接态与横幅</summary>
public interface IApiAvailabilityService : IDisposable
{
    ApiAvailabilitySnapshot Current { get; }

    /// <summary>本机是否已配置 BaseUrl+ApiKey；未配置时不探测、不谈 API 状态</summary>
    bool IsConfigured { get; }

    /// <summary>最近一次 info 的 apiVersion；Ready 稳态只探 status 时沿用</summary>
    string? LastApiVersion { get; }

    /// <summary>最近一次 info 的 contractVersion；Ready 稳态只探 status 时沿用</summary>
    string? LastContractVersion { get; }

    /// <summary>最近一次 status 的 database；未探到则为 null</summary>
    string? LastDatabase { get; }

    /// <summary>最近一次 status 的 schema；未探到则为 null</summary>
    string? LastSchema { get; }

    /// <summary>最近一次 status 的 schemaVersion；未探到则为 null</summary>
    string? LastSchemaVersion { get; }

    event Action? Changed;

    void Start();

    Task ProbeAsync(CancellationToken ct = default);

    /// <summary>配置变更后丢掉旧探测结果，等新配置首检</summary>
    void Reset();
}

/// <summary>
/// 聚合 PacAPI 系统端点为 Shell 可用性快照
///
/// 未配置时不发 HTTP、不更新探测态；协议区间与 PacApiContractGate 同口径
/// </summary>
public sealed class ApiAvailabilityService : IApiAvailabilityService
{
    // HTTP 没有活连接掉线事件：可达时 1s 探 status；不可达时 2s 全量再探
    // 401/403：挂起自动探测，只盯 ConfigEpoch；错密钥继续请求只会白耗换票限流
    private static readonly TimeSpan ReadyPollInterval = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan DegradedPollInterval = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan UnconfiguredPollInterval = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan EpochWatchInterval = TimeSpan.FromSeconds(2);
    // info + status 各最多 AvailabilityAttemptTimeout，外加换票与余量
    private static readonly TimeSpan ProbeBudget = TimeSpan.FromSeconds(6);

    private readonly PacApiClient _api;
    private readonly IReleaseVersionService _versions;
    private readonly IAppLogger _logger;
    private readonly TimeProvider _time;
    private readonly SemaphoreSlim _probeGate = new(1, 1);
    private readonly object _startGate = new();
    private readonly CancellationTokenSource _cts = new();

    private ApiAvailabilitySnapshot _current = new(
        ApiAvailabilityState.Connecting,
        Detail: null,
        CheckedAt: DateTimeOffset.MinValue,
        FirstCheckCompleted: false);

    // 与 PacApiClient.ConfigEpoch 对齐；密钥被拒后挂起自动探测，等保存配置后再探
    private int _authFailEpoch = -1;
    // 换票 429：按 Retry-After（缺省 60s）暂停探测；保存配置抬 ConfigEpoch 后解除
    private DateTimeOffset _rateLimitUntil = DateTimeOffset.MinValue;
    private int _rateLimitEpoch = -1;
    // 换配置后只收当前代探测，免得旧请求回写状态、诊断和退避
    private int _probeGeneration;

    private bool _started;

    public event Action? Changed;

    public ApiAvailabilitySnapshot Current
    {
        get
        {
            lock (_startGate)
            {
                return _current;
            }
        }
    }

    public bool IsConfigured => _api.IsConfigured;

    public string? LastApiVersion { get; private set; }

    public string? LastContractVersion { get; private set; }

    public string? LastDatabase { get; private set; }

    public string? LastSchema { get; private set; }

    public string? LastSchemaVersion { get; private set; }

    public ApiAvailabilityService(
        PacApiClient api,
        IReleaseVersionService versions,
        IAppLogger logger,
        TimeProvider timeProvider)
    {
        _api = api ?? throw new ArgumentNullException(nameof(api));
        _versions = versions ?? throw new ArgumentNullException(nameof(versions));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _time = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    }

    public void Start()
    {
        lock (_startGate)
        {
            if (_started)
            {
                return;
            }

            _started = true;
            _ = Task.Run(() => RunLoopAsync(_cts.Token));
        }
    }

    public async Task ProbeAsync(CancellationToken ct = default)
    {
        if (!_api.IsConfigured)
        {
            _authFailEpoch = -1;
            PublishIdle();
            return;
        }

        if (_authFailEpoch >= 0 && _authFailEpoch != _api.ConfigEpoch)
        {
            _authFailEpoch = -1;
        }

        if (_rateLimitEpoch >= 0 && _rateLimitEpoch != _api.ConfigEpoch)
        {
            ClearRateLimitHold();
        }

        var configEpoch = _api.ConfigEpoch;
        var probeGeneration = Volatile.Read(ref _probeGeneration);
        await _probeGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (!IsCurrentProbe(configEpoch, probeGeneration))
            {
                return;
            }

            using var budget = CancellationTokenSource.CreateLinkedTokenSource(ct);
            budget.CancelAfter(ProbeBudget);
            var snap = await CheckOnceAsync(configEpoch, probeGeneration, budget.Token).ConfigureAwait(false);
            if (!IsCurrentProbe(configEpoch, probeGeneration))
            {
                return;
            }

            if (snap.State is ApiAvailabilityState.Ready)
            {
                _authFailEpoch = -1;
                ClearRateLimitHold();
            }

            Publish(snap);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            if (!IsCurrentProbe(configEpoch, probeGeneration))
            {
                return;
            }

            // 预算耗尽：刚才还连着 PacAPI 时不要标成进程不可达；schema/协议阻断保持原态
            var prev = Current;
            var state = TimeoutState(prev.State);
            Publish(new ApiAvailabilitySnapshot(
                state,
                Detail: state == prev.State && !string.IsNullOrWhiteSpace(prev.Detail)
                    ? prev.Detail
                    : TimeoutDetail(state),
                CheckedAt: _time.GetUtcNow(),
                FirstCheckCompleted: true));
        }
        finally
        {
            _probeGate.Release();
        }
    }

    public void Reset()
    {
        Interlocked.Increment(ref _probeGeneration);
        _authFailEpoch = -1;
        ClearRateLimitHold();
        LastApiVersion = null;
        LastContractVersion = null;
        LastDatabase = null;
        LastSchema = null;
        LastSchemaVersion = null;
        Publish(new ApiAvailabilitySnapshot(
            ApiAvailabilityState.Connecting,
            Detail: null,
            CheckedAt: _time.GetUtcNow(),
            FirstCheckCompleted: false), force: true);
    }

    public void Dispose()
    {
        try
        {
            _cts.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }

        _cts.Dispose();
        _probeGate.Dispose();
    }

    private async Task RunLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            var startedAt = _time.GetUtcNow();
            try
            {
                if (!_api.IsConfigured)
                {
                    _authFailEpoch = -1;
                    ClearRateLimitHold();
                    PublishIdle();
                }
                else if (IsAuthFailHoldActive())
                {
                    // 密钥已拒：不发 HTTP；保存配置抬 ConfigEpoch 后再探
                    if (_authFailEpoch != _api.ConfigEpoch)
                    {
                        _authFailEpoch = -1;
                        await ProbeAsync(ct).ConfigureAwait(false);
                    }
                }
                else if (IsRateLimitHoldActive())
                {
                    // 换票限流窗口内不请求；到期后再探
                }
                else
                {
                    await ProbeAsync(ct).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                _logger.Warn("PacApi", "availability.probe.fail", "API availability probe failed", ex);
                Publish(new ApiAvailabilitySnapshot(
                    ApiAvailabilityState.Unavailable,
                    Detail: null,
                    CheckedAt: _time.GetUtcNow(),
                    FirstCheckCompleted: true));
            }

            var interval = !_api.IsConfigured
                ? UnconfiguredPollInterval
                : Current.State is ApiAvailabilityState.Ready
                    ? ReadyPollInterval
                    : IsAuthFailHoldActive()
                        ? EpochWatchInterval
                        : IsRateLimitHoldActive()
                            ? RateLimitWatchRemain()
                            : DegradedPollInterval;
            var remain = interval - (_time.GetUtcNow() - startedAt);
            if (remain <= TimeSpan.Zero)
            {
                continue;
            }

            try
            {
                await Task.Delay(remain, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return;
            }
        }
    }

    private async Task<ApiAvailabilitySnapshot> CheckOnceAsync(
        int configEpoch,
        int probeGeneration,
        CancellationToken ct)
    {
        var at = _time.GetUtcNow();
        // 调用方已保证 IsConfigured；未配置不写成探测态

        try
        {
            // Ready 稳态只请求 status；其余态（含库/schema 阻断恢复）先拉 info 再检协议
            var needInfo = Current.State is not ApiAvailabilityState.Ready;
            if (needInfo)
            {
                var info = await _api.GetAvailabilityJsonAsync(
                        () =>
                        {
                            var req = new HttpRequestMessage(HttpMethod.Get, _api.Resolve("/v1/system/info"));
                            req.Options.Set(PacApiClient.SkipContractGateKey, true);
                            return req;
                        },
                        PacJsonContext.Default.PacApiSystemInfo,
                        ct)
                    .ConfigureAwait(false)
                    ?? throw new InvalidOperationException("empty /v1/system/info response");

                if (IsCurrentProbe(configEpoch, probeGeneration))
                {
                    LastApiVersion = info.ApiVersion;
                    LastContractVersion = info.ContractVersion;
                }

                var contractBlock = ClassifyContract(info.ContractVersion);
                if (contractBlock is not null)
                {
                    return new ApiAvailabilitySnapshot(
                        ApiAvailabilityState.ContractBlocked,
                        Detail: contractBlock,
                        CheckedAt: at,
                        FirstCheckCompleted: true);
                }
            }

            var status = await ReadStatusAsync(configEpoch, probeGeneration, ct).ConfigureAwait(false);
            if (status is null)
            {
                return new ApiAvailabilitySnapshot(
                    ApiAvailabilityState.Unavailable,
                    Detail: "PacAPI 服务返回空状态",
                    CheckedAt: at,
                    FirstCheckCompleted: true);
            }

            var state = ClassifyStatus(status);
            return new ApiAvailabilitySnapshot(
                state,
                Detail: state is ApiAvailabilityState.Ready ? null : DescribeServerDatabase(status),
                CheckedAt: at,
                FirstCheckCompleted: true);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.Warn("PacApi", "availability.check.fail", "API availability check failed", ex);
            if (IsCredentialRejected(ex))
            {
                if (IsCurrentProbe(configEpoch, probeGeneration))
                {
                    _authFailEpoch = configEpoch;
                }

                return new ApiAvailabilitySnapshot(
                    ApiAvailabilityState.Unavailable,
                    Detail: DescribeFailure(ex),
                    CheckedAt: at,
                    FirstCheckCompleted: true);
            }

            if (IsRateLimited(ex) && IsCurrentProbe(configEpoch, probeGeneration))
            {
                ArmRateLimitHold(ex, configEpoch);
            }

            // 库/schema 已有稳定诊断时，瞬时 timeout/transport/429 不要盖成「无法连接地址」来回闪
            var prev = Current;
            if (ShouldKeepDegraded(prev.State, ex))
            {
                return new ApiAvailabilitySnapshot(
                    prev.State,
                    Detail: prev.Detail ?? DescribeFailure(ex),
                    CheckedAt: at,
                    FirstCheckCompleted: true);
            }

            return new ApiAvailabilitySnapshot(
                ApiAvailabilityState.Unavailable,
                Detail: DescribeFailure(ex),
                CheckedAt: at,
                FirstCheckCompleted: true);
        }
    }

    private bool IsAuthFailHoldActive()
        => _authFailEpoch >= 0 && _authFailEpoch == _api.ConfigEpoch;

    private bool IsCurrentProbe(int configEpoch, int probeGeneration)
        => configEpoch == _api.ConfigEpoch
           && probeGeneration == Volatile.Read(ref _probeGeneration);

    private bool IsRateLimitHoldActive()
        => _rateLimitEpoch == _api.ConfigEpoch && _time.GetUtcNow() < _rateLimitUntil;

    private TimeSpan RateLimitWatchRemain()
    {
        var remain = _rateLimitUntil - _time.GetUtcNow();
        if (remain <= TimeSpan.Zero)
        {
            return EpochWatchInterval;
        }

        // 夹在 2s～60s，到期立刻再探
        if (remain > TimeSpan.FromSeconds(60))
        {
            return TimeSpan.FromSeconds(60);
        }

        return remain < EpochWatchInterval ? EpochWatchInterval : remain;
    }

    private void ArmRateLimitHold(Exception ex, int configEpoch)
    {
        var wait = TimeSpan.FromSeconds(60);
        if (ex is PacApiException { RetryAfter: { } retry } && retry > TimeSpan.Zero)
        {
            wait = retry > TimeSpan.FromMinutes(2) ? TimeSpan.FromMinutes(2) : retry;
        }

        var until = _time.GetUtcNow() + wait;
        if (until > _rateLimitUntil || _rateLimitEpoch != configEpoch)
        {
            _rateLimitUntil = until;
            _rateLimitEpoch = configEpoch;
        }
    }

    private void ClearRateLimitHold()
    {
        _rateLimitUntil = DateTimeOffset.MinValue;
        _rateLimitEpoch = -1;
    }

    private static bool ShouldKeepDegraded(ApiAvailabilityState prev, Exception ex)
    {
        if (prev is not (ApiAvailabilityState.ServerDatabaseBlocked
            or ApiAvailabilityState.SchemaBlocked
            or ApiAvailabilityState.ContractBlocked))
        {
            return false;
        }

        return IsTransientProbeFailure(ex);
    }

    internal static bool IsTransientProbeFailure(Exception ex)
        => ex is PacApiException pac
            ? pac.IsTransient
            : ex is TimeoutException or OperationCanceledException or HttpRequestException;

    /// <summary>分类失败原因给横幅与设置页；密钥与地址优先，避免裸异常</summary>
    internal static string? DescribeFailure(Exception ex)
    {
        if (ex is PacApiException pac)
        {
            if (pac.Status is 401 or 403
                || string.Equals(pac.Code, "unauthorized", StringComparison.OrdinalIgnoreCase)
                || string.Equals(pac.Code, "forbidden", StringComparison.OrdinalIgnoreCase))
            {
                return "密钥无效或无权限，请前往设置检查";
            }

            if (IsRateLimited(pac))
            {
                return "请求过于频繁，请稍后再试";
            }

            if (string.Equals(pac.Code, "transport", StringComparison.OrdinalIgnoreCase))
            {
                return "无法连接 PacAPI 服务，请检查地址与网络";
            }

            if (string.Equals(pac.Code, "timeout", StringComparison.OrdinalIgnoreCase)
                || pac.Status is 408)
            {
                return "连接 PacAPI 服务超时，请检查地址与网络";
            }
        }

        return null;
    }

    /// <summary>设置「测试连接」等：有分类文案用分类，否则回退</summary>
    internal static string DescribeUserFacing(Exception ex, string? fallback = null)
        => DescribeFailure(ex) ?? fallback ?? "连接失败";

    internal static string DescribeServerDatabase(PacApiSystemStatus status)
    {
        if (!string.Equals(status.Database, "ok", StringComparison.OrdinalIgnoreCase))
        {
            return "PacAPI 服务已连接，但服务端数据库不可用";
        }

        return status.Schema switch
        {
            "incompatible" => "服务端数据库结构不兼容",
            "metadata_missing" => "服务端数据库缺少结构元数据",
            "unavailable" => "PacAPI 服务已连接，但暂时无法读取数据库结构",
            "skipped" => "PacAPI 服务已连接，但服务端数据库不可用",
            _ => "PacAPI 服务已连接，但服务端数据库不可用",
        };
    }

    /// <summary>密钥无效：自动探测挂起直到保存配置</summary>
    internal static bool IsCredentialRejected(Exception ex)
    {
        if (ex is not PacApiException pac)
        {
            return false;
        }

        if (pac.Status is 401 or 403)
        {
            return true;
        }

        return string.Equals(pac.Code, "unauthorized", StringComparison.OrdinalIgnoreCase)
               || string.Equals(pac.Code, "forbidden", StringComparison.OrdinalIgnoreCase);
    }

    internal static bool IsRateLimited(Exception ex)
        => ex is PacApiException pac && IsRateLimited(pac);

    private static bool IsRateLimited(PacApiException pac)
        => pac.Status is 429
           || string.Equals(pac.Code, "rate_limited", StringComparison.OrdinalIgnoreCase);

    private static bool IsSchemaVersionBlocked(string? schema)
        => string.Equals(schema, "incompatible", StringComparison.OrdinalIgnoreCase)
           || string.Equals(schema, "metadata_missing", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// 按 Database / Schema 分类；总 Status=unavailable 时 Database 仍可能是 ok
    ///
    /// schema=unavailable 是读版本失败，不是版本不兼容；先看 Database，避免断库误报 schema Error
    /// </summary>
    internal static ApiAvailabilityState ClassifyStatus(PacApiSystemStatus status)
    {
        if (!string.Equals(status.Database, "ok", StringComparison.OrdinalIgnoreCase))
        {
            return ApiAvailabilityState.ServerDatabaseBlocked;
        }

        if (IsSchemaVersionBlocked(status.Schema))
        {
            return ApiAvailabilityState.SchemaBlocked;
        }

        // 库 ping 过但读 schema 失败 / 其它非 ok：当服务端库问题，勿抬成硬阻断「不兼容」
        if (!string.Equals(status.Schema, "ok", StringComparison.OrdinalIgnoreCase)
            || !string.Equals(status.Status, "ok", StringComparison.OrdinalIgnoreCase))
        {
            return ApiAvailabilityState.ServerDatabaseBlocked;
        }

        return ApiAvailabilityState.Ready;
    }

    private static ApiAvailabilityState TimeoutState(ApiAvailabilityState prev)
        => prev switch
        {
            // Ready 只请求 status；超时多半是 HTTP 卡住，不要写成服务端库不可用
            ApiAvailabilityState.ServerDatabaseBlocked
                => ApiAvailabilityState.ServerDatabaseBlocked,
            ApiAvailabilityState.SchemaBlocked => ApiAvailabilityState.SchemaBlocked,
            ApiAvailabilityState.ContractBlocked => ApiAvailabilityState.ContractBlocked,
            _ => ApiAvailabilityState.Unavailable,
        };

    private static string TimeoutDetail(ApiAvailabilityState prev)
        => prev switch
        {
            ApiAvailabilityState.ServerDatabaseBlocked
                => "PacAPI 服务已连接，但服务端数据库不可用",
            ApiAvailabilityState.SchemaBlocked => "服务端数据库结构不兼容",
            ApiAvailabilityState.ContractBlocked => "与 PacAPI 服务协议版本不兼容，业务功能已阻断",
            _ => "PacAPI 服务探测超时",
        };

    private string? ClassifyContract(string? contractVersion)
    {
        var ver = _versions.Current;
        var min = ver.MinApiContract;
        var max = ver.MaxApiContract;
        if (string.IsNullOrWhiteSpace(min)
            || string.IsNullOrWhiteSpace(max)
            || string.Equals(min, "unknown", StringComparison.OrdinalIgnoreCase)
            || string.Equals(max, "unknown", StringComparison.OrdinalIgnoreCase))
        {
            return "客户端缺少 PacAPI 服务协议版本范围，请更新客户端";
        }

        return PacApiContractGate.ClassifyBlockReason(contractVersion, min, max);
    }

    private async Task<PacApiSystemStatus?> ReadStatusAsync(
        int configEpoch,
        int probeGeneration,
        CancellationToken ct)
    {
        // 503 带诊断正文；勿走 EnsureSuccess，避免丢掉 database/schema
        using var response = await _api.SendAvailabilityAsync(
                () =>
                {
                    var req = new HttpRequestMessage(HttpMethod.Get, _api.Resolve("/v1/system/status"));
                    req.Options.Set(PacApiClient.SkipContractGateKey, true);
                    return req;
                },
                ct)
            .ConfigureAwait(false);

        if (response.StatusCode is not (HttpStatusCode.OK or HttpStatusCode.ServiceUnavailable))
        {
            // 非 200/503：走统一错误映射并抛出，由 CheckOnce 收成 Unavailable
            await PacApiClient.EnsureSuccessAsync(response, _time, _logger, ct).ConfigureAwait(false);
        }

        await using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        var status = await JsonSerializer.DeserializeAsync(
                stream,
                PacJsonContext.Default.PacApiSystemStatus,
                ct)
            .ConfigureAwait(false);
        if (status is not null && IsCurrentProbe(configEpoch, probeGeneration))
        {
            LastDatabase = status.Database;
            LastSchema = status.Schema;
            LastSchemaVersion = status.SchemaVersion;
        }

        return status;
    }

    /// <summary>未配置：回 idle，勿留下「服务不可用」假象；探测枚举对 UI 无意义</summary>
    private void PublishIdle()
        => Publish(new ApiAvailabilitySnapshot(
            ApiAvailabilityState.Connecting,
            Detail: null,
            CheckedAt: _time.GetUtcNow(),
            FirstCheckCompleted: false));

    private void Publish(ApiAvailabilitySnapshot snap, bool force = false)
    {
        ApiAvailabilitySnapshot prev;
        var changed = false;
        lock (_startGate)
        {
            // CheckedAt 每次探测都会变；连接态与横幅只关心 State / Detail / 首检
            prev = _current;
            _current = snap;
            changed = force
                      || prev.State != snap.State
                      || prev.FirstCheckCompleted != snap.FirstCheckCompleted
                      || !string.Equals(prev.Detail, snap.Detail, StringComparison.Ordinal);
        }

        if (!changed)
        {
            return;
        }

        if (prev.State != snap.State)
        {
            _logger.Info("PacApi", "availability.state", "API availability probe state changed", new
            {
                from = prev.State.ToString(),
                to = snap.State.ToString(),
                detail = snap.Detail,
                firstCheckCompleted = snap.FirstCheckCompleted
            });
        }

        Changed?.Invoke();
    }
}
