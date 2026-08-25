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

# 临时改写仓根清单执行命令，结束后始终还原
with_root_manifest() {
  local fixture="$1"
  shift
  local backup status=0
  backup="$(mktemp)"
  cp "$ROOT_DIR/release-manifest.json" "$backup"
  cp "$fixture" "$ROOT_DIR/release-manifest.json"
  set +e
  "$@"
  status=$?
  set -e
  cp "$backup" "$ROOT_DIR/release-manifest.json"
  rm -f "$backup"
  return "$status"
}

run ./scripts/export-version.sh
run ./scripts/check-version.sh
(
  extra_module_dir="$(mktemp -d "$ROOT_DIR/runtime/agents/modules/tooling-extra.XXXXXX")"
  trap 'rm -rf "$extra_module_dir"' EXIT
  printf '%s\n' '{"id":"ToolingExtra","version":"0.1.0"}' > "$extra_module_dir/module.json"
  if ./scripts/check-version.sh > /dev/null 2>&1; then
    echo "ERROR: check-version should reject source modules absent from release-manifest.json" >&2
    exit 1
  fi
)
bash -n ./scripts/clean-build-artifacts.sh
./scripts/clean-build-artifacts.sh --dry-run > /dev/null

stable_fixture_manifest="$(mktemp)"
jq '
  .release.channel = "stable" |
  .product.version = (
    if (.product.version | test("-beta\\.")) then
      (.product.version | sub("-beta\\.[0-9]+$"; ""))
    else .product.version end
  ) |
  .components.desktop.avalonia.version = (
    if (.components.desktop.avalonia.version | test("-beta\\.")) then
      (.components.desktop.avalonia.version | sub("-beta\\.[0-9]+$"; ""))
    else .components.desktop.avalonia.version end
  ) |
  .components.agents.minDesktop = .components.desktop.avalonia.version |
  .components.agents.maxDesktop = .components.desktop.avalonia.version
' "$ROOT_DIR/release-manifest.json" > "$stable_fixture_manifest"
validate_manifest_v2 "$stable_fixture_manifest"

eval "$(./scripts/resolve-release-plan.sh "$ROOT_DIR/release-manifest.json" | sed 's/^\([^=]*\)=\(.*\)$/export \1=\2/')"
[[ "${desktop_artifact_name:-}" == pactoolkits-desktop-avalonia-win-x64-* ]] || {
  echo "ERROR: unexpected avalonia artifact name: ${desktop_artifact_name:-<empty>}" >&2
  exit 1
}

# Desktop 实现标识属于发布协议，未知实现必须在生成发布计划前拒绝
unknown_impl_manifest="$(mktemp)"
jq '.components.desktop = {other: .components.desktop.avalonia}' "$ROOT_DIR/release-manifest.json" > "$unknown_impl_manifest"
if validate_manifest_v2 "$unknown_impl_manifest" > /dev/null 2>&1; then
  echo "ERROR: manifest validation should require components.desktop.avalonia" >&2
  exit 1
fi
rm -f "$unknown_impl_manifest"

invalid_package_id_manifest="$(mktemp)"
jq '.components.desktop.avalonia.packageId = "pactoolkits-beta"' "$ROOT_DIR/release-manifest.json" > "$invalid_package_id_manifest"
if validate_manifest_v2 "$invalid_package_id_manifest" > /dev/null 2>&1; then
  echo "ERROR: manifest validation should reject an unexpected desktop packageId" >&2
  exit 1
fi
rm -f "$invalid_package_id_manifest"

invalid_channel_manifest="$(mktemp)"
jq '.release.channel = "preview"' "$ROOT_DIR/release-manifest.json" > "$invalid_channel_manifest"
if validate_manifest_v2 "$invalid_channel_manifest" > /dev/null 2>&1; then
  echo "ERROR: manifest validation should reject an unsupported release channel" >&2
  exit 1
fi
rm -f "$invalid_channel_manifest"

beta_manifest="$(mktemp)"
trap 'rm -f "$beta_manifest"' EXIT
jq '
  .release.channel = "beta" |
  .product.version = "0.17.1-beta.1" |
  .components.desktop.avalonia.version = "0.17.1-beta.1" |
  .components.agents.minDesktop = "0.17.1-beta.1" |
  .components.agents.maxDesktop = "0.17.1-beta.1"
' "$stable_fixture_manifest" > "$beta_manifest"
validate_manifest_v2 "$beta_manifest"
agent_min_desktop="$(jq -r '.components["agents"].minDesktop' "$beta_manifest")"
[[ -n "$agent_min_desktop" ]] || {
  echo "ERROR: beta channel agents.minDesktop must be present" >&2
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
if validate_release_prerelease_flag "$beta_manifest" "false" > /dev/null 2>&1; then
  echo "ERROR: beta channel should reject prerelease=false" >&2
  exit 1
fi
beta_release_channel_plan="$(
  ./scripts/validate-release-channel.sh \
    --manifest "$beta_manifest" \
    --tag "v0.17.1-beta.1" \
    --prerelease true \
    --dry-run false \
    --confirm true
)"
echo "$beta_release_channel_plan" | grep -Fq 'channel=beta' || {
  echo "ERROR: beta release validation should resolve the beta channel" >&2
  exit 1
}

stable_beta_product_manifest="$(mktemp)"
jq '.release.channel = "stable" | .product.version = "0.17.1-beta.1"' "$stable_fixture_manifest" > "$stable_beta_product_manifest"
if validate_manifest_v2 "$stable_beta_product_manifest" > /dev/null 2>&1; then
  echo "ERROR: stable channel should reject beta product.version" >&2
  exit 1
fi

beta_stable_product_manifest="$(mktemp)"
jq '.release.channel = "beta"' "$stable_fixture_manifest" > "$beta_stable_product_manifest"
if validate_manifest_v2 "$beta_stable_product_manifest" > /dev/null 2>&1; then
  echo "ERROR: beta channel should reject stable-only product.version" >&2
  exit 1
fi

invalid_min_max_manifest="$(mktemp)"
jq '.components.api.maxDbSchema = "1.2.21"' "$ROOT_DIR/release-manifest.json" > "$invalid_min_max_manifest"
if validate_manifest_v2 "$invalid_min_max_manifest" > /dev/null 2>&1; then
  echo "ERROR: manifest validation should reject minDbSchema > maxDbSchema" >&2
  exit 1
fi

invalid_db_compat_manifest="$(mktemp)"
jq '.components.database.postgres.version = "9.9.9"' "$ROOT_DIR/release-manifest.json" > "$invalid_db_compat_manifest"
if validate_manifest_v2 "$invalid_db_compat_manifest" > /dev/null 2>&1; then
  echo "ERROR: manifest validation should reject database.postgres.version outside component bounds" >&2
  exit 1
fi

# Windows/MSYS2 jq 输出 CRLF 时，带 CR 的 api id 仍应通过
validate_component_db_bounds "$ROOT_DIR/release-manifest.json" $'api\r' || {
  echo "ERROR: validate_component_db_bounds should accept api id with trailing CR" >&2
  exit 1
}
validate_agents_desktop_bounds "$ROOT_DIR/release-manifest.json" || {
  echo "ERROR: validate_agents_desktop_bounds should accept release-manifest agents min/max Desktop" >&2
  exit 1
}

trap 'rm -f "$beta_manifest" "$stable_beta_product_manifest" "$beta_stable_product_manifest" "$invalid_min_max_manifest" "$invalid_db_compat_manifest"' EXIT

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
    --dry-run false \
    --confirm true
)"
echo "$release_channel_plan" | grep -Fq 'channel=stable' || {
  echo "ERROR: stable release validation should resolve the stable channel" >&2
  exit 1
}
if ./scripts/validate-release-channel.sh \
  --manifest "$stable_fixture_manifest" \
  --tag "v$(manifest_product_version "$stable_fixture_manifest")" \
  --prerelease false \
  --dry-run false \
  --confirm false > /dev/null 2>&1; then
  echo "ERROR: formal release validation should require confirm=true" >&2
  exit 1
fi

server_release_test_root="$(mktemp -d)"
for component in api desktop agents; do
  mkdir -p "$server_release_test_root/source/$component"
  printf '%s\n' "$component payload" > "$server_release_test_root/source/$component/payload.bin"
done
export RELEASE_COMMIT="test-commit-1"
export RELEASE_CI_RUN="test-run-1"
export RELEASE_PUBLISHED_AT="2026-08-16T00:00:00Z"
for component in api desktop agents; do
  ./scripts/prepare-server-release.sh \
    "$component" "1.2.3-beta.1" beta \
    "$server_release_test_root/source/$component" \
    "$server_release_test_root/prepared/$component"
done
mkdir -p "$server_release_test_root/server/.incoming/run-1"
mv "$server_release_test_root/prepared"/* "$server_release_test_root/server/.incoming/run-1/"
./scripts/publish-server-releases.sh \
  "$server_release_test_root/server" run-1 beta \
  1.2.3-beta.1 1.2.3-beta.1 1.2.3-beta.1
for component in api desktop agents; do
  [[ "$(readlink "$server_release_test_root/server/$component/beta")" == "releases/1.2.3-beta.1" ]] || {
    echo "ERROR: $component beta pointer should target its immutable release" >&2
    exit 1
  }
  (cd "$server_release_test_root/server/$component/releases/1.2.3-beta.1" && sha256sum -c SHA256SUMS > /dev/null)
done

export RELEASE_COMMIT="test-commit-2"
export RELEASE_CI_RUN="test-run-2"
mkdir -p "$server_release_test_root/server/.incoming/run-2"
for component in api desktop agents; do
  ./scripts/prepare-server-release.sh \
    "$component" "1.2.3-beta.1" beta \
    "$server_release_test_root/source/$component" \
    "$server_release_test_root/server/.incoming/run-2/$component"
done
./scripts/publish-server-releases.sh \
  "$server_release_test_root/server" run-2 beta \
  1.2.3-beta.1 1.2.3-beta.1 1.2.3-beta.1
[[ "$(jq -r '.commit' "$server_release_test_root/server/api/releases/1.2.3-beta.1/release.json")" == "test-commit-1" ]] || {
  echo "ERROR: republishing a component version must not overwrite its release metadata" >&2
  exit 1
}

probe_all="$(./scripts/probe-server-releases.sh \
  "$server_release_test_root/server" 1.2.3-beta.1 1.2.3-beta.1 1.2.3-beta.1)"
[[ -z "$probe_all" ]] || {
  echo "ERROR: probe should list no components when all snapshots exist" >&2
  exit 1
}

./scripts/publish-server-releases.sh \
  "$server_release_test_root/server" run-empty beta \
  1.2.3-beta.1 1.2.3-beta.1 1.2.3-beta.1
[[ "$(jq -r '.commit' "$server_release_test_root/server/api/releases/1.2.3-beta.1/release.json")" == "test-commit-1" ]] || {
  echo "ERROR: pointer-only publish must not overwrite release metadata" >&2
  exit 1
}
[[ "$(readlink "$server_release_test_root/server/api/beta")" == "releases/1.2.3-beta.1" ]] || {
  echo "ERROR: pointer-only publish should keep the beta pointer" >&2
  exit 1
}

rm -rf "$server_release_test_root/server/agents/releases/1.2.3-beta.1"
probe_agents="$(./scripts/probe-server-releases.sh \
  "$server_release_test_root/server" 1.2.3-beta.1 1.2.3-beta.1 1.2.3-beta.1)"
[[ "$probe_agents" == "agents" ]] || {
  echo "ERROR: probe should list only the missing agents snapshot" >&2
  exit 1
}

export RELEASE_COMMIT="test-commit-3"
export RELEASE_CI_RUN="test-run-3"
mkdir -p "$server_release_test_root/server/.incoming/run-3"
./scripts/prepare-server-release.sh \
  agents "1.2.3-beta.1" beta \
  "$server_release_test_root/source/agents" \
  "$server_release_test_root/server/.incoming/run-3/agents"
./scripts/publish-server-releases.sh \
  "$server_release_test_root/server" run-3 beta \
  1.2.3-beta.1 1.2.3-beta.1 1.2.3-beta.1
[[ "$(jq -r '.commit' "$server_release_test_root/server/api/releases/1.2.3-beta.1/release.json")" == "test-commit-1" ]] || {
  echo "ERROR: partial publish must not overwrite existing api metadata" >&2
  exit 1
}
[[ "$(jq -r '.commit' "$server_release_test_root/server/agents/releases/1.2.3-beta.1/release.json")" == "test-commit-3" ]] || {
  echo "ERROR: partial publish should commit the missing agents snapshot" >&2
  exit 1
}

export RELEASE_COMMIT="test-commit-4"
export RELEASE_CI_RUN="test-run-4"
mkdir -p "$server_release_test_root/server/.incoming/run-4"
for component in api desktop agents; do
  ./scripts/prepare-server-release.sh \
    "$component" "1.2.4-beta.1" beta \
    "$server_release_test_root/source/$component" \
    "$server_release_test_root/server/.incoming/run-4/$component"
done
./scripts/publish-server-releases.sh \
  "$server_release_test_root/server" run-4 beta \
  1.2.4-beta.1 1.2.4-beta.1 1.2.4-beta.1
for component in api desktop agents; do
  [[ "$(readlink "$server_release_test_root/server/$component/beta")" == "releases/1.2.4-beta.1" ]] || {
    echo "ERROR: $component beta pointer should switch to the new immutable release" >&2
    exit 1
  }
done
rm -rf "$server_release_test_root"

make_api_snapshot() {
  local version="$1"
  local dest="$2"
  local payload="$3"
  local pack source
  pack="$(mktemp -d)"
  source="$(mktemp -d)"
  printf '%s\n' "$payload" > "$pack/PacToolkits.Api.dll"
  tar -C "$pack" -czf "$source/PacToolkits-Api-${version}.tar.gz" .
  RELEASE_COMMIT="deploy-api-$version" \
    RELEASE_CI_RUN="deploy-api-run" \
    RELEASE_PUBLISHED_AT="2026-08-19T00:00:00Z" \
    ./scripts/prepare-server-release.sh api "$version" stable "$source" "$dest"
  rm -rf "$pack" "$source"
}

api_deploy_root="$(mktemp -d)"
make_api_snapshot 0.1.0 "$api_deploy_root/snap-0.1.0" v1
make_api_snapshot 0.1.1 "$api_deploy_root/snap-0.1.1" v2
api_install="$(mktemp -d)"
./apps/api-asp/scripts/deploy.sh apply \
  --snapshot "$api_deploy_root/snap-0.1.0" \
  --root "$api_install" \
  --skip-service \
  --dry-run
[[ ! -e "$api_install/current" ]] || {
  echo "ERROR: api deploy.sh dry-run must not create current" >&2
  exit 1
}
./apps/api-asp/scripts/deploy.sh apply \
  --snapshot "$api_deploy_root/snap-0.1.0" \
  --root "$api_install" \
  --skip-service
[[ "$(readlink "$api_install/current")" == "releases/0.1.0" ]] || {
  echo "ERROR: api deploy.sh apply should point current at releases/0.1.0" >&2
  exit 1
}
grep -Fq v1 "$api_install/releases/0.1.0/PacToolkits.Api.dll" || {
  echo "ERROR: api deploy.sh apply should extract PacToolkits.Api.dll" >&2
  exit 1
}
if ./apps/api-asp/scripts/deploy.sh rollback --root "$api_install" --skip-service > /dev/null 2>&1; then
  echo "ERROR: api deploy.sh rollback should fail without previous" >&2
  exit 1
fi
./apps/api-asp/scripts/deploy.sh apply \
  --snapshot "$api_deploy_root/snap-0.1.1" \
  --root "$api_install" \
  --skip-service
[[ "$(readlink "$api_install/current")" == "releases/0.1.1" ]] || {
  echo "ERROR: api deploy.sh apply should switch current to releases/0.1.1" >&2
  exit 1
}
[[ "$(readlink "$api_install/previous")" == "releases/0.1.0" ]] || {
  echo "ERROR: api deploy.sh apply should record previous as releases/0.1.0" >&2
  exit 1
}
./apps/api-asp/scripts/deploy.sh rollback --root "$api_install" --skip-service
[[ "$(readlink "$api_install/current")" == "releases/0.1.0" ]] || {
  echo "ERROR: api deploy.sh rollback should restore releases/0.1.0" >&2
  exit 1
}
[[ "$(readlink "$api_install/previous")" == "releases/0.1.1" ]] || {
  echo "ERROR: api deploy.sh rollback should swap previous to the rolled-off release" >&2
  exit 1
}
mkdir -p "$api_deploy_root/feed/api/releases"
cp -R "$api_deploy_root/snap-0.1.0" "$api_deploy_root/feed/api/releases/0.1.0"
ln -s "releases/0.1.0" "$api_deploy_root/feed/api/current"
api_feed_install="$(mktemp -d)"
./apps/api-asp/scripts/deploy.sh apply \
  --feed "$api_deploy_root/feed" \
  --channel current \
  --root "$api_feed_install" \
  --skip-service
[[ "$(readlink "$api_feed_install/current")" == "releases/0.1.0" ]] || {
  echo "ERROR: api deploy.sh --feed should follow api/current" >&2
  exit 1
}
bad_sum="$(mktemp -d)"
bad_root="$(mktemp -d)"
cp -R "$api_deploy_root/snap-0.1.0/." "$bad_sum/"
printf '%s  %s\n' "$(printf '%064d' 1)" "PacToolkits-Api-0.1.0.tar.gz" > "$bad_sum/SHA256SUMS"
if ./apps/api-asp/scripts/deploy.sh apply --snapshot "$bad_sum" --root "$bad_root" --skip-service > /dev/null 2>&1; then
  echo "ERROR: api deploy.sh apply should reject a snapshot with invalid SHA256SUMS" >&2
  exit 1
fi
install_unit_dry="$(
  ./apps/api-asp/scripts/deploy.sh install-unit \
    --root /opt/pactoolkits/api-test \
    --env-file /etc/pactoolkits/.env.asp.test \
    --dry-run
)"
echo "$install_unit_dry" | grep -Fq 'WorkingDirectory=/opt/pactoolkits/api-test/current' || {
  echo "ERROR: api deploy.sh install-unit should follow --root" >&2
  exit 1
}
echo "$install_unit_dry" | grep -Fq 'EnvironmentFile=/etc/pactoolkits/.env.asp.test' || {
  echo "ERROR: api deploy.sh install-unit should follow --env-file" >&2
  exit 1
}
rm -rf "$api_deploy_root" "$api_install" "$api_feed_install" "$bad_sum" "$bad_root"

database_policy_base_manifest="$(mktemp)"
cp "$stable_fixture_manifest" "$database_policy_base_manifest"
./scripts/validate-database-policy.sh \
  --manifest "$stable_fixture_manifest" \
  --base-ref refs/heads/pactoolkits-missing-test-ref \
  --base-manifest "$database_policy_base_manifest" > /dev/null

beta_db_follow_legacy_manifest="$(mktemp)"
jq '
  .release.channel = "beta" |
  .product.version = "0.18.0-beta.1" |
  .components.desktop.avalonia.version = "0.18.0-beta.1" |
  .components.agents.minDesktop = "0.18.0-beta.1" |
  .components.agents.maxDesktop = "0.18.0-beta.1"
' "$stable_fixture_manifest" > "$beta_db_follow_legacy_manifest"
./scripts/validate-database-policy.sh \
  --manifest "$beta_db_follow_legacy_manifest" \
  --base-ref refs/heads/pactoolkits-missing-test-ref \
  --base-manifest "$database_policy_base_manifest" > /dev/null

beta_db_upgrade_manifest="$(mktemp)"
jq \
  --arg db "$(semver_bump_patch "$(manifest_database_postgres_version "$stable_fixture_manifest")")" \
  '
  .release.channel = "beta" |
  .product.version = "0.18.0-beta.1" |
  .components.desktop.avalonia.version = "0.18.0-beta.1" |
  .components.agents.minDesktop = "0.18.0-beta.1" |
  .components.agents.maxDesktop = "0.18.0-beta.1" |
  .components.api.minDbSchema = $db |
  .components.api.maxDbSchema = $db |
  .components.database.postgres.version = $db
' "$stable_fixture_manifest" > "$beta_db_upgrade_manifest"
./scripts/validate-database-policy.sh \
  --manifest "$beta_db_upgrade_manifest" \
  --base-ref refs/heads/pactoolkits-missing-test-ref \
  --base-manifest "$database_policy_base_manifest" > /dev/null
if grep -Fq 'beta' "$ROOT_DIR/apps/desktop-avalonia/src/Version.g.props" \
  && grep -Eq '<AssemblyVersion>[^<]*beta' "$ROOT_DIR/apps/desktop-avalonia/src/Version.g.props"; then
  echo "ERROR: AssemblyVersion must use numeric major.minor.build.revision only" >&2
  exit 1
fi

policy_git_dir="$(mktemp -d)"
git -C "$policy_git_dir" init -q
git -C "$policy_git_dir" config user.email tooling-tests@example.invalid
git -C "$policy_git_dir" config user.name tooling-tests
mkdir -p "$policy_git_dir/database/postgres/migrations"
cp "$database_policy_base_manifest" "$policy_git_dir/release-manifest.json"
printf '%s\n' 'select 1;' > "$policy_git_dir/database/postgres/migrations/V1_0_0__baseline.sql"
git -C "$policy_git_dir" add .
git -C "$policy_git_dir" commit -qm baseline
policy_base_ref="$(git -C "$policy_git_dir" rev-parse HEAD)"
printf '%s\n' 'select 2;' > "$policy_git_dir/database/postgres/migrations/V1_0_0__baseline.sql"
git -C "$policy_git_dir" add .
git -C "$policy_git_dir" commit -qm modify-migration
if (
  cd "$policy_git_dir"
  "$ROOT_DIR/scripts/validate-database-policy.sh" \
    --manifest release-manifest.json \
    --base-ref "$policy_base_ref"
) > /dev/null 2>&1; then
  echo "ERROR: database policy should reject modification of an existing migration" >&2
  exit 1
fi

legacy_reloc_git_dir="$(mktemp -d)"
git -C "$legacy_reloc_git_dir" init -q
git -C "$legacy_reloc_git_dir" config user.email tooling-tests@example.invalid
git -C "$legacy_reloc_git_dir" config user.name tooling-tests
cp "$stable_fixture_manifest" "$legacy_reloc_git_dir/release-manifest.json"
mkdir -p "$legacy_reloc_git_dir/pactoolkits-db/sql/migrations"
cp "$ROOT_DIR/database/postgres/migrations/V1_2_0__baseline.sql" \
  "$legacy_reloc_git_dir/pactoolkits-db/sql/migrations/V1_2_0__baseline.sql"
git -C "$legacy_reloc_git_dir" add .
git -C "$legacy_reloc_git_dir" commit -qm legacy-baseline
legacy_reloc_base_ref="$(git -C "$legacy_reloc_git_dir" rev-parse HEAD)"
mkdir -p "$legacy_reloc_git_dir/database/postgres/migrations"
git -C "$legacy_reloc_git_dir" mv pactoolkits-db/sql/migrations/V1_2_0__baseline.sql \
  database/postgres/migrations/V1_2_0__baseline.sql
jq '
  .release.channel = "beta" |
  .product.version = "1.0.0-beta.1" |
  .components.desktop.avalonia.version = "1.0.0-beta.1" |
  .components.agents.minDesktop = "1.0.0-beta.1" |
  .components.agents.maxDesktop = "1.0.0-beta.1"
' "$stable_fixture_manifest" > "$legacy_reloc_git_dir/release-manifest.json"
git -C "$legacy_reloc_git_dir" add .
git -C "$legacy_reloc_git_dir" commit -qm monorepo-reloc
(
  cd "$legacy_reloc_git_dir"
  "$ROOT_DIR/scripts/validate-database-policy.sh" \
    --manifest release-manifest.json \
    --base-ref "$legacy_reloc_base_ref"
) > /dev/null
rm -rf "$legacy_reloc_git_dir"

beta_new_migration_git_dir="$(mktemp -d)"
git -C "$beta_new_migration_git_dir" init -q
git -C "$beta_new_migration_git_dir" config user.email tooling-tests@example.invalid
git -C "$beta_new_migration_git_dir" config user.name tooling-tests
mkdir -p "$beta_new_migration_git_dir/database/postgres/migrations"
cp "$database_policy_base_manifest" "$beta_new_migration_git_dir/release-manifest.json"
printf '%s\n' 'select 1;' > "$beta_new_migration_git_dir/database/postgres/migrations/V1_0_0__baseline.sql"
git -C "$beta_new_migration_git_dir" add .
git -C "$beta_new_migration_git_dir" commit -qm baseline
beta_new_migration_base_ref="$(git -C "$beta_new_migration_git_dir" rev-parse HEAD)"
jq '
  .release.channel = "beta" |
  .product.version = "0.18.0-beta.1" |
  .components.desktop.avalonia.version = "0.18.0-beta.1" |
  .components.agents.minDesktop = "0.18.0-beta.1" |
  .components.agents.maxDesktop = "0.18.0-beta.1"
' "$database_policy_base_manifest" > "$beta_new_migration_git_dir/release-manifest.json"
printf '%s\n' 'select 1;' \
  > "$beta_new_migration_git_dir/database/postgres/migrations/V9_9_9__policy_probe.sql"
git -C "$beta_new_migration_git_dir" add .
git -C "$beta_new_migration_git_dir" commit -qm add-beta-migration
(
  cd "$beta_new_migration_git_dir"
  "$ROOT_DIR/scripts/validate-database-policy.sh" \
    --manifest release-manifest.json \
    --base-ref "$beta_new_migration_base_ref"
) > /dev/null
rm -rf "$beta_new_migration_git_dir"
rm -rf "$policy_git_dir"
rm -f "$database_policy_base_manifest" "$beta_db_follow_legacy_manifest" "$beta_db_upgrade_manifest"

target_beta_manifest="$(mktemp)"
jq '
  .product.version = "0.18.0-beta.1" |
  .components.desktop.avalonia.version = "0.18.0-beta.1" |
  .components.agents.minDesktop = "0.18.0-beta.1" |
  .components.agents.maxDesktop = "0.18.0-beta.1" |
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
jq '.release.channel = "stable" | .components.desktop.avalonia.version = "0.18.0-beta.1"' "$stable_fixture_manifest" > "$beta_desktop_on_stable_manifest"
if validate_manifest_v2 "$beta_desktop_on_stable_manifest" > /dev/null 2>&1; then
  echo "ERROR: stable channel should reject beta desktop.version" >&2
  exit 1
fi

stable_desktop_on_beta_manifest="$(mktemp)"
jq '.release.channel = "beta" | .product.version = "0.17.1-beta.1" | .components.desktop.avalonia.version = "0.16.1"' "$stable_fixture_manifest" > "$stable_desktop_on_beta_manifest"
if validate_manifest_v2 "$stable_desktop_on_beta_manifest" > /dev/null 2>&1; then
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
if validate_manifest_v2 "$leading_zero_manifest" > /dev/null 2>&1; then
  echo "ERROR: manifest validation should reject leading-zero product.version" >&2
  exit 1
fi

beta_auto_expected="$(resolve_product_auto_version "$(manifest_product_version "$stable_fixture_manifest")" "beta" "none")"
beta_auto_out="$(with_root_manifest "$stable_fixture_manifest" \
  ./scripts/bump-version.sh --channel beta --product auto --dry-run 2>&1)" || {
  echo "ERROR: beta auto bump dry-run failed" >&2
  echo "$beta_auto_out" >&2
  exit 1
}
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
jq '.components.agents.modules.Injector.version = "not-semver"' \
  "$ROOT_DIR/release-manifest.json" > "$invalid_manifest"
if validate_manifest_v2 "$invalid_manifest" > /dev/null 2>&1; then
  echo "ERROR: manifest validation should reject invalid agents.modules versions" >&2
  exit 1
fi

invalid_module_contract_half_manifest="$(mktemp)"
jq 'del(.components.agents.modules.Injector.maxApiContract)' \
  "$ROOT_DIR/release-manifest.json" > "$invalid_module_contract_half_manifest"
if validate_manifest_v2 "$invalid_module_contract_half_manifest" > /dev/null 2>&1; then
  echo "ERROR: manifest validation should reject half agents.modules api contract bounds" >&2
  exit 1
fi

invalid_module_contract_order_manifest="$(mktemp)"
jq '
  .components.agents.modules.Injector.minApiContract = "1.5.0" |
  .components.agents.modules.Injector.maxApiContract = "1.4.0"
' "$ROOT_DIR/release-manifest.json" > "$invalid_module_contract_order_manifest"
if validate_manifest_v2 "$invalid_module_contract_order_manifest" > /dev/null 2>&1; then
  echo "ERROR: manifest validation should reject agents.modules minApiContract > maxApiContract" >&2
  exit 1
fi

invalid_api_contract_manifest="$(mktemp)"
jq '.components.api.contractVersion = "not-semver"' \
  "$ROOT_DIR/release-manifest.json" > "$invalid_api_contract_manifest"
if validate_manifest_v2 "$invalid_api_contract_manifest" > /dev/null 2>&1; then
  echo "ERROR: manifest validation should reject invalid api.contractVersion" >&2
  exit 1
fi

invalid_bundle_version_manifest="$(mktemp)"
invalid_module_id_manifest="$(mktemp)"
agents_staging_fixture="$(mktemp -d)"
trap 'rm -f "$beta_manifest" "$stable_beta_product_manifest" "$beta_stable_product_manifest" "$invalid_min_max_manifest" "$invalid_db_compat_manifest" "$target_beta_manifest" "$beta_desktop_on_stable_manifest" "$stable_desktop_on_beta_manifest" "$leading_zero_manifest" "$invalid_manifest" "$invalid_module_contract_half_manifest" "$invalid_module_contract_order_manifest" "$invalid_api_contract_manifest" "$invalid_bundle_version_manifest" "$invalid_module_id_manifest"; rm -rf "$agents_staging_fixture"' EXIT
jq '.components["agents"].version = "not-semver"' "$ROOT_DIR/release-manifest.json" > "$invalid_bundle_version_manifest"
if validate_manifest_v2 "$invalid_bundle_version_manifest" > /dev/null 2>&1; then
  echo "ERROR: manifest validation should reject invalid agents host versions" >&2
  exit 1
fi

jq '.components.agents.modules["../Probe"] = {"version":"1.0.0"}' \
  "$ROOT_DIR/release-manifest.json" > "$invalid_module_id_manifest"
if validate_manifest_v2 "$invalid_module_id_manifest" > /dev/null 2>&1; then
  echo "ERROR: manifest validation should reject non-portable module ids" >&2
  exit 1
fi

# staging 契约要求发布清单、模块目录和模块配置文件形成精确集合
: > "$agents_staging_fixture/Agents.exe"
printf '%s\n' '{"schemaVersion":2}' > "$agents_staging_fixture/ReleaseManifest.json"
while IFS= read -r module_id; do
  [[ -n "$module_id" ]] || continue
  module_dir="$agents_staging_fixture/Modules/$module_id"
  mkdir -p "$module_dir"
  module_version="$(manifest_agents_module_version "$ROOT_DIR/release-manifest.json" "$module_id")"
  printf '%s\n' "{\"id\":\"$module_id\",\"version\":\"$module_version\",\"entry\":{\"win-x64\":\"$module_id.exe\"}}" > "$module_dir/module.json"
  : > "$module_dir/$module_id.exe"
  printf '%s\n' '{"Enabled":true}' > "$module_dir/settings.json"
  printf '%s\n' '{"schemaVersion":1,"sections":[{"fields":[{"key":"Enabled","type":"bool"}]}]}' > "$module_dir/settings.schema.json"
done < <(manifest_agents_module_ids "$ROOT_DIR/release-manifest.json")
validate_agents_staging_layout "$agents_staging_fixture" "$ROOT_DIR/release-manifest.json" 0 || {
  echo "ERROR: validate_agents_staging_layout should accept fixture with settings + schema" >&2
  exit 1
}
first_module_id="$(manifest_agents_module_ids "$ROOT_DIR/release-manifest.json" | head -n1)"
cp "$agents_staging_fixture/Modules/$first_module_id/module.json" \
  "$agents_staging_fixture/Modules/$first_module_id/module.json.valid-version"
jq '.version = "0.0.0"' \
  "$agents_staging_fixture/Modules/$first_module_id/module.json.valid-version" \
  > "$agents_staging_fixture/Modules/$first_module_id/module.json"
if validate_agents_staging_layout "$agents_staging_fixture" "$ROOT_DIR/release-manifest.json" 0 > /dev/null 2>&1; then
  echo "ERROR: validate_agents_staging_layout should reject module version drift" >&2
  exit 1
fi
mv "$agents_staging_fixture/Modules/$first_module_id/module.json.valid-version" \
  "$agents_staging_fixture/Modules/$first_module_id/module.json"
cp "$agents_staging_fixture/Modules/$first_module_id/module.json" \
  "$agents_staging_fixture/Modules/$first_module_id/module.json.valid"
jq '.entry["win-x64"] = "../Agents.exe"' \
  "$agents_staging_fixture/Modules/$first_module_id/module.json.valid" \
  > "$agents_staging_fixture/Modules/$first_module_id/module.json"
if validate_agents_staging_layout "$agents_staging_fixture" "$ROOT_DIR/release-manifest.json" 0 > /dev/null 2>&1; then
  echo "ERROR: validate_agents_staging_layout should reject entry path traversal" >&2
  exit 1
fi
mv "$agents_staging_fixture/Modules/$first_module_id/module.json.valid" \
  "$agents_staging_fixture/Modules/$first_module_id/module.json"
mkdir -p "$agents_staging_fixture/Modules/StaleModule"
if validate_agents_staging_layout "$agents_staging_fixture" "$ROOT_DIR/release-manifest.json" 0 > /dev/null 2>&1; then
  echo "ERROR: validate_agents_staging_layout should reject module dirs absent from manifest" >&2
  exit 1
fi
rm -rf "$agents_staging_fixture/Modules/StaleModule"
rm -f "$agents_staging_fixture/Modules/$first_module_id/settings.json"
if validate_agents_staging_layout "$agents_staging_fixture" "$ROOT_DIR/release-manifest.json" 0 > /dev/null 2>&1; then
  echo "ERROR: validate_agents_staging_layout should reject missing settings.json" >&2
  exit 1
fi

current_agents="$(manifest_agents_version "$ROOT_DIR/release-manifest.json")"
IFS='.' read -r agents_major agents_minor agents_patch <<< "$current_agents"
next_agents="${agents_major}.${agents_minor}.$((agents_patch + 1))"
agents_plan_out="$(./scripts/release-agents.sh --bump-agents "$next_agents" --dry-run 2>&1)"
echo "$agents_plan_out" | grep -Fq "agents.version: $next_agents" || {
  echo "ERROR: dry-run release plan should reflect bumped agents version ($next_agents)" >&2
  exit 1
}
echo "$agents_plan_out" | grep -Fq "PacToolkits-Agents-win-x64-${next_agents}.zip" || {
  echo "ERROR: dry-run artifact name should reflect bumped agents version ($next_agents)" >&2
  exit 1
}

desktop_plan_out="$(with_root_manifest "$stable_fixture_manifest" \
  ./scripts/release-desktop.sh --bump-desktop 9.9.9 --dry-run 2>&1)" || {
  echo "ERROR: release-desktop dry-run with desktop bump failed" >&2
  echo "$desktop_plan_out" >&2
  exit 1
}
echo "$desktop_plan_out" | grep -Fq "desktop.avalonia.version: 9.9.9" || {
  echo "ERROR: dry-run desktop release plan should reflect bumped desktop version (9.9.9)" >&2
  exit 1
}

conflict_out="$(./scripts/release-agents.sh --bump-agents 0.6.2 --bump-component agents=0.6.3 --dry-run 2>&1)" && {
  echo "ERROR: release script should reject conflicting agents bump flags" >&2
  exit 1
}
echo "$conflict_out" | grep -Fq "conflicting agents version" || {
  echo "ERROR: expected conflicting agents version error message" >&2
  exit 1
}

bump_conflict_out="$(./scripts/bump-version.sh --component agents=0.6.2 --component agents=0.6.3 --dry-run 2>&1)" && {
  echo "ERROR: bump-version.sh should reject duplicate agents component updates" >&2
  exit 1
}
echo "$bump_conflict_out" | grep -Fq "conflicting component version for agents" || {
  echo "ERROR: expected bump-version duplicate agents component error message" >&2
  exit 1
}

product_version="$(manifest_product_version "$ROOT_DIR/release-manifest.json")"
if validate_release_tag_matches_product_version "v${product_version}" "$ROOT_DIR/release-manifest.json"; then
  :
else
  echo "ERROR: matching release tag validation should succeed" >&2
  exit 1
fi
if validate_release_tag_matches_product_version "v9.9.9" "$ROOT_DIR/release-manifest.json" > /dev/null 2>&1; then
  echo "ERROR: release tag validation should reject manifest mismatch" >&2
  exit 1
fi
RELEASE_TAG="v9.9.9" ./scripts/resolve-release-plan.sh "$ROOT_DIR/release-manifest.json" > /dev/null 2>&1 && {
  echo "ERROR: resolve-release-plan should reject mismatched RELEASE_TAG" >&2
  exit 1
}

current_db="$(manifest_database_postgres_version "$ROOT_DIR/release-manifest.json")"

# 独立字段：只改 api bounds、module bounds 或 contract 时，其它字段须保持不变
api_bounds_preview="$(mktemp)"
api_bounds_out="$(./scripts/bump-version.sh \
  --component-min-db "api=$current_db" \
  --component-max-db "api=$current_db" \
  --output "$api_bounds_preview" \
  --dry-run 2>&1)" || {
  echo "ERROR: bump-version api min/maxDbSchema independent update failed" >&2
  echo "$api_bounds_out" >&2
  exit 1
}
jq -e --arg db "$current_db" --slurpfile root "$ROOT_DIR/release-manifest.json" '
  .components.api.minDbSchema == $db and
  .components.api.maxDbSchema == $db and
  .components.api.version == $root[0].components.api.version and
  .components.desktop.avalonia.version == $root[0].components.desktop.avalonia.version and
  .components.agents.minDesktop == $root[0].components.agents.minDesktop and
  .components.agents.maxDesktop == $root[0].components.agents.maxDesktop
' "$api_bounds_preview" > /dev/null || {
  echo "ERROR: api bounds bump should only touch api min/maxDbSchema" >&2
  exit 1
}
rm -f "$api_bounds_preview"

module_bounds_preview="$(mktemp)"
current_module_contract="$(jq -r '.components.agents.modules.Injector.minApiContract' "$ROOT_DIR/release-manifest.json")"
module_bounds_out="$(./scripts/bump-version.sh \
  --module-min-api-contract "Injector=$current_module_contract" \
  --module-max-api-contract "Injector=$current_module_contract" \
  --output "$module_bounds_preview" \
  --dry-run 2>&1)" || {
  echo "ERROR: bump-version module min/maxApiContract independent update failed" >&2
  echo "$module_bounds_out" >&2
  exit 1
}
jq -e --arg c "$current_module_contract" --slurpfile root "$ROOT_DIR/release-manifest.json" '
  .components.agents.modules.Injector.minApiContract == $c and
  .components.agents.modules.Injector.maxApiContract == $c and
  .components.agents.modules.Injector.version == $root[0].components.agents.modules.Injector.version and
  .components.agents.modules.Scanner == $root[0].components.agents.modules.Scanner
' "$module_bounds_preview" > /dev/null || {
  echo "ERROR: module bounds bump should only touch target module min/maxApiContract" >&2
  exit 1
}
rm -f "$module_bounds_preview"

contract_preview="$(mktemp)"
current_contract="$(jq -r '.components.api.contractVersion' "$ROOT_DIR/release-manifest.json")"
contract_out="$(./scripts/bump-version.sh \
  --api-contract "$current_contract" \
  --output "$contract_preview" \
  --dry-run 2>&1)" || {
  echo "ERROR: bump-version --api-contract independent update failed" >&2
  echo "$contract_out" >&2
  exit 1
}
jq -e --arg c "$current_contract" --slurpfile root "$ROOT_DIR/release-manifest.json" '
  .components.api.contractVersion == $c and
  .components.api.version == $root[0].components.api.version
' "$contract_preview" > /dev/null || {
  echo "ERROR: --api-contract should not change api packaging version" >&2
  exit 1
}
rm -f "$contract_preview"

# 显式升 desktop 不得改写 agents minDesktop / maxDesktop
desktop_no_pin_preview="$(mktemp)"
current_desktop="$(manifest_desktop_version "$ROOT_DIR/release-manifest.json")"
current_agents_min="$(jq -r '.components.agents.minDesktop' "$ROOT_DIR/release-manifest.json")"
current_agents_max="$(jq -r '.components.agents.maxDesktop' "$ROOT_DIR/release-manifest.json")"
desktop_no_pin_out="$(./scripts/bump-version.sh \
  --desktop "$current_desktop" \
  --product "$current_desktop" \
  --output "$desktop_no_pin_preview" \
  --dry-run 2>&1)" || {
  echo "ERROR: bump-version desktop-only should succeed without auto-pinning agents" >&2
  echo "$desktop_no_pin_out" >&2
  exit 1
}
jq -e --arg amin "$current_agents_min" --arg amax "$current_agents_max" '
  .components.agents.minDesktop == $amin and
  .components.agents.maxDesktop == $amax
' "$desktop_no_pin_preview" > /dev/null || {
  echo "ERROR: desktop bump must not auto rewrite agents min/maxDesktop" >&2
  exit 1
}
rm -f "$desktop_no_pin_preview"

# 只升 agents：product 代填抬 Desktop；单点钉住则整段平移，宽区间只抬越界边
agents_only_preview="$(mktemp)"
current_agents_ver="$(manifest_agents_version "$ROOT_DIR/release-manifest.json")"
IFS='.' read -r a_maj a_min a_pat <<< "$current_agents_ver"
agents_only_next="${a_maj}.${a_min}.$((a_pat + 1))"
agents_only_out="$(./scripts/bump-version.sh \
  --component "agents=$agents_only_next" \
  --output "$agents_only_preview" \
  --dry-run 2>&1)" || {
  echo "ERROR: beta agents-only component bump should succeed" >&2
  echo "$agents_only_out" >&2
  exit 1
}
if [[ "$current_agents_min" == "$current_agents_max" && "$current_agents_max" == "$current_desktop" ]]; then
  jq -e --arg av "$agents_only_next" '
    .components.agents.version == $av and
    .components.desktop.avalonia.version == .product.version and
    .components.agents.minDesktop == .components.desktop.avalonia.version and
    .components.agents.maxDesktop == .components.desktop.avalonia.version
  ' "$agents_only_preview" > /dev/null || {
    echo "ERROR: agents-only bump should pin minDesktop/maxDesktop to product-derived Desktop when previously pinned" >&2
    jq '{product: .product.version, desktop: .components.desktop.avalonia.version, agents: .components.agents}' \
      "$agents_only_preview" >&2
    exit 1
  }
else
  jq -e --arg av "$agents_only_next" --arg amin "$current_agents_min" '
    .components.agents.version == $av and
    .components.desktop.avalonia.version == .product.version and
    .components.agents.minDesktop == $amin and
    .components.agents.maxDesktop == .components.desktop.avalonia.version
  ' "$agents_only_preview" > /dev/null || {
    echo "ERROR: agents-only bump should keep wide minDesktop and raise maxDesktop to product-derived Desktop" >&2
    jq '{product: .product.version, desktop: .components.desktop.avalonia.version, agents: .components.agents}' \
      "$agents_only_preview" >&2
    exit 1
  }
fi
rm -f "$agents_only_preview"

# 显式 --desktop 越过 maxDesktop 且未改 agents 区间时仍应失败
if [[ "$current_agents_max" == "$current_desktop" ]]; then
  out_of_range_desktop="$(./scripts/bump-version.sh \
    --desktop 9.9.9-beta.1 \
    --product 9.9.9-beta.1 \
    --dry-run 2>&1)" && {
    echo "ERROR: explicit desktop above agents.maxDesktop should fail without agents flags" >&2
    exit 1
  }
  echo "$out_of_range_desktop" | grep -Fq "must be <= agents.maxDesktop" || {
    echo "ERROR: expected desktop vs agents.maxDesktop validation error" >&2
    echo "$out_of_range_desktop" >&2
    exit 1
  }
fi

[[ ! -f database/postgres/verify/04_environment_settings.sql ]] || {
  echo "ERROR: environment settings verify should be removed after dropping app_environment_settings" >&2
  exit 1
}
if grep -Fq '04_environment_settings.sql' database/postgres/scripts/lib/verify.sh; then
  echo "ERROR: Bash verify suite should not reference removed environment settings verification" >&2
  exit 1
fi
if grep -Fq '04_environment_settings.sql' database/postgres/scripts/deploy.ps1; then
  echo "ERROR: PowerShell verify suite should not reference removed environment settings verification" >&2
  exit 1
fi
grep -Fq 'obsolete table present: app_environment_settings' database/postgres/verify/01_structure.sql || {
  echo "ERROR: structure verify should reject leftover app_environment_settings" >&2
  exit 1
}
grep -Fq "current_setting('pactoolkits.expected_schema_version'" database/postgres/verify/03_schema_version.sql || {
  echo "ERROR: schema version verification should bridge the psql variable through a session setting" >&2
  exit 1
}
if grep -Fq "pg_try_advisory_lock" database/postgres/scripts/lib/common.sh; then
  echo "ERROR: Bash deploy lock must survive separate psql processes" >&2
  exit 1
fi
grep -Fq 'scripts/prepare-server-release.sh' .github/workflows/publish-release.yml || {
  echo "ERROR: formal release must prepare immutable server snapshots" >&2
  exit 1
}
grep -Fq 'FEED_PATH: ${{ secrets.FEED_PATH }}' .github/workflows/publish-release.yml || {
  echo "ERROR: server publishing must use the FEED_PATH secret" >&2
  exit 1
}
grep -Fq 'release_root="$feed_path/pactoolkits"' .github/workflows/publish-release.yml || {
  echo "ERROR: formal releases must publish below FEED_PATH/pactoolkits" >&2
  exit 1
}
grep -Fq 'release_root="$feed_path/pactoolkits-test"' .github/workflows/publish-release.yml || {
  echo "ERROR: test releases must publish below FEED_PATH/pactoolkits-test" >&2
  exit 1
}
dry_run_publish_guards="$(grep -Fc "github.event_name != 'workflow_dispatch' || inputs.dry_run == false" .github/workflows/release.yml)"
[[ "$dry_run_publish_guards" -eq 2 ]] || {
  echo "ERROR: dry-run release must guard both release-note generation and publishing" >&2
  exit 1
}
velopack_package_version="$(sed -n 's/.*PackageReference Include="Velopack" Version="\([^"]*\)".*/\1/p' apps/desktop-avalonia/src/PacToolkits.Desktop.Avalonia.csproj)"
vpk_tool_version="$(sed -n 's/^[[:space:]]*VPK_VERSION:[[:space:]]*//p' .github/workflows/package-desktop.yml)"
[[ -n "$velopack_package_version" && "$vpk_tool_version" == "$velopack_package_version" ]] || {
  echo "ERROR: vpk tool version must match the Desktop Velopack package version" >&2
  exit 1
}
grep -Fq 'key: ${{ runner.os }}-vpk-${{ env.VPK_VERSION }}' .github/workflows/package-desktop.yml || {
  echo "ERROR: VPK cache identity must use its pinned tool version" >&2
  exit 1
}
if sed -n '/name: Cache dotnet global tools/,/name: Install vpk/p' .github/workflows/package-desktop.yml \
  | grep -Fq 'hashFiles('; then
  echo "ERROR: unrelated workflow changes must not invalidate the VPK cache" >&2
  exit 1
fi
bundle_agents_block="$(sed -n '/name: Bundle agents into desktop publish output/,/name: Pack with vpk/p' .github/workflows/package-desktop.yml)"
collect_agents_block="$(sed -n '/name: Collect desktop release assets/,/name: Upload desktop release artifact/p' .github/workflows/package-desktop.yml)"
grep -Fq 'cp "$source_dir/ReleaseManifest.json"' <<< "$bundle_agents_block" || {
  echo "ERROR: package-desktop must copy Agents ReleaseManifest.json into the Velopack pack tree" >&2
  exit 1
}
grep -Fq 'cp "$source_dir/ReleaseManifest.json"' <<< "$collect_agents_block" || {
  echo "ERROR: package-desktop must include Agents ReleaseManifest.json in the Agents zip" >&2
  exit 1
}
grep -Fq 'key: ${{ runner.os }}-ahk2exe-${{ env.AHK2EXE_TAG }}-${{ env.AHK2EXE_EXE_SHA256 }}' .github/workflows/build-agents.yml || {
  echo "ERROR: Ahk2Exe cache identity must use the pinned tag and executable SHA256" >&2
  exit 1
}
grep -Fq 'tests/PacToolkits.Agents.Contracts.Tests/PacToolkits.Agents.Contracts.Tests.csproj' .github/workflows/build-agents.yml || {
  echo "ERROR: Agents build must validate contracts and every module default settings file" >&2
  exit 1
}
grep -Fq '$entryPath = Join-Path $moduleDirPath "__ci_compile_entry__.ahk"' .github/workflows/build-agents.yml || {
  echo "ERROR: AHK compile entry must stay in the module source directory so A_ScriptDir includes resolve" >&2
  exit 1
}
if sed -n '/name: Cache Ahk2Exe asset/,/name: Resolve Ahk2Exe compiler/p' .github/workflows/build-agents.yml \
  | grep -Fq 'hashFiles('; then
  echo "ERROR: unrelated workflow changes must not invalidate the Ahk2Exe cache" >&2
  exit 1
fi
ahk_modules_cache_block="$(sed -n '/name: Cache AHK module binaries/,/name: Report AHK modules cache status/p' .github/workflows/build-agents.yml)"
for identity in \
  '${{ env.RUNTIME }}' \
  '${{ env.AUTOHOTKEY_VERSION }}' \
  '${{ env.AHK2EXE_TAG }}' \
  '${{ env.AHK2EXE_EXE_SHA256 }}' \
  "runtime/agents/modules/**/main.ahk" \
  "runtime/agents/modules/**/src/**/*.ahk" \
  "runtime/agents/modules/**/assets/**/*.ico" \
  "runtime/agents/modules/**/module.json"; do
  grep -Fq "$identity" <<< "$ahk_modules_cache_block" || {
    echo "ERROR: AHK modules cache identity is missing $identity" >&2
    exit 1
  }
done
for unrelated_input in "settings.json" "settings.schema.json" "ReleaseManifest.json"; do
  if grep -Fq "$unrelated_input" <<< "$ahk_modules_cache_block"; then
    echo "ERROR: $unrelated_input must not invalidate the AHK EXE cache" >&2
    exit 1
  fi
done
if grep -Eq "release-manifest.json|build-agents.yml" <<< "$ahk_modules_cache_block"; then
  echo "ERROR: unrelated manifest or workflow changes must not invalidate the AHK modules cache" >&2
  exit 1
fi

publish_host_block="$(sed -n '/name: Publish Host/,/name: Validate Agents binaries/p' .github/workflows/build-agents.yml)"
staging_create_line="$(grep -nF 'New-Item -ItemType Directory -Path $stagingDir -Force' <<< "$publish_host_block" | cut -d: -f1)"
manifest_copy_line="$(grep -nF 'Copy-Item "runtime/agents/host/ReleaseManifest.json"' <<< "$publish_host_block" | cut -d: -f1)"
if [[ -z "$staging_create_line" || -z "$manifest_copy_line" || "$staging_create_line" -ge "$manifest_copy_line" ]]; then
  echo "ERROR: Agents staging directory must exist before copying the Host release manifest" >&2
  exit 1
fi

gen_secrets="${ROOT_DIR}/apps/api-asp/scripts/gen-dev-secrets.sh"
bash -n "$gen_secrets"
grep -Fq 'PAC_AGENTS_API_KEY=${PAC_AGENTS_API_KEY}' "$gen_secrets" || {
  echo "ERROR: gen-dev-secrets must write PAC_AGENTS_API_KEY" >&2
  exit 1
}
grep -Fq 'Auth__Clients__${AGENTS_ID}__ApiKeyHash=${Agents_ApiKeyHash}' "$gen_secrets" || {
  echo "ERROR: gen-dev-secrets must write agents ApiKeyHash" >&2
  exit 1
}

echo "Tooling tests passed."
