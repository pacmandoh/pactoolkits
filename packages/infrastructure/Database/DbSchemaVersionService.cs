using Npgsql;
using PacToolkits.Application.Abstractions;
using PacToolkits.Application.DTOs;

namespace PacToolkits.Infrastructure.Database;

/// <summary>
/// 当前 Schema 版本只读查询
///
/// 负责：读取 <c>schema_version</c> 并区分元数据缺失与其它失败
/// 不执行迁移
/// </summary>
public sealed class DbSchemaVersionService : IDbSchemaVersionService
{
    private readonly IDbConfigService _dbConfig;
    private readonly IAppLogger _logger;

    public DbSchemaVersionService(IDbConfigService dbConfig, IAppLogger logger)
    {
        _dbConfig = dbConfig ?? throw new ArgumentNullException(nameof(dbConfig));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public Task<DbSchemaVersionRead> TryReadSchemaVersionAsync(CancellationToken ct)
        => TryReadSchemaVersionAsync(_dbConfig.Current, ct);

    public async Task<DbSchemaVersionRead> TryReadSchemaVersionAsync(PgOptions options, CancellationToken ct)
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
                return new DbSchemaVersionRead(
                    false,
                    null,
                    "数据库缺少 schema_version 当前值",
                    IsMetadataMissing: true);
            }

            return new DbSchemaVersionRead(true, version, null);
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UndefinedTable)
        {
            return new DbSchemaVersionRead(
                false,
                null,
                "schema_version 表不存在",
                IsMetadataMissing: true);
        }
        catch (Exception ex)
        {
            _logger.Warn("DbSchemaVersion", "schema_version.read_fail", "Failed reading schema_version", ex);
            return new DbSchemaVersionRead(false, null, ex.Message);
        }
    }

}
