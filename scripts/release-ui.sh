#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
UI_DIR="$ROOT_DIR/apps/desktop-avalonia/src"
MANIFEST="$ROOT_DIR/release-manifest.json"

usage() {
  cat <<'USAGE'
Usage:
  release-ui.sh [options]

Options:
  --bump-ui X.Y.Z            Optional: bump uiVersion before release.
  --bump-suite X.Y.Z         Optional: explicitly set suiteVersion (otherwise auto major/minor/patch by component changes when ui/agent/db bumped).
  --bump-agent X.Y.Z         Optional: bump agentVersion.
  --bump-db X.Y.Z            Optional: bump dbSchemaVersion.
  --bump-channel C           Optional: bump manifest build.channel (stable|beta).
  --pack-version X.Y.Z       Optional: vpk pack version (default: manifest suiteVersion).
  --channel C                Optional: vpk channel (default: manifest build.channel).
  --runtime RID              Runtime for publish/pack (default: win-arm64).
  --framework TFM            Target framework (default: net10.0).
  --configuration CFG        Build configuration (default: Release).
  --self-contained true|false   dotnet publish self-contained (default: false).
  --output-dir DIR           vpk output directory (default: UI project Releases directory).
  --pack-dir DIR             publish output directory for vpk (default: bin/<cfg>/<tfm>/<rid>/publish).
  --main-exe FILE            main exe for vpk (default: PacToolkits.Desktop.Avalonia.exe).
  --icon FILE                icon for setup package (default: Assets/app.ico).
  --vpk-directive NAME       optional vpk target directive (e.g. win).
  --upload-target TARGET     Optional rsync target, e.g. user@host:/path/feed/pactoolkits
  --no-delete                upload without rsync --delete.
  --skip-upload              do not upload.
  --dry-run                  print commands only.
  -h, --help                 show help.

Examples:
  ./scripts/release-ui.sh --bump-ui 0.4.2 \
    --runtime win-arm64 --vpk-directive win \
    --upload-target user@host:/var/www/updates/pactoolkits

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
FRAMEWORK="net10.0"
CONFIGURATION="Release"
SELF_CONTAINED="false"
OUTPUT_DIR="$UI_DIR/Releases"
PACK_DIR=""
MAIN_EXE="PacToolkits.Desktop.Avalonia.exe"
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
    stable|beta) ;;
    *) echo "ERROR: --channel must be stable|beta" >&2; exit 1 ;;
  esac
fi

if [[ -n "$BUMP_CHANNEL" ]]; then
  case "$BUMP_CHANNEL" in
    stable|beta) ;;
    *) echo "ERROR: --bump-channel must be stable|beta" >&2; exit 1 ;;
  esac
fi

if [[ -z "$VPK_DIRECTIVE" && "$(uname -s)" == "Darwin" && "$RUNTIME" == win-* ]]; then
  VPK_DIRECTIVE="win"
fi

if [[ -z "$PACK_DIR" ]]; then
  PACK_DIR="$UI_DIR/bin/$CONFIGURATION/$FRAMEWORK/$RUNTIME/publish"
fi

if [[ "$OUTPUT_DIR" != /* ]]; then
  OUTPUT_DIR="$ROOT_DIR/$OUTPUT_DIR"
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
manifest_suite="$(jq -r '.suiteVersion' "$MANIFEST")"
manifest_channel="$(jq -r '.build.channel' "$MANIFEST")"

if [[ -z "$PACK_VERSION" ]]; then
  PACK_VERSION="$manifest_suite"
fi
if [[ -z "$CHANNEL" ]]; then
  CHANNEL="$manifest_channel"
fi

if [[ "$PACK_VERSION" != "$manifest_suite" ]]; then
  echo "ERROR: --pack-version ($PACK_VERSION) != manifest suiteVersion ($manifest_suite)" >&2
  echo "Run bump-version first, or omit --pack-version to use manifest suiteVersion." >&2
  exit 1
fi

echo "Release plan:"
echo "- suiteVersion: $PACK_VERSION"
echo "- uiVersion: $manifest_ui"
echo "- channel: $CHANNEL"
echo "- runtime: $RUNTIME"
echo "- framework: $FRAMEWORK"
echo "- packDir: $PACK_DIR"
echo "- outputDir: $OUTPUT_DIR"

run_cmd dotnet publish "$UI_DIR/PacToolkits.Desktop.Avalonia.csproj" \
  -c "$CONFIGURATION" \
  -f "$FRAMEWORK" \
  -r "$RUNTIME" \
  --self-contained "$SELF_CONTAINED"

PACINJECTOR_SRC="$UI_DIR/Tools/pacinjector.exe"
PACINJECTOR_DST="$PACK_DIR/Tools/pacinjector.exe"
PACINJECTOR_MIN_BYTES=4096

if [[ "$DRY_RUN" == "true" ]]; then
  printf '[dry-run] mkdir -p %q\n' "$PACK_DIR/Tools"
  printf '[dry-run] cp -f %q %q\n' "$PACINJECTOR_SRC" "$PACINJECTOR_DST"
else
  [[ -f "$PACINJECTOR_SRC" ]] || {
    echo "ERROR: missing agent binary: $PACINJECTOR_SRC" >&2
    echo "Build agent first, e.g.: ./scripts/release-agent.sh --skip-upload --dry-run" >&2
    exit 1
  }
  mkdir -p "$PACK_DIR/Tools"
  cp -f "$PACINJECTOR_SRC" "$PACINJECTOR_DST"
  [[ -f "$PACINJECTOR_DST" ]] || { echo "ERROR: failed to copy pacinjector.exe to publish output" >&2; exit 1; }
  pacinjector_size="$(wc -c < "$PACINJECTOR_DST" | tr -d ' ')"
  if [[ "${pacinjector_size:-0}" -le "$PACINJECTOR_MIN_BYTES" ]]; then
    echo "ERROR: pacinjector.exe too small to be valid ($PACINJECTOR_DST, ${pacinjector_size} bytes)" >&2
    exit 1
  fi
fi

if [[ "$DRY_RUN" != "true" ]]; then
  [[ -d "$PACK_DIR" ]] || { echo "ERROR: pack dir not found: $PACK_DIR" >&2; exit 1; }
  [[ -f "$PACK_DIR/$MAIN_EXE" ]] || { echo "ERROR: main exe not found: $PACK_DIR/$MAIN_EXE" >&2; exit 1; }
  [[ -f "$ICON_FILE" ]] || { echo "ERROR: icon not found: $ICON_FILE" >&2; exit 1; }
  [[ -f "$PACINJECTOR_DST" ]] || { echo "ERROR: pacinjector.exe not found in publish output: $PACINJECTOR_DST" >&2; exit 1; }
  [[ -f "$PACK_DIR/Sql/Bootstrap/000_init_meta.sql" ]] || { echo "ERROR: bootstrap SQL not found in publish output" >&2; exit 1; }
  [[ -d "$PACK_DIR/Sql/Migrations" ]] || { echo "ERROR: migrations SQL directory not found in publish output" >&2; exit 1; }
  [[ -d "$PACK_DIR/Sql/Verify" ]] || { echo "ERROR: verify SQL directory not found in publish output" >&2; exit 1; }
fi

vpk_args=(vpk)
[[ -n "$VPK_DIRECTIVE" ]] && vpk_args+=("[$VPK_DIRECTIVE]")
vpk_args+=(pack
  --packId pactoolkits
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
  echo "No upload target provided. Package completed locally at: $OUTPUT_DIR"
  exit 0
fi

if [[ "$DRY_RUN" != "true" ]]; then
  require_cmd rsync
fi
upload_target="${UPLOAD_TARGET%/}/$CHANNEL/"
rsync_args=(rsync -avz)
[[ "$RSYNC_DELETE" == "true" ]] && rsync_args+=(--delete)
rsync_args+=("$OUTPUT_DIR/" "$upload_target")
run_cmd "${rsync_args[@]}"

echo "Release upload done: $upload_target"
