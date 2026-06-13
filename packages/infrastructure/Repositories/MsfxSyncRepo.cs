using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using Npgsql;
using PacToolkits.Application.Abstractions;
using PacToolkits.Application.DTOs;
using PacToolkits.Infrastructure.Database;

namespace PacToolkits.Infrastructure.Repositories;

public sealed class MsfxSyncRepo : IMsfxSyncRepo
{
    private const string MapQueueBaseWhere = """
        (
          s.map_status in ('PENDING', 'NEED_REVIEW', 'FAILED')
          or (s.map_status = 'MAPPED' and s.code_status in ('NEW', 'TASKED', 'FAILED'))
        )
        and (@map_status::text is null or s.map_status = @map_status::text)
        and (@code_status::text is null or s.code_status = @code_status::text)
        """;

    private readonly IDb _db;
    private readonly PgOptions _opt;

    public MsfxSyncRepo(IDb db, IOptions<PgOptions> opt)
    {
        _db = db;
        _opt = opt.Value;
    }

    public Task<MsfxPullWindow> GetPullWindowAsync(string sourceApi, CancellationToken ct)
    {
        const string sql = "select begin_at, end_at from msfx_get_pull_window(@source_api)";
        return _db.WithConnection(async (conn, token) =>
        {
            await using var cmd = conn.CreateCommand(sql, _opt.CommandTimeoutSeconds);
            cmd.AddParam("source_api", sourceApi);
            await using var reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false);
            if (!await reader.ReadAsync(token).ConfigureAwait(false))
            {
                var now = DateTimeOffset.Now;
                return new MsfxPullWindow(now.AddDays(-7), now);
            }

            var begin = reader.GetFieldValue<DateTimeOffset>(0);
            var end = reader.GetFieldValue<DateTimeOffset>(1);
            return new MsfxPullWindow(begin, end);
        }, ct);
    }

    public Task<MsfxPullBatchStartResult> StartPullBatchAsync(
        string sourceApi,
        DateTimeOffset beginAt,
        DateTimeOffset endAt,
        CancellationToken ct)
    {
        const string sql = """
            insert into msfx_pull_batch(source_api, begin_date, end_date, status, started_at)
            values (@source_api, @begin_date, @end_date, 'RUNNING', clock_timestamp())
            returning id
            """;

        return _db.WithConnection(async (conn, token) =>
        {
            await using var cmd = conn.CreateCommand(sql, _opt.CommandTimeoutSeconds);
            cmd.AddParam("source_api", sourceApi);
            cmd.AddParam("begin_date", beginAt.Date);
            cmd.AddParam("end_date", endAt.Date);
            var idObj = await cmd.ExecuteScalarAsync(token).ConfigureAwait(false);
            var id = Convert.ToInt64(idObj ?? 0L);
            return new MsfxPullBatchStartResult(id);
        }, ct);
    }

    public Task FinishPullBatchAsync(
        long batchId,
        string status,
        int successCount,
        int failCount,
        string? errMsg,
        CancellationToken ct)
    {
        const string sql = """
            update msfx_pull_batch
            set status = @status,
                success_count = @success_count,
                fail_count = @fail_count,
                err_msg = @err_msg,
                finished_at = clock_timestamp()
            where id = @id
            """;

        return _db.WithConnection(async (conn, token) =>
        {
            await using var cmd = conn.CreateCommand(sql, _opt.CommandTimeoutSeconds);
            cmd.AddParam("id", batchId);
            cmd.AddParam("status", status);
            cmd.AddParam("success_count", successCount);
            cmd.AddParam("fail_count", failCount);
            AddNullableParam(cmd, "err_msg", errMsg);
            _ = await cmd.ExecuteNonQueryAsync(token).ConfigureAwait(false);
        }, ct);
    }

    public Task UpdatePullBatchRequestIdAsync(long batchId, string? requestId, CancellationToken ct)
    {
        const string sql = """
            update msfx_pull_batch
            set request_id = coalesce(request_id, @request_id)
            where id = @id
              and @request_id is not null
              and trim(@request_id) <> ''
            """;

        return _db.WithConnection(async (conn, token) =>
        {
            await using var cmd = conn.CreateCommand(sql, _opt.CommandTimeoutSeconds);
            cmd.AddParam("id", batchId);
            AddNullableParam(cmd, "request_id", NullIfWhiteSpace(requestId));
            _ = await cmd.ExecuteNonQueryAsync(token).ConfigureAwait(false);
        }, ct);
    }

    public Task AdvancePullCursorAsync(
        string sourceApi,
        DateTimeOffset beginAt,
        DateTimeOffset endAt,
        long batchId,
        string batchStatus,
        CancellationToken ct)
    {
        const string sql = "select msfx_advance_pull_cursor(@source_api, @begin_at, @end_at, @batch_id, @batch_status)";
        return _db.WithConnection(async (conn, token) =>
        {
            await using var cmd = conn.CreateCommand(sql, _opt.CommandTimeoutSeconds);
            cmd.AddParam("source_api", sourceApi);
            cmd.AddParam("begin_at", beginAt.UtcDateTime);
            cmd.AddParam("end_at", endAt.UtcDateTime);
            cmd.AddParam("batch_id", batchId);
            cmd.AddParam("batch_status", batchStatus);
            _ = await cmd.ExecuteScalarAsync(token).ConfigureAwait(false);
        }, ct);
    }

    public Task<IReadOnlyList<MsfxBillRetryRow>> GetDueBillRetriesAsync(
        string sourceApi,
        int limit,
        CancellationToken ct)
    {
        const string sql = """
            select
              r.bill_code,
              r.from_ref_user_id,
              r.to_ref_user_id,
              r.retry_count,
              r.next_retry_at,
              r.last_error
            from msfx_pull_bill_retry r
            where r.source_api = @source_api
              and r.next_retry_at <= clock_timestamp()
            order by r.next_retry_at, r.id
            limit @limit
            """;

        return _db.WithConnection(async (conn, token) =>
        {
            var rows = new List<MsfxBillRetryRow>();
            await using var cmd = conn.CreateCommand(sql, _opt.CommandTimeoutSeconds);
            cmd.AddParam("source_api", sourceApi);
            cmd.AddParam("limit", Math.Max(0, limit));
            await using var reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false);
            while (await reader.ReadAsync(token).ConfigureAwait(false))
            {
                rows.Add(new MsfxBillRetryRow(
                    BillCode: reader.GetString(0),
                    FromRefUserId: reader.IsDBNull(1) ? null : reader.GetString(1),
                    ToRefUserId: reader.IsDBNull(2) ? null : reader.GetString(2),
                    RetryCount: reader.IsDBNull(3) ? 0 : reader.GetInt32(3),
                    NextRetryAt: reader.GetFieldValue<DateTimeOffset>(4),
                    LastError: reader.IsDBNull(5) ? null : reader.GetString(5)));
            }

            return (IReadOnlyList<MsfxBillRetryRow>)rows;
        }, ct);
    }

    public Task UpsertBillRetryAsync(
        string sourceApi,
        string billCode,
        string? fromRefUserId,
        string? toRefUserId,
        string? lastError,
        CancellationToken ct)
    {
        const string sql = """
            insert into msfx_pull_bill_retry(
              source_api,
              bill_code,
              from_ref_user_id,
              to_ref_user_id,
              retry_count,
              next_retry_at,
              first_failed_at,
              last_failed_at,
              last_error,
              updated_at
            )
            values (
              @source_api,
              @bill_code,
              @from_ref_user_id,
              @to_ref_user_id,
              1,
              clock_timestamp() + interval '1 minute',
              clock_timestamp(),
              clock_timestamp(),
              @last_error,
              clock_timestamp()
            )
            on conflict (source_api, bill_code) do update
            set from_ref_user_id = coalesce(excluded.from_ref_user_id, msfx_pull_bill_retry.from_ref_user_id),
                to_ref_user_id = coalesce(excluded.to_ref_user_id, msfx_pull_bill_retry.to_ref_user_id),
                retry_count = msfx_pull_bill_retry.retry_count + 1,
                next_retry_at = clock_timestamp() + make_interval(mins => least(360, greatest(1, (power(2::numeric, least(msfx_pull_bill_retry.retry_count + 1, 8)) * 5)::int))),
                last_failed_at = clock_timestamp(),
                last_error = excluded.last_error,
                updated_at = clock_timestamp()
            """;

        return _db.WithConnection(async (conn, token) =>
        {
            await using var cmd = conn.CreateCommand(sql, _opt.CommandTimeoutSeconds);
            cmd.AddParam("source_api", sourceApi);
            cmd.AddParam("bill_code", billCode);
            AddNullableParam(cmd, "from_ref_user_id", NullIfWhiteSpace(fromRefUserId));
            AddNullableParam(cmd, "to_ref_user_id", NullIfWhiteSpace(toRefUserId));
            AddNullableParam(cmd, "last_error", NullIfWhiteSpace(lastError));
            _ = await cmd.ExecuteNonQueryAsync(token).ConfigureAwait(false);
        }, ct);
    }

    public Task MarkBillRetrySucceededAsync(
        string sourceApi,
        string billCode,
        CancellationToken ct)
    {
        const string sql = """
            delete from msfx_pull_bill_retry
            where source_api = @source_api
              and bill_code = @bill_code
            """;

        return _db.WithConnection(async (conn, token) =>
        {
            await using var cmd = conn.CreateCommand(sql, _opt.CommandTimeoutSeconds);
            cmd.AddParam("source_api", sourceApi);
            cmd.AddParam("bill_code", billCode);
            _ = await cmd.ExecuteNonQueryAsync(token).ConfigureAwait(false);
        }, ct);
    }

    public Task UpsertBillWatchAsync(
        string sourceApi,
        string billCode,
        string? fromRefUserId,
        string? toRefUserId,
        string? fromEntName,
        string? billType,
        string? billTime,
        string? billUploadTime,
        string? lastSeenStatus,
        string? rawJson,
        CancellationToken ct)
    {
        const string sql = """
            insert into msfx_pull_bill_watch(
              source_api,
              bill_code,
              from_ref_user_id,
              to_ref_user_id,
              from_ent_name,
              bill_type,
              bill_time,
              bill_upload_time,
              last_seen_status,
              raw_json,
              retry_count,
              next_check_at,
              state,
              first_seen_at,
              last_seen_at,
              resolved_at,
              last_error,
              updated_at
            )
            values (
              @source_api,
              @bill_code,
              @from_ref_user_id,
              @to_ref_user_id,
              @from_ent_name,
              @bill_type,
              @bill_time,
              @bill_upload_time,
              @last_seen_status,
              @raw_json::jsonb,
              0,
              clock_timestamp(),
              'WATCHING',
              clock_timestamp(),
              clock_timestamp(),
              null,
              null,
              clock_timestamp()
            )
            on conflict (source_api, bill_code) do update
            set from_ref_user_id = coalesce(excluded.from_ref_user_id, msfx_pull_bill_watch.from_ref_user_id),
                to_ref_user_id = coalesce(excluded.to_ref_user_id, msfx_pull_bill_watch.to_ref_user_id),
                from_ent_name = coalesce(excluded.from_ent_name, msfx_pull_bill_watch.from_ent_name),
                bill_type = coalesce(excluded.bill_type, msfx_pull_bill_watch.bill_type),
                bill_time = coalesce(excluded.bill_time, msfx_pull_bill_watch.bill_time),
                bill_upload_time = coalesce(excluded.bill_upload_time, msfx_pull_bill_watch.bill_upload_time),
                last_seen_status = coalesce(excluded.last_seen_status, msfx_pull_bill_watch.last_seen_status),
                raw_json = excluded.raw_json,
                next_check_at = clock_timestamp(),
                state = 'WATCHING',
                last_seen_at = clock_timestamp(),
                resolved_at = null,
                last_error = null,
                updated_at = clock_timestamp()
            """;

        return _db.WithConnection(async (conn, token) =>
        {
            await using var cmd = conn.CreateCommand(sql, _opt.CommandTimeoutSeconds);
            cmd.AddParam("source_api", sourceApi);
            cmd.AddParam("bill_code", billCode);
            AddNullableParam(cmd, "from_ref_user_id", NullIfWhiteSpace(fromRefUserId));
            AddNullableParam(cmd, "to_ref_user_id", NullIfWhiteSpace(toRefUserId));
            AddNullableParam(cmd, "from_ent_name", NullIfWhiteSpace(fromEntName));
            AddNullableParam(cmd, "bill_type", NullIfWhiteSpace(billType));
            AddNullableParam(cmd, "bill_time", NullIfWhiteSpace(billTime));
            AddNullableParam(cmd, "bill_upload_time", NullIfWhiteSpace(billUploadTime));
            AddNullableParam(cmd, "last_seen_status", NullIfWhiteSpace(lastSeenStatus));
            cmd.AddParam("raw_json", string.IsNullOrWhiteSpace(rawJson) ? "{}" : rawJson);
            _ = await cmd.ExecuteNonQueryAsync(token).ConfigureAwait(false);
        }, ct);
    }

    public Task<IReadOnlyList<MsfxBillWatchRow>> GetDueBillWatchesAsync(
        string sourceApi,
        int limit,
        CancellationToken ct)
    {
        const string sql = """
            select
              w.bill_code,
              w.from_ref_user_id,
              w.to_ref_user_id,
              w.from_ent_name,
              w.bill_type,
              w.bill_time,
              w.bill_upload_time,
              w.last_seen_status,
              w.raw_json::text,
              w.retry_count,
              w.next_check_at
            from msfx_pull_bill_watch w
            where w.source_api = @source_api
              and w.state = 'WATCHING'
              and w.next_check_at <= clock_timestamp()
            order by w.next_check_at, w.id
            limit @limit
            """;

        return _db.WithConnection(async (conn, token) =>
        {
            var rows = new List<MsfxBillWatchRow>();
            await using var cmd = conn.CreateCommand(sql, _opt.CommandTimeoutSeconds);
            cmd.AddParam("source_api", sourceApi);
            cmd.AddParam("limit", Math.Max(0, limit));
            await using var reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false);
            while (await reader.ReadAsync(token).ConfigureAwait(false))
            {
                rows.Add(new MsfxBillWatchRow(
                    BillCode: reader.GetString(0),
                    FromRefUserId: reader.IsDBNull(1) ? null : reader.GetString(1),
                    ToRefUserId: reader.IsDBNull(2) ? null : reader.GetString(2),
                    FromEntName: reader.IsDBNull(3) ? null : reader.GetString(3),
                    BillType: reader.IsDBNull(4) ? null : reader.GetString(4),
                    BillTime: reader.IsDBNull(5) ? null : reader.GetString(5),
                    BillUploadTime: reader.IsDBNull(6) ? null : reader.GetString(6),
                    LastSeenStatus: reader.IsDBNull(7) ? null : reader.GetString(7),
                    RawJson: reader.IsDBNull(8) ? null : reader.GetString(8),
                    RetryCount: reader.IsDBNull(9) ? 0 : reader.GetInt32(9),
                    NextCheckAt: reader.GetFieldValue<DateTimeOffset>(10)));
            }

            return (IReadOnlyList<MsfxBillWatchRow>)rows;
        }, ct);
    }

    public Task MarkBillWatchResolvedAsync(
        string sourceApi,
        string billCode,
        CancellationToken ct)
    {
        const string sql = """
            update msfx_pull_bill_watch
            set state = 'RESOLVED',
                resolved_at = clock_timestamp(),
                last_error = null,
                updated_at = clock_timestamp()
            where source_api = @source_api
              and bill_code = @bill_code
            """;

        return _db.WithConnection(async (conn, token) =>
        {
            await using var cmd = conn.CreateCommand(sql, _opt.CommandTimeoutSeconds);
            cmd.AddParam("source_api", sourceApi);
            cmd.AddParam("bill_code", billCode);
            _ = await cmd.ExecuteNonQueryAsync(token).ConfigureAwait(false);
        }, ct);
    }

    public Task RescheduleBillWatchAsync(
        string sourceApi,
        string billCode,
        string? lastSeenStatus,
        string? lastError,
        CancellationToken ct)
    {
        const string sql = """
            update msfx_pull_bill_watch
            set retry_count = msfx_pull_bill_watch.retry_count + 1,
                last_seen_status = coalesce(@last_seen_status, msfx_pull_bill_watch.last_seen_status),
                next_check_at = clock_timestamp() + make_interval(mins => least(360, greatest(5, (power(2::numeric, least(msfx_pull_bill_watch.retry_count, 6)) * 5)::int))),
                last_error = @last_error,
                last_seen_at = clock_timestamp(),
                updated_at = clock_timestamp()
            where source_api = @source_api
              and bill_code = @bill_code
            """;

        return _db.WithConnection(async (conn, token) =>
        {
            await using var cmd = conn.CreateCommand(sql, _opt.CommandTimeoutSeconds);
            cmd.AddParam("source_api", sourceApi);
            cmd.AddParam("bill_code", billCode);
            AddNullableParam(cmd, "last_seen_status", NullIfWhiteSpace(lastSeenStatus));
            AddNullableParam(cmd, "last_error", NullIfWhiteSpace(lastError));
            _ = await cmd.ExecuteNonQueryAsync(token).ConfigureAwait(false);
        }, ct);
    }

    public Task<long> UpsertInboundBillAsync(
        long batchId,
        string billCode,
        string billType,
        string billTime,
        string billUploadTime,
        string fromRefUserId,
        string fromEntName,
        string toRefUserId,
        string toUserId,
        string toUserName,
        string status,
        string rawJson,
        CancellationToken ct)
    {
        const string sql = """
            insert into msfx_upout_bill(
                pull_batch_id,
                bill_code,
                bill_type,
                bill_time,
                bill_time_format,
                bill_upload_time,
                from_ref_user_id,
                from_ent_name,
                to_ref_user_id,
                upout_status_raw,
                first_seen_at,
                last_seen_at,
                raw_json
            )
            values (
                @pull_batch_id,
                @bill_code,
                @bill_type,
                @bill_time,
                @bill_time_format,
                @bill_upload_time,
                @from_ref_user_id,
                @from_ent_name,
                @to_ref_user_id,
                @upout_status_raw,
                now(),
                now(),
                @raw_json::jsonb
            )
            on conflict (bill_code) do update
            set pull_batch_id = excluded.pull_batch_id,
                bill_type = excluded.bill_type,
                bill_time = excluded.bill_time,
                bill_time_format = excluded.bill_time_format,
                bill_upload_time = excluded.bill_upload_time,
                from_ref_user_id = excluded.from_ref_user_id,
                from_ent_name = excluded.from_ent_name,
                to_ref_user_id = excluded.to_ref_user_id,
                upout_status_raw = excluded.upout_status_raw,
                last_seen_at = now(),
                raw_json = excluded.raw_json
            returning id
            """;

        return _db.WithConnection(async (conn, token) =>
        {
            await using var cmd = conn.CreateCommand(sql, _opt.CommandTimeoutSeconds);
            cmd.AddParam("pull_batch_id", batchId);
            cmd.AddParam("bill_code", billCode);
            AddNullableParam(cmd, "bill_type", NullIfWhiteSpace(billType));
            AddNullableParam(cmd, "bill_time", ParseDateOrNull(billTime));
            AddNullableParam(cmd, "bill_time_format", ParseDateTimeUtcOrNull(billTime));
            AddNullableParam(cmd, "bill_upload_time", ParseDateTimeUtcOrNull(billUploadTime));
            AddNullableParam(cmd, "from_ref_user_id", NullIfWhiteSpace(fromRefUserId));
            AddNullableParam(cmd, "from_ent_name", NullIfWhiteSpace(fromEntName));
            AddNullableParam(cmd, "to_ref_user_id", NullIfWhiteSpace(toRefUserId));
            cmd.AddParam("upout_status_raw", status);
            cmd.AddParam("raw_json", string.IsNullOrWhiteSpace(rawJson) ? "{}" : rawJson);

            var idObj = await cmd.ExecuteScalarAsync(token).ConfigureAwait(false);
            return Convert.ToInt64(idObj ?? 0L);
        }, ct);
    }

    public Task<MsfxIngestDetailResult> IngestUpoutDetailAsync(
        long billId,
        string billCode,
        IReadOnlyList<(string DrugName, string PackageSpec, string PrepnSpec, string BatchNo, IReadOnlyList<(string Code, string CodeLevel, string? Level1Code, string? Level2Code, string? Level3Code, string? Level4Code, string? Level5Code)> Codes)> drugs,
        CancellationToken ct)
    {
        return _db.WithTransaction(async (conn, tx, token) =>
        {
            var insertedItems = 0;
            var insertedCodes = 0;
            var insertedStaging = 0;

            foreach (var drug in drugs)
            {
                var rowKey = BuildSourceRowKey(billCode, drug.DrugName, drug.PackageSpec, drug.PrepnSpec, drug.BatchNo);
                var itemId = await UpsertDetailItemAsync(conn, tx, billId, rowKey, drug, token).ConfigureAwait(false);
                if (itemId > 0)
                    insertedItems++;

                var batch = await UpsertCodeRelationAndStagingBatchAsync(
                    conn,
                    tx,
                    itemId,
                    billCode,
                    drug.DrugName,
                    drug.PrepnSpec,
                    drug.Codes,
                    token).ConfigureAwait(false);
                insertedCodes += batch.RelationCount;
                insertedStaging += batch.NewStagingCount;
            }

            return new MsfxIngestDetailResult(insertedItems, insertedCodes, insertedStaging);
        }, ct: ct);
    }

    public Task<MsfxMapApplyResult> ApplyMappingAsync(int limit, CancellationToken ct)
    {
        return _db.WithConnection(async (conn, token) =>
        {
            var safeLimit = Math.Max(0, limit);
            var pendingBefore = await GetPendingCountAsync(conn, token).ConfigureAwait(false);
            if (pendingBefore <= 0)
                return new MsfxMapApplyResult(0, 0, 0);

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
                return new MsfxMappingStatusSnapshot(0, 0, 0, 0, 0);

            return new MsfxMappingStatusSnapshot(
                PendingCount: reader.IsDBNull(0) ? 0 : reader.GetInt32(0),
                MappedCount: reader.IsDBNull(1) ? 0 : reader.GetInt32(1),
                NeedReviewCount: reader.IsDBNull(2) ? 0 : reader.GetInt32(2),
                FailedCount: reader.IsDBNull(3) ? 0 : reader.GetInt32(3),
                TotalCount: reader.IsDBNull(4) ? 0 : reader.GetInt32(4));
        }, ct);
    }

    public Task<MsfxMappingBacklogDiagnostic> GetMappingBacklogDiagnosticAsync(CancellationToken ct)
    {
        const string sql = """
            select
              sum(case when s.map_status = 'PENDING' then 1 else 0 end)::int as pending_count,
              sum(case when s.map_status = 'PENDING' and coalesce(nullif(s.source_drug_name_raw, ''), '') <> '' then 1 else 0 end)::int as pending_with_drug_raw_count,
              sum(case when s.map_status = 'PENDING' and coalesce(nullif(s.source_spec_raw, ''), '') <> '' then 1 else 0 end)::int as pending_with_spec_raw_count,
              sum(case when s.map_status = 'PENDING' and s.source_relation_id is not null then 1 else 0 end)::int as pending_with_relation_count,
              sum(case when s.map_status = 'PENDING' and coalesce(nullif(s.source_name_norm, ''), '') <> '' then 1 else 0 end)::int as pending_with_name_norm_count,
              sum(case when s.map_status = 'PENDING' and coalesce(nullif(s.source_spec_norm, ''), '') <> '' then 1 else 0 end)::int as pending_with_spec_norm_count
            from msfx_code_staging s
            """;

        return _db.WithConnection(async (conn, token) =>
        {
            await using var cmd = conn.CreateCommand(sql, _opt.CommandTimeoutSeconds);
            await using var reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false);
            if (!await reader.ReadAsync(token).ConfigureAwait(false))
                return new MsfxMappingBacklogDiagnostic(0, 0, 0, 0, 0, 0);

            return new MsfxMappingBacklogDiagnostic(
                PendingCount: reader.GetInt32(0),
                PendingWithDrugRawCount: reader.GetInt32(1),
                PendingWithSpecRawCount: reader.GetInt32(2),
                PendingWithRelationCount: reader.GetInt32(3),
                PendingWithNameNormCount: reader.GetInt32(4),
                PendingWithSpecNormCount: reader.GetInt32(5));
        }, ct);
    }

    public Task<MsfxBuildTaskResult> BuildInjectTasksAsync(int maxGroups, CancellationToken ct)
    {
        const string sql = "select created_tasks, tasked_codes from msfx_build_inject_tasks(@max_groups)";
        return _db.WithConnection(async (conn, token) =>
        {
            await using var cmd = conn.CreateCommand(sql, _opt.CommandTimeoutSeconds);
            cmd.AddParam("max_groups", Math.Max(0, maxGroups));
            await using var reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false);
            if (!await reader.ReadAsync(token).ConfigureAwait(false))
                return new MsfxBuildTaskResult(0, 0);

            return new MsfxBuildTaskResult(
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
        DateTimeOffset? cursorUpdatedAt,
        long? cursorId,
        bool newer,
        CancellationToken ct)
    {
        var scope = NormalizeSearchScope(searchScope);
        var tokens = SplitKeywords(keyword);
        var keywordClause = BuildKeywordClause(scope, tokens);
        var orderDir = newer ? "asc" : "desc";
        var cursorClause = cursorUpdatedAt.HasValue && cursorId.HasValue
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
                AddKeywordParams(listCmd, tokens);
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
            if (newer)
                pageRows.Reverse();

            await using var countCmd = conn.CreateCommand(countSql, _opt.CommandTimeoutSeconds);
            AddNullableParam(countCmd, "map_status", NormalizeOptional(mapStatus));
            AddNullableParam(countCmd, "code_status", NormalizeOptional(codeStatus));
            AddKeywordParams(countCmd, tokens);
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
                AddKeywordParams(newerCmd, tokens);
                newerCmd.AddParam("first_updated_at", first.UpdatedAt.ToUniversalTime());
                newerCmd.AddParam("first_id", first.StagingId);
                hasNewer = Convert.ToBoolean((await newerCmd.ExecuteScalarAsync(token).ConfigureAwait(false)) ?? false, CultureInfo.InvariantCulture);

                await using var olderCmd = conn.CreateCommand(hasOlderSql, _opt.CommandTimeoutSeconds);
                AddNullableParam(olderCmd, "map_status", NormalizeOptional(mapStatus));
                AddNullableParam(olderCmd, "code_status", NormalizeOptional(codeStatus));
                AddKeywordParams(olderCmd, tokens);
                olderCmd.AddParam("last_updated_at", last.UpdatedAt.ToUniversalTime());
                olderCmd.AddParam("last_id", last.StagingId);
                hasOlder = Convert.ToBoolean((await olderCmd.ExecuteScalarAsync(token).ConfigureAwait(false)) ?? false, CultureInfo.InvariantCulture);
            }

            return new MsfxMappingQueuePage(pageRows, totalCount, hasNewer, hasOlder);
        }, ct);
    }

    public Task<IReadOnlyList<MsfxInjectTaskQueueRow>> GetInjectTaskQueueAsync(int limit, CancellationToken ct)
    {
        const string sqlBody = """
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
            left join msfx_inject_task_code tc on tc.task_id = t.id
            left join msfx_code_staging s on s.id = tc.staging_id
            left join msfx_code_relation r on r.id = s.source_relation_id
            left join msfx_upout_item i on i.id = r.upout_item_id
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
                cmd.AddParam("limit", Math.Clamp(limit, 1, 5000));
            await using var reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false);
            var rows = new List<MsfxInjectTaskQueueRow>(Math.Max(1, useLimit ? limit : 256));
            while (await reader.ReadAsync(token).ConfigureAwait(false))
            {
                rows.Add(new MsfxInjectTaskQueueRow(
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

            return (IReadOnlyList<MsfxInjectTaskQueueRow>)rows;
        }, ct);
    }

    public Task<MsfxReopenInjectTaskResult> ReopenInjectTaskAsync(long taskId, string? operatorName, string? reason, CancellationToken ct)
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
                throw new InvalidOperationException($"未能重开任务 {taskId}");

            return new MsfxReopenInjectTaskResult(
                TaskId: reader.GetInt64(0),
                Status: reader.GetString(1),
                TotalCodes: reader.GetInt32(2));
        }, ct);
    }

    public Task<MsfxDiscardInjectTaskResult> DiscardInjectTaskAsync(long taskId, string? operatorName, string? reason, CancellationToken ct)
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
                throw new InvalidOperationException($"未能弃用任务 {taskId}");

            return new MsfxDiscardInjectTaskResult(
                TaskId: reader.GetInt64(0),
                Status: reader.GetString(1),
                TotalCodes: reader.GetInt32(2));
        }, ct);
    }

    public Task<MsfxRemapInjectTaskResult> RemapInjectTaskAsync(long taskId, string? operatorName, string? reason, CancellationToken ct)
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
                throw new InvalidOperationException($"未能回退任务 {taskId} 到映射队列");

            return new MsfxRemapInjectTaskResult(
                TaskId: reader.GetInt64(0),
                Status: reader.GetString(1),
                TotalCodes: reader.GetInt32(2),
                ResetStagingCount: reader.GetInt32(3));
        }, ct);
    }

    public Task<MsfxMergeInjectTaskResult> MergeInjectTasksAsync(IReadOnlyList<long> taskIds, string? operatorName, string? reason, CancellationToken ct)
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
                throw new InvalidOperationException("至少需要两条任务才能执行合并");

            await using var cmd = conn.CreateCommand(sql, _opt.CommandTimeoutSeconds);
            cmd.AddParam("task_ids", ids);
            AddNullableParam(cmd, "operator_name", NullIfWhiteSpace(operatorName));
            AddNullableParam(cmd, "reason", NullIfWhiteSpace(reason));

            await using var reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false);
            if (!await reader.ReadAsync(token).ConfigureAwait(false))
                throw new InvalidOperationException("未能完成任务合并");

            return new MsfxMergeInjectTaskResult(
                TaskId: reader.GetInt64(0),
                Status: reader.GetString(1),
                TotalCodes: reader.GetInt32(2),
                MergedTaskCount: reader.GetInt32(3));
        }, ct);
    }

    public Task<MsfxSplitInjectTaskResult> SplitInjectTaskAsync(long taskId, string splitMode, string? operatorName, string? reason, CancellationToken ct)
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
                throw new InvalidOperationException($"未能完成任务 {taskId} 的拆分");

            return new MsfxSplitInjectTaskResult(
                CreatedTasks: reader.GetInt32(0),
                TotalCodes: reader.GetInt32(1),
                SplitMode: reader.GetString(2));
        }, ct);
    }

    public Task<MsfxSplitInjectTaskCustomResult> SplitInjectTaskCustomAsync(long taskId, IReadOnlyList<string> groupKeys, IReadOnlyList<int> bucketIndexes, string? operatorName, string? reason, CancellationToken ct)
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
                throw new InvalidOperationException("自定义拆分参数无效");

            await using var cmd = conn.CreateCommand(sql, _opt.CommandTimeoutSeconds);
            cmd.AddParam("task_id", taskId);
            cmd.AddParam("group_keys", keys);
            cmd.AddParam("bucket_indexes", buckets);
            AddNullableParam(cmd, "operator_name", NullIfWhiteSpace(operatorName));
            AddNullableParam(cmd, "reason", NullIfWhiteSpace(reason));

            await using var reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false);
            if (!await reader.ReadAsync(token).ConfigureAwait(false))
                throw new InvalidOperationException($"未能完成任务 {taskId} 的自定义拆分");

            return new MsfxSplitInjectTaskCustomResult(
                CreatedTasks: reader.GetInt32(0),
                TotalCodes: reader.GetInt32(1),
                BucketCount: reader.GetInt32(2));
        }, ct);
    }

    public Task<IReadOnlyList<MsfxInjectTaskSplitUnitRow>> GetInjectTaskSplitUnitsAsync(long taskId, CancellationToken ct)
    {
        const string sql = """
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
                coalesce(
                  nullif(btrim(coalesce(s.source_code_level_5, '')), ''),
                  nullif(btrim(coalesce(s.source_code_level_4, '')), ''),
                  nullif(btrim(coalesce(s.source_code_level_3, '')), ''),
                  nullif(btrim(coalesce(s.source_code_level_2, '')), ''),
                  nullif(btrim(coalesce(s.source_code_level_1, '')), ''),
                  nullif(btrim(coalesce(tc.leaf_code, '')), '')
                ) as parent_cluster_key
              from msfx_inject_task_code tc
              join msfx_code_staging s on s.id = tc.staging_id
              left join msfx_code_relation r on r.id = s.source_relation_id
              left join msfx_upout_item i on i.id = r.upout_item_id
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
            var rows = new List<MsfxInjectTaskSplitUnitRow>();
            while (await reader.ReadAsync(token).ConfigureAwait(false))
            {
                rows.Add(new MsfxInjectTaskSplitUnitRow(
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

            return (IReadOnlyList<MsfxInjectTaskSplitUnitRow>)rows;
        }, ct);
    }

    public Task<IReadOnlyList<MsfxInjectTaskSplitCodeRow>> GetInjectTaskSplitCodeRowsAsync(long taskId, CancellationToken ct)
    {
        const string sql = """
            select
              coalesce(
                nullif(btrim(coalesce(s.source_code_level_5, '')), ''),
                nullif(btrim(coalesce(s.source_code_level_4, '')), ''),
                nullif(btrim(coalesce(s.source_code_level_3, '')), ''),
                nullif(btrim(coalesce(s.source_code_level_2, '')), ''),
                nullif(btrim(coalesce(s.source_code_level_1, '')), ''),
                nullif(btrim(coalesce(tc.leaf_code, '')), '')
              ) as group_key,
              coalesce(
                nullif(btrim(coalesce(s.source_code_level_5, '')), ''),
                nullif(btrim(coalesce(s.source_code_level_4, '')), ''),
                nullif(btrim(coalesce(s.source_code_level_3, '')), ''),
                nullif(btrim(coalesce(s.source_code_level_2, '')), ''),
                nullif(btrim(coalesce(s.source_code_level_1, '')), ''),
                nullif(btrim(coalesce(tc.leaf_code, '')), '')
              ) as display_cluster_code,
              tc.leaf_code,
              nullif(btrim(coalesce(s.source_code_level_1, '')), '') as code_level_1,
              nullif(btrim(coalesce(s.source_code_level_2, '')), '') as code_level_2,
              nullif(btrim(coalesce(s.source_code_level_3, '')), '') as code_level_3,
              nullif(btrim(coalesce(s.source_code_level_4, '')), '') as code_level_4,
              nullif(btrim(coalesce(s.source_code_level_5, '')), '') as code_level_5,
              coalesce(nullif(btrim(i.produce_batch_no), ''), '未提供批号') as batch_no,
              coalesce(nullif(btrim(s.source_bill_code), ''), '--') as source_bill_code
            from msfx_inject_task_code tc
            join msfx_code_staging s on s.id = tc.staging_id
            left join msfx_code_relation r on r.id = s.source_relation_id
            left join msfx_upout_item i on i.id = r.upout_item_id
            where tc.task_id = @task_id
            order by
              coalesce(
                nullif(btrim(coalesce(s.source_code_level_5, '')), ''),
                nullif(btrim(coalesce(s.source_code_level_4, '')), ''),
                nullif(btrim(coalesce(s.source_code_level_3, '')), ''),
                nullif(btrim(coalesce(s.source_code_level_2, '')), ''),
                nullif(btrim(coalesce(s.source_code_level_1, '')), ''),
                nullif(btrim(coalesce(tc.leaf_code, '')), '')
              ),
              tc.leaf_code
            """;

        return _db.WithConnection(async (conn, token) =>
        {
            await using var cmd = conn.CreateCommand(sql, _opt.CommandTimeoutSeconds);
            cmd.AddParam("task_id", taskId);
            await using var reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false);
            var rows = new List<MsfxInjectTaskSplitCodeRow>();
            while (await reader.ReadAsync(token).ConfigureAwait(false))
            {
                rows.Add(new MsfxInjectTaskSplitCodeRow(
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

            return (IReadOnlyList<MsfxInjectTaskSplitCodeRow>)rows;
        }, ct);
    }

    public Task<IReadOnlyList<MsfxMappingBatchGroupRow>> GetMappingBatchGroupsAsync(
        string? mapStatus,
        string? codeStatus,
        string? searchScope,
        string? keyword,
        int limit,
        CancellationToken ct)
    {
        var scope = NormalizeSearchScope(searchScope);
        var tokens = SplitKeywords(keyword);
        var keywordClause = BuildKeywordClause(scope, tokens);
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
            AddKeywordParams(cmd, tokens);
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
        var keywordClause = BuildKeywordClause(scope, tokens);
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
            if (actionNorm == "APPLY_MAP")
            {
                if (string.IsNullOrWhiteSpace(normalizedDrugId) || string.IsNullOrWhiteSpace(normalizedSpec))
                    return new MsfxMappingBatchPreview(0, 0, 0);

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
            AddKeywordParams(cmd, tokens);
            await using var reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false);
            if (!await reader.ReadAsync(token).ConfigureAwait(false))
                return new MsfxMappingBatchPreview(0, 0, 0);

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
        var keywordClause = BuildKeywordClause(scope, tokens);
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
                  set mapped_drug_id = @drug_id,
                      mapped_spec = @spec,
                      map_status = 'MAPPED',
                      map_reason_code = 'MANUAL_MAP_DISCARD',
                      map_reason_detail = 'manual map and discard task',
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
                select count(*)::int
                from upd_stage
                """,
            _ => $"""
                with target as (
                  select s.id
                  from msfx_code_staging s
                  where {MapQueueBaseWhere}
                    {keywordClause}
                    {groupClause}
                    and s.map_status in ('PENDING', 'FAILED', 'NEED_REVIEW')
                )
                update msfx_code_staging s
                set mapped_drug_id = @drug_id,
                    mapped_spec = @spec,
                    map_status = 'MAPPED',
                    map_reason_code = 'MANUAL_MAP',
                    map_reason_detail = null,
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
            AddKeywordParams(cmd, tokens);
            var normalizedDrugId = NormalizeOptional(drugId);
            var normalizedSpec = NormalizeOptional(spec);
            if (string.IsNullOrWhiteSpace(normalizedDrugId) || string.IsNullOrWhiteSpace(normalizedSpec))
                return new MsfxMappingBatchApplyResult(0);

            cmd.AddParam("drug_id", normalizedDrugId);
            cmd.AddParam("spec", normalizedSpec);

            if (actionNorm == "APPLY_DISCARD")
            {
                var affectedCount = Convert.ToInt32((await cmd.ExecuteScalarAsync(token).ConfigureAwait(false)) ?? 0, CultureInfo.InvariantCulture);
                return new MsfxMappingBatchApplyResult(affectedCount);
            }

            var affected = await cmd.ExecuteNonQueryAsync(token).ConfigureAwait(false);
            return new MsfxMappingBatchApplyResult(affected);
        }, ct);
    }

    private async Task<long> UpsertDetailItemAsync(
        System.Data.IDbConnection conn,
        System.Data.IDbTransaction tx,
        long billId,
        string sourceRowKey,
        (string DrugName, string PackageSpec, string PrepnSpec, string BatchNo, IReadOnlyList<(string Code, string CodeLevel, string? Level1Code, string? Level2Code, string? Level3Code, string? Level4Code, string? Level5Code)> Codes) drug,
        CancellationToken ct)
    {
        const string sql = """
            insert into msfx_upout_item(
                bill_id,
                source_row_key,
                physic_name,
                package_spec,
                pkg_spec,
                prepn_spec,
                produce_batch_no,
                code_count,
                raw_json
            )
            values (
                @bill_id,
                @source_row_key,
                @physic_name,
                @package_spec,
                @pkg_spec,
                @prepn_spec,
                @produce_batch_no,
                @code_count,
                @raw_json::jsonb
            )
            on conflict (bill_id, source_row_key) do update
            set physic_name = excluded.physic_name,
                package_spec = excluded.package_spec,
                pkg_spec = excluded.pkg_spec,
                prepn_spec = excluded.prepn_spec,
                produce_batch_no = excluded.produce_batch_no,
                code_count = excluded.code_count,
                raw_json = excluded.raw_json
            returning id
            """;

        await using var cmd = conn.CreateCommand(sql, _opt.CommandTimeoutSeconds, tx);
        cmd.AddParam("bill_id", billId);
        cmd.AddParam("source_row_key", sourceRowKey);
        AddNullableParam(cmd, "physic_name", NullIfWhiteSpace(drug.DrugName));
        AddNullableParam(cmd, "package_spec", NullIfWhiteSpace(drug.PackageSpec));
        AddNullableParam(cmd, "pkg_spec", NullIfWhiteSpace(drug.PackageSpec));
        AddNullableParam(cmd, "prepn_spec", NullIfWhiteSpace(drug.PrepnSpec));
        AddNullableParam(cmd, "produce_batch_no", NullIfWhiteSpace(drug.BatchNo));
        cmd.AddParam("code_count", drug.Codes.Count);
        cmd.AddParam("raw_json", JsonSerializer.Serialize(new
        {
            drug.DrugName,
            drug.PackageSpec,
            drug.PrepnSpec,
            drug.BatchNo,
            CodeCount = drug.Codes.Count
        }));

        var idObj = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
        return Convert.ToInt64(idObj ?? 0L);
    }

    private async Task<(int RelationCount, int NewStagingCount)> UpsertCodeRelationAndStagingBatchAsync(
        System.Data.IDbConnection conn,
        System.Data.IDbTransaction tx,
        long upoutItemId,
        string billCode,
        string drugName,
        string prepnSpec,
        IReadOnlyList<(string Code, string CodeLevel, string? Level1Code, string? Level2Code, string? Level3Code, string? Level4Code, string? Level5Code)> codes,
        CancellationToken ct)
    {
        if (codes.Count == 0)
            return (0, 0);

        var deduped = new Dictionary<string, (string? L1, string? L2, string? L3, string? L4, string? L5)>(StringComparer.Ordinal);
        foreach (var c in codes)
        {
            var leaf = NullIfWhiteSpace(c.Code);
            if (leaf is null)
                continue;

            if (deduped.ContainsKey(leaf))
                continue;

            deduped[leaf] = BuildCodeHierarchy(
                leaf,
                c.CodeLevel,
                c.Level1Code,
                c.Level2Code,
                c.Level3Code,
                c.Level4Code,
                c.Level5Code);
        }

        if (deduped.Count == 0)
            return (0, 0);

        var leafCodes = new string[deduped.Count];
        var level1Codes = new string?[deduped.Count];
        var level2Codes = new string?[deduped.Count];
        var level3Codes = new string?[deduped.Count];
        var level4Codes = new string?[deduped.Count];
        var level5Codes = new string?[deduped.Count];

        var i = 0;
        foreach (var kv in deduped)
        {
            leafCodes[i] = kv.Key;
            level1Codes[i] = kv.Value.L1;
            level2Codes[i] = kv.Value.L2;
            level3Codes[i] = kv.Value.L3;
            level4Codes[i] = kv.Value.L4;
            level5Codes[i] = kv.Value.L5;
            i++;
        }

        const string sql = """
            with src as (
              select distinct on (leaf_code)
                t.leaf_code,
                t.code_level_1,
                t.code_level_2,
                t.code_level_3,
                t.code_level_4,
                t.code_level_5
              from unnest(
                @leaf_codes::text[],
                @level1_codes::text[],
                @level2_codes::text[],
                @level3_codes::text[],
                @level4_codes::text[],
                @level5_codes::text[]
              ) as t(leaf_code, code_level_1, code_level_2, code_level_3, code_level_4, code_level_5)
              where t.leaf_code is not null and btrim(t.leaf_code) <> ''
            ),
            new_target as (
              select count(*)::int as cnt
              from src s
              left join msfx_code_staging st on st.leaf_code = s.leaf_code
              where st.leaf_code is null
            ),
            upsert_relation as (
              insert into msfx_code_relation(
                upout_item_id,
                source_type,
                leaf_code,
                code_level_1,
                code_level_2,
                code_level_3,
                code_level_4,
                code_level_5,
                prepn_spec,
                raw_json
              )
              select
                @upout_item_id,
                'UPOUT_DETAIL',
                s.leaf_code,
                s.code_level_1,
                s.code_level_2,
                s.code_level_3,
                s.code_level_4,
                s.code_level_5,
                @prepn_spec,
                jsonb_build_object('LeafCode', s.leaf_code, 'PrepnSpec', @prepn_spec)
              from src s
              on conflict (leaf_code) do update
              set upout_item_id = coalesce(msfx_code_relation.upout_item_id, excluded.upout_item_id),
                  source_type = excluded.source_type,
                  code_level_1 = coalesce(excluded.code_level_1, msfx_code_relation.code_level_1),
                  code_level_2 = coalesce(excluded.code_level_2, msfx_code_relation.code_level_2),
                  code_level_3 = coalesce(excluded.code_level_3, msfx_code_relation.code_level_3),
                  code_level_4 = coalesce(excluded.code_level_4, msfx_code_relation.code_level_4),
                  code_level_5 = coalesce(excluded.code_level_5, msfx_code_relation.code_level_5),
                  prepn_spec = coalesce(excluded.prepn_spec, msfx_code_relation.prepn_spec),
                  raw_json = excluded.raw_json
              returning id, leaf_code, code_level_1, code_level_2, code_level_3, code_level_4, code_level_5
            ),
            upsert_staging as (
              insert into msfx_code_staging(
                leaf_code,
                source_relation_id,
                source_bill_code,
                source_drug_name_raw,
                source_spec_raw,
                source_code_level_1,
                source_code_level_2,
                source_code_level_3,
                source_code_level_4,
                source_code_level_5,
                map_status,
                code_status
              )
              select
                ur.leaf_code,
                ur.id,
                @source_bill_code,
                @source_drug_name_raw,
                @source_spec_raw,
                ur.code_level_1,
                ur.code_level_2,
                ur.code_level_3,
                ur.code_level_4,
                ur.code_level_5,
                'PENDING',
                'NEW'
              from upsert_relation ur
              on conflict (leaf_code) do update
              set source_relation_id = excluded.source_relation_id,
                  source_bill_code = excluded.source_bill_code,
                  source_drug_name_raw = excluded.source_drug_name_raw,
                  source_spec_raw = excluded.source_spec_raw,
                  source_code_level_1 = coalesce(excluded.source_code_level_1, msfx_code_staging.source_code_level_1),
                  source_code_level_2 = coalesce(excluded.source_code_level_2, msfx_code_staging.source_code_level_2),
                  source_code_level_3 = coalesce(excluded.source_code_level_3, msfx_code_staging.source_code_level_3),
                  source_code_level_4 = coalesce(excluded.source_code_level_4, msfx_code_staging.source_code_level_4),
                  source_code_level_5 = coalesce(excluded.source_code_level_5, msfx_code_staging.source_code_level_5),
                  updated_at = now()
              returning 1
            )
            select
              coalesce((select count(*)::int from upsert_relation), 0) as relation_count,
              coalesce((select cnt from new_target), 0) as new_staging_count
            """;

        await using var cmd = conn.CreateCommand(sql, _opt.CommandTimeoutSeconds, tx);
        cmd.AddParam("upout_item_id", upoutItemId);
        AddNullableParam(cmd, "source_bill_code", NullIfWhiteSpace(billCode));
        AddNullableParam(cmd, "source_drug_name_raw", NullIfWhiteSpace(drugName));
        AddNullableParam(cmd, "source_spec_raw", NullIfWhiteSpace(prepnSpec));
        AddNullableParam(cmd, "prepn_spec", NullIfWhiteSpace(prepnSpec));
        cmd.Parameters.AddWithValue("leaf_codes", leafCodes);
        cmd.Parameters.AddWithValue("level1_codes", level1Codes);
        cmd.Parameters.AddWithValue("level2_codes", level2Codes);
        cmd.Parameters.AddWithValue("level3_codes", level3Codes);
        cmd.Parameters.AddWithValue("level4_codes", level4Codes);
        cmd.Parameters.AddWithValue("level5_codes", level5Codes);

        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        if (!await reader.ReadAsync(ct).ConfigureAwait(false))
            return (0, 0);

        return (reader.GetInt32(0), reader.GetInt32(1));
    }

    private async Task<long> UpsertCodeRelationAsync(
        System.Data.IDbConnection conn,
        System.Data.IDbTransaction tx,
        long upoutItemId,
        string leafCode,
        string codeLevel,
        string? level1Code,
        string? level2Code,
        string? level3Code,
        string? level4Code,
        string? level5Code,
        string prepnSpec,
        CancellationToken ct)
    {
        var hierarchy = BuildCodeHierarchy(leafCode, codeLevel, level1Code, level2Code, level3Code, level4Code, level5Code);
        const string insertSql = """
            insert into msfx_code_relation(
                upout_item_id,
                source_type,
                leaf_code,
                code_level_1,
                code_level_2,
                code_level_3,
                code_level_4,
                code_level_5,
                prepn_spec,
                raw_json
            )
            values (
                @upout_item_id,
                'UPOUT_DETAIL',
                @leaf_code,
                @code_level_1,
                @code_level_2,
                @code_level_3,
                @code_level_4,
                @code_level_5,
                @prepn_spec,
                @raw_json::jsonb
            )
            on conflict (leaf_code) do update
            set upout_item_id = coalesce(msfx_code_relation.upout_item_id, excluded.upout_item_id),
                source_type = excluded.source_type,
                code_level_1 = coalesce(excluded.code_level_1, msfx_code_relation.code_level_1),
                code_level_2 = coalesce(excluded.code_level_2, msfx_code_relation.code_level_2),
                code_level_3 = coalesce(excluded.code_level_3, msfx_code_relation.code_level_3),
                code_level_4 = coalesce(excluded.code_level_4, msfx_code_relation.code_level_4),
                code_level_5 = coalesce(excluded.code_level_5, msfx_code_relation.code_level_5),
                prepn_spec = coalesce(excluded.prepn_spec, msfx_code_relation.prepn_spec),
                raw_json = excluded.raw_json
            returning id
            """;

        await using var cmd = conn.CreateCommand(insertSql, _opt.CommandTimeoutSeconds, tx);
        cmd.AddParam("upout_item_id", upoutItemId);
        cmd.AddParam("leaf_code", leafCode);
        AddNullableParam(cmd, "code_level_1", hierarchy.L1);
        AddNullableParam(cmd, "code_level_2", hierarchy.L2);
        AddNullableParam(cmd, "code_level_3", hierarchy.L3);
        AddNullableParam(cmd, "code_level_4", hierarchy.L4);
        AddNullableParam(cmd, "code_level_5", hierarchy.L5);
        AddNullableParam(cmd, "prepn_spec", NullIfWhiteSpace(prepnSpec));
        cmd.AddParam("raw_json", JsonSerializer.Serialize(new { LeafCode = leafCode, CodeLevel = codeLevel, PrepnSpec = prepnSpec }));

        var idObj = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
        return Convert.ToInt64(idObj ?? 0L);
    }

    private async Task<bool> UpsertStagingAsync(
        System.Data.IDbConnection conn,
        System.Data.IDbTransaction tx,
        long sourceRelationId,
        string billCode,
        string drugName,
        string prepnSpec,
        string leafCode,
        CancellationToken ct)
    {
        const string sql = """
            insert into msfx_code_staging(
                leaf_code,
                source_relation_id,
                source_bill_code,
                source_drug_name_raw,
                source_spec_raw,
                source_code_level_1,
                source_code_level_2,
                source_code_level_3,
                source_code_level_4,
                source_code_level_5,
                map_status,
                code_status
            )
            select
                @leaf_code,
                @source_relation_id,
                @source_bill_code,
                @source_drug_name_raw,
                @source_spec_raw,
                r.code_level_1,
                r.code_level_2,
                r.code_level_3,
                r.code_level_4,
                r.code_level_5,
                'PENDING',
                'NEW'
            from msfx_code_relation r
            where r.id = @source_relation_id
            on conflict (leaf_code) do update
            set source_relation_id = excluded.source_relation_id,
                source_bill_code = excluded.source_bill_code,
                source_drug_name_raw = excluded.source_drug_name_raw,
                source_spec_raw = excluded.source_spec_raw,
                source_code_level_1 = coalesce(excluded.source_code_level_1, msfx_code_staging.source_code_level_1),
                source_code_level_2 = coalesce(excluded.source_code_level_2, msfx_code_staging.source_code_level_2),
                source_code_level_3 = coalesce(excluded.source_code_level_3, msfx_code_staging.source_code_level_3),
                source_code_level_4 = coalesce(excluded.source_code_level_4, msfx_code_staging.source_code_level_4),
                source_code_level_5 = coalesce(excluded.source_code_level_5, msfx_code_staging.source_code_level_5),
                updated_at = now()
            returning id
            """;

        await using var cmd = conn.CreateCommand(sql, _opt.CommandTimeoutSeconds, tx);
        cmd.AddParam("leaf_code", leafCode);
        cmd.AddParam("source_relation_id", sourceRelationId);
        AddNullableParam(cmd, "source_bill_code", NullIfWhiteSpace(billCode));
        AddNullableParam(cmd, "source_drug_name_raw", NullIfWhiteSpace(drugName));
        AddNullableParam(cmd, "source_spec_raw", NullIfWhiteSpace(prepnSpec));

        var idObj = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
        return idObj is not null && idObj != DBNull.Value;
    }

    private static void AddNullableParam<T>(NpgsqlCommand cmd, string name, T? value)
    {
        cmd.Parameters.AddWithValue(name, value is null ? DBNull.Value : (object)value);
    }

    private static string? NullIfWhiteSpace(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string? NormalizeOptional(string? value)
    {
        var text = (value ?? string.Empty).Trim();
        return text.Length == 0 ? null : text;
    }

    private static string NormalizeGroupKey(string? value)
        => (value ?? string.Empty).Trim();

    private static string NormalizeSearchScope(string? scope)
    {
        var text = (scope ?? string.Empty).Trim().ToUpperInvariant();
        return text.Length == 0 ? "ALL" : text;
    }

    private static string[] SplitKeywords(string? keyword)
    {
        var text = (keyword ?? string.Empty).Trim();
        if (text.Length == 0)
            return [];
        return text.Split([' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    private static string BuildKeywordClause(string scope, IReadOnlyList<string> tokens)
    {
        if (tokens.Count == 0)
            return string.Empty;

        var fieldExpr = scope switch
        {
            "BILL" => "coalesce(s.source_bill_code, '') ilike {0}",
            "TRACE" => "s.leaf_code ilike {0}",
            "SOURCE_RAW" => "(coalesce(s.source_drug_name_raw, '') ilike {0} or coalesce(s.source_spec_raw, '') ilike {0})",
            "SOURCE_NORM" => "(coalesce(s.source_name_norm, '') ilike {0} or coalesce(s.source_spec_norm, '') ilike {0})",
            "LEVEL_CODE" => "(coalesce(s.source_code_level_1, '') ilike {0} or coalesce(s.source_code_level_2, '') ilike {0} or coalesce(s.source_code_level_3, '') ilike {0} or coalesce(s.source_code_level_4, '') ilike {0} or coalesce(s.source_code_level_5, '') ilike {0})",
            "REASON" => "(coalesce(s.map_reason_code, '') ilike {0} or coalesce(s.map_reason_detail, '') ilike {0})",
            "TARGET" => "(coalesce(s.mapped_drug_id, '') ilike {0} or coalesce(s.mapped_spec, '') ilike {0})",
            _ => "(s.leaf_code ilike {0} or coalesce(s.source_bill_code, '') ilike {0} or coalesce(s.source_drug_name_raw, '') ilike {0} or coalesce(s.source_spec_raw, '') ilike {0} or coalesce(s.source_name_norm, '') ilike {0} or coalesce(s.source_spec_norm, '') ilike {0} or coalesce(s.source_code_level_1, '') ilike {0} or coalesce(s.source_code_level_2, '') ilike {0} or coalesce(s.source_code_level_3, '') ilike {0} or coalesce(s.source_code_level_4, '') ilike {0} or coalesce(s.source_code_level_5, '') ilike {0} or coalesce(s.map_reason_code, '') ilike {0} or coalesce(s.map_reason_detail, '') ilike {0} or coalesce(s.mapped_drug_id, '') ilike {0} or coalesce(s.mapped_spec, '') ilike {0})"
        };

        var parts = new List<string>(tokens.Count);
        for (var i = 0; i < tokens.Count; i++)
            parts.Add(string.Format(CultureInfo.InvariantCulture, fieldExpr, $"@kw_{i}"));

        return $"and ({string.Join(" and ", parts)})";
    }

    private static void AddKeywordParams(NpgsqlCommand cmd, IReadOnlyList<string> tokens)
    {
        for (var i = 0; i < tokens.Count; i++)
            cmd.AddParam($"kw_{i}", $"%{tokens[i]}%");
    }

    private async Task<int> GetPendingCountAsync(System.Data.IDbConnection conn, CancellationToken ct)
    {
        const string sql = "select count(*)::int from msfx_code_staging where map_status = 'PENDING'";
        await using var cmd = conn.CreateCommand(sql, _opt.CommandTimeoutSeconds);
        var obj = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
        return Convert.ToInt32(obj ?? 0, CultureInfo.InvariantCulture);
    }

    private async Task<MsfxMapApplyResult> ApplyMappingViaFunctionAsync(
        System.Data.IDbConnection conn,
        int limit,
        CancellationToken ct)
    {
        const string sql = "select processed_count, mapped_count, review_count from msfx_apply_mapping(@limit)";
        await using var cmd = conn.CreateCommand(sql, _opt.CommandTimeoutSeconds);
        cmd.AddParam("limit", limit);
        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        if (!await reader.ReadAsync(ct).ConfigureAwait(false))
            return new MsfxMapApplyResult(0, 0, 0);

        return new MsfxMapApplyResult(
            reader.GetInt32(0),
            reader.GetInt32(1),
            reader.GetInt32(2));
    }

    private async Task<MsfxMappingBacklogDiagnostic> GetMappingBacklogDiagnosticByConnectionAsync(
        System.Data.IDbConnection conn,
        CancellationToken ct)
    {
        const string sql = """
            select
              sum(case when s.map_status = 'PENDING' then 1 else 0 end)::int as pending_count,
              sum(case when s.map_status = 'PENDING' and coalesce(nullif(s.source_drug_name_raw, ''), '') <> '' then 1 else 0 end)::int as pending_with_drug_raw_count,
              sum(case when s.map_status = 'PENDING' and coalesce(nullif(s.source_spec_raw, ''), '') <> '' then 1 else 0 end)::int as pending_with_spec_raw_count,
              sum(case when s.map_status = 'PENDING' and s.source_relation_id is not null then 1 else 0 end)::int as pending_with_relation_count,
              sum(case when s.map_status = 'PENDING' and coalesce(nullif(s.source_name_norm, ''), '') <> '' then 1 else 0 end)::int as pending_with_name_norm_count,
              sum(case when s.map_status = 'PENDING' and coalesce(nullif(s.source_spec_norm, ''), '') <> '' then 1 else 0 end)::int as pending_with_spec_norm_count
            from msfx_code_staging s
            """;

        await using var cmd = conn.CreateCommand(sql, _opt.CommandTimeoutSeconds);
        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        if (!await reader.ReadAsync(ct).ConfigureAwait(false))
            return new MsfxMappingBacklogDiagnostic(0, 0, 0, 0, 0, 0);

        return new MsfxMappingBacklogDiagnostic(
            PendingCount: reader.GetInt32(0),
            PendingWithDrugRawCount: reader.GetInt32(1),
            PendingWithSpecRawCount: reader.GetInt32(2),
            PendingWithRelationCount: reader.GetInt32(3),
            PendingWithNameNormCount: reader.GetInt32(4),
            PendingWithSpecNormCount: reader.GetInt32(5));
    }

    private static DateTime? ParseDateOrNull(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        if (DateTime.TryParse(value, out var dt))
            return dt.Date;
        return null;
    }

    private static DateTime? ParseDateTimeUtcOrNull(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        if (DateTimeOffset.TryParse(value, out var dto))
            return dto.UtcDateTime;
        if (DateTime.TryParse(value, out var d))
        {
            if (d.Kind == DateTimeKind.Unspecified)
                d = DateTime.SpecifyKind(d, DateTimeKind.Local);
            return d.ToUniversalTime();
        }
        return null;
    }

    private static (string? L1, string? L2, string? L3, string? L4, string? L5) BuildCodeHierarchy(
        string code,
        string? codeLevel,
        string? level1Code,
        string? level2Code,
        string? level3Code,
        string? level4Code,
        string? level5Code)
    {
        var l1 = NullIfWhiteSpace(level1Code);
        var l2 = NullIfWhiteSpace(level2Code);
        var l3 = NullIfWhiteSpace(level3Code);
        var l4 = NullIfWhiteSpace(level4Code);
        var l5 = NullIfWhiteSpace(level5Code);
        var c = NullIfWhiteSpace(code);
        var level = ParseLevelInt(codeLevel);

        if (l1 is not null || l2 is not null || l3 is not null || l4 is not null || l5 is not null)
            return (l1, l2, l3, l4, l5);

        if (c is null)
            return (null, null, null, null, null);

        return level switch
        {
            2 => (null, c, null, null, null),
            3 => (null, null, c, null, null),
            4 => (null, null, null, c, null),
            5 => (null, null, null, null, c),
            _ => (c, null, null, null, null)
        };
    }

    private static int ParseLevelInt(string? levelText)
    {
        if (string.IsNullOrWhiteSpace(levelText))
            return 0;
        return int.TryParse(levelText.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var n)
            ? n
            : 0;
    }

    private static string NormalizeTaskSplitMode(string? splitMode)
    {
        var raw = NormalizeOptional(splitMode)?.ToUpperInvariant();
        return raw == "PARENT_CLUSTER" ? "PARENT_CLUSTER" : "BATCH";
    }

    private static string BuildSourceRowKey(
        string billCode,
        string drugName,
        string packageSpec,
        string prepnSpec,
        string batchNo)
    {
        var raw = string.Join("|", [
            billCode.Trim(),
            (drugName ?? string.Empty).Trim(),
            (packageSpec ?? string.Empty).Trim(),
            (prepnSpec ?? string.Empty).Trim(),
            (batchNo ?? string.Empty).Trim()
        ]);

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(raw));
        return Convert.ToHexString(hash);
    }
}
