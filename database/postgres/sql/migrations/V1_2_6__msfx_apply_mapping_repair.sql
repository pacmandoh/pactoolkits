-- V1_2_6__msfx_apply_mapping_repair.sql
-- 修复 msfx_apply_mapping 可能因历史版本漂移导致的 no-op
-- superseded by V1_2_7__msfx_apply_mapping_single_update_fix.sql

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
