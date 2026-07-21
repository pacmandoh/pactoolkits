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
                  [--component-min-db COMPONENT_ID=X.Y.Z]...
                  [--desktop-min-db X.Y.Z]
                  [--channel stable|beta] [--date YYYY-MM-DD]
                  [--output PATH] [--dry-run]

Examples:
  bump-version.sh --product 0.4.1 --desktop 0.4.1 --component agents=0.3.1
  bump-version.sh --module Injector=0.6.2
  bump-version.sh --product auto --desktop 0.4.4
  bump-version.sh --db 1.2.1 --channel beta
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

report_duplicate_component_version() {
  local component_id="$1"
  local previous="$2"
  local current="$3"
  if [[ "$previous" != "$current" ]]; then
    echo "ERROR: conflicting component version for $component_id: $previous vs $current" >&2
  else
    echo "ERROR: duplicate component update for $component_id" >&2
  fi
  exit 1
}

report_duplicate_component_min_db() {
  local component_id="$1"
  local previous="$2"
  local current="$3"
  if [[ "$previous" != "$current" ]]; then
    echo "ERROR: conflicting component minDbSchema for $component_id: $previous vs $current" >&2
  else
    echo "ERROR: duplicate component minDbSchema update for $component_id" >&2
  fi
  exit 1
}

MANIFEST="$ROOT_DIR/release-manifest.json"
PRODUCT=""
DESKTOP=""
DB=""
DESKTOP_MIN_DB=""
CHANNEL=""
DATE_STR="$(date -u +%F)"
DRY_RUN="false"
OUTPUT_FILE=""
declare -a COMPONENT_UPDATES=()
declare -a MODULE_UPDATES=()
declare -a COMPONENT_MIN_DB_UPDATES=()

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
    --db)
      DB="${2:-}"
      shift 2
      ;;
    --desktop-min-db)
      DESKTOP_MIN_DB="${2:-}"
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

effective_channel="$CHANNEL"
if [[ -z "$effective_channel" ]]; then
  effective_channel="$(manifest_release_channel "$MANIFEST")"
fi
final_channel="$effective_channel"

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
for v in "$DB" "$DESKTOP_MIN_DB"; do
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

if [[ -z "$PRODUCT$DESKTOP$DB$DESKTOP_MIN_DB$CHANNEL" && ${#COMPONENT_UPDATES[@]} -eq 0 && ${#MODULE_UPDATES[@]} -eq 0 && ${#COMPONENT_MIN_DB_UPDATES[@]} -eq 0 ]]; then
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
    report_duplicate_component_version "$component_id" "$existing_version" "$component_version"
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
  component_updates_json="$(jq -n \
    --argjson base "$component_updates_json" \
    --arg id "$component_id" \
    --arg ver "$component_version" \
    '$base + {($id): $ver}')"
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
    if [[ "$existing_version" != "$module_version" ]]; then
      echo "ERROR: conflicting module version for $module_id: $existing_version vs $module_version" >&2
    else
      echo "ERROR: duplicate module update for $module_id" >&2
    fi
    exit 1
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
  module_updates_json="$(jq -n \
    --argjson base "$module_updates_json" \
    --arg id "$module_id" \
    --arg ver "$module_version" \
    '$base + {($id): $ver}')"
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

component_min_db_json='{}'
for item in "${COMPONENT_MIN_DB_UPDATES[@]}"; do
  [[ "$item" == *=* ]] || {
    echo "ERROR: --component-min-db expects COMPONENT_ID=X.Y.Z, got: $item" >&2
    exit 1
  }
  component_id="${item%%=*}"
  min_db_version="${item#*=}"
  existing_min_db="$(jq -r --arg id "$component_id" '.[$id] // empty' <<< "$component_min_db_json")"
  if [[ -n "$existing_min_db" ]]; then
    report_duplicate_component_min_db "$component_id" "$existing_min_db" "$min_db_version"
  fi
  is_semver "$min_db_version" || {
    echo "ERROR: invalid component minDbSchema for $component_id: $min_db_version" >&2
    exit 1
  }
  case "$component_id" in
    desktop)
      desktop_impl="$(manifest_desktop_implementation "$MANIFEST")"
      [[ -n "$desktop_impl" ]] || {
        echo "ERROR: unknown manifest component for minDbSchema: $component_id" >&2
        exit 1
      }
      jq -e --arg impl "$desktop_impl" '.components.desktop[$impl].minDbSchema' "$MANIFEST" > /dev/null || {
        echo "ERROR: unknown manifest component for minDbSchema: $component_id" >&2
        exit 1
      }
      ;;
    *)
      jq -e --arg id "$component_id" '.components[$id].minDbSchema' "$MANIFEST" > /dev/null || {
        echo "ERROR: unknown manifest component for minDbSchema: $component_id" >&2
        exit 1
      }
      ;;
  esac
  component_min_db_json="$(jq -n \
    --argjson base "$component_min_db_json" \
    --arg id "$component_id" \
    --arg ver "$min_db_version" \
    '$base + {($id): $ver}')"
done

if [[ -n "$DESKTOP_MIN_DB" ]]; then
  existing_desktop_min_db="$(jq -r '.desktop // empty' <<< "$component_min_db_json")"
  if [[ -n "$existing_desktop_min_db" ]]; then
    if [[ "$existing_desktop_min_db" != "$DESKTOP_MIN_DB" ]]; then
      echo "ERROR: conflicting desktop minDbSchema: --desktop-min-db $DESKTOP_MIN_DB vs --component-min-db desktop=$existing_desktop_min_db" >&2
    else
      echo "ERROR: duplicate desktop minDbSchema update: use --desktop-min-db or --component-min-db desktop=..., not both" >&2
    fi
    exit 1
  fi
fi

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

if [[ "$final_channel" == "beta" && -z "$DESKTOP" && -n "$PRODUCT" ]]; then
  DESKTOP="$PRODUCT"
fi

TMP_FILE="$(mktemp)"
trap 'rm -f "$TMP_FILE"' EXIT

jq \
  --arg product "$PRODUCT" \
  --arg desktop "$DESKTOP" \
  --arg db "$DB" \
  --arg desktop_min_db "$DESKTOP_MIN_DB" \
  --arg channel "$CHANNEL" \
  --arg date "$DATE_STR" \
  --argjson component_updates "$component_updates_json" \
  --argjson module_updates "$module_updates_json" \
  --argjson component_min_db_updates "$component_min_db_json" \
  '
  def desktop_impl:
    .components.desktop
    | to_entries
    | map(select(.value | type == "object"))
    | .[0].key;
  .product.version = (if $product == "" then .product.version else $product end) |
  reduce ($component_updates | to_entries[]) as $item (.;
    if $item.key == "database.postgres" then
      .components.database.postgres.version = $item.value
    elif $item.key == "desktop" then
      .components.desktop[desktop_impl].version = $item.value
    else
      .components[$item.key].version = $item.value
    end
  ) |
  reduce ($module_updates | to_entries[]) as $item (.;
    .components.agents.modules[$item.key].version = $item.value
  ) |
  reduce ($component_min_db_updates | to_entries[]) as $item (.;
    if $item.key == "desktop" then
      .components.desktop[desktop_impl].minDbSchema = $item.value
    else
      .components[$item.key].minDbSchema = $item.value
    end
  ) |
  .components.desktop[desktop_impl].version = (
    if $desktop == "" then .components.desktop[desktop_impl].version else $desktop end
  ) |
  .components.database.postgres.version = (if $db == "" then .components.database.postgres.version else $db end) |
  .components.desktop[desktop_impl].minDbSchema = (
    if $desktop_min_db == "" then .components.desktop[desktop_impl].minDbSchema else $desktop_min_db end
  ) |
  .release.channel = (if $channel == "" then .release.channel else $channel end) |
  .release.date = $date
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
