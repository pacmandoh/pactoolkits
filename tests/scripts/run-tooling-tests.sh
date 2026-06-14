#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
# shellcheck source=manifest-v2.sh
source "$ROOT_DIR/scripts/manifest-v2.sh"

run() {
  echo "+ $*"
  "$@"
}

cd "$ROOT_DIR"
chmod +x scripts/*.sh tests/scripts/*.sh

run ./scripts/export-version.sh
run ./scripts/check-version.sh

eval "$(./scripts/resolve-release-plan.sh "$ROOT_DIR/release-manifest.json" | sed 's/^\([^=]*\)=\(.*\)$/export \1=\2/')"
[[ "${implementation:-}" == "avalonia" ]] || {
  echo "ERROR: expected default implementation=avalonia, got: ${implementation:-<empty>}" >&2
  exit 1
}
[[ "${desktop_artifact_name:-}" == pactoolkits-desktop-avalonia-win-x64-* ]] || {
  echo "ERROR: unexpected avalonia artifact name: ${desktop_artifact_name:-<empty>}" >&2
  exit 1
}

electron_manifest="$(mktemp)"
trap 'rm -f "$electron_manifest"' EXIT
jq '.components.desktop.implementation = "electron"' "$ROOT_DIR/release-manifest.json" > "$electron_manifest"
validate_manifest_v2 "$electron_manifest"
eval "$(./scripts/resolve-release-plan.sh "$electron_manifest" | sed 's/^\([^=]*\)=\(.*\)$/export \1=\2/')"
[[ "${implementation:-}" == "electron" ]] || {
  echo "ERROR: expected electron implementation plan" >&2
  exit 1
}
[[ "${desktop_artifact_name:-}" == pactoolkits-desktop-electron-win-x64-* ]] || {
  echo "ERROR: unexpected electron artifact name: ${desktop_artifact_name:-<empty>}" >&2
  exit 1
}

beta_manifest="$(mktemp)"
trap 'rm -f "$electron_manifest" "$beta_manifest"' EXIT
jq '.release.channel = "beta"' "$ROOT_DIR/release-manifest.json" > "$beta_manifest"
validate_manifest_v2 "$beta_manifest"
db_version="$(jq -r '.components["database-postgres"].version' "$beta_manifest")"
desktop_min_db="$(jq -r '.components.desktop.minDbSchema' "$beta_manifest")"
agent_min_db="$(jq -r '.components["agent-injector-ahk"].minDbSchema' "$beta_manifest")"
[[ "$desktop_min_db" == "$db_version" ]] || {
  echo "ERROR: beta channel desktop.minDbSchema must track database-postgres.version" >&2
  exit 1
}
[[ "$agent_min_db" == "$db_version" ]] || {
  echo "ERROR: beta channel agent minDbSchema must track database-postgres.version" >&2
  exit 1
}

invalid_manifest="$(mktemp)"
jq '.components.desktop.bundles += ["missing-agent"]' "$ROOT_DIR/release-manifest.json" > "$invalid_manifest"
if validate_manifest_v2 "$invalid_manifest" >/dev/null 2>&1; then
  echo "ERROR: manifest validation should reject invalid bundle references" >&2
  exit 1
fi

invalid_component_manifest="$(mktemp)"
trap 'rm -f "$electron_manifest" "$beta_manifest" "$invalid_manifest" "$invalid_component_manifest"' EXIT
jq '.components = ({"bad-component": {"version": "not-semver"}} + .components)' "$ROOT_DIR/release-manifest.json" > "$invalid_component_manifest"
if validate_manifest_v2 "$invalid_component_manifest" >/dev/null 2>&1; then
  echo "ERROR: manifest validation should reject invalid component semver anywhere in components" >&2
  exit 1
fi

invalid_bundle_version_manifest="$(mktemp)"
trap 'rm -f "$electron_manifest" "$beta_manifest" "$invalid_manifest" "$invalid_component_manifest" "$invalid_bundle_version_manifest"' EXIT
jq '.components["agent-injector-ahk"].version = "not-semver"' "$ROOT_DIR/release-manifest.json" > "$invalid_bundle_version_manifest"
if validate_manifest_v2 "$invalid_bundle_version_manifest" >/dev/null 2>&1; then
  echo "ERROR: manifest validation should reject invalid bundled component versions" >&2
  exit 1
fi

run ./scripts/build-electron-preview.sh
preview_zip="$ROOT_DIR/artifacts/desktop/electron-preview/win-x64/pactoolkits-desktop-electron-preview-win-x64-$(manifest_product_version "$ROOT_DIR/release-manifest.json").zip"
[[ -f "$preview_zip" ]] || {
  echo "ERROR: electron preview zip missing: $preview_zip" >&2
  exit 1
}

current_agent="$(manifest_agent_injector_ahk_version "$ROOT_DIR/release-manifest.json")"
IFS='.' read -r agent_major agent_minor agent_patch <<< "$current_agent"
next_agent="${agent_major}.${agent_minor}.$((agent_patch + 1))"
agent_plan_out="$(./scripts/release-agent-injector-ahk.sh --bump-agent "$next_agent" --dry-run 2>&1)"
echo "$agent_plan_out" | grep -Fq "agent-injector-ahk.version: $next_agent" || {
  echo "ERROR: dry-run release plan should reflect bumped agent version ($next_agent)" >&2
  exit 1
}
echo "$agent_plan_out" | grep -Fq "pactoolkits-injector-win-x64-${next_agent}-" || {
  echo "ERROR: dry-run artifact name should reflect bumped agent version ($next_agent)" >&2
  exit 1
}

desktop_plan_out="$(./scripts/release-desktop.sh --bump-desktop 9.9.9 --dry-run --skip-upload 2>&1)"
echo "$desktop_plan_out" | grep -Fq "desktop.version: 9.9.9" || {
  echo "ERROR: dry-run desktop release plan should reflect bumped desktop version (9.9.9)" >&2
  exit 1
}

conflict_out="$(./scripts/release-agent-injector-ahk.sh --bump-agent 0.6.2 --bump-component agent-injector-ahk=0.6.3 --dry-run 2>&1)" && {
  echo "ERROR: release script should reject conflicting agent bump flags" >&2
  exit 1
}
echo "$conflict_out" | grep -Fq "conflicting agent version" || {
  echo "ERROR: expected conflicting agent version error message" >&2
  exit 1
}

bump_conflict_out="$(./scripts/bump-version.sh --agent 0.6.2 --component agent-injector-ahk=0.6.3 --dry-run 2>&1)" && {
  echo "ERROR: bump-version.sh should reject duplicate agent component updates" >&2
  exit 1
}
echo "$bump_conflict_out" | grep -Fq "conflicting component version for agent-injector-ahk" || {
  echo "ERROR: expected bump-version duplicate agent component error message" >&2
  exit 1
}

bump_duplicate_out="$(./scripts/bump-version.sh --component agent-injector-ahk=0.6.2 --component agent-injector-ahk=0.6.3 --dry-run 2>&1)" && {
  echo "ERROR: bump-version.sh should reject repeated --component for the same id" >&2
  exit 1
}
echo "$bump_duplicate_out" | grep -Fq "conflicting component version for agent-injector-ahk" || {
  echo "ERROR: expected bump-version repeated --component error message" >&2
  exit 1
}

product_version="$(manifest_product_version "$ROOT_DIR/release-manifest.json")"
if validate_release_tag_matches_product_version "v${product_version}" "$ROOT_DIR/release-manifest.json"; then
  :
else
  echo "ERROR: matching release tag validation should succeed" >&2
  exit 1
fi
if validate_release_tag_matches_product_version "v9.9.9" "$ROOT_DIR/release-manifest.json" >/dev/null 2>&1; then
  echo "ERROR: release tag validation should reject manifest mismatch" >&2
  exit 1
fi
RELEASE_TAG="v9.9.9" ./scripts/resolve-release-plan.sh "$ROOT_DIR/release-manifest.json" >/dev/null 2>&1 && {
  echo "ERROR: resolve-release-plan should reject mismatched RELEASE_TAG" >&2
  exit 1
}

current_db="$(manifest_database_postgres_version "$ROOT_DIR/release-manifest.json")"
desktop_min_db_conflict_out="$(./scripts/bump-version.sh --desktop-min-db "$current_db" --component-min-db "desktop=9.9.9" --dry-run 2>&1)" && {
  echo "ERROR: bump-version.sh should reject conflicting desktop minDbSchema flags" >&2
  exit 1
}
echo "$desktop_min_db_conflict_out" | grep -Fq "conflicting desktop minDbSchema" || {
  echo "ERROR: expected bump-version desktop minDbSchema conflict error message" >&2
  exit 1
}

echo "Tooling tests passed."
