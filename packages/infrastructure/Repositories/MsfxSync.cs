using Microsoft.Extensions.Options;
using PacToolkits.Application.Abstractions;
using PacToolkits.Application.DTOs;
using PacToolkits.Infrastructure.Database;

namespace PacToolkits.Infrastructure.Repositories;

public sealed partial class MsfxSyncRepo : IMsfxSyncRepo
{
    private const string MapQueueBaseWhere = """
        (
          s.map_status in ('PENDING', 'NEED_REVIEW', 'FAILED')
          or (s.map_status = 'MAPPED' and s.code_status in ('NEW', 'TASKED', 'FAILED'))
        )
        and (@map_status::text is null or s.map_status = @map_status::text)
        and (@code_status::text is null or s.code_status = @code_status::text)
        """;

    private const string ManualMapNormBackfillSetClause = """
        source_name_norm = coalesce(
          nullif(trim(s.source_name_norm), ''),
          nullif(trim(@g_source_name_norm), ''),
          nullif(trim(msfx_norm_name(s.source_drug_name_raw)), ''),
          nullif(trim(msfx_norm_name(@drug_id)), ''),
          '-'),
        source_spec_norm = coalesce(
          nullif(trim(s.source_spec_norm), ''),
          nullif(trim(@g_source_spec_norm), ''),
          nullif(trim(msfx_norm_spec(s.source_spec_raw, (
            select i.pkg_spec
            from msfx_code_relation r
            join msfx_upout_item i on i.id = r.upout_item_id
            where r.id = s.source_relation_id
            limit 1
          ))), ''),
          nullif(trim(msfx_norm_spec(@spec, null)), ''),
          '-'),
        mapped_drug_id = coalesce(nullif(trim(@drug_id), ''), s.mapped_drug_id),
        mapped_spec = coalesce(nullif(trim(@spec), ''), s.mapped_spec),
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
}
