#!/usr/bin/env bash
set -euo pipefail

# Shared jq helpers for release-manifest.json schema v2.

manifest_schema_version() {
  jq -r '.schemaVersion // 1' "$1"
}

manifest_product_version() {
  jq -r '.product.version // .suiteVersion // empty' "$1"
}

manifest_desktop_version() {
  jq -r '.components.desktop.version // .uiVersion // empty' "$1"
}

manifest_desktop_implementation() {
  jq -r '.components.desktop.implementation // "avalonia"' "$1"
}

manifest_desktop_min_db() {
  jq -r '.components.desktop.minDbSchema // .compat.uiMinDbSchema // empty' "$1"
}

manifest_agent_injector_ahk_version() {
  jq -r '.components["agent-injector-ahk"].version // .agentVersion // empty' "$1"
}

manifest_agent_injector_ahk_min_db() {
  jq -r '.components["agent-injector-ahk"].minDbSchema // .compat.agentMinDbSchema // empty' "$1"
}

manifest_database_postgres_version() {
  jq -r '.components["database-postgres"].version // .dbSchemaVersion // empty' "$1"
}

manifest_release_channel() {
  jq -r '.release.channel // .build.channel // empty' "$1"
}

manifest_release_date() {
  jq -r '.release.date // .build.date // empty' "$1"
}

manifest_agent_component_ids() {
  jq -r '
    .components
    | to_entries[]
    | select(.value.artifact["windows-x64"] != null)
    | .key
  ' "$1"
}

manifest_agent_source_dir() {
  local component_id="$1"
  local suffix="${component_id#agent-}"
  printf 'runtime/agents/%s\n' "$suffix"
}

validate_manifest_v2() {
  local manifest="$1"
  local schema
  schema="$(manifest_schema_version "$manifest")"
  if [[ "$schema" != "2" ]]; then
    echo "ERROR: unsupported manifest schemaVersion=$schema (expected 2)" >&2
    return 1
  fi

  jq -e '
    def semver: test("^[0-9]+\\.[0-9]+\\.[0-9]+$");
    def date: test("^[0-9]{4}-[0-9]{2}-[0-9]{2}$");
    . as $root |
    $root.schemaVersion == 2 and
    $root.product.id == "pactoolkits" and
    ($root.product.version | semver) and
    $root.components.desktop.version and
    ($root.components.desktop.version | semver) and
    ($root.components.desktop.implementation | IN("avalonia", "electron")) and
    $root.components.desktop.packageId == "pactoolkits" and
    ($root.components.desktop.minDbSchema | semver) and
    ($root.components.desktop.bundles | type == "array") and
    ($root.components.desktop.bundles | length > 0) and
    ($root.components["database-postgres"].version | semver) and
    ($root.release.channel | IN("stable", "beta")) and
    ($root.release.date | date) and
    (
      $root.components.desktop.bundles
      | all(
          . as $bundle |
          $root.components[$bundle] as $component |
          ($component != null) and
          ($component.version | semver) and
          ($component.minDbSchema | semver) and
          ($component.artifact["windows-x64"] | type == "string" and length > 0) and
          (($component.artifact.installDir? // $bundle) | type == "string" and length > 0)
        )
    ) and
    (
      $root.components
      | to_entries
      | all(
          (.value.version? // null) == null
          or (.value.version | semver)
        )
    )
  ' "$manifest" >/dev/null || {
    echo "ERROR: manifest validation failed for $manifest" >&2
    return 1
  }
}

release_tag_version() {
  local tag="${1:-}"
  tag="${tag#refs/tags/}"
  [[ "$tag" == v* ]] || return 1
  local version="${tag#v}"
  [[ "$version" =~ ^[0-9]+\.[0-9]+\.[0-9]+$ ]] || return 1
  printf '%s' "$version"
}

validate_release_tag_matches_product_version() {
  local tag="${1:-}"
  local manifest="$2"
  [[ -n "$tag" ]] || return 0

  local tag_version product_version
  tag_version="$(release_tag_version "$tag")" || {
    echo "ERROR: invalid release tag format (expected vX.Y.Z): $tag" >&2
    return 1
  }
  product_version="$(manifest_product_version "$manifest")"
  [[ -n "$product_version" ]] || {
    echo "ERROR: manifest product.version is empty: $manifest" >&2
    return 1
  }
  if [[ "$tag_version" != "$product_version" ]]; then
    echo "ERROR: release tag ($tag) does not match manifest product.version ($product_version)" >&2
    return 1
  fi
}
