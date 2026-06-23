using Npgsql;
using PacToolkits.Application.Abstractions;
using PacToolkits.Application.DTOs;
using PacToolkits.Infrastructure.Database;

namespace PacToolkits.Infrastructure.Repositories;

public sealed class DashboardRepo : IDashboardRepo
{
    private readonly IDb _db;
    private readonly PgOptions _opt;
    private readonly IClientAliasService _alias;

    public DashboardRepo(IDb db, Microsoft.Extensions.Options.IOptions<PgOptions> opt, IClientAliasService alias)
    {
        _db = db;
        _opt = opt.Value;
        _alias = alias;
    }

    public Task<IReadOnlyList<string>> GetClientNamesAsync(CancellationToken ct)
        => _db.WithConnection(async (conn, token) =>
        {
            const string sql = """
                                   with clients as (
                                     select coalesce(nullif(btrim(split_part(client_id,'|',1)),''), btrim(client_id)) as client_machine
                                     from trace_txn
                                     where client_id is not null and btrim(client_id) <> ''
                                     union
                                     select coalesce(nullif(btrim(split_part(client_id,'|',1)),''), btrim(client_id)) as client_machine
                                     from trace_entry_log
                                     where client_id is not null and btrim(client_id) <> ''
                                   )
                                   select distinct client_machine
                                   from clients
                                   where client_machine is not null and btrim(client_machine) <> ''
                                   order by client_machine
                                   limit 200
                               """;

            await using var cmd = conn.CreateCommand(sql, _opt.CommandTimeoutSeconds);
            var list = new List<string>();
            await using var reader = await cmd.ExecuteReaderAsync(token);
            while (await reader.ReadAsync(token))
            {
                list.Add(reader.GetString(0));
            }

            return (IReadOnlyList<string>)list;
        }, ct);

    public Task<IReadOnlyList<(string Client, long Value)>> GetClientsAsync(DashboardQuery q, CancellationToken ct)
        => _db.WithConnection(async (conn, token) =>
        {
            var sql = $"""
                                   with base as (
                                     select
                                       t.client_id as client_raw,
                                       {ClientMachineExpr("t")} as client_machine,
                                       t.req_qty,
                                       t.created_at
                                     from trace_txn t
                                     where t.status = 'COMMITTED'
                                       and t.created_at::date between @from and @to
                                       and (coalesce(@client,'') = '' or {ClientMachineExpr("t")} = @client)
                                       and (coalesce(@drug,'') = '' or btrim(t.drug_id) = btrim(@drug))
                                       and (coalesce(@spec,'') = '' or btrim(coalesce(t.spec,'')) = btrim(@spec))
                                   ),
                                   agg as (
                                     select
                                       client_machine,
                                       coalesce(sum(req_qty),0)::bigint as value
                                     from base
                                     group by client_machine
                                   ),
                                   latest as (
                                     select distinct on (client_machine)
                                       client_machine,
                                       client_raw
                                     from base
                                     order by client_machine, created_at desc
                                   )
                                   select
                                     l.client_raw as client,
                                     a.value
                                   from agg a
                                   join latest l on l.client_machine = a.client_machine
                                   order by value desc
                                   limit @n
                               """;

            await using var cmd = conn.CreateCommand(sql, _opt.CommandTimeoutSeconds);
            cmd.AddParam("from", q.Range.From.ToDateTime(TimeOnly.MinValue));
            cmd.AddParam("to", q.Range.To.ToDateTime(TimeOnly.MinValue));
            cmd.AddParam("client", q.ClientName ?? string.Empty);
            cmd.AddParam("n", q.TopN);
            cmd.AddParam("drug", q.DrugId ?? string.Empty);
            cmd.AddParam("spec", q.Spec ?? string.Empty);

            var list = new List<(string Client, long Value)>();
            await using var reader = await cmd.ExecuteReaderAsync(token);
            while (await reader.ReadAsync(token))
            {
                var raw = reader.GetString(0);
                list.Add((
                    Client: raw,
                    Value: reader.GetInt64(1)
                ));
            }

            return (IReadOnlyList<(string Client, long Value)>)list;
        }, ct);

    public Task<DashboardKpiDto> GetKpisAsync(DashboardQuery q, CancellationToken ct)
        => _db.WithConnection(async (conn, token) =>
        {
            var sql = $"""
                               with
                               -- ====== pool: all vs avail ======
                               p_pool_all as (
                                 select *
                                 from trace_pool p
                                 where 1=1
                                   and (coalesce(@drug,'') = '' or btrim(p.drug_id) = btrim(@drug))
                                   and (coalesce(@spec,'') = '' or btrim(coalesce(p.spec,'')) = btrim(@spec))
                               ),
                               p_pool_avail as (
                                 select *
                                 from p_pool_all p
                                 where p.status = 1
                               ),

                               -- ====== txn ======
                               p_txn as (
                                 select *
                                 from trace_txn t
                                 where 1=1
                                   and t.created_at::date between @from::date and @to::date
                                   and (coalesce(@client,'') = '' or {ClientMachineExpr("t")} = @client)
                                   and (coalesce(@drug,'') = '' or btrim(t.drug_id) = btrim(@drug))
                                   and (coalesce(@spec,'') = '' or btrim(coalesce(t.spec,'')) = btrim(@spec))
                               ),

                               wk_range as (
                                 select
                                   (coalesce(max(created_at)::date, current_date)) as wk_to,
                                   (coalesce(max(created_at)::date, current_date) - 6) as wk_from
                                 from trace_txn
                                 where status = 'COMMITTED'
                               ),

                               wk as (
                                 select
                                   t.drug_id,
                                   coalesce(t.spec,'') as spec,
                                   coalesce(sum(t.req_qty),0)::bigint as wk_used
                                 from trace_txn t
                                 cross join wk_range r
                                 where t.status = 'COMMITTED'
                                   and t.created_at::date between r.wk_from and r.wk_to
                                   and (coalesce(@drug,'') = '' or btrim(t.drug_id) = btrim(@drug))
                                   and (coalesce(@spec,'') = '' or btrim(coalesce(t.spec,'')) = btrim(@spec))
                                 group by t.drug_id, coalesce(t.spec,'')
                               ),

                               active_drug as (
                                 select distinct
                                   d.drug_id,
                                   coalesce(d.spec,'') as spec
                                 from drug_index d
                                 where coalesce(d.note,'') not ilike '%弃用%'
                               ),

                               -- ====== pool aggregation ======
                               pool_agg as (
                                 select
                                   a.drug_id,
                                   a.spec,
                                   coalesce(av.remain_sum, 0)::bigint as remain_sum,   -- 只算可用
                                   coalesce(a.qty_sum, 0)::bigint as qty_sum,          -- 总量(0+1)
                                   coalesce(a.code_count, 0)::bigint as code_count
                                 from (
                                   select
                                     p.drug_id,
                                     coalesce(p.spec,'') as spec,
                                     coalesce(sum(p.qty),0)::bigint as qty_sum,
                                     coalesce(count(*),0)::bigint as code_count
                                   from p_pool_all p
                                   group by p.drug_id, coalesce(p.spec,'')
                                 ) a
                                 left join (
                                   select
                                     p.drug_id,
                                     coalesce(p.spec,'') as spec,
                                     coalesce(sum(p.remain),0)::bigint as remain_sum
                                   from p_pool_avail p
                                   group by p.drug_id, coalesce(p.spec,'')
                                 ) av on av.drug_id = a.drug_id and av.spec = a.spec
                               ),

                               low_calc as (
                                 select
                                   a.drug_id,
                                   a.spec,
                                   a.remain_sum,
                                   a.qty_sum,
                                   coalesce(w.wk_used::numeric, (a.qty_sum::numeric / 2)) as threshold,
                                   (a.remain_sum <= coalesce(w.wk_used::numeric, (a.qty_sum::numeric / 2))) as is_low
                                 from pool_agg a
                                 left join wk w on w.drug_id = a.drug_id and w.spec = a.spec
                                 where exists (
                                   select 1
                                   from active_drug d
                                   where d.drug_id = a.drug_id
                                     and d.spec = a.spec
                                 )
                               )

                               select
                                 -- 1) 可用追溯码库存：remain 总和（status=1）
                                 (select coalesce(sum(p.remain),0)::bigint from p_pool_avail p) as available_remain,

                                 -- 2) 期间使用条数：COMMITTED sum(req_qty)
                                 (select coalesce(sum(t.req_qty),0)::bigint from p_txn t where t.status='COMMITTED') as period_used,

                                 -- 3) 异常/回滚：ROLLED_BACK count(*)
                                 (select coalesce(count(*),0)::bigint from p_txn t where t.status='ROLLED_BACK') as abnormal,

                                 -- 4) 追溯码告紧：按“可用remain_sum” vs “阈值”判断（阈值来自总量/周用量）
                                 (select coalesce(count(*),0)::bigint from low_calc x where x.is_low) as low_stock,

                                 -- ===== Wave 分母（关键修正）：总量必须包含 status=0+1 =====
                                 (select coalesce(sum(p.qty),0)::bigint from p_pool_all p) as total_qty,

                                 (select coalesce(count(*),0)::bigint
                                  from p_txn t
                                  where t.status in ('COMMITTED','ROLLED_BACK')) as total_txn,

                                 (select coalesce(count(*),0)::bigint from pool_agg) as total_drug,

                               -- ===== Selected：drug 选中时，用完比例：remain<=0 / all（spec 为空=全部规格，也要能算） =====
                               (case when coalesce(@drug,'') <> ''
                                  then (select coalesce(count(*),0)::bigint
                                        from p_pool_all p
                                        where exists (
                                          select 1
                                          from active_drug d
                                          where d.drug_id = p.drug_id
                                            and d.spec = coalesce(p.spec,'')
                                        ))
                                  else null end) as selected_pool_count,

                               (case when coalesce(@drug,'') <> ''
                                  then (select coalesce(count(*),0)::bigint
                                        from p_pool_all p
                                        where coalesce(p.remain,0) <= 0
                                          and exists (
                                            select 1
                                            from active_drug d
                                            where d.drug_id = p.drug_id
                                              and d.spec = coalesce(p.spec,'')
                                          ))
                                  else null end) as selected_zero_remain_count
                               """;

            await using var cmd = conn.CreateCommand(sql, _opt.CommandTimeoutSeconds);

            cmd.AddParam("from", q.Range.From.ToDateTime(TimeOnly.MinValue));
            cmd.AddParam("to", q.Range.To.ToDateTime(TimeOnly.MinValue));

            cmd.AddParam("client", q.ClientName ?? string.Empty);
            cmd.AddParam("drug", q.DrugId ?? string.Empty);
            cmd.AddParam("spec", q.Spec ?? string.Empty);

            await using var reader = await cmd.ExecuteReaderAsync(token);
            if (!await reader.ReadAsync(token))
            {
                return new DashboardKpiDto(0, 0, 0, 0, 0, 0, 0, null, null);
            }

            return new DashboardKpiDto(
                AvailableRemain: reader.GetInt64(0),
                PeriodUsed: reader.GetInt64(1),
                Abnormal: reader.GetInt64(2),
                LowStockCount: reader.GetInt64(3),
                TotalQty: reader.GetInt64(4),
                TotalTxnCount: reader.GetInt64(5),
                TotalDrugCount: reader.GetInt64(6),
                SelectedPoolCount: reader.IsDBNull(7) ? null : reader.GetInt64(7),
                SelectedZeroRemainCount: reader.IsDBNull(8) ? null : reader.GetInt64(8)
            );
        }, ct);

    public Task<PagedResult<TrendRowDto>> GetTrendPageAsync(
        DashboardQuery q,
        int page,
        int pageSize,
        CancellationToken ct)
        => _db.WithConnection(async (conn, token) =>
        {
            var (_, safePageSize, offset) = NormalizePaging(page, pageSize);

            var cteSql = $"""
                                  with base as (
                                    select
                                      t.drug_id,
                                      coalesce(t.spec,'') as spec,
                                      {ClientMachineExpr("t")} as client_id,
                                      case when @trend_metric = 1 then 1 else t.req_qty end as v
                                    from trace_txn t
                                    where t.status = 'COMMITTED'
                                      and t.created_at::date between @from and @to
                                      and (coalesce(@client,'') = '' or {ClientMachineExpr("t")}=@client)
                                      and (coalesce(@drug,'') = '' or btrim(t.drug_id) = btrim(@drug))
                                      and (coalesce(@spec,'') = '' or btrim(coalesce(t.spec,'')) = btrim(@spec))
                                  ),
                                  agg as (
                                    select
                                      drug_id,
                                      spec,
                                      client_id,
                                      coalesce(sum(v),0)::bigint as client_qty
                                    from base
                                    group by drug_id, spec, client_id
                                  ),
                                  tot as (
                                    select
                                      drug_id,
                                      spec,
                                      coalesce(sum(client_qty),0)::bigint as total_qty
                                    from agg
                                    group by drug_id, spec
                                  ),
                                  ranked as (
                                    select
                                      a.drug_id,
                                      a.spec,
                                      a.client_id,
                                      a.client_qty,
                                      t.total_qty,
                                      row_number() over(
                                        partition by a.drug_id, a.spec
                                        order by a.client_qty desc, a.client_id
                                      ) as rn
                                    from agg a
                                    join tot t using (drug_id, spec)
                                  ),
                                  final as (
                                    select
                                      row_number() over(order by r.total_qty desc)::int as rank,
                                      r.drug_id as name,
                                      r.spec as sub,
                                      nullif(r.client_id,'') as top_client_raw,
                                      round((r.client_qty::numeric / nullif(r.total_qty,0) * 100), 1) as top_client_pct,
                                      r.total_qty::text as value_text
                                    from ranked r
                                    where r.rn = 1
                                  )
                                  """;

            var countSql = cteSql + " select count(*)::int from final;";
            var pageSql = cteSql + """
                                    select rank, name, sub, top_client_raw, top_client_pct, value_text
                                    from final
                                    order by rank
                                    offset @offset
                                    limit @n
                                    """;

            await using var count = conn.CreateCommand(countSql, _opt.CommandTimeoutSeconds);
            FillTrendQueryParams(count, q);
            var totalObj = await count.ExecuteScalarAsync(token);
            var totalCount = totalObj is int i ? i : Convert.ToInt32(totalObj ?? 0);

            await using var cmd = conn.CreateCommand(pageSql, _opt.CommandTimeoutSeconds);
            FillTrendQueryParams(cmd, q);
            cmd.AddParam("offset", offset);
            cmd.AddParam("n", safePageSize);

            var list = new List<TrendRowDto>();
            await using var reader = await cmd.ExecuteReaderAsync(token);
            while (await reader.ReadAsync(token))
            {
                list.Add(new TrendRowDto(
                    Rank: reader.GetInt32(0),
                    Name: reader.GetString(1),
                    Sub: reader.GetString(2),
                    TopClientRaw: reader.IsDBNull(3) ? null : FormatClient(reader.GetString(3)),
                    TopClientPct: reader.GetFieldValue<decimal>(4),
                    ValueText: reader.GetString(5)
                ));
            }

            return new PagedResult<TrendRowDto>(list, totalCount);
        }, ct);

    public Task<PagedResult<TraceTxnDto>> GetRecentTxnsPageAsync(
        DashboardQuery q,
        int page,
        int pageSize,
        CancellationToken ct)
        => _db.WithConnection(async (conn, token) =>
        {
            var (_, safePageSize, offset) = NormalizePaging(page, pageSize);

            var countSql = $"""
                                   select count(*)::int
                                   from trace_txn t
                                   where t.created_at::date between @from and @to
                                     and (coalesce(@client,'') = '' or {ClientMachineExpr("t")}=@client)
                                     and (coalesce(@drug,'') = '' or btrim(t.drug_id) = btrim(@drug))
                                     and (coalesce(@spec,'') = '' or btrim(coalesce(t.spec,'')) = btrim(@spec))
                               """;

            var sql = $"""
                                   select
                                     row_number() over(order by t.created_at desc)::bigint as id,
                                     t.status,
                                     (t.drug_id || ' ' || coalesce(t.spec,'')) as title,
                                     t.drug_id,
                                     coalesce(t.spec,'') as spec,
                                     t.req_qty::int as qty,
                                     t.created_at,
                                     {ClientMachineExpr("t")} as client_machine
                                   from trace_txn t
                                   where t.created_at::date between @from and @to
                                     and (coalesce(@client,'') = '' or {ClientMachineExpr("t")}=@client)
                                     and (coalesce(@drug,'') = '' or btrim(t.drug_id) = btrim(@drug))
                                     and (coalesce(@spec,'') = '' or btrim(coalesce(t.spec,'')) = btrim(@spec))
                                   order by t.created_at desc
                                   offset @offset
                                   limit @n
                               """;

            await using var count = conn.CreateCommand(countSql, _opt.CommandTimeoutSeconds);
            count.AddParam("from", q.Range.From.ToDateTime(TimeOnly.MinValue));
            count.AddParam("to", q.Range.To.ToDateTime(TimeOnly.MinValue));
            count.AddParam("client", q.ClientName ?? string.Empty);
            count.AddParam("drug", q.DrugId ?? string.Empty);
            count.AddParam("spec", q.Spec ?? string.Empty);
            var totalObj = await count.ExecuteScalarAsync(token);
            var totalCount = totalObj is int i ? i : Convert.ToInt32(totalObj ?? 0);

            await using var cmd = conn.CreateCommand(sql, _opt.CommandTimeoutSeconds);
            cmd.AddParam("from", q.Range.From.ToDateTime(TimeOnly.MinValue));
            cmd.AddParam("to", q.Range.To.ToDateTime(TimeOnly.MinValue));
            cmd.AddParam("client", q.ClientName ?? string.Empty);
            cmd.AddParam("offset", offset);
            cmd.AddParam("n", safePageSize);
            cmd.AddParam("drug", q.DrugId ?? string.Empty);
            cmd.AddParam("spec", q.Spec ?? string.Empty);

            var list = new List<TraceTxnDto>();
            await using var reader = await cmd.ExecuteReaderAsync(token);
            while (await reader.ReadAsync(token))
            {
                var statusStr = reader.GetString(1);
                var status = (statusStr).Trim().ToUpperInvariant() switch
                {
                    "COMMITTED" => TxnStatus.Commit,
                    "ROLLED_BACK" => TxnStatus.Rollback,
                    _ => TxnStatus.Pending
                };

                list.Add(new TraceTxnDto(
                    Id: reader.GetInt64(0),
                    Status: status,
                    Badge: StatusToBadge(status),
                    Title: reader.GetString(2),
                    DrugId: reader.GetString(3),
                    Spec: reader.GetString(4),
                    Qty: reader.GetInt32(5),
                    CreatedAt: ReadDateTimeOffset(reader.GetValue(6)),
                    ClientName: reader.IsDBNull(7) ? null : FormatClient(reader.GetString(7))
                ));
            }

            return new PagedResult<TraceTxnDto>(list, totalCount);
        }, ct);

    public Task<PagedResult<AbnormalRowDto>> GetAbnormalQueuePageAsync(
        DashboardQuery q,
        int page,
        int pageSize,
        CancellationToken ct)
        => _db.WithConnection(async (conn, token) =>
        {
            var (_, safePageSize, offset) = NormalizePaging(page, pageSize);

            var countSql = $"""
                                   select count(*)::int
                                   from trace_txn t
                                   where t.created_at::date between @from and @to
                                     and (coalesce(@client,'') = '' or {ClientMachineExpr("t")}=@client)
                                     and t.status = 'ROLLED_BACK'
                                     and (coalesce(@drug,'') = '' or btrim(t.drug_id) = btrim(@drug))
                                     and (coalesce(@spec,'') = '' or btrim(coalesce(t.spec,'')) = btrim(@spec))
                               """;

            var sql = $"""
                                   select
                                     '回滚事务' as title,
                                     (t.drug_id || ' ' || coalesce(t.spec,'') || ' · req=' || t.req_qty::text || ' · txn=' || t.txn_id) as detail,
                                     {ClientMachineExpr("t")} as client_machine,
                                     null::bigint as txn_id
                                   from trace_txn t
                                   where t.created_at::date between @from and @to
                                     and (coalesce(@client,'') = '' or {ClientMachineExpr("t")}=@client)
                                     and t.status = 'ROLLED_BACK'
                                     and (coalesce(@drug,'') = '' or btrim(t.drug_id) = btrim(@drug))
                                     and (coalesce(@spec,'') = '' or btrim(coalesce(t.spec,'')) = btrim(@spec))
                                   order by t.created_at desc
                                   offset @offset
                                   limit @n
                               """;

            await using var count = conn.CreateCommand(countSql, _opt.CommandTimeoutSeconds);
            count.AddParam("from", q.Range.From.ToDateTime(TimeOnly.MinValue));
            count.AddParam("to", q.Range.To.ToDateTime(TimeOnly.MinValue));
            count.AddParam("client", q.ClientName ?? string.Empty);
            count.AddParam("drug", q.DrugId ?? string.Empty);
            count.AddParam("spec", q.Spec ?? string.Empty);
            var totalObj = await count.ExecuteScalarAsync(token);
            var totalCount = totalObj is int i ? i : Convert.ToInt32(totalObj ?? 0);

            await using var cmd = conn.CreateCommand(sql, _opt.CommandTimeoutSeconds);
            cmd.AddParam("from", q.Range.From.ToDateTime(TimeOnly.MinValue));
            cmd.AddParam("to", q.Range.To.ToDateTime(TimeOnly.MinValue));
            cmd.AddParam("client", q.ClientName ?? string.Empty);
            cmd.AddParam("offset", offset);
            cmd.AddParam("n", safePageSize);
            cmd.AddParam("drug", q.DrugId ?? string.Empty);
            cmd.AddParam("spec", q.Spec ?? string.Empty);

            var list = new List<AbnormalRowDto>();
            await using var reader = await cmd.ExecuteReaderAsync(token);
            while (await reader.ReadAsync(token))
            {
                list.Add(new AbnormalRowDto(
                    Title: reader.GetString(0),
                    Detail: reader.GetString(1),
                    ClientDisplay: reader.IsDBNull(2) ? string.Empty : FormatClient(reader.GetString(2)),
                    Badge: TxnBadge.Danger,
                    TxnId: null
                ));
            }

            return new PagedResult<AbnormalRowDto>(list, totalCount);
        }, ct);

    public Task<IReadOnlyList<string>> GetDrugIdsAsync(CancellationToken ct)
        => _db.WithConnection(async (conn, token) =>
        {
            const string sql = """
                                   select distinct drug_id
                                   from drug_index
                                   where drug_id is not null
                                     and drug_id <> ''
                                     and coalesce(note,'') not ilike '%弃用%'
                                   order by drug_id
                                   limit 5000
                               """;

            await using var cmd = conn.CreateCommand(sql, _opt.CommandTimeoutSeconds);
            var list = new List<string>();
            await using var reader = await cmd.ExecuteReaderAsync(token);
            while (await reader.ReadAsync(token))
            {
                list.Add(reader.GetString(0));
            }

            return (IReadOnlyList<string>)list;
        }, ct);

    public Task<IReadOnlyList<string>> GetSpecsByDrugAsync(string drugId, CancellationToken ct)
        => _db.WithConnection(async (conn, token) =>
        {
            const string sql = """
                                   select distinct spec
                                   from drug_index
                                   where drug_id = @drug_id
                                     and coalesce(note,'') not ilike '%弃用%'
                                   order by spec
                                   limit 2000
                               """;

            await using var cmd = conn.CreateCommand(sql, _opt.CommandTimeoutSeconds);
            cmd.AddParam("drug_id", drugId);

            var list = new List<string>();
            await using var reader = await cmd.ExecuteReaderAsync(token);
            while (await reader.ReadAsync(token))
            {
                list.Add(reader.GetString(0));
            }

            return (IReadOnlyList<string>)list;
        }, ct);

    private static TxnBadge StatusToBadge(TxnStatus s)
        => s switch
        {
            TxnStatus.Commit => TxnBadge.Done,
            TxnStatus.Pending => TxnBadge.Danger,
            TxnStatus.Rollback => TxnBadge.Warning,
            _ => TxnBadge.Unknown
        };

    public Task<PagedResult<TraceEntryLogDto>> GetEntryLogsPageAsync(
        DashboardQuery q,
        int page,
        int pageSize,
        CancellationToken ct)
        => _db.WithConnection(async (conn, token) =>
        {
            var (_, safePageSize, offset) = NormalizePaging(page, pageSize);

            var countSql = $"""
                select count(*)::int
                from trace_entry_log l
                where l.entry_at::date between @from and @to
                  and (coalesce(@client,'') = '' or {ClientMachineExpr("l")} = @client)
                  and (coalesce(@drug,'') = '' or btrim(l.drug_id) = btrim(@drug))
                  and (coalesce(@spec,'') = '' or btrim(coalesce(l.spec,'')) = btrim(@spec))
            """;

            var sql = $"""
                select
                  l.entry_at,
                  l.drug_id,
                  l.spec,
                  l.entry_count,
                  l.qty_per_trace,
                  l.total_available_qty,
                  case
                    when l.failed_count <= 0 and lower(coalesce(l.result, '')) = 'failed'
                      then l.entry_count
                    else l.failed_count
                  end as failed_count,
                  l.result,
                  l.txn_id,
                  {ClientMachineExpr("l")} as client_machine,
                  l.source,
                  l.message
                from trace_entry_log l
                where l.entry_at::date between @from and @to
                  and (coalesce(@client,'') = '' or {ClientMachineExpr("l")} = @client)
                  and (coalesce(@drug,'') = '' or btrim(l.drug_id) = btrim(@drug))
                  and (coalesce(@spec,'') = '' or btrim(coalesce(l.spec,'')) = btrim(@spec))
                order by l.entry_at desc
                offset @offset
                limit @n
            """;

            await using var count = conn.CreateCommand(countSql, _opt.CommandTimeoutSeconds);
            count.AddParam("from", q.Range.From.ToDateTime(TimeOnly.MinValue));
            count.AddParam("to", q.Range.To.ToDateTime(TimeOnly.MinValue));
            count.AddParam("client", q.ClientName ?? string.Empty);
            count.AddParam("drug", q.DrugId ?? string.Empty);
            count.AddParam("spec", q.Spec ?? string.Empty);
            var totalObj = await count.ExecuteScalarAsync(token);
            var totalCount = totalObj is int i ? i : Convert.ToInt32(totalObj ?? 0);

            await using var cmd = conn.CreateCommand(sql, _opt.CommandTimeoutSeconds);
            cmd.AddParam("from", q.Range.From.ToDateTime(TimeOnly.MinValue));
            cmd.AddParam("to", q.Range.To.ToDateTime(TimeOnly.MinValue));
            cmd.AddParam("client", q.ClientName ?? string.Empty);
            cmd.AddParam("drug", q.DrugId ?? string.Empty);
            cmd.AddParam("spec", q.Spec ?? string.Empty);
            cmd.AddParam("offset", offset);
            cmd.AddParam("n", safePageSize);

            var list = new List<TraceEntryLogDto>();

            await using var reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false);
            while (await reader.ReadAsync(token).ConfigureAwait(false))
            {
                var rawClient = reader.IsDBNull(9) ? string.Empty : reader.GetString(9);
                list.Add(new TraceEntryLogDto(
                    EntryAt: ReadDateTimeOffset(reader.GetValue(0)),
                    DrugId: reader.IsDBNull(1) ? string.Empty : reader.GetString(1),
                    Spec: reader.IsDBNull(2) ? string.Empty : reader.GetString(2),
                    EntryCount: reader.IsDBNull(3) ? 0 : reader.GetInt32(3),
                    QtyPerTrace: reader.IsDBNull(4) ? 0 : reader.GetInt32(4),
                    TotalAvailableQty: reader.IsDBNull(5) ? 0 : reader.GetInt32(5),
                    FailedCount: reader.IsDBNull(6) ? 0 : Math.Max(0, reader.GetInt32(6)),
                    Result: reader.IsDBNull(7) ? string.Empty : reader.GetString(7),
                    TxnId: reader.IsDBNull(8) ? null : reader.GetInt64(8),
                    Client: string.IsNullOrWhiteSpace(rawClient) ? string.Empty : rawClient,
                    Source: reader.IsDBNull(10) ? string.Empty : reader.GetString(10),
                    Message: reader.IsDBNull(11) ? null : reader.GetString(11)
                ));
            }

            return new PagedResult<TraceEntryLogDto>(list, totalCount);
        }, ct);

    private static DateTimeOffset ReadDateTimeOffset(object value)
    {
        return value switch
        {
            DateTimeOffset dto when dto.Offset == TimeSpan.Zero
                => new DateTimeOffset(DateTime.SpecifyKind(dto.DateTime, DateTimeKind.Local)),
            DateTimeOffset dto => dto,
            DateTime dt => dt.Kind switch
            {
                DateTimeKind.Utc => new DateTimeOffset(dt, TimeSpan.Zero),
                DateTimeKind.Local => new DateTimeOffset(dt),
                _ => new DateTimeOffset(DateTime.SpecifyKind(dt, DateTimeKind.Local))
            },
            _ => DateTimeOffset.Parse(value.ToString() ?? string.Empty)
        };
    }

    private static int ResolveTrendMetric(DashboardQuery q)
    {
        var p = q.GetType().GetProperty("TrendMetric");
        if (p?.GetValue(q) is { } v)
        {
            return v is int i ? i : Convert.ToInt32(v);
        }

        return 0;
    }

    private static void FillTrendQueryParams(NpgsqlCommand cmd, DashboardQuery q)
    {
        cmd.AddParam("from", q.Range.From.ToDateTime(TimeOnly.MinValue));
        cmd.AddParam("to", q.Range.To.ToDateTime(TimeOnly.MinValue));
        cmd.AddParam("client", q.ClientName ?? string.Empty);
        cmd.AddParam("drug", q.DrugId ?? string.Empty);
        cmd.AddParam("spec", q.Spec ?? string.Empty);
        cmd.AddParam("trend_metric", ResolveTrendMetric(q));
    }

    private string FormatClient(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return raw;
        }

        var ci = ClientParser.Parse(raw);
        var machine = (ci.Machine ?? raw).Trim();
        if (machine.Length == 0)
        {
            return raw;
        }

        var display = _alias.Resolve(machine);
        return string.IsNullOrWhiteSpace(display) ? machine : display;
    }

    private static string ClientMachineExpr(string alias)
        => $"coalesce(nullif(btrim(split_part({alias}.client_id,'|',1)),''), btrim(coalesce({alias}.client_id,'')))";

    private static (int Page, int PageSize, int Offset) NormalizePaging(int page, int pageSize)
    {
        var safePage = Math.Max(1, page);
        var safePageSize = Math.Max(1, pageSize);
        var offset = (safePage - 1) * safePageSize;
        return (safePage, safePageSize, offset);
    }
}
