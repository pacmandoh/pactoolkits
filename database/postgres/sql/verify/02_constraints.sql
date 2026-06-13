\set ON_ERROR_STOP on
\pset pager off

-- Read-only data invariants (safe for non-empty production tables)
do $$
declare
  bad_count bigint;
begin
  select count(*) into bad_count
  from trace_pool
  where not (
    (remain > 0 and status = 1)
    or (remain = 0 and status = 0)
  );

  if bad_count > 0 then
    raise exception 'trace_pool invariant violation rows=%', bad_count;
  end if;

  select count(*) into bad_count
  from trace_txn
  where status not in ('PENDING','COMMITTED','ROLLED_BACK');

  if bad_count > 0 then
    raise exception 'trace_txn status violation rows=%', bad_count;
  end if;
end $$;

select 'OK: constraints' as verify;
