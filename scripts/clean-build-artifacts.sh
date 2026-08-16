#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
DRY_RUN="false"
ALL_DOTNET="false"

usage() {
  cat << 'USAGE'
Usage:
  clean-build-artifacts.sh [--dry-run] [--all-dotnet]

Removes local build/package outputs produced by release and test-package workflows.

Default scope:
  - artifacts/agents
  - artifacts/desktop
  - desktop + Agents Releases directories
  - Avalonia win-x64 publish output

Options:
  --dry-run              Print paths that would be removed.
  --all-dotnet           Also remove all bin/ and obj/ directories in the repo.
  -h, --help             Show this help.
USAGE
}

while [[ $# -gt 0 ]]; do
  case "$1" in
    --dry-run)
      DRY_RUN="true"
      shift
      ;;
    --all-dotnet)
      ALL_DOTNET="true"
      shift
      ;;
    -h | --help)
      usage
      exit 0
      ;;
    *)
      echo "ERROR: unknown arg: $1" >&2
      usage >&2
      exit 1
      ;;
  esac
done

remove_path() {
  local rel="$1"
  local path="$ROOT_DIR/$rel"

  case "$path" in
    "$ROOT_DIR" | "$ROOT_DIR/" | "/" | "") ;;
    "$ROOT_DIR"/*) ;;
    *)
      echo "ERROR: refusing to remove path outside repo: $path" >&2
      exit 1
      ;;
  esac

  if [[ ! -e "$path" ]]; then
    return
  fi

  if [[ "$DRY_RUN" == "true" ]]; then
    echo "would remove: $rel"
  else
    rm -rf "$path"
    echo "removed: $rel"
  fi
}

declare -a paths=(
  "artifacts/agents"
  "artifacts/desktop"
  "apps/desktop-avalonia/src/Releases"
)

for module_dir in "$ROOT_DIR/runtime/agents/modules"/*; do
  [[ -d "$module_dir" && -f "$module_dir/module.json" ]] || continue
  paths+=("${module_dir#"$ROOT_DIR/"}/Releases")
done

paths+=(
  "apps/desktop-avalonia/src/bin/Release/net10.0/win-x64"
  "apps/desktop-avalonia/src/obj/Release/net10.0/win-x64"
)

if [[ "$ALL_DOTNET" == "true" ]]; then
  while IFS= read -r dir; do
    paths+=("${dir#"$ROOT_DIR/"}")
  done < <(find "$ROOT_DIR" \
    \( -path "$ROOT_DIR/.git" -o -path "$ROOT_DIR/.git/*" \) -prune -o \
    -type d \( -name bin -o -name obj \) -print)
fi

seen=""
removed_any="false"
for rel in "${paths[@]}"; do
  [[ -n "$rel" ]] || continue
  case "$seen" in
    *"|$rel|"*) continue ;;
  esac
  seen="${seen}|${rel}|"
  if [[ -e "$ROOT_DIR/$rel" ]]; then
    removed_any="true"
  fi
  remove_path "$rel"
done

if [[ "$removed_any" != "true" ]]; then
  echo "No build artifacts found."
fi
