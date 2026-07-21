-- V1_2_9__msfx_retry_queue_and_cursor_success_only.sql
-- 目标：
-- 1) 拉取游标仅在 SUCCESS 批次推进，避免 PARTIAL 推进导致漏拉
-- 2) 新增失败单据重试队列表，避免重复回扫整窗

create table if not exists msfx_pull_bill_retry (
  id bigserial primary key,
  source_api text not null,
  bill_code text not null,
  from_ref_user_id text,
  to_ref_user_id text,
  retry_count integer not null default 0,
  next_retry_at timestamptz not null default clock_timestamp(),
  first_failed_at timestamptz not null default clock_timestamp(),
  last_failed_at timestamptz not null default clock_timestamp(),
  last_error text,
  created_at timestamptz not null default clock_timestamp(),
  updated_at timestamptz not null default clock_timestamp(),
  constraint uq_msfx_pull_bill_retry unique (source_api, bill_code)
);

create index if not exists idx_msfx_pull_bill_retry_due
  on msfx_pull_bill_retry(source_api, next_retry_at, id);

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
  if p_batch_status <> 'SUCCESS' then
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
