-- V1_2_4__msfx_mapping_reason_and_batch_meta.sql
-- 映射原因可解释化 + 批次请求元信息补齐

-- =========================
-- 0) remove slim layer, slim original tables directly
-- =========================
drop trigger if exists msfx_upout_bill_aiud_sync_slim on msfx_upout_bill;
drop trigger if exists msfx_upout_item_aiud_sync_slim on msfx_upout_item;
drop trigger if exists msfx_code_relation_aiud_sync_slim on msfx_code_relation;

drop function if exists trg_msfx_sync_upout_bill_slim();
drop function if exists trg_msfx_sync_upout_item_slim();
drop function if exists trg_msfx_sync_code_relation_slim();

drop view if exists msfx_code_relation_compat;
drop view if exists msfx_upout_item_compat;
drop view if exists msfx_upout_bill_compat;
drop view if exists msfx_code_relation_compact;
drop view if exists msfx_upout_item_compact;
drop view if exists msfx_upout_bill_compact;

drop table if exists msfx_code_relation_slim;
drop table if exists msfx_upout_item_slim;
drop table if exists msfx_upout_bill_slim;

alter table if exists msfx_upout_bill
  drop column if exists bill_out_id,
  drop column if exists bill_type_name,
  drop column if exists store_out_date,
  drop column if exists update_date,
  drop column if exists from_user_id,
  drop column if exists from_user_name,
  drop column if exists to_user_id,
  drop column if exists to_user_name,
  drop column if exists ent_send_id,
  drop column if exists ent_send_name,
  drop column if exists ent_recv_id,
  drop column if exists ent_recv_name,
  drop column if exists ass_ent_id,
  drop column if exists ass_ent_name,
  drop column if exists ass_ref_ent_id;

alter table if exists msfx_upout_item
  drop column if exists prod_id,
  drop column if exists prod_seq_no,
  drop column if exists product_code,
  drop column if exists sub_type_no,
  drop column if exists physic_info,
  drop column if exists physic_type,
  drop column if exists physic_type_name,
  drop column if exists pkg_ratio,
  drop column if exists pkg_unit_desc,
  drop column if exists prepn_type,
  drop column if exists prepn_type_desc,
  drop column if exists prepn_unit,
  drop column if exists prepn_unit_desc,
  drop column if exists preparations_unit,
  drop column if exists prepn_count,
  drop column if exists least_pkg_amount,
  drop column if exists least_prepn_amount,
  drop column if exists produce_date,
  drop column if exists valid_end_date,
  drop column if exists exprie_date,
  drop column if exists approval_no,
  drop column if exists approve_no,
  drop column if exists produce_ent_name,
  drop column if exists product_ent_name;

alter table if exists msfx_code_relation
  drop column if exists parent_code,
  drop column if exists code_level,
  drop column if exists code_pack_level,
  drop column if exists pkg_amount,
  drop column if exists prepn_amount,
  drop column if exists code_active_info_id,
  drop column if exists active_date,
  drop column if exists active_count,
  drop column if exists small_num,
  drop column if exists other_num,
  drop column if exists process_count,
  drop column if exists process_flag,
  drop column if exists relation_type,
  drop column if exists upload_file_name,
  drop column if exists upload_file_path;

alter table if exists msfx_code_relation
  add column if not exists code_level_1 text null,
  add column if not exists code_level_2 text null,
  add column if not exists code_level_3 text null,
  add column if not exists code_level_4 text null,
  add column if not exists code_level_5 text null;

update msfx_code_relation r
set code_level_1 = coalesce(r.code_level_1, r.code)
where r.code_level_1 is null;

create index if not exists idx_msfx_code_relation_l1
on msfx_code_relation(code_level_1);

create index if not exists idx_msfx_code_relation_l2
on msfx_code_relation(code_level_2);

alter table if exists msfx_code_staging
  add column if not exists source_code_level_1 text null,
  add column if not exists source_code_level_2 text null,
  add column if not exists source_code_level_3 text null,
  add column if not exists source_code_level_4 text null,
  add column if not exists source_code_level_5 text null;

alter table if exists msfx_code_staging
  add column if not exists map_reason_code text null;

alter table if exists msfx_code_staging
  add column if not exists map_reason_detail text null;

create index if not exists idx_msfx_code_staging_reason
on msfx_code_staging(map_status, map_reason_code, updated_at desc);

create or replace function msfx_apply_mapping(p_limit integer default 5000)
returns table(processed_count integer, mapped_count integer, review_count integer)
language plpgsql
as $$
begin
  return query
  with base as (
    select
      s.id,
      coalesce(nullif(s.source_drug_name_raw, ''), i.physic_name, '') as drug_name_raw,
      coalesce(nullif(s.source_spec_raw, ''), i.prepn_spec, '') as spec_raw,
      i.pkg_spec
    from msfx_code_staging s
    left join msfx_code_relation r on r.id = s.source_relation_id
    left join msfx_upout_item i on i.id = r.upout_item_id
    where s.map_status = 'PENDING'
    order by s.created_at, s.id
    limit greatest(coalesce(p_limit, 0), 0)
  ),
  normed as (
    update msfx_code_staging s
    set
      source_drug_name_raw = b.drug_name_raw,
      source_spec_raw = b.spec_raw,
      source_name_norm = msfx_norm_name(b.drug_name_raw),
      source_spec_norm = msfx_norm_spec(b.spec_raw, b.pkg_spec),
      updated_at = now()
    from base b
    where s.id = b.id
    returning s.id, s.source_name_norm, s.source_spec_norm
  ),
  resolved as (
    select
      n.id,
      n.source_name_norm,
      n.source_spec_norm,
      coalesce(mr.target_drug_id, di.drug_id) as target_drug_id,
      coalesce(mr.target_spec, di.spec) as target_spec
    from normed n
    left join lateral (
      select m.target_drug_id, m.target_spec
      from msfx_map_rule m
      where m.enabled
        and msfx_norm_name(m.source_drug_name) = n.source_name_norm
        and msfx_norm_spec(m.source_spec, null) = n.source_spec_norm
      order by m.priority asc, m.id asc
      limit 1
    ) mr on true
    left join lateral (
      select d.drug_id, d.spec
      from drug_index d
      where msfx_norm_name(d.drug_id) = n.source_name_norm
        and msfx_norm_spec(d.spec, null) = n.source_spec_norm
      order by d.drug_id, d.spec
      limit 1
    ) di on mr.target_drug_id is null
  ),
  decided as (
    select
      r.id,
      r.target_drug_id,
      r.target_spec,
      case
        when coalesce(r.source_name_norm, '') = '' then 'EMPTY_NAME_NORM'
        when coalesce(r.source_spec_norm, '') = '' then 'EMPTY_SPEC_NORM'
        when r.target_drug_id is null or r.target_spec is null then 'NO_TARGET_MATCH'
        else null
      end as reason_code,
      case
        when coalesce(r.source_name_norm, '') = '' then '药名归一化为空，需人工指定映射'
        when coalesce(r.source_spec_norm, '') = '' then '规格归一化为空，需人工指定映射'
        when r.target_drug_id is null or r.target_spec is null then '未命中 map_rule 或 drug_index'
        else null
      end as reason_detail
    from resolved r
  ),
  upd as (
    update msfx_code_staging s
    set
      mapped_drug_id = d.target_drug_id,
      mapped_spec = d.target_spec,
      map_status = case
                     when d.target_drug_id is not null and d.target_spec is not null then 'MAPPED'
                     else 'NEED_REVIEW'
                   end,
      map_reason_code = d.reason_code,
      map_reason_detail = d.reason_detail,
      err_msg = case when d.reason_detail is null then null else d.reason_detail end,
      updated_at = now()
    from decided d
    where s.id = d.id
    returning s.map_status
  )
  select
    count(*)::int as processed_count,
    count(*) filter (where map_status = 'MAPPED')::int as mapped_count,
    count(*) filter (where map_status = 'NEED_REVIEW')::int as review_count
  from upd;
end;
$$;

create or replace function msfx_apply_manual_mapping_by_norm(
  p_name_norm text,
  p_spec_norm text,
  p_target_drug_id text,
  p_target_spec text,
  p_save_rule boolean default true
)
returns integer
language plpgsql
as $$
declare
  v_affected integer := 0;
begin
  if trim(coalesce(p_name_norm, '')) = '' or trim(coalesce(p_spec_norm, '')) = '' then
    raise exception 'name_norm/spec_norm cannot be empty';
  end if;
  if trim(coalesce(p_target_drug_id, '')) = '' or trim(coalesce(p_target_spec, '')) = '' then
    raise exception 'target_drug_id/target_spec cannot be empty';
  end if;

  if not exists (
    select 1 from drug_index d
    where d.drug_id = trim(p_target_drug_id)
      and d.spec = trim(p_target_spec)
  ) then
    raise exception 'target drug/spec not exists in drug_index';
  end if;

  update msfx_code_staging s
  set
    mapped_drug_id = trim(p_target_drug_id),
    mapped_spec = trim(p_target_spec),
    map_status = 'MAPPED',
    map_reason_code = null,
    map_reason_detail = null,
    err_msg = null,
    code_status = case when s.code_status in ('FAILED', 'DUPLICATE') then 'NEW' else s.code_status end,
    updated_at = now()
  where s.source_name_norm = trim(p_name_norm)
    and s.source_spec_norm = trim(p_spec_norm)
    and s.map_status in ('PENDING', 'NEED_REVIEW', 'FAILED');

  get diagnostics v_affected = row_count;

  if p_save_rule then
    insert into msfx_map_rule(
      rule_type, source_drug_name, source_spec,
      target_drug_id, target_spec, priority, enabled, note
    )
    values (
      'COMBINED', trim(p_name_norm), trim(p_spec_norm),
      trim(p_target_drug_id), trim(p_target_spec),
      20, true, 'auto saved by batch manual mapping'
    )
    on conflict do nothing;
  end if;

  return v_affected;
end;
$$;
