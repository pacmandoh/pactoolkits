namespace PacToolkits.Infrastructure.Database;

internal static class MsfxInjectSql
{
    public const string TaskCodeStagingJoin = """
        join msfx_code_staging s on s.id = tc.staging_id
        left join msfx_code_relation r on r.id = s.source_relation_id
        left join msfx_upout_item i on i.id = r.upout_item_id
        """;

    public const string QueueStagingJoin = """
        left join msfx_inject_task_code tc on tc.task_id = t.id
        left join msfx_code_staging s on s.id = tc.staging_id
        left join msfx_code_relation r on r.id = s.source_relation_id
        left join msfx_upout_item i on i.id = r.upout_item_id
        """;

    public const string ParentClusterKeyExpr = """
        coalesce(
          nullif(btrim(coalesce(s.source_code_level_5, '')), ''),
          nullif(btrim(coalesce(s.source_code_level_4, '')), ''),
          nullif(btrim(coalesce(s.source_code_level_3, '')), ''),
          nullif(btrim(coalesce(s.source_code_level_2, '')), ''),
          nullif(btrim(coalesce(s.source_code_level_1, '')), ''),
          nullif(btrim(coalesce(tc.leaf_code, '')), '')
        )
        """;
}
