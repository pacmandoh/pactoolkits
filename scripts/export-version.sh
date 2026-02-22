#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
MANIFEST="$ROOT_DIR/release-manifest.json"
UI_DIR="$ROOT_DIR/pactoolkits-ui"
AGENT_DIR="$ROOT_DIR/pactoolkits-agent"

require_cmd() {
  command -v "$1" >/dev/null 2>&1 || {
    echo "ERROR: required command not found: $1" >&2
    exit 1
  }
}

require_cmd jq

[[ -f "$MANIFEST" ]] || {
  echo "ERROR: manifest not found: $MANIFEST" >&2
  exit 1
}

suite_version="$(jq -r '.suiteVersion' "$MANIFEST")"
agent_version="$(jq -r '.agentVersion' "$MANIFEST")"
ui_version="$(jq -r '.uiVersion' "$MANIFEST")"
db_schema_version="$(jq -r '.dbSchemaVersion' "$MANIFEST")"
ui_min_db_schema="$(jq -r '.compat.uiMinDbSchema' "$MANIFEST")"
agent_min_db_schema="$(jq -r '.compat.agentMinDbSchema' "$MANIFEST")"
build_channel="$(jq -r '.build.channel' "$MANIFEST")"
build_date="$(jq -r '.build.date' "$MANIFEST")"

mkdir -p "$UI_DIR" "$AGENT_DIR"

cat > "$UI_DIR/Version.g.props" <<XML
<Project>
  <PropertyGroup>
    <AppVersion>$ui_version</AppVersion>
    <Version>$ui_version</Version>
    <AssemblyVersion>${ui_version}.0</AssemblyVersion>
    <FileVersion>${ui_version}.0</FileVersion>
    <InformationalVersion>${ui_version}+${build_channel}.${build_date}</InformationalVersion>
  </PropertyGroup>
</Project>
XML

cat > "$UI_DIR/version.generated.json" <<JSON
{
  "suiteVersion": "$suite_version",
  "agentVersion": "$agent_version",
  "uiVersion": "$ui_version",
  "dbSchemaVersion": "$db_schema_version",
  "compat": {
    "uiMinDbSchema": "$ui_min_db_schema",
    "agentMinDbSchema": "$agent_min_db_schema"
  },
  "build": {
    "channel": "$build_channel",
    "date": "$build_date"
  }
}
JSON

cat > "$AGENT_DIR/version.generated.json" <<JSON
{
  "suiteVersion": "$suite_version",
  "agentVersion": "$agent_version",
  "uiVersion": "$ui_version",
  "dbSchemaVersion": "$db_schema_version",
  "compat": {
    "uiMinDbSchema": "$ui_min_db_schema",
    "agentMinDbSchema": "$agent_min_db_schema"
  },
  "build": {
    "channel": "$build_channel",
    "date": "$build_date"
  }
}
JSON

echo "Exported version artifacts:"
echo "- $UI_DIR/Version.g.props"
echo "- $UI_DIR/version.generated.json"
echo "- $AGENT_DIR/version.generated.json"
