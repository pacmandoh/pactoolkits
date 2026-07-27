using System.Data;
using Microsoft.Extensions.Options;
using PacToolkits.Application.Abstractions;
using PacToolkits.Application.DTOs;
using PacToolkits.Application.TextSearch;
using PacToolkits.Infrastructure.Database;

namespace PacToolkits.Infrastructure.Repositories;

/// <summary>
/// 库存总览（追溯池）分页与汇总查询
///
/// 实现 <c>trace_pool</c> 库存查询、文本与拼音过滤以及弃用状态联查
/// 仅数据访问，不含 UI 分页状态
/// </summary>
public sealed class InventoryOverviewRepo : IInventoryOverviewRepo
{
    private readonly IDb _db;
    private readonly PgOptions _opt;

    public InventoryOverviewRepo(IDb db, IOptions<PgOptions> opt)
    {
        _db = db;
        _opt = opt.Value;
    }

    public Task<PagedResult<TracePoolStockRowDto>> GetStockPageAsync(
        KeywordSearchContext keyword,
        int page,
        int pageSize,
        CancellationToken ct)
        => _db.WithConnection(async (conn, token) =>
        {
            var (_, safePageSize, offset) = NormalizePaging(page, pageSize, maxPageSize: 3000);

            var countSql = $"""
                select count(*)::int
                from trace_pool t
                where
                  {TracePoolKeywordSql.TracePoolWhereClause}
            """;

            var sql = $"""
                with {DrugCatalogSql.DeprecatedMapCte}
                select
                  t.drug_id,
                  t.spec,
                  t.trace_code,
                  t.qty,
                  t.remain,
                  t.status,
                  (t.remain = 0) as is_low,
                  (dm.drug_id is not null) as is_deprecated
                from trace_pool t
                left join deprecated_map dm
                  on dm.drug_id = t.drug_id
                 and dm.spec = t.spec
                where
                  {TracePoolKeywordSql.TracePoolWhereClause}
                order by t.id desc
                offset @offset
                limit @n
            """;

            await using var count = conn.CreateCommand(countSql, _opt.CommandTimeoutSeconds);
            TracePoolKeywordSql.AddKeywordParams(count, keyword);
            var totalCountObj = await count.ExecuteScalarAsync(token);
            var totalCount = totalCountObj is int i ? i : Convert.ToInt32(totalCountObj ?? 0);

            await using var cmd = conn.CreateCommand(sql, _opt.CommandTimeoutSeconds);
            TracePoolKeywordSql.AddKeywordParams(cmd, keyword);
            cmd.AddParam("offset", offset);
            cmd.AddParam("n", safePageSize);

            var list = new List<TracePoolStockRowDto>();
            await using var r = await cmd.ExecuteReaderAsync(token);
            while (await r.ReadAsync(token))
            {
                list.Add(new TracePoolStockRowDto(
                    DrugId: r.GetString(0),
                    Spec: r.GetString(1),
                    TraceCode: r.GetString(2),
                    Qty: r.GetInt32(3),
                    Remain: r.GetInt32(4),
                    Status: r.GetInt32(5),
                    IsLow: r.GetBoolean(6),
                    IsDeprecated: r.GetBoolean(7)
                ));
            }
            return new PagedResult<TracePoolStockRowDto>(list, totalCount);
        }, ct);

    public Task<PagedResult<TracePoolDrugSpecAggDto>> GetDrugSpecAggPageAsync(
        KeywordSearchContext keyword,
        int page,
        int pageSize,
        CancellationToken ct)
        => _db.WithConnection(async (conn, token) =>
        {
            var (_, safePageSize, offset) = NormalizePaging(page, pageSize, maxPageSize: 2000);
            var groupedWhere = TracePoolKeywordSql.TracePoolGroupedWhereClause;

            var countSql = $"""
                select count(*)::int
                from (
                  select 1
                  from trace_pool
                  where
                    {groupedWhere}
                  group by drug_id, spec
                ) x
            """;

            var sql = $"""
                with {DrugCatalogSql.DeprecatedMapCte},
                pool as (
                   select
                     drug_id,
                     spec,
                     count(*)::bigint               as code_count,
                     coalesce(sum(qty),0)::bigint   as qty_sum,
                     coalesce(sum(remain),0)::bigint as remain_sum
                   from trace_pool
                   where
                     {groupedWhere}
                   group by drug_id, spec
                ),
                {WeekUsageSql.WeekRangeCte},
                {WeekUsageSql.WeekUsageCte}
                select
                  p.drug_id,
                  p.spec,
                  p.code_count,
                  p.qty_sum,
                  p.remain_sum,
                  coalesce(wk.wk_used, 0)::bigint as week_used,
                  coalesce(wk.wk_used::numeric, (p.qty_sum::numeric / 2)) as threshold,
                  (p.remain_sum <= coalesce(wk.wk_used::numeric, (p.qty_sum::numeric / 2))) as is_low,
                  (dm.drug_id is not null) as is_deprecated
                from pool p
                left join wk using (drug_id, spec)
                left join deprecated_map dm
                  on dm.drug_id = p.drug_id
                 and dm.spec = p.spec
                order by p.remain_sum asc, p.drug_id asc
                offset @offset
                limit @n
            """;

            await using var count = conn.CreateCommand(countSql, _opt.CommandTimeoutSeconds);
            TracePoolKeywordSql.AddKeywordParams(count, keyword);
            var totalCountObj = await count.ExecuteScalarAsync(token);
            var totalCount = totalCountObj is int i ? i : Convert.ToInt32(totalCountObj ?? 0);

            await using var cmd = conn.CreateCommand(sql, _opt.CommandTimeoutSeconds);
            TracePoolKeywordSql.AddKeywordParams(cmd, keyword);
            cmd.AddParam("offset", offset);
            cmd.AddParam("n", safePageSize);

            var list = new List<TracePoolDrugSpecAggDto>();
            await using var r = await cmd.ExecuteReaderAsync(token);
            while (await r.ReadAsync(token))
            {
                list.Add(new TracePoolDrugSpecAggDto(
                    DrugId: r.GetString(0),
                    Spec: r.GetString(1),
                    CodeCount: r.GetInt64(2),
                    QtySum: r.GetInt64(3),
                    RemainSum: r.GetInt64(4),
                    WeekUsed: r.GetInt64(5),
                    Threshold: r.GetFieldValue<decimal>(6),
                    IsLow: r.GetBoolean(7),
                    IsDeprecated: r.GetBoolean(8)
                ));
            }
            return new PagedResult<TracePoolDrugSpecAggDto>(list, totalCount);
        }, ct);

    public Task<PagedResult<LowStockRowDto>> GetLowStockPageAsync(
        KeywordSearchContext keyword,
        int page,
        int pageSize,
        CancellationToken ct)
        => _db.WithConnection(async (conn, token) =>
        {
            var (_, safePageSize, offset) = NormalizePaging(page, pageSize, maxPageSize: 2000);
            var groupedWhere = TracePoolKeywordSql.TracePoolGroupedWhereClause;

            var countSql = $"""
                with {DrugCatalogSql.ActiveDrugCte},
                pool as (
                   select drug_id, spec,
                          coalesce(sum(remain),0)::bigint as remain_sum,
                          coalesce(sum(qty),0)::bigint as qty_sum
                   from trace_pool
                   where exists (
                     select 1
                     from active_drug a
                     where a.drug_id = trace_pool.drug_id
                       and a.spec = trace_pool.spec
                   )
                     and (
                       {groupedWhere}
                     )
                   group by drug_id, spec
                ),
                {WeekUsageSql.WeekRangeCte},
                {WeekUsageSql.WeekUsageCte}
                select count(*)::int
                from pool p
                left join wk using (drug_id,spec)
                where p.remain_sum <= coalesce(wk.wk_used::numeric, (p.qty_sum::numeric / 2))
            """;

            var sql = $"""
                with {DrugCatalogSql.ActiveDrugCte},
                pool as (
                   select drug_id, spec,
                          coalesce(sum(remain),0)::bigint as remain_sum,
                          coalesce(sum(qty),0)::bigint as qty_sum
                   from trace_pool
                   where exists (
                     select 1
                     from active_drug a
                     where a.drug_id = trace_pool.drug_id
                       and a.spec = trace_pool.spec
                   )
                     and (
                       {groupedWhere}
                     )
                   group by drug_id, spec
                ),
                {WeekUsageSql.WeekRangeCte},
                {WeekUsageSql.WeekUsageCte}
                select
                  p.drug_id,
                  p.spec,
                  p.remain_sum,
                  coalesce(wk.wk_used::numeric, (p.qty_sum::numeric / 2)) as threshold
                from pool p
                left join wk using (drug_id,spec)
                where p.remain_sum <= coalesce(wk.wk_used::numeric, (p.qty_sum::numeric / 2))
                order by p.remain_sum asc
                offset @offset
                limit @n
            """;

            await using var count = conn.CreateCommand(countSql, _opt.CommandTimeoutSeconds);
            TracePoolKeywordSql.AddKeywordParams(count, keyword);
            var totalCountObj = await count.ExecuteScalarAsync(token);
            var totalCount = totalCountObj is int i ? i : Convert.ToInt32(totalCountObj ?? 0);

            await using var cmd = conn.CreateCommand(sql, _opt.CommandTimeoutSeconds);
            TracePoolKeywordSql.AddKeywordParams(cmd, keyword);
            cmd.AddParam("offset", offset);
            cmd.AddParam("n", safePageSize);
            var list = new List<LowStockRowDto>();

            await using var r = await cmd.ExecuteReaderAsync(token);
            while (await r.ReadAsync(token))
            {
                list.Add(new LowStockRowDto(
                    DrugId: r.GetString(0),
                    Spec: r.GetString(1),
                    RemainSum: r.GetInt64(2),
                    Threshold: r.GetFieldValue<decimal>(3),
                    IsLow: true
                ));
            }
            return new PagedResult<LowStockRowDto>(list, totalCount);
        }, ct);

    public Task UpdateStockCellAsync(
        string traceCode,
        string columnHeader,
        string? rawValue,
        CancellationToken ct)
        => _db.WithConnection(async (conn, token) =>
        {
            if (string.IsNullOrWhiteSpace(traceCode))
            {
                throw new ArgumentException("trace_code 不能为空", nameof(traceCode));
            }

            var header = (columnHeader ?? string.Empty).Trim();
            var value = (rawValue ?? string.Empty).Trim();

            string sql;
            object param;

            switch (header)
            {
                case "追溯码":
                    if (value.Length == 0)
                    {
                        throw new ArgumentException("追溯码不能为空", nameof(rawValue));
                    }

                    sql = "update trace_pool set trace_code = @v where trace_code = @trace_code";
                    param = value;
                    break;
                case "数量":
                    if (!int.TryParse(value, out var qty) || qty <= 0)
                    {
                        throw new ArgumentException("数量必须为大于 0 的整数", nameof(rawValue));
                    }

                    sql = """
                          update trace_pool
                          set qty = @v,
                              remain = case when remain > @v then @v else remain end
                          where trace_code = @trace_code
                          """;
                    param = qty;
                    break;
                case "剩余":
                    if (!int.TryParse(value, out var remain) || remain < 0)
                    {
                        throw new ArgumentException("剩余必须为大于等于 0 的整数", nameof(rawValue));
                    }

                    sql = """
                          update trace_pool
                          set remain = least(@v, greatest(qty, 0))
                          where trace_code = @trace_code
                          """;
                    param = remain;
                    break;
                default:
                    throw new ArgumentException($"不支持编辑列：{header}", nameof(columnHeader));
            }

            await using var cmd = conn.CreateCommand(sql, _opt.CommandTimeoutSeconds);
            cmd.AddParam("trace_code", traceCode);
            cmd.AddParam("v", param);
            var affected = await cmd.ExecuteNonQueryAsync(token);
            if (affected <= 0)
            {
                throw new InvalidOperationException("更新失败：未找到对应追溯码记录");
            }
        }, ct);

    public Task<int> DeleteStockByTraceCodesAsync(
        IReadOnlyList<string> traceCodes,
        CancellationToken ct)
        => _db.WithConnection(async (conn, token) =>
        {
            if (traceCodes is null || traceCodes.Count == 0)
            {
                return 0;
            }

            var normalized = traceCodes
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x.Trim())
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            if (normalized.Length == 0)
            {
                return 0;
            }

            const string sql = """
                with deleted as (
                    delete from trace_pool
                    where trace_code = any(@trace_codes)
                    returning 1
                )
                select count(*)::int from deleted
            """;

            await using var cmd = conn.CreateCommand(sql, _opt.CommandTimeoutSeconds);
            cmd.AddParam("trace_codes", normalized);
            var scalar = await cmd.ExecuteScalarAsync(token);
            return scalar is int i ? i : Convert.ToInt32(scalar ?? 0);
        }, ct);

    public Task<StockReassignApplyResultDto> ReassignStockByTraceCodeAsync(
        string traceCode,
        string targetDrugId,
        string targetSpec,
        int targetQty,
        string reason,
        string operatorName,
        string source,
        CancellationToken ct)
        => _db.WithTransaction(async (conn, tx, token) =>
        {
            var trace = (traceCode ?? string.Empty).Trim();
            var targetDrug = (targetDrugId ?? string.Empty).Trim();
            var targetSpecSafe = (targetSpec ?? string.Empty).Trim();
            var qty = targetQty;
            var reasonSafe = (reason ?? string.Empty).Trim();
            var operatorSafe = (operatorName ?? string.Empty).Trim();
            var sourceSafe = (source ?? string.Empty).Trim();

            if (trace.Length == 0)
            {
                throw new ArgumentException("trace_code 不能为空", nameof(traceCode));
            }

            if (targetDrug.Length == 0 || targetSpecSafe.Length == 0)
            {
                throw new ArgumentException("目标药品名与规格不能为空");
            }

            if (qty <= 0)
            {
                throw new ArgumentException("目标数量必须为大于 0 的整数", nameof(targetQty));
            }

            if (reasonSafe.Length == 0)
            {
                throw new ArgumentException("迁移原因不能为空", nameof(reason));
            }

            if (operatorSafe.Length == 0)
            {
                operatorSafe = "unknown";
            }

            if (sourceSafe.Length == 0)
            {
                sourceSafe = "inventory_ui";
            }

            const string targetSql = DrugCatalogSql.DrugSpecExistsSql;
            bool targetExists;
            await using (var targetCmd = conn.CreateCommand(targetSql, _opt.CommandTimeoutSeconds, tx))
            {
                targetCmd.AddParam("drug_id", targetDrug);
                targetCmd.AddParam("spec", targetSpecSafe);
                var scalar = await targetCmd.ExecuteScalarAsync(token);
                targetExists = scalar is bool b && b;
            }

            if (!targetExists)
            {
                throw new InvalidOperationException("目标药品规格不存在，无法迁移");
            }

            const string lockSql = """
                select drug_id, spec, qty
                from trace_pool
                where trace_code = @trace_code
                for update
            """;

            string? oldDrugId = null;
            string? oldSpec = null;
            var oldQty = 0;
            await using (var lockCmd = conn.CreateCommand(lockSql, _opt.CommandTimeoutSeconds, tx))
            {
                lockCmd.AddParam("trace_code", trace);
                await using var reader = await lockCmd.ExecuteReaderAsync(token);
                if (await reader.ReadAsync(token))
                {
                    oldDrugId = reader.GetString(0);
                    oldSpec = reader.GetString(1);
                    oldQty = reader.GetInt32(2);
                }
            }

            if (oldDrugId is null || oldSpec is null)
            {
                throw new InvalidOperationException("未找到对应追溯码记录");
            }

            if (string.Equals(oldDrugId, targetDrug, StringComparison.Ordinal)
                && string.Equals(oldSpec, targetSpecSafe, StringComparison.Ordinal)
                && oldQty == qty)
            {
                throw new InvalidOperationException("目标药品规格与数量与当前一致，无需迁移");
            }

            const string updateSql = """
                update trace_pool
                set drug_id = @new_drug_id,
                    spec = @new_spec,
                    qty = @new_qty,
                    remain = least(remain, @new_qty)
                where trace_code = @trace_code
            """;
            int affected;
            await using (var updateCmd = conn.CreateCommand(updateSql, _opt.CommandTimeoutSeconds, tx))
            {
                updateCmd.AddParam("new_drug_id", targetDrug);
                updateCmd.AddParam("new_spec", targetSpecSafe);
                updateCmd.AddParam("new_qty", qty);
                updateCmd.AddParam("trace_code", trace);
                affected = await updateCmd.ExecuteNonQueryAsync(token);
            }

            if (affected <= 0)
            {
                throw new InvalidOperationException("迁移失败：未更新任何记录");
            }

            const string auditSql = """
                insert into inventory_reassign_audit(
                  at,
                  operator_name,
                  source,
                  reason,
                  trace_code,
                  old_drug_id,
                  old_spec,
                  new_drug_id,
                  new_spec,
                  affected_rows,
                  success,
                  error
                )
                values(
                  clock_timestamp(),
                  @operator_name,
                  @source,
                  @reason,
                  @trace_code,
                  @old_drug_id,
                  @old_spec,
                  @new_drug_id,
                  @new_spec,
                  @affected_rows,
                  true,
                  null
                )
                returning id
            """;

            long auditId;
            await using (var auditCmd = conn.CreateCommand(auditSql, _opt.CommandTimeoutSeconds, tx))
            {
                auditCmd.AddParam("operator_name", operatorSafe);
                auditCmd.AddParam("source", sourceSafe);
                auditCmd.AddParam("reason", reasonSafe);
                auditCmd.AddParam("trace_code", trace);
                auditCmd.AddParam("old_drug_id", oldDrugId);
                auditCmd.AddParam("old_spec", oldSpec);
                auditCmd.AddParam("new_drug_id", targetDrug);
                auditCmd.AddParam("new_spec", targetSpecSafe);
                auditCmd.AddParam("affected_rows", affected);

                var idObj = await auditCmd.ExecuteScalarAsync(token);
                auditId = idObj is long l ? l : Convert.ToInt64(idObj ?? 0L);
            }

            return new StockReassignApplyResultDto(affected, auditId);
        }, IsolationLevel.ReadCommitted, ct);

    public Task<StockReassignPreviewDto> PreviewStockReassignByKeywordAsync(
        KeywordSearchContext keyword,
        string targetDrugId,
        string targetSpec,
        int targetQty,
        int sampleLimit,
        CancellationToken ct)
        => _db.WithConnection(async (conn, token) =>
        {
            var kw = keyword.Keyword;
            var targetDrug = (targetDrugId ?? string.Empty).Trim();
            var targetSpecSafe = (targetSpec ?? string.Empty).Trim();
            var qty = targetQty;
            var safeLimit = Math.Clamp(sampleLimit, 1, 200);

            if (kw.Length == 0)
            {
                throw new ArgumentException("筛选关键字不能为空", nameof(keyword));
            }

            if (targetDrug.Length == 0 || targetSpecSafe.Length == 0)
            {
                throw new ArgumentException("目标药品名与规格不能为空");
            }

            if (qty <= 0)
            {
                throw new ArgumentException("目标数量必须为大于 0 的整数", nameof(targetQty));
            }

            const string targetSql = DrugCatalogSql.DrugSpecExistsSql;
            bool targetExists;
            await using (var targetCmd = conn.CreateCommand(targetSql, _opt.CommandTimeoutSeconds))
            {
                targetCmd.AddParam("drug_id", targetDrug);
                targetCmd.AddParam("spec", targetSpecSafe);
                var scalar = await targetCmd.ExecuteScalarAsync(token);
                targetExists = scalar is bool b && b;
            }

            var countSql = $"""
                select count(*)::int
                from trace_pool t
                where
                  {TracePoolKeywordSql.TracePoolRequiredMatchClause}
            """;
            int matchCount;
            await using (var countCmd = conn.CreateCommand(countSql, _opt.CommandTimeoutSeconds))
            {
                TracePoolKeywordSql.AddKeywordParams(countCmd, keyword);
                var scalar = await countCmd.ExecuteScalarAsync(token);
                matchCount = scalar is int i ? i : Convert.ToInt32(scalar ?? 0);
            }

            var willChangeSql = $"""
                select count(*)::int
                from trace_pool t
                where
                  ({TracePoolKeywordSql.TracePoolRequiredMatchClause})
                  and (t.drug_id <> @drug_id or t.spec <> @spec or t.qty <> @qty)
            """;
            int willChangeCount;
            await using (var willCmd = conn.CreateCommand(willChangeSql, _opt.CommandTimeoutSeconds))
            {
                TracePoolKeywordSql.AddKeywordParams(willCmd, keyword);
                willCmd.AddParam("drug_id", targetDrug);
                willCmd.AddParam("spec", targetSpecSafe);
                willCmd.AddParam("qty", qty);
                var scalar = await willCmd.ExecuteScalarAsync(token);
                willChangeCount = scalar is int i ? i : Convert.ToInt32(scalar ?? 0);
            }

            var sampleSql = $"""
                select t.trace_code, t.drug_id, t.spec, t.qty, t.remain
                from trace_pool t
                where
                  ({TracePoolKeywordSql.TracePoolRequiredMatchClause})
                  and (t.drug_id <> @drug_id or t.spec <> @spec or t.qty <> @qty)
                order by t.id desc
                limit @n
            """;
            var samples = new List<StockReassignPreviewItemDto>();
            await using (var sampleCmd = conn.CreateCommand(sampleSql, _opt.CommandTimeoutSeconds))
            {
                TracePoolKeywordSql.AddKeywordParams(sampleCmd, keyword);
                sampleCmd.AddParam("drug_id", targetDrug);
                sampleCmd.AddParam("spec", targetSpecSafe);
                sampleCmd.AddParam("qty", qty);
                sampleCmd.AddParam("n", safeLimit);
                await using var reader = await sampleCmd.ExecuteReaderAsync(token);
                while (await reader.ReadAsync(token))
                {
                    samples.Add(new StockReassignPreviewItemDto(
                        TraceCode: reader.GetString(0),
                        DrugId: reader.GetString(1),
                        Spec: reader.GetString(2),
                        Qty: reader.GetInt32(3),
                        Remain: reader.GetInt32(4)));
                }
            }

            return new StockReassignPreviewDto(
                TargetExists: targetExists,
                MatchCount: matchCount,
                WillChangeCount: willChangeCount,
                CurrentDrugId: null,
                CurrentSpec: null,
                IsNoopTarget: matchCount > 0 && willChangeCount == 0,
                Samples: samples);
        }, ct);

    public Task<StockReassignApplyResultDto> ReassignStockByKeywordAsync(
        KeywordSearchContext keyword,
        string targetDrugId,
        string targetSpec,
        int targetQty,
        string reason,
        string operatorName,
        string source,
        CancellationToken ct)
        => _db.WithTransaction(async (conn, tx, token) =>
        {
            var kw = keyword.Keyword;
            var targetDrug = (targetDrugId ?? string.Empty).Trim();
            var targetSpecSafe = (targetSpec ?? string.Empty).Trim();
            var qty = targetQty;
            var reasonSafe = (reason ?? string.Empty).Trim();
            var operatorSafe = (operatorName ?? string.Empty).Trim();
            var sourceSafe = (source ?? string.Empty).Trim();

            if (kw.Length == 0)
            {
                throw new ArgumentException("筛选关键字不能为空", nameof(keyword));
            }

            if (targetDrug.Length == 0 || targetSpecSafe.Length == 0)
            {
                throw new ArgumentException("目标药品名与规格不能为空");
            }

            if (qty <= 0)
            {
                throw new ArgumentException("目标数量必须为大于 0 的整数", nameof(targetQty));
            }

            if (reasonSafe.Length == 0)
            {
                throw new ArgumentException("迁移原因不能为空", nameof(reason));
            }

            if (operatorSafe.Length == 0)
            {
                operatorSafe = "unknown";
            }

            if (sourceSafe.Length == 0)
            {
                sourceSafe = "inventory_ui";
            }

            const string targetSql = DrugCatalogSql.DrugSpecExistsSql;
            bool targetExists;
            await using (var targetCmd = conn.CreateCommand(targetSql, _opt.CommandTimeoutSeconds, tx))
            {
                targetCmd.AddParam("drug_id", targetDrug);
                targetCmd.AddParam("spec", targetSpecSafe);
                var scalar = await targetCmd.ExecuteScalarAsync(token);
                targetExists = scalar is bool b && b;
            }
            if (!targetExists)
            {
                throw new InvalidOperationException("目标药品规格不存在，无法迁移");
            }

            var updateSql = $"""
                update trace_pool t
                set drug_id = @new_drug_id,
                    spec = @new_spec,
                    qty = @new_qty,
                    remain = least(remain, @new_qty)
                where
                  ({TracePoolKeywordSql.TracePoolRequiredMatchClause})
                  and (t.drug_id <> @new_drug_id or t.spec <> @new_spec or t.qty <> @new_qty)
            """;
            int affected;
            await using (var updateCmd = conn.CreateCommand(updateSql, _opt.CommandTimeoutSeconds, tx))
            {
                updateCmd.AddParam("new_drug_id", targetDrug);
                updateCmd.AddParam("new_spec", targetSpecSafe);
                updateCmd.AddParam("new_qty", qty);
                TracePoolKeywordSql.AddKeywordParams(updateCmd, keyword);
                affected = await updateCmd.ExecuteNonQueryAsync(token);
            }

            if (affected <= 0)
            {
                throw new InvalidOperationException("未检测到需要迁移的数据");
            }

            const string auditSql = """
                insert into inventory_reassign_audit(
                  at,
                  operator_name,
                  source,
                  reason,
                  trace_code,
                  old_drug_id,
                  old_spec,
                  new_drug_id,
                  new_spec,
                  affected_rows,
                  success,
                  error
                )
                values(
                  clock_timestamp(),
                  @operator_name,
                  @source,
                  @reason,
                  @trace_code,
                  @old_drug_id,
                  @old_spec,
                  @new_drug_id,
                  @new_spec,
                  @affected_rows,
                  true,
                  null
                )
                returning id
            """;
            long auditId;
            await using (var auditCmd = conn.CreateCommand(auditSql, _opt.CommandTimeoutSeconds, tx))
            {
                auditCmd.AddParam("operator_name", operatorSafe);
                auditCmd.AddParam("source", sourceSafe);
                auditCmd.AddParam("reason", $"[FILTER:{kw}] {reasonSafe}");
                auditCmd.AddParam("trace_code", $"[FILTER:{kw}]");
                auditCmd.AddParam("old_drug_id", "*");
                auditCmd.AddParam("old_spec", "*");
                auditCmd.AddParam("new_drug_id", targetDrug);
                auditCmd.AddParam("new_spec", targetSpecSafe);
                auditCmd.AddParam("affected_rows", affected);
                var scalar = await auditCmd.ExecuteScalarAsync(token);
                auditId = scalar is long l ? l : Convert.ToInt64(scalar ?? 0L);
            }

            return new StockReassignApplyResultDto(affected, auditId);
        }, IsolationLevel.ReadCommitted, ct);

    public Task<PagedResult<MissingInventoryRowDto>> GetMissingInventoryPageAsync(
        KeywordSearchContext keyword,
        int page,
        int pageSize,
        CancellationToken ct)
        => _db.WithConnection(async (conn, token) =>
        {
            var (_, safePageSize, offset) = NormalizePaging(page, pageSize, maxPageSize: 2000);
            var drugWhere = TracePoolKeywordSql.DrugIndexAliasedWhereClause;

            var countSql = $"""
                select count(*)::int
                from drug_index d
                where
                  ({drugWhere})
                  and {DrugCatalogSql.ActiveNotePredicate}
                  and not exists (
                      select 1
                      from trace_pool p
                      where p.drug_id = d.drug_id
                        and p.spec = d.spec
                  )
            """;

            var sql = $"""
                select d.drug_id, d.spec, d.note
                from drug_index d
                where
                  ({drugWhere})
                  and {DrugCatalogSql.ActiveNotePredicate}
                  and not exists (
                      select 1
                      from trace_pool p
                      where p.drug_id = d.drug_id
                        and p.spec = d.spec
                  )
                order by d.drug_id asc, d.spec asc
                offset @offset
                limit @n
            """;

            await using var count = conn.CreateCommand(countSql, _opt.CommandTimeoutSeconds);
            TracePoolKeywordSql.AddKeywordParams(count, keyword);
            var totalCountObj = await count.ExecuteScalarAsync(token);
            var totalCount = totalCountObj is int i ? i : Convert.ToInt32(totalCountObj ?? 0);

            await using var cmd = conn.CreateCommand(sql, _opt.CommandTimeoutSeconds);
            TracePoolKeywordSql.AddKeywordParams(cmd, keyword);
            cmd.AddParam("offset", offset);
            cmd.AddParam("n", safePageSize);

            var list = new List<MissingInventoryRowDto>();
            await using var r = await cmd.ExecuteReaderAsync(token);
            while (await r.ReadAsync(token))
            {
                list.Add(new MissingInventoryRowDto(
                    DrugId: r.GetString(0),
                    Spec: r.GetString(1),
                    Note: r.IsDBNull(2) ? null : r.GetString(2)
                ));
            }

            return new PagedResult<MissingInventoryRowDto>(list, totalCount);
        }, ct);

    private static (int Page, int PageSize, int Offset) NormalizePaging(int page, int pageSize, int maxPageSize)
    {
        var safePage = Math.Max(1, page);
        var safePageSize = Math.Clamp(pageSize, 1, maxPageSize);
        var offset = (safePage - 1) * safePageSize;
        return (safePage, safePageSize, offset);
    }
}
