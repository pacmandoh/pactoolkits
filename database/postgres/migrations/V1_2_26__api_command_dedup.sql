-- PacApi 写命令持久化幂等（client_id + operation + command_id）
create table if not exists api_command_dedup (
  client_id text not null,
  operation text not null,
  command_id uuid not null,
  request_digest text not null,
  -- 0=in_progress, 1=completed
  status smallint not null,
  response_status int null,
  response_content_type text null,
  response_body bytea null,
  created_at timestamptz not null default now(),
  completed_at timestamptz null,
  primary key (client_id, operation, command_id)
);

create index if not exists ix_api_command_dedup_created
  on api_command_dedup (created_at);
