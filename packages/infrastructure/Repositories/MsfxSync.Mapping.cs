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
            var result = await ApplyMappingViaFunctionAsync(conn, safeLimit, token).ConfigureAwait(false);
            if (result.ProcessedCount == 0)
            {
                var backlog = await GetMappingBacklogDiagnosticByConnectionAsync(conn, token).ConfigureAwait(false);
                if (backlog.PendingCount == 0)
                {
                    return result;
                }

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

    public Task<MsfxMappingQueuePage> GetMappingQueuePageAsync(
        int pageSize,
        IReadOnlyCollection<string>? mapStatuses,
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
        var normalizedMapStatuses = (mapStatuses ?? Array.Empty<string>())
            .Select(NormalizeOptional)
            .Where(static status => status is not null)
            .Select(static status => status!.ToUpperInvariant())
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var mapStatusesClause = normalizedMapStatuses.Length == 0
            ? string.Empty
            : "and s.map_status = any(@map_statuses::text[])";
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
            {mapStatusesClause}
            {keywordClause}
            {cursorClause}
            order by s.updated_at {orderDir}, s.id {orderDir}
            limit @limit
            """;
        var countSql = $"""
            select count(*)::int
            from msfx_code_staging s
            where {MapQueueBaseWhere}
            {mapStatusesClause}
            {keywordClause}
            """;
        var hasNewerSql = $"""
            select exists(
              select 1
              from msfx_code_staging s
              where {MapQueueBaseWhere}
                {mapStatusesClause}
                {keywordClause}
                and (s.updated_at, s.id) > (@first_updated_at, @first_id)
            )
            """;
        var hasOlderSql = $"""
            select exists(
              select 1
              from msfx_code_staging s
              where {MapQueueBaseWhere}
                {mapStatusesClause}
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
                AddNullableParam(listCmd, "code_status", NormalizeOptional(codeStatus));
                AddMapStatusesParam(listCmd, normalizedMapStatuses);
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
            AddNullableParam(countCmd, "code_status", NormalizeOptional(codeStatus));
            AddMapStatusesParam(countCmd, normalizedMapStatuses);
            AddKeywordParams(countCmd, tokens, pinyinExactPerToken);
            var totalCount = Convert.ToInt32((await countCmd.ExecuteScalarAsync(token).ConfigureAwait(false)) ?? 0, CultureInfo.InvariantCulture);

            var hasNewer = false;
            var hasOlder = false;
            if (pageRows.Count > 0)
            {
                var first = pageRows[0];
                var last = pageRows[^1];

                await using var newerCmd = conn.CreateCommand(hasNewerSql, _opt.CommandTimeoutSeconds);
                AddNullableParam(newerCmd, "code_status", NormalizeOptional(codeStatus));
                AddMapStatusesParam(newerCmd, normalizedMapStatuses);
                AddKeywordParams(newerCmd, tokens, pinyinExactPerToken);
                newerCmd.AddParam("first_updated_at", first.UpdatedAt.ToUniversalTime());
                newerCmd.AddParam("first_id", first.StagingId);
                hasNewer = Convert.ToBoolean((await newerCmd.ExecuteScalarAsync(token).ConfigureAwait(false)) ?? false, CultureInfo.InvariantCulture);

                await using var olderCmd = conn.CreateCommand(hasOlderSql, _opt.CommandTimeoutSeconds);
                AddNullableParam(olderCmd, "code_status", NormalizeOptional(codeStatus));
                AddMapStatusesParam(olderCmd, normalizedMapStatuses);
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

    private static void AddMapStatusesParam(Npgsql.NpgsqlCommand command, string[] mapStatuses)
    {
        if (mapStatuses.Length > 0)
        {
            command.AddParam("map_statuses", mapStatuses);
        }
    }
}
