#!/usr/bin/env bash
# Phase A：Views 里用了、Styles/ 却没有对应 Selector 的 AXAML class token
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/../../../.." && pwd)"
cd "$ROOT_DIR"

VIEWS="apps/desktop-avalonia/src/Views"
STYLES="apps/desktop-avalonia/src/Styles"
TMP="$(mktemp -d)"
trap 'rm -rf "$TMP"' EXIT
warn=0
USE_RG=0
command -v rg > /dev/null 2>&1 && USE_RG=1

SKIP_REGEX='^(pac-|text-|font-|m-|p-|gap-|flex|grid|col-|row-|items-|justify-|rounded|border|bg-|opacity-|hidden|visible|w-|h-|min-|max-|shad|accent|muted|destructive|primary|secondary|card|sidebar|shell|page-|kpi-|status-|dg-|data-grid|Primary|Secondary|Ghost|Outline|Destructive|Large|Icon|Clearable|Single|Stack|Caption|Action|ActionTile|ActionWide|NoPressedAnimation|PasswordReveal)$'

echo "Unused style class scan (Phase A)"
echo "repo: $ROOT_DIR"
if [[ "$USE_RG" -eq 0 ]]; then
  echo "note: rg not found — using grep fallback"
fi
echo

# Collect class token from Selector=".Token" or Selector="Type.Token"
if [[ "$USE_RG" -eq 1 ]]; then
  rg 'Selector="' "$STYLES" -g '*.axaml' 2>/dev/null \
    | sed -nE 's/.*Selector="[^"]*\.([^".^[:space:]|]+).*/\1/p' \
    > "$TMP/defined.txt" || true
else
  grep -RE 'Selector="' "$STYLES" --include='*.axaml' 2>/dev/null \
    | sed -nE 's/.*Selector="[^"]*\.([^".^[:space:]|]+).*/\1/p' \
    > "$TMP/defined.txt" || true
fi

# Collect Classes="..." tokens from Views
if [[ "$USE_RG" -eq 1 ]]; then
  rg 'Classes="[^"]+"' "$VIEWS" -g '*.axaml' 2>/dev/null \
    | sed -nE 's/.*Classes="([^"]+)".*/\1/p' \
    > "$TMP/classes-lines.txt" || true
else
  grep -RE 'Classes="[^"]+"' "$VIEWS" --include='*.axaml' 2>/dev/null \
    | sed -nE 's/.*Classes="([^"]+)".*/\1/p' \
    > "$TMP/classes-lines.txt" || true
fi

: > "$TMP/used.txt"
while IFS= read -r block; do
  for token in $block; do
    [[ -z "$token" ]] && continue
    echo "$token" >> "$TMP/used.txt"
  done
done < "$TMP/classes-lines.txt"

sort -u "$TMP/used.txt" > "$TMP/used-uniq.txt"

while IFS= read -r token; do
  [[ -z "$token" ]] && continue
  [[ "$token" =~ $SKIP_REGEX ]] && continue
  if grep -qxF "$token" "$TMP/defined.txt" 2>/dev/null; then
    continue
  fi
  if grep -qE "Selector=\"[^\"]*\\.${token}([.\"]|$)" "$STYLES" --include='*.axaml' 2>/dev/null; then
    continue
  fi
  echo "WARN: class '$token' in Views Classes= but no matching Selector in Styles/"
  warn=1
done < "$TMP/used-uniq.txt"

if [[ "$warn" -eq 0 ]]; then
  echo "OK:   no obvious orphan custom class tokens (heuristic; dynamic Classes.Add may differ)"
fi

echo
echo "Next: Phase B — ShadUI built-in Classes may WARN; rg token in Styles/Controls/*.cs for Classes.Add"
echo "      see references/false-positive-guards.md"
