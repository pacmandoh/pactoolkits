using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Npgsql;
using PacToolkits.Application.DTOs;
using PacToolkits.Infrastructure.Database;

namespace PacToolkits.Infrastructure.Repositories;

/// <summary>
/// MSFX 仓储共享 SQL/归一化工具（partial）
/// </summary>
public sealed partial class MsfxSyncRepo
{
    private static void AddNullableParam<T>(NpgsqlCommand cmd, string name, T? value)
    {
        cmd.Parameters.AddWithValue(name, value is null ? DBNull.Value : (object)value);
    }

    private static string? NullIfWhiteSpace(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string? NormalizeOptional(string? value)
        => NullIfWhiteSpace(value);

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
