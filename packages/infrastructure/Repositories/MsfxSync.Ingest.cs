using System.Text.Json;
using PacToolkits.Application.DTOs;
using PacToolkits.Infrastructure.Database;

namespace PacToolkits.Infrastructure.Repositories;

public sealed partial class MsfxSyncRepo
{
    public Task<long> UpsertInboundBillAsync(
        long batchId,
        string billCode,
        string billType,
        string billTime,
        string billUploadTime,
        string fromRefUserId,
        string fromEntName,
        string toRefUserId,
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
            var processedItems = 0;
            var newItems = 0;
            var processedCodes = 0;
            var newCodes = 0;

            foreach (var drug in drugs)
            {
                var rowKey = BuildSourceRowKey(billCode, drug.DrugName, drug.PackageSpec, drug.PrepnSpec, drug.BatchNo);
                var item = await UpsertDetailItemAsync(conn, tx, billId, rowKey, drug, token).ConfigureAwait(false);
                processedItems++;
                if (item.IsNew)
                {
                    newItems++;
                }

                var batch = await UpsertCodeRelationAndStagingBatchAsync(
                    conn,
                    tx,
                    item.Id,
                    billCode,
                    drug.DrugName,
                    drug.PrepnSpec,
                    drug.Codes,
                    token).ConfigureAwait(false);
                processedCodes += batch.ProcessedCount;
                newCodes += batch.NewCount;
            }

            return new MsfxIngestDetailResult(processedItems, newItems, processedCodes, newCodes);
        }, ct: ct);
    }

    private async Task<(long Id, bool IsNew)> UpsertDetailItemAsync(
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
            -- xmax=0 表示本语句插入了新行（Postgres upsert 区分 insert/update）
            returning id, (xmax = 0) as is_new
            """;

        await using var cmd = conn.CreateCommand(sql, _opt.CommandTimeoutSeconds, tx);
        cmd.AddParam("bill_id", billId);
        cmd.AddParam("source_row_key", sourceRowKey);
        AddNullableParam(cmd, "physic_name", NullIfWhiteSpace(drug.DrugName));
        // 同时写入 package_spec 和 pkg_spec，以兼容仍使用历史列名的数据结构
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

        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        if (!await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            return (0, false);
        }

        return (reader.GetInt64(0), reader.GetBoolean(1));
    }

    private async Task<(int ProcessedCount, int NewCount)> UpsertCodeRelationAndStagingBatchAsync(
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
        {
            return (0, 0);
        }

        var deduped = new Dictionary<string, (string? L1, string? L2, string? L3, string? L4, string? L5)>(StringComparer.Ordinal);
        foreach (var c in codes)
        {
            var leaf = NullIfWhiteSpace(c.Code);
            if (leaf is null)
            {
                continue;
            }

            if (deduped.ContainsKey(leaf))
            {
                continue;
            }

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
        {
            return (0, 0);
        }

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
        {
            return (0, 0);
        }

        return (reader.GetInt32(0), reader.GetInt32(1));
    }


}
