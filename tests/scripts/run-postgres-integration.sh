#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
ENV_FILE="${PG_ITEST_ENV:-$ROOT_DIR/.tmp/pactoolkits-itest.env}"

die() { printf '[pg-itest][error] %s\n' "$*" >&2; exit 1; }
log() { printf '[pg-itest] %s\n' "$*"; }
pass() { printf '[pg-itest][pass] %s\n' "$*"; }

load_env_file() {
  local file="$1"
  local mode

  [[ -f "$file" ]] || die "env file not found: $file"
  if stat -f '%Lp' "$file" >/dev/null 2>&1; then
    mode="$(stat -f '%Lp' "$file")"
  else
    mode="$(stat -c '%a' "$file")"
  fi
  [[ "$mode" == "600" ]] || die "env file must be mode 0600: $file (found $mode)"

  while IFS= read -r line || [[ -n "$line" ]]; do
    line="${line#"${line%%[![:space:]]*}"}"
    line="${line%"${line##*[![:space:]]}"}"
    [[ -z "$line" || "$line" == \#* ]] && continue
    [[ "$line" =~ ^(export[[:space:]]+)?[A-Za-z_][A-Za-z0-9_]*= ]] || die "invalid env line (only KEY=value allowed): $line"
    if [[ "$line" == export\ * ]]; then
      line="${line#export }"
    fi
    local key="${line%%=*}"
    local value="${line#*=}"
    if [[ ${#value} -ge 2 ]]; then
      if [[ "$value" == \"*\" && "$value" == *\" ]]; then
        value="${value:1:${#value}-2}"
      elif [[ "$value" == \'*\' && "$value" == *\' ]]; then
        value="${value:1:${#value}-2}"
      fi
    fi
    export "$key=$value"
  done < "$file"
}

load_env_file "$ENV_FILE"

export PGHOST="${PGHOST:-127.0.0.1}"
export PGPORT="${PGPORT:-5432}"
export PGUSER="${PGUSER:-postgres}"
export PGDATABASE="${PGDATABASE:-postgres}"

require_cmd() { command -v "$1" >/dev/null 2>&1 || die "command not found: $1"; }
require_cmd psql

log "target database: ${PGUSER}@${PGHOST}:${PGPORT}/${PGDATABASE}"

psql -v ON_ERROR_STOP=1 -X -d "$PGDATABASE" -c "select 1" >/dev/null
pass "connection ok"

schema_version="$(psql -v ON_ERROR_STOP=1 -X -q -t -A -d "$PGDATABASE" \
  -c "select schema_version from public.schema_version where singleton = true")"
[[ -n "$schema_version" ]] || die "schema_version missing"
pass "schema_version=$schema_version"

obsolete_env_table="$(psql -v ON_ERROR_STOP=1 -X -q -t -A -d "$PGDATABASE" \
  -c "select to_regclass('public.app_environment_settings')")"
[[ -z "$obsolete_env_table" ]] || die "app_environment_settings should be dropped"
pass "app_environment_settings dropped"

drug_source_default="$(psql -v ON_ERROR_STOP=1 -X -q -t -A -d "$PGDATABASE" \
  -c "select column_default from information_schema.columns where table_schema='public' and table_name='drug_key_fix_audit' and column_name='source'")"
[[ "$drug_source_default" == *"drug_index_desktop"* ]] || die "drug_key_fix_audit.source default expected drug_index_desktop, got: $drug_source_default"
pass "drug_key_fix_audit.source default=drug_index_desktop"

inv_source_default="$(psql -v ON_ERROR_STOP=1 -X -q -t -A -d "$PGDATABASE" \
  -c "select column_default from information_schema.columns where table_schema='public' and table_name='inventory_reassign_audit' and column_name='source'")"
[[ "$inv_source_default" == *"inventory_desktop"* ]] || die "inventory_reassign_audit.source default expected inventory_desktop, got: $inv_source_default"
pass "inventory_reassign_audit.source default=inventory_desktop"

trace_count="$(psql -v ON_ERROR_STOP=1 -X -q -t -A -d "$PGDATABASE" \
  -c "select count(*) from public.trace_pool")"
[[ "$trace_count" =~ ^[0-9]+$ ]] || die "trace_pool query failed"
pass "business read ok: trace_pool rows=$trace_count"

before_txn="$(psql -v ON_ERROR_STOP=1 -X -q -t -A -d "$PGDATABASE" -c "select count(*) from public.trace_txn")"
psql -v ON_ERROR_STOP=1 -X -d "$PGDATABASE" -c "select drug_id, trace_code from public.trace_pool limit 5;" >/dev/null
after_txn="$(psql -v ON_ERROR_STOP=1 -X -q -t -A -d "$PGDATABASE" -c "select count(*) from public.trace_txn")"
[[ "$before_txn" == "$after_txn" ]] || die "unexpected mutation after read-only query"
pass "read-only query left trace_txn unchanged ($before_txn)"

export PG_ITEST=1

if command -v dotnet >/dev/null 2>&1 && [[ -f "$ROOT_DIR/tests/PostgresIntegrationHarness/PostgresIntegrationHarness.csproj" ]]; then
  log "running application-layer PostgreSQL integration harness"
  (
    cd "$ROOT_DIR"
    if ! dotnet build tests/PostgresIntegrationHarness/PostgresIntegrationHarness.csproj -c Release --no-restore -v minimal >/tmp/pactoolkits-itest-build.log 2>&1; then
      dotnet build tests/PostgresIntegrationHarness/PostgresIntegrationHarness.csproj -c Release -v minimal >/tmp/pactoolkits-itest-build.log 2>&1
    fi
    dotnet run --project tests/PostgresIntegrationHarness/PostgresIntegrationHarness.csproj -c Release --no-build
  )
  pass "application-layer PostgreSQL integration harness"
fi

if command -v dotnet >/dev/null 2>&1 && [[ -f "$ROOT_DIR/tests/PacToolkits.Desktop.Tests/PacToolkits.Desktop.Tests.csproj" ]]; then
  log "running xUnit PostgreSQL integration tests"
  (
    cd "$ROOT_DIR"
    if ! dotnet build tests/PacToolkits.Desktop.Tests/PacToolkits.Desktop.Tests.csproj -c Release -p:PostgresIntegration=true --no-restore -v minimal >/tmp/pactoolkits-itest-xunit-build.log 2>&1; then
      dotnet build tests/PacToolkits.Desktop.Tests/PacToolkits.Desktop.Tests.csproj -c Release -p:PostgresIntegration=true -v minimal >/tmp/pactoolkits-itest-xunit-build.log 2>&1
    fi
    dotnet test tests/PacToolkits.Desktop.Tests/PacToolkits.Desktop.Tests.csproj -c Release -p:PostgresIntegration=true --no-restore --no-build -v minimal \
      --filter "FullyQualifiedName~PostgresIntegrationTests"
  )
  pass "xUnit PostgreSQL integration tests"
fi

log "all PostgreSQL integration checks passed"
