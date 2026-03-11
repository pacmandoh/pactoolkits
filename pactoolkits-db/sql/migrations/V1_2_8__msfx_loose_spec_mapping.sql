-- V1_2_8__msfx_loose_spec_mapping.sql
-- 目标：
-- 1) 新增宽松规格归一化函数 msfx_norm_spec_loose
-- 2) 重写 msfx_apply_mapping：严格匹配优先，宽松匹配兜底，宽松多命中进入 NEED_REVIEW

create or replace function msfx_norm_spec_loose(prepn_spec text, pkg_spec text)
returns text
language sql
immutable
as $$
with src as (
  select
    trim(coalesce(prepn_spec, '')) as p_raw,
    trim(coalesce(pkg_spec, '')) as k_raw
),
norm as (
  select
    lower(
      regexp_replace(
        replace(replace(replace(p_raw, '∶', ':'), '：', ':'), '×', '*'),
        '\s+',
        '',
        'g'
      )
    ) as p1,
    lower(
      regexp_replace(
        replace(replace(replace(k_raw, '×', '*'), 'x', '*'), '＊', '*'),
        '\s+',
        '',
        'g'
      )
    ) as k1
  from src
),
strip_bracket as (
  select
    regexp_replace(regexp_replace(p1, '[（(][^）)]*[）)]', '', 'g'), '[，,;；]+$', '', 'g') as p2,
    k1
  from norm
),
main_spec as (
  select
    case
      when p2 ~ '^[0-9.]+(ml|l|mg|g|iu|μg|ug|%)[:][0-9.]+(mg|g|ml|iu|μg|ug|%)' then
        regexp_replace(
          p2,
          '^([0-9.]+(?:ml|l|mg|g|iu|μg|ug|%)[:][0-9.]+(?:mg|g|ml|iu|μg|ug|%)).*$',
          '\1'
        )
      else p2
    end as p3,
    k1
  from strip_bracket
),
pack as (
  select
    p3,
    case
      when k1 ~ '(\d+)(片|粒|支|瓶|袋|丸|盒|板)' then
        regexp_replace(k1, '.*?(\d+)(片|粒|支|瓶|袋|丸|盒|板).*', '\1\2')
      else null
    end as pack_unit
  from main_spec
)
select nullif(
  case
    when coalesce(p3, '') <> '' and pack_unit is not null then p3 || '*' || pack_unit
    when coalesce(p3, '') <> '' then p3
    else pack_unit
  end,
  ''
)
from pack;
$$;

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
      b.pkg_spec,
      msfx_norm_name(b.drug_name_raw) as name_norm,
      msfx_norm_spec(b.spec_raw, b.pkg_spec) as spec_norm,
      msfx_norm_spec_loose(b.spec_raw, b.pkg_spec) as spec_norm_loose
    from base b
  ),
  resolved as (
    select
      n.id,
      n.drug_name_raw,
      n.spec_raw,
      n.name_norm,
      n.spec_norm,
      n.spec_norm_loose,
      sr.target_drug_id as strict_rule_drug_id,
      sr.target_spec as strict_rule_spec,
      sd.drug_id as strict_di_drug_id,
      sd.spec as strict_di_spec,
      lr.target_drug_id as loose_rule_drug_id,
      lr.target_spec as loose_rule_spec,
      coalesce(lrc.rule_cnt, 0) as loose_rule_cnt,
      ld.drug_id as loose_di_drug_id,
      ld.spec as loose_di_spec,
      coalesce(ldc.di_cnt, 0) as loose_di_cnt
    from normalized n
    left join lateral (
      select m.target_drug_id, m.target_spec
      from msfx_map_rule m
      where m.enabled
        and msfx_norm_name(m.source_drug_name) = n.name_norm
        and msfx_norm_spec(m.source_spec, null) = n.spec_norm
      order by m.priority asc, m.id asc
      limit 1
    ) sr on true
    left join lateral (
      select d.drug_id, d.spec
      from drug_index d
      where msfx_norm_name(d.drug_id) = n.name_norm
        and msfx_norm_spec(d.spec, null) = n.spec_norm
      order by d.drug_id, d.spec
      limit 1
    ) sd on sr.target_drug_id is null
    left join lateral (
      select count(*)::int as rule_cnt
      from msfx_map_rule m
      where m.enabled
        and msfx_norm_name(m.source_drug_name) = n.name_norm
        and msfx_norm_spec_loose(m.source_spec, null) = n.spec_norm_loose
    ) lrc on (sr.target_drug_id is null and sd.drug_id is null)
    left join lateral (
      select m.target_drug_id, m.target_spec
      from msfx_map_rule m
      where m.enabled
        and msfx_norm_name(m.source_drug_name) = n.name_norm
        and msfx_norm_spec_loose(m.source_spec, null) = n.spec_norm_loose
      order by m.priority asc, m.id asc
      limit 1
    ) lr on (sr.target_drug_id is null and sd.drug_id is null and coalesce(lrc.rule_cnt, 0) = 1)
    left join lateral (
      select count(*)::int as di_cnt
      from drug_index d
      where msfx_norm_name(d.drug_id) = n.name_norm
        and msfx_norm_spec_loose(d.spec, null) = n.spec_norm_loose
    ) ldc on (sr.target_drug_id is null and sd.drug_id is null and coalesce(lrc.rule_cnt, 0) = 0)
    left join lateral (
      select d.drug_id, d.spec
      from drug_index d
      where msfx_norm_name(d.drug_id) = n.name_norm
        and msfx_norm_spec_loose(d.spec, null) = n.spec_norm_loose
      order by d.drug_id, d.spec
      limit 1
    ) ld on (sr.target_drug_id is null and sd.drug_id is null and coalesce(lrc.rule_cnt, 0) = 0 and coalesce(ldc.di_cnt, 0) = 1)
  ),
  decided as (
    select
      r.id,
      r.drug_name_raw,
      r.spec_raw,
      r.name_norm,
      r.spec_norm,
      coalesce(
        r.strict_rule_drug_id,
        r.strict_di_drug_id,
        r.loose_rule_drug_id,
        r.loose_di_drug_id
      ) as target_drug_id,
      coalesce(
        r.strict_rule_spec,
        r.strict_di_spec,
        r.loose_rule_spec,
        r.loose_di_spec
      ) as target_spec,
      case
        when coalesce(r.name_norm, '') = '' then 'EMPTY_NAME_NORM'
        when coalesce(r.spec_norm, '') = '' and coalesce(r.spec_norm_loose, '') = '' then 'EMPTY_SPEC_NORM'
        when r.strict_rule_drug_id is null and r.strict_di_drug_id is null and r.loose_rule_cnt > 1 then 'AMBIGUOUS_RULE_LOOSE'
        when r.strict_rule_drug_id is null and r.strict_di_drug_id is null and r.loose_rule_cnt = 0 and r.loose_di_cnt > 1 then 'AMBIGUOUS_DI_LOOSE'
        when coalesce(
          r.strict_rule_drug_id,
          r.strict_di_drug_id,
          r.loose_rule_drug_id,
          r.loose_di_drug_id
        ) is null then 'NO_TARGET_MATCH'
        else null
      end as reason_code,
      case
        when coalesce(r.name_norm, '') = '' then '药名归一化为空，需人工指定映射'
        when coalesce(r.spec_norm, '') = '' and coalesce(r.spec_norm_loose, '') = '' then '规格归一化为空，需人工指定映射'
        when r.strict_rule_drug_id is null and r.strict_di_drug_id is null and r.loose_rule_cnt > 1 then '宽松规则命中多条，需人工确认'
        when r.strict_rule_drug_id is null and r.strict_di_drug_id is null and r.loose_rule_cnt = 0 and r.loose_di_cnt > 1 then '宽松药品库命中多条，需人工确认'
        when coalesce(
          r.strict_rule_drug_id,
          r.strict_di_drug_id,
          r.loose_rule_drug_id,
          r.loose_di_drug_id
        ) is null then '未命中 map_rule 或 drug_index'
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
