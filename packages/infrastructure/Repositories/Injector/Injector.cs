using Microsoft.Extensions.Options;
using PacToolkits.Application.Abstractions;
using PacToolkits.Application.DTOs;
using PacToolkits.Infrastructure.Database;

namespace PacToolkits.Infrastructure.Repositories;

/// <summary>Injector 预留 SQL 与仓库任务函数调用</summary>
public sealed class InjectorRepo : IInjectorRepo
{
    private readonly IDb _db;
    private readonly PgOptions _opt;

    public InjectorRepo(IDb db, IOptions<PgOptions> opt)
    {
        _db = db;
        _opt = opt.Value;
    }

    public Task<InjectorReserveResult> ReserveAsync(InjectorReserveRequest request, CancellationToken ct)
        => _db.WithConnection(async (conn, token) =>
        {
            const string sql = """
                WITH
                params AS (
                  SELECT
                    @txn_id::text    AS txn_id,
                    @client_id::text AS client_id,
                    @drug_id::text   AS drug_id,
                    @spec::text      AS spec,
                    @whole_n::int    AS whole_n,
                    @rem_need::int   AS rem_need
                ),
                stats AS (
                  SELECT
                    count(*)::int AS cnt_any,
                    count(*) FILTER (WHERE tp.status=1 AND tp.remain > 0)::int AS cnt_usable,
                    count(*) FILTER (WHERE tp.status=1 AND tp.remain = tp.qty)::int AS cnt_whole,
                    COALESCE(sum(tp.remain) FILTER (WHERE tp.status=1 AND tp.remain > 0), 0)::int AS sum_usable_remain
                  FROM trace_pool tp, params p
                  WHERE tp.drug_id = p.drug_id
                    AND tp.spec    = p.spec
                ),
                whole_locked AS MATERIALIZED (
                  SELECT tp.id
                  FROM trace_pool tp, params p
                  WHERE p.whole_n > 0
                    AND tp.drug_id = p.drug_id
                    AND tp.spec    = p.spec
                    AND tp.status  = 1
                    AND tp.remain  = tp.qty
                  ORDER BY
                    tp.in_date ASC,
                    COALESCE(tp.last_used, 'epoch'::timestamptz) ASC,
                    tp.id ASC
                  FOR UPDATE SKIP LOCKED
                  LIMIT (SELECT whole_n FROM params)
                ),
                whole_alloc AS (
                  SELECT
                    tp.id, tp.trace_code, tp.remain AS take_qty,
                    row_number() OVER (
                      ORDER BY tp.in_date ASC, COALESCE(tp.last_used, 'epoch'::timestamptz) ASC, tp.id ASC
                    )::int AS seq
                  FROM trace_pool tp
                  JOIN whole_locked w ON w.id = tp.id
                ),
                rem_locked AS MATERIALIZED (
                  SELECT tp.id
                  FROM trace_pool tp, params p
                  WHERE p.rem_need > 0
                    AND tp.drug_id = p.drug_id
                    AND tp.spec    = p.spec
                    AND tp.status  = 1
                    AND tp.remain  > 0
                    AND NOT EXISTS (SELECT 1 FROM whole_locked w WHERE w.id = tp.id)
                  ORDER BY
                    ((tp.remain < tp.qty) IS TRUE) DESC,
                    tp.in_date ASC,
                    COALESCE(tp.last_used, 'epoch'::timestamptz) ASC,
                    tp.id ASC
                  FOR UPDATE SKIP LOCKED
                  LIMIT LEAST(GREATEST((SELECT rem_need FROM params)*2, 50), 5000)
                ),
                rem_picked AS (
                  SELECT
                    tp.id, tp.trace_code, tp.remain, tp.qty,
                    row_number() OVER w AS seq,
                    sum(tp.remain) OVER w AS cum_remain
                  FROM trace_pool tp
                  JOIN rem_locked l ON l.id = tp.id
                  WINDOW w AS (
                    ORDER BY
                      ((tp.remain < tp.qty) IS TRUE) DESC,
                      tp.in_date ASC,
                      COALESCE(tp.last_used, 'epoch'::timestamptz) ASC,
                      tp.id ASC
                  )
                ),
                rem_alloc AS (
                  SELECT
                    id, trace_code, seq, remain,
                    GREATEST(
                      LEAST(
                        remain,
                        (SELECT rem_need FROM params) - (cum_remain - remain)
                      ),
                      0
                    )::int AS take_qty
                  FROM rem_picked
                ),
                whole_sum AS (
                  SELECT COALESCE(count(*),0)::int AS n, COALESCE(sum(take_qty),0)::int AS sum_take FROM whole_alloc
                ),
                rem_sum AS (
                  SELECT COALESCE(sum(take_qty),0)::int AS sum_take FROM rem_alloc
                ),
                guard AS (
                  SELECT
                    CASE
                      WHEN ws.n = (SELECT whole_n FROM params)
                       AND rs.sum_take = (SELECT rem_need FROM params) THEN 1
                      ELSE 0
                    END AS ok,
                    CASE
                      WHEN ws.n = (SELECT whole_n FROM params)
                       AND rs.sum_take = (SELECT rem_need FROM params) THEN 'OK'
                      WHEN s.cnt_any = 0 THEN 'NO_ENTRY'
                      WHEN (SELECT whole_n FROM params) > 0 AND s.cnt_whole < (SELECT whole_n FROM params) THEN 'NO_AVAILABLE'
                      WHEN (SELECT rem_need FROM params) > 0 AND s.cnt_usable = 0 THEN 'NO_AVAILABLE'
                      WHEN (SELECT rem_need FROM params) > 0
                       AND (s.sum_usable_remain - COALESCE(ws.sum_take, 0)) < (SELECT rem_need FROM params)
                        THEN 'INSUFFICIENT_TOTAL'
                      ELSE 'CONCURRENCY_OR_LIMIT'
                    END AS reason,
                    (ws.sum_take + rs.sum_take)::int AS req_qty
                  FROM stats s, whole_sum ws, rem_sum rs
                ),
                upd_whole AS (
                  UPDATE trace_pool tp
                  SET remain = tp.remain - a.take_qty
                  FROM whole_alloc a, guard g
                  WHERE g.ok = 1
                    AND tp.id = a.id
                    AND a.take_qty > 0
                  RETURNING a.seq, tp.id AS pool_id, a.trace_code, a.take_qty
                ),
                upd_rem AS (
                  UPDATE trace_pool tp
                  SET remain = tp.remain - a.take_qty
                  FROM rem_alloc a, guard g
                  WHERE g.ok = 1
                    AND tp.id = a.id
                    AND a.take_qty > 0
                  RETURNING a.seq + (SELECT whole_n FROM params), tp.id AS pool_id, a.trace_code, a.take_qty
                ),
                upd AS (
                  SELECT * FROM upd_whole
                  UNION ALL
                  SELECT * FROM upd_rem
                ),
                ins_txn AS (
                  INSERT INTO trace_txn(txn_id, client_id, drug_id, spec, req_qty, status)
                  SELECT p.txn_id, p.client_id, p.drug_id, p.spec, g.req_qty, 'PENDING'
                  FROM params p, guard g
                  WHERE g.ok = 1
                  ON CONFLICT (txn_id) DO UPDATE
                    SET client_id    = EXCLUDED.client_id,
                        drug_id      = EXCLUDED.drug_id,
                        spec         = EXCLUDED.spec,
                        req_qty      = EXCLUDED.req_qty,
                        status       = 'PENDING',
                        committed_at = NULL
                  WHERE trace_txn.status = 'PENDING'
                  RETURNING txn_id
                ),
                del_items AS (
                  DELETE FROM trace_txn_item
                  WHERE (SELECT ok FROM guard)=1
                    AND txn_id = (SELECT txn_id FROM ins_txn)
                  RETURNING 1
                ),
                ins_items AS (
                  INSERT INTO trace_txn_item(txn_id, pool_id, take_qty, trace_code)
                  SELECT (SELECT txn_id FROM ins_txn), u.pool_id, u.take_qty, u.trace_code
                  FROM upd u
                  WHERE (SELECT ok FROM guard)=1
                  RETURNING pool_id, trace_code, take_qty
                )
                SELECT
                  CASE WHEN g.ok=1 THEN 'OK' ELSE 'FAIL' END AS status,
                  g.reason,
                  u.seq, u.pool_id, u.trace_code, u.take_qty
                FROM guard g
                LEFT JOIN upd u ON g.ok=1
                ORDER BY u.seq NULLS FIRST
                """;

            await using var cmd = conn.CreateCommand(sql, _opt.CommandTimeoutSeconds);
            cmd.AddParam("txn_id", request.TxnId);
            cmd.AddParam("client_id", request.ClientId);
            cmd.AddParam("drug_id", request.DrugId);
            cmd.AddParam("spec", request.Spec);
            cmd.AddParam("whole_n", request.WholeN);
            cmd.AddParam("rem_need", request.RemNeed);
            await using var reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false);
            string? status = null;
            string? reason = null;
            var items = new List<InjectorReserveItem>();
            var sumTake = 0;
            while (await reader.ReadAsync(token).ConfigureAwait(false))
            {
                status ??= reader.GetString(0);
                reason ??= reader.GetString(1);
                if (reader.IsDBNull(3) || reader.IsDBNull(5))
                {
                    continue;
                }

                var poolId = reader.GetInt64(3);
                var take = reader.GetInt32(5);
                var code = reader.IsDBNull(4) ? string.Empty : reader.GetString(4);
                var seq = reader.IsDBNull(2) ? 0 : reader.GetInt32(2);
                if (poolId <= 0 || take <= 0 || string.IsNullOrEmpty(code))
                {
                    continue;
                }

                sumTake += take;
                items.Add(new InjectorReserveItem(poolId, take, code, seq));
            }

            if (status is null)
            {
                return new InjectorReserveResult(
                    Ok: false,
                    Reason: "NO_RESULT",
                    Message: "未返回任何结果行",
                    Items: [],
                    SumTake: 0,
                    WholeN: request.WholeN,
                    RemNeed: request.RemNeed);
            }

            if (status != "OK")
            {
                var msg = reason switch
                {
                    "NO_ENTRY" => "未录入追溯码：追溯池中不存在该药品的任何记录",
                    "NO_AVAILABLE" or "INSUFFICIENT_TOTAL" => "追溯码库存不足，请及时录入",
                    "CONCURRENCY_OR_LIMIT" => "并发抢占或 LIMIT 截断：请重试",
                    _ => reason ?? "FAIL",
                };
                return new InjectorReserveResult(
                    Ok: false,
                    Reason: reason,
                    Message: msg,
                    Items: [],
                    SumTake: 0,
                    WholeN: request.WholeN,
                    RemNeed: request.RemNeed);
            }

            return new InjectorReserveResult(
                Ok: true,
                Reason: "OK",
                Message: null,
                Items: items,
                SumTake: sumTake,
                WholeN: request.WholeN,
                RemNeed: request.RemNeed);
        }, ct);

    public Task<InjectorTxnResult> CommitAsync(string txnId, CancellationToken ct)
        => _db.WithConnection(async (conn, token) =>
        {
            const string sql = """
                UPDATE trace_txn
                SET status='COMMITTED', committed_at=now()
                WHERE txn_id=@txn_id AND status='PENDING'
                RETURNING txn_id
                """;
            await using var cmd = conn.CreateCommand(sql, _opt.CommandTimeoutSeconds);
            cmd.AddParam("txn_id", txnId);
            await using var reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false);
            if (!await reader.ReadAsync(token).ConfigureAwait(false))
            {
                return new InjectorTxnResult(false, "没有在 PENDING 状态的预留事务", 0);
            }

            return new InjectorTxnResult(true, null, 0);
        }, ct);

    public Task<InjectorTxnResult> RollbackAsync(string txnId, CancellationToken ct)
        => _db.WithConnection(async (conn, token) =>
        {
            const string sql = """
                WITH tx AS (
                  UPDATE trace_txn
                  SET status='ROLLED_BACK'
                  WHERE txn_id=@txn_id AND status='PENDING'
                  RETURNING txn_id
                ),
                upd AS (
                  UPDATE trace_pool tp
                  SET remain = tp.remain + tti.take_qty
                  FROM trace_txn_item tti, tx
                  WHERE tti.txn_id = tx.txn_id
                    AND tti.pool_id = tp.id
                  RETURNING tp.id
                )
                SELECT count(*)::int FROM upd
                """;
            await using var cmd = conn.CreateCommand(sql, _opt.CommandTimeoutSeconds);
            cmd.AddParam("txn_id", txnId);
            var restored = Convert.ToInt32(await cmd.ExecuteScalarAsync(token).ConfigureAwait(false));
            if (restored == 0)
            {
                return new InjectorTxnResult(false, "没有在 PENDING 状态的预留事务", 0);
            }

            return new InjectorTxnResult(true, null, restored);
        }, ct);

    public Task<IReadOnlyList<string>> ListExpiredPendingTxnIdsAsync(
        int timeoutMinutes,
        int maxBatch,
        CancellationToken ct)
        => _db.WithConnection(async (conn, token) =>
        {
            const string sql = """
                SELECT txn_id
                FROM trace_txn
                WHERE status='PENDING'
                  AND created_at < (now() - (@mins * interval '1 minute'))
                ORDER BY created_at, txn_id
                LIMIT @max_batch
                """;
            await using var cmd = conn.CreateCommand(sql, _opt.CommandTimeoutSeconds);
            cmd.AddParam("mins", timeoutMinutes);
            cmd.AddParam("max_batch", maxBatch);
            await using var reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false);
            var ids = new List<string>();
            while (await reader.ReadAsync(token).ConfigureAwait(false))
            {
                var id = reader.GetString(0);
                if (!string.IsNullOrWhiteSpace(id))
                {
                    ids.Add(id);
                }
            }

            return (IReadOnlyList<string>)ids;
        }, ct);

    public Task<IReadOnlyList<InjectorClaimedTask>> ClaimByTargetAsync(
        string clientId,
        string drugId,
        string spec,
        CancellationToken ct)
        => _db.WithConnection(async (conn, token) =>
        {
            const string sql = """
                SELECT task_id, bill_id, source_bill_code, mapped_drug_id, mapped_spec, total_codes
                FROM msfx_claim_inject_task_by_target(@client_id, @drug_id, @spec)
                """;
            await using var cmd = conn.CreateCommand(sql, _opt.CommandTimeoutSeconds);
            cmd.AddParam("client_id", clientId);
            cmd.AddParam("drug_id", drugId);
            cmd.AddParam("spec", spec);
            await using var reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false);
            var tasks = new List<InjectorClaimedTask>();
            while (await reader.ReadAsync(token).ConfigureAwait(false))
            {
                tasks.Add(new InjectorClaimedTask(
                    reader.GetInt64(0),
                    reader.GetInt64(1),
                    reader.IsDBNull(2) ? string.Empty : reader.GetString(2),
                    reader.IsDBNull(3) ? string.Empty : reader.GetString(3),
                    reader.IsDBNull(4) ? string.Empty : reader.GetString(4),
                    reader.IsDBNull(5) ? 0 : reader.GetInt32(5)));
            }

            return (IReadOnlyList<InjectorClaimedTask>)tasks;
        }, ct);

    public Task<bool> HasWarehouseSuccessAsync(
        string warehouseBillNo,
        string drugId,
        string spec,
        string? rowFingerprint,
        CancellationToken ct)
        => _db.WithConnection(async (conn, token) =>
        {
            const string sql = """
                SELECT msfx_has_warehouse_success_task(
                    @bill, @drug_id, @spec, NULLIF(@fingerprint, ''))
                """;
            await using var cmd = conn.CreateCommand(sql, _opt.CommandTimeoutSeconds);
            cmd.AddParam("bill", warehouseBillNo);
            cmd.AddParam("drug_id", drugId);
            cmd.AddParam("spec", spec);
            cmd.AddParam("fingerprint", rowFingerprint ?? string.Empty);
            var value = await cmd.ExecuteScalarAsync(token).ConfigureAwait(false);
            return value is true;
        }, ct);

    public Task<IReadOnlyList<InjectorPendingCode>> GetPendingCodesAsync(long taskId, CancellationToken ct)
        => _db.WithConnection(async (conn, token) =>
        {
            const string sql = """
                SELECT
                  tc.seq, tc.leaf_code, COALESCE(tc.staging_id, 0),
                  COALESCE(s.source_code_level_1, ''),
                  COALESCE(s.source_code_level_2, ''),
                  COALESCE(s.source_code_level_3, ''),
                  COALESCE(s.source_code_level_4, ''),
                  COALESCE(s.source_code_level_5, '')
                FROM msfx_inject_task_code tc
                LEFT JOIN msfx_code_staging s ON s.id = tc.staging_id
                WHERE tc.task_id = @task_id
                  AND tc.status = 'PENDING'
                ORDER BY tc.seq, tc.leaf_code
                """;
            await using var cmd = conn.CreateCommand(sql, _opt.CommandTimeoutSeconds);
            cmd.AddParam("task_id", taskId);
            await using var reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false);
            var rows = new List<InjectorPendingCode>();
            while (await reader.ReadAsync(token).ConfigureAwait(false))
            {
                rows.Add(new InjectorPendingCode(
                    reader.IsDBNull(0) ? 0 : reader.GetInt32(0),
                    reader.IsDBNull(1) ? string.Empty : reader.GetString(1),
                    reader.IsDBNull(2) ? 0 : reader.GetInt64(2),
                    reader.IsDBNull(3) ? string.Empty : reader.GetString(3),
                    reader.IsDBNull(4) ? string.Empty : reader.GetString(4),
                    reader.IsDBNull(5) ? string.Empty : reader.GetString(5),
                    reader.IsDBNull(6) ? string.Empty : reader.GetString(6),
                    reader.IsDBNull(7) ? string.Empty : reader.GetString(7)));
            }

            return (IReadOnlyList<InjectorPendingCode>)rows;
        }, ct);

    public Task UpdateTaskCodesAsync(
        long taskId,
        IReadOnlyList<string> leafCodes,
        string status,
        string? verifyResult,
        string? errMsg,
        CancellationToken ct)
    {
        var codes = DistinctNonEmpty(leafCodes);
        if (codes.Count == 0)
        {
            return Task.CompletedTask;
        }

        return _db.WithConnection(async (conn, token) =>
        {
            const string sql = """
                UPDATE msfx_inject_task_code
                SET status=@status,
                    injected_at = CASE WHEN @status = 'INJECTED' THEN now() ELSE injected_at END,
                    verify_result = NULLIF(@verify, ''),
                    err_msg = NULLIF(@err, '')
                WHERE task_id=@task_id AND leaf_code = ANY(@codes)
                """;
            await using var cmd = conn.CreateCommand(sql, _opt.CommandTimeoutSeconds);
            cmd.AddParam("status", status);
            cmd.AddParam("verify", verifyResult ?? string.Empty);
            cmd.AddParam("err", errMsg ?? string.Empty);
            cmd.AddParam("task_id", taskId);
            cmd.AddParam("codes", codes.ToArray());
            await cmd.ExecuteNonQueryAsync(token).ConfigureAwait(false);
        }, ct);
    }

    public Task UpdateStagingStatusAsync(
        IReadOnlyList<long> stagingIds,
        string codeStatus,
        string? errMsg,
        CancellationToken ct)
    {
        var ids = stagingIds.Where(static id => id > 0).Distinct().ToArray();
        if (ids.Length == 0)
        {
            return Task.CompletedTask;
        }

        return _db.WithConnection(async (conn, token) =>
        {
            const string sql = """
                UPDATE msfx_code_staging
                SET code_status=@status,
                    verified_at = CASE WHEN @status = 'VERIFIED' THEN now() ELSE verified_at END,
                    err_msg = NULLIF(@err, ''),
                    updated_at = now()
                WHERE id = ANY(@ids)
                """;
            await using var cmd = conn.CreateCommand(sql, _opt.CommandTimeoutSeconds);
            cmd.AddParam("status", codeStatus);
            cmd.AddParam("err", errMsg ?? string.Empty);
            cmd.AddParam("ids", ids);
            await cmd.ExecuteNonQueryAsync(token).ConfigureAwait(false);
        }, ct);
    }

    public Task InsertEventAsync(
        long taskId,
        string stage,
        string level,
        string message,
        string? leafCode,
        CancellationToken ct)
        => _db.WithConnection(async (conn, token) =>
        {
            const string sql = """
                INSERT INTO msfx_inject_event(task_id, leaf_code, stage, level, message)
                VALUES (@task_id, NULLIF(@leaf, ''), @stage, @level, @message)
                """;
            await using var cmd = conn.CreateCommand(sql, _opt.CommandTimeoutSeconds);
            cmd.AddParam("task_id", taskId);
            cmd.AddParam("leaf", leafCode ?? string.Empty);
            cmd.AddParam("stage", stage);
            cmd.AddParam("level", level);
            cmd.AddParam("message", message);
            await cmd.ExecuteNonQueryAsync(token).ConfigureAwait(false);
        }, ct);

    public Task<IReadOnlyList<InjectorFinalizeRow>> FinalizeAsync(
        long taskId,
        string? errMsg,
        string? warehouseBillNo,
        string? rowFingerprint,
        CancellationToken ct)
        => _db.WithConnection(async (conn, token) =>
        {
            const string sql = """
                SELECT task_id, task_status, success_codes, failed_codes, total_codes
                FROM msfx_finalize_inject_task(
                    @task_id, NULLIF(@err, ''), NULLIF(@bill, ''), NULLIF(@fp, ''))
                """;
            await using var cmd = conn.CreateCommand(sql, _opt.CommandTimeoutSeconds);
            cmd.AddParam("task_id", taskId);
            cmd.AddParam("err", errMsg ?? string.Empty);
            cmd.AddParam("bill", warehouseBillNo ?? string.Empty);
            cmd.AddParam("fp", rowFingerprint ?? string.Empty);
            await using var reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false);
            var rows = new List<InjectorFinalizeRow>();
            while (await reader.ReadAsync(token).ConfigureAwait(false))
            {
                rows.Add(new InjectorFinalizeRow(
                    reader.GetInt64(0),
                    reader.IsDBNull(1) ? string.Empty : reader.GetString(1),
                    reader.IsDBNull(2) ? 0 : reader.GetInt32(2),
                    reader.IsDBNull(3) ? 0 : reader.GetInt32(3),
                    reader.IsDBNull(4) ? 0 : reader.GetInt32(4)));
            }

            return (IReadOnlyList<InjectorFinalizeRow>)rows;
        }, ct);

    private static List<string> DistinctNonEmpty(IReadOnlyList<string>? values)
    {
        if (values is null || values.Count == 0)
        {
            return [];
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var list = new List<string>();
        foreach (var raw in values)
        {
            var v = raw.Trim();
            if (v.Length == 0 || !seen.Add(v))
            {
                continue;
            }

            list.Add(v);
        }

        return list;
    }
}
