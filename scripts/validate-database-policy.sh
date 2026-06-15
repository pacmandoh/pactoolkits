#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
# shellcheck source=manifest-v2.sh
source "$ROOT_DIR/scripts/manifest-v2.sh"

usage() {
  cat <<'USAGE'
Usage:
  validate-database-policy.sh [options]

Options:
  --manifest PATH          Candidate Manifest V2 file.
  --base-ref REF           Stable/main Git baseline (default: origin/main).
  --base-manifest PATH     Optional extracted baseline manifest.
  --allow-beta-migration BOOL
                           Explicit CI authorization (default: false).
  -h, --help               Show this help.
USAGE
}

die() {
  printf 'ERROR: %s\n' "$*" >&2
  exit 1
}

normalize_bool() {
  case "${1:-}" in
    true|false) printf '%s' "$1" ;;
    *) die "expected boolean true/false, got: ${1:-<empty>}" ;;
  esac
}

MANIFEST="$ROOT_DIR/release-manifest.json"
BASE_REF="origin/main"
BASE_MANIFEST=""
ALLOW_BETA_MIGRATION="false"

while [[ $# -gt 0 ]]; do
  case "$1" in
    --manifest) MANIFEST="$2"; shift 2 ;;
    --base-ref) BASE_REF="$2"; shift 2 ;;
    --base-manifest) BASE_MANIFEST="$2"; shift 2 ;;
    --allow-beta-migration) ALLOW_BETA_MIGRATION="$2"; shift 2 ;;
    -h|--help) usage; exit 0 ;;
    *) die "unknown argument: $1" ;;
  esac
done

ALLOW_BETA_MIGRATION="$(normalize_bool "$ALLOW_BETA_MIGRATION")"
[[ -f "$MANIFEST" ]] || die "manifest not found: $MANIFEST"
validate_manifest_v2 "$MANIFEST"

temp_base_manifest=""
cleanup() {
  [[ -z "$temp_base_manifest" ]] || rm -f "$temp_base_manifest"
}
trap cleanup EXIT

if [[ -z "$BASE_MANIFEST" ]]; then
  git rev-parse --verify "$BASE_REF^{commit}" >/dev/null 2>&1 ||
    die "base ref not found: $BASE_REF"
  temp_base_manifest="$(mktemp)"
  git show "$BASE_REF:release-manifest.json" > "$temp_base_manifest" 2>/dev/null ||
    die "release-manifest.json not found at base ref $BASE_REF"
  BASE_MANIFEST="$temp_base_manifest"
fi

[[ -f "$BASE_MANIFEST" ]] || die "base manifest not found: $BASE_MANIFEST"
base_schema_version="$(manifest_schema_version "$BASE_MANIFEST")"
base_db_version="$(manifest_database_postgres_version "$BASE_MANIFEST")"
is_stable_semver "$base_db_version" ||
  die "invalid stable/main baseline database version: $base_db_version"

channel="$(manifest_release_channel "$MANIFEST")"
policy="$(manifest_database_migration_policy "$MANIFEST")"
candidate_db_version="$(manifest_database_postgres_version "$MANIFEST")"

migration_diff=""
if git rev-parse --verify "$BASE_REF^{commit}" >/dev/null 2>&1; then
  migration_diff="$(git diff --name-status "$BASE_REF"...HEAD -- database/postgres/sql/migrations || true)"
fi

modified_applied="$(
  printf '%s\n' "$migration_diff" |
    awk '$1 ~ /^(M|D|R|C)/ { print }'
)"
[[ -z "$modified_applied" ]] ||
  die "existing SQL migration files are immutable relative to $BASE_REF: $(printf '%s' "$modified_applied" | tr '\n' ';')"

changed_migrations="$(
  printf '%s\n' "$migration_diff" |
    awk 'NF > 0 { print }'
)"

if [[ "$channel" == "beta" ]]; then
  if semver_gte_stable "$base_db_version" "$candidate_db_version"; then
    :
  elif [[ "$base_schema_version" -lt 2 ]]; then
    :
  elif [[ "$policy" == "isolated-beta" && "$ALLOW_BETA_MIGRATION" == "true" ]]; then
    :
  else
    die "ordinary Beta database-postgres.version ($candidate_db_version) cannot exceed stable/main baseline ($base_db_version)"
  fi

  if [[ -n "$changed_migrations" ]]; then
    [[ "$policy" == "isolated-beta" ]] ||
      die "Beta SQL migration changes require migrationPolicy=isolated-beta"
    [[ "$ALLOW_BETA_MIGRATION" == "true" ]] ||
      die "Beta SQL migration changes require explicit CI authorization"
  fi
fi

printf 'channel=%s\n' "$channel"
printf 'migrationPolicy=%s\n' "$policy"
printf 'databaseVersion=%s\n' "$candidate_db_version"
printf 'stableBaselineDatabaseVersion=%s\n' "$base_db_version"
printf 'betaMigrationAuthorized=%s\n' "$ALLOW_BETA_MIGRATION"
printf 'migrationChanges=%s\n' "$([[ -n "$changed_migrations" ]] && printf true || printf false)"
