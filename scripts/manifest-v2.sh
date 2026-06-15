#!/usr/bin/env bash
set -euo pipefail

# Shared jq helpers for release-manifest.json schema v2.

manifest_schema_version() {
  jq -r '.schemaVersion // 1' "$1"
}

manifest_product_version() {
  jq -r '.product.version // .suiteVersion // empty' "$1"
}

manifest_desktop_version() {
  jq -r '.components.desktop.version // .uiVersion // empty' "$1"
}

manifest_desktop_implementation() {
  jq -r '.components.desktop.implementation // "avalonia"' "$1"
}

manifest_desktop_min_db() {
  jq -r '.components.desktop.minDbSchema // .compat.uiMinDbSchema // empty' "$1"
}

manifest_desktop_max_db() {
  jq -r '.components.desktop.maxDbSchema // empty' "$1"
}

manifest_agent_injector_ahk_version() {
  jq -r '.components["agent-injector-ahk"].version // .agentVersion // empty' "$1"
}

manifest_agent_injector_ahk_min_db() {
  jq -r '.components["agent-injector-ahk"].minDbSchema // .compat.agentMinDbSchema // empty' "$1"
}

manifest_database_postgres_version() {
  jq -r '.components["database-postgres"].version // .dbSchemaVersion // empty' "$1"
}

manifest_database_migration_policy() {
  jq -r '.components["database-postgres"].migrationPolicy // "stable-only"' "$1"
}

manifest_release_channel() {
  jq -r '.release.channel // .build.channel // empty' "$1"
}

manifest_release_date() {
  jq -r '.release.date // .build.date // empty' "$1"
}

manifest_agent_component_ids() {
  jq -r '
    .components
    | to_entries[]
    | select(.value.artifact["windows-x64"] != null)
    | .key
  ' "$1"
}

manifest_agent_source_dir() {
  local component_id="$1"
  local suffix="${component_id#agent-}"
  printf 'runtime/agents/%s\n' "$suffix"
}

is_stable_semver() {
  [[ "$1" =~ ^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)$ ]]
}

is_beta_semver() {
  [[ "$1" =~ ^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)-beta\.(0|[1-9][0-9]*)$ ]]
}

is_desktop_semver_for_channel() {
  local channel="$1"
  local version="$2"
  case "$channel" in
    stable) is_stable_semver "$version" ;;
    beta) is_beta_semver "$version" ;;
    *) return 1 ;;
  esac
}

semver_stable_base() {
  local version="$1"
  if is_beta_semver "$version"; then
    printf '%s' "${version%-beta.*}"
  else
    printf '%s' "$version"
  fi
}

semver_beta_prerelease_number() {
  local version="$1"
  if is_beta_semver "$version"; then
    printf '%s' "${version##*-beta.}"
  fi
}

semver_to_first_beta() {
  local stable_base="$1"
  printf '%s-beta.1' "$stable_base"
}

semver_bump_beta_prerelease() {
  local version="$1"
  local base n
  base="$(semver_stable_base "$version")"
  n="$(semver_beta_prerelease_number "$version")"
  if [[ -z "$n" ]]; then
    semver_to_first_beta "$base"
    return
  fi
  printf '%s-beta.%s' "$base" "$((n + 1))"
}

is_product_semver_for_channel() {
  local channel="$1"
  local version="$2"
  case "$channel" in
    stable) is_stable_semver "$version" ;;
    beta) is_beta_semver "$version" ;;
    *) return 1 ;;
  esac
}

semver_compare_stable() {
  local a="$1"
  local b="$2"
  local aM aN aP bM bN bP
  IFS='.' read -r aM aN aP <<< "$a"
  IFS='.' read -r bM bN bP <<< "$b"
  if (( aM != bM )); then
    (( aM > bM )) && printf 'greater' || printf 'less'
    return
  fi
  if (( aN != bN )); then
    (( aN > bN )) && printf 'greater' || printf 'less'
    return
  fi
  if (( aP != bP )); then
    (( aP > bP )) && printf 'greater' || printf 'less'
    return
  fi
  printf 'equal'
}

semver_gte_stable() {
  local cmp
  cmp="$(semver_compare_stable "$1" "$2")"
  [[ "$cmp" == "greater" || "$cmp" == "equal" ]]
}

semver_lte_stable() {
  local cmp
  cmp="$(semver_compare_stable "$1" "$2")"
  [[ "$cmp" == "less" || "$cmp" == "equal" ]]
}

validate_component_db_bounds() {
  local manifest="$1"
  local component_id="$2"
  local min_db max_db
  min_db="$(jq -r --arg id "$component_id" '.components[$id].minDbSchema // empty' "$manifest")"
  max_db="$(jq -r --arg id "$component_id" '.components[$id].maxDbSchema // empty' "$manifest")"
  [[ -n "$min_db" && -n "$max_db" ]] || {
    echo "ERROR: $component_id requires minDbSchema and maxDbSchema" >&2
    return 1
  }
  is_stable_semver "$min_db" || {
    echo "ERROR: invalid minDbSchema for $component_id: $min_db" >&2
    return 1
  }
  is_stable_semver "$max_db" || {
    echo "ERROR: invalid maxDbSchema for $component_id: $max_db" >&2
    return 1
  }
  semver_lte_stable "$min_db" "$max_db" || {
    echo "ERROR: $component_id minDbSchema ($min_db) must be <= maxDbSchema ($max_db)" >&2
    return 1
  }
}

validate_database_postgres_component_compat() {
  local manifest="$1"
  local db_version
  db_version="$(manifest_database_postgres_version "$manifest")"
  is_stable_semver "$db_version" || {
    echo "ERROR: invalid database-postgres.version: $db_version" >&2
    return 1
  }

  validate_component_db_bounds "$manifest" "desktop" || return 1
  local min_db max_db
  min_db="$(manifest_desktop_min_db "$manifest")"
  max_db="$(manifest_desktop_max_db "$manifest")"
  semver_lte_stable "$min_db" "$db_version" || {
    echo "ERROR: database-postgres.version ($db_version) must be >= desktop.minDbSchema ($min_db)" >&2
    return 1
  }
  semver_lte_stable "$db_version" "$max_db" || {
    echo "ERROR: database-postgres.version ($db_version) must be <= desktop.maxDbSchema ($max_db)" >&2
    return 1
  }

  local component_id
  while IFS= read -r component_id; do
    [[ -n "$component_id" ]] || continue
    validate_component_db_bounds "$manifest" "$component_id" || return 1
    min_db="$(jq -r --arg id "$component_id" '.components[$id].minDbSchema' "$manifest")"
    max_db="$(jq -r --arg id "$component_id" '.components[$id].maxDbSchema' "$manifest")"
    semver_lte_stable "$min_db" "$db_version" || {
      echo "ERROR: database-postgres.version ($db_version) must be >= $component_id.minDbSchema ($min_db)" >&2
      return 1
    }
    semver_lte_stable "$db_version" "$max_db" || {
      echo "ERROR: database-postgres.version ($db_version) must be <= $component_id.maxDbSchema ($max_db)" >&2
      return 1
    }
  done < <(manifest_agent_component_ids "$manifest")
}

validate_desktop_version_matches_channel() {
  local manifest="$1"
  local channel desktop_version
  channel="$(manifest_release_channel "$manifest")"
  desktop_version="$(manifest_desktop_version "$manifest")"
  [[ -n "$channel" && -n "$desktop_version" ]] || {
    echo "ERROR: manifest release.channel and components.desktop.version are required" >&2
    return 1
  }
  if is_desktop_semver_for_channel "$channel" "$desktop_version"; then
    return 0
  fi
  case "$channel" in
    stable)
      echo "ERROR: stable channel requires components.desktop.version X.Y.Z, got: $desktop_version" >&2
      ;;
    beta)
      echo "ERROR: beta channel requires components.desktop.version X.Y.Z-beta.N, got: $desktop_version" >&2
      ;;
    *)
      echo "ERROR: unsupported release.channel: $channel" >&2
      ;;
  esac
  return 1
}

resolve_product_auto_version() {
  local current_product="$1"
  local target_channel="$2"
  local auto_level="$3"

  if [[ "$target_channel" == "beta" ]]; then
    local base
    base="$(semver_stable_base "$current_product")"
    if [[ "$auto_level" != "none" ]]; then
      case "$auto_level" in
        major) base="$(semver_bump_major "$base")" ;;
        minor) base="$(semver_bump_minor "$base")" ;;
        patch) base="$(semver_bump_patch "$base")" ;;
      esac
      semver_to_first_beta "$base"
      return
    fi
    if is_beta_semver "$current_product"; then
      printf '%s' "$current_product"
      return
    fi
    semver_to_first_beta "$base"
    return
  fi

  case "$auto_level" in
    major) semver_bump_major "$current_product" ;;
    minor) semver_bump_minor "$current_product" ;;
    patch) semver_bump_patch "$current_product" ;;
    none) printf '%s' "$current_product" ;;
  esac
}

semver_bump_major() {
  local ver="$1"
  ver="$(semver_stable_base "$ver")"
  local major _minor _patch
  IFS='.' read -r major _minor _patch <<< "$ver"
  printf '%s.0.0' "$((major + 1))"
}

semver_bump_minor() {
  local ver="$1"
  ver="$(semver_stable_base "$ver")"
  local major minor _patch
  IFS='.' read -r major minor _patch <<< "$ver"
  printf '%s.%s.0' "$major" "$((minor + 1))"
}

semver_bump_patch() {
  local ver="$1"
  ver="$(semver_stable_base "$ver")"
  local major minor patch
  IFS='.' read -r major minor patch <<< "$ver"
  printf '%s.%s.%s' "$major" "$minor" "$((patch + 1))"
}

validate_product_version_matches_channel() {
  local manifest="$1"
  local channel product_version
  channel="$(manifest_release_channel "$manifest")"
  product_version="$(manifest_product_version "$manifest")"
  [[ -n "$channel" && -n "$product_version" ]] || {
    echo "ERROR: manifest release.channel and product.version are required" >&2
    return 1
  }
  if is_product_semver_for_channel "$channel" "$product_version"; then
    return 0
  fi
  case "$channel" in
    stable)
      echo "ERROR: stable channel requires product.version X.Y.Z, got: $product_version" >&2
      ;;
    beta)
      echo "ERROR: beta channel requires product.version X.Y.Z-beta.N, got: $product_version" >&2
      ;;
    *)
      echo "ERROR: unsupported release.channel: $channel" >&2
      ;;
  esac
  return 1
}

validate_manifest_v2() {
  local manifest="$1"
  local schema
  schema="$(manifest_schema_version "$manifest")"
  if [[ "$schema" != "2" ]]; then
    echo "ERROR: unsupported manifest schemaVersion=$schema (expected 2)" >&2
    return 1
  fi

  jq -e '
    def semver: test("^(0|[1-9][0-9]*)\\.(0|[1-9][0-9]*)\\.(0|[1-9][0-9]*)$");
    def date: test("^[0-9]{4}-[0-9]{2}-[0-9]{2}$");
    . as $root |
    $root.schemaVersion == 2 and
    $root.product.id == "pactoolkits" and
    ($root.product.version | type == "string" and length > 0) and
    $root.components.desktop.version and
    ($root.components.desktop.version | type == "string" and length > 0) and
    ($root.components.desktop.implementation | IN("avalonia", "electron")) and
    $root.components.desktop.packageId == "pactoolkits" and
    ($root.components.desktop.minDbSchema | semver) and
    ($root.components.desktop.maxDbSchema | semver) and
    ($root.components.desktop.bundles | type == "array") and
    ($root.components.desktop.bundles | length > 0) and
    ($root.components["database-postgres"].version | semver) and
    ($root.components["database-postgres"].migrationPolicy | IN("stable-only", "manual", "isolated-beta")) and
    ($root.release.channel | IN("stable", "beta")) and
    ($root.release.date | date) and
    (
      $root.components.desktop.bundles
      | all(
          . as $bundle |
          $root.components[$bundle] as $component |
          ($component != null) and
          ($component.version | semver) and
          ($component.minDbSchema | semver) and
          ($component.maxDbSchema | semver) and
          ($component.artifact["windows-x64"] | type == "string" and length > 0) and
          (($component.artifact.installDir? // $bundle) | type == "string" and length > 0)
        )
    ) and
    (
      $root.components
      | to_entries
      | all(
          .key == "desktop"
          or (.value.version? // null) == null
          or (.value.version | semver)
        )
    )
  ' "$manifest" >/dev/null || {
    echo "ERROR: manifest validation failed for $manifest" >&2
    return 1
  }

  validate_product_version_matches_channel "$manifest" || return 1
  local channel migration_policy
  channel="$(manifest_release_channel "$manifest")"
  migration_policy="$(manifest_database_migration_policy "$manifest")"
  if [[ "$channel" != "beta" && "$migration_policy" == "isolated-beta" ]]; then
    echo "ERROR: isolated-beta migrationPolicy requires release.channel=beta" >&2
    return 1
  fi
  validate_desktop_version_matches_channel "$manifest" || return 1
  validate_database_postgres_component_compat "$manifest" || return 1
}

release_tag_version() {
  local tag="${1:-}"
  tag="${tag#refs/tags/}"
  [[ "$tag" == v* ]] || return 1
  local version="${tag#v}"
  if is_stable_semver "$version" || is_beta_semver "$version"; then
    printf '%s' "$version"
    return 0
  fi
  return 1
}

validate_release_tag_matches_product_version() {
  local tag="${1:-}"
  local manifest="$2"
  [[ -n "$tag" ]] || return 0

  local tag_version product_version
  tag_version="$(release_tag_version "$tag")" || {
    echo "ERROR: invalid release tag format (expected vX.Y.Z or vX.Y.Z-beta.N): $tag" >&2
    return 1
  }
  product_version="$(manifest_product_version "$manifest")"
  [[ -n "$product_version" ]] || {
    echo "ERROR: manifest product.version is empty: $manifest" >&2
    return 1
  }
  if [[ "$tag_version" != "$product_version" ]]; then
    echo "ERROR: release tag ($tag) does not match manifest product.version ($product_version)" >&2
    return 1
  fi
}

expected_release_prerelease() {
  local manifest="$1"
  local channel
  channel="$(manifest_release_channel "$manifest")"
  case "$channel" in
    beta) printf 'true' ;;
    stable) printf 'false' ;;
    *)
      echo "ERROR: unsupported release.channel: $channel" >&2
      return 1
      ;;
  esac
}

validate_release_prerelease_flag() {
  local manifest="$1"
  local actual_prerelease="${2:-}"
  local expected
  expected="$(expected_release_prerelease "$manifest")" || return 1
  case "$actual_prerelease" in
    true|false) ;;
    *)
      echo "ERROR: prerelease flag must be true or false, got: $actual_prerelease" >&2
      return 1
      ;;
  esac
  if [[ "$expected" != "$actual_prerelease" ]]; then
    local channel
    channel="$(manifest_release_channel "$manifest")"
    echo "ERROR: GitHub release prerelease=$actual_prerelease does not match release.channel=$channel (expected prerelease=$expected)" >&2
    return 1
  fi
}
