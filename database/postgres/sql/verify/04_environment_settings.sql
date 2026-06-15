\set ON_ERROR_STOP on
\pset pager off

-- Structural checks for migration policy metadata (read-only).
do $$
declare
  has_primary_key boolean;
begin
  if to_regclass('public.app_environment_settings') is null then
    raise exception 'missing table: app_environment_settings';
  end if;

  if not exists (
    select 1
    from information_schema.columns
    where table_schema = 'public'
      and table_name = 'app_environment_settings'
      and column_name = 'key'
      and data_type = 'text'
      and is_nullable = 'NO'
  ) then
    raise exception 'invalid column: app_environment_settings.key';
  end if;

  if not exists (
    select 1
    from information_schema.columns
    where table_schema = 'public'
      and table_name = 'app_environment_settings'
      and column_name = 'value'
      and data_type = 'text'
      and is_nullable = 'NO'
  ) then
    raise exception 'invalid column: app_environment_settings.value';
  end if;

  select exists (
    select 1
    from information_schema.table_constraints tc
    join information_schema.key_column_usage kcu
      on kcu.constraint_catalog = tc.constraint_catalog
     and kcu.constraint_schema = tc.constraint_schema
     and kcu.constraint_name = tc.constraint_name
    where tc.table_schema = 'public'
      and tc.table_name = 'app_environment_settings'
      and tc.constraint_type = 'PRIMARY KEY'
    group by tc.constraint_catalog, tc.constraint_schema, tc.constraint_name
    having count(*) = 1 and min(kcu.column_name) = 'key'
  ) into has_primary_key;

  if not has_primary_key then
    raise exception 'missing primary key: app_environment_settings(key)';
  end if;
end $$;

select 'OK: environment settings' as verify;
