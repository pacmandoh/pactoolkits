-- V1_2_1__drug_key_fix_audit.sql
-- 药品主键纠错迁移审计

create table if not exists drug_key_fix_audit (
  id                  bigserial primary key,
  at                  timestamptz not null default clock_timestamp(),
  operator_name       text not null,
  source              text not null default 'drug_index_ui',
  reason              text not null,

  old_drug_id         text not null,
  old_spec            text not null,
  new_drug_id         text not null,
  new_spec            text not null,

  target_existed      boolean not null default false,
  trace_pool_affected integer not null check (trace_pool_affected >= 0),
  trace_txn_affected  integer not null check (trace_txn_affected >= 0),

  success             boolean not null default true,
  error               text null
);

create index if not exists idx_drug_key_fix_audit_at
on drug_key_fix_audit(at desc);

create index if not exists idx_drug_key_fix_audit_operator_at
on drug_key_fix_audit(operator_name, at desc);

create index if not exists idx_drug_key_fix_audit_old_key
on drug_key_fix_audit(old_drug_id, old_spec, at desc);

create index if not exists idx_drug_key_fix_audit_new_key
on drug_key_fix_audit(new_drug_id, new_spec, at desc);
