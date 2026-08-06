#!/usr/bin/env bash
set -euo pipefail

# Manifest V2 查询和校验函数由本地发布脚本与 CI 共同使用

# Windows/MSYS2 原生 jq 常输出 CRLF；去掉 CR，避免 id/版本带 CR 导致查找失败或终端回车覆盖报错行
jq_r() {
  jq -r "$@" | tr -d '\r'
}

manifest_schema_version() {
  jq_r '.schemaVersion // empty' "$1"
}

manifest_product_version() {
  jq_r '.product.version // empty' "$1"
}

manifest_desktop_version() {
  jq_r '.components.desktop.avalonia.version // empty' "$1"
}

manifest_desktop_min_db() {
  jq_r '.components.desktop.avalonia.minDbSchema // empty' "$1"
}

manifest_desktop_max_db() {
  jq_r '.components.desktop.avalonia.maxDbSchema // empty' "$1"
}

manifest_desktop_package_id() {
  jq_r '.components.desktop.avalonia.packageId // empty' "$1"
}

manifest_agents_version() {
  jq_r '.components["agents"].version // empty' "$1"
}

manifest_agents_min_desktop() {
  jq_r '.components["agents"].minDesktop // empty' "$1"
}

manifest_agents_max_desktop() {
  jq_r '.components["agents"].maxDesktop // empty' "$1"
}

manifest_agents_module_ids() {
  jq_r '.components.agents.modules // {} | keys[]' "$1"
}

manifest_agents_module_version() {
  local manifest="$1"
  local module_id="$2"
  jq_r --arg id "$module_id" \
    '.components.agents.modules[$id].version // empty' "$manifest"
}

# 以 module.json 的 ID 解析源码目录，使新增模块无需修改脚本
manifest_agents_module_source_dir() {
  local module_id="${1:-}"
  if [[ -z "$module_id" ]]; then
    echo "ERROR: agents module id is required" >&2
    return 1
  fi

  local root="${ROOT_DIR:-}"
  if [[ -z "$root" ]]; then
    root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
  fi

  local modules_root="$root/runtime/agents/modules"
  if [[ ! -d "$modules_root" ]]; then
    echo "ERROR: missing agents modules root: $modules_root" >&2
    return 1
  fi

  local found=""
  local dir meta id
  for dir in "$modules_root"/*/; do
    [[ -d "$dir" ]] || continue
    meta="${dir}module.json"
    [[ -f "$meta" ]] || continue
    id="$(jq_r '.id // empty' "$meta")"
    if [[ "$id" != "$module_id" ]]; then
      continue
    fi
    if [[ -n "$found" ]]; then
      echo "ERROR: duplicate module.json id=$module_id under $modules_root" >&2
      return 1
    fi
    found="${dir%/}"
  done

  if [[ -z "$found" ]]; then
    echo "ERROR: no runtime/agents/modules/*/module.json with id=$module_id" >&2
    return 1
  fi

  printf '%s\n' "${found#"$root"/}"
}

agents_module_json_entry_win_x64() {
  jq_r '.entry["win-x64"] // empty' "$1"
}

# 单模块校验同时约束描述文件、默认配置、schema 和入口文件
validate_agents_module_dir() {
  local module_dir="$1"
  local expected_id="$2"
  local min_bytes="${3:-0}"
  local expected_version="${4:-}"

  [[ -d "$module_dir" ]] || {
    echo "ERROR: missing agent module dir: $module_dir" >&2
    return 1
  }
  [[ -f "$module_dir/module.json" ]] || {
    echo "ERROR: missing module.json: $module_dir/module.json" >&2
    return 1
  }

  local id entry version
  id="$(jq_r '.id // empty' "$module_dir/module.json")"
  [[ "$id" == "$expected_id" ]] || {
    echo "ERROR: module.json id=$id != directory id=$expected_id ($module_dir)" >&2
    return 1
  }

  if [[ -n "$expected_version" ]]; then
    version="$(jq_r '.version // empty' "$module_dir/module.json")"
    [[ "$version" == "$expected_version" ]] || {
      echo "ERROR: module.json version=$version != manifest version=$expected_version ($module_dir)" >&2
      return 1
    }
  fi

  entry="$(agents_module_json_entry_win_x64 "$module_dir/module.json")"
  [[ -n "$entry" ]] || {
    echo "ERROR: module.json missing entry.win-x64: $module_dir/module.json" >&2
    return 1
  }
  [[ "$entry" != *\\* && "$entry" == "$(basename "$entry")" ]] || {
    echo "ERROR: module.json entry.win-x64 must be a file name: $entry ($module_dir/module.json)" >&2
    return 1
  }
  [[ -f "$module_dir/$entry" ]] || {
    echo "ERROR: missing module entry binary: $module_dir/$entry" >&2
    return 1
  }
  [[ -f "$module_dir/settings.json" ]] || {
    echo "ERROR: missing settings.json: $module_dir/settings.json" >&2
    return 1
  }
  [[ -f "$module_dir/settings.schema.json" ]] || {
    echo "ERROR: missing settings.schema.json: $module_dir/settings.schema.json" >&2
    return 1
  }

  if [[ "${min_bytes:-0}" -gt 0 ]]; then
    local size
    size="$(wc -c < "$module_dir/$entry" | tr -d ' ')"
    if [[ "${size:-0}" -le "$min_bytes" ]]; then
      echo "ERROR: module entry too small to be valid ($module_dir/$entry, ${size} bytes)" >&2
      return 1
    fi
  fi
}

# Agents staging 必须与发布清单形成精确模块集合，禁止缺失或残留目录
validate_agents_staging_layout() {
  local dir="$1"
  local manifest="$2"
  local min_bytes="${3:-4096}"

  [[ -f "$dir/Agents.exe" ]] || {
    echo "ERROR: missing Host binary: $dir/Agents.exe" >&2
    echo "Build via CI (build-agents.yml) or publish host + modules into --artifact-dir." >&2
    return 1
  }
  [[ -f "$dir/ReleaseManifest.json" ]] || {
    echo "ERROR: missing Agents ReleaseManifest.json: $dir/ReleaseManifest.json" >&2
    return 1
  }

  local module_count=0
  local module_id module_version
  while IFS= read -r module_id; do
    module_id="${module_id//$'\r'/}"
    [[ -n "$module_id" ]] || continue
    module_count=$((module_count + 1))
    module_version="$(manifest_agents_module_version "$manifest" "$module_id")"
    [[ -n "$module_version" ]] || {
      echo "ERROR: release-manifest.json missing version for module=$module_id" >&2
      return 1
    }
    validate_agents_module_dir \
      "$dir/Modules/$module_id" \
      "$module_id" \
      "$min_bytes" \
      "$module_version" || return 1
  done < <(manifest_agents_module_ids "$manifest")

  if [[ "$module_count" -eq 0 ]]; then
    echo "ERROR: release-manifest.json has no components.agents.modules entries" >&2
    return 1
  fi

  local staged_dir staged_id
  for staged_dir in "$dir/Modules"/*/; do
    [[ -d "$staged_dir" ]] || continue
    staged_id="$(basename "${staged_dir%/}")"
    if ! jq -e --arg id "$staged_id" '.components.agents.modules[$id] != null' "$manifest" > /dev/null; then
      echo "ERROR: staging has module dir not listed in release-manifest.json: $staged_dir" >&2
      return 1
    fi
  done
}

manifest_database_postgres_version() {
  # V1 仅用于读取历史清单，待发布候选必须使用 V2 结构
  jq_r '.components.database.postgres.version // .dbSchemaVersion // empty' "$1"
}

manifest_release_channel() {
  jq_r '.release.channel // empty' "$1"
}

manifest_release_date() {
  jq_r '.release.date // empty' "$1"
}

manifest_agents_component_ids() {
  # Host 版本独立于 modules 映射，避免模块版本变化隐式修改 Host 版本
  jq_r '
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

# SemVer 2.0 全量比较（含 prerelease；build +… 忽略）；输出 less|equal|greater
semver_compare_full() {
  local a b a_core a_pre b_core b_pre
  a="${1%%+*}"
  b="${2%%+*}"
  if [[ "$a" == *-* ]]; then
    a_core="${a%%-*}"
    a_pre="${a#*-}"
  else
    a_core="$a"
    a_pre=""
  fi
  if [[ "$b" == *-* ]]; then
    b_core="${b%%-*}"
    b_pre="${b#*-}"
  else
    b_core="$b"
    b_pre=""
  fi

  local core_cmp
  core_cmp="$(semver_compare_stable "$a_core" "$b_core")"
  if [[ "$core_cmp" != "equal" ]]; then
    printf '%s' "$core_cmp"
    return
  fi

  # 同核心：正式版 > prerelease
  if [[ -z "$a_pre" && -z "$b_pre" ]]; then
    printf 'equal'
    return
  fi
  if [[ -z "$a_pre" ]]; then
    printf 'greater'
    return
  fi
  if [[ -z "$b_pre" ]]; then
    printf 'less'
    return
  fi

  # prerelease 标识点分隔比较
  local IFS='.'
  # shellcheck disable=SC2206
  local -a a_ids=($a_pre) b_ids=($b_pre)
  local i a_id b_id a_num b_num
  local max=${#a_ids[@]}
  ((${#b_ids[@]} > max)) && max=${#b_ids[@]}
  for ((i = 0; i < max; i++)); do
    if ((i >= ${#a_ids[@]})); then
      printf 'less'
      return
    fi
    if ((i >= ${#b_ids[@]})); then
      printf 'greater'
      return
    fi
    a_id="${a_ids[$i]}"
    b_id="${b_ids[$i]}"
    a_num=0
    b_num=0
    [[ "$a_id" =~ ^(0|[1-9][0-9]*)$ ]] && a_num=1
    [[ "$b_id" =~ ^(0|[1-9][0-9]*)$ ]] && b_num=1
    if ((a_num && b_num)); then
      if ((10#$a_id != 10#$b_id)); then
        ((10#$a_id > 10#$b_id)) && printf 'greater' || printf 'less'
        return
      fi
      continue
    fi
    if ((a_num && !b_num)); then
      printf 'less'
      return
    fi
    if ((!a_num && b_num)); then
      printf 'greater'
      return
    fi
    if [[ "$a_id" > "$b_id" ]]; then
      printf 'greater'
      return
    fi
    if [[ "$a_id" < "$b_id" ]]; then
      printf 'less'
      return
    fi
  done
  printf 'equal'
}

semver_gte_full() {
  local cmp
  cmp="$(semver_compare_full "$1" "$2")"
  [[ "$cmp" == "greater" || "$cmp" == "equal" ]]
}

semver_lte_full() {
  local cmp
  cmp="$(semver_compare_full "$1" "$2")"
  [[ "$cmp" == "less" || "$cmp" == "equal" ]]
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
  local component_id="${2//$'\r'/}"
  local min_db max_db
  case "$component_id" in
    desktop)
      min_db="$(manifest_desktop_min_db "$manifest")"
      max_db="$(manifest_desktop_max_db "$manifest")"
      ;;
    *)
      echo "ERROR: component minDbSchema is only defined for desktop (got $component_id)" >&2
      return 1
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

validate_agents_desktop_bounds() {
  local manifest="$1"
  local min_desktop max_desktop desktop_version
  min_desktop="$(manifest_agents_min_desktop "$manifest")"
  max_desktop="$(manifest_agents_max_desktop "$manifest")"
  [[ -n "$min_desktop" && -n "$max_desktop" ]] || {
    echo "ERROR: agents requires minDesktop and maxDesktop" >&2
    return 1
  }

  # 接受 stable / beta 通道形式（与 Desktop/Product 版本一致）
  if ! is_stable_semver "$min_desktop" && ! is_beta_semver "$min_desktop"; then
    echo "ERROR: invalid agents.minDesktop: $min_desktop" >&2
    return 1
  fi
  if ! is_stable_semver "$max_desktop" && ! is_beta_semver "$max_desktop"; then
    echo "ERROR: invalid agents.maxDesktop: $max_desktop" >&2
    return 1
  fi
  semver_lte_full "$min_desktop" "$max_desktop" || {
    echo "ERROR: agents.minDesktop ($min_desktop) must be <= maxDesktop ($max_desktop)" >&2
    return 1
  }

  desktop_version="$(manifest_desktop_version "$manifest")"
  if ! is_stable_semver "$desktop_version" && ! is_beta_semver "$desktop_version"; then
    echo "ERROR: invalid desktop.avalonia.version for agents bounds check: $desktop_version" >&2
    return 1
  fi
  # 严格 SemVer：下限为 stable 1.0.2 时，1.0.2-beta 不算进
  semver_gte_full "$desktop_version" "$min_desktop" || {
    echo "ERROR: desktop.avalonia.version ($desktop_version) must be >= agents.minDesktop ($min_desktop)" >&2
    return 1
  }
  semver_lte_full "$desktop_version" "$max_desktop" || {
    echo "ERROR: desktop.avalonia.version ($desktop_version) must be <= agents.maxDesktop ($max_desktop)" >&2
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

  validate_agents_desktop_bounds "$manifest" || return 1
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
    ($root.components.agents.minDesktop | type == "string" and length > 0) and
    ($root.components.agents.maxDesktop | type == "string" and length > 0) and
    ($root.components.agents.artifact["windows-x64"] | type == "string" and length > 0) and
    (($root.components.agents.artifact.installDir? // ".") | type == "string" and length > 0) and
    ($root.components.agents.modules | type == "object") and
    ($root.components.agents.modules | length > 0) and
    (
      $root.components.agents.modules
      | to_entries
      | all(
          (.key | type == "string" and test("^[A-Za-z0-9][A-Za-z0-9._-]*$")) and
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
