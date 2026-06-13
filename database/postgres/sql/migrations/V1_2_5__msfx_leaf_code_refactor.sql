-- V1_2_5__msfx_leaf_code_refactor.sql
-- msfx: 退场 code/trace_code，仅保留层级字段 + leaf_code

-- =========================
-- 1) relation: code -> leaf_code
-- =========================
do $$
begin
  if exists (
    select 1 from information_schema.columns
    where table_schema = 'public' and table_name = 'msfx_code_relation' and column_name = 'code'
  ) and not exists (
    select 1 from information_schema.columns
    where table_schema = 'public' and table_name = 'msfx_code_relation' and column_name = 'leaf_code'
  ) then
    execute 'alter table msfx_code_relation rename column code to leaf_code';
  end if;
end$$;

alter table if exists msfx_code_relation
  add column if not exists leaf_code text;

update msfx_code_relation
set leaf_code = coalesce(leaf_code, code_level_1, code_level_2, code_level_3, code_level_4, code_level_5)
where leaf_code is null;

alter table msfx_code_relation
  drop constraint if exists msfx_code_relation_code_key;

alter table msfx_code_relation
  alter column leaf_code set not null;

alter table msfx_code_relation
  add constraint uq_msfx_code_relation_leaf_code unique (leaf_code);

alter table if exists msfx_code_relation
  drop column if exists code;

-- =========================
-- 2) staging: trace_code -> leaf_code
-- =========================
do $$
begin
  if exists (
    select 1 from information_schema.columns
    where table_schema = 'public' and table_name = 'msfx_code_staging' and column_name = 'trace_code'
  ) and not exists (
    select 1 from information_schema.columns
    where table_schema = 'public' and table_name = 'msfx_code_staging' and column_name = 'leaf_code'
  ) then
    execute 'alter table msfx_code_staging rename column trace_code to leaf_code';
  end if;
end$$;

alter table if exists msfx_code_staging
  add column if not exists leaf_code text;

update msfx_code_staging
set leaf_code = coalesce(leaf_code, source_code_level_1, source_code_level_2, source_code_level_3, source_code_level_4, source_code_level_5)
where leaf_code is null;

alter table msfx_code_staging
  drop constraint if exists msfx_code_staging_trace_code_key;

alter table msfx_code_staging
  alter column leaf_code set not null;

alter table msfx_code_staging
  add constraint uq_msfx_code_staging_leaf_code unique (leaf_code);

alter table if exists msfx_code_staging
  drop column if exists trace_code;

-- =========================
-- 3) task_code/event: trace_code -> leaf_code
-- =========================
do $$
begin
  if exists (
    select 1 from information_schema.columns
    where table_schema = 'public' and table_name = 'msfx_inject_task_code' and column_name = 'trace_code'
  ) and not exists (
    select 1 from information_schema.columns
    where table_schema = 'public' and table_name = 'msfx_inject_task_code' and column_name = 'leaf_code'
  ) then
    execute 'alter table msfx_inject_task_code rename column trace_code to leaf_code';
  end if;
end$$;

alter table if exists msfx_inject_task_code
  add column if not exists leaf_code text;

update msfx_inject_task_code tc
set leaf_code = s.leaf_code
from msfx_code_staging s
where tc.leaf_code is null
  and tc.staging_id = s.id;

alter table msfx_inject_task_code
  alter column leaf_code set not null;

alter table msfx_inject_task_code
  drop constraint if exists msfx_inject_task_code_pkey;

alter table msfx_inject_task_code
  add constraint msfx_inject_task_code_pkey primary key (task_id, leaf_code);

alter table if exists msfx_inject_task_code
  drop column if exists trace_code;

create index if not exists idx_msfx_inject_task_code_leaf
on msfx_inject_task_code(leaf_code);

do $$
begin
  if exists (
    select 1 from information_schema.columns
    where table_schema = 'public' and table_name = 'msfx_inject_event' and column_name = 'trace_code'
  ) and not exists (
    select 1 from information_schema.columns
    where table_schema = 'public' and table_name = 'msfx_inject_event' and column_name = 'leaf_code'
  ) then
    execute 'alter table msfx_inject_event rename column trace_code to leaf_code';
  end if;
end$$;

alter table if exists msfx_inject_event
  add column if not exists leaf_code text;

alter table if exists msfx_inject_event
  drop column if exists trace_code;

drop index if exists idx_msfx_inject_event_trace_code_time;
create index if not exists idx_msfx_inject_event_leaf_code_time
on msfx_inject_event(leaf_code, created_at desc);

-- =========================
-- 4) rebuild function: build inject tasks (leaf_code)
-- =========================
create or replace function msfx_build_inject_tasks(p_max_groups integer default 200)
returns table(created_tasks integer, tasked_codes integer)
language plpgsql
as $$
begin
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
      min(c.created_at) as first_at
    from candidates c
    group by c.bill_id, c.source_bill_code, c.mapped_drug_id, c.mapped_spec
    order by min(c.created_at)
    limit greatest(coalesce(p_max_groups, 0), 0)
  ),
  selected as (
    select c.*
    from candidates c
    join picked_groups g
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
      total_codes
    )
    select
      'MSFX_INBOUND',
      'NEW',
      s.bill_id,
      s.source_bill_code,
      s.mapped_drug_id,
      s.mapped_spec,
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

-- =========================
-- 5) rebuild function: pool verified codes (leaf_code)
-- =========================
create or replace function msfx_pool_verified_codes(p_limit integer default 5000)
returns table(
  picked_count integer,
  pooled_count integer,
  duplicate_count integer
)
language plpgsql
as $$
begin
  return query
  with picked as (
    select
      s.id,
      s.leaf_code,
      s.mapped_drug_id,
      s.mapped_spec
    from msfx_code_staging s
    where s.code_status = 'VERIFIED'
      and s.mapped_drug_id is not null
      and s.mapped_spec is not null
    order by coalesce(s.verified_at, s.updated_at), s.id
    for update skip locked
    limit greatest(coalesce(p_limit, 0), 0)
  ),
  ins as (
    insert into trace_pool(drug_id, spec, qty, trace_code)
    select p.mapped_drug_id, p.mapped_spec, d.qty, p.leaf_code
    from picked p
    join drug_index d
      on d.drug_id = p.mapped_drug_id
     and d.spec = p.mapped_spec
    on conflict (trace_code) do nothing
    returning trace_code
  ),
  upd as (
    update msfx_code_staging s
    set
      code_status = case when i.trace_code is not null then 'POOLED' else 'DUPLICATE' end,
      pooled_at = now(),
      updated_at = now(),
      err_msg = case when i.trace_code is null then coalesce(s.err_msg, 'leaf_code already exists in trace_pool') else s.err_msg end
    from picked p
    left join ins i on i.trace_code = p.leaf_code
    where s.id = p.id
    returning s.code_status
  )
  select
    (select count(*)::int from picked) as picked_count,
    (select count(*)::int from upd where code_status = 'POOLED') as pooled_count,
    (select count(*)::int from upd where code_status = 'DUPLICATE') as duplicate_count;
end;
$$;
