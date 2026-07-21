#!/usr/bin/env bash
set -euo pipefail

# shellcheck disable=SC1091
source "$(dirname "${BASH_SOURCE[0]}")/common.sh"

VERIFY_DIR="$DB_ROOT/verify"

run_verify_suite() {
  local expected="$1"

  [[ -n "$expected" ]] || die "expected schema version is empty"

  psql_file "$VERIFY_DIR/01_structure.sql"
  psql_file "$VERIFY_DIR/02_constraints.sql"
  psql -v ON_ERROR_STOP=1 -X -v expected_schema_version="$expected" -f "$VERIFY_DIR/03_schema_version.sql"
  psql_file "$VERIFY_DIR/04_environment_settings.sql"

  log "verify passed (expected schema_version=${expected})"
}
