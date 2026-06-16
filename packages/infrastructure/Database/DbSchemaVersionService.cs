using Npgsql;
using PacToolkits.Application.Abstractions;
using PacToolkits.Application.DTOs;

namespace PacToolkits.Infrastructure.Database;

public sealed class DbSchemaVersionService : IDbSchemaVersionService
{
    private readonly IDbConfigService _dbConfig;
    private readonly IAppLogger _logger;

    public DbSchemaVersionService(IDbConfigService dbConfig, IAppLogger logger)
    {
        _dbConfig = dbConfig ?? throw new ArgumentNullException(nameof(dbConfig));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public Task<DbSchemaVersionReadResult> TryReadSchemaVersionAsync(CancellationToken ct)
        => TryReadSchemaVersionAsync(_dbConfig.Current, ct);

    public async Task<DbSchemaVersionReadResult> TryReadSchemaVersionAsync(PgOptions options, CancellationToken ct)
    {
        try
        {
            await using var conn = await PgConnectionFactory.OpenAsync(options, ct).ConfigureAwait(false);

            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "select schema_version from public.schema_version where singleton = true";
            var result = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
            var version = result?.ToString()?.Trim();

            if (string.IsNullOrWhiteSpace(version))
            {
                return new DbSchemaVersionReadResult(
                    false,
                    null,
                    "数据库缺少 schema_version 当前值",
                    IsMetadataMissing: true);
            }

            return new DbSchemaVersionReadResult(true, version, null);
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UndefinedTable)
        {
            return new DbSchemaVersionReadResult(
                false,
                null,
                "schema_version 表不存在",
                IsMetadataMissing: true);
        }
        catch (Exception ex)
        {
            _logger.Warn("DbSchemaVersion", "schema_version.read_fail", "Failed reading schema_version", ex);
            return new DbSchemaVersionReadResult(false, null, ex.Message);
        }
    }

}
