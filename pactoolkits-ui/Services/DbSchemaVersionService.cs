using System;
using System.Threading;
using System.Threading.Tasks;
using Npgsql;

namespace pactoolkits_ui.Services;

public interface IDbSchemaVersionService
{
    Task<(bool ok, string? value, string? reason)> TryReadSchemaVersionAsync(CancellationToken ct);
}

public sealed class DbSchemaVersionService : IDbSchemaVersionService
{
    private readonly IDbConfigService _dbConfig;
    private readonly IAppLogger _logger;

    public DbSchemaVersionService(IDbConfigService dbConfig, IAppLogger logger)
    {
        _dbConfig = dbConfig ?? throw new ArgumentNullException(nameof(dbConfig));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<(bool ok, string? value, string? reason)> TryReadSchemaVersionAsync(CancellationToken ct)
    {
        try
        {
            var opt = _dbConfig.Current;
            var csb = new NpgsqlConnectionStringBuilder
            {
                Host = opt.Host,
                Port = opt.Port,
                Database = opt.Database,
                Username = opt.Username,
                Password = opt.Password,
                Timeout = opt.ConnectTimeoutSeconds,
                KeepAlive = opt.KeepAliveSeconds
            };

            await using var conn = new NpgsqlConnection(csb.ToString());
            await conn.OpenAsync(ct).ConfigureAwait(false);

            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "select schema_version from schema_version where singleton = true";
            var result = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
            var version = result?.ToString()?.Trim();

            if (string.IsNullOrWhiteSpace(version))
                return (false, null, "数据库缺少 schema_version 当前值");

            return (true, version, null);
        }
        catch (Exception ex)
        {
            _logger.Warn("DbSchemaVersion", "schema_version.read_fail", "Failed reading schema_version", ex);
            return (false, null, ex.Message);
        }
    }
}
