-- V1_2_0__baseline.sql
-- Generated from legacy schema/01_tables.sql + 02_triggers.sql + 03_indexes.sql
\set ON_ERROR_STOP on

-- 01_tables.sql
-- 数据库层设置时区
-- ALTER DATABASE codepool_dev SET timezone = 'Asia/Shanghai';

-- =========================
-- 1) 药品索引表
-- =========================
create table if not exists drug_index (
  drug_id  text    not null,
  spec     text    not null,
  qty      integer not null check (qty > 0),

  rule_key text,
  pre_tc   text,
  note     text,

  created_at timestamptz not null default now(),
  updated_at timestamptz null,  -- 创建时为 NULL，更新时再写
  version    bigint not null default 0, -- 乐观锁版本

  primary key (drug_id, spec)
);

-- =========================
-- 2) 追溯码池
-- =========================
create table if not exists trace_pool (
  id         bigserial primary key,

  drug_id    text    not null,
  spec       text    not null,
  qty        integer not null check (qty > 0),

  trace_code text    not null unique,

  -- 允许 NULL（仅用于 INSERT 省略字段的过渡态），由 trigger 自动补齐 remain=qty
  remain     integer null check (remain >= 0 and remain <= qty),

  status     smallint not null default 1 check (status in (0,1)),

  in_date    timestamptz not null default now(),
  last_used  timestamptz,

  -- remain 和 status 必须匹配
  constraint ck_trace_pool_status_remain
  check (
    (remain > 0 and status = 1)
    or (remain = 0 and status = 0)
  ),

  constraint fk_trace_pool_drug
  foreign key (drug_id, spec)
    references drug_index (drug_id, spec)
    on update cascade
    on delete restrict
);

-- =========================
-- 3) 事务主表
-- =========================
create table if not exists trace_txn (
  txn_id       text primary key,  -- AHK 生成：时间戳+随机 或 GUID 字符串
  client_id    text not null,     -- 机器名/用户名

  drug_id      text not null,
  spec         text not null,
  req_qty      integer not null check (req_qty > 0),

  status       text not null default 'PENDING'
               check (status in ('PENDING','COMMITTED','ROLLED_BACK')),

  created_at   timestamptz not null default now(), -- now() 为事务属性
  committed_at timestamptz,
  note         text,

  -- 避免事务指向不存在的药品规格
  constraint fk_trace_txn_drug
  foreign key (drug_id, spec)
    references drug_index (drug_id, spec)
    on update cascade
    on delete restrict
);

-- =========================
-- 4) 事务明细表
-- =========================
create table if not exists trace_txn_item (
  txn_id     text not null,
  pool_id    bigint not null,
  take_qty   integer not null check (take_qty > 0),
  trace_code text not null,  -- 冗余：便于客户端直接取用

  primary key (txn_id, pool_id),

  constraint fk_txn_item_txn
  foreign key (txn_id)
    references trace_txn(txn_id)
    on delete cascade,

  constraint fk_txn_item_pool
  foreign key (pool_id)
    references trace_pool(id)
    on delete restrict
);

-- =========================
-- 5) 变更水位（热更新）
-- =========================
create table if not exists app_change_watermark (
  topic text primary key,
  version bigint not null default 0,
  updated_at timestamptz not null default now()
);

-- =========================
-- 6) 追溯码录入日志（原本地文件 -> DB）
-- =========================
create table if not exists trace_entry_log (
  id                  bigserial primary key,
  entry_at            timestamptz not null default now(),

  drug_id             text not null,
  spec                text not null,

  entry_count         integer not null check (entry_count >= 0),
  qty_per_trace       integer not null check (qty_per_trace >= 0),
  total_available_qty integer not null check (total_available_qty >= 0),
  failed_count        integer not null default 0 check (failed_count >= 0),

  result              text not null default '',
  txn_id              bigint null,
  client_id           text not null default '',
  source              text not null default '',
  message             text null,

  created_at          timestamptz not null default now()
);

-- =========================
-- 7) 库存归属迁移审计
-- =========================
create table if not exists inventory_reassign_audit (
  id            bigserial primary key,
  at            timestamptz not null default clock_timestamp(),
  operator_name text not null,
  source        text not null default 'inventory_ui',
  reason        text not null,

  trace_code    text not null,
  old_drug_id   text not null,
  old_spec      text not null,
  new_drug_id   text not null,
  new_spec      text not null,

  affected_rows integer not null check (affected_rows > 0),
  success       boolean not null default true,
  error         text null
);

-- =========================
-- 8) 数据库 Schema 版本（集中版本控制）
-- 单行表：只维护当前 schema 版本
-- =========================
create table if not exists schema_version (
  singleton       boolean primary key default true check (singleton),
  schema_version  text not null,
  applied_at      timestamptz not null default clock_timestamp(),
  note            text not null default ''
);

insert into schema_version(singleton, schema_version, note)
values (true, '1.2.0', 'initial baseline')
on conflict (singleton) do nothing;

-- 02_triggers.sql

-- =========================
-- A) trace_pool INSERT 时：remain 缺省则 = qty；并同步 status
-- =========================
create or replace function trg_trace_pool_init_remain_status()
returns trigger as $$
begin
  if new.remain is null then
    new.remain := new.qty;
  end if;

  new.status := case when new.remain > 0 then 1 else 0 end;

  -- in_date 已有 DEFAULT now()
  return new;
end;
$$ language plpgsql;

drop trigger if exists trace_pool_bi_init on trace_pool;

create trigger trace_pool_bi_init
before insert on trace_pool
for each row
execute function trg_trace_pool_init_remain_status();

-- =========================
-- B) trace_pool remain 变化：自动同步 status + last_used
-- =========================
create or replace function trg_trace_pool_touch_on_remain()
returns trigger as $$
begin
  -- 禁止把 remain 更新成 NULL（允许 NULL 只用于 INSERT 省略字段）
  if new.remain is null then
    raise exception 'remain cannot be NULL on UPDATE (pool_id=%)', new.id
      using errcode = '23514'; -- check_violation 语义
  end if;

  -- 只在 remain 真变化时触发（NULL 安全）
  if new.remain is distinct from old.remain then
    new.status := case when new.remain > 0 then 1 else 0 end;
  end if;

  -- 只在用完才记录 last_used，减少写字段
  if new.remain = 0 and old.remain > 0 then
    new.last_used := clock_timestamp(); -- clock_timestamp() 为事件属性
  end if;

  return new;
end;
$$ language plpgsql;

drop trigger if exists trace_pool_bu_remain on trace_pool;

create trigger trace_pool_bu_remain
before update of remain on trace_pool
for each row
execute function trg_trace_pool_touch_on_remain();

-- =========================
-- C) drug_index：更新时写 updated_at + version
-- =========================
create or replace function trg_drug_index_set_updated_at_version()
returns trigger as $$
begin
  new.updated_at := now();
  new.version := old.version + 1;
  return new;
end;
$$ language plpgsql;

drop trigger if exists drug_index_bu_updated_at on drug_index;

create trigger drug_index_bu_updated_at
before update on drug_index
for each row
when (
  (to_jsonb(new) - 'updated_at' - 'version') is distinct from (to_jsonb(old) - 'updated_at' - 'version')
)
execute function trg_drug_index_set_updated_at_version();

-- =========================
-- D) trace_txn_item：根据 pool_id 自动填充 trace_code，并校验一致性
-- =========================
create or replace function trg_txn_item_fill_trace_code()
returns trigger as $$
declare
  v_code text;
begin
  -- 从池表拿真实 trace_code
  select tp.trace_code
    into v_code
  from trace_pool tp
  where tp.id = new.pool_id;

  if v_code is null then
    raise exception 'trace_pool.id=% not found or trace_code is NULL', new.pool_id
      using errcode = '23503'; -- foreign_key_violation 语义
  end if;

  -- 如果应用没传 trace_code：直接填
  if new.trace_code is null or new.trace_code = '' then
    new.trace_code := v_code;
  else
    -- 如果应用传了：强校验必须一致（不一致直接报错）
    if new.trace_code is distinct from v_code then
      raise exception 'trace_code mismatch: pool_id=% expected=% got=%',
        new.pool_id, v_code, new.trace_code
        using errcode = '23514'; -- check_violation 语义
    end if;
  end if;

  return new;
end;
$$ language plpgsql;

drop trigger if exists trace_txn_item_biu_fill_code on trace_txn_item;

create trigger trace_txn_item_biu_fill_code
before insert or update of pool_id, trace_code on trace_txn_item
for each row
execute function trg_txn_item_fill_trace_code();

-- =========================
-- E) 热更新变更水位 + NOTIFY
-- =========================
create or replace function app_touch_watermark(_topic text)
returns void
language plpgsql
as $$
begin
  insert into app_change_watermark(topic, version, updated_at)
  values (_topic, 1, clock_timestamp())
  on conflict (topic) do update
    set version = app_change_watermark.version + 1,
        updated_at = excluded.updated_at;

  perform pg_notify('pactoolkits_change', _topic);
end;
$$;

create or replace function trg_touch_watermark()
returns trigger
language plpgsql
as $$
begin
  perform app_touch_watermark(TG_ARGV[0]);
  return null;
end;
$$;

drop trigger if exists trace_pool_as_touch_watermark on trace_pool;
create trigger trace_pool_as_touch_watermark
after insert or update or delete on trace_pool
for each statement
execute function trg_touch_watermark('inventory');

drop trigger if exists trace_txn_as_touch_watermark on trace_txn;
create trigger trace_txn_as_touch_watermark
after insert or update or delete on trace_txn
for each statement
execute function trg_touch_watermark('inventory');

drop trigger if exists trace_txn_item_as_touch_watermark on trace_txn_item;
create trigger trace_txn_item_as_touch_watermark
after insert or update or delete on trace_txn_item
for each statement
execute function trg_touch_watermark('inventory');

drop trigger if exists drug_index_as_touch_watermark on drug_index;
create trigger drug_index_as_touch_watermark
after insert or update or delete on drug_index
for each statement
execute function trg_touch_watermark('drug_index');

drop trigger if exists trace_entry_log_as_touch_watermark on trace_entry_log;
create trigger trace_entry_log_as_touch_watermark
after insert or update or delete on trace_entry_log
for each statement
execute function trg_touch_watermark('inventory');

-- 03_indexes.sql

-- =========================
-- 索引
-- =========================
create index if not exists idx_txn_status_created
on trace_txn(status, created_at);

-- FK 列建索引
create index if not exists idx_txn_item_txn
on trace_txn_item(txn_id);

create index if not exists idx_txn_item_pool
on trace_txn_item(pool_id);

-- 可选：同一 txn 内 trace_code 不重复
-- create unique index if not exists uq_txn_item_txn_trace_code
-- on trace_txn_item(txn_id, trace_code);

-- 待定（EXPLAIN）
-- create index if not exists idx_trace_drug_status_remain
-- on trace_pool(drug_id, status, remain);

-- 核心取码索引
create index if not exists idx_trace_pick_ultra
on trace_pool (
  drug_id,
  spec,
  ((remain < qty) is true) desc,
  in_date,
  coalesce(last_used, 'epoch'::timestamptz),
  id
)
where remain > 0;

-- 变更水位读取（轮询兜底）
create index if not exists idx_watermark_updated_at
on app_change_watermark(updated_at desc);

-- trace_entry_log 查询（Dashboard）
create index if not exists idx_trace_entry_log_entry_at
on trace_entry_log(entry_at desc);

create index if not exists idx_trace_entry_log_drug_spec_entry_at
on trace_entry_log(drug_id, spec, entry_at desc);

create index if not exists idx_trace_entry_log_client_entry_at
on trace_entry_log(client_id, entry_at desc);

-- =========================
-- 序列自检/修复（避免 trace_pool.id 与序列错位）
-- 可反复执行：每次将 nextval 对齐到 max(id)+1
-- =========================
do $$
begin
  if to_regclass('public.trace_pool') is not null then
    perform setval(
      pg_get_serial_sequence('public.trace_pool', 'id'),
      coalesce((select max(id) from public.trace_pool), 0) + 1,
      false
    );
  end if;
end
$$;

-- inventory_reassign_audit 查询
create index if not exists idx_reassign_audit_at
on inventory_reassign_audit(at desc);

create index if not exists idx_reassign_audit_operator_at
on inventory_reassign_audit(operator_name, at desc);

create index if not exists idx_reassign_audit_trace_code
on inventory_reassign_audit(trace_code, at desc);

-- =========================
-- Schema 版本写入（迁移末尾）
-- 每次 schema 变更后请同步更新该版本号
-- =========================
insert into schema_version(singleton, schema_version, applied_at, note)
values (true, '1.2.0', clock_timestamp(), 'schema apply: 01/02/03 baseline')
on conflict (singleton) do update
  set schema_version = excluded.schema_version,
      applied_at = excluded.applied_at,
      note = excluded.note;
