#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
ENV_FILE="${PG_ITEST_ENV:-/tmp/pactoolkits-itest.env}"

die() { printf '[pg-itest][error] %s\n' "$*" >&2; exit 1; }
log() { printf '[pg-itest] %s\n' "$*"; }
pass() { printf '[pg-itest][pass] %s\n' "$*"; }

is_managed_beta_db() {
  [[ "$1" =~ ^pactoolkits_beta_itest_[0-9]+_[0-9]+$ ]]
}

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

cleanup_beta_db() {
  if [[ -z "${CREATED_BETA_DB:-}" ]]; then
    return
  fi
  if ! is_managed_beta_db "$CREATED_BETA_DB"; then
    log "skip cleanup for unexpected database name: $CREATED_BETA_DB"
    return
  fi
  if command -v dropdb >/dev/null 2>&1; then
    dropdb --if-exists --maintenance-db="${ADMIN_DB:-postgres}" "$CREATED_BETA_DB" >/dev/null 2>&1 || true
    log "cleaned up integration database $CREATED_BETA_DB"
  fi
}

load_env_file "$ENV_FILE"

export PGHOST="${PGHOST:-127.0.0.1}"
export PGPORT="${PGPORT:-5432}"
export PGUSER="${PGUSER:-postgres}"
export PGDATABASE="${PGDATABASE:-codepool_dev}"

require_cmd() { command -v "$1" >/dev/null 2>&1 || die "command not found: $1"; }
require_cmd psql
require_cmd createdb
require_cmd dropdb

BETA_VERSION="0.18.0-beta.1"
ITEST_RUN_ID="${PG_ITEST_RUN_ID:-${RANDOM}_$$}"
BETA_DB="pactoolkits_beta_itest_${ITEST_RUN_ID}"
BETA_DB="${BETA_DB//[^a-zA-Z0-9_]/_}"
is_managed_beta_db "$BETA_DB" || die "generated beta database name is invalid: $BETA_DB"

MARKER_SQL="$ROOT_DIR/scripts/create-beta-database.sql"
ADMIN_DB="postgres"
CREATED_BETA_DB=""
trap cleanup_beta_db EXIT

log "target database: ${PGUSER}@${PGHOST}:${PGPORT}/${PGDATABASE}"
log "integration beta database: $BETA_DB"

psql -v ON_ERROR_STOP=1 -X -d "$PGDATABASE" -c "select 1" >/dev/null
pass "connection ok"

schema_version="$(psql -v ON_ERROR_STOP=1 -X -q -t -A -d "$PGDATABASE" \
  -c "select schema_version from public.schema_version where singleton = true")"
[[ -n "$schema_version" ]] || die "schema_version missing"
pass "schema_version=$schema_version"

env_table="$(psql -v ON_ERROR_STOP=1 -X -q -t -A -d "$PGDATABASE" \
  -c "select to_regclass('public.app_environment_settings')")"
[[ "$env_table" == "app_environment_settings" ]] || die "app_environment_settings table missing"
pass "app_environment_settings table exists"

env_rows="$(psql -v ON_ERROR_STOP=1 -X -q -t -A -d "$PGDATABASE" \
  -c "select count(*) from public.app_environment_settings")"
log "production env rows=$env_rows (empty => runtime defaults production/false)"

trace_count="$(psql -v ON_ERROR_STOP=1 -X -q -t -A -d "$PGDATABASE" \
  -c "select count(*) from public.trace_pool")"
[[ "$trace_count" =~ ^[0-9]+$ ]] || die "trace_pool query failed"
pass "business read ok: trace_pool rows=$trace_count"

before_txn="$(psql -v ON_ERROR_STOP=1 -X -q -t -A -d "$PGDATABASE" -c "select count(*) from public.trace_txn")"
psql -v ON_ERROR_STOP=1 -X -d "$PGDATABASE" -c "select drug_id, trace_code from public.trace_pool limit 5;" >/dev/null
after_txn="$(psql -v ON_ERROR_STOP=1 -X -q -t -A -d "$PGDATABASE" -c "select count(*) from public.trace_txn")"
[[ "$before_txn" == "$after_txn" ]] || die "unexpected mutation after read-only query"
pass "read-only query left trace_txn unchanged ($before_txn)"

exists="$(psql -v ON_ERROR_STOP=1 -X -q -t -A -d "$ADMIN_DB" \
  -c "select exists(select 1 from pg_database where datname = '$BETA_DB')")"
[[ "$exists" == "f" ]] || die "integration database already exists: $BETA_DB"

log "creating isolated beta database from template $PGDATABASE"
if ! createdb --maintenance-db="$ADMIN_DB" --template="$PGDATABASE" "$BETA_DB" 2>/tmp/pactoolkits-itest-createdb.err; then
  die "template clone failed: $(tr '\n' ' ' </tmp/pactoolkits-itest-createdb.err). Disconnect other sessions from $PGDATABASE and retry, or provide a backup restore path."
fi
CREATED_BETA_DB="$BETA_DB"

psql -v ON_ERROR_STOP=1 -X --dbname="$BETA_DB" \
  -v "beta_version=$BETA_VERSION" \
  -v "database_source=production-clone" \
  -f "$MARKER_SQL" >/dev/null

beta_env="$(psql -v ON_ERROR_STOP=1 -X -q -t -A -d "$BETA_DB" \
  -c "select setting_key || '=' ||
        case jsonb_typeof(setting_value)
          when 'string' then setting_value #>> '{}'
          else setting_value::text
        end
      from public.app_environment_settings
      where environment = 'isolated'
      order by setting_key")"
echo "$beta_env" | grep -Fq 'Database.Environment=isolated' || die "beta env marker missing"
echo "$beta_env" | grep -Fq 'Database.Source=production-clone' || die "beta source marker missing"
echo "$beta_env" | grep -Fq "Database.BetaVersion=$BETA_VERSION" || die "beta version marker missing"
pass "beta clone markers written"

beta_schema="$(psql -v ON_ERROR_STOP=1 -X -q -t -A -d "$BETA_DB" \
  -c "select schema_version from public.schema_version where singleton = true")"
[[ "$beta_schema" == "$schema_version" ]] || die "beta clone schema_version mismatch"
beta_trace="$(psql -v ON_ERROR_STOP=1 -X -q -t -A -d "$BETA_DB" -c "select count(*) from public.trace_pool")"
[[ "$beta_trace" == "$trace_count" ]] || die "beta clone data mismatch on trace_pool"
pass "beta clone copied schema/data (trace_pool=$beta_trace)"

export PG_ITEST=1
export PG_ITEST_BETA_DATABASE="$BETA_DB"

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

cleanup_beta_db
CREATED_BETA_DB=""
trap - EXIT
pass "beta integration database cleaned up"

log "all PostgreSQL integration checks passed"
