-- app_environment_settings stores deployment environment flags for migration policy.
create table if not exists public.app_environment_settings (
  key   text primary key,
  value text not null default ''
);
