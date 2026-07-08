-- 任务结算与 warehouse_bill_no 原子写入。
-- 解决 SUCCESS 已提交但 warehouse_bill_no 独立回写失败导致的防重失效问题。

drop function if exists msfx_finalize_inject_task(bigint, text);

create or replace function msfx_finalize_inject_task(
  p_task_id bigint,
  p_err_msg text default null,
  p_warehouse_bill_no text default null
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
declare
  v_bill_no text;
begin
  v_bill_no := nullif(btrim(coalesce(p_warehouse_bill_no, '')), '');

  return query
  with stats as (
    select
      p_task_id as id,
      count(*)::int as total_n,
      count(*) filter (where tc.status = 'INJECTED')::int as succ_n,
      count(*) filter (where tc.status in ('FAILED', 'SKIPPED'))::int as fail_n
    from msfx_inject_task_code tc
    where tc.task_id = p_task_id
  ),
  upd as (
    update msfx_inject_task t
    set
      total_codes = s.total_n,
      success_codes = s.succ_n,
      failed_codes = s.fail_n,
      status = case
                 when s.total_n > 0 and s.succ_n = s.total_n then 'SUCCESS'
                 else 'FAILED'
               end,
      finished_at = now(),
      warehouse_bill_no = case
                            when s.total_n > 0 and s.succ_n = s.total_n and v_bill_no is not null
                              then v_bill_no
                            else t.warehouse_bill_no
                          end,
      err_msg = case
                  when p_err_msg is not null then p_err_msg
                  when s.fail_n > 0 then coalesce(t.err_msg, 'failed detail exists')
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
      case when u.status = 'SUCCESS' then 'INFO' else 'ERR' end,
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
