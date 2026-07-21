#!/usr/bin/env bash
set -euo pipefail

DB_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
SCRIPT_ROOT="$DB_ROOT/scripts"
MANIFEST_PATH="$DB_ROOT/../../release-manifest.json"

log() { printf '[db] %s\n' "$*"; }
warn() { printf '[db][warn] %s\n' "$*" >&2; }
die() { printf '[db][error] %s\n' "$*" >&2; exit 1; }

require_cmd() {
  command -v "$1" >/dev/null 2>&1 || die "command not found: $1"
}

sha256_file() {
  local file="$1"
  if command -v sha256sum >/dev/null 2>&1; then
    sha256sum "$file" | awk '{print $1}'
  elif command -v shasum >/dev/null 2>&1; then
    shasum -a 256 "$file" | awk '{print $1}'
  else
    die "need sha256sum or shasum"
  fi
}

read_manifest_db_version() {
  [[ -f "$MANIFEST_PATH" ]] || die "manifest not found: $MANIFEST_PATH"
  local version
  version="$(jq -r '.components.database.postgres.version // empty' "$MANIFEST_PATH")"
  [[ -n "$version" && "$version" != "null" ]] || die "manifest database.postgres.version is empty: $MANIFEST_PATH"
  printf '%s' "$version"
}

load_db_env_from_json() {
  local cfg="$1"
  [[ -f "$cfg" ]] || die "config not found: $cfg"

  export PGHOST="$(jq -r '.PGHOST // "127.0.0.1"' "$cfg")"
  export PGPORT="$(jq -r '.PGPORT // 5432' "$cfg")"
  export PGDATABASE="$(jq -r '.PGDATABASE // "codepool_dev"' "$cfg")"
  export PGUSER="$(jq -r '.PGUSER // "postgres"' "$cfg")"
  export PGPASSWORD="$(jq -r '.PGPASSWORD // empty' "$cfg")"
}

psql_exec() {
  local sql="$1"
  psql -v ON_ERROR_STOP=1 -X -q -t -A -c "$sql"
}

psql_file() {
  local file="$1"
  psql -v ON_ERROR_STOP=1 -X -f "$file"
}

sql_literal() {
  printf '%s' "$1" | sed "s/'/''/g"
}

DEPLOY_LOCK_OWNER=""

ensure_deploy_lock_table() {
  psql_exec "
create table if not exists schema_deploy_lock (
  singleton        boolean primary key default true check (singleton),
  lock_owner       text not null,
  lock_acquired_at timestamptz not null default clock_timestamp(),
  lock_expires_at  timestamptz not null
);
" >/dev/null
}

renew_deploy_lock() {
  [[ -n "$DEPLOY_LOCK_OWNER" ]] || return 0

  local owner
  owner="$(sql_literal "$DEPLOY_LOCK_OWNER")"
  psql_exec "
update schema_deploy_lock
set lock_expires_at = clock_timestamp() + interval '60 minutes'
where singleton = true and lock_owner = '${owner}';
" >/dev/null
}

release_deploy_lock() {
  [[ -n "$DEPLOY_LOCK_OWNER" ]] || return 0

  local owner
  owner="$(sql_literal "$DEPLOY_LOCK_OWNER")"
  psql_exec "
delete from schema_deploy_lock
where singleton = true and lock_owner = '${owner}';
" >/dev/null 2>&1 || true
  DEPLOY_LOCK_OWNER=""
}

with_advisory_lock() {
  local lock_id="$1"
  local action="$2"
  local owner acquired

  ensure_deploy_lock_table
  owner="${USER:-unknown}@$(hostname):$$:${lock_id}:$(date +%s)"
  owner="$(sql_literal "$owner")"
  acquired="$(psql_exec "
with got as (
  insert into schema_deploy_lock(singleton, lock_owner, lock_acquired_at, lock_expires_at)
  values (true, '${owner}', clock_timestamp(), clock_timestamp() + interval '60 minutes')
  on conflict (singleton) do update
    set lock_owner = excluded.lock_owner,
        lock_acquired_at = excluded.lock_acquired_at,
        lock_expires_at = excluded.lock_expires_at
  where schema_deploy_lock.lock_expires_at < clock_timestamp()
  returning lock_owner
)
select lock_owner from got;
")"
  [[ -n "$acquired" ]] || die "another deploy process is running (lock_id=${lock_id})"

  DEPLOY_LOCK_OWNER="$acquired"
  trap release_deploy_lock EXIT
  "$action"
  release_deploy_lock
  trap - EXIT
}
