using Microsoft.Extensions.Options;
using Npgsql;
using PacToolkits.Application.Abstractions;
using PacToolkits.Application.DTOs;
using PacToolkits.Infrastructure.Database;

namespace PacToolkits.Infrastructure.Repositories;

/// <summary>
/// 扫码入库（追溯码写入 <c>trace_pool</c>）
///
/// 实现追溯码批量写入，并在序列导致唯一冲突时校准后重试
/// 不解析扫码设备协议
/// </summary>
public sealed class ScanCodeRepo : IScanCodeRepo
{
    private readonly IDb _db;
    private readonly PgOptions _opt;

    public ScanCodeRepo(IDb db, IOptions<PgOptions> opt)
    {
        _db = db;
        _opt = opt.Value;
    }

    public Task<ScanCodeInsertResult> InsertTraceCodesAsync(
        string drugId,
        string spec,
        int qty,
        IReadOnlyList<string> traceCodes,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(drugId))
        {
            throw new ArgumentException("drug_id 不能为空", nameof(drugId));
        }

        if (string.IsNullOrWhiteSpace(spec))
        {
            throw new ArgumentException("spec 不能为空", nameof(spec));
        }

        if (qty <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(qty), "qty 必须大于 0");
        }

        if (traceCodes.Count == 0)
        {
            return Task.FromResult(new ScanCodeInsertResult(0, 0, 0));
        }

        return InsertWithRetryAsync(drugId, spec, qty, traceCodes, ct);
    }

    private Task<ScanCodeInsertResult> InsertWithRetryAsync(
        string drugId,
        string spec,
        int qty,
        IReadOnlyList<string> traceCodes,
        CancellationToken ct)
        => _db.WithConnection(
            async (conn, token) =>
            {
                var tx = AmbientDbScope.Transaction;
                // 外层用例事务里语句失败后不可继续；用 savepoint 保住 serial 校准与重试
                if (tx is not null)
                {
                    await using (var sp = conn.CreateCommand(
                                       "savepoint scan_insert",
                                       _opt.CommandTimeoutSeconds,
                                       tx))
                    {
                        await sp.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                    }
                }

                try
                {
                    var result = await InsertAsync(conn, drugId, spec, qty, traceCodes, token)
                        .ConfigureAwait(false);
                    if (tx is not null)
                    {
                        await using var release = conn.CreateCommand(
                            "release savepoint scan_insert",
                            _opt.CommandTimeoutSeconds,
                            tx);
                        await release.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                    }

                    return result;
                }
                // 仅校准 serial 漂移导致的 pkey 冲突；业务码重复走另一条路径
                catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UniqueViolation &&
                                                   string.Equals(
                                                       ex.ConstraintName,
                                                       "trace_pool_pkey",
                                                       StringComparison.Ordinal))
                {
                    if (tx is not null)
                    {
                        await using var rb = conn.CreateCommand(
                            "rollback to savepoint scan_insert",
                            _opt.CommandTimeoutSeconds,
                            tx);
                        await rb.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                    }

                    await SyncTracePoolIdSequenceAsync(conn, token).ConfigureAwait(false);
                    var retried = await InsertAsync(conn, drugId, spec, qty, traceCodes, token)
                        .ConfigureAwait(false);
                    if (tx is not null)
                    {
                        await using var release = conn.CreateCommand(
                            "release savepoint scan_insert",
                            _opt.CommandTimeoutSeconds,
                            tx);
                        await release.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                    }

                    return retried;
                }
            },
            ct);

    private async Task<ScanCodeInsertResult> InsertAsync(
        System.Data.IDbConnection conn,
        string drugId,
        string spec,
        int qty,
        IReadOnlyList<string> traceCodes,
        CancellationToken token)
    {
        const string sql = """
            with incoming as (
              select distinct btrim(x) as trace_code
              from unnest(@codes) as t(x)
              where btrim(x) <> ''
            ),
            ins as (
              insert into trace_pool(drug_id, spec, qty, trace_code, remain)
              select @drug_id, @spec, @qty, i.trace_code, @qty
              from incoming i
              on conflict (trace_code) do nothing
              returning 1
            )
            select
              (select count(*)::int from incoming) as requested_count,
              (select count(*)::int from ins) as inserted_count
        """;

        await using var cmd = conn.CreateCommand(sql, _opt.CommandTimeoutSeconds);
        cmd.AddParam("drug_id", drugId);
        cmd.AddParam("spec", spec);
        cmd.AddParam("qty", qty);
        cmd.AddParam("codes", traceCodes as string[] ?? new List<string>(traceCodes).ToArray());

        await using var reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false);
        if (!await reader.ReadAsync(token).ConfigureAwait(false))
        {
            return new ScanCodeInsertResult(0, 0, 0);
        }

        var requested = reader.GetInt32(0);
        var inserted = reader.GetInt32(1);
        var skipped = Math.Max(0, requested - inserted);
        return new ScanCodeInsertResult(requested, inserted, skipped);
    }

    public Task<IReadOnlyList<string>> FindExistingTraceCodesAsync(
        IReadOnlyList<string> traceCodes,
        CancellationToken ct)
    {
        if (traceCodes.Count == 0)
        {
            return Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>());
        }

        return _db.WithConnection(async (conn, token) =>
        {
            const string sql = """
                select trace_code
                from trace_pool
                where trace_code = any(@codes)
                """;

            await using var cmd = conn.CreateCommand(sql, _opt.CommandTimeoutSeconds);
            cmd.AddParam("codes", traceCodes as string[] ?? traceCodes.ToArray());

            var existing = new List<string>();
            await using var reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false);
            while (await reader.ReadAsync(token).ConfigureAwait(false))
            {
                existing.Add(reader.GetString(0));
            }

            return (IReadOnlyList<string>)existing;
        }, ct);
    }

    private async Task SyncTracePoolIdSequenceAsync(System.Data.IDbConnection conn, CancellationToken token)
    {
        const string sql = """
            select setval(
              pg_get_serial_sequence('trace_pool', 'id'),
              coalesce((select max(id) from trace_pool), 0) + 1,
              false
            )
        """;

        await using var cmd = conn.CreateCommand(sql, _opt.CommandTimeoutSeconds);
        _ = await cmd.ExecuteScalarAsync(token).ConfigureAwait(false);
    }
}
