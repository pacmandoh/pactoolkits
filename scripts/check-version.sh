#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
# shellcheck source=manifest-v2.sh
source "$ROOT_DIR/scripts/manifest-v2.sh"

MANIFEST="$ROOT_DIR/release-manifest.json"
DESKTOP_VERSION_PROPS="$ROOT_DIR/apps/desktop-avalonia/src/Version.g.props"
DESKTOP_VERSION_JSON="$ROOT_DIR/apps/desktop-avalonia/src/version.generated.json"

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
  echo "ERROR: version.generated.json does not match release-manifest.json" >&2
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
  [[ -n "$component_id" ]] || continue
  agent_json="$ROOT_DIR/$(manifest_agent_source_dir "$component_id")/version.generated.json"
  [[ -f "$agent_json" ]] || {
    echo "ERROR: missing $agent_json (run scripts/export-version.sh)" >&2
    exit 1
  }
  agent_canonical="$(jq -S . "$agent_json")"
  if [[ "$manifest_canonical" != "$agent_canonical" ]]; then
    echo "ERROR: $agent_json does not match release-manifest.json" >&2
    exit 1
  fi
done < <(manifest_agent_component_ids "$MANIFEST")

echo "Version check passed."
echo "- product.version: $(manifest_product_version "$MANIFEST")"
echo "- desktop.version: $manifest_desktop"
echo "- desktop.implementation: $(manifest_desktop_implementation "$MANIFEST")"
echo "- database-postgres.version: $(manifest_database_postgres_version "$MANIFEST")"
while IFS= read -r component_id; do
  [[ -n "$component_id" ]] || continue
  version="$(jq -r --arg id "$component_id" '.components[$id].version' "$MANIFEST")"
  echo "- ${component_id}.version: $version"
done < <(manifest_agent_component_ids "$MANIFEST")
