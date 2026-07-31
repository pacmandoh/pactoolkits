#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck disable=SC1091
source "$SCRIPT_DIR/lib/common.sh"
# shellcheck disable=SC1091
source "$SCRIPT_DIR/lib/migrate.sh"
# shellcheck disable=SC1091
source "$SCRIPT_DIR/lib/verify.sh"

LOCK_ID=82423019
DB_CONFIG_JSON="$SCRIPT_DIR/config.json"

usage() {
  cat <<USAGE
Usage:
  deploy.sh <command> [--config path]

Commands:
  doctor             Check required tools and DB connectivity
  bootstrap          Initialize meta tables and apply all migrations
  upgrade            Apply pending migrations only
  plan               Show migration plan (applied/pending)
  status             Show DB schema version and expected manifest version
  verify             Run read-only verification suite
  full               upgrade + verify
  realign-checksums  Fix applied checksum/name to current *.sql; append note (no SQL rerun)
USAGE
}

parse_args() {
  if [[ $# -lt 1 ]]; then
    usage
    exit 1
  fi

  if [[ "$1" == "-h" || "$1" == "--help" ]]; then
    usage
    exit 0
  fi

  COMMAND="$1"
  shift

  while [[ $# -gt 0 ]]; do
    case "$1" in
      --config)
        DB_CONFIG_JSON="$2"
        shift 2
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
}

doctor() {
  require_cmd psql
  require_cmd jq

  load_db_env_from_json "$DB_CONFIG_JSON"

  log "tools OK: psql=$(psql --version | head -n1), jq=$(jq --version)"
  log "db target: ${PGUSER}@${PGHOST}:${PGPORT}/${PGDATABASE}"

  psql_exec "select 1" >/dev/null
  log "connectivity OK"
}

status() {
  load_db_env_from_json "$DB_CONFIG_JSON"
  ensure_meta_tables

  local expected current
  expected="$(read_manifest_db_version)"
  current="$(current_schema_version)"

  echo "manifest database.postgres.version: ${expected}"
  echo "db.schema_version:      ${current:-<empty>}"
}

plan() {
  load_db_env_from_json "$DB_CONFIG_JSON"
  ensure_meta_tables
  plan_migrations
}

bootstrap() {
  load_db_env_from_json "$DB_CONFIG_JSON"
  with_advisory_lock "$LOCK_ID" upgrade_migrations
  log "bootstrap complete"
}

upgrade() {
  load_db_env_from_json "$DB_CONFIG_JSON"
  with_advisory_lock "$LOCK_ID" upgrade_migrations
  log "upgrade complete"
}

verify() {
  load_db_env_from_json "$DB_CONFIG_JSON"
  local expected
  expected="$(read_manifest_db_version)"
  run_verify_suite "$expected"
}

full() {
  upgrade
  verify
}

realign_checksums() {
  require_cmd perl
  load_db_env_from_json "$DB_CONFIG_JSON"
  with_advisory_lock "$LOCK_ID" realign_migration_checksums
}

main() {
  parse_args "$@"

  case "$COMMAND" in
    doctor) doctor ;;
    bootstrap) bootstrap ;;
    upgrade) upgrade ;;
    plan) plan ;;
    status) status ;;
    verify) verify ;;
    full) full ;;
    realign-checksums) realign_checksums ;;
    *) usage; exit 1 ;;
  esac
}

main "$@"
