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

stable_fixture_manifest="$(mktemp)"
jq '
  .release.channel = "stable" |
  .components["database-postgres"].migrationPolicy = "stable-only" |
  .product.version = (
    if (.product.version | test("-beta\\.")) then
      (.product.version | sub("-beta\\.[0-9]+$"; ""))
    else .product.version end
  ) |
  .components.desktop.version = (
    if (.components.desktop.version | test("-beta\\.")) then
      (.components.desktop.version | sub("-beta\\.[0-9]+$"; ""))
    else .components.desktop.version end
  )
' "$ROOT_DIR/release-manifest.json" > "$stable_fixture_manifest"
validate_manifest_v2 "$stable_fixture_manifest"
live_release_channel="$(manifest_release_channel "$ROOT_DIR/release-manifest.json")"

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

invalid_implementation_manifest="$(mktemp)"
jq '.components.desktop.implementation = "hybrid"' "$ROOT_DIR/release-manifest.json" > "$invalid_implementation_manifest"
if validate_manifest_v2 "$invalid_implementation_manifest" >/dev/null 2>&1; then
  echo "ERROR: manifest validation should reject multiple or unknown desktop implementations" >&2
  exit 1
fi
rm -f "$invalid_implementation_manifest"

invalid_package_id_manifest="$(mktemp)"
jq '.components.desktop.packageId = "pactoolkits-beta"' "$ROOT_DIR/release-manifest.json" > "$invalid_package_id_manifest"
if validate_manifest_v2 "$invalid_package_id_manifest" >/dev/null 2>&1; then
  echo "ERROR: manifest validation should reject an unexpected desktop packageId" >&2
  exit 1
fi
rm -f "$invalid_package_id_manifest"

invalid_channel_manifest="$(mktemp)"
jq '.release.channel = "preview"' "$ROOT_DIR/release-manifest.json" > "$invalid_channel_manifest"
if validate_manifest_v2 "$invalid_channel_manifest" >/dev/null 2>&1; then
  echo "ERROR: manifest validation should reject an unsupported release channel" >&2
  exit 1
fi
rm -f "$invalid_channel_manifest"

beta_manifest="$(mktemp)"
trap 'rm -f "$electron_manifest" "$beta_manifest"' EXIT
jq '.release.channel = "beta" | .product.version = "0.17.1-beta.1" | .components.desktop.version = "0.17.1-beta.1"' "$stable_fixture_manifest" > "$beta_manifest"
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
if validate_release_tag_matches_product_version "v0.17.1-beta.1" "$beta_manifest"; then
  :
else
  echo "ERROR: beta release tag validation should succeed" >&2
  exit 1
fi
[[ "$(expected_release_prerelease "$beta_manifest")" == "true" ]] || {
  echo "ERROR: beta channel should require GitHub prerelease=true" >&2
  exit 1
}
validate_release_prerelease_flag "$beta_manifest" "true"
if validate_release_prerelease_flag "$beta_manifest" "false" >/dev/null 2>&1; then
  echo "ERROR: beta channel should reject prerelease=false" >&2
  exit 1
fi
beta_release_channel_plan="$(
  ./scripts/validate-release-channel.sh \
    --manifest "$beta_manifest" \
    --tag "v0.17.1-beta.1" \
    --prerelease true \
    --feed-root /feed/pactoolkits \
    --feed-target /feed/pactoolkits/beta \
    --dry-run false \
    --confirm true
)"
echo "$beta_release_channel_plan" | grep -Fq 'feed=/feed/pactoolkits/beta' || {
  echo "ERROR: beta release validation should resolve the beta feed" >&2
  exit 1
}
if ./scripts/validate-release-channel.sh \
  --manifest "$beta_manifest" \
  --tag "v0.17.1-beta.1" \
  --prerelease true \
  --feed-root /feed/pactoolkits \
  --feed-target /feed/pactoolkits/stable \
  --dry-run false \
  --confirm true >/dev/null 2>&1; then
  echo "ERROR: beta release validation should reject the stable feed target" >&2
  exit 1
fi

stable_beta_product_manifest="$(mktemp)"
jq '.release.channel = "stable" | .product.version = "0.17.1-beta.1"' "$stable_fixture_manifest" > "$stable_beta_product_manifest"
if validate_manifest_v2 "$stable_beta_product_manifest" >/dev/null 2>&1; then
  echo "ERROR: stable channel should reject beta product.version" >&2
  exit 1
fi

beta_stable_product_manifest="$(mktemp)"
jq '.release.channel = "beta"' "$stable_fixture_manifest" > "$beta_stable_product_manifest"
if validate_manifest_v2 "$beta_stable_product_manifest" >/dev/null 2>&1; then
  echo "ERROR: beta channel should reject stable-only product.version" >&2
  exit 1
fi

invalid_min_max_manifest="$(mktemp)"
jq '.components.desktop.maxDbSchema = "1.2.21"' "$ROOT_DIR/release-manifest.json" > "$invalid_min_max_manifest"
if validate_manifest_v2 "$invalid_min_max_manifest" >/dev/null 2>&1; then
  echo "ERROR: manifest validation should reject minDbSchema > maxDbSchema" >&2
  exit 1
fi

invalid_db_compat_manifest="$(mktemp)"
jq '.components["database-postgres"].version = "9.9.9"' "$ROOT_DIR/release-manifest.json" > "$invalid_db_compat_manifest"
if validate_manifest_v2 "$invalid_db_compat_manifest" >/dev/null 2>&1; then
  echo "ERROR: manifest validation should reject database-postgres.version outside component bounds" >&2
  exit 1
fi

invalid_migration_policy_manifest="$(mktemp)"
jq '.components["database-postgres"].migrationPolicy = "auto"' "$ROOT_DIR/release-manifest.json" > "$invalid_migration_policy_manifest"
if validate_manifest_v2 "$invalid_migration_policy_manifest" >/dev/null 2>&1; then
  echo "ERROR: manifest validation should reject invalid migrationPolicy" >&2
  exit 1
fi

stable_isolated_beta_manifest="$(mktemp)"
trap 'rm -f "$electron_manifest" "$beta_manifest" "$stable_beta_product_manifest" "$beta_stable_product_manifest" "$invalid_min_max_manifest" "$invalid_db_compat_manifest" "$invalid_migration_policy_manifest" "$stable_isolated_beta_manifest"' EXIT
jq '.components["database-postgres"].migrationPolicy = "isolated-beta"' "$stable_fixture_manifest" > "$stable_isolated_beta_manifest"
if validate_manifest_v2 "$stable_isolated_beta_manifest" >/dev/null 2>&1; then
  echo "ERROR: stable channel should reject isolated-beta migrationPolicy" >&2
  exit 1
fi

[[ "$(expected_release_prerelease "$stable_fixture_manifest")" == "false" ]] || {
  echo "ERROR: stable channel should require GitHub prerelease=false" >&2
  exit 1
}
validate_release_prerelease_flag "$stable_fixture_manifest" "false"

release_channel_plan="$(
  ./scripts/validate-release-channel.sh \
    --manifest "$stable_fixture_manifest" \
    --tag "v$(manifest_product_version "$stable_fixture_manifest")" \
    --prerelease false \
    --feed-root /feed/pactoolkits \
    --feed-target /feed/pactoolkits/stable \
    --dry-run false \
    --confirm true
)"
echo "$release_channel_plan" | grep -Fq 'feed=/feed/pactoolkits/stable' || {
  echo "ERROR: stable release validation should resolve the stable feed" >&2
  exit 1
}
if ./scripts/validate-release-channel.sh \
  --manifest "$stable_fixture_manifest" \
  --tag "v$(manifest_product_version "$stable_fixture_manifest")" \
  --prerelease false \
  --feed-root /feed/pactoolkits \
  --feed-target /feed/pactoolkits/beta \
  --dry-run false \
  --confirm true >/dev/null 2>&1; then
  echo "ERROR: stable release validation should reject the beta feed target" >&2
  exit 1
fi
if ./scripts/validate-release-channel.sh \
  --manifest "$stable_fixture_manifest" \
  --tag "v$(manifest_product_version "$stable_fixture_manifest")" \
  --prerelease false \
  --dry-run false \
  --confirm false >/dev/null 2>&1; then
  echo "ERROR: formal release validation should require confirm=true" >&2
  exit 1
fi

database_policy_base_manifest="$(mktemp)"
cp "$stable_fixture_manifest" "$database_policy_base_manifest"
./scripts/validate-database-policy.sh \
  --manifest "$stable_fixture_manifest" \
  --base-ref refs/heads/pactoolkits-missing-test-ref \
  --base-manifest "$database_policy_base_manifest" \
  --allow-beta-migration false >/dev/null

beta_db_follow_main_manifest="$(mktemp)"
jq '
  .release.channel = "beta" |
  .product.version = "0.18.0-beta.1" |
  .components.desktop.version = "0.18.0-beta.1" |
  .components["database-postgres"].migrationPolicy = "stable-only"
' "$stable_fixture_manifest" > "$beta_db_follow_main_manifest"
./scripts/validate-database-policy.sh \
  --manifest "$beta_db_follow_main_manifest" \
  --base-ref refs/heads/pactoolkits-missing-test-ref \
  --base-manifest "$database_policy_base_manifest" \
  --allow-beta-migration false >/dev/null

beta_db_isolated_follow_main_manifest="$(mktemp)"
jq '.components["database-postgres"].migrationPolicy = "isolated-beta"' \
  "$beta_db_follow_main_manifest" > "$beta_db_isolated_follow_main_manifest"
./scripts/validate-database-policy.sh \
  --manifest "$beta_db_isolated_follow_main_manifest" \
  --base-ref refs/heads/pactoolkits-missing-test-ref \
  --base-manifest "$database_policy_base_manifest" \
  --allow-beta-migration false >/dev/null

beta_db_upgrade_manifest="$(mktemp)"
jq '
  .release.channel = "beta" |
  .product.version = "0.18.0-beta.1" |
  .components.desktop.version = "0.18.0-beta.1" |
  .components.desktop.minDbSchema = "1.2.24" |
  .components.desktop.maxDbSchema = "1.2.24" |
  .components["agent-injector-ahk"].minDbSchema = "1.2.24" |
  .components["agent-injector-ahk"].maxDbSchema = "1.2.24" |
  .components["database-postgres"].version = "1.2.24"
' "$stable_fixture_manifest" > "$beta_db_upgrade_manifest"
if ./scripts/validate-database-policy.sh \
  --manifest "$beta_db_upgrade_manifest" \
  --base-ref refs/heads/pactoolkits-missing-test-ref \
  --base-manifest "$database_policy_base_manifest" \
  --allow-beta-migration false >/dev/null 2>&1; then
  echo "ERROR: ordinary Beta must not raise database-postgres.version above Stable" >&2
  exit 1
fi
jq '.components["database-postgres"].migrationPolicy = "isolated-beta"' \
  "$beta_db_upgrade_manifest" > "${beta_db_upgrade_manifest}.authorized"
./scripts/validate-database-policy.sh \
  --manifest "${beta_db_upgrade_manifest}.authorized" \
  --base-ref refs/heads/pactoolkits-missing-test-ref \
  --base-manifest "$database_policy_base_manifest" \
  --allow-beta-migration true >/dev/null

legacy_baseline_manifest="$(mktemp)"
cat > "$legacy_baseline_manifest" <<'EOF'
{
  "suiteVersion": "0.17.1",
  "dbSchemaVersion": "1.2.23",
  "build": { "channel": "stable" }
}
EOF
if [[ "$live_release_channel" == "beta" ]]; then
  ./scripts/validate-database-policy.sh \
    --manifest "$ROOT_DIR/release-manifest.json" \
    --base-ref refs/heads/pactoolkits-missing-test-ref \
    --base-manifest "$legacy_baseline_manifest" \
    --allow-beta-migration false >/dev/null
  jq '
    .components.desktop.minDbSchema = "1.2.24" |
    .components.desktop.maxDbSchema = "1.2.24" |
    .components["agent-injector-ahk"].minDbSchema = "1.2.24" |
    .components["agent-injector-ahk"].maxDbSchema = "1.2.24" |
    .components["database-postgres"].version = "1.2.24"
  ' "$ROOT_DIR/release-manifest.json" > "${legacy_baseline_manifest}.candidate"
  if ./scripts/validate-database-policy.sh \
    --manifest "${legacy_baseline_manifest}.candidate" \
    --base-ref refs/heads/pactoolkits-missing-test-ref \
    --base-manifest "$legacy_baseline_manifest" \
    --allow-beta-migration false >/dev/null 2>&1; then
    echo "ERROR: beta database policy should reject DB versions above the legacy stable/main baseline" >&2
    exit 1
  fi
fi
if grep -Fq 'beta' "$ROOT_DIR/apps/desktop-avalonia/src/Version.g.props" \
  && grep -Eq '<AssemblyVersion>[^<]*beta' "$ROOT_DIR/apps/desktop-avalonia/src/Version.g.props"; then
  echo "ERROR: AssemblyVersion must use numeric major.minor.build.revision only" >&2
  exit 1
fi

policy_git_dir="$(mktemp -d)"
git -C "$policy_git_dir" init -q
git -C "$policy_git_dir" config user.email tooling-tests@example.invalid
git -C "$policy_git_dir" config user.name tooling-tests
mkdir -p "$policy_git_dir/database/postgres/sql/migrations"
cp "$database_policy_base_manifest" "$policy_git_dir/release-manifest.json"
printf '%s\n' 'select 1;' > "$policy_git_dir/database/postgres/sql/migrations/V1_0_0__baseline.sql"
git -C "$policy_git_dir" add .
git -C "$policy_git_dir" commit -qm baseline
policy_base_ref="$(git -C "$policy_git_dir" rev-parse HEAD)"
printf '%s\n' 'select 2;' > "$policy_git_dir/database/postgres/sql/migrations/V1_0_0__baseline.sql"
git -C "$policy_git_dir" add .
git -C "$policy_git_dir" commit -qm modify-migration
if (
  cd "$policy_git_dir"
  "$ROOT_DIR/scripts/validate-database-policy.sh" \
    --manifest release-manifest.json \
    --base-ref "$policy_base_ref" \
    --allow-beta-migration false
) >/dev/null 2>&1; then
  echo "ERROR: database policy should reject modification of an existing migration" >&2
  exit 1
fi

legacy_reloc_git_dir="$(mktemp -d)"
git -C "$legacy_reloc_git_dir" init -q
git -C "$legacy_reloc_git_dir" config user.email tooling-tests@example.invalid
git -C "$legacy_reloc_git_dir" config user.name tooling-tests
cat > "$legacy_reloc_git_dir/release-manifest.json" <<'EOF'
{
  "suiteVersion": "0.17.1",
  "dbSchemaVersion": "1.2.23",
  "build": { "channel": "stable" }
}
EOF
mkdir -p "$legacy_reloc_git_dir/pactoolkits-db/sql/migrations"
cp "$ROOT_DIR/database/postgres/sql/migrations/V1_2_0__baseline.sql" \
  "$legacy_reloc_git_dir/pactoolkits-db/sql/migrations/V1_2_0__baseline.sql"
git -C "$legacy_reloc_git_dir" add .
git -C "$legacy_reloc_git_dir" commit -qm legacy-main
legacy_reloc_base_ref="$(git -C "$legacy_reloc_git_dir" rev-parse HEAD)"
mkdir -p "$legacy_reloc_git_dir/database/postgres/sql/migrations"
git -C "$legacy_reloc_git_dir" mv pactoolkits-db/sql/migrations/V1_2_0__baseline.sql \
  database/postgres/sql/migrations/V1_2_0__baseline.sql
jq '
  .release.channel = "beta" |
  .product.version = "1.0.0-beta.1" |
  .components.desktop.version = "1.0.0-beta.1" |
  .components["database-postgres"].migrationPolicy = "stable-only"
' "$stable_fixture_manifest" > "$legacy_reloc_git_dir/release-manifest.json"
git -C "$legacy_reloc_git_dir" add .
git -C "$legacy_reloc_git_dir" commit -qm monorepo-reloc
(
  cd "$legacy_reloc_git_dir"
  "$ROOT_DIR/scripts/validate-database-policy.sh" \
    --manifest release-manifest.json \
    --base-ref "$legacy_reloc_base_ref" \
    --allow-beta-migration false
) >/dev/null
rm -rf "$legacy_reloc_git_dir"

beta_new_migration_git_dir="$(mktemp -d)"
git -C "$beta_new_migration_git_dir" init -q
git -C "$beta_new_migration_git_dir" config user.email tooling-tests@example.invalid
git -C "$beta_new_migration_git_dir" config user.name tooling-tests
mkdir -p "$beta_new_migration_git_dir/database/postgres/sql/migrations"
cp "$database_policy_base_manifest" "$beta_new_migration_git_dir/release-manifest.json"
printf '%s\n' 'select 1;' > "$beta_new_migration_git_dir/database/postgres/sql/migrations/V1_0_0__baseline.sql"
git -C "$beta_new_migration_git_dir" add .
git -C "$beta_new_migration_git_dir" commit -qm baseline
beta_new_migration_base_ref="$(git -C "$beta_new_migration_git_dir" rev-parse HEAD)"
jq '
  .release.channel = "beta" |
  .product.version = "0.18.0-beta.1" |
  .components.desktop.version = "0.18.0-beta.1"
' "$database_policy_base_manifest" > "$beta_new_migration_git_dir/release-manifest.json"
printf '%s\n' \
  'create table if not exists app_environment_settings (' \
  '  environment text not null,' \
  '  setting_key text not null,' \
  '  setting_value jsonb not null default '"'"'{}'"'"'::jsonb,' \
  '  created_at timestamptz not null default now(),' \
  '  updated_at timestamptz not null default now(),' \
  '  primary key (environment, setting_key)' \
  ');' \
  > "$beta_new_migration_git_dir/database/postgres/sql/migrations/V1_2_23__app_environment_settings.sql"
git -C "$beta_new_migration_git_dir" add .
git -C "$beta_new_migration_git_dir" commit -qm add-beta-migration
if (
  cd "$beta_new_migration_git_dir"
  "$ROOT_DIR/scripts/validate-database-policy.sh" \
    --manifest release-manifest.json \
    --base-ref "$beta_new_migration_base_ref" \
    --allow-beta-migration false
) >/dev/null 2>&1; then
  echo "ERROR: beta channel should reject new SQL migration without explicit authorization" >&2
  exit 1
fi
jq '.components["database-postgres"].migrationPolicy = "isolated-beta"' \
  "$beta_new_migration_git_dir/release-manifest.json" > "$beta_new_migration_git_dir/release-manifest.authorized.json"
(
  cd "$beta_new_migration_git_dir"
  "$ROOT_DIR/scripts/validate-database-policy.sh" \
    --manifest release-manifest.authorized.json \
    --base-ref "$beta_new_migration_base_ref" \
    --allow-beta-migration true
) >/dev/null
rm -rf "$beta_new_migration_git_dir"
rm -rf "$policy_git_dir"
rm -f "$database_policy_base_manifest" "$beta_db_follow_main_manifest" "$beta_db_isolated_follow_main_manifest" "$beta_db_upgrade_manifest" "${beta_db_upgrade_manifest}.authorized" "$legacy_baseline_manifest" "${legacy_baseline_manifest}.candidate"

target_beta_manifest="$(mktemp)"
jq '
  .product.version = "0.18.0-beta.1" |
  .components.desktop.version = "0.18.0-beta.1" |
  .release.channel = "beta"
' "$stable_fixture_manifest" > "$target_beta_manifest"
validate_manifest_v2 "$target_beta_manifest"
if validate_release_tag_matches_product_version "v0.18.0-beta.1" "$target_beta_manifest"; then
  :
else
  echo "ERROR: target beta manifest tag validation should succeed" >&2
  exit 1
fi

beta_desktop_on_stable_manifest="$(mktemp)"
jq '.release.channel = "stable" | .components.desktop.version = "0.18.0-beta.1"' "$stable_fixture_manifest" > "$beta_desktop_on_stable_manifest"
if validate_manifest_v2 "$beta_desktop_on_stable_manifest" >/dev/null 2>&1; then
  echo "ERROR: stable channel should reject beta desktop.version" >&2
  exit 1
fi

stable_desktop_on_beta_manifest="$(mktemp)"
jq '.release.channel = "beta" | .product.version = "0.17.1-beta.1" | .components.desktop.version = "0.16.1"' "$stable_fixture_manifest" > "$stable_desktop_on_beta_manifest"
if validate_manifest_v2 "$stable_desktop_on_beta_manifest" >/dev/null 2>&1; then
  echo "ERROR: beta channel should reject stable-only desktop.version" >&2
  exit 1
fi

for bad_version in "01.2.3" "1.02.3" "1.2.03" "1.2.3-beta.01"; do
  if is_stable_semver "$bad_version" || is_beta_semver "$bad_version"; then
    echo "ERROR: strict semver should reject leading-zero version: $bad_version" >&2
    exit 1
  fi
done
leading_zero_manifest="$(mktemp)"
jq '.product.version = "01.2.3"' "$ROOT_DIR/release-manifest.json" > "$leading_zero_manifest"
if validate_manifest_v2 "$leading_zero_manifest" >/dev/null 2>&1; then
  echo "ERROR: manifest validation should reject leading-zero product.version" >&2
  exit 1
fi

beta_auto_expected="$(resolve_product_auto_version "$(manifest_product_version "$stable_fixture_manifest")" "beta" "none")"
manifest_backup="$(mktemp)"
cp "$ROOT_DIR/release-manifest.json" "$manifest_backup"
cp "$stable_fixture_manifest" "$ROOT_DIR/release-manifest.json"
beta_auto_out="$(./scripts/bump-version.sh --channel beta --product auto --dry-run 2>&1)"
cp "$manifest_backup" "$ROOT_DIR/release-manifest.json"
rm -f "$manifest_backup"
echo "$beta_auto_out" | grep -Fq "\"version\": \"$beta_auto_expected\"" || {
  echo "ERROR: --channel beta --product auto should produce $beta_auto_expected" >&2
  exit 1
}
beta_auto_hits="$(echo "$beta_auto_out" | grep -c "\"version\": \"$beta_auto_expected\"" || true)"
[[ "$beta_auto_hits" -ge 2 ]] || {
  echo "ERROR: --channel beta --product auto should sync product.version and desktop.version" >&2
  exit 1
}

existing_beta_product="$(resolve_product_auto_version "0.17.1-beta.1" "beta" "none")"
[[ "$existing_beta_product" == "0.17.1-beta.1" ]] || {
  echo "ERROR: resolve_product_auto_version should keep existing beta when auto_level=none" >&2
  exit 1
}
next_beta_product="$(resolve_product_auto_version "0.17.1-beta.1" "beta" "patch")"
[[ "$next_beta_product" == "0.17.2-beta.1" ]] || {
  echo "ERROR: resolve_product_auto_version should open a new beta line after component patch bump" >&2
  exit 1
}

invalid_manifest="$(mktemp)"
jq '.components.desktop.bundles += ["missing-agent"]' "$ROOT_DIR/release-manifest.json" > "$invalid_manifest"
if validate_manifest_v2 "$invalid_manifest" >/dev/null 2>&1; then
  echo "ERROR: manifest validation should reject invalid bundle references" >&2
  exit 1
fi

invalid_component_manifest="$(mktemp)"
trap 'rm -f "$electron_manifest" "$beta_manifest" "$stable_beta_product_manifest" "$beta_stable_product_manifest" "$invalid_min_max_manifest" "$invalid_db_compat_manifest" "$invalid_migration_policy_manifest" "$stable_isolated_beta_manifest" "$target_beta_manifest" "$beta_desktop_on_stable_manifest" "$stable_desktop_on_beta_manifest" "$leading_zero_manifest" "$invalid_manifest" "$invalid_component_manifest"' EXIT
jq '.components = ({"bad-component": {"version": "not-semver"}} + .components)' "$ROOT_DIR/release-manifest.json" > "$invalid_component_manifest"
if validate_manifest_v2 "$invalid_component_manifest" >/dev/null 2>&1; then
  echo "ERROR: manifest validation should reject invalid component semver anywhere in components" >&2
  exit 1
fi

invalid_bundle_version_manifest="$(mktemp)"
trap 'rm -f "$electron_manifest" "$beta_manifest" "$stable_beta_product_manifest" "$beta_stable_product_manifest" "$invalid_min_max_manifest" "$invalid_db_compat_manifest" "$invalid_migration_policy_manifest" "$stable_isolated_beta_manifest" "$target_beta_manifest" "$beta_desktop_on_stable_manifest" "$stable_desktop_on_beta_manifest" "$leading_zero_manifest" "$invalid_manifest" "$invalid_component_manifest" "$invalid_bundle_version_manifest"' EXIT
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

manifest_backup="$(mktemp)"
cp "$ROOT_DIR/release-manifest.json" "$manifest_backup"
cp "$stable_fixture_manifest" "$ROOT_DIR/release-manifest.json"
desktop_plan_out="$(./scripts/release-desktop.sh --bump-desktop 9.9.9 --dry-run --skip-upload 2>&1)"
cp "$manifest_backup" "$ROOT_DIR/release-manifest.json"
rm -f "$manifest_backup"
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

beta_db_name="$(./scripts/create-beta-database.sh --version 0.18.0-beta.1 --name-only)"
[[ "$beta_db_name" == "pactoolkits_beta_0_18_0_beta_1" ]] || {
  echo "ERROR: unexpected isolated Beta database name: $beta_db_name" >&2
  exit 1
}

beta_build_db_name="$(./scripts/create-beta-database.sh --version 0.18.0-beta.1+sha.7 --name-only)"
[[ "$beta_build_db_name" == "pactoolkits_beta_0_18_0_beta_1_sha_7" ]] || {
  echo "ERROR: unexpected isolated Beta database name with build metadata: $beta_build_db_name" >&2
  exit 1
}

if ./scripts/create-beta-database.sh --version 0.18.0 --name-only >/dev/null 2>&1; then
  echo "ERROR: isolated Beta database script should reject a Stable version" >&2
  exit 1
fi

beta_db_plan="$(./scripts/create-beta-database.sh --version 0.18.0-beta.1 --template pactoolkits_production --dry-run)"
echo "$beta_db_plan" | grep -Fq "createdb" || {
  echo "ERROR: isolated Beta database dry-run should include createdb" >&2
  exit 1
}
echo "$beta_db_plan" | grep -Fq "production-clone" || {
  echo "ERROR: isolated Beta database dry-run should write the production-clone marker" >&2
  exit 1
}

plain_sql_backup_dir="$(mktemp -d)"
plain_sql_backup="$plain_sql_backup_dir/pactoolkits-beta-backup.SQL"
touch "$plain_sql_backup"
beta_sql_restore_plan="$(./scripts/create-beta-database.sh --version 0.18.0-beta.2 --backup "$plain_sql_backup" --dry-run)"
rm -rf "$plain_sql_backup_dir"
echo "$beta_sql_restore_plan" | grep -Fq "psql" || {
  echo "ERROR: uppercase .SQL backup should use psql restore" >&2
  exit 1
}
if echo "$beta_sql_restore_plan" | grep -Fq "pg_restore"; then
  echo "ERROR: uppercase .SQL backup should not use pg_restore" >&2
  exit 1
fi

grep -Fq "('isolated', 'Database.Environment'" scripts/create-beta-database.sql || {
  echo "ERROR: isolated Beta database SQL is missing the environment marker" >&2
  exit 1
}
grep -Fq "('isolated', 'Database.AllowBetaMigrations'" scripts/create-beta-database.sql || {
  echo "ERROR: isolated Beta database SQL is missing the migration authorization marker" >&2
  exit 1
}

grep -Fq '04_environment_settings.sql' database/postgres/scripts/lib/verify.sh || {
  echo "ERROR: Bash verify suite is missing environment settings verification" >&2
  exit 1
}
grep -Fq '04_environment_settings.sql' database/postgres/scripts/deploy.ps1 || {
  echo "ERROR: PowerShell verify suite is missing environment settings verification" >&2
  exit 1
}
grep -Fq "current_setting('pactoolkits.expected_schema_version'" database/postgres/sql/verify/03_schema_version.sql || {
  echo "ERROR: schema version verification should bridge the psql variable through a session setting" >&2
  exit 1
}
if grep -Fq "pg_try_advisory_lock" database/postgres/scripts/lib/common.sh; then
  echo "ERROR: Bash deploy lock must survive separate psql processes" >&2
  exit 1
fi
if grep -Fq "datname = :'database_name'" scripts/create-beta-database.sh scripts/create-beta-database.ps1; then
  echo "ERROR: psql -c database lookups must not rely on psql variable interpolation" >&2
  exit 1
fi
grep -Fq 'cp release-manifest.json dist/release-manifest.json' .github/workflows/publish-release.yml || {
  echo "ERROR: release feed must publish channel release-manifest.json" >&2
  exit 1
}

echo "Tooling tests passed."
