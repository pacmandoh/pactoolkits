#!/usr/bin/env bash
set -euo pipefail

# shellcheck disable=SC1091
source "$(dirname "${BASH_SOURCE[0]}")/common.sh"

MIGRATIONS_DIR="$DB_ROOT/migrations"
BOOTSTRAP_SQL="$DB_ROOT/bootstrap/000_init_meta.sql"

# version<TAB>checksum<TAB>name 行（仅 success=true）；避免 bash 4 关联数组（macOS /bin/bash 3.2）
APPLIED_MAP=""

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

# 当前 deploy 记账用短名（无 V…__ / .sql）；旧工具可能写成完整文件名
migration_name_from_file() {
  local file="$1"
  local base
  base="$(basename "$file")"
  echo "$base" | sed -E 's/^V[0-9_]+__(.*)\.sql$/\1/'
}

ensure_meta_tables() {
  psql_file "$BOOTSTRAP_SQL" >/dev/null
}

# 一次查出全部已应用记录，避免每个文件往返 psql
load_applied_checksums() {
  APPLIED_MAP="$(psql_exec "select version || chr(9) || checksum || chr(9) || coalesce(name, '') from schema_migrations where success = true order by version" | tr -d '\r')"
}

is_migration_applied() {
  local version="$1"
  printf '%s\n' "$APPLIED_MAP" | awk -F'\t' -v v="$version" '$1 == v { found=1; exit } END { exit !found }'
}

applied_checksum() {
  local version="$1"
  printf '%s\n' "$APPLIED_MAP" | awk -F'\t' -v v="$version" '$1 == v { print $2; exit }'
}

applied_name() {
  local version="$1"
  printf '%s\n' "$APPLIED_MAP" | awk -F'\t' -v v="$version" '$1 == v { print $3; exit }'
}

applied_map_put() {
  local version="$1"
  local checksum="$2"
  local name="$3"
  APPLIED_MAP="$(printf '%s\n' "$APPLIED_MAP" | awk -F'\t' -v v="$version" '$1 != v')"
  APPLIED_MAP="$(printf '%s\n%s\t%s\t%s\n' "$APPLIED_MAP" "$version" "$checksum" "$name" | sed '/^$/d')"
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

  applied_map_put "$version" "$checksum" "$name"
}

# 将 stdin 内容做 SHA-256（用于 CRLF 对照）
sha256_stdin() {
  if command -v sha256sum >/dev/null 2>&1; then
    sha256sum | awk '{print $1}'
  elif command -v shasum >/dev/null 2>&1; then
    shasum -a 256 | awk '{print $1}'
  else
    die "need sha256sum or shasum"
  fi
}

# 校正已应用 migration 的 checksum / 短 name，并追加 note（不重跑 SQL）
realign_migration_checksums() {
  local file version name checksum current_checksum current_name crlf_checksum
  local note_parts note_text note_lit old_checksum_lit part
  local checksum_changed name_changed reason updated matched skipped
  updated=0
  matched=0
  skipped=0

  ensure_meta_tables
  load_applied_checksums

  while IFS= read -r file; do
    version="$(migration_version_from_file "$file")"
    name="$(migration_name_from_file "$file")"

    if ! is_migration_applied "$version"; then
      skipped=$((skipped + 1))
      continue
    fi

    checksum="$(sha256_file "$file")"
    current_checksum="$(applied_checksum "$version")"
    current_name="$(applied_name "$version")"

    checksum_changed=0
    name_changed=0
    note_parts=()

    if [[ "$current_checksum" != "$checksum" ]]; then
      checksum_changed=1
      crlf_checksum="$(perl -pe 's/\n/\r\n/' < "$file" | sha256_stdin)"
      if [[ "$current_checksum" == "$crlf_checksum" ]]; then
        reason="eol CRLF->LF"
      else
        warn "V${version}: stored checksum matches neither LF nor CRLF working-tree transform; still realigning to current file"
        reason="working-tree realign (not simple CRLF)"
      fi
      note_parts+=("checksum realigned (${reason}); was ${current_checksum}")
    fi

    if [[ "$current_name" != "$name" ]]; then
      name_changed=1
      note_parts+=("name realigned; was ${current_name}")
    fi

    if [[ "$checksum_changed" -eq 0 && "$name_changed" -eq 0 ]]; then
      matched=$((matched + 1))
      continue
    fi

    note_text=""
    for part in "${note_parts[@]}"; do
      if [[ -z "$note_text" ]]; then
        note_text="$part"
      else
        note_text="${note_text} | ${part}"
      fi
    done
    note_lit="$(sql_literal "$note_text")"
    old_checksum_lit="$(sql_literal "$current_checksum")"
    renew_deploy_lock

    # checksum / name 任一漂移都 UPDATE，并始终追加 note
    psql_exec "
update schema_migrations
set checksum = '$(sql_literal "$checksum")',
    name = '$(sql_literal "$name")',
    note = case
      when note is null or btrim(note) = '' then '${note_lit}'
      else note || ' | ${note_lit}'
    end
where version = '$(sql_literal "$version")'
  and success = true
  and checksum = '${old_checksum_lit}';
" >/dev/null

    applied_map_put "$version" "$checksum" "$name"
    log "realigned: V${version} (${name}) [${note_text}]"
    updated=$((updated + 1))
  done < <(migration_files)

  log "realign-checksums done: updated=${updated} matched=${matched} pending_skipped=${skipped}"
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

# 已应用：只校验 checksum（内存）；未应用：执行并记账
apply_one_migration() {
  local file="$1"
  local version name checksum current_checksum

  version="$(migration_version_from_file "$file")"
  name="$(migration_name_from_file "$file")"
  checksum="$(sha256_file "$file")"

  if is_migration_applied "$version"; then
    current_checksum="$(applied_checksum "$version")"
    if [[ "$current_checksum" != "$checksum" ]]; then
      die "migration checksum changed after applied: version=${version}, file=$(basename "$file") (run: ./scripts/deploy.sh realign-checksums)"
    fi
    return 1
  fi

  log "apply migration: V${version} (${name})"
  renew_deploy_lock
  psql_file "$file"

  record_migration "$version" "$name" "$checksum" "applied by database/postgres/scripts/deploy.sh"
  set_schema_version "$version" "migration ${name}"

  log "done migration: V${version}"
  return 0
}

plan_migrations() {
  local file version name applied=0 pending=0
  load_applied_checksums

  while IFS= read -r file; do
    version="$(migration_version_from_file "$file")"
    name="$(migration_name_from_file "$file")"
    if is_migration_applied "$version"; then
      echo "APPLIED  V${version}  ${name}"
      applied=$((applied + 1))
    else
      echo "PENDING  V${version}  ${name}"
      pending=$((pending + 1))
    fi
  done < <(migration_files)

  echo "summary: applied=${applied} pending=${pending}"
}

upgrade_migrations() {
  local file skipped=0 applied_now=0
  ensure_meta_tables
  load_applied_checksums

  while IFS= read -r file; do
    if apply_one_migration "$file"; then
      applied_now=$((applied_now + 1))
    else
      skipped=$((skipped + 1))
    fi
  done < <(migration_files)

  if [[ "$applied_now" -eq 0 ]]; then
    log "upgrade: no pending migrations (checked ${skipped} applied)"
  else
    log "upgrade: applied ${applied_now} pending; skipped ${skipped} already applied"
  fi
}

current_schema_version() {
  ensure_meta_tables
  psql_exec "select schema_version from schema_version where singleton=true" | tr -d '\r'
}
