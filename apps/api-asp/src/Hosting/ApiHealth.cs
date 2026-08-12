using Microsoft.Extensions.Options;
using PacToolkits.Application.Abstractions;
using PacToolkits.Core;

namespace PacToolkits.Api.Hosting;

/// <summary>进程、PostgreSQL 与 schema 区间校验的健康快照</summary>
public sealed record ApiHealthSnapshot(
    bool Ok,
    string Database,
    string Schema,
    string? SchemaVersion);

/// <summary>业务可用性健康检查（响应不含连接串与账号）</summary>
public interface IApiHealth
{
    Task<ApiHealthSnapshot> CheckAsync(CancellationToken ct = default);
}

/// <summary>
/// 连库探测并 Match SchemaBounds
///
/// 短缓存与 single-flight，避免匿名 /health 压库
/// </summary>
public sealed class ApiHealth : IApiHealth
{
    // 成功快照短缓存，避免匿名 /health 被连续请求；失败不缓存，否则库恢复会被 TTL 挡住
    private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(1);

    private readonly IDbConfigService _db;
    private readonly IDbSchemaGate _schemaGate;
    private readonly SchemaBoundsOptions _bounds;
    private readonly TimeProvider _time;
    private readonly SemaphoreSlim _probeGate = new(1, 1);
    private readonly object _cacheGate = new();

    private ApiHealthSnapshot? _cache;
    private DateTimeOffset _cacheUtc;

    public ApiHealth(
        IDbConfigService db,
        IDbSchemaGate schemaGate,
        IOptions<SchemaBoundsOptions> bounds,
        TimeProvider timeProvider)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _schemaGate = schemaGate ?? throw new ArgumentNullException(nameof(schemaGate));
        _bounds = bounds?.Value ?? throw new ArgumentNullException(nameof(bounds));
        _time = timeProvider ?? TimeProvider.System;
    }

    public async Task<ApiHealthSnapshot> CheckAsync(CancellationToken ct = default)
    {
        var hit = TryGetFreshCache();
        if (hit is not null)
        {
            return hit;
        }

        await _probeGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            hit = TryGetFreshCache();
            if (hit is not null)
            {
                return hit;
            }

            var snap = await ProbeAsync(ct).ConfigureAwait(false);
            lock (_cacheGate)
            {
                if (snap.Ok)
                {
                    _cache = snap;
                    _cacheUtc = _time.GetUtcNow();
                }
                else
                {
                    _cache = null;
                }
            }

            return snap;
        }
        finally
        {
            _probeGate.Release();
        }
    }

    private ApiHealthSnapshot? TryGetFreshCache()
    {
        lock (_cacheGate)
        {
            var cached = _cache;
            if (cached is not null && _time.GetUtcNow() - _cacheUtc < CacheTtl)
            {
                return cached;
            }
        }

        return null;
    }

    private async Task<ApiHealthSnapshot> ProbeAsync(CancellationToken ct)
    {
        // 诊断探库要快失败；勿用连接池 ConnectTimeoutSeconds（默认 6s）把 /status 拖死
        using var ping = CancellationTokenSource.CreateLinkedTokenSource(ct);
        ping.CancelAfter(ProbeTimeout);
        bool reachable;
        try
        {
            reachable = await _db.TestConnectionAsync(_db.Current, ping.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            reachable = false;
        }
        if (!reachable)
        {
            return new ApiHealthSnapshot(Ok: false, Database: "unavailable", Schema: "skipped", SchemaVersion: null);
        }

        var read = await _schemaGate.ReadAsync(ct).ConfigureAwait(false);
        var match = _schemaGate.Match(read, _bounds.MinDbSchema, _bounds.MaxDbSchema);
        if (!match.IsCompatible)
        {
            // 读版本失败（Unknown）多半是库抖了一下，不要报成 Database=ok + schema unavailable
            if (match.Status == DbSchemaCompatibility.Unknown)
            {
                return new ApiHealthSnapshot(
                    Ok: false,
                    Database: "unavailable",
                    Schema: "skipped",
                    SchemaVersion: null);
            }

            var schemaState = match.Status switch
            {
                DbSchemaCompatibility.MetadataMissing => "metadata_missing",
                DbSchemaCompatibility.BelowMinimum or DbSchemaCompatibility.AboveMaximum => "incompatible",
                _ => "unavailable",
            };
            return new ApiHealthSnapshot(
                Ok: false,
                Database: "ok",
                Schema: schemaState,
                SchemaVersion: string.IsNullOrWhiteSpace(match.CurrentVersion) ? null : match.CurrentVersion);
        }

        return new ApiHealthSnapshot(
            Ok: true,
            Database: "ok",
            Schema: "ok",
            SchemaVersion: match.CurrentVersion);
    }
}
