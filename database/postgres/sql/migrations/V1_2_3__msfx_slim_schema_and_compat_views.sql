-- V1_2_3__msfx_slim_schema_and_compat_views.sql
-- 码上放心瘦身层：slim tables + 同步触发器 + 兼容视图

-- =========================
-- 1) slim tables
-- =========================
create table if not exists msfx_upout_bill_slim (
  bill_id            bigint primary key,
  pull_batch_id      bigint null,
  bill_code          text not null unique,
  bill_type          text null,
  bill_time          date null,
  bill_time_format   timestamptz null,
  bill_upload_time   timestamptz null,
  from_ref_user_id   text null,
  from_ent_name      text null,
  to_ref_user_id     text null,
  upout_status_raw   text not null,
  first_seen_at      timestamptz not null,
  last_seen_at       timestamptz not null,
  raw_json           jsonb not null default '{}'::jsonb,
  created_at         timestamptz not null,
  updated_at         timestamptz not null
);

create index if not exists idx_msfx_upout_bill_slim_upload
on msfx_upout_bill_slim(bill_upload_time desc nulls last);

create index if not exists idx_msfx_upout_bill_slim_status
on msfx_upout_bill_slim(upout_status_raw, last_seen_at desc);

create table if not exists msfx_upout_item_slim (
  item_id            bigint primary key,
  bill_id            bigint not null,
  source_row_key     text not null,
  physic_name        text null,
  package_spec       text null,
  prepn_spec         text null,
  produce_batch_no   text null,
  code_count         integer null,
  raw_json           jsonb not null default '{}'::jsonb,
  created_at         timestamptz not null,
  updated_at         timestamptz not null,
  unique (bill_id, source_row_key)
);

create index if not exists idx_msfx_upout_item_slim_bill
on msfx_upout_item_slim(bill_id);

create index if not exists idx_msfx_upout_item_slim_name_spec
on msfx_upout_item_slim(physic_name, prepn_spec);

create table if not exists msfx_code_relation_slim (
  relation_id        bigint primary key,
  upout_item_id      bigint null,
  source_type        text not null,
  code               text not null unique,
  parent_code        text null,
  code_level         text null,
  prepn_spec         text null,
  raw_json           jsonb not null default '{}'::jsonb,
  created_at         timestamptz not null
);

create index if not exists idx_msfx_code_relation_slim_item
on msfx_code_relation_slim(upout_item_id);

create index if not exists idx_msfx_code_relation_slim_parent
on msfx_code_relation_slim(parent_code);

-- =========================
-- 2) backfill
-- =========================
insert into msfx_upout_bill_slim (
  bill_id, pull_batch_id, bill_code, bill_type,
  bill_time, bill_time_format, bill_upload_time,
  from_ref_user_id, from_ent_name, to_ref_user_id,
  upout_status_raw, first_seen_at, last_seen_at,
  raw_json, created_at, updated_at
)
select
  b.id, b.pull_batch_id, b.bill_code, b.bill_type,
  b.bill_time, b.bill_time_format, b.bill_upload_time,
  b.from_ref_user_id, b.from_ent_name, b.to_ref_user_id,
  b.upout_status_raw, b.first_seen_at, b.last_seen_at,
  b.raw_json, b.created_at, b.updated_at
from msfx_upout_bill b
on conflict (bill_id) do update
set pull_batch_id = excluded.pull_batch_id,
    bill_code = excluded.bill_code,
    bill_type = excluded.bill_type,
    bill_time = excluded.bill_time,
    bill_time_format = excluded.bill_time_format,
    bill_upload_time = excluded.bill_upload_time,
    from_ref_user_id = excluded.from_ref_user_id,
    from_ent_name = excluded.from_ent_name,
    to_ref_user_id = excluded.to_ref_user_id,
    upout_status_raw = excluded.upout_status_raw,
    first_seen_at = excluded.first_seen_at,
    last_seen_at = excluded.last_seen_at,
    raw_json = excluded.raw_json,
    created_at = excluded.created_at,
    updated_at = excluded.updated_at;

insert into msfx_upout_item_slim (
  item_id, bill_id, source_row_key,
  physic_name, package_spec, prepn_spec,
  produce_batch_no, code_count, raw_json,
  created_at, updated_at
)
select
  i.id, i.bill_id, i.source_row_key,
  i.physic_name, coalesce(i.package_spec, i.pkg_spec), i.prepn_spec,
  i.produce_batch_no, i.code_count, i.raw_json,
  i.created_at, i.updated_at
from msfx_upout_item i
on conflict (item_id) do update
set bill_id = excluded.bill_id,
    source_row_key = excluded.source_row_key,
    physic_name = excluded.physic_name,
    package_spec = excluded.package_spec,
    prepn_spec = excluded.prepn_spec,
    produce_batch_no = excluded.produce_batch_no,
    code_count = excluded.code_count,
    raw_json = excluded.raw_json,
    created_at = excluded.created_at,
    updated_at = excluded.updated_at;

insert into msfx_code_relation_slim (
  relation_id, upout_item_id, source_type,
  code, parent_code, code_level,
  prepn_spec, raw_json, created_at
)
select
  r.id, r.upout_item_id, r.source_type,
  r.code, r.parent_code, r.code_level,
  r.prepn_spec, r.raw_json, r.created_at
from msfx_code_relation r
on conflict (relation_id) do update
set upout_item_id = excluded.upout_item_id,
    source_type = excluded.source_type,
    code = excluded.code,
    parent_code = excluded.parent_code,
    code_level = excluded.code_level,
    prepn_spec = excluded.prepn_spec,
    raw_json = excluded.raw_json,
    created_at = excluded.created_at;

-- =========================
-- 3) realtime sync triggers
-- =========================
create or replace function trg_msfx_sync_upout_bill_slim()
returns trigger
language plpgsql
as $$
begin
  if tg_op = 'DELETE' then
    delete from msfx_upout_bill_slim where bill_id = old.id;
    return old;
  end if;

  insert into msfx_upout_bill_slim (
    bill_id, pull_batch_id, bill_code, bill_type,
    bill_time, bill_time_format, bill_upload_time,
    from_ref_user_id, from_ent_name, to_ref_user_id,
    upout_status_raw, first_seen_at, last_seen_at,
    raw_json, created_at, updated_at
  )
  values (
    new.id, new.pull_batch_id, new.bill_code, new.bill_type,
    new.bill_time, new.bill_time_format, new.bill_upload_time,
    new.from_ref_user_id, new.from_ent_name, new.to_ref_user_id,
    new.upout_status_raw, new.first_seen_at, new.last_seen_at,
    new.raw_json, new.created_at, new.updated_at
  )
  on conflict (bill_id) do update
  set pull_batch_id = excluded.pull_batch_id,
      bill_code = excluded.bill_code,
      bill_type = excluded.bill_type,
      bill_time = excluded.bill_time,
      bill_time_format = excluded.bill_time_format,
      bill_upload_time = excluded.bill_upload_time,
      from_ref_user_id = excluded.from_ref_user_id,
      from_ent_name = excluded.from_ent_name,
      to_ref_user_id = excluded.to_ref_user_id,
      upout_status_raw = excluded.upout_status_raw,
      first_seen_at = excluded.first_seen_at,
      last_seen_at = excluded.last_seen_at,
      raw_json = excluded.raw_json,
      created_at = excluded.created_at,
      updated_at = excluded.updated_at;

  return new;
end;
$$;

drop trigger if exists msfx_upout_bill_aiud_sync_slim on msfx_upout_bill;
create trigger msfx_upout_bill_aiud_sync_slim
after insert or update or delete on msfx_upout_bill
for each row execute function trg_msfx_sync_upout_bill_slim();

create or replace function trg_msfx_sync_upout_item_slim()
returns trigger
language plpgsql
as $$
begin
  if tg_op = 'DELETE' then
    delete from msfx_upout_item_slim where item_id = old.id;
    return old;
  end if;

  insert into msfx_upout_item_slim (
    item_id, bill_id, source_row_key,
    physic_name, package_spec, prepn_spec,
    produce_batch_no, code_count, raw_json,
    created_at, updated_at
  )
  values (
    new.id, new.bill_id, new.source_row_key,
    new.physic_name, coalesce(new.package_spec, new.pkg_spec), new.prepn_spec,
    new.produce_batch_no, new.code_count, new.raw_json,
    new.created_at, new.updated_at
  )
  on conflict (item_id) do update
  set bill_id = excluded.bill_id,
      source_row_key = excluded.source_row_key,
      physic_name = excluded.physic_name,
      package_spec = excluded.package_spec,
      prepn_spec = excluded.prepn_spec,
      produce_batch_no = excluded.produce_batch_no,
      code_count = excluded.code_count,
      raw_json = excluded.raw_json,
      created_at = excluded.created_at,
      updated_at = excluded.updated_at;

  return new;
end;
$$;

drop trigger if exists msfx_upout_item_aiud_sync_slim on msfx_upout_item;
create trigger msfx_upout_item_aiud_sync_slim
after insert or update or delete on msfx_upout_item
for each row execute function trg_msfx_sync_upout_item_slim();

create or replace function trg_msfx_sync_code_relation_slim()
returns trigger
language plpgsql
as $$
begin
  if tg_op = 'DELETE' then
    delete from msfx_code_relation_slim where relation_id = old.id;
    return old;
  end if;

  insert into msfx_code_relation_slim (
    relation_id, upout_item_id, source_type,
    code, parent_code, code_level,
    prepn_spec, raw_json, created_at
  )
  values (
    new.id, new.upout_item_id, new.source_type,
    new.code, new.parent_code, new.code_level,
    new.prepn_spec, new.raw_json, new.created_at
  )
  on conflict (relation_id) do update
  set upout_item_id = excluded.upout_item_id,
      source_type = excluded.source_type,
      code = excluded.code,
      parent_code = excluded.parent_code,
      code_level = excluded.code_level,
      prepn_spec = excluded.prepn_spec,
      raw_json = excluded.raw_json,
      created_at = excluded.created_at;

  return new;
end;
$$;

drop trigger if exists msfx_code_relation_aiud_sync_slim on msfx_code_relation;
create trigger msfx_code_relation_aiud_sync_slim
after insert or update or delete on msfx_code_relation
for each row execute function trg_msfx_sync_code_relation_slim();

-- =========================
-- 4) compact views for low-noise query
-- =========================
create or replace view msfx_upout_bill_compact as
select
  s.bill_id as id,
  s.bill_code,
  s.bill_type,
  s.bill_time,
  s.bill_upload_time,
  s.from_ent_name,
  s.from_ref_user_id,
  s.to_ref_user_id,
  s.upout_status_raw,
  s.last_seen_at,
  s.raw_json
from msfx_upout_bill_slim s;

create or replace view msfx_upout_item_compact as
select
  s.item_id as id,
  s.bill_id,
  s.source_row_key,
  s.physic_name,
  s.package_spec,
  s.prepn_spec,
  s.produce_batch_no,
  s.code_count,
  s.updated_at,
  s.raw_json
from msfx_upout_item_slim s;

create or replace view msfx_code_relation_compact as
select
  s.relation_id as id,
  s.upout_item_id,
  s.source_type,
  s.code,
  s.parent_code,
  s.code_level,
  s.prepn_spec,
  s.created_at,
  s.raw_json
from msfx_code_relation_slim s;

-- =========================
-- 5) legacy compat views (full field names)
-- =========================
create or replace view msfx_upout_bill_compat as
select
  s.bill_id as id,
  s.pull_batch_id,
  s.bill_code,
  null::bigint as bill_out_id,
  s.bill_type,
  null::text as bill_type_name,
  s.bill_time,
  s.bill_time_format,
  s.bill_upload_time,
  null::timestamptz as store_out_date,
  null::timestamptz as update_date,
  s.from_ref_user_id,
  null::text as from_user_id,
  s.from_ent_name,
  null::text as from_user_name,
  s.to_ref_user_id,
  null::text as to_user_id,
  null::text as to_user_name,
  null::text as ent_send_id,
  null::text as ent_send_name,
  null::text as ent_recv_id,
  null::text as ent_recv_name,
  null::text as ass_ent_id,
  null::text as ass_ent_name,
  null::text as ass_ref_ent_id,
  s.upout_status_raw,
  s.first_seen_at,
  s.last_seen_at,
  null::timestamptz as status_changed_at,
  s.raw_json,
  s.created_at,
  s.updated_at
from msfx_upout_bill_slim s;

create or replace view msfx_upout_item_compat as
select
  s.item_id as id,
  s.bill_id,
  s.source_row_key,
  null::text as drug_ent_base_info_id,
  null::text as prod_id,
  null::text as prod_seq_no,
  null::text as product_code,
  null::text as sub_type_no,
  s.physic_name,
  null::text as physic_info,
  null::text as physic_type,
  null::text as physic_type_name,
  s.package_spec as pkg_spec,
  s.package_spec,
  null::text as pkg_ratio,
  null::text as pkg_unit_desc,
  s.prepn_spec,
  null::text as prepn_type,
  null::text as prepn_type_desc,
  null::text as prepn_unit,
  null::text as prepn_unit_desc,
  null::text as preparations_unit,
  s.code_count,
  null::integer as prepn_count,
  null::integer as least_pkg_amount,
  null::integer as least_prepn_amount,
  s.produce_batch_no,
  null::date as produce_date,
  null::date as valid_end_date,
  null::date as exprie_date,
  null::text as approval_no,
  null::text as approve_no,
  null::text as produce_ent_name,
  null::text as product_ent_name,
  s.raw_json,
  s.created_at,
  s.updated_at
from msfx_upout_item_slim s;

create or replace view msfx_code_relation_compat as
select
  s.relation_id as id,
  s.upout_item_id,
  s.source_type,
  s.code,
  s.parent_code,
  s.code_level,
  null::text as code_pack_level,
  null::text as status_code,
  null::integer as pkg_amount,
  null::integer as prepn_amount,
  s.prepn_spec,
  null::text as code_active_info_id,
  null::timestamptz as active_date,
  null::integer as active_count,
  null::integer as small_num,
  null::integer as other_num,
  null::integer as process_count,
  null::text as process_flag,
  null::text as relation_type,
  null::text as upload_file_name,
  null::text as upload_file_path,
  s.raw_json,
  s.created_at
from msfx_code_relation_slim s;
