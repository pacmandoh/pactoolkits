#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
AGENT_DIR="$ROOT_DIR/runtime/agents/injector-ahk"
ARTIFACT_DIR="$ROOT_DIR/artifacts/agents/agent-injector-ahk/win-x64"
# shellcheck source=manifest-v2.sh
source "$ROOT_DIR/scripts/manifest-v2.sh"

MANIFEST="$ROOT_DIR/release-manifest.json"
RELEASES_DIR="$AGENT_DIR/Releases"

usage() {
  cat << 'USAGE'
Usage:
  release-agent-injector-ahk.sh [options]

Options:
  --bump-agent X.Y.Z         Optional: bump components.agent-injector-ahk.version before packaging.
  --bump-component ID=X.Y.Z  Optional: bump a manifest component (e.g. agent-injector-ahk=0.3.1).
  --bump-product X.Y.Z       Optional: bump product.version (alias: --bump-suite).
  --bump-desktop X.Y.Z       Optional: bump components.desktop.version (alias: --bump-ui).
  --bump-db X.Y.Z            Optional: bump components.database-postgres.version.
  --bump-channel C           Optional: bump release.channel (stable|beta).
  --artifact-dir DIR         Optional: package prebuilt files from DIR instead of source files.
  --channel C                Optional: package channel tag (default: manifest build.channel).
  --output-dir DIR           Output directory (default: runtime/agents/injector-ahk/Releases).
  --upload-target TARGET     Optional rsync target, e.g. user@host:/var/www/updates/pactoolkits-agent/
  --dry-run                  Print commands only.
  --skip-upload              Do not upload.
  -h, --help                 Show help.

Notes:
  - This script does not compile AHK on macOS. It packages either:
    1) source runtime bundle (main.ahk + src + assets + version file), or
    2) files from --artifact-dir (recommended if built on Windows).
  - With --dry-run and a bump flag, release plan uses a preview manifest so
    artifact names reflect the bumped version.
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

validate_bump_component() {
  local item="$1"
  [[ "$item" == *=* ]] || {
    echo "ERROR: --bump-component expects COMPONENT_ID=X.Y.Z, got: $item" >&2
    exit 1
  }
  local component_id="${item%%=*}"
  local component_version="${item#*=}"
  is_semver "$component_version" || {
    echo "ERROR: invalid semver in --bump-component: $item" >&2
    exit 1
  }
  case "$component_id" in
    agent-injector-ahk | desktop | database-postgres) ;;
    *)
      echo "ERROR: unknown component in --bump-component: $component_id" >&2
      exit 1
      ;;
  esac
}

validate_bump_conflicts() {
  if [[ -n "$BUMP_AGENT" && -n "$BUMP_COMPONENT" ]]; then
    local component_id="${BUMP_COMPONENT%%=*}"
    if [[ "$component_id" == "agent-injector-ahk" ]]; then
      local component_version="${BUMP_COMPONENT#*=}"
      if [[ "$BUMP_AGENT" != "$component_version" ]]; then
        echo "ERROR: conflicting agent version: --bump-agent $BUMP_AGENT vs --bump-component $BUMP_COMPONENT" >&2
      else
        echo "ERROR: duplicate agent version bump: use --bump-agent or --bump-component agent-injector-ahk=..., not both" >&2
      fi
      exit 1
    fi
  fi

  if [[ -n "$BUMP_UI" && -n "$BUMP_COMPONENT" && "${BUMP_COMPONENT%%=*}" == "desktop" ]]; then
    local component_version="${BUMP_COMPONENT#*=}"
    if [[ "$BUMP_UI" != "$component_version" ]]; then
      echo "ERROR: conflicting desktop version: --bump-desktop $BUMP_UI vs --bump-component $BUMP_COMPONENT" >&2
    else
      echo "ERROR: duplicate desktop version bump: use --bump-desktop or --bump-component desktop=..., not both" >&2
    fi
    exit 1
  fi

  if [[ -n "$BUMP_DB" && -n "$BUMP_COMPONENT" && "${BUMP_COMPONENT%%=*}" == "database-postgres" ]]; then
    local component_version="${BUMP_COMPONENT#*=}"
    if [[ "$BUMP_DB" != "$component_version" ]]; then
      echo "ERROR: conflicting database version: --bump-db $BUMP_DB vs --bump-component $BUMP_COMPONENT" >&2
    else
      echo "ERROR: duplicate database version bump: use --bump-db or --bump-component database-postgres=..., not both" >&2
    fi
    exit 1
  fi
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
BUMP_COMPONENT=""
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
PLAN_MANIFEST_TMP=""
stage_dir=""

while [[ $# -gt 0 ]]; do
  case "$1" in
    --bump-component)
      BUMP_COMPONENT="${2:-}"
      shift 2
      ;;
    --bump-agent)
      BUMP_AGENT="${2:-}"
      shift 2
      ;;
    --bump-product | --bump-suite)
      BUMP_SUITE="${2:-}"
      shift 2
      ;;
    --bump-desktop | --bump-ui)
      BUMP_UI="${2:-}"
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
    --artifact-dir)
      ARTIFACT_DIR="${2:-}"
      shift 2
      ;;
    --channel)
      CHANNEL="${2:-}"
      shift 2
      ;;
    --output-dir)
      OUTPUT_DIR="${2:-}"
      shift 2
      ;;
    --upload-target)
      UPLOAD_TARGET="${2:-}"
      shift 2
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
require_cmd zip

[[ -f "$MANIFEST" ]] || {
  echo "ERROR: missing $MANIFEST" >&2
  exit 1
}
[[ -z "$BUMP_AGENT" ]] || is_semver "$BUMP_AGENT" || {
  echo "ERROR: invalid --bump-agent" >&2
  exit 1
}
[[ -z "$BUMP_SUITE" ]] || is_semver "$BUMP_SUITE" || {
  echo "ERROR: invalid --bump-suite" >&2
  exit 1
}
[[ -z "$BUMP_UI" ]] || is_semver "$BUMP_UI" || {
  echo "ERROR: invalid --bump-ui" >&2
  exit 1
}
[[ -z "$BUMP_DB" ]] || is_semver "$BUMP_DB" || {
  echo "ERROR: invalid --bump-db" >&2
  exit 1
}
[[ -z "$BUMP_COMPONENT" ]] || validate_bump_component "$BUMP_COMPONENT"
validate_bump_conflicts

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

cleanup_release_temp() {
  [[ -n "$stage_dir" ]] && rm -rf "$stage_dir"
  [[ -n "$PLAN_MANIFEST_TMP" ]] && rm -f "$PLAN_MANIFEST_TMP"
}
trap cleanup_release_temp EXIT

if [[ -n "$BUMP_AGENT$BUMP_COMPONENT$BUMP_SUITE$BUMP_UI$BUMP_DB$BUMP_CHANNEL" ]]; then
  bump_args=("$ROOT_DIR/scripts/bump-version.sh")
  [[ -n "$BUMP_AGENT" ]] && bump_args+=(--agent "$BUMP_AGENT")
  [[ -n "$BUMP_COMPONENT" ]] && bump_args+=(--component "$BUMP_COMPONENT")
  [[ -n "$BUMP_SUITE" ]] && bump_args+=(--suite "$BUMP_SUITE")
  [[ -n "$BUMP_UI" ]] && bump_args+=(--ui "$BUMP_UI")
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

agent_version="$(manifest_agent_injector_ahk_version "$MANIFEST_FOR_PLAN" 2> /dev/null || jq -r '.components["agent-injector-ahk"].version // .agentVersion' "$MANIFEST_FOR_PLAN")"
manifest_channel="$(manifest_release_channel "$MANIFEST_FOR_PLAN" 2> /dev/null || jq -r '.release.channel // .build.channel' "$MANIFEST_FOR_PLAN")"
if [[ -z "$CHANNEL" ]]; then
  CHANNEL="$manifest_channel"
fi

artifact_name="pactoolkits-injector-win-x64-${agent_version}-${CHANNEL}.zip"
artifact_path="$OUTPUT_DIR/$artifact_name"

if [[ "$DRY_RUN" == "true" ]]; then
  echo "Release plan:"
  echo "- agent-injector-ahk.version: $agent_version"
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
