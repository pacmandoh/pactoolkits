#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
# shellcheck source=manifest-v2.sh
source "$ROOT_DIR/scripts/manifest-v2.sh"

MANIFEST="$ROOT_DIR/release-manifest.json"
DESKTOP_AVALONIA_DIR="$ROOT_DIR/apps/desktop-avalonia/src"

require_cmd() {
  command -v "$1" > /dev/null 2>&1 || {
    echo "ERROR: required command not found: $1" >&2
    exit 1
  }
}

require_cmd jq

[[ -f "$MANIFEST" ]] || {
  echo "ERROR: manifest not found: $MANIFEST" >&2
  exit 1
}

validate_manifest_v2 "$MANIFEST"

desktop_version="$(manifest_desktop_version "$MANIFEST")"
assembly_version="$(semver_stable_base "$desktop_version")"
build_channel="$(manifest_release_channel "$MANIFEST")"
build_date="$(manifest_release_date "$MANIFEST")"

mkdir -p "$DESKTOP_AVALONIA_DIR"

cat > "$DESKTOP_AVALONIA_DIR/Version.g.props" << XML
<Project>
  <PropertyGroup>
    <AppVersion>$desktop_version</AppVersion>
    <Version>$desktop_version</Version>
    <AssemblyVersion>${assembly_version}.0</AssemblyVersion>
    <FileVersion>${assembly_version}.0</FileVersion>
    <InformationalVersion>${desktop_version}+${build_channel}.${build_date}</InformationalVersion>
  </PropertyGroup>
</Project>
XML

manifest_snapshot="$DESKTOP_AVALONIA_DIR/ReleaseManifest.json"
jq -S . "$MANIFEST" > "$manifest_snapshot"

agents_version="$(manifest_agents_version "$MANIFEST")"
agents_assembly_version="$(semver_stable_base "$agents_version")"

while IFS= read -r component_id; do
  component_id="${component_id//$'\r'/}"
  [[ -n "$component_id" ]] || continue
  agents_dir="$ROOT_DIR/$(manifest_agents_source_dir "$component_id")"
  mkdir -p "$agents_dir"
  cp "$manifest_snapshot" "$agents_dir/ReleaseManifest.json"

  cat > "$agents_dir/Version.g.props" << XML
<Project>
  <PropertyGroup>
    <AppVersion>$agents_version</AppVersion>
    <Version>$agents_version</Version>
    <AssemblyVersion>${agents_assembly_version}.0</AssemblyVersion>
    <FileVersion>${agents_assembly_version}.0</FileVersion>
    <InformationalVersion>${agents_version}+${build_channel}.${build_date}</InformationalVersion>
  </PropertyGroup>
</Project>
XML
done < <(manifest_agents_component_ids "$MANIFEST")

while IFS= read -r module_id; do
  module_id="${module_id//$'\r'/}"
  [[ -n "$module_id" ]] || continue
  module_dir="$ROOT_DIR/$(manifest_agents_module_source_dir "$module_id")"
  mkdir -p "$module_dir"
  cp "$manifest_snapshot" "$module_dir/ReleaseManifest.json"

  module_json="$module_dir/module.json"
  module_version="$(manifest_agents_module_version "$MANIFEST" "$module_id")"
  [[ -n "$module_version" ]] || {
    echo "ERROR: missing version for agents.modules.$module_id" >&2
    exit 1
  }
  if [[ -f "$module_json" ]]; then
    tmp="$(mktemp)"
    jq --arg id "$module_id" --arg ver "$module_version" \
      '.id = $id | .version = $ver' "$module_json" > "$tmp"
    mv "$tmp" "$module_json"
  else
    echo "ERROR: missing module.json for $module_id at $module_json" >&2
    exit 1
  fi
done < <(manifest_agents_module_ids "$MANIFEST")

echo "Exported version artifacts:"
echo "- $DESKTOP_AVALONIA_DIR/Version.g.props"
echo "- $manifest_snapshot"
while IFS= read -r component_id; do
  component_id="${component_id//$'\r'/}"
  [[ -n "$component_id" ]] || continue
  echo "- $ROOT_DIR/$(manifest_agents_source_dir "$component_id")/ReleaseManifest.json"
  echo "- $ROOT_DIR/$(manifest_agents_source_dir "$component_id")/Version.g.props"
done < <(manifest_agents_component_ids "$MANIFEST")
while IFS= read -r module_id; do
  module_id="${module_id//$'\r'/}"
  [[ -n "$module_id" ]] || continue
  echo "- $ROOT_DIR/$(manifest_agents_module_source_dir "$module_id")/ReleaseManifest.json"
  echo "- $ROOT_DIR/$(manifest_agents_module_source_dir "$module_id")/module.json (version synced)"
done < <(manifest_agents_module_ids "$MANIFEST")
