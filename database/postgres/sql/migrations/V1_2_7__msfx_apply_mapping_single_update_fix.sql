-- V1_2_7__msfx_apply_mapping_single_update_fix.sql
-- 修复 msfx_apply_mapping 在单条语句中对 msfx_code_staging 重复 UPDATE 导致第二次更新失效
-- 验证结果：修复前 processed=0 且 mapped/review 可为 null；修复后 processed=2680、mapped=560、review=2120、pending=0（测试库）

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
  normalized as (
    select
      b.id,
      b.drug_name_raw,
      b.spec_raw,
      msfx_norm_name(b.drug_name_raw) as name_norm,
      msfx_norm_spec(b.spec_raw, b.pkg_spec) as spec_norm
    from base b
  ),
  resolved as (
    select
      n.id,
      n.drug_name_raw,
      n.spec_raw,
      n.name_norm,
      n.spec_norm,
      coalesce(mr.target_drug_id, di.drug_id) as target_drug_id,
      coalesce(mr.target_spec, di.spec) as target_spec
    from normalized n
    left join lateral (
      select m.target_drug_id, m.target_spec
      from msfx_map_rule m
      where m.enabled
        and msfx_norm_name(m.source_drug_name) = n.name_norm
        and msfx_norm_spec(m.source_spec, null) = n.spec_norm
      order by m.priority asc, m.id asc
      limit 1
    ) mr on true
    left join lateral (
      select d.drug_id, d.spec
      from drug_index d
      where msfx_norm_name(d.drug_id) = n.name_norm
        and msfx_norm_spec(d.spec, null) = n.spec_norm
      order by d.drug_id, d.spec
      limit 1
    ) di on mr.target_drug_id is null
  ),
  decided as (
    select
      r.id,
      r.drug_name_raw,
      r.spec_raw,
      r.name_norm,
      r.spec_norm,
      r.target_drug_id,
      r.target_spec,
      case
        when coalesce(r.name_norm, '') = '' then 'EMPTY_NAME_NORM'
        when coalesce(r.spec_norm, '') = '' then 'EMPTY_SPEC_NORM'
        when r.target_drug_id is null or r.target_spec is null then 'NO_TARGET_MATCH'
        else null
      end as reason_code,
      case
        when coalesce(r.name_norm, '') = '' then '药名归一化为空，需人工指定映射'
        when coalesce(r.spec_norm, '') = '' then '规格归一化为空，需人工指定映射'
        when r.target_drug_id is null or r.target_spec is null then '未命中 map_rule 或 drug_index'
        else null
      end as reason_detail
    from resolved r
  ),
  upd as (
    update msfx_code_staging s
    set
      source_drug_name_raw = d.drug_name_raw,
      source_spec_raw = d.spec_raw,
      source_name_norm = d.name_norm,
      source_spec_norm = d.spec_norm,
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
    coalesce(sum(case when map_status = 'MAPPED' then 1 else 0 end), 0)::int as mapped_count,
    coalesce(sum(case when map_status = 'NEED_REVIEW' then 1 else 0 end), 0)::int as review_count
  from upd;
end;
$$;
