create or replace function msfx_claim_inject_task_by_target(
  p_client_id text,
  p_mapped_drug_id text,
  p_mapped_spec text
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

  if trim(coalesce(p_mapped_drug_id, '')) = '' then
    raise exception 'p_mapped_drug_id cannot be empty';
  end if;

  if trim(coalesce(p_mapped_spec, '')) = '' then
    raise exception 'p_mapped_spec cannot be empty';
  end if;

  return query
  with picked as (
    select
      t.id,
      t.status as prev_status
    from msfx_inject_task t
    where (
            t.status = 'NEW'
         or (
            t.status = 'FAILED'
          and exists (
                select 1
                from msfx_inject_task_code tc
                where tc.task_id = t.id
                  and tc.status = 'FAILED'
              )
         )
          )
      and btrim(t.mapped_drug_id) = btrim(p_mapped_drug_id)
      and btrim(t.mapped_spec) = btrim(p_mapped_spec)
    order by
      t.created_at,
      t.id
    for update skip locked
    limit 1
  ),
  reset_code as (
    update msfx_inject_task_code tc
    set
      status = 'PENDING',
      injected_at = null,
      verify_result = null,
      err_msg = null
    from picked p
    where tc.task_id = p.id
      and p.prev_status = 'FAILED'
      and tc.status = 'FAILED'
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
      status = 'RUNNING',
      client_id = p_client_id,
      picked_at = coalesce(t.picked_at, now()),
      finished_at = null,
      err_msg = null
    from picked p
    where t.id = p.id
    returning t.id, t.bill_id, t.source_bill_code, t.mapped_drug_id, t.mapped_spec, t.total_codes
  ),
  evt as (
    insert into msfx_inject_event(task_id, stage, level, message)
    select
      u.id,
      'DB_SYNC',
      'INFO',
      case
        when exists (select 1 from reset_code rc where rc.task_id = u.id)
          then 'task re-claimed by target: ' || p_client_id
        else 'task claimed by target: ' || p_client_id
      end
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
