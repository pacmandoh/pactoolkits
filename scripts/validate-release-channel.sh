#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
# shellcheck source=manifest-v2.sh
source "$ROOT_DIR/scripts/manifest-v2.sh"

usage() {
  cat <<'USAGE'
Usage:
  validate-release-channel.sh [options]

Options:
  --manifest PATH          Manifest V2 file (default: release-manifest.json).
  --tag TAG                Release tag. Required for a formal release.
  --prerelease BOOL        Actual/planned GitHub prerelease flag.
  --feed-root PATH         Feed root before the channel suffix.
  --feed-target PATH       Exact channel feed target to validate.
  --dry-run BOOL           true/false (default: true).
  --confirm BOOL           true/false (default: false).
  -h, --help               Show this help.
USAGE
}

die() {
  printf 'ERROR: %s\n' "$*" >&2
  exit 1
}

normalize_bool() {
  case "${1:-}" in
    true|false) printf '%s' "$1" ;;
    *) die "expected boolean true/false, got: ${1:-<empty>}" ;;
  esac
}

resolve_feed_target() {
  local root="${1%/}"
  local channel="$2"
  [[ -n "$root" ]] || die "feed root is empty"
  case "$root" in
    */stable|*/beta) die "feed root must not include a channel suffix: $root" ;;
  esac
  printf '%s/%s' "$root" "$channel"
}

MANIFEST="$ROOT_DIR/release-manifest.json"
TAG=""
PRERELEASE=""
FEED_ROOT="/feed/pactoolkits"
FEED_TARGET=""
DRY_RUN="true"
CONFIRM="false"

while [[ $# -gt 0 ]]; do
  case "$1" in
    --manifest) MANIFEST="$2"; shift 2 ;;
    --tag) TAG="$2"; shift 2 ;;
    --prerelease) PRERELEASE="$2"; shift 2 ;;
    --feed-root) FEED_ROOT="$2"; shift 2 ;;
    --feed-target) FEED_TARGET="$2"; shift 2 ;;
    --dry-run) DRY_RUN="$2"; shift 2 ;;
    --confirm) CONFIRM="$2"; shift 2 ;;
    -h|--help) usage; exit 0 ;;
    *) die "unknown argument: $1" ;;
  esac
done

DRY_RUN="$(normalize_bool "$DRY_RUN")"
CONFIRM="$(normalize_bool "$CONFIRM")"
[[ -z "$PRERELEASE" ]] || PRERELEASE="$(normalize_bool "$PRERELEASE")"
[[ -f "$MANIFEST" ]] || die "manifest not found: $MANIFEST"

validate_manifest_v2 "$MANIFEST"

channel="$(manifest_release_channel "$MANIFEST")"
version="$(manifest_product_version "$MANIFEST")"
migration_policy="$(manifest_database_migration_policy "$MANIFEST")"
implementation="$(manifest_desktop_implementation "$MANIFEST")"
expected_prerelease="$(expected_release_prerelease "$MANIFEST")"
expected_feed_target="$(resolve_feed_target "$FEED_ROOT" "$channel")"

if [[ -n "$TAG" ]]; then
  validate_release_tag_matches_product_version "$TAG" "$MANIFEST"
fi

if [[ -n "$PRERELEASE" ]]; then
  validate_release_prerelease_flag "$MANIFEST" "$PRERELEASE"
elif [[ "$DRY_RUN" == "false" ]]; then
  die "formal release validation requires --prerelease"
fi

if [[ -z "$FEED_TARGET" ]]; then
  FEED_TARGET="$expected_feed_target"
fi
[[ "${FEED_TARGET%/}" == "$expected_feed_target" ]] ||
  die "feed target must be the exact $channel channel directory: expected $expected_feed_target, got $FEED_TARGET"

case "${FEED_TARGET%/}" in
  */stable|*/beta) ;;
  *) die "feed target must end with /stable or /beta: $FEED_TARGET" ;;
esac

if [[ "$DRY_RUN" == "false" ]]; then
  [[ "$CONFIRM" == "true" ]] || die "formal release requires confirm=true"
  [[ -n "$TAG" ]] || die "formal release requires a tag"
fi

printf 'channel=%s\n' "$channel"
printf 'version=%s\n' "$version"
printf 'feed=%s\n' "$expected_feed_target"
printf 'migrationPolicy=%s\n' "$migration_policy"
printf 'desktopImplementation=%s\n' "$implementation"
printf 'prerelease=%s\n' "$expected_prerelease"
printf 'dryRun=%s\n' "$DRY_RUN"
printf 'confirmed=%s\n' "$CONFIRM"
