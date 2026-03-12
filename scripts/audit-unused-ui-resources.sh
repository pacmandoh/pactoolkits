#!/usr/bin/env bash
set -euo pipefail

ROOT="${1:-pactoolkits-ui}"

if [[ ! -d "$ROOT" ]]; then
  echo "UI root not found: $ROOT" >&2
  exit 1
fi

TMP_KEYS=$(mktemp)
TMP_UNUSED=$(mktemp)
trap 'rm -f "$TMP_KEYS" "$TMP_UNUSED"' EXIT

# Collect x:Key definitions from axaml files.
rg --glob "**/*.axaml" -n 'x:Key="[^"]+"' "$ROOT" \
  | sed -E 's#^([^:]+):[0-9]+:.*x:Key="([^"]+)".*#\1\t\2#' \
  | sort -u > "$TMP_KEYS"

echo "[audit] scanning resource keys..."
while IFS=$'\t' read -r file key; do
  [[ -z "${key:-}" ]] && continue

  # Runtime-composed brush keys are used via Converter string interpolation.
  if [[ "$key" =~ ^Brush(Done|Danger|Warning|Info|Purple)Bg[0-9]+$ ]]; then
    continue
  fi

  # Total occurrences across axaml/cs. <=1 means definition-only.
  ref_count=$( (rg --glob "**/*.axaml" --glob "**/*.cs" -n -F "$key" "$ROOT" || true) \
    | wc -l | tr -d ' ')

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

# Collect class names from style selectors in shared styles.
TMP_STYLE_CLASSES=$(mktemp)
TMP_UNUSED_CLASSES=$(mktemp)
trap 'rm -f "$TMP_KEYS" "$TMP_UNUSED" "$TMP_STYLE_CLASSES" "$TMP_UNUSED_CLASSES"' EXIT

rg --glob "**/Styles/*.axaml" -n 'Selector="[^"]*\.[A-Za-z_][A-Za-z0-9_-]*' "$ROOT" \
  | sed -E 's#.*Selector="([^"]+)".*#\1#' \
  | tr ' ' '\n' \
  | sed -nE 's#.*\.([A-Za-z_][A-Za-z0-9_-]*).*#\1#p' \
  | sort -u > "$TMP_STYLE_CLASSES"

echo "[audit] scanning style classes..."
while IFS= read -r cls; do
  [[ -z "${cls:-}" ]] && continue

  ref_count=$(
    (
      rg --glob "**/*.axaml" -n "Classes=\"[^\"]*\\b${cls}\\b" "$ROOT" || true
      rg --glob "**/*.axaml" -n "Classes\\.${cls}=" "$ROOT" || true
      rg --glob "**/*.cs" -n "\"${cls}\"" "$ROOT" || true
    ) | wc -l | tr -d ' '
  )

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
