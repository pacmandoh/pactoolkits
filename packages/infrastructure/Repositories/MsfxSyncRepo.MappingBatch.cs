using System.Globalization;
using PacToolkits.Application.DTOs;
using PacToolkits.Infrastructure.Database;

namespace PacToolkits.Infrastructure.Repositories;

public sealed partial class MsfxSyncRepo
{
    public Task<IReadOnlyList<MsfxMappingBatchGroupRow>> GetMappingBatchGroupsAsync(
        string? mapStatus,
        string? codeStatus,
        string? searchScope,
        string? keyword,
        string[][]? pinyinExactPerToken,
        int limit,
        CancellationToken ct)
    {
        var scope = NormalizeSearchScope(searchScope);
        var tokens = SplitKeywords(keyword);
        var keywordClause = BuildKeywordClause(scope, tokens, pinyinExactPerToken);
        var sql = $"""
            select
              coalesce(
                array_to_string(
                  array_agg(
                    distinct to_char(b.bill_time, 'YYYY-MM-DD')
                    order by to_char(b.bill_time, 'YYYY-MM-DD')
                  ),
                  ' / '
                ),
                '--'
              ) as source_bill_times,
              coalesce(
                array_to_string(
                  array_agg(
                    distinct nullif(coalesce(s.source_bill_code, ''), '')
                    order by nullif(coalesce(s.source_bill_code, ''), '')
                  ),
                  ' / '
                ),
                '--'
              ) as source_bill_codes,
              coalesce(s.source_drug_name_raw, '') as source_drug_name_raw,
              coalesce(s.source_spec_raw, '') as source_spec_raw,
              coalesce(s.source_name_norm, '') as source_name_norm,
              coalesce(s.source_spec_norm, '') as source_spec_norm,
              count(*)::int as total_count,
              count(*) filter (where s.map_status = 'PENDING')::int as pending_count,
              count(*) filter (where s.map_status = 'NEED_REVIEW')::int as need_review_count,
              count(*) filter (where s.map_status = 'FAILED')::int as failed_count
            from msfx_code_staging s
            left join msfx_upout_bill b on b.bill_code = s.source_bill_code
            where {MapQueueBaseWhere}
              {keywordClause}
              and s.map_status in ('PENDING', 'FAILED', 'NEED_REVIEW')
            group by
              coalesce(s.source_drug_name_raw, ''),
              coalesce(s.source_spec_raw, ''),
              coalesce(s.source_name_norm, ''),
              coalesce(s.source_spec_norm, '')
            order by count(*) desc,
                     coalesce(s.source_drug_name_raw, '') asc,
                     coalesce(s.source_spec_raw, '') asc
            limit @limit
            """;

        return _db.WithConnection(async (conn, token) =>
        {
            await using var cmd = conn.CreateCommand(sql, _opt.CommandTimeoutSeconds);
            AddNullableParam(cmd, "map_status", NormalizeOptional(mapStatus));
            AddNullableParam(cmd, "code_status", NormalizeOptional(codeStatus));
            AddKeywordParams(cmd, tokens, pinyinExactPerToken);
            cmd.AddParam("limit", Math.Clamp(limit, 1, 1000));
            await using var reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false);
            var rows = new List<MsfxMappingBatchGroupRow>();
            while (await reader.ReadAsync(token).ConfigureAwait(false))
            {
                var pendingCount = reader.GetInt32(7);
                var needReviewCount = reader.GetInt32(8);
                var failedCount = reader.GetInt32(9);
                rows.Add(new MsfxMappingBatchGroupRow(
                    SourceBillTimes: reader.GetString(0),
                    SourceBillCodes: reader.GetString(1),
                    SourceDrugNameRaw: reader.GetString(2),
                    SourceSpecRaw: reader.GetString(3),
                    SourceNameNorm: reader.GetString(4),
                    SourceSpecNorm: reader.GetString(5),
                    TotalCount: reader.GetInt32(6),
                    PendingCount: pendingCount,
                    NeedReviewCount: needReviewCount,
                    FailedCount: failedCount));
            }

            return (IReadOnlyList<MsfxMappingBatchGroupRow>)rows;
        }, ct);
    }

    public Task<MsfxMappingBatchPreview> PreviewMappingBatchByGroupAsync(
        string? mapStatus,
        string? codeStatus,
        string? searchScope,
        string? keyword,
        string[][]? pinyinExactPerToken,
        string? groupSourceDrugNameRaw,
        string? groupSourceSpecRaw,
        string? groupSourceNameNorm,
        string? groupSourceSpecNorm,
        string action,
        string? drugId,
        string? spec,
        CancellationToken ct)
    {
        var scope = NormalizeSearchScope(searchScope);
        var tokens = SplitKeywords(keyword);
        var keywordClause = BuildKeywordClause(scope, tokens, pinyinExactPerToken);
        var actionNorm = NormalizeOptional(action)?.ToUpperInvariant() ?? "APPLY_MAP";
        const string groupClause = """
            and coalesce(s.source_drug_name_raw, '') = @g_source_drug_name_raw
            and coalesce(s.source_spec_raw, '') = @g_source_spec_raw
            and coalesce(s.source_name_norm, '') = @g_source_name_norm
            and coalesce(s.source_spec_norm, '') = @g_source_spec_norm
            """;
        var sql = actionNorm switch
        {
            _ => $"""
                with filtered as (
                  select s.id
                  from msfx_code_staging s
                  where {MapQueueBaseWhere}
                    {keywordClause}
                    {groupClause}
                    and s.map_status in ('PENDING', 'FAILED', 'NEED_REVIEW')
                )
                select count(*)::int as candidate_count
                from filtered
                """
        };

        return _db.WithConnection(async (conn, token) =>
        {
            var normalizedDrugId = NormalizeOptional(drugId);
            var normalizedSpec = NormalizeOptional(spec);
            var targetExists = false;
            if (actionNorm is "APPLY_MAP" or "APPLY_DISCARD")
            {
                if (string.IsNullOrWhiteSpace(normalizedDrugId) || string.IsNullOrWhiteSpace(normalizedSpec))
                {
                    return new MsfxMappingBatchPreview(0, 0, 0);
                }

                const string existsSql = """
                    select exists(
                      select 1 from drug_index d
                      where d.drug_id = @drug_id
                        and d.spec = @spec
                    )
                    """;
                await using var existsCmd = conn.CreateCommand(existsSql, _opt.CommandTimeoutSeconds);
                existsCmd.AddParam("drug_id", normalizedDrugId);
                existsCmd.AddParam("spec", normalizedSpec);
                targetExists = Convert.ToBoolean((await existsCmd.ExecuteScalarAsync(token).ConfigureAwait(false)) ?? false, CultureInfo.InvariantCulture);
            }

            await using var cmd = conn.CreateCommand(sql, _opt.CommandTimeoutSeconds);
            AddNullableParam(cmd, "map_status", NormalizeOptional(mapStatus));
            AddNullableParam(cmd, "code_status", NormalizeOptional(codeStatus));
            cmd.AddParam("g_source_drug_name_raw", NormalizeGroupKey(groupSourceDrugNameRaw));
            cmd.AddParam("g_source_spec_raw", NormalizeGroupKey(groupSourceSpecRaw));
            cmd.AddParam("g_source_name_norm", NormalizeGroupKey(groupSourceNameNorm));
            cmd.AddParam("g_source_spec_norm", NormalizeGroupKey(groupSourceSpecNorm));
            AddKeywordParams(cmd, tokens, pinyinExactPerToken);
            await using var reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false);
            if (!await reader.ReadAsync(token).ConfigureAwait(false))
            {
                return new MsfxMappingBatchPreview(0, 0, 0);
            }

            var candidate = reader.GetInt32(0);
            var eligible = targetExists ? candidate : 0;
            var blocked = Math.Max(0, candidate - eligible);
            return new MsfxMappingBatchPreview(candidate, eligible, blocked);
        }, ct);
    }

    public Task<MsfxMappingBatchApplyResult> ApplyMappingBatchByGroupAsync(
        string? mapStatus,
        string? codeStatus,
        string? searchScope,
        string? keyword,
        string[][]? pinyinExactPerToken,
        string? groupSourceDrugNameRaw,
        string? groupSourceSpecRaw,
        string? groupSourceNameNorm,
        string? groupSourceSpecNorm,
        string action,
        string? drugId,
        string? spec,
        CancellationToken ct)
    {
        var scope = NormalizeSearchScope(searchScope);
        var tokens = SplitKeywords(keyword);
        var keywordClause = BuildKeywordClause(scope, tokens, pinyinExactPerToken);
        var actionNorm = NormalizeOptional(action)?.ToUpperInvariant() ?? "APPLY_MAP";
        const string groupClause = """
            and coalesce(s.source_drug_name_raw, '') = @g_source_drug_name_raw
            and coalesce(s.source_spec_raw, '') = @g_source_spec_raw
            and coalesce(s.source_name_norm, '') = @g_source_name_norm
            and coalesce(s.source_spec_norm, '') = @g_source_spec_norm
            """;
        var sql = actionNorm switch
        {
            "APPLY_DISCARD" => $"""
                with target as (
                  select
                    s.id as staging_id,
                    s.leaf_code,
                    b.id as bill_id,
                    s.source_bill_code,
                    s.created_at
                  from msfx_code_staging s
                  left join msfx_upout_bill b on b.bill_code = s.source_bill_code
                  where {MapQueueBaseWhere}
                    {keywordClause}
                    {groupClause}
                    and s.map_status in ('PENDING', 'FAILED', 'NEED_REVIEW')
                ),
                upd_map as (
                  update msfx_code_staging s
                  set {ManualMapNormBackfillSetClause}
                      map_status = 'MAPPED',
                      map_reason_code = 'MANUAL_MAP_DISCARD',
                      map_reason_detail = 'manual map and discard task',
                      inject_task_id = null,
                      code_status = case when s.code_status in ('FAILED', 'DUPLICATE') then 'NEW' else s.code_status end,
                      err_msg = null,
                      updated_at = now()
                  from target t
                  where s.id = t.staging_id
                    and exists (
                      select 1 from drug_index d
                      where d.drug_id = @drug_id
                        and d.spec = @spec
                    )
                  returning s.id
                ),
                mapped as (
                  select t.*
                  from target t
                  join upd_map u on u.id = t.staging_id
                ),
                del_old_code as (
                  delete from msfx_inject_task_code tc
                  using mapped m
                  where tc.staging_id = m.staging_id
                ),
                picked_groups as (
                  select
                    m.bill_id,
                    m.source_bill_code,
                    min(m.created_at) as first_at,
                    min(m.staging_id) as first_staging_id
                  from mapped m
                  group by m.bill_id, m.source_bill_code
                ),
                seq_base as (
                  select coalesce(max(t.queue_seq), 0)::bigint as base_seq
                  from msfx_inject_task t
                ),
                numbered_groups as (
                  select
                    g.bill_id,
                    g.source_bill_code,
                    (sb.base_seq + row_number() over (order by g.first_at, g.first_staging_id))::bigint as queue_seq
                  from picked_groups g
                  cross join seq_base sb
                ),
                ins_task as (
                  insert into msfx_inject_task (
                    task_type,
                    status,
                    bill_id,
                    source_bill_code,
                    mapped_drug_id,
                    mapped_spec,
                    queue_seq,
                    total_codes,
                    finished_at,
                    err_msg
                  )
                  select
                    'MSFX_INBOUND',
                    'DISCARDED',
                    m.bill_id,
                    m.source_bill_code,
                    @drug_id,
                    @spec,
                    min(g.queue_seq)::bigint,
                    count(*)::int,
                    now(),
                    'task discarded from mapping batch'
                  from mapped m
                  join numbered_groups g
                    on m.bill_id is not distinct from g.bill_id
                   and m.source_bill_code is not distinct from g.source_bill_code
                  group by m.bill_id, m.source_bill_code
                  returning id, bill_id, source_bill_code
                ),
                ins_code as (
                  insert into msfx_inject_task_code (
                    task_id,
                    leaf_code,
                    staging_id,
                    seq,
                    status,
                    err_msg
                  )
                  select
                    t.id,
                    m.leaf_code,
                    m.staging_id,
                    row_number() over (
                      partition by t.id
                      order by m.created_at, m.staging_id
                    )::int,
                    'PENDING',
                    'task discarded from mapping batch'
                  from mapped m
                  join ins_task t
                    on m.bill_id is not distinct from t.bill_id
                   and m.source_bill_code is not distinct from t.source_bill_code
                  on conflict (task_id, leaf_code) do nothing
                  returning task_id, staging_id
                ),
                upd_stage as (
                  update msfx_code_staging s
                  set inject_task_id = i.task_id,
                      code_status = 'TASKED',
                      updated_at = now()
                  from ins_code i
                  where s.id = i.staging_id
                  returning s.id
                ),
                evt as (
                  insert into msfx_inject_event(task_id, stage, level, message)
                  select
                    t.id,
                    'DB_SYNC',
                    'WARN',
                    'task discarded from mapping batch'
                  from ins_task t
                  returning 1
                )
                select coalesce((select count(*)::int from upd_map), 0)
                """,
            _ => $"""
                with target as (
                  select s.id
                  from msfx_code_staging s
                  where {MapQueueBaseWhere}
                    {keywordClause}
                    {groupClause}
                    and s.map_status in ('PENDING', 'FAILED', 'NEED_REVIEW')
                ),
                del_old_code as (
                  delete from msfx_inject_task_code tc
                  using target t
                  where tc.staging_id = t.id
                ),
                upd_map as (
                  update msfx_code_staging s
                  set {ManualMapNormBackfillSetClause}
                      map_status = 'MAPPED',
                      map_reason_code = 'MANUAL_MAP',
                      map_reason_detail = null,
                      inject_task_id = null,
                      code_status = case when s.code_status in ('FAILED', 'DUPLICATE') then 'NEW' else s.code_status end,
                      err_msg = null,
                      updated_at = now()
                  from target t
                  where s.id = t.id
                    and exists (
                      select 1 from drug_index d
                      where d.drug_id = @drug_id
                        and d.spec = @spec
                    )
                  returning s.id
                )
                select count(*)::int from upd_map
                """
        };

        return _db.WithConnection(async (conn, token) =>
        {
            await using var cmd = conn.CreateCommand(sql, _opt.CommandTimeoutSeconds);
            AddNullableParam(cmd, "map_status", NormalizeOptional(mapStatus));
            AddNullableParam(cmd, "code_status", NormalizeOptional(codeStatus));
            cmd.AddParam("g_source_drug_name_raw", NormalizeGroupKey(groupSourceDrugNameRaw));
            cmd.AddParam("g_source_spec_raw", NormalizeGroupKey(groupSourceSpecRaw));
            cmd.AddParam("g_source_name_norm", NormalizeGroupKey(groupSourceNameNorm));
            cmd.AddParam("g_source_spec_norm", NormalizeGroupKey(groupSourceSpecNorm));
            AddKeywordParams(cmd, tokens, pinyinExactPerToken);
            var normalizedDrugId = NormalizeOptional(drugId);
            var normalizedSpec = NormalizeOptional(spec);
            if (string.IsNullOrWhiteSpace(normalizedDrugId) || string.IsNullOrWhiteSpace(normalizedSpec))
            {
                return new MsfxMappingBatchApplyResult(0);
            }

            cmd.AddParam("drug_id", normalizedDrugId);
            cmd.AddParam("spec", normalizedSpec);

            var affectedCount = Convert.ToInt32((await cmd.ExecuteScalarAsync(token).ConfigureAwait(false)) ?? 0, CultureInfo.InvariantCulture);
            return new MsfxMappingBatchApplyResult(affectedCount);
        }, ct);
    }

}
