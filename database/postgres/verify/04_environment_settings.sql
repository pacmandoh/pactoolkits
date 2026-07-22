\set ON_ERROR_STOP on
\pset pager off

-- Structural checks for environment metadata (read-only).
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
      and column_name = 'environment'
      and data_type = 'text'
      and is_nullable = 'NO'
  ) then
    raise exception 'invalid column: app_environment_settings.environment';
  end if;

  if not exists (
    select 1
    from information_schema.columns
    where table_schema = 'public'
      and table_name = 'app_environment_settings'
      and column_name = 'setting_key'
      and data_type = 'text'
      and is_nullable = 'NO'
  ) then
    raise exception 'invalid column: app_environment_settings.setting_key';
  end if;

  if not exists (
    select 1
    from information_schema.columns
    where table_schema = 'public'
      and table_name = 'app_environment_settings'
      and column_name = 'setting_value'
      and udt_name = 'jsonb'
      and is_nullable = 'NO'
  ) then
    raise exception 'invalid column: app_environment_settings.setting_value';
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
    having count(*) = 2
       and count(*) filter (where kcu.column_name in ('environment', 'setting_key')) = 2
  ) into has_primary_key;

  if not has_primary_key then
    raise exception 'missing primary key: app_environment_settings(environment, setting_key)';
  end if;
end $$;

select 'OK: environment settings' as verify;
