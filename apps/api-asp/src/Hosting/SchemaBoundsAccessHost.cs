using PacToolkits.Application.Abstractions;

namespace PacToolkits.Api.Hosting;

/// <summary>
/// 周期对齐 SchemaBounds 与 <see cref="IDbAccessGuard"/>（以 <see cref="IApiHealth"/> 判定为据）
///
/// 默认阻断数据面至首检通过；库不可达或 schema 不合保持 503
/// </summary>
public sealed class SchemaBoundsAccessHost : BackgroundService
{
    public const string NotReadyReason = "schema_bounds:not_ready";

    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(15);

    private readonly IApiHealth _health;
    private readonly IDbAccessGuard _guard;
    private readonly ILogger<SchemaBoundsAccessHost> _logger;

    public SchemaBoundsAccessHost(
        IApiHealth health,
        IDbAccessGuard guard,
        ILogger<SchemaBoundsAccessHost> logger)
    {
        _health = health ?? throw new ArgumentNullException(nameof(health));
        _guard = guard ?? throw new ArgumentNullException(nameof(guard));
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
                await Task.Delay(Interval, stoppingToken).ConfigureAwait(false);
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

        // 库不可达等非 schema 失败：数据面继续 503（与 /health 一致，不靠 Clear 放行）
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
