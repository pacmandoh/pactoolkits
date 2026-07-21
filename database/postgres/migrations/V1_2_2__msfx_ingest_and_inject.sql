-- V1_2_2__msfx_ingest_and_inject.sql
-- 码上放心：上游出库拉取、映射、注入任务结构

-- =========================
-- 1) 拉取批次
-- =========================
create table if not exists msfx_pull_batch (
  id                bigserial primary key,
  source_api        text not null check (source_api in ('listupout', 'listupout_detail', 'query_relation')),
  begin_date        date null,
  end_date          date null,
  request_id        text null,
  status            text not null default 'RUNNING'
                    check (status in ('RUNNING', 'SUCCESS', 'PARTIAL', 'FAILED')),
  started_at        timestamptz not null default clock_timestamp(),
  finished_at       timestamptz null,
  success_count     integer not null default 0 check (success_count >= 0),
  fail_count        integer not null default 0 check (fail_count >= 0),
  err_msg           text null,
  raw_response      jsonb null
);

create index if not exists idx_msfx_pull_batch_started
on msfx_pull_batch(started_at desc);

create index if not exists idx_msfx_pull_batch_status_started
on msfx_pull_batch(status, started_at desc);

create table if not exists msfx_pull_cursor (
  source_api          text primary key check (source_api in ('listupout', 'listupout_detail', 'query_relation')),
  last_success_begin  timestamptz null,
  last_success_end    timestamptz null,
  last_batch_id       bigint null references msfx_pull_batch(id) on delete set null,
  updated_at          timestamptz not null default now()
);

-- =========================
-- 2) 上游出库单头
-- =========================
create table if not exists msfx_upout_bill (
  id                bigserial primary key,
  pull_batch_id     bigint null references msfx_pull_batch(id) on delete set null,

  bill_code         text not null unique,
  bill_out_id       bigint null,
  bill_type         text null,
  bill_type_name    text null,

  bill_time         date null,
  bill_time_format  timestamptz null,
  bill_upload_time  timestamptz null,
  store_out_date    timestamptz null,
  update_date       timestamptz null,

  from_ref_user_id  text null,
  from_user_id      text null,
  from_ent_name     text null,
  from_user_name    text null,

  to_ref_user_id    text null,
  to_user_id        text null,
  to_user_name      text null,

  ent_send_id       text null,
  ent_send_name     text null,
  ent_recv_id       text null,
  ent_recv_name     text null,

  ass_ent_id        text null,
  ass_ent_name      text null,
  ass_ref_ent_id    text null,

  -- 当前策略：业务表只落已入库单据（status=2）
  upout_status_raw  text not null default '2' check (upout_status_raw = '2'),

  first_seen_at     timestamptz not null default now(),
  last_seen_at      timestamptz not null default now(),
  status_changed_at timestamptz null,

  raw_json          jsonb not null default '{}'::jsonb,
  created_at        timestamptz not null default now(),
  updated_at        timestamptz not null default now()
);

create index if not exists idx_msfx_upout_bill_upload_time
on msfx_upout_bill(bill_upload_time desc nulls last);

create index if not exists idx_msfx_upout_bill_status_upload
on msfx_upout_bill(upout_status_raw, bill_upload_time desc nulls last);

create index if not exists idx_msfx_upout_bill_last_seen
on msfx_upout_bill(last_seen_at desc);

-- =========================
-- 3) 上游出库单药品行
-- 注意：上游列表未给稳定 line_no，使用 source_row_key 去重
-- =========================
create table if not exists msfx_upout_item (
  id                    bigserial primary key,
  bill_id               bigint not null references msfx_upout_bill(id) on delete cascade,
  source_row_key        text not null,

  drug_ent_base_info_id text null,
  prod_id               text null,
  prod_seq_no           text null,
  product_code          text null,
  sub_type_no           text null,

  physic_name           text null,
  physic_info           text null,
  physic_type           text null,
  physic_type_name      text null,

  pkg_spec              text null,
  package_spec          text null,
  pkg_ratio             text null,
  pkg_unit_desc         text null,

  prepn_spec            text null,
  prepn_type            text null,
  prepn_type_desc       text null,
  prepn_unit            text null,
  prepn_unit_desc       text null,
  preparations_unit     text null,

  code_count            integer null check (code_count is null or code_count >= 0),
  prepn_count           integer null check (prepn_count is null or prepn_count >= 0),
  least_pkg_amount      integer null check (least_pkg_amount is null or least_pkg_amount >= 0),
  least_prepn_amount    integer null check (least_prepn_amount is null or least_prepn_amount >= 0),

  produce_batch_no      text null,
  produce_date          date null,
  valid_end_date        date null,
  exprie_date           date null,

  approval_no           text null,
  approve_no            text null,

  produce_ent_name      text null,
  product_ent_name      text null,

  raw_json              jsonb not null default '{}'::jsonb,
  created_at            timestamptz not null default now(),
  updated_at            timestamptz not null default now(),

  unique (bill_id, source_row_key)
);

create index if not exists idx_msfx_upout_item_bill
on msfx_upout_item(bill_id);

create index if not exists idx_msfx_upout_item_drug_batch
on msfx_upout_item(drug_ent_base_info_id, produce_batch_no);

-- =========================
-- 4) 码关系明细
-- =========================
create table if not exists msfx_code_relation (
  id                    bigserial primary key,
  upout_item_id         bigint null references msfx_upout_item(id) on delete set null,

  source_type           text not null check (source_type in ('RELATION_QUERY', 'UPOUT_DETAIL')),
  code                  text not null,
  parent_code           text null,
  code_level            text null,
  code_pack_level       text null,
  status_code           text null,

  pkg_amount            integer null check (pkg_amount is null or pkg_amount >= 0),
  prepn_amount          integer null check (prepn_amount is null or prepn_amount >= 0),
  prepn_spec            text null,

  code_active_info_id   text null,
  active_date           timestamptz null,
  active_count          integer null check (active_count is null or active_count >= 0),
  small_num             integer null check (small_num is null or small_num >= 0),
  other_num             integer null check (other_num is null or other_num >= 0),
  process_count         integer null check (process_count is null or process_count >= 0),
  process_flag          text null,
  relation_type         text null,
  upload_file_name      text null,
  upload_file_path      text null,

  raw_json              jsonb not null default '{}'::jsonb,
  created_at            timestamptz not null default now(),

  unique (code)
);

create index if not exists idx_msfx_code_relation_item
on msfx_code_relation(upout_item_id);

create index if not exists idx_msfx_code_relation_parent
on msfx_code_relation(parent_code);

-- =========================
-- 5) 映射规则（药品/规格）
-- =========================
create table if not exists msfx_map_rule (
  id                    bigserial primary key,
  rule_type             text not null check (rule_type in ('DRUG', 'SPEC', 'COMBINED')),

  source_drug_name      text null,
  source_spec           text null,

  target_drug_id        text not null,
  target_spec           text not null,

  priority              integer not null default 100,
  enabled               boolean not null default true,
  note                  text null,

  updated_at            timestamptz not null default now(),
  created_at            timestamptz not null default now()
);

create index if not exists idx_msfx_map_rule_lookup
on msfx_map_rule(rule_type, enabled, priority, source_drug_name, source_spec);

-- =========================
-- 6) 标准化码池（待注入/待入 trace_pool）
-- =========================
create table if not exists msfx_code_staging (
  id                    bigserial primary key,
  trace_code            text not null unique,
  source_relation_id    bigint not null references msfx_code_relation(id) on delete restrict,

  source_bill_code      text null,
  source_drug_name_raw  text null,
  source_spec_raw       text null,
  source_name_norm      text null,
  source_spec_norm      text null,

  -- 映射到院内标准值；trace_pool 只使用 mapped_* 字段
  mapped_drug_id        text null,
  mapped_spec           text null,

  map_status            text not null default 'PENDING'
                        check (map_status in ('PENDING', 'MAPPED', 'NEED_REVIEW', 'FAILED')),
  code_status           text not null default 'NEW'
                        check (code_status in ('NEW', 'TASKED', 'INJECTED', 'VERIFIED', 'POOLED', 'FAILED', 'DUPLICATE')),

  inject_task_id        bigint null,
  injected_at           timestamptz null,
  verified_at           timestamptz null,
  pooled_at             timestamptz null,

  err_msg               text null,
  created_at            timestamptz not null default now(),
  updated_at            timestamptz not null default now(),

  constraint ck_msfx_code_staging_mapped_fields
  check (
    map_status <> 'MAPPED'
    or (
      mapped_drug_id is not null
      and mapped_spec is not null
      and source_name_norm is not null
      and source_spec_norm is not null
    )
  )
);

create index if not exists idx_msfx_code_staging_map
on msfx_code_staging(map_status, code_status, created_at desc);

create index if not exists idx_msfx_code_staging_task
on msfx_code_staging(inject_task_id);

create index if not exists idx_msfx_code_staging_norm_lookup
on msfx_code_staging(source_name_norm, source_spec_norm, map_status, created_at desc);

alter table msfx_code_staging
  drop constraint if exists fk_msfx_code_staging_mapped_drug;

alter table msfx_code_staging
  add constraint fk_msfx_code_staging_mapped_drug
  foreign key (mapped_drug_id, mapped_spec)
  references drug_index(drug_id, spec)
  on update cascade
  on delete restrict
  deferrable initially immediate;

-- =========================
-- 7) 注入任务头（agent 抢占）
-- =========================
create table if not exists msfx_inject_task (
  id                    bigserial primary key,
  task_type             text not null default 'MSFX_INBOUND'
                        check (task_type in ('MSFX_INBOUND')),
  status                text not null default 'NEW'
                        check (status in ('NEW', 'RUNNING', 'SUCCESS', 'PARTIAL', 'FAILED', 'CANCELLED')),

  bill_id               bigint null references msfx_upout_bill(id) on delete set null,
  source_bill_code      text null,
  mapped_drug_id        text not null,
  mapped_spec           text not null,

  priority              integer not null default 100,
  retry_count           integer not null default 0 check (retry_count >= 0),
  max_retry             integer not null default 3 check (max_retry >= 0),

  client_id             text null,
  target_window         text null,

  total_codes           integer not null default 0 check (total_codes >= 0),
  success_codes         integer not null default 0 check (success_codes >= 0),
  failed_codes          integer not null default 0 check (failed_codes >= 0),

  created_at            timestamptz not null default now(),
  picked_at             timestamptz null,
  finished_at           timestamptz null,
  err_msg               text null,

  constraint ck_msfx_inject_task_counts
  check (success_codes + failed_codes <= total_codes)
);

create index if not exists idx_msfx_inject_task_pick
on msfx_inject_task(status, priority desc, created_at);

create index if not exists idx_msfx_inject_task_bill
on msfx_inject_task(bill_id, status);

create index if not exists idx_msfx_inject_task_source_bill
on msfx_inject_task(source_bill_code, status);

create index if not exists idx_msfx_inject_task_pick_new
on msfx_inject_task(priority desc, created_at)
where status = 'NEW';

-- =========================
-- 8) 注入任务明细
-- =========================
create table if not exists msfx_inject_task_code (
  task_id               bigint not null references msfx_inject_task(id) on delete cascade,
  trace_code            text not null,
  staging_id            bigint null references msfx_code_staging(id) on delete set null,
  seq                   integer not null check (seq > 0),
  status                text not null default 'PENDING'
                        check (status in ('PENDING', 'INJECTED', 'FAILED', 'SKIPPED')),
  injected_at           timestamptz null,
  verify_result         text null,
  err_msg               text null,

  primary key (task_id, trace_code),
  unique (task_id, seq)
);

create index if not exists idx_msfx_inject_task_code_status
on msfx_inject_task_code(task_id, status, seq);

create index if not exists idx_msfx_inject_task_code_staging
on msfx_inject_task_code(staging_id);

create unique index if not exists uq_msfx_inject_task_code_staging
on msfx_inject_task_code(staging_id)
where staging_id is not null;

-- =========================
-- 9) 注入事件日志
-- =========================
create table if not exists msfx_inject_event (
  id                    bigserial primary key,
  task_id               bigint not null references msfx_inject_task(id) on delete cascade,
  trace_code            text null,
  stage                 text not null check (stage in ('PARSE', 'INJECT', 'VERIFY', 'DB_SYNC')),
  level                 text not null check (level in ('INFO', 'WARN', 'ERR')),
  message               text not null,
  created_at            timestamptz not null default now()
);

create index if not exists idx_msfx_inject_event_task_time
on msfx_inject_event(task_id, created_at desc);

create index if not exists idx_msfx_inject_event_stage_level_time
on msfx_inject_event(stage, level, created_at desc);

create index if not exists idx_msfx_inject_event_trace_code_time
on msfx_inject_event(trace_code, created_at desc);

-- =========================
-- 10) 外键补齐：staging.inject_task_id -> inject_task.id
-- =========================
alter table msfx_code_staging
  drop constraint if exists fk_msfx_code_staging_inject_task;

alter table msfx_code_staging
  add constraint fk_msfx_code_staging_inject_task
  foreign key (inject_task_id)
  references msfx_inject_task(id)
  on delete set null;

-- =========================
-- 11) 触发器：更新时间戳
-- =========================
create or replace function trg_msfx_touch_updated_at()
returns trigger as $$
begin
  new.updated_at := now();
  return new;
end;
$$ language plpgsql;

drop trigger if exists msfx_upout_bill_bu_touch on msfx_upout_bill;
create trigger msfx_upout_bill_bu_touch
before update on msfx_upout_bill
for each row
execute function trg_msfx_touch_updated_at();

drop trigger if exists msfx_upout_item_bu_touch on msfx_upout_item;
create trigger msfx_upout_item_bu_touch
before update on msfx_upout_item
for each row
execute function trg_msfx_touch_updated_at();

drop trigger if exists msfx_code_staging_bu_touch on msfx_code_staging;
create trigger msfx_code_staging_bu_touch
before update on msfx_code_staging
for each row
execute function trg_msfx_touch_updated_at();

-- =========================
-- 12) 水位通知（便于 UI 轮询/订阅）
-- =========================
drop trigger if exists msfx_upout_bill_as_touch_watermark on msfx_upout_bill;
create trigger msfx_upout_bill_as_touch_watermark
after insert or update or delete on msfx_upout_bill
for each statement
execute function trg_touch_watermark('msfx');

drop trigger if exists msfx_code_staging_as_touch_watermark on msfx_code_staging;
create trigger msfx_code_staging_as_touch_watermark
after insert or update or delete on msfx_code_staging
for each statement
execute function trg_touch_watermark('msfx');

drop trigger if exists msfx_inject_task_as_touch_watermark on msfx_inject_task;
create trigger msfx_inject_task_as_touch_watermark
after insert or update or delete on msfx_inject_task
for each statement
execute function trg_touch_watermark('msfx');

-- =========================
-- 13) 归一化 + 映射 + 任务生成函数
-- =========================
create or replace function msfx_norm_name(v text)
returns text
language sql
immutable
as $$
  select nullif(
    regexp_replace(
      regexp_replace(trim(coalesce(v, '')), '[（(].*?[）)]', '', 'g'),
      '\s+', '', 'g'
    ),
    ''
  );
$$;

create or replace function msfx_norm_spec(prepn_spec text, pkg_spec text)
returns text
language sql
immutable
as $$
with s as (
  select
    lower(regexp_replace(replace(replace(trim(coalesce(prepn_spec, '')), '∶', ':'), '：', ':'), '\s+', '', 'g')) as p,
    lower(regexp_replace(replace(replace(trim(coalesce(pkg_spec, '')), '×', '*'), 'x', '*'), '\s+', '', 'g')) as k
),
pack as (
  select
    p,
    case
      when k ~ '(\d+)(片|粒|支|瓶|袋|丸|盒|板)' then regexp_replace(k, '.*?(\d+)(片|粒|支|瓶|袋|丸|盒|板).*', '\1\2')
      else null
    end as pack_unit
  from s
)
select nullif(
  case
    when p <> '' and pack_unit is not null then p || '*' || pack_unit
    when p <> '' then p
    else pack_unit
  end,
  ''
)
from pack;
$$;

create or replace function msfx_apply_mapping(p_limit integer default 5000)
returns table(processed_count integer, mapped_count integer, review_count integer)
language plpgsql
as $$
begin
  return query
  with base as (
    select
      s.id,
      coalesce(nullif(s.source_drug_name_raw, ''), i.physic_name, '') as drug_name_raw,
      coalesce(nullif(s.source_spec_raw, ''), i.prepn_spec, '') as spec_raw,
      i.pkg_spec
    from msfx_code_staging s
    left join msfx_code_relation r on r.id = s.source_relation_id
    left join msfx_upout_item i on i.id = r.upout_item_id
    where s.map_status = 'PENDING'
    order by s.created_at, s.id
    limit greatest(coalesce(p_limit, 0), 0)
  ),
  normed as (
    update msfx_code_staging s
    set
      source_drug_name_raw = b.drug_name_raw,
      source_spec_raw = b.spec_raw,
      source_name_norm = msfx_norm_name(b.drug_name_raw),
      source_spec_norm = msfx_norm_spec(b.spec_raw, b.pkg_spec),
      updated_at = now()
    from base b
    where s.id = b.id
    returning s.id, s.source_name_norm, s.source_spec_norm
  ),
  resolved as (
    select
      n.id,
      coalesce(mr.target_drug_id, di.drug_id) as target_drug_id,
      coalesce(mr.target_spec, di.spec) as target_spec
    from normed n
    left join lateral (
      select m.target_drug_id, m.target_spec
      from msfx_map_rule m
      where m.enabled
        and msfx_norm_name(m.source_drug_name) = n.source_name_norm
        and msfx_norm_spec(m.source_spec, null) = n.source_spec_norm
      order by m.priority asc, m.id asc
      limit 1
    ) mr on true
    left join lateral (
      select d.drug_id, d.spec
      from drug_index d
      where msfx_norm_name(d.drug_id) = n.source_name_norm
        and msfx_norm_spec(d.spec, null) = n.source_spec_norm
      order by d.drug_id, d.spec
      limit 1
    ) di on mr.target_drug_id is null
  ),
  upd as (
    update msfx_code_staging s
    set
      mapped_drug_id = r.target_drug_id,
      mapped_spec = r.target_spec,
      map_status = case
                     when r.target_drug_id is not null and r.target_spec is not null then 'MAPPED'
                     else 'NEED_REVIEW'
                   end,
      updated_at = now()
    from resolved r
    where s.id = r.id
    returning s.map_status
  )
  select
    count(*)::int as processed_count,
    count(*) filter (where map_status = 'MAPPED')::int as mapped_count,
    count(*) filter (where map_status = 'NEED_REVIEW')::int as review_count
  from upd;
end;
$$;

create or replace function msfx_build_inject_tasks(p_max_groups integer default 200)
returns table(created_tasks integer, tasked_codes integer)
language plpgsql
as $$
begin
  return query
  with candidates as (
    select
      s.id as staging_id,
      s.trace_code,
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
      trace_code,
      staging_id,
      seq,
      status
    )
    select
      t.id,
      s.trace_code,
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
    on conflict (task_id, trace_code) do nothing
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

-- 任务原子抢占：NEW -> RUNNING（并发安全）
create or replace function msfx_claim_inject_tasks(
  p_client_id text,
  p_limit integer default 1
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

  return query
  with picked as (
    select t.id
    from msfx_inject_task t
    where t.status = 'NEW'
    order by t.priority desc, t.created_at, t.id
    for update skip locked
    limit greatest(coalesce(p_limit, 0), 0)
  ),
  upd as (
    update msfx_inject_task t
    set
      status = 'RUNNING',
      client_id = p_client_id,
      picked_at = coalesce(t.picked_at, now()),
      err_msg = null
    from picked p
    where t.id = p.id
    returning t.id, t.bill_id, t.source_bill_code, t.mapped_drug_id, t.mapped_spec, t.total_codes
  ),
  evt as (
    insert into msfx_inject_event(task_id, stage, level, message)
    select u.id, 'DB_SYNC', 'INFO', 'task claimed by ' || p_client_id
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

-- 任务结算：根据明细状态汇总 SUCCESS/PARTIAL/FAILED
create or replace function msfx_finalize_inject_task(
  p_task_id bigint,
  p_err_msg text default null
)
returns table(
  task_id bigint,
  task_status text,
  success_codes integer,
  failed_codes integer,
  total_codes integer
)
language plpgsql
as $$
begin
  return query
  with stats as (
    select
      p_task_id as id,
      count(*)::int as total_n,
      count(*) filter (where status = 'INJECTED')::int as succ_n,
      count(*) filter (where status in ('FAILED', 'SKIPPED'))::int as fail_n
    from msfx_inject_task_code
    where task_id = p_task_id
  ),
  upd as (
    update msfx_inject_task t
    set
      total_codes = s.total_n,
      success_codes = s.succ_n,
      failed_codes = s.fail_n,
      status = case
                 when s.total_n = 0 then 'FAILED'
                 when s.succ_n = s.total_n then 'SUCCESS'
                 when s.succ_n > 0 then 'PARTIAL'
                 else 'FAILED'
               end,
      finished_at = now(),
      err_msg = case
                  when p_err_msg is not null then p_err_msg
                  when s.fail_n > 0 then coalesce(t.err_msg, 'partial/failed detail exists')
                  else null
                end
    from stats s
    where t.id = s.id
    returning t.id, t.status, t.success_codes, t.failed_codes, t.total_codes
  ),
  evt as (
    insert into msfx_inject_event(task_id, stage, level, message)
    select
      u.id,
      'DB_SYNC',
      case when u.status in ('SUCCESS') then 'INFO' when u.status = 'PARTIAL' then 'WARN' else 'ERR' end,
      'task finalized: status=' || u.status || ', success=' || u.success_codes || ', failed=' || u.failed_codes || ', total=' || u.total_codes
    from upd u
    returning 1
  )
  select
    u.id as task_id,
    u.status as task_status,
    u.success_codes,
    u.failed_codes,
    u.total_codes
  from upd u;
end;
$$;

-- 落池：VERIFIED -> trace_pool（幂等）-> POOLED/DUPLICATE
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
      s.trace_code,
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
    select p.mapped_drug_id, p.mapped_spec, d.qty, p.trace_code
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
      err_msg = case when i.trace_code is null then coalesce(s.err_msg, 'trace_code already exists in trace_pool') else s.err_msg end
    from picked p
    left join ins i on i.trace_code = p.trace_code
    where s.id = p.id
    returning s.code_status
  )
  select
    (select count(*)::int from picked) as picked_count,
    (select count(*)::int from upd where code_status = 'POOLED') as pooled_count,
    (select count(*)::int from upd where code_status = 'DUPLICATE') as duplicate_count;
end;
$$;

-- 拉取游标读取：返回建议窗口 [begin_at, end_at]
create or replace function msfx_get_pull_window(
  p_source_api text,
  p_lookback interval default interval '10 minutes',
  p_default_back interval default interval '7 days'
)
returns table(begin_at timestamptz, end_at timestamptz)
language plpgsql
as $$
declare
  v_last_end timestamptz;
  v_now timestamptz := now();
begin
  select c.last_success_end
    into v_last_end
  from msfx_pull_cursor c
  where c.source_api = p_source_api;

  if v_last_end is null then
    begin_at := v_now - p_default_back;
  else
    begin_at := v_last_end - p_lookback;
  end if;

  end_at := v_now;
  return next;
end;
$$;

-- 拉取游标推进：仅成功/部分成功批次推进
create or replace function msfx_advance_pull_cursor(
  p_source_api text,
  p_begin timestamptz,
  p_end timestamptz,
  p_batch_id bigint,
  p_batch_status text
)
returns boolean
language plpgsql
as $$
begin
  if p_batch_status not in ('SUCCESS', 'PARTIAL') then
    return false;
  end if;

  insert into msfx_pull_cursor(source_api, last_success_begin, last_success_end, last_batch_id, updated_at)
  values (p_source_api, p_begin, p_end, p_batch_id, now())
  on conflict (source_api) do update
    set last_success_begin = excluded.last_success_begin,
        last_success_end = excluded.last_success_end,
        last_batch_id = excluded.last_batch_id,
        updated_at = excluded.updated_at;

  return true;
end;
$$;
