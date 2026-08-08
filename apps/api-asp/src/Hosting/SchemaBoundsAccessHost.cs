using PacToolkits.Application.Abstractions;

namespace PacToolkits.Api.Hosting;

/// <summary>
/// 周期对齐 SchemaBounds 与 <see cref="IDbAccessGuard"/>（以 <see cref="IApiHealth"/> 判定为据）
///
/// /health 与经 IDb 的业务读写共用同一判定；库不可达不设 Block
/// </summary>
public sealed class SchemaBoundsAccessHost : BackgroundService
{
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

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var snap = await _health.CheckAsync(stoppingToken).ConfigureAwait(false);
                Apply(snap);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "schema_bounds.access refresh failed");
            }

            try
            {
                await Task.Delay(Interval, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private void Apply(ApiHealthSnapshot snap)
    {
        // schema 不合才 Block；库不可达走传输错误，避免长期误设 Block
        if (snap.Ok)
        {
            if (_guard.IsBlocked)
            {
                _guard.Clear();
                _logger.LogInformation("schema_bounds.access cleared");
            }

            return;
        }

        if (snap.Database == "ok" && snap.Schema is "incompatible" or "metadata_missing" or "unavailable")
        {
            var version = string.IsNullOrWhiteSpace(snap.SchemaVersion) ? "-" : snap.SchemaVersion;
            var reason = $"schema_bounds:{snap.Schema} version={version}";
            if (!_guard.IsBlocked || !string.Equals(_guard.BlockReason, reason, StringComparison.Ordinal))
            {
                _guard.Block(reason);
                _logger.LogWarning("schema_bounds.access blocked {Reason}", reason);
            }

            return;
        }

        if (_guard.IsBlocked)
        {
            _guard.Clear();
            _logger.LogInformation("schema_bounds.access cleared (db not reachable or non-schema fail)");
        }
    }
}
