#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
UI_DIR="$ROOT_DIR/pactoolkits-ui"
MANIFEST="$ROOT_DIR/release-manifest.json"

usage() {
  cat <<'USAGE'
Usage:
  release-ui.sh [options]

Options:
  --bump-ui X.Y.Z            Optional: bump uiVersion before release.
  --bump-suite X.Y.Z         Optional: bump suiteVersion with UI.
  --bump-agent X.Y.Z         Optional: bump agentVersion.
  --bump-db X.Y.Z            Optional: bump dbSchemaVersion.
  --bump-channel C           Optional: bump manifest build.channel (stable|beta|dev).
  --pack-version X.Y.Z       Optional: vpk pack version (default: manifest uiVersion).
  --channel C                Optional: vpk channel (default: manifest build.channel).
  --runtime RID              Runtime for publish/pack (default: win-arm64).
  --framework TFM            Target framework (default: net8.0).
  --configuration CFG        Build configuration (default: Release).
  --self-contained true|false   dotnet publish self-contained (default: false).
  --output-dir DIR           vpk output directory (default: ./Releases).
  --pack-dir DIR             publish output directory for vpk (default: bin/<cfg>/<tfm>/<rid>/publish).
  --main-exe FILE            main exe for vpk (default: pactoolkits-ui.exe).
  --icon FILE                icon for setup package (default: Assets/app.ico).
  --vpk-directive NAME       optional vpk target directive (e.g. win).
  --upload-target TARGET     Optional rsync target, e.g. user@host:/path/feed/pactoolkits-ui/
  --no-delete                upload without rsync --delete.
  --skip-upload              do not upload.
  --dry-run                  print commands only.
  -h, --help                 show help.

Examples:
  ./scripts/release-ui.sh --bump-ui 0.4.2 --bump-suite 0.4.2 \
    --runtime win-arm64 --vpk-directive win \
    --upload-target user@host:/var/www/updates/pactoolkits-ui/

  ./scripts/release-ui.sh --channel stable --runtime win-x64 --dry-run
USAGE
}

require_cmd() {
  command -v "$1" >/dev/null 2>&1 || {
    echo "ERROR: required command not found: $1" >&2
    exit 1
  }
}

is_semver() {
  [[ "$1" =~ ^[0-9]+\.[0-9]+\.[0-9]+$ ]]
}

run_cmd() {
  if [[ "$DRY_RUN" == "true" ]]; then
    printf '[dry-run] '
    printf '%q ' "$@"
    echo
  else
    "$@"
  fi
}

BUMP_UI=""
BUMP_SUITE=""
BUMP_AGENT=""
BUMP_DB=""
BUMP_CHANNEL=""
PACK_VERSION=""
CHANNEL=""
RUNTIME="win-arm64"
FRAMEWORK="net8.0"
CONFIGURATION="Release"
SELF_CONTAINED="false"
OUTPUT_DIR="./Releases"
PACK_DIR=""
MAIN_EXE="pactoolkits-ui.exe"
ICON_FILE="$UI_DIR/Assets/app.ico"
VPK_DIRECTIVE=""
UPLOAD_TARGET=""
RSYNC_DELETE="true"
SKIP_UPLOAD="false"
DRY_RUN="false"

while [[ $# -gt 0 ]]; do
  case "$1" in
    --bump-ui) BUMP_UI="${2:-}"; shift 2 ;;
    --bump-suite) BUMP_SUITE="${2:-}"; shift 2 ;;
    --bump-agent) BUMP_AGENT="${2:-}"; shift 2 ;;
    --bump-db) BUMP_DB="${2:-}"; shift 2 ;;
    --bump-channel) BUMP_CHANNEL="${2:-}"; shift 2 ;;
    --pack-version) PACK_VERSION="${2:-}"; shift 2 ;;
    --channel) CHANNEL="${2:-}"; shift 2 ;;
    --runtime) RUNTIME="${2:-}"; shift 2 ;;
    --framework) FRAMEWORK="${2:-}"; shift 2 ;;
    --configuration) CONFIGURATION="${2:-}"; shift 2 ;;
    --self-contained) SELF_CONTAINED="${2:-}"; shift 2 ;;
    --output-dir) OUTPUT_DIR="${2:-}"; shift 2 ;;
    --pack-dir) PACK_DIR="${2:-}"; shift 2 ;;
    --main-exe) MAIN_EXE="${2:-}"; shift 2 ;;
    --icon) ICON_FILE="${2:-}"; shift 2 ;;
    --vpk-directive) VPK_DIRECTIVE="${2:-}"; shift 2 ;;
    --upload-target) UPLOAD_TARGET="${2:-}"; shift 2 ;;
    --no-delete) RSYNC_DELETE="false"; shift ;;
    --skip-upload) SKIP_UPLOAD="true"; shift ;;
    --dry-run) DRY_RUN="true"; shift ;;
    -h|--help) usage; exit 0 ;;
    *) echo "ERROR: unknown arg: $1" >&2; usage; exit 1 ;;
  esac
done

require_cmd jq
require_cmd dotnet
if [[ "$DRY_RUN" != "true" ]]; then
  require_cmd vpk
fi

[[ -f "$MANIFEST" ]] || { echo "ERROR: missing $MANIFEST" >&2; exit 1; }
[[ "$SELF_CONTAINED" == "true" || "$SELF_CONTAINED" == "false" ]] || {
  echo "ERROR: --self-contained must be true|false" >&2
  exit 1
}

[[ -z "$BUMP_UI" ]] || is_semver "$BUMP_UI" || { echo "ERROR: invalid --bump-ui" >&2; exit 1; }
[[ -z "$BUMP_SUITE" ]] || is_semver "$BUMP_SUITE" || { echo "ERROR: invalid --bump-suite" >&2; exit 1; }
[[ -z "$BUMP_AGENT" ]] || is_semver "$BUMP_AGENT" || { echo "ERROR: invalid --bump-agent" >&2; exit 1; }
[[ -z "$BUMP_DB" ]] || is_semver "$BUMP_DB" || { echo "ERROR: invalid --bump-db" >&2; exit 1; }
[[ -z "$PACK_VERSION" ]] || is_semver "$PACK_VERSION" || { echo "ERROR: invalid --pack-version" >&2; exit 1; }

if [[ -n "$CHANNEL" ]]; then
  case "$CHANNEL" in
    stable|beta|dev) ;;
    *) echo "ERROR: --channel must be stable|beta|dev" >&2; exit 1 ;;
  esac
fi

if [[ -n "$BUMP_CHANNEL" ]]; then
  case "$BUMP_CHANNEL" in
    stable|beta|dev) ;;
    *) echo "ERROR: --bump-channel must be stable|beta|dev" >&2; exit 1 ;;
  esac
fi

if [[ -z "$VPK_DIRECTIVE" && "$(uname -s)" == "Darwin" && "$RUNTIME" == win-* ]]; then
  VPK_DIRECTIVE="win"
fi

if [[ -z "$PACK_DIR" ]]; then
  PACK_DIR="$UI_DIR/bin/$CONFIGURATION/$FRAMEWORK/$RUNTIME/publish"
fi

if [[ -n "$BUMP_UI$BUMP_SUITE$BUMP_AGENT$BUMP_DB$BUMP_CHANNEL" ]]; then
  bump_args=("$ROOT_DIR/scripts/bump-version.sh")
  [[ -n "$BUMP_UI" ]] && bump_args+=(--ui "$BUMP_UI")
  [[ -n "$BUMP_SUITE" ]] && bump_args+=(--suite "$BUMP_SUITE")
  [[ -n "$BUMP_AGENT" ]] && bump_args+=(--agent "$BUMP_AGENT")
  [[ -n "$BUMP_DB" ]] && bump_args+=(--db "$BUMP_DB")
  [[ -n "$BUMP_CHANNEL" ]] && bump_args+=(--channel "$BUMP_CHANNEL")
  run_cmd "${bump_args[@]}"
fi

run_cmd "$ROOT_DIR/scripts/check-version.sh"

manifest_ui="$(jq -r '.uiVersion' "$MANIFEST")"
manifest_channel="$(jq -r '.build.channel' "$MANIFEST")"

if [[ -z "$PACK_VERSION" ]]; then
  PACK_VERSION="$manifest_ui"
fi
if [[ -z "$CHANNEL" ]]; then
  CHANNEL="$manifest_channel"
fi

if [[ "$PACK_VERSION" != "$manifest_ui" ]]; then
  echo "ERROR: --pack-version ($PACK_VERSION) != manifest uiVersion ($manifest_ui)" >&2
  echo "Run bump-version first, or omit --pack-version to use manifest uiVersion." >&2
  exit 1
fi

echo "Release plan:"
echo "- uiVersion: $PACK_VERSION"
echo "- channel: $CHANNEL"
echo "- runtime: $RUNTIME"
echo "- framework: $FRAMEWORK"
echo "- packDir: $PACK_DIR"
echo "- outputDir: $OUTPUT_DIR"

run_cmd dotnet publish "$UI_DIR/pactoolkits-ui.csproj" \
  -c "$CONFIGURATION" \
  -f "$FRAMEWORK" \
  -r "$RUNTIME" \
  --self-contained "$SELF_CONTAINED"

if [[ "$DRY_RUN" != "true" ]]; then
  [[ -d "$PACK_DIR" ]] || { echo "ERROR: pack dir not found: $PACK_DIR" >&2; exit 1; }
  [[ -f "$PACK_DIR/$MAIN_EXE" ]] || { echo "ERROR: main exe not found: $PACK_DIR/$MAIN_EXE" >&2; exit 1; }
  [[ -f "$ICON_FILE" ]] || { echo "ERROR: icon not found: $ICON_FILE" >&2; exit 1; }
fi

vpk_args=(vpk)
[[ -n "$VPK_DIRECTIVE" ]] && vpk_args+=("[$VPK_DIRECTIVE]")
vpk_args+=(pack
  --packId pactoolkits-ui
  --packVersion "$PACK_VERSION"
  --packDir "$PACK_DIR"
  --outputDir "$OUTPUT_DIR"
  --mainExe "$MAIN_EXE"
  --runtime "$RUNTIME"
  --channel "$CHANNEL"
  --noPortable
  -i "$ICON_FILE")
run_cmd "${vpk_args[@]}"

if [[ "$SKIP_UPLOAD" == "true" ]]; then
  echo "Skip upload."
  exit 0
fi

if [[ -z "$UPLOAD_TARGET" ]]; then
  echo "No upload target provided. Package completed locally at: $UI_DIR/$OUTPUT_DIR"
  exit 0
fi

if [[ "$DRY_RUN" != "true" ]]; then
  require_cmd rsync
fi
rsync_args=(rsync -avz)
[[ "$RSYNC_DELETE" == "true" ]] && rsync_args+=(--delete)
rsync_args+=("$UI_DIR/$OUTPUT_DIR/" "$UPLOAD_TARGET")
run_cmd "${rsync_args[@]}"

echo "Release upload done: $UPLOAD_TARGET"
