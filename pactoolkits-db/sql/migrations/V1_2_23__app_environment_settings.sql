create table if not exists app_environment_settings (
  environment text not null,
  setting_key text not null,
  setting_value jsonb not null default '{}'::jsonb,
  created_at timestamptz not null default now(),
  updated_at timestamptz not null default now(),

  primary key (environment, setting_key),

  constraint ck_app_environment_settings_environment_not_blank
    check (btrim(environment) <> ''),
  constraint ck_app_environment_settings_setting_key_not_blank
    check (btrim(setting_key) <> '')
);
