#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
ARTIFACT_DIR="$ROOT_DIR/artifacts/agents/win-x64"
# shellcheck source=manifest-v2.sh
source "$ROOT_DIR/scripts/manifest-v2.sh"

MANIFEST="$ROOT_DIR/release-manifest.json"
RELEASES_DIR="$ROOT_DIR/artifacts/agents/Releases"

usage() {
  cat << 'USAGE'
Usage:
  release-agents.sh [options]

Options:
  --bump-agents X.Y.Z         Optional: bump components.agents.version before packaging.
  --bump-component ID=X.Y.Z  Optional: bump a manifest component (e.g. agents=0.3.1).
  --bump-product X.Y.Z       Optional: bump product.version.
  --bump-desktop X.Y.Z       Optional: bump components.desktop.<impl>.version.
  --bump-db X.Y.Z            Optional: bump components.database.postgres.version.
  --bump-channel C           Optional: bump release.channel (stable|beta).
  --artifact-dir DIR         Prebuilt host+modules dir (default: artifacts/agents/win-x64).
  --channel C                Optional: package channel tag (default: manifest release.channel).
  --output-dir DIR           Output directory (default: artifacts/agents/Releases).
  --upload-target TARGET     Optional rsync target, e.g. user@host:/var/www/updates/pactoolkits-agents/
  --dry-run                  Print commands only.
  --skip-upload              Do not upload.
  -h, --help                 Show help.

Notes:
  - Packages the CI staging layout only:
      <artifact-dir>/Agents.exe
      <artifact-dir>/Modules/Injector/...
  - Build on Windows via .github/workflows/build-agents.yml (or equivalent), then pass --artifact-dir.
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
    agents | desktop | database.postgres) ;;
    *)
      echo "ERROR: unknown component in --bump-component: $component_id" >&2
      exit 1
      ;;
  esac
}

validate_bump_conflicts() {
  if [[ -n "$BUMP_AGENTS" && -n "$BUMP_COMPONENT" ]]; then
    local component_id="${BUMP_COMPONENT%%=*}"
    if [[ "$component_id" == "agents" ]]; then
      local component_version="${BUMP_COMPONENT#*=}"
      if [[ "$BUMP_AGENTS" != "$component_version" ]]; then
        echo "ERROR: conflicting agents version: --bump-agents $BUMP_AGENTS vs --bump-component $BUMP_COMPONENT" >&2
      else
        echo "ERROR: duplicate agents version bump: use --bump-agents or --bump-component agents=..., not both" >&2
      fi
      exit 1
    fi
  fi

  if [[ -n "$BUMP_DESKTOP" && -n "$BUMP_COMPONENT" && "${BUMP_COMPONENT%%=*}" == "desktop" ]]; then
    local component_version="${BUMP_COMPONENT#*=}"
    if [[ "$BUMP_DESKTOP" != "$component_version" ]]; then
      echo "ERROR: conflicting desktop version: --bump-desktop $BUMP_DESKTOP vs --bump-component $BUMP_COMPONENT" >&2
    else
      echo "ERROR: duplicate desktop version bump: use --bump-desktop or --bump-component desktop=..., not both" >&2
    fi
    exit 1
  fi

  if [[ -n "$BUMP_DB" && -n "$BUMP_COMPONENT" && "${BUMP_COMPONENT%%=*}" == "database.postgres" ]]; then
    local component_version="${BUMP_COMPONENT#*=}"
    if [[ "$BUMP_DB" != "$component_version" ]]; then
      echo "ERROR: conflicting database version: --bump-db $BUMP_DB vs --bump-component $BUMP_COMPONENT" >&2
    else
      echo "ERROR: duplicate database version bump: use --bump-db or --bump-component database.postgres=..., not both" >&2
    fi
    exit 1
  fi
}

validate_agents_artifact_layout() {
  local dir="$1"
  local host_exe="$dir/Agents.exe"
  local module_dir="$dir/Modules/Injector"
  local module_exe="$module_dir/Injector.exe"
  [[ -f "$host_exe" ]] || {
    echo "ERROR: missing Host binary: $host_exe" >&2
    echo "Build via CI (build-agents.yml) or publish host + module into --artifact-dir." >&2
    return 1
  }
  [[ -d "$module_dir" ]] || {
    echo "ERROR: missing injector module dir: $module_dir" >&2
    return 1
  }
  [[ -f "$module_exe" ]] || {
    echo "ERROR: missing injector module binary: $module_exe" >&2
    return 1
  }
  [[ -f "$module_dir/module.json" ]] || {
    echo "ERROR: missing module.json: $module_dir/module.json" >&2
    return 1
  }
}

DRY_RUN="false"
SKIP_UPLOAD="false"
UPLOAD_TARGET=""
OUTPUT_DIR="$RELEASES_DIR"
CHANNEL=""
BUMP_AGENTS=""
BUMP_COMPONENT=""
BUMP_PRODUCT=""
BUMP_DESKTOP=""
BUMP_DB=""
BUMP_CHANNEL=""
PLAN_MANIFEST_TMP=""

while [[ $# -gt 0 ]]; do
  case "$1" in
    --bump-agents)
      BUMP_AGENTS="${2:-}"
      shift 2
      ;;
    --bump-component)
      BUMP_COMPONENT="${2:-}"
      shift 2
      ;;
    --bump-product)
      BUMP_PRODUCT="${2:-}"
      shift 2
      ;;
    --bump-desktop)
      BUMP_DESKTOP="${2:-}"
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
    --dry-run)
      DRY_RUN="true"
      shift
      ;;
    --skip-upload)
      SKIP_UPLOAD="true"
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
  echo "ERROR: manifest not found: $MANIFEST" >&2
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
[[ -z "$BUMP_COMPONENT" ]] || validate_bump_component "$BUMP_COMPONENT"
validate_bump_conflicts

run_cmd() {
  if [[ "$DRY_RUN" == "true" ]]; then
    printf '[dry-run] '
    printf '%q ' "$@"
    echo
    return 0
  fi
  "$@"
}

if [[ -n "$BUMP_AGENTS$BUMP_COMPONENT$BUMP_PRODUCT$BUMP_DESKTOP$BUMP_DB$BUMP_CHANNEL" ]]; then
  bump_args=("$ROOT_DIR/scripts/bump-version.sh")
  [[ -n "$BUMP_AGENTS" ]] && bump_args+=(--component "agents=$BUMP_AGENTS")
  [[ -n "$BUMP_COMPONENT" ]] && bump_args+=(--component "$BUMP_COMPONENT")
  [[ -n "$BUMP_PRODUCT" ]] && bump_args+=(--product "$BUMP_PRODUCT")
  [[ -n "$BUMP_DESKTOP" ]] && bump_args+=(--desktop "$BUMP_DESKTOP")
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

agent_version="$(manifest_agents_version "$MANIFEST_FOR_PLAN")"
manifest_channel="$(manifest_release_channel "$MANIFEST_FOR_PLAN")"
if [[ -z "$CHANNEL" ]]; then
  CHANNEL="$manifest_channel"
fi

artifact_name="PacToolkits-Agents-win-x64-${agent_version}-${CHANNEL}.zip"
artifact_path="$OUTPUT_DIR/$artifact_name"

echo "Release plan:"
echo "- agents.version: $agent_version"
injector_module_version="$(manifest_agents_module_version "$MANIFEST_FOR_PLAN" "Injector")"
[[ -n "$injector_module_version" ]] || {
  echo "ERROR: missing agents.modules.Injector.version" >&2
  exit 1
}
echo "- agents.modules.Injector.version: $injector_module_version"
echo "- channel: $CHANNEL"
echo "- artifact-dir: $ARTIFACT_DIR"
echo "- output: $artifact_path"

if [[ "$DRY_RUN" == "true" ]]; then
  printf '[dry-run] validate layout %q\n' "$ARTIFACT_DIR"
  printf '[dry-run] rsync -a %q/ %q/\n' "$ARTIFACT_DIR" "$(mktemp -u)/stage"
  printf '[dry-run] zip -> %q\n' "$artifact_path"
else
  validate_agents_artifact_layout "$ARTIFACT_DIR" || exit 1
  mkdir -p "$OUTPUT_DIR"
  stage_dir="$(mktemp -d)"
  trap 'rm -rf "$stage_dir"' EXIT
  rsync -a --exclude '.DS_Store' "$ARTIFACT_DIR"/ "$stage_dir"/
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
