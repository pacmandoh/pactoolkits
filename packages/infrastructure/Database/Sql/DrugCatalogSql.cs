namespace PacToolkits.Infrastructure.Database;

/// <summary>
/// 药品目录 SQL 片段：活跃/弃用口径（note 含「弃用」）及存在性探测
/// 多个仓储共用同一套药品目录查询口径
/// </summary>
internal static class DrugCatalogSql
{
    public const string ActiveNotePredicate = "coalesce(d.note,'') not ilike '%弃用%'";

    public const string ActiveNotePredicateUnaliased = "coalesce(note,'') not ilike '%弃用%'";

    public const string DeprecatedMapCte = """
        deprecated_map as (
          select distinct d.drug_id, d.spec
          from drug_index d
          where coalesce(d.note,'') ilike '%弃用%'
        )
        """;

    public const string ActiveDrugCte = """
        active_drug as (
          select distinct d.drug_id, d.spec
          from drug_index d
          where coalesce(d.note,'') not ilike '%弃用%'
        )
        """;

    public const string DrugSpecExistsSql = """
        select exists(
          select 1 from drug_index d
          where d.drug_id = @drug_id
            and d.spec = @spec
        )
        """;
}
