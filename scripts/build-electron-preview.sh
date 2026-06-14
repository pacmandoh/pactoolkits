#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
# shellcheck source=manifest-v2.sh
source "$ROOT_DIR/scripts/manifest-v2.sh"

require_cmd() {
  command -v "$1" >/dev/null 2>&1 || {
    echo "ERROR: required command not found: $1" >&2
    exit 1
  }
}

require_cmd jq

validate_manifest_v2 "$ROOT_DIR/release-manifest.json"
"$ROOT_DIR/scripts/export-version.sh"

product_version="$(manifest_product_version "$ROOT_DIR/release-manifest.json")"
desktop_version="$(manifest_desktop_version "$ROOT_DIR/release-manifest.json")"
implementation="$(manifest_desktop_implementation "$ROOT_DIR/release-manifest.json")"
channel="$(manifest_release_channel "$ROOT_DIR/release-manifest.json")"

out="$ROOT_DIR/artifacts/desktop/electron-preview/win-x64"
stage="$out/pactoolkits-desktop-electron-preview-win-x64-${product_version}"
rm -rf "$stage"
mkdir -p "$stage/app" "$stage/electron/main" "$stage/electron/preload"

cp "$ROOT_DIR/apps/desktop-electron/package.json" "$stage/package.json"
cp "$ROOT_DIR/apps/desktop-electron/app/index.html" "$stage/app/index.html"
cp "$ROOT_DIR/apps/desktop-electron/electron/main/index.js" "$stage/electron/main/index.js"
cp "$ROOT_DIR/apps/desktop-electron/electron/preload/index.js" "$stage/electron/preload/index.js"
cp "$ROOT_DIR/apps/desktop-electron/README.md" "$stage/README.md"
cp "$ROOT_DIR/apps/desktop-avalonia/src/version.generated.json" "$stage/version.generated.json"

jq -n \
  --arg product "$product_version" \
  --arg desktop "$desktop_version" \
  --arg implementation "$implementation" \
  --arg channel "$channel" \
  --arg kind "electron-preview" \
  '{
    kind: $kind,
    productVersion: $product,
    desktopVersion: $desktop,
    implementation: $implementation,
    releaseChannel: $channel,
    entry: "electron/main/index.js",
    note: "Preview bundle only; not published to formal feed"
  }' > "$stage/preview-manifest.json"

(
  cd "$out"
  rm -f "pactoolkits-desktop-electron-preview-win-x64-${product_version}.zip"
  zip -qr "pactoolkits-desktop-electron-preview-win-x64-${product_version}.zip" \
    "pactoolkits-desktop-electron-preview-win-x64-${product_version}"
)

echo "Electron preview bundle ready:"
echo "- $stage"
echo "- $out/pactoolkits-desktop-electron-preview-win-x64-${product_version}.zip"
