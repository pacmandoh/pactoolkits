using Npgsql;
using PacToolkits.Application.TextSearch;

namespace PacToolkits.Infrastructure.Database;

internal static class TracePoolKeywordSql
{
    public const string TracePoolWhereClause = """
        @kw = ''
        or t.drug_id    ilike ('%' || @kw || '%')
        or t.spec       ilike ('%' || @kw || '%')
        or t.trace_code ilike ('%' || @kw || '%')
        or (cardinality(@pinyin_drug_ids::text[]) > 0 and t.drug_id = any(@pinyin_drug_ids::text[]))
        or (cardinality(@pinyin_specs::text[]) > 0 and t.spec = any(@pinyin_specs::text[]))
        """;

    public const string TracePoolGroupedWhereClause = """
        @kw = ''
        or drug_id ilike ('%' || @kw || '%')
        or spec    ilike ('%' || @kw || '%')
        or (cardinality(@pinyin_drug_ids::text[]) > 0 and drug_id = any(@pinyin_drug_ids::text[]))
        or (cardinality(@pinyin_specs::text[]) > 0 and spec = any(@pinyin_specs::text[]))
        """;

    public const string TracePoolRequiredMatchClause = """
        t.drug_id    ilike ('%' || @kw || '%')
        or t.spec       ilike ('%' || @kw || '%')
        or t.trace_code ilike ('%' || @kw || '%')
        or (cardinality(@pinyin_drug_ids::text[]) > 0 and t.drug_id = any(@pinyin_drug_ids::text[]))
        or (cardinality(@pinyin_specs::text[]) > 0 and t.spec = any(@pinyin_specs::text[]))
        """;

    public const string DrugIndexAliasedWhereClause = """
        @kw = ''
        or d.drug_id ilike ('%' || @kw || '%')
        or d.spec    ilike ('%' || @kw || '%')
        or coalesce(d.note,'') ilike ('%' || @kw || '%')
        or (cardinality(@pinyin_drug_ids::text[]) > 0 and d.drug_id = any(@pinyin_drug_ids::text[]))
        or (cardinality(@pinyin_specs::text[]) > 0 and d.spec = any(@pinyin_specs::text[]))
        """;

    public static void AddKeywordParams(NpgsqlCommand cmd, KeywordSearchContext keyword)
    {
        cmd.AddParam("kw", keyword.Keyword);
        cmd.AddParam("pinyin_drug_ids", ToArray(keyword.PinyinDrugIds));
        cmd.AddParam("pinyin_specs", ToArray(keyword.PinyinSpecs));
    }

    private static string[] ToArray(IReadOnlyList<string> values)
        => values.Count == 0 ? [] : values as string[] ?? values.ToArray();
}
