#!/usr/bin/env bash
# Phase A：标出页面 ViewModel 里没有 AXAML 绑定的属性（Phase B 再确认）
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/../../../.." && pwd)"
cd "$ROOT_DIR"

VIEWS_DIR="apps/desktop-avalonia/src/Views"
VM_DIR="apps/desktop-avalonia/src/ViewModels/Pages"
warn=0
USE_RG=0
command -v rg > /dev/null 2>&1 && USE_RG=1

search() {
  local pattern="$1"
  shift
  if [[ "$USE_RG" -eq 1 ]]; then
    rg --no-heading \
      --glob '!.git/**' --glob '!**/bin/**' --glob '!**/obj/**' \
      "$pattern" "$@" 2> /dev/null || true
  else
    grep -RE "$pattern" "$@" 2> /dev/null || true
  fi
}

snake_to_pascal() {
  local name="${1#_}"
  echo "$name" | awk -F_ '{for(i=1;i<=NF;i++){printf toupper(substr($i,1,1)) substr($i,2)}}'
}

echo "Orphan VM property scan (Phase A — Views/*.axaml binding check only)"
echo "repo: $ROOT_DIR"
if [[ "$USE_RG" -eq 0 ]]; then
  echo "note: rg not found — using grep fallback"
fi
echo

collect_vm_files() {
  find "$VM_DIR" -name '*.cs' -type f 2> /dev/null | sort
}

while IFS= read -r vm_file; do
  [[ -f "$vm_file" ]] || continue
  vm_base="$(basename "$vm_file" .cs)"

  while IFS= read -r line; do
    [[ -z "$line" ]] && continue
    field="$(echo "$line" | sed -nE 's/.*private [^ ]+ (_[A-Za-z0-9_]+).*/\1/p')"
    [[ -z "$field" ]] && continue
    prop="$(snake_to_pascal "$field")"
    [[ -z "$prop" ]] && continue

    if [[ "$USE_RG" -eq 1 ]]; then
      hits="$(search "$prop" "$VIEWS_DIR" -g '*.axaml' | wc -l | tr -d ' ')"
    else
      hits="$(grep -RE "$prop" "$VIEWS_DIR" --include='*.axaml' 2>/dev/null | wc -l | tr -d ' ')"
    fi
    if [[ "$hits" -eq 0 ]]; then
      echo "WARN: $vm_base.$prop — no binding in Views (check code-behind/tests/GetNotifiableCommands)"
      warn=1
    fi
  done < <(grep -A1 '\[ObservableProperty\]' "$vm_file" 2> /dev/null | grep 'private ' || true)

  while IFS= read -r decl; do
    [[ -z "$decl" ]] && continue
    prop="$(echo "$decl" | sed -nE 's/.*public [^ ]+ ([A-Za-z0-9_]+) \{ get.*/\1/p')"
    [[ -z "$prop" ]] && continue
    [[ "$prop" == "Instance" ]] && continue

    if [[ "$USE_RG" -eq 1 ]]; then
      hits="$(search "$prop" "$VIEWS_DIR" -g '*.axaml' | wc -l | tr -d ' ')"
    else
      hits="$(grep -RE "$prop" "$VIEWS_DIR" --include='*.axaml' 2>/dev/null | wc -l | tr -d ' ')"
    fi
    if [[ "$hits" -eq 0 ]]; then
      echo "WARN: $vm_base.$prop — no binding in Views"
      warn=1
    fi
  done < <(grep -E 'public [A-Za-z0-9_<>,\[\]?]+ [A-Za-z0-9_]+ \{ get' "$vm_file" 2> /dev/null || true)
done < <(collect_vm_files)

if [[ "$warn" -eq 0 ]]; then
  echo "OK:   no obvious orphan VM properties (Views binding heuristic)"
fi

echo
echo "Next: Phase B — references/false-positive-guards.md"
