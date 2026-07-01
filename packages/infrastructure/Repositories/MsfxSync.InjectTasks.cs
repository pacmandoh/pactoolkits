using PacToolkits.Application.DTOs;
using PacToolkits.Infrastructure.Database;

namespace PacToolkits.Infrastructure.Repositories;

public sealed partial class MsfxSyncRepo
{
    public Task<IReadOnlyList<MsfxInjectQueueRow>> GetInjectQueueAsync(int limit, CancellationToken ct)
    {
        var sqlBody = $"""
            select
              t.id,
              t.status,
              t.source_bill_code,
              coalesce(
                string_agg(distinct nullif(btrim(coalesce(i.produce_batch_no, '')), ''), ' / ' order by nullif(btrim(coalesce(i.produce_batch_no, '')), '')),
                '--'
              ) as batch_nos,
              t.mapped_drug_id,
              t.mapped_spec,
              t.total_codes,
              count(tc.leaf_code)::int as current_code_count,
              t.success_codes,
              t.failed_codes,
              t.retry_count,
              t.created_at,
              t.picked_at,
              t.finished_at,
              t.err_msg
            from msfx_inject_task t
            {MsfxInjectSql.QueueStagingJoin}
            group by
              t.id,
              t.status,
              t.source_bill_code,
              t.mapped_drug_id,
              t.mapped_spec,
              t.total_codes,
              t.success_codes,
              t.failed_codes,
              t.retry_count,
              t.created_at,
              t.picked_at,
              t.finished_at,
              t.err_msg,
              t.queue_seq
            order by
              case when t.status = 'DISCARDED' then 1 else 0 end,
              t.queue_seq,
              t.id
            """;

        return _db.WithConnection(async (conn, token) =>
        {
            var useLimit = limit > 0;
            var sql = useLimit ? $"{sqlBody}\nlimit @limit" : sqlBody;
            await using var cmd = conn.CreateCommand(sql, _opt.CommandTimeoutSeconds);
            if (useLimit)
            {
                cmd.AddParam("limit", Math.Clamp(limit, 1, 5000));
            }

            await using var reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false);
            var rows = new List<MsfxInjectQueueRow>(Math.Max(1, useLimit ? limit : 256));
            while (await reader.ReadAsync(token).ConfigureAwait(false))
            {
                rows.Add(new MsfxInjectQueueRow(
                    TaskId: reader.GetInt64(0),
                    Status: reader.GetString(1),
                    SourceBillCode: reader.IsDBNull(2) ? null : reader.GetString(2),
                    BatchNos: reader.IsDBNull(3) ? null : reader.GetString(3),
                    MappedDrugId: reader.GetString(4),
                    MappedSpec: reader.GetString(5),
                    TotalCodes: reader.GetInt32(6),
                    CurrentCodeCount: reader.GetInt32(7),
                    SuccessCodes: reader.GetInt32(8),
                    FailedCodes: reader.GetInt32(9),
                    RetryCount: reader.GetInt32(10),
                    CreatedAt: reader.GetFieldValue<DateTimeOffset>(11),
                    PickedAt: reader.IsDBNull(12) ? null : reader.GetFieldValue<DateTimeOffset>(12),
                    FinishedAt: reader.IsDBNull(13) ? null : reader.GetFieldValue<DateTimeOffset>(13),
                    ErrMsg: reader.IsDBNull(14) ? null : reader.GetString(14)));
            }

            return (IReadOnlyList<MsfxInjectQueueRow>)rows;
        }, ct);
    }

    public Task<MsfxInjectReopen> ReopenInjectAsync(long taskId, string? operatorName, string? reason, CancellationToken ct)
    {
        const string sql = """
            select task_id, task_status, total_codes
            from msfx_reopen_inject_task(@task_id, @operator_name, @reason)
            """;

        return _db.WithConnection(async (conn, token) =>
        {
            await using var cmd = conn.CreateCommand(sql, _opt.CommandTimeoutSeconds);
            cmd.AddParam("task_id", taskId);
            AddNullableParam(cmd, "operator_name", NullIfWhiteSpace(operatorName));
            AddNullableParam(cmd, "reason", NullIfWhiteSpace(reason));

            await using var reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false);
            if (!await reader.ReadAsync(token).ConfigureAwait(false))
            {
                throw new InvalidOperationException($"未能重开任务 {taskId}");
            }

            return new MsfxInjectReopen(
                TaskId: reader.GetInt64(0),
                Status: reader.GetString(1),
                TotalCodes: reader.GetInt32(2));
        }, ct);
    }

    public Task<MsfxInjectDiscard> DiscardInjectAsync(long taskId, string? operatorName, string? reason, CancellationToken ct)
    {
        const string sql = """
            select task_id, task_status, total_codes
            from msfx_discard_inject_task(@task_id, @operator_name, @reason)
            """;

        return _db.WithConnection(async (conn, token) =>
        {
            await using var cmd = conn.CreateCommand(sql, _opt.CommandTimeoutSeconds);
            cmd.AddParam("task_id", taskId);
            AddNullableParam(cmd, "operator_name", NullIfWhiteSpace(operatorName));
            AddNullableParam(cmd, "reason", NullIfWhiteSpace(reason));

            await using var reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false);
            if (!await reader.ReadAsync(token).ConfigureAwait(false))
            {
                throw new InvalidOperationException($"未能弃用任务 {taskId}");
            }

            return new MsfxInjectDiscard(
                TaskId: reader.GetInt64(0),
                Status: reader.GetString(1),
                TotalCodes: reader.GetInt32(2));
        }, ct);
    }

    public Task<MsfxInjectRemap> RemapInjectAsync(long taskId, string? operatorName, string? reason, CancellationToken ct)
    {
        const string sql = """
            select task_id, task_status, total_codes, reset_staging_count
            from msfx_remap_inject_task(@task_id, @operator_name, @reason)
            """;

        return _db.WithConnection(async (conn, token) =>
        {
            await using var cmd = conn.CreateCommand(sql, _opt.CommandTimeoutSeconds);
            cmd.AddParam("task_id", taskId);
            AddNullableParam(cmd, "operator_name", NullIfWhiteSpace(operatorName));
            AddNullableParam(cmd, "reason", NullIfWhiteSpace(reason));

            await using var reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false);
            if (!await reader.ReadAsync(token).ConfigureAwait(false))
            {
                throw new InvalidOperationException($"未能回退任务 {taskId} 到映射队列");
            }

            return new MsfxInjectRemap(
                TaskId: reader.GetInt64(0),
                Status: reader.GetString(1),
                TotalCodes: reader.GetInt32(2),
                ResetStagingCount: reader.GetInt32(3));
        }, ct);
    }

    public Task<MsfxInjectMerge> MergeInjectsAsync(IReadOnlyList<long> taskIds, string? operatorName, string? reason, CancellationToken ct)
    {
        const string sql = """
            select
              result_task_id as task_id,
              result_task_status as task_status,
              result_total_codes as total_codes,
              result_merged_task_count as merged_task_count
            from msfx_merge_inject_tasks(@task_ids, @operator_name, @reason)
            """;

        return _db.WithConnection(async (conn, token) =>
        {
            var ids = taskIds?
                .Where(x => x > 0)
                .Distinct()
                .ToArray() ?? Array.Empty<long>();
            if (ids.Length < 2)
            {
                throw new InvalidOperationException("至少需要两条任务才能执行合并");
            }

            await using var cmd = conn.CreateCommand(sql, _opt.CommandTimeoutSeconds);
            cmd.AddParam("task_ids", ids);
            AddNullableParam(cmd, "operator_name", NullIfWhiteSpace(operatorName));
            AddNullableParam(cmd, "reason", NullIfWhiteSpace(reason));

            await using var reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false);
            if (!await reader.ReadAsync(token).ConfigureAwait(false))
            {
                throw new InvalidOperationException("未能完成任务合并");
            }

            return new MsfxInjectMerge(
                TaskId: reader.GetInt64(0),
                Status: reader.GetString(1),
                TotalCodes: reader.GetInt32(2),
                MergedTaskCount: reader.GetInt32(3));
        }, ct);
    }

    public Task<MsfxInjectSplit> SplitInjectAsync(long taskId, string splitMode, string? operatorName, string? reason, CancellationToken ct)
    {
        const string sql = """
            select
              result_created_tasks as created_tasks,
              result_total_codes as total_codes,
              result_split_mode as split_mode
            from msfx_split_inject_task(@task_id, @split_mode, @operator_name, @reason)
            """;

        return _db.WithConnection(async (conn, token) =>
        {
            await using var cmd = conn.CreateCommand(sql, _opt.CommandTimeoutSeconds);
            cmd.AddParam("task_id", taskId);
            cmd.AddParam("split_mode", NormalizeTaskSplitMode(splitMode));
            AddNullableParam(cmd, "operator_name", NullIfWhiteSpace(operatorName));
            AddNullableParam(cmd, "reason", NullIfWhiteSpace(reason));

            await using var reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false);
            if (!await reader.ReadAsync(token).ConfigureAwait(false))
            {
                throw new InvalidOperationException($"未能完成任务 {taskId} 的拆分");
            }

            return new MsfxInjectSplit(
                CreatedTasks: reader.GetInt32(0),
                TotalCodes: reader.GetInt32(1),
                SplitMode: reader.GetString(2));
        }, ct);
    }

    public Task<MsfxInjectSplitCustom> SplitInjectCustomAsync(long taskId, IReadOnlyList<string> groupKeys, IReadOnlyList<int> bucketIndexes, string? operatorName, string? reason, CancellationToken ct)
    {
        const string sql = """
            select
              result_created_tasks as created_tasks,
              result_total_codes as total_codes,
              result_bucket_count as bucket_count
            from msfx_split_inject_task_custom(@task_id, @group_keys, @bucket_indexes, @operator_name, @reason)
            """;

        return _db.WithConnection(async (conn, token) =>
        {
            var keys = groupKeys?
                .Select(x => (x ?? string.Empty).Trim())
                .Where(x => x.Length > 0)
                .ToArray() ?? Array.Empty<string>();
            var buckets = bucketIndexes?.ToArray() ?? Array.Empty<int>();
            if (keys.Length == 0 || keys.Length != buckets.Length)
            {
                throw new InvalidOperationException("自定义拆分参数无效");
            }

            await using var cmd = conn.CreateCommand(sql, _opt.CommandTimeoutSeconds);
            cmd.AddParam("task_id", taskId);
            cmd.AddParam("group_keys", keys);
            cmd.AddParam("bucket_indexes", buckets);
            AddNullableParam(cmd, "operator_name", NullIfWhiteSpace(operatorName));
            AddNullableParam(cmd, "reason", NullIfWhiteSpace(reason));

            await using var reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false);
            if (!await reader.ReadAsync(token).ConfigureAwait(false))
            {
                throw new InvalidOperationException($"未能完成任务 {taskId} 的自定义拆分");
            }

            return new MsfxInjectSplitCustom(
                CreatedTasks: reader.GetInt32(0),
                TotalCodes: reader.GetInt32(1),
                BucketCount: reader.GetInt32(2));
        }, ct);
    }

    public Task<IReadOnlyList<MsfxInjectSplitUnitRow>> GetInjectSplitUnitsAsync(long taskId, CancellationToken ct)
    {
        var sql = $"""
            with source_codes as (
              select
                tc.leaf_code,
                s.source_bill_code,
                coalesce(nullif(btrim(i.produce_batch_no), ''), '未提供批号') as batch_no,
                s.source_code_level_1,
                s.source_code_level_2,
                s.source_code_level_3,
                s.source_code_level_4,
                s.source_code_level_5,
                {MsfxInjectSql.ParentClusterKeyExpr} as parent_cluster_key
              from msfx_inject_task_code tc
              {MsfxInjectSql.TaskCodeStagingJoin}
              where tc.task_id = @task_id
            )
            select
              parent_cluster_key as group_key,
              parent_cluster_key,
              parent_cluster_key as display_cluster_code,
              max(nullif(btrim(source_code_level_1), '')) as code_level_1,
              max(nullif(btrim(source_code_level_2), '')) as code_level_2,
              max(nullif(btrim(source_code_level_3), '')) as code_level_3,
              max(nullif(btrim(source_code_level_4), '')) as code_level_4,
              max(nullif(btrim(source_code_level_5), '')) as code_level_5,
              coalesce(
                string_agg(
                  distinct nullif(btrim(coalesce(batch_no, '')), ''),
                  ' / ' order by nullif(btrim(coalesce(batch_no, '')), '')
                ),
                '未提供批号'
              ) as batch_no,
              coalesce(
                string_agg(
                  distinct nullif(btrim(coalesce(source_bill_code, '')), ''),
                  ' / ' order by nullif(btrim(coalesce(source_bill_code, '')), '')
                ),
                '--'
              ) as source_bill_codes,
              count(*)::int as code_count
            from source_codes
            group by parent_cluster_key
            order by parent_cluster_key
            """;

        return _db.WithConnection(async (conn, token) =>
        {
            await using var cmd = conn.CreateCommand(sql, _opt.CommandTimeoutSeconds);
            cmd.AddParam("task_id", taskId);
            await using var reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false);
            var rows = new List<MsfxInjectSplitUnitRow>();
            while (await reader.ReadAsync(token).ConfigureAwait(false))
            {
                rows.Add(new MsfxInjectSplitUnitRow(
                    GroupKey: reader.GetString(0),
                    ParentClusterKey: reader.GetString(1),
                    DisplayClusterCode: reader.GetString(2),
                    CodeLevel1: reader.IsDBNull(3) ? null : reader.GetString(3),
                    CodeLevel2: reader.IsDBNull(4) ? null : reader.GetString(4),
                    CodeLevel3: reader.IsDBNull(5) ? null : reader.GetString(5),
                    CodeLevel4: reader.IsDBNull(6) ? null : reader.GetString(6),
                    CodeLevel5: reader.IsDBNull(7) ? null : reader.GetString(7),
                    BatchNo: reader.GetString(8),
                    SourceBillCodes: reader.GetString(9),
                    CodeCount: reader.GetInt32(10)));
            }

            return (IReadOnlyList<MsfxInjectSplitUnitRow>)rows;
        }, ct);
    }

    public Task<IReadOnlyList<MsfxInjectSplitCodeRow>> GetInjectSplitCodeRowsAsync(long taskId, CancellationToken ct)
    {
        var sql = $"""
            select
              {MsfxInjectSql.ParentClusterKeyExpr} as group_key,
              {MsfxInjectSql.ParentClusterKeyExpr} as display_cluster_code,
              tc.leaf_code,
              nullif(btrim(coalesce(s.source_code_level_1, '')), '') as code_level_1,
              nullif(btrim(coalesce(s.source_code_level_2, '')), '') as code_level_2,
              nullif(btrim(coalesce(s.source_code_level_3, '')), '') as code_level_3,
              nullif(btrim(coalesce(s.source_code_level_4, '')), '') as code_level_4,
              nullif(btrim(coalesce(s.source_code_level_5, '')), '') as code_level_5,
              coalesce(nullif(btrim(i.produce_batch_no), ''), '未提供批号') as batch_no,
              coalesce(nullif(btrim(s.source_bill_code), ''), '--') as source_bill_code
            from msfx_inject_task_code tc
            {MsfxInjectSql.TaskCodeStagingJoin}
            where tc.task_id = @task_id
            order by
              {MsfxInjectSql.ParentClusterKeyExpr},
              tc.leaf_code
            """;

        return _db.WithConnection(async (conn, token) =>
        {
            await using var cmd = conn.CreateCommand(sql, _opt.CommandTimeoutSeconds);
            cmd.AddParam("task_id", taskId);
            await using var reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false);
            var rows = new List<MsfxInjectSplitCodeRow>();
            while (await reader.ReadAsync(token).ConfigureAwait(false))
            {
                rows.Add(new MsfxInjectSplitCodeRow(
                    GroupKey: reader.GetString(0),
                    DisplayClusterCode: reader.GetString(1),
                    LeafCode: reader.GetString(2),
                    CodeLevel1: reader.IsDBNull(3) ? null : reader.GetString(3),
                    CodeLevel2: reader.IsDBNull(4) ? null : reader.GetString(4),
                    CodeLevel3: reader.IsDBNull(5) ? null : reader.GetString(5),
                    CodeLevel4: reader.IsDBNull(6) ? null : reader.GetString(6),
                    CodeLevel5: reader.IsDBNull(7) ? null : reader.GetString(7),
                    BatchNo: reader.GetString(8),
                    SourceBillCode: reader.GetString(9)));
            }

            return (IReadOnlyList<MsfxInjectSplitCodeRow>)rows;
        }, ct);
    }

}
