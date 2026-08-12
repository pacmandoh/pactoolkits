using PacToolkits.Application.Abstractions;

namespace PacToolkits.Api.Hosting;

/// <summary>
/// 按周期用 <see cref="IApiHealth"/> 结果同步 SchemaBounds 与 <see cref="IDbAccessGuard"/>
///
/// 首检通过前默认拦住业务库访问；库不可达或 schema 不合继续 503
/// 阻断时 2s 再探，避免库已恢复而门禁还停在健康间隔
/// </summary>
public sealed class SchemaBoundsAccessHost : BackgroundService
{
    public const string NotReadyReason = "schema_bounds:not_ready";

    private static readonly TimeSpan HealthyInterval = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan BlockedInterval = TimeSpan.FromSeconds(2);

    private readonly IApiHealth _health;
    private readonly IDbAccessGuard _guard;
    private readonly TimeProvider _time;
    private readonly ILogger<SchemaBoundsAccessHost> _logger;

    public SchemaBoundsAccessHost(
        IApiHealth health,
        IDbAccessGuard guard,
        TimeProvider timeProvider,
        ILogger<SchemaBoundsAccessHost> logger)
    {
        _health = health ?? throw new ArgumentNullException(nameof(health));
        _guard = guard ?? throw new ArgumentNullException(nameof(guard));
        _time = timeProvider ?? TimeProvider.System;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public override async Task StartAsync(CancellationToken cancellationToken)
    {
        // 宿主接受请求前先完成首检，避免 Kestrel 已监听而 gate 仍未就绪
        await RefreshAsync(cancellationToken).ConfigureAwait(false);
        await base.StartAsync(cancellationToken).ConfigureAwait(false);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(
                    _guard.IsBlocked ? BlockedInterval : HealthyInterval,
                    _time,
                    stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            await RefreshAsync(stoppingToken).ConfigureAwait(false);
        }
    }

    private async Task RefreshAsync(CancellationToken ct)
    {
        try
        {
            var snap = await _health.CheckAsync(ct).ConfigureAwait(false);
            Apply(snap);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // 关停时不再改写 gate
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "schema_bounds.access refresh failed");
            SetBlocked("schema_bounds:check_failed");
        }
    }

    private void Apply(ApiHealthSnapshot snap)
    {
        if (snap.Ok)
        {
            if (_guard.IsBlocked)
            {
                _guard.Clear();
                _logger.LogInformation("schema_bounds.access cleared");
            }

            return;
        }

        if (snap.Database == "ok"
            && snap.Schema is "incompatible" or "metadata_missing" or "unavailable")
        {
            var version = string.IsNullOrWhiteSpace(snap.SchemaVersion) ? "-" : snap.SchemaVersion;
            SetBlocked($"schema_bounds:{snap.Schema} version={version}");
            return;
        }

        // 库不可达等非 schema 失败：业务路由继续 503（与 /health 一致，不要 Clear 放行）
        SetBlocked("schema_bounds:db_unavailable");
    }

    private void SetBlocked(string reason)
    {
        if (_guard.IsBlocked && string.Equals(_guard.BlockReason, reason, StringComparison.Ordinal))
        {
            return;
        }

        _guard.Block(reason);
        _logger.LogWarning("schema_bounds.access blocked {Reason}", reason);
    }
}
