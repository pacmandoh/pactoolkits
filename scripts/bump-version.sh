#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
MANIFEST="$ROOT_DIR/release-manifest.json"

usage() {
  cat <<'USAGE'
Usage:
  bump-version.sh [--suite X.Y.Z|auto] [--agent X.Y.Z] [--ui X.Y.Z] [--db X.Y.Z]
                  [--ui-min-db X.Y.Z] [--agent-min-db X.Y.Z]
                  [--channel stable|beta] [--date YYYY-MM-DD] [--dry-run]

Examples:
  bump-version.sh --suite 0.4.1 --ui 0.4.1 --agent 0.3.1
  bump-version.sh --suite auto --ui 0.4.4
  bump-version.sh --db 1.2.1 --channel beta
  bump-version.sh --ui-min-db 1.2.0 --agent-min-db 1.2.0
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

semver_bump_patch() {
  local ver="$1"
  IFS='.' read -r major minor patch <<< "$ver"
  printf '%s.%s.%s\n' "$major" "$minor" "$((patch + 1))"
}

semver_bump_minor() {
  local ver="$1"
  IFS='.' read -r major minor _patch <<< "$ver"
  printf '%s.%s.0\n' "$major" "$((minor + 1))"
}

semver_bump_major() {
  local ver="$1"
  IFS='.' read -r major _minor _patch <<< "$ver"
  printf '%s.0.0\n' "$((major + 1))"
}

# Return one of: none / patch / minor / major / downgrade
semver_change_level() {
  local old="$1"
  local new="$2"
  local oM oN oP nM nN nP
  IFS='.' read -r oM oN oP <<< "$old"
  IFS='.' read -r nM nN nP <<< "$new"

  if (( nM < oM )); then echo "downgrade"; return; fi
  if (( nM > oM )); then echo "major"; return; fi

  if (( nN < oN )); then echo "downgrade"; return; fi
  if (( nN > oN )); then echo "minor"; return; fi

  if (( nP < oP )); then echo "downgrade"; return; fi
  if (( nP > oP )); then echo "patch"; return; fi

  echo "none"
}

max_level() {
  local a="$1"
  local b="$2"
  local rank_a=0
  local rank_b=0
  case "$a" in
    patch) rank_a=1 ;;
    minor) rank_a=2 ;;
    major) rank_a=3 ;;
  esac
  case "$b" in
    patch) rank_b=1 ;;
    minor) rank_b=2 ;;
    major) rank_b=3 ;;
  esac
  if (( rank_b > rank_a )); then
    echo "$b"
  else
    echo "$a"
  fi
}

SUITE=""
AGENT=""
UI=""
DB=""
UI_MIN_DB=""
AGENT_MIN_DB=""
CHANNEL=""
DATE_STR="$(date -u +%F)"
DRY_RUN="false"

while [[ $# -gt 0 ]]; do
  case "$1" in
    --suite) SUITE="${2:-}"; shift 2 ;;
    --agent) AGENT="${2:-}"; shift 2 ;;
    --ui) UI="${2:-}"; shift 2 ;;
    --db) DB="${2:-}"; shift 2 ;;
    --ui-min-db) UI_MIN_DB="${2:-}"; shift 2 ;;
    --agent-min-db) AGENT_MIN_DB="${2:-}"; shift 2 ;;
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

if [[ -n "$SUITE" && "$SUITE" != "auto" ]]; then
  is_semver "$SUITE" || { echo "ERROR: invalid --suite (expect X.Y.Z or auto)" >&2; exit 1; }
fi
[[ -n "$AGENT" ]] && is_semver "$AGENT" || [[ -z "$AGENT" ]] || { echo "ERROR: invalid --agent" >&2; exit 1; }
[[ -n "$UI" ]] && is_semver "$UI" || [[ -z "$UI" ]] || { echo "ERROR: invalid --ui" >&2; exit 1; }
[[ -n "$DB" ]] && is_semver "$DB" || [[ -z "$DB" ]] || { echo "ERROR: invalid --db" >&2; exit 1; }
[[ -n "$UI_MIN_DB" ]] && is_semver "$UI_MIN_DB" || [[ -z "$UI_MIN_DB" ]] || { echo "ERROR: invalid --ui-min-db" >&2; exit 1; }
[[ -n "$AGENT_MIN_DB" ]] && is_semver "$AGENT_MIN_DB" || [[ -z "$AGENT_MIN_DB" ]] || { echo "ERROR: invalid --agent-min-db" >&2; exit 1; }
is_date "$DATE_STR" || { echo "ERROR: invalid --date" >&2; exit 1; }

if [[ -n "$CHANNEL" ]]; then
  case "$CHANNEL" in
    stable|beta) ;;
    *) echo "ERROR: --channel must be stable|beta" >&2; exit 1 ;;
  esac
fi

if [[ -z "$SUITE$AGENT$UI$DB$UI_MIN_DB$AGENT_MIN_DB$CHANNEL" ]]; then
  echo "ERROR: nothing to update" >&2
  usage
  exit 1
fi

current_suite="$(jq -r '.suiteVersion' "$MANIFEST")"
current_agent="$(jq -r '.agentVersion' "$MANIFEST")"
current_ui="$(jq -r '.uiVersion' "$MANIFEST")"
current_db="$(jq -r '.dbSchemaVersion' "$MANIFEST")"
is_semver "$current_suite" || { echo "ERROR: manifest suiteVersion is not semver: $current_suite" >&2; exit 1; }
is_semver "$current_agent" || { echo "ERROR: manifest agentVersion is not semver: $current_agent" >&2; exit 1; }
is_semver "$current_ui" || { echo "ERROR: manifest uiVersion is not semver: $current_ui" >&2; exit 1; }
is_semver "$current_db" || { echo "ERROR: manifest dbSchemaVersion is not semver: $current_db" >&2; exit 1; }

agent_level="none"
ui_level="none"
db_level="none"
if [[ -n "$AGENT" ]]; then
  agent_level="$(semver_change_level "$current_agent" "$AGENT")"
  [[ "$agent_level" != "downgrade" ]] || { echo "ERROR: --agent cannot downgrade ($current_agent -> $AGENT)" >&2; exit 1; }
fi
if [[ -n "$UI" ]]; then
  ui_level="$(semver_change_level "$current_ui" "$UI")"
  [[ "$ui_level" != "downgrade" ]] || { echo "ERROR: --ui cannot downgrade ($current_ui -> $UI)" >&2; exit 1; }
fi
if [[ -n "$DB" ]]; then
  db_level="$(semver_change_level "$current_db" "$DB")"
  [[ "$db_level" != "downgrade" ]] || { echo "ERROR: --db cannot downgrade ($current_db -> $DB)" >&2; exit 1; }
fi

suite_auto_level="none"
suite_auto_level="$(max_level "$suite_auto_level" "$agent_level")"
suite_auto_level="$(max_level "$suite_auto_level" "$ui_level")"
suite_auto_level="$(max_level "$suite_auto_level" "$db_level")"

if [[ "$SUITE" == "auto" ]]; then
  case "$suite_auto_level" in
    major) SUITE="$(semver_bump_major "$current_suite")" ;;
    minor) SUITE="$(semver_bump_minor "$current_suite")" ;;
    patch) SUITE="$(semver_bump_patch "$current_suite")" ;;
    none) SUITE="$current_suite" ;;
  esac
elif [[ -z "$SUITE" && -n "$AGENT$UI$DB" ]]; then
  case "$suite_auto_level" in
    major) SUITE="$(semver_bump_major "$current_suite")" ;;
    minor) SUITE="$(semver_bump_minor "$current_suite")" ;;
    patch) SUITE="$(semver_bump_patch "$current_suite")" ;;
    none) SUITE="$current_suite" ;;
  esac
fi

TMP_FILE="$(mktemp)"
trap 'rm -f "$TMP_FILE"' EXIT

jq \
  --arg suite "$SUITE" \
  --arg agent "$AGENT" \
  --arg ui "$UI" \
  --arg db "$DB" \
  --arg ui_min_db "$UI_MIN_DB" \
  --arg agent_min_db "$AGENT_MIN_DB" \
  --arg channel "$CHANNEL" \
  --arg date "$DATE_STR" \
  '
  .suiteVersion = (if $suite == "" then .suiteVersion else $suite end) |
  .agentVersion = (if $agent == "" then .agentVersion else $agent end) |
  .uiVersion = (if $ui == "" then .uiVersion else $ui end) |
  .dbSchemaVersion = (if $db == "" then .dbSchemaVersion else $db end) |
  .compat.uiMinDbSchema = (if $ui_min_db == "" then .compat.uiMinDbSchema else $ui_min_db end) |
  .compat.agentMinDbSchema = (if $agent_min_db == "" then .compat.agentMinDbSchema else $agent_min_db end) |
  .build.channel = (if $channel == "" then .build.channel else $channel end) |
  .build.date = $date
  ' "$MANIFEST" > "$TMP_FILE"

jq -e '
  .suiteVersion and .agentVersion and .uiVersion and .dbSchemaVersion and
  .compat.uiMinDbSchema and .compat.agentMinDbSchema and
  .build.channel and .build.date
' "$TMP_FILE" >/dev/null

if [[ "$DRY_RUN" == "true" ]]; then
  echo "=== DRY RUN ==="
  diff -u "$MANIFEST" "$TMP_FILE" || true
  exit 0
fi

mv "$TMP_FILE" "$MANIFEST"

echo "Updated $MANIFEST"
jq '{suiteVersion, agentVersion, uiVersion, dbSchemaVersion, compat, build}' "$MANIFEST"

"$ROOT_DIR/scripts/export-version.sh"
