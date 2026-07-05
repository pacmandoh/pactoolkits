-- 支持手动将执行队列任务退回映射结果队列，并允许重开已弃用任务

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
      and t.status in ('SUCCESS', 'DISCARDED')
  ) then
    raise exception 'task % is not in SUCCESS/DISCARDED status', p_task_id;
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
      and t.status in ('SUCCESS', 'DISCARDED')
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

create or replace function msfx_remap_inject_task(
  p_task_id bigint,
  p_operator text default null,
  p_reason text default null
)
returns table(
  task_id bigint,
  task_status text,
  total_codes integer,
  reset_staging_count integer
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
      and t.status in ('NEW', 'FAILED', 'DISCARDED', 'SUCCESS', 'CANCELLED')
  ) then
    raise exception 'task % is not eligible for remap', p_task_id;
  end if;

  v_reason := nullif(btrim(coalesce(p_reason, '')), '');
  v_message := 'task returned to mapping queue manually';
  if nullif(btrim(coalesce(p_operator, '')), '') is not null then
    v_message := v_message || ': ' || btrim(p_operator);
  end if;
  if v_reason is not null then
    v_message := v_message || ' (' || v_reason || ')';
  end if;

  return query
  with target_codes as (
    select tc.task_id, tc.staging_id
    from msfx_inject_task_code tc
    where tc.task_id = p_task_id
      and tc.staging_id is not null
  ),
  reset_staging as (
    update msfx_code_staging s
    set
      mapped_drug_id = null,
      mapped_spec = null,
      map_status = 'PENDING',
      code_status = 'NEW',
      inject_task_id = null,
      map_reason_code = 'MANUAL_REMAP',
      map_reason_detail = coalesce(v_reason, 'manual remap from task queue'),
      err_msg = null,
      updated_at = now()
    from target_codes tc
    where s.id = tc.staging_id
    returning s.id
  ),
  del_code as (
    delete from msfx_inject_task_code tc
    where tc.task_id = p_task_id
    returning 1
  ),
  upd as (
    update msfx_inject_task t
    set
      status = 'CANCELLED',
      client_id = null,
      picked_at = null,
      finished_at = now(),
      success_codes = 0,
      failed_codes = 0,
      err_msg = coalesce(v_reason, 'task returned to mapping queue manually')
    where t.id = p_task_id
      and t.status in ('NEW', 'FAILED', 'DISCARDED', 'SUCCESS', 'CANCELLED')
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
    u.total_codes,
    (select count(*)::int from reset_staging) as reset_staging_count
  from upd u;
end;
$$;
