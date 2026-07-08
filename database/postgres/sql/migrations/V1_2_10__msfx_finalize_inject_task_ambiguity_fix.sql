-- 修复 msfx_finalize_inject_task 中 task_id 歧义引用（PL/pgSQL 变量 vs 列名）
-- 说明：该问题会导致结算 SQL 报错并使任务状态无法正常收敛。

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
      case when u.status = 'SUCCESS' then 'INFO' when u.status = 'PARTIAL' then 'WARN' else 'ERR' end,
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
