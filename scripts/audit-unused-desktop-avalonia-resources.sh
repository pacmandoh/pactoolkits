#!/usr/bin/env bash
set -euo pipefail

ROOT="${1:-apps/desktop-avalonia/src}"

if [[ ! -d "$ROOT" ]]; then
  echo "UI root not found: $ROOT" >&2
  exit 1
fi

if command -v rg > /dev/null 2>&1; then
  SEARCH() { rg --glob "$1" -n "$2" "$ROOT" 2> /dev/null || true; }
else
  SEARCH() {
    find "$ROOT" -type f -name "${1#**/}" 2> /dev/null | while read -r file; do
      grep -n "$2" "$file" 2> /dev/null || true
    done
  }
  SEARCH_ALL() {
    find "$ROOT" \( -name '*.axaml' -o -name '*.cs' \) -type f 2> /dev/null | while read -r file; do
      grep -nF "$1" "$file" 2> /dev/null || true
    done
  }
fi

TMP_KEYS=$(mktemp)
TMP_UNUSED=$(mktemp)
trap 'rm -f "$TMP_KEYS" "$TMP_UNUSED"' EXIT

if command -v rg > /dev/null 2>&1; then
  rg --glob "**/*.axaml" -n 'x:Key="[^"]+"' "$ROOT" \
    | sed -E 's#^([^:]+):[0-9]+:.*x:Key="([^"]+)".*#\1\t\2#' \
    | sort -u > "$TMP_KEYS"
else
  find "$ROOT" -name '*.axaml' -type f | while read -r file; do
    grep -n 'x:Key="[^"]*"' "$file" 2> /dev/null \
      | sed -E 's#^([^:]+):[0-9]+:.*x:Key="([^"]+)".*#\1\t\2#' || true
  done | sort -u > "$TMP_KEYS"
fi

echo "[audit] scanning resource keys..."
while IFS=$'\t' read -r file key; do
  [[ -z "${key:-}" ]] && continue

  if [[ "$key" =~ ^Brush(Done|Danger|Warning|Info|Purple)Bg[0-9]+$ ]]; then
    continue
  fi

  if command -v rg > /dev/null 2>&1; then
    ref_count=$( (rg --glob "**/*.axaml" --glob "**/*.cs" -n -F "$key" "$ROOT" || true) \
      | wc -l | tr -d ' ')
  else
    ref_count=$(SEARCH_ALL "$key" | wc -l | tr -d ' ')
  fi

  if [[ "$ref_count" -le 1 ]]; then
    printf '%s\t%s\n' "$file" "$key" >> "$TMP_UNUSED"
  fi
done < "$TMP_KEYS"

if [[ -s "$TMP_UNUSED" ]]; then
  echo "[audit] unused resource keys:"
  cat "$TMP_UNUSED"
else
  echo "[audit] no unused resource keys found"
fi

TMP_STYLE_CLASSES=$(mktemp)
TMP_UNUSED_CLASSES=$(mktemp)
trap 'rm -f "$TMP_KEYS" "$TMP_UNUSED" "$TMP_STYLE_CLASSES" "$TMP_UNUSED_CLASSES"' EXIT

if command -v rg > /dev/null 2>&1; then
  rg --glob "**/Styles/**/*.axaml" -n 'Selector="[^"]*\.[A-Za-z_][A-Za-z0-9_-]*' "$ROOT" \
    | sed -E 's#.*Selector="([^"]+)".*#\1#' \
    | tr ' ' '\n' \
    | sed -nE 's#.*\.([A-Za-z_][A-Za-z0-9_-]*).*#\1#p' \
    | sort -u > "$TMP_STYLE_CLASSES"
else
  find "$ROOT/Styles" -name '*.axaml' -type f | while read -r file; do
    grep -n 'Selector="[^"]*\.' "$file" 2> /dev/null \
      | sed -E 's#.*Selector="([^"]+)".*#\1#' \
      | tr ' ' '\n' \
      | sed -nE 's#.*\.([A-Za-z_][A-Za-z0-9_-]*).*#\1#p' || true
  done | sort -u > "$TMP_STYLE_CLASSES"
fi

echo "[audit] scanning style classes..."
while IFS= read -r cls; do
  [[ -z "${cls:-}" ]] && continue

  if command -v rg > /dev/null 2>&1; then
    ref_count=$(
      (
        rg --glob "**/*.axaml" -n "Classes=\"[^\"]*\\b${cls}\\b" "$ROOT" || true
        rg --glob "**/*.axaml" -n "Classes\\.${cls}=" "$ROOT" || true
        rg --glob "**/*.cs" -n "\"${cls}\"" "$ROOT" || true
      ) | wc -l | tr -d ' '
    )
  else
    ref_count=$(
      (
        grep -rE "Classes=\"[^\"]*\\b${cls}\\b" "$ROOT" --include='*.axaml' 2> /dev/null || true
        grep -rE "Classes\\.${cls}=" "$ROOT" --include='*.axaml' 2> /dev/null || true
        grep -rF "\"${cls}\"" "$ROOT" --include='*.cs' 2> /dev/null || true
      ) | wc -l | tr -d ' '
    )
  fi

  if [[ "$ref_count" == "0" ]]; then
    echo "$cls" >> "$TMP_UNUSED_CLASSES"
  fi
done < "$TMP_STYLE_CLASSES"

if [[ -s "$TMP_UNUSED_CLASSES" ]]; then
  echo "[audit] style classes with no direct Classes= usage (review manually):"
  cat "$TMP_UNUSED_CLASSES"
else
  echo "[audit] no unused style classes found"
fi
