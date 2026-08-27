#!/usr/bin/env bash
# Phase A：标出引用很少的已注册 DI 类型（多半已死 — Phase B 再确认）
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/../../../.." && pwd)"
cd "$ROOT_DIR"

warn=0
USE_RG=0
command -v rg > /dev/null 2>&1 && USE_RG=1

search() {
  local pattern="$1"
  shift
  if [[ "$USE_RG" -eq 1 ]]; then
    rg -n --no-heading \
      --glob '!.git/**' --glob '!**/bin/**' --glob '!**/obj/**' --glob '!.idea/**' \
      "$pattern" "$@" 2> /dev/null || true
  else
    local paths=()
    local exclude_files=()
    while [[ $# -gt 0 ]]; do
      case "$1" in
        --glob)
          shift
          ;;
        --glob=*)
          local g="${1#--glob=}"
          if [[ "$g" == !* ]]; then
            exclude_files+=("${g#!}")
          fi
          shift
          ;;
        *)
          paths+=("$1")
          shift
          ;;
      esac
    done
    [[ ${#paths[@]} -eq 0 ]] && paths=(apps packages tests)
    grep -RIn \
      --exclude-dir=.git --exclude-dir=bin --exclude-dir=obj --exclude-dir=.idea \
      --exclude='ServiceCollectionExtensions.cs' --exclude='ServiceRegistration.cs' \
      -E "$pattern" "${paths[@]}" 2> /dev/null || true
  fi
}

count_type_refs() {
  local type="$1"
  local short="${type##*.}"
  local alt=""
  if [[ "$short" == I* ]] && [[ ${#short} -gt 1 ]]; then
    alt="${short#I}"
  fi

  local hits
  hits="$(search "${short}" apps packages tests --glob '*.cs' | wc -l | tr -d ' ')"
  if [[ -n "$alt" ]]; then
    local alt_hits
    alt_hits="$(search "${alt}" apps packages tests --glob '*.cs' | wc -l | tr -d ' ')"
    if [[ "$alt_hits" -gt "$hits" ]]; then
      hits="$alt_hits"
    fi
  fi
  echo "$hits"
}

extract_registered_types() {
  local file="$1"
  [[ -f "$file" ]] || return 0
  grep -oE 'AddSingleton<[^>]+>' "$file" \
    | sed -E 's/AddSingleton<//; s/>$//' \
    | tr ',' '\n' \
    | sed 's/^[[:space:]]*//; s/[[:space:]]*$//' \
    | grep -v '^$' || true
}

REG_FILES=(
  "packages/application/Services/ServiceCollectionExtensions.cs"
  "packages/infrastructure/Database/ServiceCollectionExtensions.cs"
  "apps/desktop-avalonia/src/Composition/ServiceRegistration.cs"
)

echo "Dead DI scan (Phase A — WARN only; confirm via false-positive-guards.md)"
echo "repo: $ROOT_DIR"
if [[ "$USE_RG" -eq 0 ]]; then
  echo "note: rg not found — using grep fallback (install ripgrep for better results)"
fi
echo

declare -A SEEN=()

for reg_file in "${REG_FILES[@]}"; do
  while IFS= read -r type; do
    [[ -z "$type" ]] && continue
    [[ -n "${SEEN[$type]:-}" ]] && continue
    SEEN[$type]=1

    [[ "$type" == *'`'* ]] && continue
    [[ "$type" == "AppPageBase" ]] && continue
    [[ "$type" == "IConfiguration" ]] && continue
    [[ "$type" == "DialogManager" ]] && continue
    [[ "$type" == "ToastManager" ]] && continue

    refs="$(count_type_refs "$type")"
    if [[ "$refs" -le 2 ]]; then
      echo "WARN: low refs ($refs) for DI type '$type' (registered in $reg_file)"
      warn=1
    fi
  done < <(extract_registered_types "$reg_file")
done

echo
echo "Page VM / View pairing"
while IFS= read -r vm_line; do
  [[ -z "$vm_line" ]] && continue
  vm_file="${vm_line%%:*}"
  vm_class="$(basename "$vm_file" .cs)"
  view_name="${vm_class%ViewModel}View.axaml"
  view_path="apps/desktop-avalonia/src/Views/Pages/${view_name}"
  if [[ ! -f "$view_path" ]]; then
    alt="apps/desktop-avalonia/src/Views/${view_name}"
    if [[ ! -f "$alt" ]]; then
      echo "WARN: $vm_class has no matching View ($view_name)"
      warn=1
    fi
  fi
done < <(search 'class [A-Za-z0-9]+ViewModel : AppPageBase' apps/desktop-avalonia/src/ViewModels/Pages -g '*.cs' 2>/dev/null || \
  grep -RE 'class [A-Za-z0-9]+ViewModel : AppPageBase' apps/desktop-avalonia/src/ViewModels/Pages --include='*.cs' 2>/dev/null || true)

if [[ "$warn" -eq 0 ]]; then
  echo "OK:   no low-ref DI types or missing page views detected"
fi

echo
echo "Next: Phase B manual confirmation — references/di-reflection-wiring.md, references/false-positive-guards.md"
