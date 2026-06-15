#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
# shellcheck source=manifest-v2.sh
source "$ROOT_DIR/scripts/manifest-v2.sh"

MANIFEST="$ROOT_DIR/release-manifest.json"
UI_DIR="$ROOT_DIR/apps/desktop-avalonia/src"

require_cmd() {
  command -v "$1" >/dev/null 2>&1 || {
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

mkdir -p "$UI_DIR"

cat > "$UI_DIR/Version.g.props" <<XML
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

manifest_snapshot="$UI_DIR/version.generated.json"
jq -S . "$MANIFEST" > "$manifest_snapshot"

while IFS= read -r component_id; do
  [[ -n "$component_id" ]] || continue
  agent_dir="$ROOT_DIR/$(manifest_agent_source_dir "$component_id")"
  mkdir -p "$agent_dir"
  cp "$manifest_snapshot" "$agent_dir/version.generated.json"
done < <(manifest_agent_component_ids "$MANIFEST")

echo "Exported version artifacts:"
echo "- $UI_DIR/Version.g.props"
echo "- $manifest_snapshot"
while IFS= read -r component_id; do
  [[ -n "$component_id" ]] || continue
  echo "- $ROOT_DIR/$(manifest_agent_source_dir "$component_id")/version.generated.json"
done < <(manifest_agent_component_ids "$MANIFEST")
