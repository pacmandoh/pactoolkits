namespace PacToolkits.Infrastructure.Database;

/// <summary>
/// 集中定义 MSFX 注入任务联查和父码聚类使用的 SQL 表达式
/// 集中维护 staging / relation / upout_item 联查路径
/// </summary>
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

    // 父码优先 L5 到 L1，缺失才用 leaf；改序会拆错簇
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
