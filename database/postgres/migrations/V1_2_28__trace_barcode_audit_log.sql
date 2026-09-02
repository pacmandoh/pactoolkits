-- V1_2_28__trace_barcode_audit_log.sql
-- 追溯码条码生成与导出审计

create table if not exists trace_barcode_audit_log (
  id            bigserial primary key,
  batch_id      uuid not null,
  at            timestamptz not null default clock_timestamp(),
  operator_name text not null,
  action        text not null check (action in ('preview', 'export')),
  trace_code    text not null,
  drug_id       text null,
  spec          text null,
  file_name     text null,
  success       boolean not null default true,
  error         text null
);

create unique index if not exists uq_trace_barcode_audit_log_batch_action_code
  on trace_barcode_audit_log(batch_id, action, trace_code);

create index if not exists idx_trace_barcode_audit_log_trace_at
  on trace_barcode_audit_log(trace_code, at desc);

create index if not exists idx_trace_barcode_audit_log_operator_at
  on trace_barcode_audit_log(operator_name, at desc);

create index if not exists idx_trace_barcode_audit_log_at
  on trace_barcode_audit_log(at desc);
