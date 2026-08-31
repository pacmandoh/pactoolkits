namespace PacToolkits.Infrastructure.Database;

/// <summary>
/// 近 7 日（含当日）已提交流水用量 CTE
///
/// 窗口锚点取 <c>trace_txn</c> 最新 created_at，而非 wall-clock
/// 用量按 created_at 半开区间过滤才能走索引
/// </summary>
internal static class WeekUsageSql
{
    public const string WeekRangeCte = """
        wk_range as (
          select
            (coalesce(max(created_at)::date, current_date)) as wk_to,
            (coalesce(max(created_at)::date, current_date) - 6) as wk_from
          from trace_txn
          where status='COMMITTED'
        )
        """;

    public const string WeekUsageCte = """
        wk as (
          select
            t.drug_id,
            t.spec,
            coalesce(sum(t.req_qty),0)::bigint as wk_used
          from trace_txn t
          cross join wk_range r
          where t.status='COMMITTED'
            and t.created_at >= r.wk_from
            and t.created_at < (r.wk_to + 1)
          group by t.drug_id, t.spec
        )
        """;

    public const string WeekUsageFilteredCte = """
        wk as (
          select
            t.drug_id,
            coalesce(t.spec,'') as spec,
            coalesce(sum(t.req_qty),0)::bigint as wk_used
          from trace_txn t
          cross join wk_range r
          where t.status = 'COMMITTED'
            and t.created_at >= r.wk_from
            and t.created_at < (r.wk_to + 1)
            and (coalesce(@drug,'') = '' or btrim(t.drug_id) = btrim(@drug))
            and (coalesce(@spec,'') = '' or btrim(coalesce(t.spec,'')) = btrim(@spec))
          group by t.drug_id, coalesce(t.spec,'')
        )
        """;
}
