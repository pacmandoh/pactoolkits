#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
MANIFEST="$ROOT_DIR/release-manifest.json"
UI_PROPS="$ROOT_DIR/apps/desktop-avalonia/src/Version.g.props"
UI_JSON="$ROOT_DIR/apps/desktop-avalonia/src/version.generated.json"
AGENT_JSON="$ROOT_DIR/runtime/agent-ahk/version.generated.json"

require_cmd() {
  command -v "$1" >/dev/null 2>&1 || {
    echo "ERROR: required command not found: $1" >&2
    exit 1
  }
}

require_cmd jq

[[ -f "$MANIFEST" ]] || { echo "ERROR: missing $MANIFEST" >&2; exit 1; }
[[ -f "$UI_PROPS" ]] || { echo "ERROR: missing $UI_PROPS (run scripts/export-version.sh)" >&2; exit 1; }
[[ -f "$UI_JSON" ]] || { echo "ERROR: missing $UI_JSON (run scripts/export-version.sh)" >&2; exit 1; }
[[ -f "$AGENT_JSON" ]] || { echo "ERROR: missing $AGENT_JSON (run scripts/export-version.sh)" >&2; exit 1; }

manifest_ui="$(jq -r '.uiVersion' "$MANIFEST")"
manifest_agent="$(jq -r '.agentVersion' "$MANIFEST")"
manifest_suite="$(jq -r '.suiteVersion' "$MANIFEST")"
manifest_db="$(jq -r '.dbSchemaVersion' "$MANIFEST")"
manifest_ui_min_db="$(jq -r '.compat.uiMinDbSchema' "$MANIFEST")"
manifest_agent_min_db="$(jq -r '.compat.agentMinDbSchema' "$MANIFEST")"

if command -v rg >/dev/null 2>&1; then
  ui_props_app="$(rg -o "<AppVersion>[^<]+</AppVersion>" "$UI_PROPS" | sed -E 's#<AppVersion>([^<]+)</AppVersion>#\1#')"
else
  ui_props_app="$(grep -oE "<AppVersion>[^<]+</AppVersion>" "$UI_PROPS" | sed -E 's#<AppVersion>([^<]+)</AppVersion>#\1#')"
fi
ui_json_ui="$(jq -r '.uiVersion' "$UI_JSON")"
ui_json_agent="$(jq -r '.agentVersion' "$UI_JSON")"
ui_json_suite="$(jq -r '.suiteVersion' "$UI_JSON")"
ui_json_db="$(jq -r '.dbSchemaVersion' "$UI_JSON")"
ui_json_ui_min_db="$(jq -r '.compat.uiMinDbSchema' "$UI_JSON")"
ui_json_agent_min_db="$(jq -r '.compat.agentMinDbSchema' "$UI_JSON")"

agent_json_agent="$(jq -r '.agentVersion' "$AGENT_JSON")"
agent_json_ui="$(jq -r '.uiVersion' "$AGENT_JSON")"
agent_json_suite="$(jq -r '.suiteVersion' "$AGENT_JSON")"
agent_json_db="$(jq -r '.dbSchemaVersion' "$AGENT_JSON")"
agent_json_ui_min_db="$(jq -r '.compat.uiMinDbSchema' "$AGENT_JSON")"
agent_json_agent_min_db="$(jq -r '.compat.agentMinDbSchema' "$AGENT_JSON")"

[[ "$ui_props_app" == "$manifest_ui" ]] || { echo "ERROR: Version.g.props AppVersion=$ui_props_app != manifest uiVersion=$manifest_ui" >&2; exit 1; }
[[ "$ui_json_ui" == "$manifest_ui" ]] || { echo "ERROR: ui/version.generated.json uiVersion mismatch" >&2; exit 1; }
[[ "$ui_json_agent" == "$manifest_agent" ]] || { echo "ERROR: ui/version.generated.json agentVersion mismatch" >&2; exit 1; }
[[ "$ui_json_suite" == "$manifest_suite" ]] || { echo "ERROR: ui/version.generated.json suiteVersion mismatch" >&2; exit 1; }
[[ "$ui_json_db" == "$manifest_db" ]] || { echo "ERROR: ui/version.generated.json dbSchemaVersion mismatch" >&2; exit 1; }
[[ "$ui_json_ui_min_db" == "$manifest_ui_min_db" ]] || { echo "ERROR: ui/version.generated.json compat.uiMinDbSchema mismatch" >&2; exit 1; }
[[ "$ui_json_agent_min_db" == "$manifest_agent_min_db" ]] || { echo "ERROR: ui/version.generated.json compat.agentMinDbSchema mismatch" >&2; exit 1; }

[[ "$agent_json_agent" == "$manifest_agent" ]] || { echo "ERROR: agent/version.generated.json agentVersion mismatch" >&2; exit 1; }
[[ "$agent_json_ui" == "$manifest_ui" ]] || { echo "ERROR: agent/version.generated.json uiVersion mismatch" >&2; exit 1; }
[[ "$agent_json_suite" == "$manifest_suite" ]] || { echo "ERROR: agent/version.generated.json suiteVersion mismatch" >&2; exit 1; }
[[ "$agent_json_db" == "$manifest_db" ]] || { echo "ERROR: agent/version.generated.json dbSchemaVersion mismatch" >&2; exit 1; }
[[ "$agent_json_ui_min_db" == "$manifest_ui_min_db" ]] || { echo "ERROR: agent/version.generated.json compat.uiMinDbSchema mismatch" >&2; exit 1; }
[[ "$agent_json_agent_min_db" == "$manifest_agent_min_db" ]] || { echo "ERROR: agent/version.generated.json compat.agentMinDbSchema mismatch" >&2; exit 1; }

echo "Version check passed."
echo "- suiteVersion: $manifest_suite"
echo "- uiVersion: $manifest_ui"
echo "- agentVersion: $manifest_agent"
echo "- dbSchemaVersion: $manifest_db"
echo "- uiMinDbSchema: $manifest_ui_min_db"
echo "- agentMinDbSchema: $manifest_agent_min_db"
