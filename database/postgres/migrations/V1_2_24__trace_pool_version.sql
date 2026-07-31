-- trace_pool 乐观锁：多客户端编辑防静默覆盖
alter table trace_pool
  add column if not exists version bigint not null default 0; -- 乐观锁版本

-- 任意业务字段变更时自增（与 drug_index 同模式；SQL 亦可显式 version = version + 1）
create or replace function trg_trace_pool_bump_version()
returns trigger as $$
begin
  new.version := old.version + 1;
  return new;
end;
$$ language plpgsql;

drop trigger if exists trace_pool_bu_version on trace_pool;

create trigger trace_pool_bu_version
before update on trace_pool
for each row
when (
  (to_jsonb(new) - 'version') is distinct from (to_jsonb(old) - 'version')
)
execute function trg_trace_pool_bump_version();
