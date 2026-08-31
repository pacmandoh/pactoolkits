#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
# shellcheck source=manifest-v2.sh
source "$ROOT_DIR/scripts/manifest-v2.sh"

usage() {
  cat << 'USAGE'
Usage:
  bump-version.sh [--product X.Y.Z|X.Y.Z-beta.N|auto] [--desktop X.Y.Z|X.Y.Z-beta.N]
                  [--db X.Y.Z] [--component COMPONENT_ID=X.Y.Z]...
                  [--module MODULE_ID=X.Y.Z]...
                  [--component-min-db api=X.Y.Z]...
                  [--component-max-db api=X.Y.Z]...
                  [--module-min-api-contract MODULE_ID=X.Y.Z]...
                  [--module-max-api-contract MODULE_ID=X.Y.Z]...
                  [--api-contract X.Y.Z]
                  [--desktop-min-api-contract X.Y.Z]
                  [--desktop-max-api-contract X.Y.Z]
                  [--agents-min-desktop X.Y.Z|X.Y.Z-beta.N]
                  [--agents-max-desktop X.Y.Z|X.Y.Z-beta.N]
                  [--channel stable|beta] [--date YYYY-MM-DD]
                  [--output PATH] [--dry-run]

Fields are independent: same-channel --desktop does not rewrite agents bounds;
version bumps do not rewrite schema bounds; --api-contract does not rewrite
desktop or module min/maxApiContract. On stable-to-beta with no agents flags,
min/maxDesktop follow the Desktop being written. On beta, when product fills Desktop
and agents were pinned to the previous Desktop, that pin moves with it; wide
agents ranges only expand edges that the new Desktop would otherwise break.

Examples:
  bump-version.sh --component api=0.1.1
  bump-version.sh --component-min-db api=1.2.26 --component-max-db api=1.2.26
  bump-version.sh --module Injector=0.7.1
  bump-version.sh --module-min-api-contract Injector=1.4.0 --module-max-api-contract Injector=1.4.0
  bump-version.sh --api-contract 1.1.0
  bump-version.sh --desktop-min-api-contract 1.5.0 --desktop-max-api-contract 1.5.0
  bump-version.sh --db 1.2.26 --channel beta
USAGE
}

require_cmd() {
  command -v "$1" > /dev/null 2>&1 || {
    echo "ERROR: required command not found: $1" >&2
    exit 1
  }
}

is_semver() {
  is_stable_semver "$1"
}

is_product_semver_arg() {
  local version="$1"
  local channel="${2:-}"
  if [[ -z "$channel" ]]; then
    is_stable_semver "$version" || is_beta_semver "$version"
    return
  fi
  is_product_semver_for_channel "$channel" "$version"
}

is_desktop_semver_arg() {
  local version="$1"
  local channel="${2:-}"
  if [[ -z "$channel" ]]; then
    is_stable_semver "$version" || is_beta_semver "$version"
    return
  fi
  is_desktop_semver_for_channel "$channel" "$version"
}

is_date() {
  [[ "$1" =~ ^[0-9]{4}-[0-9]{2}-[0-9]{2}$ ]]
}

semver_change_level() {
  local old="$1"
  local new="$2"
  local oM oN oP nM nN nP
  old="$(semver_stable_base "$old")"
  new="$(semver_stable_base "$new")"
  IFS='.' read -r oM oN oP <<< "$old"
  IFS='.' read -r nM nN nP <<< "$new"

  if ((nM < oM)); then
    echo "downgrade"
    return
  fi
  if ((nM > oM)); then
    echo "major"
    return
  fi
  if ((nN < oN)); then
    echo "downgrade"
    return
  fi
  if ((nN > oN)); then
    echo "minor"
    return
  fi
  if ((nP < oP)); then
    echo "downgrade"
    return
  fi
  if ((nP > oP)); then
    echo "patch"
    return
  fi
  echo "none"
}

max_level() {
  local a="$1"
  local b="$2"
  local rank_a=0
  local rank_b=0
  case "$a" in
    patch) rank_a=1 ;;
    minor) rank_a=2 ;;
    major) rank_a=3 ;;
  esac
  case "$b" in
    patch) rank_b=1 ;;
    minor) rank_b=2 ;;
    major) rank_b=3 ;;
  esac
  if ((rank_b > rank_a)); then echo "$b"; else echo "$a"; fi
}

report_duplicate_update() {
  local label="$1"
  local key="$2"
  local previous="$3"
  local current="$4"
  if [[ "$previous" != "$current" ]]; then
    echo "ERROR: conflicting $label for $key: $previous vs $current" >&2
  else
    echo "ERROR: duplicate $label update for $key" >&2
  fi
  exit 1
}

json_put_kv() {
  jq -n --argjson base "$1" --arg id "$2" --arg ver "$3" '$base + {($id): $ver}'
}

# 解析 ID=X.Y.Z 列表为 jq 对象；scope 取 api 或 module
collect_id_ver_map() {
  local flag="$1"
  local bound="$2"
  local scope="$3"
  shift 3
  local out='{}' item id ver existing
  for item in "$@"; do
    [[ "$item" == *=* ]] || {
      case "$scope" in
        api) echo "ERROR: $flag expects api=X.Y.Z, got: $item" >&2 ;;
        module) echo "ERROR: $flag expects MODULE_ID=X.Y.Z, got: $item" >&2 ;;
        *) echo "ERROR: $flag expects ID=X.Y.Z, got: $item" >&2 ;;
      esac
      exit 1
    }
    id="${item%%=*}"
    ver="${item#*=}"
    existing="$(jq -r --arg id "$id" '.[$id] // empty' <<< "$out")"
    if [[ -n "$existing" ]]; then
      report_duplicate_update "$bound" "$id" "$existing" "$ver"
    fi
    is_semver "$ver" || {
      echo "ERROR: invalid $bound for $id: $ver" >&2
      exit 1
    }
    case "$scope" in
      api)
        case "$id" in
          api) ;;
          *)
            echo "ERROR: $flag only supports api (got $id)" >&2
            exit 1
            ;;
        esac
        ;;
      module)
        jq -e --arg id "$id" '.components.agents.modules[$id] | type == "object"' "$MANIFEST" > /dev/null || {
          echo "ERROR: unknown agents module for $bound: $id" >&2
          exit 1
        }
        ;;
      *)
        echo "ERROR: internal collect scope: $scope" >&2
        exit 1
        ;;
    esac
    out="$(json_put_kv "$out" "$id" "$ver")"
  done
  printf '%s' "$out"
}

MANIFEST="$ROOT_DIR/release-manifest.json"
PRODUCT=""
DESKTOP=""
DB=""
API_CONTRACT=""
DESKTOP_MIN_API_CONTRACT=""
DESKTOP_MAX_API_CONTRACT=""
AGENTS_MIN_DESKTOP=""
AGENTS_MAX_DESKTOP=""
CHANNEL=""
DATE_STR="$(date -u +%F)"
DRY_RUN="false"
OUTPUT_FILE=""
declare -a COMPONENT_UPDATES=()
declare -a MODULE_UPDATES=()
declare -a COMPONENT_MIN_DB_UPDATES=()
declare -a COMPONENT_MAX_DB_UPDATES=()
declare -a MODULE_MIN_API_CONTRACT_UPDATES=()
declare -a MODULE_MAX_API_CONTRACT_UPDATES=()

while [[ $# -gt 0 ]]; do
  case "$1" in
    --product)
      PRODUCT="${2:-}"
      shift 2
      ;;
    --desktop)
      DESKTOP="${2:-}"
      shift 2
      ;;
    --component)
      COMPONENT_UPDATES+=("${2:-}")
      shift 2
      ;;
    --module)
      MODULE_UPDATES+=("${2:-}")
      shift 2
      ;;
    --component-min-db)
      COMPONENT_MIN_DB_UPDATES+=("${2:-}")
      shift 2
      ;;
    --component-max-db)
      COMPONENT_MAX_DB_UPDATES+=("${2:-}")
      shift 2
      ;;
    --module-min-api-contract)
      MODULE_MIN_API_CONTRACT_UPDATES+=("${2:-}")
      shift 2
      ;;
    --module-max-api-contract)
      MODULE_MAX_API_CONTRACT_UPDATES+=("${2:-}")
      shift 2
      ;;
    --db)
      DB="${2:-}"
      shift 2
      ;;
    --api-contract)
      API_CONTRACT="${2:-}"
      shift 2
      ;;
    --desktop-min-api-contract)
      DESKTOP_MIN_API_CONTRACT="${2:-}"
      shift 2
      ;;
    --desktop-max-api-contract)
      DESKTOP_MAX_API_CONTRACT="${2:-}"
      shift 2
      ;;
    --agents-min-desktop)
      AGENTS_MIN_DESKTOP="${2:-}"
      shift 2
      ;;
    --agents-max-desktop)
      AGENTS_MAX_DESKTOP="${2:-}"
      shift 2
      ;;
    --channel)
      CHANNEL="${2:-}"
      shift 2
      ;;
    --date)
      DATE_STR="${2:-}"
      shift 2
      ;;
    --output)
      OUTPUT_FILE="${2:-}"
      shift 2
      ;;
    --dry-run)
      DRY_RUN="true"
      shift
      ;;
    -h | --help)
      usage
      exit 0
      ;;
    *)
      echo "ERROR: unknown arg: $1" >&2
      usage
      exit 1
      ;;
  esac
done

require_cmd jq
[[ -f "$MANIFEST" ]] || {
  echo "ERROR: manifest not found: $MANIFEST" >&2
  exit 1
}

final_channel="$CHANNEL"
if [[ -z "$final_channel" ]]; then
  final_channel="$(manifest_release_channel "$MANIFEST")"
fi

if [[ -n "$PRODUCT" && "$PRODUCT" != "auto" ]]; then
  is_product_semver_arg "$PRODUCT" "$final_channel" || {
    case "$final_channel" in
      beta)
        echo "ERROR: invalid --product for beta channel (expect X.Y.Z-beta.N or auto)" >&2
        ;;
      *)
        echo "ERROR: invalid --product (expect X.Y.Z or auto)" >&2
        ;;
    esac
    exit 1
  }
fi
if [[ -n "$DESKTOP" ]]; then
  is_desktop_semver_arg "$DESKTOP" "$final_channel" || {
    case "$final_channel" in
      beta)
        echo "ERROR: invalid --desktop for beta channel (expect X.Y.Z-beta.N)" >&2
        ;;
      *)
        echo "ERROR: invalid --desktop (expect X.Y.Z)" >&2
        ;;
    esac
    exit 1
  }
fi
for v in "$DB" "$API_CONTRACT"; do
  [[ -z "$v" ]] || is_semver "$v" || {
    echo "ERROR: invalid semver arg" >&2
    exit 1
  }
done
is_date "$DATE_STR" || {
  echo "ERROR: invalid --date" >&2
  exit 1
}

if [[ -n "$CHANNEL" ]]; then
  case "$CHANNEL" in
    stable | beta) ;;
    *)
      echo "ERROR: --channel must be stable|beta" >&2
      exit 1
      ;;
  esac
fi

if [[ -z "$PRODUCT$DESKTOP$DB$API_CONTRACT$DESKTOP_MIN_API_CONTRACT$DESKTOP_MAX_API_CONTRACT$AGENTS_MIN_DESKTOP$AGENTS_MAX_DESKTOP$CHANNEL" &&
  ${#COMPONENT_UPDATES[@]} -eq 0 &&
  ${#MODULE_UPDATES[@]} -eq 0 &&
  ${#COMPONENT_MIN_DB_UPDATES[@]} -eq 0 &&
  ${#COMPONENT_MAX_DB_UPDATES[@]} -eq 0 &&
  ${#MODULE_MIN_API_CONTRACT_UPDATES[@]} -eq 0 &&
  ${#MODULE_MAX_API_CONTRACT_UPDATES[@]} -eq 0 ]]; then
  echo "ERROR: nothing to update" >&2
  usage
  exit 1
fi

validate_manifest_v2 "$MANIFEST"

current_product="$(manifest_product_version "$MANIFEST")"
current_desktop="$(manifest_desktop_version "$MANIFEST")"
current_db="$(manifest_database_postgres_version "$MANIFEST")"

component_updates_json='{}'
module_updates_json='{}'
product_auto_level="none"
desktop_level="none"
db_level="none"

for item in "${COMPONENT_UPDATES[@]}"; do
  [[ "$item" == *=* ]] || {
    echo "ERROR: --component expects COMPONENT_ID=X.Y.Z, got: $item" >&2
    exit 1
  }
  component_id="${item%%=*}"
  component_version="${item#*=}"
  existing_version="$(jq -r --arg id "$component_id" '.[$id] // empty' <<< "$component_updates_json")"
  if [[ -n "$existing_version" ]]; then
    report_duplicate_update "component version" "$component_id" "$existing_version" "$component_version"
  fi
  is_semver "$component_version" || {
    echo "ERROR: invalid component version for $component_id: $component_version" >&2
    exit 1
  }
  case "$component_id" in
    database.postgres)
      jq -e '.components.database.postgres.version' "$MANIFEST" > /dev/null || {
        echo "ERROR: unknown manifest component: $component_id" >&2
        exit 1
      }
      current_component_version="$(jq -r '.components.database.postgres.version' "$MANIFEST")"
      ;;
    desktop)
      current_component_version="$(manifest_desktop_version "$MANIFEST")"
      [[ -n "$current_component_version" ]] || {
        echo "ERROR: unknown manifest component: $component_id" >&2
        exit 1
      }
      ;;
    *)
      jq -e --arg id "$component_id" '.components[$id].version' "$MANIFEST" > /dev/null || {
        echo "ERROR: unknown manifest component: $component_id" >&2
        exit 1
      }
      current_component_version="$(jq -r --arg id "$component_id" '.components[$id].version' "$MANIFEST")"
      ;;
  esac
  component_level="$(semver_change_level "$current_component_version" "$component_version")"
  [[ "$component_level" != "downgrade" ]] || {
    echo "ERROR: --component $component_id cannot downgrade ($current_component_version -> $component_version)" >&2
    exit 1
  }
  product_auto_level="$(max_level "$product_auto_level" "$component_level")"
  component_updates_json="$(json_put_kv "$component_updates_json" "$component_id" "$component_version")"
done

for item in "${MODULE_UPDATES[@]}"; do
  [[ "$item" == *=* ]] || {
    echo "ERROR: --module expects MODULE_ID=X.Y.Z, got: $item" >&2
    exit 1
  }
  module_id="${item%%=*}"
  module_version="${item#*=}"
  existing_version="$(jq -r --arg id "$module_id" '.[$id] // empty' <<< "$module_updates_json")"
  if [[ -n "$existing_version" ]]; then
    report_duplicate_update "module version" "$module_id" "$existing_version" "$module_version"
  fi
  is_semver "$module_version" || {
    echo "ERROR: invalid module version for $module_id: $module_version" >&2
    exit 1
  }
  jq -e --arg id "$module_id" '.components.agents.modules[$id].version' "$MANIFEST" > /dev/null || {
    echo "ERROR: unknown agents module: $module_id" >&2
    exit 1
  }
  current_module_version="$(jq -r --arg id "$module_id" '.components.agents.modules[$id].version' "$MANIFEST")"
  module_level="$(semver_change_level "$current_module_version" "$module_version")"
  [[ "$module_level" != "downgrade" ]] || {
    echo "ERROR: --module $module_id cannot downgrade ($current_module_version -> $module_version)" >&2
    exit 1
  }
  product_auto_level="$(max_level "$product_auto_level" "$module_level")"
  module_updates_json="$(json_put_kv "$module_updates_json" "$module_id" "$module_version")"
done

if [[ -n "$DESKTOP" ]]; then
  existing_desktop="$(jq -r '.desktop // empty' <<< "$component_updates_json")"
  if [[ -n "$existing_desktop" ]]; then
    if [[ "$existing_desktop" != "$DESKTOP" ]]; then
      echo "ERROR: conflicting desktop version: --desktop $DESKTOP vs --component desktop=$existing_desktop" >&2
    else
      echo "ERROR: duplicate desktop update: use --desktop or --component desktop=..., not both" >&2
    fi
    exit 1
  fi
else
  DESKTOP="$(jq -r '.desktop // empty' <<< "$component_updates_json")"
fi

if [[ -n "$DB" ]]; then
  existing_db="$(jq -r '."database.postgres" // empty' <<< "$component_updates_json")"
  if [[ -n "$existing_db" ]]; then
    if [[ "$existing_db" != "$DB" ]]; then
      echo "ERROR: conflicting database version: --db $DB vs --component database.postgres=$existing_db" >&2
    else
      echo "ERROR: duplicate database update: use --db or --component database.postgres=..., not both" >&2
    fi
    exit 1
  fi
fi

component_min_db_json="$(collect_id_ver_map --component-min-db "component minDbSchema" api "${COMPONENT_MIN_DB_UPDATES[@]}")"
component_max_db_json="$(collect_id_ver_map --component-max-db "component maxDbSchema" api "${COMPONENT_MAX_DB_UPDATES[@]}")"

module_min_api_contract_json="$(collect_id_ver_map --module-min-api-contract "module minApiContract" module "${MODULE_MIN_API_CONTRACT_UPDATES[@]}")"
module_max_api_contract_json="$(collect_id_ver_map --module-max-api-contract "module maxApiContract" module "${MODULE_MAX_API_CONTRACT_UPDATES[@]}")"

[[ -n "$DESKTOP" ]] && desktop_level="$(semver_change_level "$(semver_stable_base "$current_desktop")" "$(semver_stable_base "$DESKTOP")")"
[[ -n "$DB" ]] && db_level="$(semver_change_level "$current_db" "$DB")"
[[ "$desktop_level" != "downgrade" ]] || {
  echo "ERROR: --desktop cannot downgrade ($current_desktop -> $DESKTOP)" >&2
  exit 1
}
[[ "$db_level" != "downgrade" ]] || {
  echo "ERROR: --db cannot downgrade ($current_db -> $DB)" >&2
  exit 1
}

product_auto_level="$(max_level "$product_auto_level" "$desktop_level")"
product_auto_level="$(max_level "$product_auto_level" "$db_level")"

current_manifest_channel="$(manifest_release_channel "$MANIFEST")"
entering_beta_channel=false
if [[ "$final_channel" == "beta" && "$current_manifest_channel" == "stable" ]]; then
  if is_stable_semver "$current_product"; then
    entering_beta_channel=true
  fi
fi

should_auto_product=false
if [[ "$PRODUCT" == "auto" ]]; then
  should_auto_product=true
elif [[ -z "$PRODUCT" && (${#COMPONENT_UPDATES[@]} -gt 0 || ${#MODULE_UPDATES[@]} -gt 0 || -n "$DESKTOP$DB") ]]; then
  should_auto_product=true
elif [[ "$entering_beta_channel" == "true" ]]; then
  should_auto_product=true
fi

if [[ "$should_auto_product" == "true" ]]; then
  PRODUCT="$(resolve_product_auto_version "$current_product" "$final_channel" "$product_auto_level")"
elif [[ -z "$PRODUCT" ]]; then
  PRODUCT=""
fi

# beta 且未传 Desktop 时与 product 对齐（与 Velopack packVersion 同号）
desktop_from_product=false
if [[ "$final_channel" == "beta" && -z "$DESKTOP" && -n "$PRODUCT" ]]; then
  DESKTOP="$PRODUCT"
  desktop_from_product=true
fi

# 仅 stable 切 beta：未传 agents 区间时 min/maxDesktop 取本次 Desktop
if [[ "$entering_beta_channel" == "true" && -n "$DESKTOP" ]]; then
  if [[ -z "$AGENTS_MIN_DESKTOP" ]]; then
    AGENTS_MIN_DESKTOP="$DESKTOP"
  fi
  if [[ -z "$AGENTS_MAX_DESKTOP" ]]; then
    AGENTS_MAX_DESKTOP="$DESKTOP"
  fi
fi

# product 代填抬 Desktop：原先单点钉住当前 Desktop 则整段平移，否则只补越界未写侧
if [[ "$desktop_from_product" == "true" && -n "$DESKTOP" && "$DESKTOP" != "$current_desktop" ]]; then
  current_agents_min="$(manifest_agents_min_desktop "$MANIFEST")"
  current_agents_max="$(manifest_agents_max_desktop "$MANIFEST")"
  if [[ -z "$AGENTS_MIN_DESKTOP" && -z "$AGENTS_MAX_DESKTOP" &&
    -n "$current_agents_min" && "$current_agents_min" == "$current_agents_max" &&
    "$current_agents_max" == "$current_desktop" ]]; then
    AGENTS_MIN_DESKTOP="$DESKTOP"
    AGENTS_MAX_DESKTOP="$DESKTOP"
  else
    if [[ -z "$AGENTS_MAX_DESKTOP" && -n "$current_agents_max" ]] \
      && ! semver_lte_full "$DESKTOP" "$current_agents_max"; then
      AGENTS_MAX_DESKTOP="$DESKTOP"
    fi
    if [[ -z "$AGENTS_MIN_DESKTOP" && -n "$current_agents_min" ]] \
      && ! semver_gte_full "$DESKTOP" "$current_agents_min"; then
      AGENTS_MIN_DESKTOP="$DESKTOP"
    fi
  fi
fi

for v in "$AGENTS_MIN_DESKTOP" "$AGENTS_MAX_DESKTOP"; do
  if [[ -n "$v" ]]; then
    if ! is_stable_semver "$v" && ! is_beta_semver "$v"; then
      echo "ERROR: invalid agents minDesktop/maxDesktop: $v" >&2
      exit 1
    fi
  fi
done

for v in "$DESKTOP_MIN_API_CONTRACT" "$DESKTOP_MAX_API_CONTRACT"; do
  if [[ -n "$v" ]]; then
    is_semver "$v" || {
      echo "ERROR: invalid desktop minApiContract/maxApiContract: $v" >&2
      exit 1
    }
  fi
done

TMP_FILE="$(mktemp)"
trap 'rm -f "$TMP_FILE"' EXIT

jq \
  --arg product "$PRODUCT" \
  --arg desktop "$DESKTOP" \
  --arg db "$DB" \
  --arg api_contract "$API_CONTRACT" \
  --arg desktop_min_api_contract "$DESKTOP_MIN_API_CONTRACT" \
  --arg desktop_max_api_contract "$DESKTOP_MAX_API_CONTRACT" \
  --arg agents_min_desktop "$AGENTS_MIN_DESKTOP" \
  --arg agents_max_desktop "$AGENTS_MAX_DESKTOP" \
  --arg channel "$CHANNEL" \
  --arg date "$DATE_STR" \
  --argjson component_updates "$component_updates_json" \
  --argjson module_updates "$module_updates_json" \
  --argjson component_min_db_updates "$component_min_db_json" \
  --argjson component_max_db_updates "$component_max_db_json" \
  --argjson module_min_api_contract_updates "$module_min_api_contract_json" \
  --argjson module_max_api_contract_updates "$module_max_api_contract_json" \
  '
  .product.version = (if $product == "" then .product.version else $product end) |
  reduce ($component_updates | to_entries[]) as $item (.;
    if $item.key == "database.postgres" then
      .components.database.postgres.version = $item.value
    elif $item.key == "desktop" then
      .components.desktop.avalonia.version = $item.value
    else
      .components[$item.key].version = $item.value
    end
  ) |
  reduce ($module_updates | to_entries[]) as $item (.;
    .components.agents.modules[$item.key].version = $item.value
  ) |
  reduce ($component_min_db_updates | to_entries[]) as $item (.;
    if $item.key == "api" then
      .components.api.minDbSchema = $item.value
    else
      .
    end
  ) |
  reduce ($component_max_db_updates | to_entries[]) as $item (.;
    if $item.key == "api" then
      .components.api.maxDbSchema = $item.value
    else
      .
    end
  ) |
  reduce ($module_min_api_contract_updates | to_entries[]) as $item (.;
    .components.agents.modules[$item.key].minApiContract = $item.value
  ) |
  reduce ($module_max_api_contract_updates | to_entries[]) as $item (.;
    .components.agents.modules[$item.key].maxApiContract = $item.value
  ) |
  .components.desktop.avalonia.version = (
    if $desktop == "" then .components.desktop.avalonia.version else $desktop end
  ) |
  .components.database.postgres.version = (if $db == "" then .components.database.postgres.version else $db end) |
  .components.api.contractVersion = (
    if $api_contract == "" then .components.api.contractVersion else $api_contract end
  ) |
  .components.desktop.avalonia.minApiContract = (
    if $desktop_min_api_contract == "" then .components.desktop.avalonia.minApiContract else $desktop_min_api_contract end
  ) |
  .components.desktop.avalonia.maxApiContract = (
    if $desktop_max_api_contract == "" then .components.desktop.avalonia.maxApiContract else $desktop_max_api_contract end
  ) |
  .components.agents.minDesktop = (
    if $agents_min_desktop == "" then .components.agents.minDesktop else $agents_min_desktop end
  ) |
  .components.agents.maxDesktop = (
    if $agents_max_desktop == "" then .components.agents.maxDesktop else $agents_max_desktop end
  ) |
  .release.channel = (if $channel == "" then .release.channel else $channel end) |
  .release.date = $date |
  del(.components.desktop.avalonia.minDbSchema, .components.desktop.avalonia.maxDbSchema)
  ' "$MANIFEST" > "$TMP_FILE"

validate_manifest_v2 "$TMP_FILE"

if [[ -n "$OUTPUT_FILE" ]]; then
  cp "$TMP_FILE" "$OUTPUT_FILE"
fi

if [[ "$DRY_RUN" == "true" ]]; then
  echo "=== DRY RUN ==="
  diff -u "$MANIFEST" "$TMP_FILE" || true
  exit 0
fi

if [[ -n "$OUTPUT_FILE" ]]; then
  echo "Preview written to $OUTPUT_FILE"
  jq '{schemaVersion, product, components, release}' "$OUTPUT_FILE"
  exit 0
fi

mv "$TMP_FILE" "$MANIFEST"

echo "Updated $MANIFEST"
jq '{schemaVersion, product, components, release}' "$MANIFEST"

"$ROOT_DIR/scripts/export-version.sh"
