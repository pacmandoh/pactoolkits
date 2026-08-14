#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
# shellcheck source=manifest-v2.sh
source "$ROOT_DIR/scripts/manifest-v2.sh"

MANIFEST="$ROOT_DIR/release-manifest.json"
DESKTOP_AVALONIA_DIR="$ROOT_DIR/apps/desktop-avalonia/src"
API_DIR="$ROOT_DIR/apps/api-asp/src"

require_cmd() {
  command -v "$1" > /dev/null 2>&1 || {
    echo "ERROR: required command not found: $1" >&2
    exit 1
  }
}

require_cmd jq

[[ -f "$MANIFEST" ]] || {
  echo "ERROR: manifest not found: $MANIFEST" >&2
  exit 1
}

validate_manifest_v2 "$MANIFEST"

# 生成 Version.g.props（AppVersion、AssemblyVersion、InformationalVersion）
write_version_g_props() {
  local dest="$1"
  local version="$2"
  local channel="$3"
  local date="$4"
  local assembly
  assembly="$(semver_stable_base "$version")"
  cat > "$dest" << XML
<Project>
  <PropertyGroup>
    <AppVersion>$version</AppVersion>
    <Version>$version</Version>
    <AssemblyVersion>${assembly}.0</AssemblyVersion>
    <FileVersion>${assembly}.0</FileVersion>
    <InformationalVersion>${version}+${channel}.${date}</InformationalVersion>
  </PropertyGroup>
</Project>
XML
}

desktop_version="$(manifest_desktop_version "$MANIFEST")"
build_channel="$(manifest_release_channel "$MANIFEST")"
build_date="$(manifest_release_date "$MANIFEST")"

mkdir -p "$DESKTOP_AVALONIA_DIR"
write_version_g_props "$DESKTOP_AVALONIA_DIR/Version.g.props" "$desktop_version" "$build_channel" "$build_date"
# Desktop 旁清单：product、desktop、agents.version、database、release
write_desktop_release_manifest "$MANIFEST" "$DESKTOP_AVALONIA_DIR/ReleaseManifest.json"

api_version="$(manifest_api_version "$MANIFEST")"
api_contract="$(manifest_api_contract_version "$MANIFEST")"
api_min_db="$(manifest_api_min_db "$MANIFEST")"
api_max_db="$(manifest_api_max_db "$MANIFEST")"
mkdir -p "$API_DIR/Hosting"

write_version_g_props "$API_DIR/Version.g.props" "$api_version" "$build_channel" "$build_date"

cat > "$API_DIR/Hosting/ApiContract.g.cs" << CS
namespace PacToolkits.Api.Hosting;

/// <summary>
/// HTTP 协议版本（SemVer）
///
/// 由清单 components.api.contractVersion 生成
/// 客户端以该常量做协议兼容判断；PacAPI 程序版本与 SchemaBounds 不参与
/// </summary>
public static class ApiContract
{
    public const string Version = "$api_contract";
}
CS

cat > "$API_DIR/Hosting/SchemaBounds.g.cs" << CS
namespace PacToolkits.Api.Hosting;

/// <summary>
/// 宿主 SchemaBounds 默认闭区间
///
/// 由清单 components.api 的 min/maxDbSchema 生成
/// </summary>
internal static class SchemaBoundsManifest
{
    public const string MinDbSchema = "$api_min_db";
    public const string MaxDbSchema = "$api_max_db";
}
CS

api_appsettings="$API_DIR/appsettings.json"
if [[ -f "$api_appsettings" ]]; then
  tmp="$(mktemp)"
  jq --arg min "$api_min_db" --arg max "$api_max_db" \
    '.SchemaBounds.MinDbSchema = $min | .SchemaBounds.MaxDbSchema = $max' \
    "$api_appsettings" > "$tmp"
  chmod 644 "$tmp"
  mv "$tmp" "$api_appsettings"
else
  echo "ERROR: missing $api_appsettings" >&2
  exit 1
fi

agents_version="$(manifest_agents_version "$MANIFEST")"

while IFS= read -r component_id; do
  component_id="${component_id//$'\r'/}"
  [[ -n "$component_id" ]] || continue
  agents_dir="$ROOT_DIR/$(manifest_agents_source_dir "$component_id")"
  mkdir -p "$agents_dir"
  write_agents_release_manifest "$MANIFEST" "$agents_dir/ReleaseManifest.json"
  write_version_g_props "$agents_dir/Version.g.props" "$agents_version" "$build_channel" "$build_date"
done < <(manifest_agents_component_ids "$MANIFEST")

while IFS= read -r module_id; do
  module_id="${module_id//$'\r'/}"
  [[ -n "$module_id" ]] || continue
  module_dir="$ROOT_DIR/$(manifest_agents_module_source_dir "$module_id")"
  mkdir -p "$module_dir"

  module_json="$module_dir/module.json"
  module_version="$(manifest_agents_module_version "$MANIFEST" "$module_id")"
  [[ -n "$module_version" ]] || {
    echo "ERROR: missing version for agents.modules.$module_id" >&2
    exit 1
  }
  module_min_c="$(manifest_agents_module_min_api_contract "$MANIFEST" "$module_id")"
  module_max_c="$(manifest_agents_module_max_api_contract "$MANIFEST" "$module_id")"
  if [[ -f "$module_json" ]]; then
    tmp="$(mktemp)"
    # 与清单成对：有 min/max 则写入，否则从 module.json 去掉这两项；顺带清掉旧 min/maxDbSchema
    if [[ -n "$module_min_c" && -n "$module_max_c" ]]; then
      jq --arg id "$module_id" --arg ver "$module_version" \
        --arg min "$module_min_c" --arg max "$module_max_c" \
        '.id = $id | .version = $ver | .minApiContract = $min | .maxApiContract = $max | del(.minDbSchema, .maxDbSchema)' \
        "$module_json" > "$tmp"
    else
      jq --arg id "$module_id" --arg ver "$module_version" \
        '.id = $id | .version = $ver | del(.minApiContract, .maxApiContract, .minDbSchema, .maxDbSchema)' \
        "$module_json" > "$tmp"
    fi
    chmod 644 "$tmp"
    mv "$tmp" "$module_json"
  else
    echo "ERROR: missing module.json for $module_id at $module_json" >&2
    exit 1
  fi
done < <(manifest_agents_module_ids "$MANIFEST")

echo "Exported version artifacts:"
echo "- $DESKTOP_AVALONIA_DIR/Version.g.props"
echo "- $DESKTOP_AVALONIA_DIR/ReleaseManifest.json"
echo "- $API_DIR/Version.g.props"
echo "- $API_DIR/Hosting/ApiContract.g.cs"
echo "- $API_DIR/Hosting/SchemaBounds.g.cs"
echo "- $API_DIR/appsettings.json (SchemaBounds)"
while IFS= read -r component_id; do
  component_id="${component_id//$'\r'/}"
  [[ -n "$component_id" ]] || continue
  echo "- $ROOT_DIR/$(manifest_agents_source_dir "$component_id")/ReleaseManifest.json"
  echo "- $ROOT_DIR/$(manifest_agents_source_dir "$component_id")/Version.g.props"
done < <(manifest_agents_component_ids "$MANIFEST")
while IFS= read -r module_id; do
  module_id="${module_id//$'\r'/}"
  [[ -n "$module_id" ]] || continue
  echo "- $ROOT_DIR/$(manifest_agents_module_source_dir "$module_id")/module.json"
done < <(manifest_agents_module_ids "$MANIFEST")
