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

manifest_canonical="$(jq -S . "$MANIFEST")"
export_canonical="$(jq -S . "$DESKTOP_VERSION_JSON")"

if [[ "$manifest_canonical" != "$export_canonical" ]]; then
  echo "ERROR: ReleaseManifest.json does not match release-manifest.json" >&2
  echo "Run scripts/export-version.sh to regenerate exports." >&2
  exit 1
fi

manifest_desktop="$(manifest_desktop_version "$MANIFEST")"

if command -v rg > /dev/null 2>&1; then
  desktop_props_app="$(rg -o "<AppVersion>[^<]+</AppVersion>" "$DESKTOP_VERSION_PROPS" | sed -E 's#<AppVersion>([^<]+)</AppVersion>#\1#')"
else
  desktop_props_app="$(grep -oE "<AppVersion>[^<]+</AppVersion>" "$DESKTOP_VERSION_PROPS" | sed -E 's#<AppVersion>([^<]+)</AppVersion>#\1#')"
fi

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
  agents_canonical="$(jq -S . "$agents_json")"
  if [[ "$manifest_canonical" != "$agents_canonical" ]]; then
    echo "ERROR: $agents_json does not match release-manifest.json" >&2
    exit 1
  fi

  agents_props="$agents_dir/Version.g.props"
  [[ -f "$agents_props" ]] || {
    echo "ERROR: missing $agents_props (run scripts/export-version.sh)" >&2
    exit 1
  }
  if command -v rg > /dev/null 2>&1; then
    agents_props_app="$(rg -o "<AppVersion>[^<]+</AppVersion>" "$agents_props" | sed -E 's#<AppVersion>([^<]+)</AppVersion>#\1#')"
  else
    agents_props_app="$(grep -oE "<AppVersion>[^<]+</AppVersion>" "$agents_props" | sed -E 's#<AppVersion>([^<]+)</AppVersion>#\1#')"
  fi
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
  module_json="$module_dir/ReleaseManifest.json"
  [[ -f "$module_json" ]] || {
    echo "ERROR: missing $module_json (run scripts/export-version.sh)" >&2
    exit 1
  }
  module_canonical="$(jq -S . "$module_json")"
  if [[ "$manifest_canonical" != "$module_canonical" ]]; then
    echo "ERROR: $module_json does not match release-manifest.json" >&2
    exit 1
  fi

  declared_version="$(manifest_agents_module_version "$MANIFEST" "$module_id")"
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

echo "Version check passed."
echo "- product.version: $(manifest_product_version "$MANIFEST")"
echo "- desktop.avalonia.version: $manifest_desktop"
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
  echo "- agents.modules.${module_id}.version: $(manifest_agents_module_version "$MANIFEST" "$module_id")"
done < <(manifest_agents_module_ids "$MANIFEST")
