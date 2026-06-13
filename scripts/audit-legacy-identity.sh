#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT_DIR"

fail=0

search() {
  local pattern="$1"
  shift
  if command -v rg >/dev/null 2>&1; then
    rg -n --no-heading \
      --glob '!.git/**' --glob '!**/bin/**' --glob '!**/obj/**' --glob '!.idea/**' \
      --glob '!scripts/audit-legacy-identity.sh' \
      "$pattern" "$@" 2>/dev/null || true
  else
    grep -RIn --exclude-dir=.git --exclude-dir=bin --exclude-dir=obj --exclude-dir=.idea \
      --exclude=audit-legacy-identity.sh \
      -E "$pattern" "$@" 2>/dev/null || true
  fi
}

scan() {
  local label="$1"
  local pattern="$2"
  shift 2
  local matches
  matches="$(search "$pattern" "$@")"
  if [[ -n "$matches" ]]; then
    echo "FAIL: $label"
    echo "$matches"
    echo
    fail=1
  else
    echo "OK:   $label"
  fi
}

allow() {
  local label="$1"
  local pattern="$2"
  shift 2
  local matches
  matches="$(search "$pattern" "$@")"
  if [[ -n "$matches" ]]; then
    echo "OK:   $label (intentional)"
    echo "$matches"
    echo
  else
    echo "WARN: $label (expected reference not found)"
    echo
  fi
}

echo "Legacy identity audit"
echo "repo: $ROOT_DIR"
echo

repo_path_hits="$(search '(^|[[:space:]["'\''`./]|cd )pactoolkits-(ui|agent|db)/' \
  .github scripts apps database README.md README.zh-CN.md \
  | grep -Ev 'updates/pactoolkits-(ui|agent|db)/' || true)"
if [[ -n "$repo_path_hits" ]]; then
  echo "FAIL: repo filesystem old root dirs"
  echo "$repo_path_hits"
  echo
  fail=1
else
  echo "OK:   repo filesystem old root dirs"
fi

allow "remote update feed paths (not repo layout)" 'updates/pactoolkits-(ui|agent|db)/' \
  scripts README.md README.zh-CN.md

scan "avares old assembly uri" 'avares://pactoolkits-ui/' \
  apps/desktop-avalonia/src

scan "workflow/script old main exe" 'pactoolkits-ui\.exe' \
  .github scripts

scan "csproj old assembly override" '<AssemblyName>pactoolkits-ui</AssemblyName>' \
  apps/desktop-avalonia/src

scan "legacy csharp namespace" 'pactoolkits_ui' \
  apps/desktop-avalonia/src

allow "legacy config compatibility constant" 'pactoolkits-ui\.config\.json' \
  apps/desktop-avalonia/src/Services/Infrastructure/AppConfigStore.cs

if [[ "$fail" -ne 0 ]]; then
  echo "Legacy identity audit failed."
  exit 1
fi

echo "Legacy identity audit passed."
