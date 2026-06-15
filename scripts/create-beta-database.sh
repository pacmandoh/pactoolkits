#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
MARKER_SQL="$ROOT_DIR/scripts/create-beta-database.sql"

usage() {
  cat <<'USAGE'
Usage:
  create-beta-database.sh --version X.Y.Z-beta.N \
    (--backup FILE | --template DATABASE) [options]

Options:
  --version VERSION        Beta product version used in the database name.
  --backup FILE            Restore a pg_dump archive or plain .sql backup.
  --template DATABASE      Clone an existing PostgreSQL database.
  --config FILE            Read PGHOST/PGPORT/PGUSER/PGPASSWORD from JSON.
  --admin-database NAME    Maintenance database (default: postgres).
  --name-only              Print the generated database name and exit.
  --dry-run                Print the creation plan without changing PostgreSQL.
  -h, --help               Show this help.

Environment:
  PGHOST, PGPORT, PGUSER, PGPASSWORD and PGSSLMODE are honored.

The target name is pactoolkits_beta_<version>, with '.', '-' and '+' replaced
by '_'. Existing databases are never overwritten or deleted.
USAGE
}

die() {
  printf '[beta-db][error] %s\n' "$*" >&2
  exit 1
}

log() {
  printf '[beta-db] %s\n' "$*"
}

require_cmd() {
  command -v "$1" >/dev/null 2>&1 || die "command not found: $1"
}

require_value() {
  [[ $# -ge 2 && -n "${2:-}" ]] || die "$1 requires a value"
}

sql_literal() {
  printf '%s' "$1" | sed "s/'/''/g"
}

is_beta_semver() {
  [[ "$1" =~ ^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)-beta\.(0|[1-9][0-9]*)(\+[0-9A-Za-z-]+(\.[0-9A-Za-z-]+)*)?$ ]]
}

database_name_for_version() {
  local version="$1"
  printf 'pactoolkits_beta_%s' "${version//[.\-+]/_}"
}

is_plain_sql_backup() {
  case "${1##*.}" in
    [sS][qQ][lL]) return 0 ;;
    *) return 1 ;;
  esac
}

print_cmd() {
  printf '[dry-run] '
  printf '%q ' "$@"
  printf '\n'
}

VERSION=""
BACKUP_FILE=""
TEMPLATE_DATABASE=""
CONFIG_PATH=""
ADMIN_DATABASE="postgres"
NAME_ONLY="false"
DRY_RUN="false"

while [[ $# -gt 0 ]]; do
  case "$1" in
    --version)
      require_value "$@"
      VERSION="$2"
      shift 2
      ;;
    --backup)
      require_value "$@"
      BACKUP_FILE="$2"
      shift 2
      ;;
    --template)
      require_value "$@"
      TEMPLATE_DATABASE="$2"
      shift 2
      ;;
    --config)
      require_value "$@"
      CONFIG_PATH="$2"
      shift 2
      ;;
    --admin-database)
      require_value "$@"
      ADMIN_DATABASE="$2"
      shift 2
      ;;
    --name-only)
      NAME_ONLY="true"
      shift
      ;;
    --dry-run)
      DRY_RUN="true"
      shift
      ;;
    -h|--help)
      usage
      exit 0
      ;;
    *)
      die "unknown argument: $1"
      ;;
  esac
done

[[ -n "$VERSION" ]] || die "--version is required"
is_beta_semver "$VERSION" || die "--version must match strict Beta SemVer: X.Y.Z-beta.N"

TARGET_DATABASE="$(database_name_for_version "$VERSION")"
[[ ${#TARGET_DATABASE} -le 63 ]] ||
  die "generated database name exceeds PostgreSQL's 63-byte identifier limit: $TARGET_DATABASE"

if [[ "$NAME_ONLY" == "true" ]]; then
  printf '%s\n' "$TARGET_DATABASE"
  exit 0
fi

if [[ -n "$BACKUP_FILE" && -n "$TEMPLATE_DATABASE" ]]; then
  die "choose exactly one source: --backup or --template"
fi
if [[ -z "$BACKUP_FILE" && -z "$TEMPLATE_DATABASE" ]]; then
  die "choose exactly one source: --backup or --template"
fi

if [[ -n "$CONFIG_PATH" ]]; then
  require_cmd jq
  [[ -f "$CONFIG_PATH" ]] || die "config not found: $CONFIG_PATH"
  export PGHOST="$(jq -r '.PGHOST // "127.0.0.1"' "$CONFIG_PATH")"
  export PGPORT="$(jq -r '.PGPORT // 5432' "$CONFIG_PATH")"
  export PGUSER="$(jq -r '.PGUSER // "postgres"' "$CONFIG_PATH")"
  export PGPASSWORD="$(jq -r '.PGPASSWORD // empty' "$CONFIG_PATH")"
  config_sslmode="$(jq -r '.PGSSLMODE // empty' "$CONFIG_PATH")"
  [[ -z "$config_sslmode" ]] || export PGSSLMODE="$config_sslmode"
fi

export PGHOST="${PGHOST:-127.0.0.1}"
export PGPORT="${PGPORT:-5432}"
export PGUSER="${PGUSER:-postgres}"

[[ -f "$MARKER_SQL" ]] || die "marker SQL not found: $MARKER_SQL"
if [[ -n "$BACKUP_FILE" ]]; then
  [[ -f "$BACKUP_FILE" ]] || die "backup not found: $BACKUP_FILE"
fi

log "target: ${PGUSER}@${PGHOST}:${PGPORT}/${TARGET_DATABASE}"
if [[ -n "$TEMPLATE_DATABASE" ]]; then
  log "source: template database $TEMPLATE_DATABASE"
else
  log "source: backup $BACKUP_FILE"
fi

if [[ "$DRY_RUN" == "true" ]]; then
  if [[ -n "$TEMPLATE_DATABASE" ]]; then
    print_cmd createdb --maintenance-db="$ADMIN_DATABASE" \
      --template="$TEMPLATE_DATABASE" "$TARGET_DATABASE"
  else
    print_cmd createdb --maintenance-db="$ADMIN_DATABASE" "$TARGET_DATABASE"
    if is_plain_sql_backup "$BACKUP_FILE"; then
      print_cmd psql -v ON_ERROR_STOP=1 -X --dbname="$TARGET_DATABASE" -f "$BACKUP_FILE"
    else
      print_cmd pg_restore --exit-on-error --no-owner --no-privileges \
        --dbname="$TARGET_DATABASE" "$BACKUP_FILE"
    fi
  fi
  print_cmd psql -v ON_ERROR_STOP=1 -X --dbname="$TARGET_DATABASE" \
    -v "beta_version=$VERSION" -v "database_source=production-clone" -f "$MARKER_SQL"
  exit 0
fi

require_cmd psql
require_cmd createdb
if [[ -n "$BACKUP_FILE" ]] && ! is_plain_sql_backup "$BACKUP_FILE"; then
  require_cmd pg_restore
fi

database_exists="$(
  target_database_literal="$(sql_literal "$TARGET_DATABASE")"
  psql -v ON_ERROR_STOP=1 -X -q -t -A --dbname="$ADMIN_DATABASE" \
    -c "select exists(select 1 from pg_database where datname = '${target_database_literal}')"
)"
[[ "$database_exists" == "f" ]] ||
  die "database already exists; refusing to overwrite: $TARGET_DATABASE"

if [[ -n "$TEMPLATE_DATABASE" ]]; then
  template_database_literal="$(sql_literal "$TEMPLATE_DATABASE")"
  template_exists="$(
    psql -v ON_ERROR_STOP=1 -X -q -t -A --dbname="$ADMIN_DATABASE" \
      -c "select exists(select 1 from pg_database where datname = '${template_database_literal}')"
  )"
  [[ "$template_exists" == "t" ]] || die "template database not found: $TEMPLATE_DATABASE"

  active_connections="$(
    psql -v ON_ERROR_STOP=1 -X -q -t -A --dbname="$ADMIN_DATABASE" \
      -c "select count(*) from pg_stat_activity where datname = '${template_database_literal}'"
  )"
  [[ "$active_connections" == "0" ]] ||
    die "template database has active connections ($active_connections); disconnect them before cloning: $TEMPLATE_DATABASE"

  createdb --maintenance-db="$ADMIN_DATABASE" \
    --template="$TEMPLATE_DATABASE" "$TARGET_DATABASE"
else
  createdb --maintenance-db="$ADMIN_DATABASE" "$TARGET_DATABASE"
  if is_plain_sql_backup "$BACKUP_FILE"; then
    psql -v ON_ERROR_STOP=1 -X --dbname="$TARGET_DATABASE" -f "$BACKUP_FILE"
  else
    pg_restore --exit-on-error --no-owner --no-privileges \
      --dbname="$TARGET_DATABASE" "$BACKUP_FILE"
  fi
fi

psql -v ON_ERROR_STOP=1 -X --dbname="$TARGET_DATABASE" \
  -v "beta_version=$VERSION" \
  -v "database_source=production-clone" \
  -f "$MARKER_SQL"

log "created isolated Beta database: $TARGET_DATABASE"
log "connection string: Host=$PGHOST;Port=$PGPORT;Database=$TARGET_DATABASE;Username=$PGUSER"
if [[ -n "${PGPASSWORD:-}" ]]; then
  log "password omitted from output; reuse the configured PGPASSWORD securely"
fi
