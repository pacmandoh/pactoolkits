#!/usr/bin/env bash
set -euo pipefail

# Resolve formal desktop release parameters from release-manifest.json (schema v2).
# Usage:
#   resolve-release-plan.sh [manifest-path]
#   resolve-release-plan.sh --github-output [manifest-path]

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
# shellcheck source=manifest-v2.sh
source "$ROOT_DIR/scripts/manifest-v2.sh"

GITHUB_OUTPUT_MODE="false"
MANIFEST="$ROOT_DIR/release-manifest.json"

while [[ $# -gt 0 ]]; do
  case "$1" in
    --github-output)
      GITHUB_OUTPUT_MODE="true"
      shift
      ;;
    -h|--help)
      cat <<'USAGE'
Usage:
  resolve-release-plan.sh [--github-output] [manifest-path]

Prints shell assignments or GitHub Actions output pairs for the formal desktop release plan.
USAGE
      exit 0
      ;;
    *)
      MANIFEST="$1"
      shift
      ;;
  esac
done

[[ -f "$MANIFEST" ]] || {
  echo "ERROR: manifest not found: $MANIFEST" >&2
  exit 1
}

validate_manifest_v2 "$MANIFEST"

release_tag="${RELEASE_TAG:-}"
if [[ -z "$release_tag" && "${GITHUB_REF:-}" == refs/tags/v* ]]; then
  release_tag="${GITHUB_REF_NAME:-}"
fi
validate_release_tag_matches_product_version "$release_tag" "$MANIFEST" || exit 1

implementation="$(manifest_desktop_implementation "$MANIFEST")"
desktop_version="$(manifest_desktop_version "$MANIFEST")"
product_version="$(manifest_product_version "$MANIFEST")"
pack_id="$(jq -r '.components.desktop.packageId' "$MANIFEST")"
channel="$(manifest_release_channel "$MANIFEST")"

case "$implementation" in
  avalonia)
    desktop_artifact_name="pactoolkits-desktop-avalonia-win-x64-${desktop_version}"
    main_exe="pactoolkits-desktop.exe"
    icon_path="apps/desktop-avalonia/src/Assets/app.ico"
    releases_dir="apps/desktop-avalonia/src/Releases"
    publish_subdir="apps/desktop-avalonia/src/bin/Release/net10.0/win-x64/publish"
    ;;
  electron)
    desktop_artifact_name="pactoolkits-desktop-electron-win-x64-${desktop_version}"
    main_exe="pactoolkits-desktop.exe"
    icon_path="apps/desktop-electron/build/icon.ico"
  if [[ ! -f "$ROOT_DIR/$icon_path" ]]; then
    icon_path="apps/desktop-avalonia/src/Assets/app.ico"
  fi
    releases_dir="apps/desktop-electron/Releases"
    publish_subdir="apps/desktop-electron/dist/win-x64"
    ;;
  *)
    echo "ERROR: unsupported components.desktop.implementation: $implementation" >&2
    exit 1
    ;;
esac

emit() {
  local key="$1"
  local value="$2"
  if [[ "$GITHUB_OUTPUT_MODE" == "true" ]]; then
    {
      printf '%s<<EOF\n' "$key"
      printf '%s\n' "$value"
      printf 'EOF\n'
    } >> "${GITHUB_OUTPUT:?GITHUB_OUTPUT is required with --github-output}"
  else
    printf '%s=%q\n' "$key" "$value"
  fi
}

emit implementation "$implementation"
emit desktop_version "$desktop_version"
emit product_version "$product_version"
emit pack_id "$pack_id"
emit channel "$channel"
emit desktop_artifact_name "$desktop_artifact_name"
emit main_exe "$main_exe"
emit icon_path "$icon_path"
emit releases_dir "$releases_dir"
emit publish_subdir "$publish_subdir"
