create or replace function msfx_task_parent_cluster_key(
  p_l1 text,
  p_l2 text,
  p_l3 text,
  p_l4 text,
  p_l5 text,
  p_leaf text
)
returns text
language sql
immutable
as $$
  select coalesce(
    nullif(btrim(coalesce(p_l5, '')), ''),
    nullif(btrim(coalesce(p_l4, '')), ''),
    nullif(btrim(coalesce(p_l3, '')), ''),
    nullif(btrim(coalesce(p_l2, '')), ''),
    nullif(btrim(coalesce(p_l1, '')), ''),
    nullif(btrim(coalesce(p_leaf, '')), '')
  );
$$;

create or replace function msfx_merge_inject_tasks(
  p_task_ids bigint[],
  p_operator text default null,
  p_reason text default null
)
returns table(
  result_task_id bigint,
  result_task_status text,
  result_total_codes integer,
  result_merged_task_count integer
)
language plpgsql
as $$
declare
  v_input_count integer;
  v_eligible_count integer;
  v_distinct_target_count integer;
  v_mapped_drug_id text;
  v_mapped_spec text;
  v_task_type text;
  v_priority integer;
  v_max_retry integer;
  v_bill_id bigint;
  v_source_bill_code text;
  v_total_codes integer;
  v_new_task_id bigint;
  v_new_task_status text;
  v_message text;
begin
  select count(*)
  into v_input_count
  from (
    select distinct x as task_id
    from unnest(coalesce(p_task_ids, '{}'::bigint[])) as t(x)
    where x is not null and x > 0
  ) q;

  if coalesce(v_input_count, 0) < 2 then
    raise exception 'at least two task ids are required';
  end if;

  select
    count(*)::int,
    count(distinct t.mapped_drug_id || '|' || t.mapped_spec)::int,
    min(t.mapped_drug_id),
    min(t.mapped_spec),
    min(t.task_type),
    max(t.priority),
    max(t.max_retry)
  into
    v_eligible_count,
    v_distinct_target_count,
    v_mapped_drug_id,
    v_mapped_spec,
    v_task_type,
    v_priority,
    v_max_retry
  from msfx_inject_task t
  join (
    select distinct x as task_id
    from unnest(p_task_ids) as q(x)
    where x is not null and x > 0
  ) ids on ids.task_id = t.id
  where t.status in ('NEW', 'FAILED', 'DISCARDED', 'CANCELLED');

  if coalesce(v_eligible_count, 0) <> v_input_count then
    raise exception 'selected tasks must all be in NEW/FAILED/DISCARDED/CANCELLED status';
  end if;

  if coalesce(v_distinct_target_count, 0) <> 1 then
    raise exception 'only tasks with the same mapped drug/spec can be merged';
  end if;

  select
    case when count(distinct t.bill_id) = 1 then min(t.bill_id) else null end,
    coalesce(
      string_agg(
        distinct nullif(btrim(coalesce(t.source_bill_code, '')), ''),
        ' / ' order by nullif(btrim(coalesce(t.source_bill_code, '')), '')
      ),
      '--'
    ),
    count(*)::int
  into
    v_bill_id,
    v_source_bill_code,
    v_total_codes
  from msfx_inject_task t
  join (
    select distinct x as task_id
    from unnest(p_task_ids) as q(x)
    where x is not null and x > 0
  ) ids on ids.task_id = t.id
  join msfx_inject_task_code tc on tc.task_id = t.id;

  if coalesce(v_total_codes, 0) <= 0 then
    raise exception 'selected tasks do not contain any task codes';
  end if;

  v_message := 'tasks merged manually';
  if nullif(btrim(coalesce(p_operator, '')), '') is not null then
    v_message := v_message || ': ' || btrim(p_operator);
  end if;
  if nullif(btrim(coalesce(p_reason, '')), '') is not null then
    v_message := v_message || ' (' || btrim(p_reason) || ')';
  end if;

  lock table msfx_inject_task in share row exclusive mode;
  lock table msfx_inject_task_code in share row exclusive mode;

  with input_ids as materialized (
    select distinct x as task_id
    from unnest(p_task_ids) as q(x)
    where x is not null and x > 0
  ),
  source_codes as materialized (
    select
      tc.task_id as source_task_id,
      tc.leaf_code,
      tc.staging_id,
      tc.seq,
      t.queue_seq,
      s.created_at
    from msfx_inject_task_code tc
    join input_ids ids on ids.task_id = tc.task_id
    join msfx_inject_task t on t.id = tc.task_id
    left join msfx_code_staging s on s.id = tc.staging_id
  ),
  del_code as (
    delete from msfx_inject_task_code tc
    using input_ids ids
    where tc.task_id = ids.task_id
    returning 1
  ),
  seq_base as (
    select coalesce(max(t.queue_seq), 0)::bigint as base_seq
    from msfx_inject_task t
  ),
  ins_task as (
    insert into msfx_inject_task (
      task_type,
      status,
      bill_id,
      source_bill_code,
      mapped_drug_id,
      mapped_spec,
      priority,
      retry_count,
      max_retry,
      queue_seq,
      total_codes
    )
    select
      coalesce(v_task_type, 'MSFX_INBOUND'),
      'NEW',
      v_bill_id,
      v_source_bill_code,
      v_mapped_drug_id,
      v_mapped_spec,
      coalesce(v_priority, 100),
      0,
      coalesce(v_max_retry, 3),
      sb.base_seq + 1,
      v_total_codes
    from seq_base sb
    returning
      msfx_inject_task.id,
      msfx_inject_task.status,
      msfx_inject_task.total_codes
  ),
  ins_code as (
    insert into msfx_inject_task_code (
      task_id,
      leaf_code,
      staging_id,
      seq,
      status
    )
    select
      t.id,
      s.leaf_code,
      s.staging_id,
      row_number() over (
        order by s.queue_seq, s.seq, coalesce(s.created_at, now()), s.leaf_code
      )::int,
      'PENDING'
    from source_codes s
    cross join ins_task t
    cross join (select count(*) from del_code) dep
    on conflict (task_id, leaf_code) do nothing
    returning task_id, staging_id
  ),
  upd_stage as (
    update msfx_code_staging s
    set
      inject_task_id = i.task_id,
      code_status = 'TASKED',
      err_msg = null,
      updated_at = now()
    from ins_code i
    where s.id = i.staging_id
    returning s.id
  ),
  upd_old as (
    update msfx_inject_task t
    set
      status = 'CANCELLED',
      client_id = null,
      picked_at = null,
      finished_at = now(),
      success_codes = 0,
      failed_codes = 0,
      err_msg = coalesce(nullif(btrim(coalesce(p_reason, '')), ''), 'merged into a new task')
    from input_ids ids
    where t.id = ids.task_id
    returning t.id
  ),
  evt_old as (
    insert into msfx_inject_event(task_id, stage, level, message)
    select id, 'DB_SYNC', 'WARN', v_message
    from upd_old
    returning 1
  ),
  evt_new as (
    insert into msfx_inject_event(task_id, stage, level, message)
    select id, 'DB_SYNC', 'INFO', 'merged task created'
    from ins_task
    returning 1
  )
  select
    it.id as new_task_id,
    it.status as new_task_status,
    it.total_codes as new_total_codes
  into
    v_new_task_id,
    v_new_task_status,
    v_total_codes
  from ins_task it;

  return query
  select
    v_new_task_id,
    coalesce(v_new_task_status, 'NEW'),
    coalesce(v_total_codes, 0),
    v_input_count;
end;
$$;

create or replace function msfx_split_inject_task_custom(
  p_task_id bigint,
  p_group_keys text[],
  p_bucket_indexes integer[],
  p_operator text default null,
  p_reason text default null
)
returns table(
  result_created_tasks integer,
  result_total_codes integer,
  result_bucket_count integer
)
language plpgsql
as $$
declare
  v_status text;
  v_task_type text;
  v_mapped_drug_id text;
  v_mapped_spec text;
  v_priority integer;
  v_max_retry integer;
  v_total_codes integer;
  v_input_count integer;
  v_distinct_bucket_count integer;
  v_source_group_count integer;
  v_assigned_group_count integer;
  v_missing_group_count integer;
  v_unknown_group_count integer;
  v_message text;
begin
  if p_task_id is null or p_task_id <= 0 then
    raise exception 'p_task_id must be positive';
  end if;

  if coalesce(array_length(p_group_keys, 1), 0) = 0
     or coalesce(array_length(p_group_keys, 1), 0) <> coalesce(array_length(p_bucket_indexes, 1), 0) then
    raise exception 'group key assignment is invalid';
  end if;

  select
    t.status,
    t.task_type,
    t.mapped_drug_id,
    t.mapped_spec,
    t.priority,
    t.max_retry,
    t.total_codes
  into
    v_status,
    v_task_type,
    v_mapped_drug_id,
    v_mapped_spec,
    v_priority,
    v_max_retry,
    v_total_codes
  from msfx_inject_task t
  where t.id = p_task_id;

  if not found then
    raise exception 'task % does not exist', p_task_id;
  end if;

  if v_status not in ('NEW', 'FAILED', 'DISCARDED', 'CANCELLED') then
    raise exception 'task % is not eligible for custom split', p_task_id;
  end if;

  if coalesce(v_total_codes, 0) <= 1 then
    raise exception 'task % does not have enough codes to split', p_task_id;
  end if;

  select count(*)::int, count(distinct bucket_index)::int
  into v_input_count, v_distinct_bucket_count
  from (
    select
      nullif(btrim(coalesce(gk, '')), '') as group_key,
      bi as bucket_index
    from unnest(p_group_keys, p_bucket_indexes) as t(gk, bi)
    where nullif(btrim(coalesce(gk, '')), '') is not null
      and bi is not null
      and bi > 0
  ) q;

  if coalesce(v_input_count, 0) = 0 or coalesce(v_distinct_bucket_count, 0) < 2 then
    raise exception 'custom split requires at least two valid buckets';
  end if;

  with source_groups as (
    select
      msfx_task_parent_cluster_key(
        s.source_code_level_1,
        s.source_code_level_2,
        s.source_code_level_3,
        s.source_code_level_4,
        s.source_code_level_5,
        tc.leaf_code
      ) as group_key
    from msfx_inject_task_code tc
    join msfx_code_staging s on s.id = tc.staging_id
    where tc.task_id = p_task_id
    group by
      msfx_task_parent_cluster_key(
        s.source_code_level_1,
        s.source_code_level_2,
        s.source_code_level_3,
        s.source_code_level_4,
        s.source_code_level_5,
        tc.leaf_code
      )
  ),
  dedup_assign as (
    select
      nullif(btrim(coalesce(gk, '')), '') as group_key
    from unnest(p_group_keys) as t(gk)
    where nullif(btrim(coalesce(gk, '')), '') is not null
    group by nullif(btrim(coalesce(gk, '')), '')
  )
  select
    (select count(*)::int from source_groups),
    (select count(*)::int from dedup_assign),
    (select count(*)::int from source_groups sg left join dedup_assign da on da.group_key = sg.group_key where da.group_key is null),
    (select count(*)::int from dedup_assign da left join source_groups sg on sg.group_key = da.group_key where sg.group_key is null)
  into
    v_source_group_count,
    v_assigned_group_count,
    v_missing_group_count,
    v_unknown_group_count;

  if coalesce(v_missing_group_count, 0) > 0 then
    raise exception 'custom split is missing % parent-cluster assignment(s)', v_missing_group_count;
  end if;
  if coalesce(v_unknown_group_count, 0) > 0 then
    raise exception 'custom split contains unknown parent-cluster assignment(s)';
  end if;
  if coalesce(v_source_group_count, 0) <> coalesce(v_assigned_group_count, 0) then
    raise exception 'custom split assignment count mismatch';
  end if;

  v_message := 'task custom split manually';
  if nullif(btrim(coalesce(p_operator, '')), '') is not null then
    v_message := v_message || ': ' || btrim(p_operator);
  end if;
  if nullif(btrim(coalesce(p_reason, '')), '') is not null then
    v_message := v_message || ' (' || btrim(p_reason) || ')';
  end if;

  lock table msfx_inject_task in share row exclusive mode;
  lock table msfx_inject_task_code in share row exclusive mode;

  return query
  with input_assign as materialized (
    select
      nullif(btrim(coalesce(gk, '')), '') as group_key,
      bi::int as bucket_index
    from unnest(p_group_keys, p_bucket_indexes) as t(gk, bi)
    where nullif(btrim(coalesce(gk, '')), '') is not null
      and bi is not null
      and bi > 0
  ),
  dedup_assign as materialized (
    select group_key, min(bucket_index)::int as bucket_index
    from input_assign
    group by group_key
  ),
  source_codes as materialized (
    select
      tc.leaf_code,
      tc.staging_id,
      tc.seq,
      coalesce(s.created_at, now()) as created_at,
      s.source_bill_code,
      b.id as bill_id,
      msfx_task_parent_cluster_key(
        s.source_code_level_1,
        s.source_code_level_2,
        s.source_code_level_3,
        s.source_code_level_4,
        s.source_code_level_5,
        tc.leaf_code
      ) as cluster_key
    from msfx_inject_task_code tc
    join msfx_code_staging s on s.id = tc.staging_id
    left join msfx_upout_bill b on b.bill_code = s.source_bill_code
    where tc.task_id = p_task_id
  ),
  source_groups as materialized (
    select
      sc.cluster_key as group_key,
      count(*)::int as code_count
    from source_codes sc
    group by sc.cluster_key
  ),
  grouped_source as materialized (
    select
      da.bucket_index,
      sc.leaf_code,
      sc.staging_id,
      sc.seq,
      sc.created_at,
      sc.source_bill_code,
      sc.bill_id
    from source_codes sc
    join dedup_assign da on da.group_key = sc.cluster_key
  ),
  del_code as (
    delete from msfx_inject_task_code tc
    where tc.task_id = p_task_id
    returning 1
  ),
  grouped_meta as (
    select
      g.bucket_index,
      case when count(distinct g.bill_id) = 1 then min(g.bill_id) else null end as bill_id,
      coalesce(
        string_agg(
          distinct nullif(btrim(coalesce(g.source_bill_code, '')), ''),
          ' / ' order by nullif(btrim(coalesce(g.source_bill_code, '')), '')
        ),
        '--'
      ) as source_bill_code,
      min(g.created_at) as first_at,
      min(g.staging_id) as first_staging_id,
      count(*)::int as total_codes
    from grouped_source g
    group by g.bucket_index
  ),
  seq_base as (
    select coalesce(max(t.queue_seq), 0)::bigint as base_seq
    from msfx_inject_task t
  ),
  numbered_groups as (
    select
      g.bucket_index,
      g.bill_id,
      g.source_bill_code,
      g.total_codes,
      (sb.base_seq + row_number() over (order by g.bucket_index, g.first_at, g.first_staging_id))::bigint as queue_seq
    from grouped_meta g
    cross join seq_base sb
  ),
  ins_task as (
    insert into msfx_inject_task (
      task_type,
      status,
      bill_id,
      source_bill_code,
      mapped_drug_id,
      mapped_spec,
      priority,
      retry_count,
      max_retry,
      queue_seq,
      total_codes
    )
    select
      v_task_type,
      'NEW',
      g.bill_id,
      g.source_bill_code,
      v_mapped_drug_id,
      v_mapped_spec,
      coalesce(v_priority, 100),
      0,
      coalesce(v_max_retry, 3),
      g.queue_seq,
      g.total_codes
    from numbered_groups g
    returning id, queue_seq
  ),
  ins_code as (
    insert into msfx_inject_task_code (
      task_id,
      leaf_code,
      staging_id,
      seq,
      status
    )
    select
      t.id,
      g.leaf_code,
      g.staging_id,
      row_number() over (
        partition by t.id
        order by g.created_at, g.seq, g.leaf_code
      )::int,
      'PENDING'
    from grouped_source g
    join numbered_groups ng on ng.bucket_index = g.bucket_index
    join ins_task t on t.queue_seq = ng.queue_seq
    cross join (select count(*) from del_code) dep
    on conflict (task_id, leaf_code) do nothing
    returning task_id, staging_id
  ),
  upd_stage as (
    update msfx_code_staging s
    set
      inject_task_id = i.task_id,
      code_status = 'TASKED',
      err_msg = null,
      updated_at = now()
    from ins_code i
    where s.id = i.staging_id
    returning s.id
  ),
  upd_old as (
    update msfx_inject_task t
    set
      status = 'CANCELLED',
      client_id = null,
      picked_at = null,
      finished_at = now(),
      success_codes = 0,
      failed_codes = 0,
      err_msg = coalesce(nullif(btrim(coalesce(p_reason, '')), ''), 'split into custom groups')
    where t.id = p_task_id
    returning t.id
  ),
  evt_old as (
    insert into msfx_inject_event(task_id, stage, level, message)
    select id, 'DB_SYNC', 'WARN', v_message
    from upd_old
    returning 1
  ),
  evt_new as (
    insert into msfx_inject_event(task_id, stage, level, message)
    select id, 'DB_SYNC', 'INFO', 'custom split task created'
    from ins_task
    returning 1
  )
  select
    (select count(*)::int from ins_task) as created_tasks,
    (select count(*)::int from upd_stage) as total_codes,
    (select count(distinct bucket_index)::int from dedup_assign) as bucket_count;
end;
$$;

create or replace function msfx_split_inject_task(
  p_task_id bigint,
  p_split_mode text,
  p_operator text default null,
  p_reason text default null
)
returns table(
  result_created_tasks integer,
  result_total_codes integer,
  result_split_mode text
)
language plpgsql
as $$
declare
  v_mode text;
  v_status text;
  v_task_type text;
  v_mapped_drug_id text;
  v_mapped_spec text;
  v_priority integer;
  v_max_retry integer;
  v_total_codes integer;
  v_group_count integer;
  v_message text;
begin
  if p_task_id is null or p_task_id <= 0 then
    raise exception 'p_task_id must be positive';
  end if;

  v_mode := upper(btrim(coalesce(p_split_mode, '')));
  if v_mode not in ('BATCH', 'PARENT_CLUSTER') then
    raise exception 'unsupported split mode: %', p_split_mode;
  end if;

  select
    t.status,
    t.task_type,
    t.mapped_drug_id,
    t.mapped_spec,
    t.priority,
    t.max_retry,
    t.total_codes
  into
    v_status,
    v_task_type,
    v_mapped_drug_id,
    v_mapped_spec,
    v_priority,
    v_max_retry,
    v_total_codes
  from msfx_inject_task t
  where t.id = p_task_id;

  if not found then
    raise exception 'task % does not exist', p_task_id;
  end if;

  if v_status not in ('NEW', 'FAILED', 'DISCARDED', 'CANCELLED') then
    raise exception 'task % is not eligible for split', p_task_id;
  end if;

  if coalesce(v_total_codes, 0) <= 1 then
    raise exception 'task % does not have enough codes to split', p_task_id;
  end if;

  if v_mode = 'PARENT_CLUSTER' then
    select count(distinct msfx_task_parent_cluster_key(
      s.source_code_level_1,
      s.source_code_level_2,
      s.source_code_level_3,
      s.source_code_level_4,
      s.source_code_level_5,
      tc.leaf_code
    ))::int
    into v_group_count
    from msfx_inject_task_code tc
    join msfx_code_staging s on s.id = tc.staging_id
    where tc.task_id = p_task_id;

    if coalesce(v_group_count, 0) <= 1 then
      raise exception 'task % has no separable parent-code clusters', p_task_id;
    end if;
  else
    if exists (
      with source_codes as (
        select
          msfx_task_parent_cluster_key(
            s.source_code_level_1,
            s.source_code_level_2,
            s.source_code_level_3,
            s.source_code_level_4,
            s.source_code_level_5,
            tc.leaf_code
          ) as cluster_key,
          coalesce(nullif(btrim(i.produce_batch_no), ''), '__EMPTY__') as batch_key
        from msfx_inject_task_code tc
        join msfx_code_staging s on s.id = tc.staging_id
        left join msfx_code_relation r on r.id = s.source_relation_id
        left join msfx_upout_item i on i.id = r.upout_item_id
        where tc.task_id = p_task_id
      )
      select 1
      from source_codes
      group by cluster_key
      having count(distinct batch_key) > 1
    ) then
      raise exception 'same parent cluster spans multiple batches, cannot split by batch safely';
    end if;

    select count(distinct coalesce(nullif(btrim(i.produce_batch_no), ''), '__EMPTY__'))::int
    into v_group_count
    from msfx_inject_task_code tc
    join msfx_code_staging s on s.id = tc.staging_id
    left join msfx_code_relation r on r.id = s.source_relation_id
    left join msfx_upout_item i on i.id = r.upout_item_id
    where tc.task_id = p_task_id;

    if coalesce(v_group_count, 0) <= 1 then
      raise exception 'task % has no separable batches', p_task_id;
    end if;
  end if;

  v_message := 'task split manually';
  if nullif(btrim(coalesce(p_operator, '')), '') is not null then
    v_message := v_message || ': ' || btrim(p_operator);
  end if;
  if nullif(btrim(coalesce(p_reason, '')), '') is not null then
    v_message := v_message || ' (' || btrim(p_reason) || ')';
  end if;

  lock table msfx_inject_task in share row exclusive mode;
  lock table msfx_inject_task_code in share row exclusive mode;

  return query
  with source_codes as materialized (
    select
      tc.leaf_code,
      tc.staging_id,
      tc.seq,
      coalesce(s.created_at, now()) as created_at,
      s.source_bill_code,
      coalesce(nullif(btrim(i.produce_batch_no), ''), '__EMPTY__') as batch_key,
      msfx_task_parent_cluster_key(
        s.source_code_level_1,
        s.source_code_level_2,
        s.source_code_level_3,
        s.source_code_level_4,
        s.source_code_level_5,
        tc.leaf_code
      ) as cluster_key,
      b.id as bill_id
    from msfx_inject_task_code tc
    join msfx_code_staging s on s.id = tc.staging_id
    left join msfx_code_relation r on r.id = s.source_relation_id
    left join msfx_upout_item i on i.id = r.upout_item_id
    left join msfx_upout_bill b on b.bill_code = s.source_bill_code
    where tc.task_id = p_task_id
  ),
  grouped_source as (
    select
      case when v_mode = 'BATCH' then batch_key else cluster_key end as group_key,
      leaf_code,
      staging_id,
      seq,
      created_at,
      source_bill_code,
      bill_id
    from source_codes
  ),
  del_code as (
    delete from msfx_inject_task_code tc
    where tc.task_id = p_task_id
    returning 1
  ),
  grouped_meta as (
    select
      g.group_key,
      case when count(distinct g.bill_id) = 1 then min(g.bill_id) else null end as bill_id,
      coalesce(
        string_agg(
          distinct nullif(btrim(coalesce(g.source_bill_code, '')), ''),
          ' / ' order by nullif(btrim(coalesce(g.source_bill_code, '')), '')
        ),
        '--'
      ) as source_bill_code,
      min(g.created_at) as first_at,
      min(g.staging_id) as first_staging_id,
      count(*)::int as total_codes
    from grouped_source g
    group by g.group_key
  ),
  seq_base as (
    select coalesce(max(t.queue_seq), 0)::bigint as base_seq
    from msfx_inject_task t
  ),
  numbered_groups as (
    select
      g.group_key,
      g.bill_id,
      g.source_bill_code,
      g.total_codes,
      (sb.base_seq + row_number() over (order by g.first_at, g.first_staging_id))::bigint as queue_seq
    from grouped_meta g
    cross join seq_base sb
  ),
  ins_task as (
    insert into msfx_inject_task (
      task_type,
      status,
      bill_id,
      source_bill_code,
      mapped_drug_id,
      mapped_spec,
      priority,
      retry_count,
      max_retry,
      queue_seq,
      total_codes
    )
    select
      v_task_type,
      'NEW',
      g.bill_id,
      g.source_bill_code,
      v_mapped_drug_id,
      v_mapped_spec,
      coalesce(v_priority, 100),
      0,
      coalesce(v_max_retry, 3),
      g.queue_seq,
      g.total_codes
    from numbered_groups g
    returning id, queue_seq
  ),
  ins_code as (
    insert into msfx_inject_task_code (
      task_id,
      leaf_code,
      staging_id,
      seq,
      status
    )
    select
      t.id,
      g.leaf_code,
      g.staging_id,
      row_number() over (
        partition by t.id
        order by g.created_at, g.seq, g.leaf_code
      )::int,
      'PENDING'
    from grouped_source g
    join numbered_groups ng on ng.group_key = g.group_key
    join ins_task t on t.queue_seq = ng.queue_seq
    cross join (select count(*) from del_code) dep
    on conflict (task_id, leaf_code) do nothing
    returning task_id, staging_id
  ),
  upd_stage as (
    update msfx_code_staging s
    set
      inject_task_id = i.task_id,
      code_status = 'TASKED',
      err_msg = null,
      updated_at = now()
    from ins_code i
    where s.id = i.staging_id
    returning s.id
  ),
  upd_old as (
    update msfx_inject_task t
    set
      status = 'CANCELLED',
      client_id = null,
      picked_at = null,
      finished_at = now(),
      success_codes = 0,
      failed_codes = 0,
      err_msg = coalesce(nullif(btrim(coalesce(p_reason, '')), ''), 'split into multiple tasks')
    where t.id = p_task_id
    returning t.id
  ),
  evt_old as (
    insert into msfx_inject_event(task_id, stage, level, message)
    select id, 'DB_SYNC', 'WARN', v_message
    from upd_old
    returning 1
  ),
  evt_new as (
    insert into msfx_inject_event(task_id, stage, level, message)
    select id, 'DB_SYNC', 'INFO', 'split task created'
    from ins_task
    returning 1
  )
  select
    (select count(*)::int from ins_task) as created_tasks,
    (select count(*)::int from upd_stage) as total_codes,
    v_mode as split_mode;
end;
$$;
