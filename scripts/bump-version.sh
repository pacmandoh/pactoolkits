#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
MANIFEST="$ROOT_DIR/release-manifest.json"

usage() {
  cat <<'USAGE'
Usage:
  bump-version.sh [--suite X.Y.Z] [--agent X.Y.Z] [--ui X.Y.Z] [--db X.Y.Z]
                  [--channel stable|beta|dev] [--date YYYY-MM-DD] [--dry-run]

Examples:
  bump-version.sh --suite 0.4.1 --ui 0.4.1 --agent 0.3.1
  bump-version.sh --db 1.2.1 --channel beta
USAGE
}

require_cmd() {
  command -v "$1" >/dev/null 2>&1 || {
    echo "ERROR: required command not found: $1" >&2
    exit 1
  }
}

is_semver() {
  [[ "$1" =~ ^[0-9]+\.[0-9]+\.[0-9]+$ ]]
}

is_date() {
  [[ "$1" =~ ^[0-9]{4}-[0-9]{2}-[0-9]{2}$ ]]
}

SUITE=""
AGENT=""
UI=""
DB=""
CHANNEL=""
DATE_STR="$(date -u +%F)"
DRY_RUN="false"

while [[ $# -gt 0 ]]; do
  case "$1" in
    --suite) SUITE="${2:-}"; shift 2 ;;
    --agent) AGENT="${2:-}"; shift 2 ;;
    --ui) UI="${2:-}"; shift 2 ;;
    --db) DB="${2:-}"; shift 2 ;;
    --channel) CHANNEL="${2:-}"; shift 2 ;;
    --date) DATE_STR="${2:-}"; shift 2 ;;
    --dry-run) DRY_RUN="true"; shift ;;
    -h|--help) usage; exit 0 ;;
    *) echo "ERROR: unknown arg: $1" >&2; usage; exit 1 ;;
  esac
done

require_cmd jq

[[ -f "$MANIFEST" ]] || {
  echo "ERROR: manifest not found: $MANIFEST" >&2
  exit 1
}

[[ -n "$SUITE" ]] && is_semver "$SUITE" || [[ -z "$SUITE" ]] || { echo "ERROR: invalid --suite" >&2; exit 1; }
[[ -n "$AGENT" ]] && is_semver "$AGENT" || [[ -z "$AGENT" ]] || { echo "ERROR: invalid --agent" >&2; exit 1; }
[[ -n "$UI" ]] && is_semver "$UI" || [[ -z "$UI" ]] || { echo "ERROR: invalid --ui" >&2; exit 1; }
[[ -n "$DB" ]] && is_semver "$DB" || [[ -z "$DB" ]] || { echo "ERROR: invalid --db" >&2; exit 1; }
is_date "$DATE_STR" || { echo "ERROR: invalid --date" >&2; exit 1; }

if [[ -n "$CHANNEL" ]]; then
  case "$CHANNEL" in
    stable|beta|dev) ;;
    *) echo "ERROR: --channel must be stable|beta|dev" >&2; exit 1 ;;
  esac
fi

if [[ -z "$SUITE$AGENT$UI$DB$CHANNEL" ]]; then
  echo "ERROR: nothing to update" >&2
  usage
  exit 1
fi

TMP_FILE="$(mktemp)"
trap 'rm -f "$TMP_FILE"' EXIT

jq \
  --arg suite "$SUITE" \
  --arg agent "$AGENT" \
  --arg ui "$UI" \
  --arg db "$DB" \
  --arg channel "$CHANNEL" \
  --arg date "$DATE_STR" \
  '
  .suiteVersion = (if $suite == "" then .suiteVersion else $suite end) |
  .agentVersion = (if $agent == "" then .agentVersion else $agent end) |
  .uiVersion = (if $ui == "" then .uiVersion else $ui end) |
  .dbSchemaVersion = (if $db == "" then .dbSchemaVersion else $db end) |
  .build.channel = (if $channel == "" then .build.channel else $channel end) |
  .build.date = $date
  ' "$MANIFEST" > "$TMP_FILE"

jq -e '
  .suiteVersion and .agentVersion and .uiVersion and .dbSchemaVersion and
  .compat.agentMinUi and .compat.uiMinAgent and .build.channel and .build.date
' "$TMP_FILE" >/dev/null

if [[ "$DRY_RUN" == "true" ]]; then
  echo "=== DRY RUN ==="
  diff -u "$MANIFEST" "$TMP_FILE" || true
  exit 0
fi

mv "$TMP_FILE" "$MANIFEST"

echo "Updated $MANIFEST"
jq '{suiteVersion, agentVersion, uiVersion, dbSchemaVersion, build}' "$MANIFEST"

"$ROOT_DIR/scripts/export-version.sh"
