using Npgsql;
using PacToolkits.Application.Abstractions;

namespace PacToolkits.Infrastructure.Database;

/// <summary>
/// 设置页探测用的 client_id 去重读取
///
/// 负责：按显式 <c>PgOptions</c> 直连查询候选表并合并结果
/// 不走 <c>IDb</c>（探测连接串与运行时池隔离）
/// </summary>
public sealed class ClientIdReadRepo : IClientIdReadRepo
{
    private readonly IAppLogger _logger;
    private readonly IDbAccessGuard _accessGuard;

    public ClientIdReadRepo(IAppLogger logger, IDbAccessGuard accessGuard)
    {
        _logger = logger;
        _accessGuard = accessGuard ?? throw new ArgumentNullException(nameof(accessGuard));
    }

    public async Task<HashSet<string>> GetDistinctClientIdsAsync(PgOptions opt, CancellationToken ct)
    {
        // 设置页探测使用显式连接串，不能走 IDb 运行时池
        _accessGuard.ThrowIfBlocked();

        await using var conn = await PgConnectionFactory.OpenAsync(
            opt,
            ct,
            includeKeepAlive: false,
            timeoutSeconds: 6).ConfigureAwait(false);

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
