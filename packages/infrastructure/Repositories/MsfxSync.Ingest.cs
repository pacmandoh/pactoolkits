using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Npgsql;
using PacToolkits.Application.DTOs;
using PacToolkits.Infrastructure.Database;

namespace PacToolkits.Infrastructure.Repositories;

public sealed partial class MsfxSyncRepo
{
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
        // package_spec 与 pkg_spec 同写：兼容历史双列，勿删其一
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
        {
            return [];
        }

        return text.Split([' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    private static string BuildKeywordClause(string scope, IReadOnlyList<string> tokens, string[][]? pinyinExactPerToken)
    {
        if (tokens.Count == 0)
        {
            return string.Empty;
        }

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
        {
            var ilikePart = string.Format(CultureInfo.InvariantCulture, fieldExpr, $"@kw_{i}");
            var exactPart = BuildExactMatchClause(scope, $"@exact_{i}");
            var exactValues = pinyinExactPerToken is not null && i < pinyinExactPerToken.Length
                ? pinyinExactPerToken[i]
                : [];

            if (exactValues.Length > 0 && exactPart.Length > 0)
            {
                parts.Add($"({ilikePart} or {exactPart})");
            }
            else
            {
                parts.Add(ilikePart);
            }
        }

        return $"and ({string.Join(" and ", parts)})";
    }

    private static string BuildExactMatchClause(string scope, string paramName)
    {
        return scope switch
        {
            "BILL" => string.Empty,
            "TRACE" => string.Empty,
            "SOURCE_RAW" => $"(coalesce(s.source_drug_name_raw, '') = any({paramName}) or coalesce(s.source_spec_raw, '') = any({paramName}))",
            "SOURCE_NORM" => $"(coalesce(s.source_name_norm, '') = any({paramName}) or coalesce(s.source_spec_norm, '') = any({paramName}))",
            "LEVEL_CODE" => string.Empty,
            "REASON" => string.Empty,
            "TARGET" => $"(coalesce(s.mapped_drug_id, '') = any({paramName}) or coalesce(s.mapped_spec, '') = any({paramName}))",
            _ => $"(coalesce(s.source_drug_name_raw, '') = any({paramName}) or coalesce(s.source_spec_raw, '') = any({paramName}) or coalesce(s.source_name_norm, '') = any({paramName}) or coalesce(s.source_spec_norm, '') = any({paramName}) or coalesce(s.mapped_drug_id, '') = any({paramName}) or coalesce(s.mapped_spec, '') = any({paramName}))"
        };
    }

    private static void AddKeywordParams(NpgsqlCommand cmd, IReadOnlyList<string> tokens, string[][]? pinyinExactPerToken)
    {
        for (var i = 0; i < tokens.Count; i++)
        {
            cmd.AddParam($"kw_{i}", $"%{tokens[i]}%");

            var exactValues = pinyinExactPerToken is not null && i < pinyinExactPerToken.Length
                ? pinyinExactPerToken[i]
                : [];
            cmd.AddParam($"exact_{i}", exactValues);
        }
    }

    private async Task<MsfxMapApplyResult> ApplyMappingViaFunctionAsync(
        System.Data.IDbConnection conn,
        int limit,
        CancellationToken ct)
    {
        const string sql = "select processed_count, mapped_count, review_count from msfx_apply_mapping(@limit)";
        await using var cmd = conn.CreateCommand(
            sql,
            Math.Max(_opt.CommandTimeoutSeconds, MappingCommandTimeoutSeconds));
        cmd.AddParam("limit", limit);
        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        if (!await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            return new MsfxMapApplyResult(0, 0, 0);
        }

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
        {
            return new MsfxMappingBacklogDiagnostic(0, 0, 0, 0, 0, 0);
        }

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
        {
            return null;
        }

        if (DateTime.TryParse(value, out var dt))
        {
            return dt.Date;
        }

        return null;
    }

    private static DateTime? ParseDateTimeUtcOrNull(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (DateTimeOffset.TryParse(value, out var dto))
        {
            return dto.UtcDateTime;
        }

        if (DateTime.TryParse(value, out var d))
        {
            if (d.Kind == DateTimeKind.Unspecified)
            {
                d = DateTime.SpecifyKind(d, DateTimeKind.Local);
            }

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
        {
            return (l1, l2, l3, l4, l5);
        }

        if (c is null)
        {
            return (null, null, null, null, null);
        }

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
        {
            return 0;
        }

        return int.TryParse(levelText.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var n)
            ? n
            : 0;
    }

    private static string NormalizeTaskSplitMode(string? splitMode)
    {
        // 非 PARENT_CLUSTER 一律 BATCH（默认拆分策略）
        var raw = NormalizeOptional(splitMode)?.ToUpperInvariant();
        return raw == "PARENT_CLUSTER" ? "PARENT_CLUSTER" : "BATCH";
    }

    // 幂等键：字段组合变更会导致重复入库或丢行
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
