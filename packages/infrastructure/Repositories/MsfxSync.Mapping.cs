using System.Globalization;
using PacToolkits.Application.DTOs;
using PacToolkits.Infrastructure.Database;

namespace PacToolkits.Infrastructure.Repositories;

public sealed partial class MsfxSyncRepo
{

    public Task<MsfxMapApplyResult> ApplyMappingAsync(int limit, CancellationToken ct)
    {
        return _db.WithConnection(async (conn, token) =>
        {
            var safeLimit = Math.Max(0, limit);
            var pendingBefore = await GetPendingCountAsync(conn, token).ConfigureAwait(false);
            if (pendingBefore <= 0)
            {
                return new MsfxMapApplyResult(0, 0, 0);
            }

            var result = await ApplyMappingViaFunctionAsync(conn, safeLimit, token).ConfigureAwait(false);
            if (result.ProcessedCount == 0)
            {
                var backlog = await GetMappingBacklogDiagnosticByConnectionAsync(conn, token).ConfigureAwait(false);
                throw new InvalidOperationException(
                    $"映射函数未推进：PENDING={backlog.PendingCount}, " +
                    $"源药名={backlog.PendingWithDrugRawCount}, " +
                    $"源规格={backlog.PendingWithSpecRawCount}, " +
                    $"关联关系={backlog.PendingWithRelationCount}, " +
                    $"归一化药名={backlog.PendingWithNameNormCount}, " +
                    $"归一化规格={backlog.PendingWithSpecNormCount}");
            }
            return result;
        }, ct);
    }

    public Task<MsfxMappingStatusSnapshot> GetMappingStatusSnapshotAsync(CancellationToken ct)
    {
        const string sql = """
            select
              coalesce(sum(case when map_status = 'PENDING' then 1 else 0 end), 0)::int as pending_count,
              coalesce(sum(case when map_status = 'MAPPED' then 1 else 0 end), 0)::int as mapped_count,
              coalesce(sum(case when map_status = 'NEED_REVIEW' then 1 else 0 end), 0)::int as review_count,
              coalesce(sum(case when map_status = 'FAILED' then 1 else 0 end), 0)::int as failed_count,
              coalesce(count(*), 0)::int as total_count
            from msfx_code_staging
            """;

        return _db.WithConnection(async (conn, token) =>
        {
            await using var cmd = conn.CreateCommand(sql, _opt.CommandTimeoutSeconds);
            await using var reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false);
            if (!await reader.ReadAsync(token).ConfigureAwait(false))
            {
                return new MsfxMappingStatusSnapshot(0, 0, 0, 0, 0);
            }

            return new MsfxMappingStatusSnapshot(
                PendingCount: reader.IsDBNull(0) ? 0 : reader.GetInt32(0),
                MappedCount: reader.IsDBNull(1) ? 0 : reader.GetInt32(1),
                NeedReviewCount: reader.IsDBNull(2) ? 0 : reader.GetInt32(2),
                FailedCount: reader.IsDBNull(3) ? 0 : reader.GetInt32(3),
                TotalCount: reader.IsDBNull(4) ? 0 : reader.GetInt32(4));
        }, ct);
    }

    public Task<MsfxBuildInject> BuildInjectsAsync(int maxGroups, CancellationToken ct)
    {
        const string cleanupSql = """
            delete from msfx_inject_task_code tc
            where tc.staging_id in (
              select s.id
              from msfx_code_staging s
              where s.map_status = 'MAPPED'
                and s.code_status = 'NEW'
                and s.inject_task_id is null
            )
            """;
        const string buildSql = "select created_tasks, tasked_codes from msfx_build_inject_tasks(@max_groups)";
        return _db.WithConnection(async (conn, token) =>
        {
            await using (var cleanupCmd = conn.CreateCommand(cleanupSql, _opt.CommandTimeoutSeconds))
            {
                await cleanupCmd.ExecuteNonQueryAsync(token).ConfigureAwait(false);
            }

            await using var cmd = conn.CreateCommand(buildSql, _opt.CommandTimeoutSeconds);
            cmd.AddParam("max_groups", Math.Max(0, maxGroups));
            await using var reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false);
            if (!await reader.ReadAsync(token).ConfigureAwait(false))
            {
                return new MsfxBuildInject(0, 0);
            }

            return new MsfxBuildInject(
                reader.GetInt32(0),
                reader.GetInt32(1));
        }, ct);
    }

    public Task<MsfxAutoBoardSnapshot> GetAutoBoardSnapshotAsync(CancellationToken ct)
    {
        const string sql = """
            with last_batch as (
              select
                b.id,
                b.status,
                b.started_at,
                b.finished_at,
                b.success_count,
                b.fail_count
              from msfx_pull_batch b
              order by b.id desc
              limit 1
            )
            select
              coalesce((select id from last_batch), 0) as last_batch_id,
              coalesce((select status from last_batch), 'NONE') as last_batch_status,
              (select started_at from last_batch) as last_batch_started_at,
              (select finished_at from last_batch) as last_batch_finished_at,
              coalesce((select success_count from last_batch), 0) as last_batch_success_count,
              coalesce((select fail_count from last_batch), 0) as last_batch_fail_count,
              (select count(*)::int from msfx_code_staging where code_status = 'NEW') as staging_new_count,
              (select count(*)::int from msfx_code_staging where code_status = 'TASKED') as staging_tasked_count,
              (select count(*)::int from msfx_code_staging where code_status = 'INJECTED') as staging_injected_count,
              (select count(*)::int from msfx_code_staging where code_status = 'VERIFIED') as staging_verified_count,
              (select count(*)::int from msfx_code_staging where code_status = 'POOLED') as staging_pooled_count,
              (select count(*)::int from msfx_code_staging where code_status = 'FAILED') as staging_failed_count,
              (select count(*)::int from msfx_code_staging where code_status = 'DUPLICATE') as staging_duplicate_count,
              (select count(*)::int from msfx_code_staging where map_status = 'PENDING') as map_pending_count,
              (select count(*)::int from msfx_code_staging where map_status = 'MAPPED') as map_mapped_count,
              (select count(*)::int from msfx_code_staging where map_status = 'NEED_REVIEW') as map_need_review_count,
              (select count(*)::int from msfx_code_staging where map_status = 'FAILED') as map_failed_count,
              (select count(*)::int from msfx_inject_task where status = 'NEW') as task_new_count,
              (select count(*)::int from msfx_inject_task where status = 'RUNNING') as task_running_count,
              (select count(*)::int from msfx_inject_task where status = 'SUCCESS') as task_success_count,
              (select count(*)::int from msfx_inject_task where status = 'FAILED') as task_failed_count,
              (select count(*)::int from msfx_inject_task where status = 'CANCELLED') as task_cancelled_count,
              (select count(*)::int from msfx_inject_task where status = 'DISCARDED') as task_discarded_count
            """;

        return _db.WithConnection(async (conn, token) =>
        {
            await using var cmd = conn.CreateCommand(sql, _opt.CommandTimeoutSeconds);
            await using var reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false);
            if (!await reader.ReadAsync(token).ConfigureAwait(false))
            {
                return new MsfxAutoBoardSnapshot(
                    0, "NONE", null, null, 0, 0,
                    0, 0, 0, 0, 0, 0, 0,
                    0, 0, 0, 0,
                    0, 0, 0, 0, 0, 0);
            }

            return new MsfxAutoBoardSnapshot(
                LastBatchId: reader.GetInt64(0),
                LastBatchStatus: reader.GetString(1),
                LastBatchStartedAt: reader.IsDBNull(2) ? null : reader.GetFieldValue<DateTimeOffset>(2),
                LastBatchFinishedAt: reader.IsDBNull(3) ? null : reader.GetFieldValue<DateTimeOffset>(3),
                LastBatchSuccessCount: reader.GetInt32(4),
                LastBatchFailCount: reader.GetInt32(5),
                StagingNewCount: reader.GetInt32(6),
                StagingTaskedCount: reader.GetInt32(7),
                StagingInjectedCount: reader.GetInt32(8),
                StagingVerifiedCount: reader.GetInt32(9),
                StagingPooledCount: reader.GetInt32(10),
                StagingFailedCount: reader.GetInt32(11),
                StagingDuplicateCount: reader.GetInt32(12),
                MapPendingCount: reader.GetInt32(13),
                MapMappedCount: reader.GetInt32(14),
                MapNeedReviewCount: reader.GetInt32(15),
                MapFailedCount: reader.GetInt32(16),
                TaskNewCount: reader.GetInt32(17),
                TaskRunningCount: reader.GetInt32(18),
                TaskSuccessCount: reader.GetInt32(19),
                TaskFailedCount: reader.GetInt32(20),
                TaskCancelledCount: reader.GetInt32(21),
                TaskDiscardedCount: reader.GetInt32(22));
        }, ct);
    }

    public Task<IReadOnlyList<MsfxPullBatchRow>> GetRecentPullBatchesAsync(int limit, CancellationToken ct)
    {
        const string sql = """
            select
              b.id,
              b.source_api,
              b.begin_date,
              b.end_date,
              b.status,
              b.success_count,
              b.fail_count,
              b.started_at,
              b.finished_at,
              b.err_msg
            from msfx_pull_batch b
            order by b.id desc
            limit @limit
            """;

        return _db.WithConnection(async (conn, token) =>
        {
            await using var cmd = conn.CreateCommand(sql, _opt.CommandTimeoutSeconds);
            cmd.AddParam("limit", Math.Clamp(limit, 1, 200));
            await using var reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false);
            var rows = new List<MsfxPullBatchRow>(Math.Max(1, limit));
            while (await reader.ReadAsync(token).ConfigureAwait(false))
            {
                rows.Add(new MsfxPullBatchRow(
                    BatchId: reader.GetInt64(0),
                    SourceApi: reader.GetString(1),
                    BeginDate: reader.IsDBNull(2) ? null : reader.GetDateTime(2),
                    EndDate: reader.IsDBNull(3) ? null : reader.GetDateTime(3),
                    Status: reader.GetString(4),
                    SuccessCount: reader.GetInt32(5),
                    FailCount: reader.GetInt32(6),
                    StartedAt: reader.GetFieldValue<DateTimeOffset>(7),
                    FinishedAt: reader.IsDBNull(8) ? null : reader.GetFieldValue<DateTimeOffset>(8),
                    ErrMsg: reader.IsDBNull(9) ? null : reader.GetString(9)));
            }

            return (IReadOnlyList<MsfxPullBatchRow>)rows;
        }, ct);
    }

    public Task<MsfxMappingQueuePage> GetMappingQueuePageAsync(
        int pageSize,
        string? mapStatus,
        string? codeStatus,
        string? searchScope,
        string? keyword,
        string[][]? pinyinExactPerToken,
        DateTimeOffset? cursorUpdatedAt,
        long? cursorId,
        bool newer,
        bool seekLastPage,
        CancellationToken ct)
    {
        var scope = NormalizeSearchScope(searchScope);
        var tokens = SplitKeywords(keyword);
        var keywordClause = BuildKeywordClause(scope, tokens, pinyinExactPerToken);
        var orderDir = seekLastPage ? "asc" : newer ? "asc" : "desc";
        var cursorClause = seekLastPage
            ? string.Empty
            : cursorUpdatedAt.HasValue && cursorId.HasValue
                ? newer
                    ? "and (s.updated_at, s.id) > (@cursor_updated_at, @cursor_id)"
                    : "and (s.updated_at, s.id) < (@cursor_updated_at, @cursor_id)"
                : string.Empty;
        var listSql = $"""
            select
              s.id,
              s.leaf_code,
              s.map_status,
              s.code_status,
              s.map_reason_code,
              s.map_reason_detail,
              s.source_bill_code,
              i.produce_batch_no,
              s.source_drug_name_raw,
              s.source_spec_raw,
              s.source_name_norm,
              s.source_spec_norm,
              s.source_code_level_1,
              s.source_code_level_2,
              s.source_code_level_3,
              s.source_code_level_4,
              s.source_code_level_5,
              s.mapped_drug_id,
              s.mapped_spec,
              to_char(b.bill_time, 'YYYY-MM-DD') as source_bill_time,
              s.updated_at
            from msfx_code_staging s
            left join msfx_upout_bill b on b.bill_code = s.source_bill_code
            left join msfx_code_relation r on r.id = s.source_relation_id
            left join msfx_upout_item i on i.id = r.upout_item_id
            where {MapQueueBaseWhere}
            {keywordClause}
            {cursorClause}
            order by s.updated_at {orderDir}, s.id {orderDir}
            limit @limit
            """;
        var countSql = $"""
            select count(*)::int
            from msfx_code_staging s
            where {MapQueueBaseWhere}
            {keywordClause}
            """;
        var hasNewerSql = $"""
            select exists(
              select 1
              from msfx_code_staging s
              where {MapQueueBaseWhere}
                {keywordClause}
                and (s.updated_at, s.id) > (@first_updated_at, @first_id)
            )
            """;
        var hasOlderSql = $"""
            select exists(
              select 1
              from msfx_code_staging s
              where {MapQueueBaseWhere}
                {keywordClause}
                and (s.updated_at, s.id) < (@last_updated_at, @last_id)
            )
            """;

        return _db.WithConnection(async (conn, token) =>
        {
            var size = Math.Clamp(pageSize, 1, 500);
            var scanned = new List<MsfxMappingQueueRow>(Math.Max(1, size + 1));
            {
                await using var listCmd = conn.CreateCommand(listSql, _opt.CommandTimeoutSeconds);
                AddNullableParam(listCmd, "map_status", NormalizeOptional(mapStatus));
                AddNullableParam(listCmd, "code_status", NormalizeOptional(codeStatus));
                AddKeywordParams(listCmd, tokens, pinyinExactPerToken);
                listCmd.AddParam("limit", size + 1);
                if (cursorUpdatedAt.HasValue && cursorId.HasValue)
                {
                    listCmd.AddParam("cursor_updated_at", cursorUpdatedAt.Value.ToUniversalTime());
                    listCmd.AddParam("cursor_id", cursorId.Value);
                }

                await using var reader = await listCmd.ExecuteReaderAsync(token).ConfigureAwait(false);
                while (await reader.ReadAsync(token).ConfigureAwait(false))
                {
                    scanned.Add(new MsfxMappingQueueRow(
                    StagingId: reader.GetInt64(0),
                    LeafCode: reader.GetString(1),
                    ProduceBatchNo: reader.IsDBNull(7) ? null : reader.GetString(7),
                    MapStatus: reader.GetString(2),
                    CodeStatus: reader.GetString(3),
                    MapReasonCode: reader.IsDBNull(4) ? null : reader.GetString(4),
                    MapReasonDetail: reader.IsDBNull(5) ? null : reader.GetString(5),
                    SourceBillCode: reader.IsDBNull(6) ? null : reader.GetString(6),
                    SourceDrugNameRaw: reader.IsDBNull(8) ? null : reader.GetString(8),
                    SourceSpecRaw: reader.IsDBNull(9) ? null : reader.GetString(9),
                    SourceNameNorm: reader.IsDBNull(10) ? null : reader.GetString(10),
                    SourceSpecNorm: reader.IsDBNull(11) ? null : reader.GetString(11),
                    SourceCodeLevel1: reader.IsDBNull(12) ? null : reader.GetString(12),
                    SourceCodeLevel2: reader.IsDBNull(13) ? null : reader.GetString(13),
                    SourceCodeLevel3: reader.IsDBNull(14) ? null : reader.GetString(14),
                    SourceCodeLevel4: reader.IsDBNull(15) ? null : reader.GetString(15),
                    SourceCodeLevel5: reader.IsDBNull(16) ? null : reader.GetString(16),
                    MappedDrugId: reader.IsDBNull(17) ? null : reader.GetString(17),
                    MappedSpec: reader.IsDBNull(18) ? null : reader.GetString(18),
                    SourceBillTime: reader.IsDBNull(19) ? null : reader.GetString(19),
                    UpdatedAt: reader.GetFieldValue<DateTimeOffset>(20)));
                }
            }

            var pageRows = scanned.Take(size).ToList();
            if (newer || seekLastPage)
            {
                pageRows.Reverse();
            }

            await using var countCmd = conn.CreateCommand(countSql, _opt.CommandTimeoutSeconds);
            AddNullableParam(countCmd, "map_status", NormalizeOptional(mapStatus));
            AddNullableParam(countCmd, "code_status", NormalizeOptional(codeStatus));
            AddKeywordParams(countCmd, tokens, pinyinExactPerToken);
            var totalCount = Convert.ToInt32((await countCmd.ExecuteScalarAsync(token).ConfigureAwait(false)) ?? 0, CultureInfo.InvariantCulture);

            var hasNewer = false;
            var hasOlder = false;
            if (pageRows.Count > 0)
            {
                var first = pageRows[0];
                var last = pageRows[^1];

                await using var newerCmd = conn.CreateCommand(hasNewerSql, _opt.CommandTimeoutSeconds);
                AddNullableParam(newerCmd, "map_status", NormalizeOptional(mapStatus));
                AddNullableParam(newerCmd, "code_status", NormalizeOptional(codeStatus));
                AddKeywordParams(newerCmd, tokens, pinyinExactPerToken);
                newerCmd.AddParam("first_updated_at", first.UpdatedAt.ToUniversalTime());
                newerCmd.AddParam("first_id", first.StagingId);
                hasNewer = Convert.ToBoolean((await newerCmd.ExecuteScalarAsync(token).ConfigureAwait(false)) ?? false, CultureInfo.InvariantCulture);

                await using var olderCmd = conn.CreateCommand(hasOlderSql, _opt.CommandTimeoutSeconds);
                AddNullableParam(olderCmd, "map_status", NormalizeOptional(mapStatus));
                AddNullableParam(olderCmd, "code_status", NormalizeOptional(codeStatus));
                AddKeywordParams(olderCmd, tokens, pinyinExactPerToken);
                olderCmd.AddParam("last_updated_at", last.UpdatedAt.ToUniversalTime());
                olderCmd.AddParam("last_id", last.StagingId);
                hasOlder = seekLastPage
                    ? false
                    : Convert.ToBoolean((await olderCmd.ExecuteScalarAsync(token).ConfigureAwait(false)) ?? false, CultureInfo.InvariantCulture);
            }

            return new MsfxMappingQueuePage(pageRows, totalCount, hasNewer, hasOlder);
        }, ct);
    }

}
