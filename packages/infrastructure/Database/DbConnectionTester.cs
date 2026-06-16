using Npgsql;
using PacToolkits.Application.Abstractions;
using PacToolkits.Application.DTOs;

namespace PacToolkits.Infrastructure.Database;

public sealed class DbConnectionTester : IDbConnectionTester
{
    private readonly IAppLogger _logger;

    public DbConnectionTester(IAppLogger logger)
    {
        _logger = logger;
    }

    public async Task<DbTestResult> TestAsync(PgOptions opt, CancellationToken ct = default)
    {
        try
        {
            var csb = new NpgsqlConnectionStringBuilder
            {
                Host = opt.Host,
                Port = opt.Port,
                Database = opt.Database,
                Username = opt.Username,
                Password = opt.Password,
                Timeout = 5,
            };

            await using var conn = new NpgsqlConnection(csb.ToString());
            await conn.OpenAsync(ct).ConfigureAwait(false);

            return new DbTestResult(true, "连接成功");
        }
        catch (Exception ex)
        {
            var (reason, _) = DbConnectionDiagnostics.Classify(ex);
            _logger.Warn("DbConnectionTester", "db.test.fail", "Database connection test failed", ex, new
            {
                opt.Host,
                opt.Port,
                opt.Database,
                opt.Username
            });
            return new DbTestResult(false, reason);
        }
    }
}
