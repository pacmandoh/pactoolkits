using Npgsql;
using PacToolkits.Application.Abstractions;

namespace PacToolkits.Infrastructure.Database;

public sealed class ClientIdReadRepo : IClientIdReadRepo
{
    private readonly IAppLogger _logger;
    private readonly IDatabaseAccessGuard _accessGuard;

    public ClientIdReadRepo(IAppLogger logger, IDatabaseAccessGuard accessGuard)
    {
        _logger = logger;
        _accessGuard = accessGuard ?? throw new ArgumentNullException(nameof(accessGuard));
    }

    public async Task<HashSet<string>> GetDistinctClientIdsAsync(PgOptions opt, CancellationToken ct)
    {
        // Uses an explicit connection string for settings-page probes, so it cannot go through IDb.
        _accessGuard.ThrowIfBlocked();

        var cs =
            $"Host={opt.Host};Port={opt.Port};Database={opt.Database};Username={opt.Username};Password={opt.Password};Timeout=6;Command Timeout=6";

        await using var conn = new NpgsqlConnection(cs);
        await conn.OpenAsync(ct);

        var candidates = new (string table, string sql)[]
        {
            ("trace_txn", "select distinct client_id from trace_txn where client_id is not null and client_id <> '' order by client_id limit 500"),
            ("trace_entry_log", "select distinct client_id from trace_entry_log where client_id is not null and client_id <> '' order by client_id limit 500"),
        };

        var merged = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var (table, sql) in candidates)
        {
            try
            {
                if (!await TableExistsAsync(conn, table, ct).ConfigureAwait(false))
                {
                    continue;
                }

                await using var cmd = new NpgsqlCommand(sql, conn);
                cmd.CommandTimeout = 6;
                await using var reader = await cmd.ExecuteReaderAsync(ct);

                while (await reader.ReadAsync(ct))
                {
                    if (reader.IsDBNull(0))
                    {
                        continue;
                    }

                    var v = reader.GetString(0).Trim();
                    if (v.Length == 0)
                    {
                        continue;
                    }

                    merged.Add(v);
                }
            }
            catch (Exception ex)
            {
                _logger.Warn("ClientIdReadRepo", "client_id.query.partial_fail", "Failed one client-id query, continue fallback", ex, new { sql });
            }
        }

        return merged;
    }

    private static async Task<bool> TableExistsAsync(NpgsqlConnection conn, string table, CancellationToken ct)
    {
        const string sql = "select to_regclass(@t) is not null";
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("t", $"public.{table}");
        cmd.CommandTimeout = 6;
        var result = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
        return result is bool exists && exists;
    }
}
