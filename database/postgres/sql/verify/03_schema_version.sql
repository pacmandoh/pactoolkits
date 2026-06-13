\set ON_ERROR_STOP on
\pset pager off

-- Requires: -v expected_schema_version='x.y.z'
do $$
declare
  actual text;
  expected text := :'expected_schema_version';
begin
  if expected is null or btrim(expected) = '' then
    raise exception 'expected_schema_version is empty';
  end if;

  select schema_version into actual
  from schema_version
  where singleton = true;

  if actual is null then
    raise exception 'schema_version current row not found';
  end if;

  if actual <> expected then
    raise exception 'schema version mismatch: db=% expected=%', actual, expected;
  end if;
end $$;

select 'OK: schema version' as verify;
