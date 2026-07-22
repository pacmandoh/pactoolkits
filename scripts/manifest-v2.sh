#!/usr/bin/env bash
set -euo pipefail

# release-manifest.json schema v2 的共用 jq 辅助

manifest_schema_version() {
  jq -r '.schemaVersion // empty' "$1"
}

manifest_product_version() {
  jq -r '.product.version // empty' "$1"
}

manifest_desktop_version() {
  jq -r '.components.desktop.avalonia.version // empty' "$1"
}

manifest_desktop_min_db() {
  jq -r '.components.desktop.avalonia.minDbSchema // empty' "$1"
}

manifest_desktop_max_db() {
  jq -r '.components.desktop.avalonia.maxDbSchema // empty' "$1"
}

manifest_desktop_package_id() {
  jq -r '.components.desktop.avalonia.packageId // empty' "$1"
}

manifest_agents_version() {
  jq -r '.components["agents"].version // empty' "$1"
}

manifest_agents_min_db() {
  jq -r '.components["agents"].minDbSchema // empty' "$1"
}

manifest_agents_module_ids() {
  jq -r '.components.agents.modules // {} | keys[]' "$1"
}

manifest_agents_module_version() {
  local manifest="$1"
  local module_id="$2"
  jq -r --arg id "$module_id" \
    '.components.agents.modules[$id].version // empty' "$manifest"
}

manifest_agents_module_source_dir() {
  local module_id="${1:-}"
  case "$module_id" in
    Injector)
      printf 'runtime/agents/modules/injector\n'
      ;;
    *)
      echo "ERROR: unsupported agents module source id: ${module_id:-<empty>}" >&2
      return 1
      ;;
  esac
}

manifest_database_postgres_version() {
  # 仅 legacy 清单兼容 V1；候选清单必须使用 V2 嵌套路径
  jq -r '.components.database.postgres.version // .dbSchemaVersion // empty' "$1"
}

manifest_release_channel() {
  jq -r '.release.channel // empty' "$1"
}

manifest_release_date() {
  jq -r '.release.date // empty' "$1"
}

manifest_agents_component_ids() {
  # 仅 Host 包；模块在 components.agents.modules 下
  jq -r '
    .components
    | to_entries[]
    | select(.key == "agents" and .value.artifact["windows-x64"] != null)
    | .key
  ' "$1"
}

manifest_agents_source_dir() {
  local component_id="$1"
  case "$component_id" in
    agents)
      printf 'runtime/agents/host\n'
      ;;
    *)
      echo "ERROR: unknown agents component id: $component_id" >&2
      return 1
      ;;
  esac
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
  if ((aM != bM)); then
    ((aM > bM)) && printf 'greater' || printf 'less'
    return
  fi
  if ((aN != bN)); then
    ((aN > bN)) && printf 'greater' || printf 'less'
    return
  fi
  if ((aP != bP)); then
    ((aP > bP)) && printf 'greater' || printf 'less'
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
  case "$component_id" in
    desktop)
      min_db="$(manifest_desktop_min_db "$manifest")"
      max_db="$(manifest_desktop_max_db "$manifest")"
      ;;
    *)
      min_db="$(jq -r --arg id "$component_id" '.components[$id].minDbSchema // empty' "$manifest")"
      max_db="$(jq -r --arg id "$component_id" '.components[$id].maxDbSchema // empty' "$manifest")"
      ;;
  esac
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
    echo "ERROR: invalid database.postgres.version: $db_version" >&2
    return 1
  }

  validate_component_db_bounds "$manifest" "desktop" || return 1
  local min_db max_db
  min_db="$(manifest_desktop_min_db "$manifest")"
  max_db="$(manifest_desktop_max_db "$manifest")"
  semver_lte_stable "$min_db" "$db_version" || {
    echo "ERROR: database.postgres.version ($db_version) must be >= desktop.minDbSchema ($min_db)" >&2
    return 1
  }
  semver_lte_stable "$db_version" "$max_db" || {
    echo "ERROR: database.postgres.version ($db_version) must be <= desktop.maxDbSchema ($max_db)" >&2
    return 1
  }

  local component_id
  while IFS= read -r component_id; do
    [[ -n "$component_id" ]] || continue
    validate_component_db_bounds "$manifest" "$component_id" || return 1
    min_db="$(jq -r --arg id "$component_id" '.components[$id].minDbSchema' "$manifest")"
    max_db="$(jq -r --arg id "$component_id" '.components[$id].maxDbSchema' "$manifest")"
    semver_lte_stable "$min_db" "$db_version" || {
      echo "ERROR: database.postgres.version ($db_version) must be >= $component_id.minDbSchema ($min_db)" >&2
      return 1
    }
    semver_lte_stable "$db_version" "$max_db" || {
      echo "ERROR: database.postgres.version ($db_version) must be <= $component_id.maxDbSchema ($max_db)" >&2
      return 1
    }
  done < <(manifest_agents_component_ids "$manifest")
}

validate_desktop_version_matches_channel() {
  local manifest="$1"
  local channel desktop_version
  channel="$(manifest_release_channel "$manifest")"
  desktop_version="$(manifest_desktop_version "$manifest")"
  [[ -n "$channel" && -n "$desktop_version" ]] || {
    echo "ERROR: manifest release.channel and components.desktop.avalonia.version are required" >&2
    return 1
  }
  if is_desktop_semver_for_channel "$channel" "$desktop_version"; then
    return 0
  fi
  case "$channel" in
    stable)
      echo "ERROR: stable channel requires components.desktop.avalonia.version X.Y.Z, got: $desktop_version" >&2
      ;;
    beta)
      echo "ERROR: beta channel requires components.desktop.avalonia.version X.Y.Z-beta.N, got: $desktop_version" >&2
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
    (($root.components | keys) == ["agents", "database", "desktop"]) and
    ($root.components.desktop | type == "object") and
    (($root.components.desktop | keys) == ["avalonia"]) and
    ($root.components.desktop.avalonia.version | type == "string" and length > 0) and
    $root.components.desktop.avalonia.packageId == "PacToolkits" and
    ($root.components.desktop.avalonia.minDbSchema | semver) and
    ($root.components.desktop.avalonia.maxDbSchema | semver) and
    ($root.components.agents.version | semver) and
    ($root.components.agents.minDbSchema | semver) and
    ($root.components.agents.maxDbSchema | semver) and
    ($root.components.agents.artifact["windows-x64"] | type == "string" and length > 0) and
    (($root.components.agents.artifact.installDir? // ".") | type == "string" and length > 0) and
    ($root.components.agents.modules | type == "object") and
    ($root.components.agents.modules | length > 0) and
    (
      $root.components.agents.modules
      | to_entries
      | all(
          (.key | type == "string" and length > 0) and
          (.value.version | semver)
        )
    ) and
    ($root.components.database.postgres.version | semver) and
    ($root.release.channel | IN("stable", "beta")) and
    ($root.release.date | date)
  ' "$manifest" > /dev/null || {
    echo "ERROR: manifest validation failed for $manifest" >&2
    return 1
  }

  validate_product_version_matches_channel "$manifest" || return 1
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
    true | false) ;;
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
