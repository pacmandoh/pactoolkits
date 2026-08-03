using PacToolkits.Application.DTOs;
using PacToolkits.Infrastructure.Database;

namespace PacToolkits.Infrastructure.Repositories;

/// <summary>
/// 看板与拉取审计等只读查询
/// </summary>
public sealed partial class MsfxSyncRepo
{
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

}
