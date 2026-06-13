using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using Npgsql;
using PacToolkits.Desktop.Avalonia.DataAccess;

namespace PacToolkits.Desktop.Avalonia.Repositories;

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
            throw new ArgumentException("drug_id 不能为空", nameof(drugId));
        if (string.IsNullOrWhiteSpace(spec))
            throw new ArgumentException("spec 不能为空", nameof(spec));
        if (qty <= 0)
            throw new ArgumentOutOfRangeException(nameof(qty), "qty 必须大于 0");
        if (traceCodes.Count == 0)
            return Task.FromResult(new ScanCodeInsertResult(0, 0, 0));

        return InsertWithRetryAsync(drugId, spec, qty, traceCodes, ct);
    }

    private async Task<ScanCodeInsertResult> InsertWithRetryAsync(
        string drugId,
        string spec,
        int qty,
        IReadOnlyList<string> traceCodes,
        CancellationToken ct)
    {
        try
        {
            return await _db.WithConnection(
                (conn, token) => InsertCoreAsync(conn, drugId, spec, qty, traceCodes, token), ct).ConfigureAwait(false);
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UniqueViolation &&
                                           string.Equals(ex.ConstraintName, "trace_pool_pkey", StringComparison.Ordinal))
        {
            await _db.WithConnection(SyncTracePoolIdSequenceAsync, ct).ConfigureAwait(false);
            return await _db.WithConnection(
                (conn, token) => InsertCoreAsync(conn, drugId, spec, qty, traceCodes, token), ct).ConfigureAwait(false);
        }
    }

    private async Task<ScanCodeInsertResult> InsertCoreAsync(
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
            return new ScanCodeInsertResult(0, 0, 0);

        var requested = reader.GetInt32(0);
        var inserted = reader.GetInt32(1);
        var skipped = Math.Max(0, requested - inserted);
        return new ScanCodeInsertResult(requested, inserted, skipped);
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
