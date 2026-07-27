#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT_DIR"

fail=0

search() {
  local pattern="$1"
  shift
  if command -v rg > /dev/null 2>&1; then
    rg -n --no-heading \
      --glob '!.git/**' --glob '!**/bin/**' --glob '!**/obj/**' --glob '!.idea/**' \
      --glob '!scripts/audit-legacy-identity.sh' \
      "$pattern" "$@" 2> /dev/null || true
  else
    grep -RIn --exclude-dir=.git --exclude-dir=bin --exclude-dir=obj --exclude-dir=.idea \
      --exclude=audit-legacy-identity.sh \
      -E "$pattern" "$@" 2> /dev/null || true
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
  | grep -Ev 'updates/pactoolkits-(ui|agent|db)/' \
  | grep -Ev 'scripts/validate-database-policy\.sh:' || true)"
if [[ -n "$repo_path_hits" ]]; then
  echo "FAIL: repo filesystem old root dirs"
  echo "$repo_path_hits"
  echo
  fail=1
else
  echo "OK:   repo filesystem old root dirs"
fi

scan "avares old assembly uri" 'avares://pactoolkits-ui/' \
  apps/desktop-avalonia/src

scan "workflow/script old main exe" 'pactoolkits-ui\.exe' \
  .github scripts

scan "csproj old assembly override" '<AssemblyName>pactoolkits-ui</AssemblyName>' \
  apps/desktop-avalonia/src

scan "csproj kebab assembly override" '<AssemblyName>pactoolkits-desktop</AssemblyName>' \
  apps/desktop-avalonia/src

scan "avares old kebab assembly uri" 'avares://pactoolkits-desktop/' \
  apps/desktop-avalonia/src tests

scan "workflow/script kebab main exe" 'pactoolkits-desktop\.exe' \
  .github scripts

scan "legacy csharp namespace" 'pactoolkits_ui' \
  apps/desktop-avalonia/src

scan "legacy agent-injector-ahk identity" 'agent-injector-ahk' \
  apps packages scripts .github runtime docs tests

scan "legacy agent-injector.exe module binary" 'agent-injector\.exe' \
  apps packages scripts .github runtime docs tests

scan "legacy injector-ahk source path" 'runtime/agents/injector-ahk' \
  apps packages scripts .github docs tests

scan "legacy AhkInjector type" 'AhkInjector' \
  apps packages scripts .github docs tests

scan "legacy postgres/sql source path" 'database/postgres/sql' \
  apps packages scripts .github docs tests database

scan "legacy Sql publish link" 'Link="Sql\\' \
  packages

scan "legacy PacToolkits.Agents.exe host name" 'PacToolkits\.Agents\.exe' \
  apps packages scripts .github runtime docs tests

allow "legacy config compatibility constant" 'pactoolkits-ui\.config\.json' \
  apps/desktop-avalonia/src/Services/Infrastructure/AppConfigStore.cs

allow "legacy migration dir constant" 'LEGACY_MIGRATION_DIR="pactoolkits-db/sql/migrations"' \
  scripts/validate-database-policy.sh

# 已删除的兼容路径不得重新进入产品代码，命中即视为审计失败
scan "legacy Tools pacinjector migration" 'pacinjector' \
  packages/agents-contracts apps/desktop-avalonia/src tests/PacToolkits.Agents.Contracts.Tests tests/PacToolkits.Desktop.Tests docs

scan "legacy AutomationTools migration" 'AutomationTools' \
  packages/agents-contracts apps/desktop-avalonia/src tests docs

scan "legacy Agents.Injector config path" 'Agents\.Injector' \
  packages/agents-contracts apps/desktop-avalonia/src tests docs

scan "legacy ValidateInjector / InjectorOptions" 'ValidateInjector|InjectorOptions' \
  packages/agents-contracts apps/desktop-avalonia/src tests docs

if [[ "$fail" -ne 0 ]]; then
  echo "Legacy identity audit failed."
  exit 1
fi

echo "Legacy identity audit passed."
