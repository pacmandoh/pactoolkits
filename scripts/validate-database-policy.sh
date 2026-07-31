#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
# shellcheck source=manifest-v2.sh
source "$ROOT_DIR/scripts/manifest-v2.sh"

usage() {
  cat << 'USAGE'
Usage:
  validate-database-policy.sh [options]

Options:
  --manifest PATH          Candidate Manifest V2 file.
  --base-ref REF           Legacy Git baseline (default: origin/main).
  --base-manifest PATH     Optional extracted baseline manifest.
  -h, --help               Show this help.
USAGE
}

die() {
  printf 'ERROR: %s\n' "$*" >&2
  exit 1
}

MANIFEST="$ROOT_DIR/release-manifest.json"
BASE_REF="origin/main"
BASE_MANIFEST=""

while [[ $# -gt 0 ]]; do
  case "$1" in
    --manifest)
      MANIFEST="$2"
      shift 2
      ;;
    --base-ref)
      BASE_REF="$2"
      shift 2
      ;;
    --base-manifest)
      BASE_MANIFEST="$2"
      shift 2
      ;;
    -h | --help)
      usage
      exit 0
      ;;
    *) die "unknown argument: $1" ;;
  esac
done

[[ -f "$MANIFEST" ]] || die "manifest not found: $MANIFEST"
validate_manifest_v2 "$MANIFEST"

temp_base_manifest=""
cleanup() {
  [[ -z "$temp_base_manifest" ]] || rm -f "$temp_base_manifest"
}
trap cleanup EXIT

if [[ -z "$BASE_MANIFEST" ]]; then
  git rev-parse --verify "$BASE_REF^{commit}" > /dev/null 2>&1 \
    || die "base ref not found: $BASE_REF"
  temp_base_manifest="$(mktemp)"
  git show "$BASE_REF:release-manifest.json" > "$temp_base_manifest" 2> /dev/null \
    || die "release-manifest.json not found at base ref $BASE_REF"
  BASE_MANIFEST="$temp_base_manifest"
fi

[[ -f "$BASE_MANIFEST" ]] || die "base manifest not found: $BASE_MANIFEST"
base_db_version="$(manifest_database_postgres_version "$BASE_MANIFEST")"
is_stable_semver "$base_db_version" \
  || die "legacy baseline release-manifest.json must expose a database schema version"

candidate_db_version="$(manifest_database_postgres_version "$MANIFEST")"

LEGACY_MIGRATION_DIR="pactoolkits-db/sql/migrations"
CURRENT_MIGRATION_DIR="database/postgres/migrations"

migration_dir_at_ref() {
  local ref="$1"
  if git ls-tree -r --name-only "$ref" -- "$CURRENT_MIGRATION_DIR" 2> /dev/null | grep -q .; then
    printf '%s' "$CURRENT_MIGRATION_DIR"
    return 0
  fi
  if git ls-tree -r --name-only "$ref" -- "$LEGACY_MIGRATION_DIR" 2> /dev/null | grep -q .; then
    printf '%s' "$LEGACY_MIGRATION_DIR"
    return 0
  fi
  printf ''
}

migration_basenames_at_ref() {
  local ref="$1"
  local dir="$2"
  [[ -n "$dir" ]] || return 0
  git ls-tree -r --name-only "$ref" -- "$dir" 2> /dev/null \
    | awk -F/ '{print $NF}' \
    | sort -u
}

migration_content_sha256() {
  local ref="$1"
  local dir="$2"
  local basename="$3"
  git show "${ref}:${dir}/${basename}" 2> /dev/null | shasum -a 256 | awk '{print $1}'
}

collect_migration_changes() {
  local base_ref="$1"
  local base_dir head_dir base_names head_names name

  base_dir="$(migration_dir_at_ref "$base_ref")"
  head_dir="$(migration_dir_at_ref "HEAD")"
  [[ -n "$base_dir" || -n "$head_dir" ]] || return 0

  base_names="$(migration_basenames_at_ref "$base_ref" "$base_dir")"
  head_names="$(migration_basenames_at_ref "HEAD" "$head_dir")"

  while IFS= read -r name; do
    [[ -n "$name" ]] || continue
    if ! grep -Fxq "$name" <<< "$head_names"; then
      printf 'D\t%s\n' "$name"
    fi
  done <<< "$base_names"

  while IFS= read -r name; do
    [[ -n "$name" ]] || continue
    if ! grep -Fxq "$name" <<< "$base_names"; then
      printf 'A\t%s\n' "$name"
      continue
    fi
    base_hash="$(migration_content_sha256 "$base_ref" "$base_dir" "$name")"
    head_hash="$(migration_content_sha256 "HEAD" "$head_dir" "$name")"
    [[ "$base_hash" == "$head_hash" ]] || printf 'M\t%s\n' "$name"
  done <<< "$head_names"
}

migration_diff=""
if git rev-parse --verify "$BASE_REF^{commit}" > /dev/null 2>&1; then
  migration_diff="$(collect_migration_changes "$BASE_REF")"
fi

modified_applied="$(
  printf '%s\n' "$migration_diff" \
    | awk '$1 ~ /^(M|D|R|C)/ { print }'
)"
[[ -z "$modified_applied" ]] \
  || die "existing SQL migration files are immutable relative to $BASE_REF: $(printf '%s' "$modified_applied" | tr '\n' ';')"

changed_migrations="$(
  printf '%s\n' "$migration_diff" \
    | awk 'NF > 0 { print }'
)"

printf 'databaseVersion=%s\n' "$candidate_db_version"
printf 'legacyBaselineDatabaseVersion=%s\n' "$base_db_version"
printf 'migrationChanges=%s\n' "$([[ -n "$changed_migrations" ]] && printf true || printf false)"
