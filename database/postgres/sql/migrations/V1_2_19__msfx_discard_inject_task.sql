-- 为注入任务增加手动弃用终态。
-- 仅允许 NEW / FAILED -> DISCARDED，弃用后不再参与 agent claim。

alter table if exists msfx_inject_task
  drop constraint if exists msfx_inject_task_status_check;

alter table if exists msfx_inject_task
  add constraint msfx_inject_task_status_check
  check (status in ('NEW', 'RUNNING', 'SUCCESS', 'FAILED', 'CANCELLED', 'DISCARDED'));

create or replace function msfx_discard_inject_task(
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
  v_reason text;
begin
  if p_task_id is null or p_task_id <= 0 then
    raise exception 'p_task_id must be positive';
  end if;

  if not exists (
    select 1
    from msfx_inject_task t
    where t.id = p_task_id
      and t.status in ('NEW', 'FAILED')
  ) then
    raise exception 'task % is not in NEW/FAILED status', p_task_id;
  end if;

  v_reason := nullif(btrim(coalesce(p_reason, '')), '');
  v_message := 'task discarded manually';
  if nullif(btrim(coalesce(p_operator, '')), '') is not null then
    v_message := v_message || ': ' || btrim(p_operator);
  end if;
  if v_reason is not null then
    v_message := v_message || ' (' || v_reason || ')';
  end if;

  return query
  with upd as (
    update msfx_inject_task t
    set
      status = 'DISCARDED',
      client_id = null,
      picked_at = null,
      finished_at = now(),
      err_msg = case
                  when v_reason is not null then v_reason
                  when nullif(btrim(coalesce(t.err_msg, '')), '') is not null then t.err_msg
                  else 'task discarded manually'
                end
    where t.id = p_task_id
      and t.status in ('NEW', 'FAILED')
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
