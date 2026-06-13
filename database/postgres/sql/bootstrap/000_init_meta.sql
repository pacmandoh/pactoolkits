-- bootstrap/000_init_meta.sql
-- Meta tables for migration tracking and schema version control

create table if not exists schema_migrations (
  version       text primary key,
  name          text not null,
  checksum      text not null,
  installed_at  timestamptz not null default clock_timestamp(),
  success       boolean not null default true,
  note          text not null default ''
);

create table if not exists schema_version (
  singleton       boolean primary key default true check (singleton),
  schema_version  text not null,
  applied_at      timestamptz not null default clock_timestamp(),
  note            text not null default ''
);

create unique index if not exists idx_schema_migrations_installed_at
on schema_migrations(installed_at desc);
