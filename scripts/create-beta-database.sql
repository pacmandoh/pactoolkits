\set ON_ERROR_STOP on

begin;

insert into public.app_environment_settings(environment, setting_key, setting_value)
values
  ('isolated', 'Database.Environment', '"isolated"'::jsonb),
  ('isolated', 'Database.Source', to_jsonb(:'database_source'::text)),
  ('isolated', 'Database.BetaVersion', to_jsonb(:'beta_version'::text))
on conflict (environment, setting_key) do update
set setting_value = excluded.setting_value,
    updated_at = now();

commit;
