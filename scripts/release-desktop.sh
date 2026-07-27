#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
DESKTOP_PROJECT_DIR="$ROOT_DIR/apps/desktop-avalonia/src"
# shellcheck source=manifest-v2.sh
source "$ROOT_DIR/scripts/manifest-v2.sh"

MANIFEST="$ROOT_DIR/release-manifest.json"

usage() {
  cat << 'USAGE'
Usage:
  release-desktop.sh [options]

Options:
  --bump-desktop X.Y.Z       Optional: bump components.desktop.avalonia.version before release.
  --bump-product X.Y.Z       Optional: bump product.version.
  --bump-agents X.Y.Z         Optional: bump components.agents.version.
  --bump-db X.Y.Z            Optional: bump components.database.postgres.version.
  --bump-channel C           Optional: bump release.channel (stable|beta).
  --pack-version X.Y.Z       Optional: vpk pack version (default: manifest product.version).
  --channel C                Optional: vpk channel (default: manifest release.channel).
  --runtime RID              Runtime for publish/pack (default: win-arm64).
  --framework TFM            Target framework (default: net10.0).
  --configuration CFG        Build configuration (default: Release).
  --self-contained true|false   dotnet publish self-contained (default: false).
  --output-dir DIR           vpk output directory (default: desktop Releases directory).
  --pack-dir DIR             publish output directory for vpk (default: bin/<cfg>/<tfm>/<rid>/publish).
  --main-exe FILE            main exe for vpk (default: PacToolkits.Desktop.exe).
  --icon FILE                icon for setup package (default: Assets/app.ico).
  --vpk-directive NAME       optional vpk target directive (e.g. win).
  --upload-target TARGET     Optional rsync target, e.g. user@host:/path/feed/pactoolkits
  --no-delete                upload without rsync --delete.
  --skip-upload              do not upload.
  --dry-run                  print commands only.
  -h, --help                 show help.

Notes:
  - With --dry-run and a bump flag, release plan uses a preview manifest so
    product/desktop versions reflect the bumped values.

Examples:
  ./scripts/release-desktop.sh --bump-desktop 0.4.2 \
    --runtime win-arm64 --vpk-directive win \
    --upload-target user@host:/var/www/updates/pactoolkits

  ./scripts/release-desktop.sh --channel stable --runtime win-x64 --dry-run
USAGE
}

require_cmd() {
  command -v "$1" > /dev/null 2>&1 || {
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

BUMP_DESKTOP=""
BUMP_PRODUCT=""
BUMP_AGENTS=""
BUMP_DB=""
BUMP_CHANNEL=""
PACK_VERSION=""
CHANNEL=""
RUNTIME="win-arm64"
FRAMEWORK="net10.0"
CONFIGURATION="Release"
SELF_CONTAINED="false"
OUTPUT_DIR="$DESKTOP_PROJECT_DIR/Releases"
PACK_DIR=""
MAIN_EXE="PacToolkits.Desktop.exe"
ICON_FILE="$DESKTOP_PROJECT_DIR/Assets/app.ico"
VPK_DIRECTIVE=""
UPLOAD_TARGET=""
RSYNC_DELETE="true"
SKIP_UPLOAD="false"
DRY_RUN="false"
PLAN_MANIFEST_TMP=""

while [[ $# -gt 0 ]]; do
  case "$1" in
    --bump-desktop)
      BUMP_DESKTOP="${2:-}"
      shift 2
      ;;
    --bump-product)
      BUMP_PRODUCT="${2:-}"
      shift 2
      ;;
    --bump-agents)
      BUMP_AGENTS="${2:-}"
      shift 2
      ;;
    --bump-db)
      BUMP_DB="${2:-}"
      shift 2
      ;;
    --bump-channel)
      BUMP_CHANNEL="${2:-}"
      shift 2
      ;;
    --pack-version)
      PACK_VERSION="${2:-}"
      shift 2
      ;;
    --channel)
      CHANNEL="${2:-}"
      shift 2
      ;;
    --runtime)
      RUNTIME="${2:-}"
      shift 2
      ;;
    --framework)
      FRAMEWORK="${2:-}"
      shift 2
      ;;
    --configuration)
      CONFIGURATION="${2:-}"
      shift 2
      ;;
    --self-contained)
      SELF_CONTAINED="${2:-}"
      shift 2
      ;;
    --output-dir)
      OUTPUT_DIR="${2:-}"
      shift 2
      ;;
    --pack-dir)
      PACK_DIR="${2:-}"
      shift 2
      ;;
    --main-exe)
      MAIN_EXE="${2:-}"
      shift 2
      ;;
    --icon)
      ICON_FILE="${2:-}"
      shift 2
      ;;
    --vpk-directive)
      VPK_DIRECTIVE="${2:-}"
      shift 2
      ;;
    --upload-target)
      UPLOAD_TARGET="${2:-}"
      shift 2
      ;;
    --no-delete)
      RSYNC_DELETE="false"
      shift
      ;;
    --skip-upload)
      SKIP_UPLOAD="true"
      shift
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
require_cmd dotnet
if [[ "$DRY_RUN" != "true" ]]; then
  require_cmd vpk
fi

[[ -f "$MANIFEST" ]] || {
  echo "ERROR: missing $MANIFEST" >&2
  exit 1
}
[[ "$SELF_CONTAINED" == "true" || "$SELF_CONTAINED" == "false" ]] || {
  echo "ERROR: --self-contained must be true|false" >&2
  exit 1
}

[[ -z "$BUMP_DESKTOP" ]] || is_semver "$BUMP_DESKTOP" || {
  echo "ERROR: invalid --bump-desktop" >&2
  exit 1
}
[[ -z "$BUMP_PRODUCT" ]] || is_semver "$BUMP_PRODUCT" || {
  echo "ERROR: invalid --bump-product" >&2
  exit 1
}
[[ -z "$BUMP_AGENTS" ]] || is_semver "$BUMP_AGENTS" || {
  echo "ERROR: invalid --bump-agents" >&2
  exit 1
}
[[ -z "$BUMP_DB" ]] || is_semver "$BUMP_DB" || {
  echo "ERROR: invalid --bump-db" >&2
  exit 1
}
[[ -z "$PACK_VERSION" ]] || is_semver "$PACK_VERSION" || {
  echo "ERROR: invalid --pack-version" >&2
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

if [[ -n "$BUMP_CHANNEL" ]]; then
  case "$BUMP_CHANNEL" in
    stable | beta) ;;
    *)
      echo "ERROR: --bump-channel must be stable|beta" >&2
      exit 1
      ;;
  esac
fi

if [[ -z "$VPK_DIRECTIVE" && "$RUNTIME" == win-* ]]; then
  case "$(uname -s)" in
    Darwin | Linux) VPK_DIRECTIVE="win" ;;
  esac
fi

if [[ -z "$PACK_DIR" ]]; then
  PACK_DIR="$DESKTOP_PROJECT_DIR/bin/$CONFIGURATION/$FRAMEWORK/$RUNTIME/publish"
fi

if [[ "$OUTPUT_DIR" != /* ]]; then
  OUTPUT_DIR="$ROOT_DIR/$OUTPUT_DIR"
fi

cleanup_release_temp() {
  [[ -n "$PLAN_MANIFEST_TMP" ]] && rm -f "$PLAN_MANIFEST_TMP"
}
trap cleanup_release_temp EXIT

if [[ -n "$BUMP_DESKTOP$BUMP_PRODUCT$BUMP_AGENTS$BUMP_DB$BUMP_CHANNEL" ]]; then
  bump_args=("$ROOT_DIR/scripts/bump-version.sh")
  [[ -n "$BUMP_DESKTOP" ]] && bump_args+=(--desktop "$BUMP_DESKTOP")
  [[ -n "$BUMP_PRODUCT" ]] && bump_args+=(--product "$BUMP_PRODUCT")
  [[ -n "$BUMP_AGENTS" ]] && bump_args+=(--component "agents=$BUMP_AGENTS")
  [[ -n "$BUMP_DB" ]] && bump_args+=(--db "$BUMP_DB")
  [[ -n "$BUMP_CHANNEL" ]] && bump_args+=(--channel "$BUMP_CHANNEL")
  if [[ "$DRY_RUN" == "true" ]]; then
    PLAN_MANIFEST_TMP="$(mktemp)"
    bump_args+=(--output "$PLAN_MANIFEST_TMP" --dry-run)
    printf '[dry-run] '
    printf '%q ' "${bump_args[@]}"
    echo
    "${bump_args[@]}"
  else
    run_cmd "${bump_args[@]}"
  fi
fi

MANIFEST_FOR_PLAN="$MANIFEST"
if [[ -n "${PLAN_MANIFEST_TMP:-}" ]]; then
  MANIFEST_FOR_PLAN="$PLAN_MANIFEST_TMP"
fi

run_cmd "$ROOT_DIR/scripts/check-version.sh"

manifest_desktop="$(manifest_desktop_version "$MANIFEST_FOR_PLAN")"
manifest_product="$(manifest_product_version "$MANIFEST_FOR_PLAN")"
manifest_channel="$(manifest_release_channel "$MANIFEST_FOR_PLAN")"
manifest_pack_id="$(manifest_desktop_package_id "$MANIFEST_FOR_PLAN")"

if [[ -z "$PACK_VERSION" ]]; then
  PACK_VERSION="$manifest_product"
fi
if [[ -z "$CHANNEL" ]]; then
  CHANNEL="$manifest_channel"
fi

if [[ "$PACK_VERSION" != "$manifest_product" ]]; then
  echo "ERROR: --pack-version ($PACK_VERSION) != manifest product.version ($manifest_product)" >&2
  echo "Run bump-version first, or omit --pack-version to use manifest product.version." >&2
  exit 1
fi

echo "Release plan:"
echo "- product.version: $PACK_VERSION"
echo "- desktop.avalonia.version: $manifest_desktop"
echo "- packId: $manifest_pack_id"
echo "- channel: $CHANNEL"
echo "- runtime: $RUNTIME"
echo "- framework: $FRAMEWORK"
echo "- packDir: $PACK_DIR"
echo "- outputDir: $OUTPUT_DIR"

run_cmd dotnet publish "$DESKTOP_PROJECT_DIR/PacToolkits.Desktop.Avalonia.csproj" \
  -c "$CONFIGURATION" \
  -f "$FRAMEWORK" \
  -r "$RUNTIME" \
  --self-contained "$SELF_CONTAINED"

AGENT_SRC_DIR="${ARTIFACT_DIR:-$ROOT_DIR/artifacts/agents/win-x64}"
AGENT_HOST_SRC="$AGENT_SRC_DIR/Agents.exe"
AGENT_DST_DIR="$PACK_DIR/Agents"
AGENT_HOST_DST="$AGENT_DST_DIR/Agents.exe"
AGENT_MIN_BYTES=4096

if [[ "$DRY_RUN" == "true" ]]; then
  printf '[dry-run] mkdir -p %q\n' "$AGENT_DST_DIR/Modules"
  printf '[dry-run] cp -f %q %q\n' "$AGENT_HOST_SRC" "$AGENT_HOST_DST"
  printf '[dry-run] cp -f %q %q\n' "$AGENT_SRC_DIR/ReleaseManifest.json" "$AGENT_DST_DIR/ReleaseManifest.json"
  while IFS= read -r module_id; do
    [[ -n "$module_id" ]] || continue
    printf '[dry-run] cp -R %q/. %q/\n' \
      "$AGENT_SRC_DIR/Modules/$module_id" \
      "$AGENT_DST_DIR/Modules/$module_id"
  done < <(manifest_agents_module_ids "$MANIFEST_FOR_PLAN")
else
  validate_agents_staging_layout "$AGENT_SRC_DIR" "$MANIFEST_FOR_PLAN" "$AGENT_MIN_BYTES" || {
    echo "Build agent first, e.g.: ./scripts/release-agents.sh --artifact-dir ... --skip-upload" >&2
    exit 1
  }

  case "$AGENT_DST_DIR" in
    "$PACK_DIR/Agents") ;;
    *)
      echo "ERROR: refusing to replace unexpected Agents destination: $AGENT_DST_DIR" >&2
      exit 1
      ;;
  esac
  rm -rf "$AGENT_DST_DIR"
  mkdir -p "$AGENT_DST_DIR/Modules"
  cp -f "$AGENT_HOST_SRC" "$AGENT_HOST_DST"
  cp -f "$AGENT_SRC_DIR/ReleaseManifest.json" "$AGENT_DST_DIR/ReleaseManifest.json"

  while IFS= read -r module_id; do
    [[ -n "$module_id" ]] || continue
    module_src="$AGENT_SRC_DIR/Modules/$module_id"
    module_dst="$AGENT_DST_DIR/Modules/$module_id"
    mkdir -p "$module_dst"
    cp -R "$module_src/." "$module_dst/"
  done < <(manifest_agents_module_ids "$MANIFEST_FOR_PLAN")

  [[ -f "$AGENT_HOST_DST" ]] || {
    echo "ERROR: failed to copy Host binary to publish output" >&2
    exit 1
  }
  agent_size="$(wc -c < "$AGENT_HOST_DST" | tr -d ' ')"
  if [[ "${agent_size:-0}" -le "$AGENT_MIN_BYTES" ]]; then
    echo "ERROR: Host binary too small to be valid ($AGENT_HOST_DST, ${agent_size} bytes)" >&2
    exit 1
  fi
  # 拷贝后复验：防 cp 漏文件；SRC 已在上方 validate 过
  validate_agents_staging_layout "$AGENT_DST_DIR" "$MANIFEST_FOR_PLAN" "$AGENT_MIN_BYTES" || exit 1
fi

if [[ "$DRY_RUN" != "true" ]]; then
  [[ -d "$PACK_DIR" ]] || {
    echo "ERROR: pack dir not found: $PACK_DIR" >&2
    exit 1
  }
  [[ -f "$PACK_DIR/$MAIN_EXE" ]] || {
    echo "ERROR: main exe not found: $PACK_DIR/$MAIN_EXE" >&2
    exit 1
  }
  [[ -f "$ICON_FILE" ]] || {
    echo "ERROR: icon not found: $ICON_FILE" >&2
    exit 1
  }
  [[ -f "$AGENT_HOST_DST" ]] || {
    echo "ERROR: Host binary not found in publish output: $AGENT_HOST_DST" >&2
    exit 1
  }
fi

vpk_args=(vpk)
[[ -n "$VPK_DIRECTIVE" ]] && vpk_args+=("[$VPK_DIRECTIVE]")
vpk_args+=(pack
  --packId "$manifest_pack_id"
  --packVersion "$PACK_VERSION"
  --packDir "$PACK_DIR"
  --outputDir "$OUTPUT_DIR"
  --mainExe "$MAIN_EXE"
  --runtime "$RUNTIME"
  --channel "$CHANNEL"
  --packTitle PacToolkits
  --packAuthors PacmanDoh
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
