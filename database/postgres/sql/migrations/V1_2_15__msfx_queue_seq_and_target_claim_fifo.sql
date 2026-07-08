-- 为注入任务增加显式队列顺序，避免依赖 created_at / id 的隐式顺序。
-- 1) 既有任务回填 queue_seq
-- 2) 重新定义建任务函数，按稳定分组顺序生成 queue_seq
-- 3) 精确 claim 时按 queue_seq FIFO 领取

alter table if exists msfx_inject_task
  add column if not exists queue_seq bigint;

alter table if exists msfx_inject_task
  add column if not exists warehouse_bill_no text;

with ordered as (
  select
    t.id,
    row_number() over (order by t.created_at, t.id)::bigint as seq
  from msfx_inject_task t
  where t.queue_seq is null
)
update msfx_inject_task t
set queue_seq = o.seq
from ordered o
where t.id = o.id;

alter table if exists msfx_inject_task
  alter column queue_seq set not null;

create unique index if not exists uq_msfx_inject_task_queue_seq
on msfx_inject_task(queue_seq);

create index if not exists idx_msfx_inject_task_wh_bill_success
on msfx_inject_task(warehouse_bill_no, mapped_drug_id, mapped_spec)
where status = 'SUCCESS' and warehouse_bill_no is not null;

create or replace function msfx_build_inject_tasks(p_max_groups integer default 200)
returns table(created_tasks integer, tasked_codes integer)
language plpgsql
as $$
begin
  lock table msfx_inject_task in share row exclusive mode;

  return query
  with candidates as (
    select
      s.id as staging_id,
      s.leaf_code,
      s.mapped_drug_id,
      s.mapped_spec,
      b.id as bill_id,
      s.source_bill_code,
      s.created_at
    from msfx_code_staging s
    left join msfx_upout_bill b on b.bill_code = s.source_bill_code
    where s.map_status = 'MAPPED'
      and s.code_status = 'NEW'
      and s.inject_task_id is null
    order by s.created_at, s.id
  ),
  picked_groups as (
    select
      c.bill_id,
      c.source_bill_code,
      c.mapped_drug_id,
      c.mapped_spec,
      min(c.created_at) as first_at,
      min(c.staging_id) as first_staging_id
    from candidates c
    group by c.bill_id, c.source_bill_code, c.mapped_drug_id, c.mapped_spec
    order by min(c.created_at), min(c.staging_id)
    limit greatest(coalesce(p_max_groups, 0), 0)
  ),
  seq_base as (
    select coalesce(max(t.queue_seq), 0)::bigint as base_seq
    from msfx_inject_task t
  ),
  numbered_groups as (
    select
      g.bill_id,
      g.source_bill_code,
      g.mapped_drug_id,
      g.mapped_spec,
      (sb.base_seq + row_number() over (order by g.first_at, g.first_staging_id))::bigint as queue_seq
    from picked_groups g
    cross join seq_base sb
  ),
  selected as (
    select c.*, g.queue_seq
    from candidates c
    join numbered_groups g
      on c.bill_id is not distinct from g.bill_id
     and c.source_bill_code is not distinct from g.source_bill_code
     and c.mapped_drug_id = g.mapped_drug_id
     and c.mapped_spec = g.mapped_spec
  ),
  ins_task as (
    insert into msfx_inject_task (
      task_type,
      status,
      bill_id,
      source_bill_code,
      mapped_drug_id,
      mapped_spec,
      queue_seq,
      total_codes
    )
    select
      'MSFX_INBOUND',
      'NEW',
      s.bill_id,
      s.source_bill_code,
      s.mapped_drug_id,
      s.mapped_spec,
      min(s.queue_seq)::bigint,
      count(*)::int
    from selected s
    group by s.bill_id, s.source_bill_code, s.mapped_drug_id, s.mapped_spec
    returning id, bill_id, source_bill_code, mapped_drug_id, mapped_spec
  ),
  ins_code as (
    insert into msfx_inject_task_code (
      task_id,
      leaf_code,
      staging_id,
      seq,
      status
    )
    select
      t.id,
      s.leaf_code,
      s.staging_id,
      row_number() over (
        partition by t.id
        order by s.created_at, s.staging_id
      )::int as seq,
      'PENDING'
    from selected s
    join ins_task t
      on s.bill_id is not distinct from t.bill_id
     and s.source_bill_code is not distinct from t.source_bill_code
     and s.mapped_drug_id = t.mapped_drug_id
     and s.mapped_spec = t.mapped_spec
    on conflict (task_id, leaf_code) do nothing
    returning task_id, staging_id
  ),
  upd_stage as (
    update msfx_code_staging s
    set
      inject_task_id = i.task_id,
      code_status = 'TASKED',
      updated_at = now()
    from ins_code i
    where s.id = i.staging_id
    returning s.id
  )
  select
    (select count(*)::int from ins_task) as created_tasks,
    (select count(*)::int from upd_stage) as tasked_codes;
end;
$$;

create or replace function msfx_has_warehouse_success_task(
  p_warehouse_bill_no text,
  p_mapped_drug_id text,
  p_mapped_spec text
)
returns boolean
language plpgsql
as $$
declare
  v_exists boolean;
begin
  if trim(coalesce(p_warehouse_bill_no, '')) = '' then
    return false;
  end if;

  if trim(coalesce(p_mapped_drug_id, '')) = '' then
    return false;
  end if;

  if trim(coalesce(p_mapped_spec, '')) = '' then
    return false;
  end if;

  select exists (
    select 1
    from msfx_inject_task t
    where t.status = 'SUCCESS'
      and btrim(coalesce(t.warehouse_bill_no, '')) = btrim(p_warehouse_bill_no)
      and btrim(t.mapped_drug_id) = btrim(p_mapped_drug_id)
      and btrim(t.mapped_spec) = btrim(p_mapped_spec)
  )
  into v_exists;

  return coalesce(v_exists, false);
end;
$$;

create or replace function msfx_claim_inject_task_by_target(
  p_client_id text,
  p_mapped_drug_id text,
  p_mapped_spec text
)
returns table(
  task_id bigint,
  bill_id bigint,
  source_bill_code text,
  mapped_drug_id text,
  mapped_spec text,
  total_codes integer
)
language plpgsql
as $$
begin
  if trim(coalesce(p_client_id, '')) = '' then
    raise exception 'p_client_id cannot be empty';
  end if;

  if trim(coalesce(p_mapped_drug_id, '')) = '' then
    raise exception 'p_mapped_drug_id cannot be empty';
  end if;

  if trim(coalesce(p_mapped_spec, '')) = '' then
    raise exception 'p_mapped_spec cannot be empty';
  end if;

  return query
  with picked as (
    select
      t.id,
      t.status as prev_status
    from msfx_inject_task t
    where (
            t.status = 'NEW'
         or (
              t.status = 'FAILED'
          and exists (
                select 1
                from msfx_inject_task_code tc
                where tc.task_id = t.id
                  and tc.status = 'FAILED'
              )
         )
          )
      and btrim(t.mapped_drug_id) = btrim(p_mapped_drug_id)
      and btrim(t.mapped_spec) = btrim(p_mapped_spec)
    order by
      t.queue_seq,
      t.id
    for update skip locked
    limit 1
  ),
  reset_code as (
    update msfx_inject_task_code tc
    set
      status = 'PENDING',
      injected_at = null,
      verify_result = null,
      err_msg = null
    from picked p
    where tc.task_id = p.id
      and p.prev_status = 'FAILED'
      and tc.status = 'FAILED'
    returning tc.task_id, tc.staging_id
  ),
  reset_staging as (
    update msfx_code_staging s
    set
      code_status = 'TASKED',
      err_msg = null,
      updated_at = now()
    from reset_code rc
    where s.id = rc.staging_id
    returning s.id
  ),
  upd as (
    update msfx_inject_task t
    set
      status = 'RUNNING',
      client_id = p_client_id,
      picked_at = coalesce(t.picked_at, now()),
      finished_at = null,
      err_msg = null
    from picked p
    where t.id = p.id
    returning t.id, t.bill_id, t.source_bill_code, t.mapped_drug_id, t.mapped_spec, t.total_codes
  ),
  evt as (
    insert into msfx_inject_event(task_id, stage, level, message)
    select
      u.id,
      'DB_SYNC',
      'INFO',
      case
        when exists (select 1 from reset_code rc where rc.task_id = u.id)
          then 'task re-claimed by target: ' || p_client_id
        else 'task claimed by target: ' || p_client_id
      end
    from upd u
    returning 1
  )
  select
    u.id as task_id,
    u.bill_id,
    u.source_bill_code,
    u.mapped_drug_id,
    u.mapped_spec,
    u.total_codes
  from upd u;
end;
$$;
