\set ON_ERROR_STOP on

begin;

create table if not exists public.app_environment_settings (
  key text primary key,
  value text not null default ''
);

insert into public.app_environment_settings(key, value)
values
  ('Database.Environment', 'isolated'),
  ('Database.AllowBetaMigrations', 'true'),
  ('Database.Source', :'database_source'),
  ('Database.BetaVersion', :'beta_version')
on conflict (key) do update
set value = excluded.value;

commit;
