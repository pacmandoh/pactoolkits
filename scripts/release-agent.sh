#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
AGENT_DIR="$ROOT_DIR/pactoolkits-agent"
MANIFEST="$ROOT_DIR/release-manifest.json"
RELEASES_DIR="$AGENT_DIR/Releases"

usage() {
  cat <<'USAGE'
Usage:
  release-agent.sh [options]

Options:
  --bump-agent X.Y.Z         Optional: bump agentVersion before packaging.
  --bump-suite X.Y.Z         Optional: explicitly set suiteVersion (otherwise auto major/minor/patch by component changes when ui/agent/db bumped).
  --bump-ui X.Y.Z            Optional: bump uiVersion.
  --bump-db X.Y.Z            Optional: bump dbSchemaVersion.
  --bump-channel C           Optional: bump manifest build.channel (stable|beta).
  --artifact-dir DIR         Optional: package prebuilt files from DIR instead of source files.
  --channel C                Optional: package channel tag (default: manifest build.channel).
  --output-dir DIR           Output directory (default: pactoolkits-agent/Releases).
  --upload-target TARGET     Optional rsync target, e.g. user@host:/var/www/updates/pactoolkits-agent/
  --dry-run                  Print commands only.
  --skip-upload              Do not upload.
  -h, --help                 Show help.

Notes:
  - This script does not compile AHK on macOS. It packages either:
    1) source runtime bundle (main.ahk + src + assets + version file), or
    2) files from --artifact-dir (recommended if built on Windows).
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

BUMP_AGENT=""
BUMP_SUITE=""
BUMP_UI=""
BUMP_DB=""
BUMP_CHANNEL=""
ARTIFACT_DIR=""
CHANNEL=""
OUTPUT_DIR="$RELEASES_DIR"
UPLOAD_TARGET=""
SKIP_UPLOAD="false"
DRY_RUN="false"

while [[ $# -gt 0 ]]; do
  case "$1" in
    --bump-agent) BUMP_AGENT="${2:-}"; shift 2 ;;
    --bump-suite) BUMP_SUITE="${2:-}"; shift 2 ;;
    --bump-ui) BUMP_UI="${2:-}"; shift 2 ;;
    --bump-db) BUMP_DB="${2:-}"; shift 2 ;;
    --bump-channel) BUMP_CHANNEL="${2:-}"; shift 2 ;;
    --artifact-dir) ARTIFACT_DIR="${2:-}"; shift 2 ;;
    --channel) CHANNEL="${2:-}"; shift 2 ;;
    --output-dir) OUTPUT_DIR="${2:-}"; shift 2 ;;
    --upload-target) UPLOAD_TARGET="${2:-}"; shift 2 ;;
    --skip-upload) SKIP_UPLOAD="true"; shift ;;
    --dry-run) DRY_RUN="true"; shift ;;
    -h|--help) usage; exit 0 ;;
    *) echo "ERROR: unknown arg: $1" >&2; usage; exit 1 ;;
  esac
done

require_cmd jq
require_cmd zip

[[ -f "$MANIFEST" ]] || { echo "ERROR: missing $MANIFEST" >&2; exit 1; }
[[ -z "$BUMP_AGENT" ]] || is_semver "$BUMP_AGENT" || { echo "ERROR: invalid --bump-agent" >&2; exit 1; }
[[ -z "$BUMP_SUITE" ]] || is_semver "$BUMP_SUITE" || { echo "ERROR: invalid --bump-suite" >&2; exit 1; }
[[ -z "$BUMP_UI" ]] || is_semver "$BUMP_UI" || { echo "ERROR: invalid --bump-ui" >&2; exit 1; }
[[ -z "$BUMP_DB" ]] || is_semver "$BUMP_DB" || { echo "ERROR: invalid --bump-db" >&2; exit 1; }

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

if [[ -n "$BUMP_AGENT$BUMP_SUITE$BUMP_UI$BUMP_DB$BUMP_CHANNEL" ]]; then
  bump_args=("$ROOT_DIR/scripts/bump-version.sh")
  [[ -n "$BUMP_AGENT" ]] && bump_args+=(--agent "$BUMP_AGENT")
  [[ -n "$BUMP_SUITE" ]] && bump_args+=(--suite "$BUMP_SUITE")
  [[ -n "$BUMP_UI" ]] && bump_args+=(--ui "$BUMP_UI")
  [[ -n "$BUMP_DB" ]] && bump_args+=(--db "$BUMP_DB")
  [[ -n "$BUMP_CHANNEL" ]] && bump_args+=(--channel "$BUMP_CHANNEL")
  run_cmd "${bump_args[@]}"
fi

run_cmd "$ROOT_DIR/scripts/check-version.sh"

agent_version="$(jq -r '.agentVersion' "$MANIFEST")"
manifest_channel="$(jq -r '.build.channel' "$MANIFEST")"
if [[ -z "$CHANNEL" ]]; then
  CHANNEL="$manifest_channel"
fi

artifact_name="pactoolkits-agent-${agent_version}-${CHANNEL}.zip"
artifact_path="$OUTPUT_DIR/$artifact_name"

if [[ "$DRY_RUN" == "true" ]]; then
  echo "Release plan:"
  echo "- agentVersion: $agent_version"
  echo "- channel: $CHANNEL"
  if [[ -n "$ARTIFACT_DIR" ]]; then
    echo "- mode: prebuilt artifact dir ($ARTIFACT_DIR)"
  else
    echo "- mode: source runtime bundle"
  fi
  echo "- output: $artifact_path"
else
  mkdir -p "$OUTPUT_DIR"
fi

stage_dir="$(mktemp -d)"
trap 'rm -rf "$stage_dir"' EXIT

if [[ -n "$ARTIFACT_DIR" ]]; then
  if [[ "$DRY_RUN" != "true" && ! -d "$ARTIFACT_DIR" ]]; then
    echo "ERROR: --artifact-dir not found: $ARTIFACT_DIR" >&2
    exit 1
  fi
  run_cmd rsync -a --exclude '.DS_Store' "$ARTIFACT_DIR"/ "$stage_dir"/
else
  run_cmd mkdir -p "$stage_dir/src" "$stage_dir/assets"
  run_cmd cp "$AGENT_DIR/main.ahk" "$stage_dir/"
  run_cmd cp "$AGENT_DIR/version.generated.json" "$stage_dir/"
  run_cmd cp -R "$AGENT_DIR/src/." "$stage_dir/src/"
  run_cmd cp -R "$AGENT_DIR/assets/." "$stage_dir/assets/"
  if [[ -f "$AGENT_DIR/.env.example" ]]; then
    run_cmd cp "$AGENT_DIR/.env.example" "$stage_dir/"
  fi
fi

if [[ "$DRY_RUN" == "true" ]]; then
  printf '[dry-run] (cd %q && zip -r -q %q .)\n' "$stage_dir" "$artifact_path"
else
  (
    cd "$stage_dir"
    zip -r -q "$artifact_path" .
  )
fi

echo "Package ready: $artifact_path"

if [[ "$SKIP_UPLOAD" == "true" ]]; then
  echo "Skip upload."
  exit 0
fi

if [[ -z "$UPLOAD_TARGET" ]]; then
  echo "No upload target provided."
  exit 0
fi

if [[ "$DRY_RUN" != "true" ]]; then
  require_cmd rsync
fi
run_cmd rsync -avz "$artifact_path" "$UPLOAD_TARGET"
echo "Upload done: $UPLOAD_TARGET"
