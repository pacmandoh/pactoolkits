using System.Data;
using Microsoft.Extensions.Options;
using PacToolkits.Application.Abstractions;
using PacToolkits.Application.DTOs;
using PacToolkits.Infrastructure.Database;

namespace PacToolkits.Infrastructure.Repositories;

/// <summary>在 PostgreSQL 中执行库存取码预留与条码审计</summary>
public sealed class TraceBarcodeRepo : ITraceBarcodeRepo
{
    private readonly IDb _db;
    private readonly PgOptions _opt;

    public TraceBarcodeRepo(IDb db, IOptions<PgOptions> opt)
    {
        _db = db;
        _opt = opt.Value;
    }

    private const string CandidateCte = """
        with excluded_recent as (
          select distinct l.trace_code
          from trace_barcode_audit_log l
          where @exclude_days > 0
            and l.action in ('export', 'preview')
            and l.success = true
            and l.at >= clock_timestamp() - (@exclude_days::text || ' days')::interval
        )
        """;

    public Task<TraceBarcodePickGroup> PickAsync(
        Guid batchId,
        string operatorName,
        string drugId,
        string spec,
        int count,
        int? excludeRecentDays,
        IReadOnlyList<string> excludeTraceCodes,
        CancellationToken ct)
        => _db.WithTransaction(async (conn, _, token) =>
        {
            var safeCount = Math.Max(0, count);
            var excludeDays = excludeRecentDays.GetValueOrDefault(0);
            if (excludeDays < 0)
            {
                excludeDays = 0;
            }

            var excludeCodes = excludeTraceCodes?.Count > 0
                ? excludeTraceCodes.ToArray()
                : Array.Empty<string>();

            var pickSql = CandidateCte + """
                , locked as (
                  select tp.id
                  from trace_pool tp
                  where tp.drug_id = @drug_id
                    and tp.spec = @spec
                    and tp.status = 1
                    and tp.remain > 0
                    and not (tp.trace_code = any(@exclude_codes))
                    and not exists (
                      select 1
                      from excluded_recent er
                      where er.trace_code = tp.trace_code
                    )
                  order by
                    ((tp.remain < tp.qty) is true) desc,
                    tp.in_date asc,
                    coalesce(tp.last_used, 'epoch'::timestamptz) asc,
                    tp.id asc
                  for update skip locked
                  limit @pick_n
                )
                select
                  tp.trace_code,
                  tp.drug_id,
                  tp.spec,
                  tp.qty,
                  tp.remain,
                  tp.in_date
                from trace_pool tp
                inner join locked l on l.id = tp.id
                order by
                  ((tp.remain < tp.qty) is true) desc,
                  tp.in_date asc,
                  coalesce(tp.last_used, 'epoch'::timestamptz) asc,
                  tp.id asc
                """;

            await using var pickCmd = conn.CreateCommand(pickSql, _opt.CommandTimeoutSeconds);
            pickCmd.AddParam("drug_id", drugId);
            pickCmd.AddParam("spec", spec);
            pickCmd.AddParam("exclude_days", excludeDays);
            pickCmd.AddParam("exclude_codes", excludeCodes);
            pickCmd.AddParam("pick_n", safeCount);

            var rows = new List<TraceBarcodePickedRow>();
            await using (var reader = await pickCmd.ExecuteReaderAsync(token).ConfigureAwait(false))
            {
                while (await reader.ReadAsync(token).ConfigureAwait(false))
                {
                    var inDate = reader.IsDBNull(5)
                        ? (DateTimeOffset?)null
                        : reader.GetFieldValue<DateTimeOffset>(5);
                    rows.Add(new TraceBarcodePickedRow(
                        reader.GetString(0),
                        reader.GetString(1),
                        reader.GetString(2),
                        reader.GetInt32(3),
                        reader.GetInt32(4),
                        inDate));
                }
            }

            var picked = rows.Count;
            var gap = Math.Max(0, safeCount - picked);
            if (picked > 0)
            {
                // 预览审计必须与候选锁同一事务提交，避免锁释放到审计写入之间被其它客户端重复选中
                await InsertAuditItemsAsync(
                        conn,
                        new TraceBarcodeAuditRequest(
                            batchId,
                            "preview",
                            operatorName,
                            rows.Select(static row => new TraceBarcodeAuditItem(
                                row.TraceCode,
                                row.DrugId,
                                row.Spec,
                                null))
                            .ToArray()),
                        token)
                    .ConfigureAwait(false);
            }

            return new TraceBarcodePickGroup(drugId, spec, safeCount, picked, gap, rows);
        }, ct: ct);

    public Task<int> InsertAuditAsync(TraceBarcodeAuditRequest request, CancellationToken ct)
        => _db.WithTransaction(
            (conn, _, token) => InsertAuditItemsAsync(conn, request, token),
            ct: ct);

    private async Task<int> InsertAuditItemsAsync(
        IDbConnection conn,
        TraceBarcodeAuditRequest request,
        CancellationToken ct)
    {
        const string sql = """
            insert into trace_barcode_audit_log(
              batch_id,
              operator_name,
              action,
              trace_code,
              drug_id,
              spec,
              file_name,
              success,
              error
            )
            values (
              @batch_id,
              @operator_name,
              @action,
              @trace_code,
              @drug_id,
              @spec,
              @file_name,
              @success,
              @error
            )
            on conflict (batch_id, action, trace_code) do nothing
            """;

        var inserted = 0;
        foreach (var item in request.Items)
        {
            await using var cmd = conn.CreateCommand(sql, _opt.CommandTimeoutSeconds);
            cmd.AddParam("batch_id", request.BatchId);
            cmd.AddParam("operator_name", request.OperatorName);
            cmd.AddParam("action", request.Action);
            cmd.AddParam("trace_code", item.TraceCode);
            cmd.AddParam("drug_id", (object?)item.DrugId ?? DBNull.Value);
            cmd.AddParam("spec", (object?)item.Spec ?? DBNull.Value);
            cmd.AddParam("file_name", (object?)item.FileName ?? DBNull.Value);
            cmd.AddParam("success", item.Success);
            cmd.AddParam("error", (object?)item.Error ?? DBNull.Value);
            inserted += await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        return inserted;
    }

    public Task<int> ClearAuditLogAsync(CancellationToken ct)
        => _db.WithConnection(async (conn, token) =>
        {
            const string sql = "delete from trace_barcode_audit_log";
            await using var cmd = conn.CreateCommand(sql, _opt.CommandTimeoutSeconds);
            return await cmd.ExecuteNonQueryAsync(token).ConfigureAwait(false);
        }, ct);
}
