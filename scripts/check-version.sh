#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
# shellcheck source=manifest-v2.sh
source "$ROOT_DIR/scripts/manifest-v2.sh"

MANIFEST="$ROOT_DIR/release-manifest.json"
DESKTOP_VERSION_PROPS="$ROOT_DIR/apps/desktop-avalonia/src/Version.g.props"
DESKTOP_VERSION_JSON="$ROOT_DIR/apps/desktop-avalonia/src/ReleaseManifest.json"

require_cmd() {
  command -v "$1" > /dev/null 2>&1 || {
    echo "ERROR: required command not found: $1" >&2
    exit 1
  }
}

require_cmd jq

props_app_version() {
  local file="$1"
  if command -v rg > /dev/null 2>&1; then
    rg -o "<AppVersion>[^<]+</AppVersion>" "$file" | sed -E 's#<AppVersion>([^<]+)</AppVersion>#\1#'
  else
    grep -oE "<AppVersion>[^<]+</AppVersion>" "$file" | sed -E 's#<AppVersion>([^<]+)</AppVersion>#\1#'
  fi
}

[[ -f "$MANIFEST" ]] || {
  echo "ERROR: missing $MANIFEST" >&2
  exit 1
}
[[ -f "$DESKTOP_VERSION_PROPS" ]] || {
  echo "ERROR: missing $DESKTOP_VERSION_PROPS (run scripts/export-version.sh)" >&2
  exit 1
}
[[ -f "$DESKTOP_VERSION_JSON" ]] || {
  echo "ERROR: missing $DESKTOP_VERSION_JSON (run scripts/export-version.sh)" >&2
  exit 1
}

validate_manifest_v2 "$MANIFEST"

expected_desktop_json="$(mktemp)"
expected_agents_json="$(mktemp)"
trap 'rm -f "$expected_desktop_json" "$expected_agents_json"' EXIT
write_desktop_release_manifest "$MANIFEST" "$expected_desktop_json"
write_agents_release_manifest "$MANIFEST" "$expected_agents_json"

if [[ "$(jq -S . "$DESKTOP_VERSION_JSON")" != "$(jq -S . "$expected_desktop_json")" ]]; then
  echo "ERROR: desktop ReleaseManifest.json does not match export from release-manifest.json" >&2
  echo "Run scripts/export-version.sh to regenerate exports." >&2
  exit 1
fi

manifest_desktop="$(manifest_desktop_version "$MANIFEST")"
desktop_props_app="$(props_app_version "$DESKTOP_VERSION_PROPS")"

[[ "$desktop_props_app" == "$manifest_desktop" ]] || {
  echo "ERROR: Version.g.props AppVersion=$desktop_props_app != manifest desktop.version=$manifest_desktop" >&2
  exit 1
}

while IFS= read -r component_id; do
  component_id="${component_id//$'\r'/}"
  [[ -n "$component_id" ]] || continue
  agents_dir="$ROOT_DIR/$(manifest_agents_source_dir "$component_id")"
  agents_json="$agents_dir/ReleaseManifest.json"
  [[ -f "$agents_json" ]] || {
    echo "ERROR: missing $agents_json (run scripts/export-version.sh)" >&2
    exit 1
  }
  if [[ "$(jq -S . "$agents_json")" != "$(jq -S . "$expected_agents_json")" ]]; then
    echo "ERROR: $agents_json does not match export from release-manifest.json" >&2
    exit 1
  fi

  agents_props="$agents_dir/Version.g.props"
  [[ -f "$agents_props" ]] || {
    echo "ERROR: missing $agents_props (run scripts/export-version.sh)" >&2
    exit 1
  }
  agents_props_app="$(props_app_version "$agents_props")"
  agents_manifest_ver="$(manifest_agents_version "$MANIFEST")"
  [[ "$agents_props_app" == "$agents_manifest_ver" ]] || {
    echo "ERROR: $agents_props AppVersion=$agents_props_app != agents.version=$agents_manifest_ver" >&2
    exit 1
  }
done < <(manifest_agents_component_ids "$MANIFEST")

while IFS= read -r module_id; do
  module_id="${module_id//$'\r'/}"
  [[ -n "$module_id" ]] || continue
  module_dir="$ROOT_DIR/$(manifest_agents_module_source_dir "$module_id")"
  declared_version="$(manifest_agents_module_version "$MANIFEST" "$module_id")"
  declared_min_db="$(manifest_agents_module_min_db "$MANIFEST" "$module_id")"
  declared_max_db="$(manifest_agents_module_max_db "$MANIFEST" "$module_id")"
  module_meta="$module_dir/module.json"
  [[ -f "$module_meta" ]] || {
    echo "ERROR: missing $module_meta (run scripts/export-version.sh)" >&2
    exit 1
  }
  meta_version="$(jq_r '.version // empty' "$module_meta")"
  [[ "$meta_version" == "$declared_version" ]] || {
    echo "ERROR: $module_meta version=$meta_version != agents.modules.$module_id.version=$declared_version" >&2
    exit 1
  }
  meta_min_db="$(jq_r '.minDbSchema // empty' "$module_meta")"
  meta_max_db="$(jq_r '.maxDbSchema // empty' "$module_meta")"
  [[ "$meta_min_db" == "$declared_min_db" ]] || {
    echo "ERROR: $module_meta minDbSchema=$meta_min_db != agents.modules.$module_id.minDbSchema=$declared_min_db" >&2
    exit 1
  }
  [[ "$meta_max_db" == "$declared_max_db" ]] || {
    echo "ERROR: $module_meta maxDbSchema=$meta_max_db != agents.modules.$module_id.maxDbSchema=$declared_max_db" >&2
    exit 1
  }
done < <(manifest_agents_module_ids "$MANIFEST")

modules_root="$ROOT_DIR/runtime/agents/modules"
for module_meta in "$modules_root"/*/module.json; do
  [[ -f "$module_meta" ]] || continue
  module_id="$(jq_r '.id // empty' "$module_meta")"
  [[ -n "$module_id" ]] || {
    echo "ERROR: module.json missing id: $module_meta" >&2
    exit 1
  }
  if ! jq -e --arg id "$module_id" '.components.agents.modules[$id] != null' "$MANIFEST" > /dev/null; then
    echo "ERROR: source module id=$module_id is not listed in release-manifest.json ($module_meta)" >&2
    exit 1
  fi
done

API_DIR="$ROOT_DIR/apps/api-asp/src"
API_VERSION_PROPS="$API_DIR/Version.g.props"
API_CONTRACT_CS="$API_DIR/Hosting/ApiContract.g.cs"
API_BOUNDS_CS="$API_DIR/Hosting/SchemaBounds.g.cs"
API_APPSETTINGS="$API_DIR/appsettings.json"

[[ -f "$API_VERSION_PROPS" ]] || {
  echo "ERROR: missing $API_VERSION_PROPS (run scripts/export-version.sh)" >&2
  exit 1
}
[[ -f "$API_CONTRACT_CS" ]] || {
  echo "ERROR: missing $API_CONTRACT_CS (run scripts/export-version.sh)" >&2
  exit 1
}
[[ -f "$API_BOUNDS_CS" ]] || {
  echo "ERROR: missing $API_BOUNDS_CS (run scripts/export-version.sh)" >&2
  exit 1
}
[[ -f "$API_APPSETTINGS" ]] || {
  echo "ERROR: missing $API_APPSETTINGS" >&2
  exit 1
}

manifest_api="$(manifest_api_version "$MANIFEST")"
api_props_app="$(props_app_version "$API_VERSION_PROPS")"
[[ "$api_props_app" == "$manifest_api" ]] || {
  echo "ERROR: api Version.g.props AppVersion=$api_props_app != api.version=$manifest_api" >&2
  exit 1
}

manifest_contract="$(manifest_api_contract_version "$MANIFEST")"
contract_cs="$(grep -oE 'Version = "[^"]+"' "$API_CONTRACT_CS" | head -n1 | sed -E 's/Version = "([^"]+)"/\1/')"
[[ "$contract_cs" == "$manifest_contract" ]] || {
  echo "ERROR: ApiContract.g.cs Version=$contract_cs != api.contractVersion=$manifest_contract" >&2
  exit 1
}

manifest_api_min="$(manifest_api_min_db "$MANIFEST")"
manifest_api_max="$(manifest_api_max_db "$MANIFEST")"
bounds_min="$(grep -oE 'MinDbSchema = "[^"]+"' "$API_BOUNDS_CS" | head -n1 | sed -E 's/MinDbSchema = "([^"]+)"/\1/')"
bounds_max="$(grep -oE 'MaxDbSchema = "[^"]+"' "$API_BOUNDS_CS" | head -n1 | sed -E 's/MaxDbSchema = "([^"]+)"/\1/')"
[[ "$bounds_min" == "$manifest_api_min" && "$bounds_max" == "$manifest_api_max" ]] || {
  echo "ERROR: SchemaBounds.g.cs ($bounds_min-$bounds_max) != api min/maxDbSchema ($manifest_api_min-$manifest_api_max)" >&2
  exit 1
}

app_min="$(jq_r '.SchemaBounds.MinDbSchema // empty' "$API_APPSETTINGS")"
app_max="$(jq_r '.SchemaBounds.MaxDbSchema // empty' "$API_APPSETTINGS")"
[[ "$app_min" == "$manifest_api_min" && "$app_max" == "$manifest_api_max" ]] || {
  echo "ERROR: appsettings SchemaBounds ($app_min-$app_max) != api min/maxDbSchema ($manifest_api_min-$manifest_api_max)" >&2
  exit 1
}

echo "Version check passed."
echo "- product.version: $(manifest_product_version "$MANIFEST")"
echo "- desktop.avalonia.version: $manifest_desktop"
echo "- api.version: $manifest_api (contract $manifest_contract, db ${manifest_api_min}-${manifest_api_max})"
echo "- database.postgres.version: $(manifest_database_postgres_version "$MANIFEST")"
while IFS= read -r component_id; do
  component_id="${component_id//$'\r'/}"
  [[ -n "$component_id" ]] || continue
  version="$(jq_r --arg id "$component_id" '.components[$id].version' "$MANIFEST")"
  echo "- ${component_id}.version: $version"
done < <(manifest_agents_component_ids "$MANIFEST")
while IFS= read -r module_id; do
  module_id="${module_id//$'\r'/}"
  [[ -n "$module_id" ]] || continue
  module_min_db="$(manifest_agents_module_min_db "$MANIFEST" "$module_id")"
  module_max_db="$(manifest_agents_module_max_db "$MANIFEST" "$module_id")"
  if [[ -n "$module_min_db" || -n "$module_max_db" ]]; then
    echo "- agents.modules.${module_id}.version: $(manifest_agents_module_version "$MANIFEST" "$module_id") (db ${module_min_db}-${module_max_db})"
  else
    echo "- agents.modules.${module_id}.version: $(manifest_agents_module_version "$MANIFEST" "$module_id")"
  fi
done < <(manifest_agents_module_ids "$MANIFEST")
