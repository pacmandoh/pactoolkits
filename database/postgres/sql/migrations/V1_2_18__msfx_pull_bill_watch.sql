-- V1_2_18__msfx_pull_bill_watch.sql
-- 目标：
-- 1) 为“已看见但当时未入库”的单据建立待确认池
-- 2) 支撑自动巡检在后续轮次按 bill_code 进行补偿重查

create table if not exists msfx_pull_bill_watch (
  id bigserial primary key,
  source_api text not null,
  bill_code text not null,
  from_ref_user_id text,
  to_ref_user_id text,
  from_ent_name text,
  bill_type text,
  bill_time text,
  bill_upload_time text,
  last_seen_status text,
  raw_json jsonb not null default '{}'::jsonb,
  retry_count integer not null default 0,
  next_check_at timestamptz not null default clock_timestamp(),
  state text not null default 'WATCHING'
    check (state in ('WATCHING', 'RESOLVED')),
  first_seen_at timestamptz not null default clock_timestamp(),
  last_seen_at timestamptz not null default clock_timestamp(),
  resolved_at timestamptz,
  last_error text,
  created_at timestamptz not null default clock_timestamp(),
  updated_at timestamptz not null default clock_timestamp(),
  constraint uq_msfx_pull_bill_watch unique (source_api, bill_code)
);

create index if not exists idx_msfx_pull_bill_watch_due
  on msfx_pull_bill_watch(source_api, state, next_check_at, id);

create index if not exists idx_msfx_pull_bill_watch_bill
  on msfx_pull_bill_watch(bill_code);
