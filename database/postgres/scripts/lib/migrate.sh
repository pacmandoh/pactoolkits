#!/usr/bin/env bash
set -euo pipefail

# shellcheck disable=SC1091
source "$(dirname "${BASH_SOURCE[0]}")/common.sh"

MIGRATIONS_DIR="$DB_ROOT/migrations"
BOOTSTRAP_SQL="$DB_ROOT/bootstrap/000_init_meta.sql"

migration_files() {
  find "$MIGRATIONS_DIR" -maxdepth 1 -type f -name 'V*__*.sql' \
    | while IFS= read -r file; do
        local version major minor patch
        version="$(migration_version_from_file "$file" | tr '.' '_')"
        IFS='_' read -r major minor patch <<< "$version"
        printf '%010d.%010d.%010d\t%s\n' "$major" "$minor" "$patch" "$file"
      done \
    | sort -k1,1 \
    | cut -f2-
}

migration_version_from_file() {
  local file="$1"
  local base
  base="$(basename "$file")"
  echo "$base" | sed -E 's/^V([0-9_]+)__.*$/\1/' | tr '_' '.'
}

migration_name_from_file() {
  local file="$1"
  local base
  base="$(basename "$file")"
  echo "$base" | sed -E 's/^V[0-9_]+__(.*)\.sql$/\1/'
}

ensure_meta_tables() {
  psql_file "$BOOTSTRAP_SQL" >/dev/null
}

is_migration_applied() {
  local version="$1"
  local exists
  exists="$(psql_exec "select exists(select 1 from schema_migrations where version='${version}' and success=true)")"
  [[ "$exists" == "t" ]]
}

record_migration() {
  local version="$1"
  local name="$2"
  local checksum="$3"
  local note="$4"

  psql_exec "
insert into schema_migrations(version, name, checksum, success, note)
values ('${version}', '${name}', '${checksum}', true, '${note}')
on conflict (version) do update
  set name = excluded.name,
      checksum = excluded.checksum,
      success = excluded.success,
      installed_at = clock_timestamp(),
      note = excluded.note;
" >/dev/null
}

set_schema_version() {
  local version="$1"
  local note="$2"

  psql_exec "
insert into schema_version(singleton, schema_version, applied_at, note)
values (true, '${version}', clock_timestamp(), '${note}')
on conflict (singleton) do update
  set schema_version = excluded.schema_version,
      applied_at = excluded.applied_at,
      note = excluded.note;
" >/dev/null
}

apply_one_migration() {
  local file="$1"
  local version name checksum

  version="$(migration_version_from_file "$file")"
  name="$(migration_name_from_file "$file")"
  checksum="$(sha256_file "$file")"

  if is_migration_applied "$version"; then
    local current_checksum
    current_checksum="$(psql_exec "select checksum from schema_migrations where version='${version}'")"
    if [[ "$current_checksum" != "$checksum" ]]; then
      die "migration checksum changed after applied: version=${version}, file=$(basename "$file")"
    fi
    log "skip applied migration: V${version} (${name})"
    return 0
  fi

  log "apply migration: V${version} (${name})"
  renew_deploy_lock
  psql_file "$file"

  record_migration "$version" "$name" "$checksum" "applied by database/postgres/scripts/deploy.sh"
  set_schema_version "$version" "migration ${name}"

  log "done migration: V${version}"
}

plan_migrations() {
  local file version name
  while IFS= read -r file; do
    version="$(migration_version_from_file "$file")"
    name="$(migration_name_from_file "$file")"
    if is_migration_applied "$version"; then
      echo "APPLIED  V${version}  ${name}"
    else
      echo "PENDING  V${version}  ${name}"
    fi
  done < <(migration_files)
}

upgrade_migrations() {
  local file
  ensure_meta_tables
  while IFS= read -r file; do
    apply_one_migration "$file"
  done < <(migration_files)
}

current_schema_version() {
  ensure_meta_tables
  psql_exec "select schema_version from schema_version where singleton=true" | tr -d '\r'
}
