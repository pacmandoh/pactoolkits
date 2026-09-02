\set ON_ERROR_STOP on
\pset pager off

-- Structural existence checks (read-only)
do $$
begin
  if to_regclass('public.schema_migrations') is null then
    raise exception 'missing table: schema_migrations';
  end if;

  if to_regclass('public.schema_version') is null then
    raise exception 'missing table: schema_version';
  end if;

  if to_regclass('public.drug_index') is null then
    raise exception 'missing table: drug_index';
  end if;

  if to_regclass('public.trace_pool') is null then
    raise exception 'missing table: trace_pool';
  end if;

  if to_regclass('public.trace_txn') is null then
    raise exception 'missing table: trace_txn';
  end if;

  if to_regclass('public.trace_txn_item') is null then
    raise exception 'missing table: trace_txn_item';
  end if;

  if to_regclass('public.app_change_watermark') is null then
    raise exception 'missing table: app_change_watermark';
  end if;

  if to_regclass('public.trace_entry_log') is null then
    raise exception 'missing table: trace_entry_log';
  end if;

  if to_regclass('public.inventory_reassign_audit') is null then
    raise exception 'missing table: inventory_reassign_audit';
  end if;

  if to_regclass('public.drug_key_fix_audit') is null then
    raise exception 'missing table: drug_key_fix_audit';
  end if;

  if to_regclass('public.api_command_dedup') is null then
    raise exception 'missing table: api_command_dedup';
  end if;

  if to_regclass('public.trace_barcode_audit_log') is null then
    raise exception 'missing table: trace_barcode_audit_log';
  end if;

  -- 1.2.23 引入、1.2.25 已删除；防止残留
  if to_regclass('public.app_environment_settings') is not null then
    raise exception 'obsolete table present: app_environment_settings';
  end if;
end $$;

select 'OK: structure' as verify;
