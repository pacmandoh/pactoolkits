-- 人工重开已成功的注入任务。
-- 仅允许 SUCCESS -> NEW，重置任务明细与 staging，使其重新进入可执行队列。

create or replace function msfx_reopen_inject_task(
  p_task_id bigint,
  p_operator text default null,
  p_reason text default null
)
returns table(
  task_id bigint,
  task_status text,
  total_codes integer
)
language plpgsql
as $$
declare
  v_message text;
begin
  if p_task_id is null or p_task_id <= 0 then
    raise exception 'p_task_id must be positive';
  end if;

  if not exists (
    select 1
    from msfx_inject_task t
    where t.id = p_task_id
      and t.status = 'SUCCESS'
  ) then
    raise exception 'task % is not in SUCCESS status', p_task_id;
  end if;

  v_message := 'task reopened manually';
  if nullif(btrim(coalesce(p_operator, '')), '') is not null then
    v_message := v_message || ': ' || btrim(p_operator);
  end if;
  if nullif(btrim(coalesce(p_reason, '')), '') is not null then
    v_message := v_message || ' (' || btrim(p_reason) || ')';
  end if;

  return query
  with reset_code as (
    update msfx_inject_task_code tc
    set
      status = 'PENDING',
      injected_at = null,
      verify_result = null,
      err_msg = null
    where tc.task_id = p_task_id
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
      status = 'NEW',
      client_id = null,
      picked_at = null,
      finished_at = null,
      success_codes = 0,
      failed_codes = 0,
      err_msg = null
    where t.id = p_task_id
      and t.status = 'SUCCESS'
    returning t.id, t.status, t.total_codes
  ),
  evt as (
    insert into msfx_inject_event(task_id, stage, level, message)
    select
      u.id,
      'DB_SYNC',
      'WARN',
      v_message
    from upd u
    returning 1
  )
  select
    u.id as task_id,
    u.status as task_status,
    u.total_codes
  from upd u;
end;
$$;
