#!/usr/bin/env bash
set -euo pipefail

DB_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
SCRIPT_ROOT="$DB_ROOT/scripts"
SQL_ROOT="$DB_ROOT/sql"
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
  jq -r '.dbSchemaVersion' "$MANIFEST_PATH"
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

with_advisory_lock() {
  local lock_id="$1"
  local action="$2"

  local locked
  locked="$(psql_exec "select pg_try_advisory_lock(${lock_id})")"
  [[ "$locked" == "t" ]] || die "another deploy process is running (lock_id=${lock_id})"

  trap 'psql_exec "select pg_advisory_unlock('${lock_id}')" >/dev/null 2>&1 || true' EXIT
  "$action"
  psql_exec "select pg_advisory_unlock(${lock_id})" >/dev/null || true
  trap - EXIT
}
