using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Npgsql;
using pactoolkits_ui.Common;

namespace pactoolkits_ui.DataAccess;

public interface IClientIdReadRepo
{
    Task<HashSet<string>> GetDistinctClientIdsAsync(PgOptions opt, CancellationToken ct);
}

public sealed class ClientIdReadRepo : IClientIdReadRepo
{
    public async Task<HashSet<string>> GetDistinctClientIdsAsync(PgOptions opt, CancellationToken ct)
    {
        var cs =
            $"Host={opt.Host};Port={opt.Port};Database={opt.Database};Username={opt.Username};Password={opt.Password};Timeout=6;Command Timeout=6";

        await using var conn = new NpgsqlConnection(cs);
        await conn.OpenAsync(ct);

        var candidates = new[]
        {
            "select distinct client_id from trace_txn where client_id is not null and client_id <> '' order by client_id limit 500",
            "select distinct client_id from trace_entry_log where client_id is not null and client_id <> '' order by client_id limit 500",
            "select distinct client_id from trace_entry where client_id is not null and client_id <> '' order by client_id limit 500",
        };

        var merged = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var sql in candidates)
        {
            try
            {
                await using var cmd = new NpgsqlCommand(sql, conn);
                cmd.CommandTimeout = 6;
                await using var reader = await cmd.ExecuteReaderAsync(ct);

                while (await reader.ReadAsync(ct))
                {
                    if (reader.IsDBNull(0)) continue;
                    var v = reader.GetString(0).Trim();
                    if (v.Length == 0) continue;
                    merged.Add(v);
                }
            }
            catch (System.Exception ex)
            {
                AppLog.Warn("ClientIdReadRepo", "client_id.query.partial_fail", "Failed one client-id query, continue fallback", ex, new { sql });
            }
        }

        return merged;
    }
}
