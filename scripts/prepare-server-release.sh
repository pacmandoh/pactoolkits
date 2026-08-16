#!/usr/bin/env bash
set -euo pipefail

usage() {
  cat << 'USAGE'
Usage:
  prepare-server-release.sh COMPONENT VERSION CHANNEL SOURCE_DIR OUTPUT_DIR

Environment:
  RELEASE_COMMIT       Published commit SHA.
  RELEASE_CI_RUN       CI run URL or identifier.
  RELEASE_PUBLISHED_AT UTC ISO-8601 timestamp.
USAGE
}

die() {
  printf 'ERROR: %s\n' "$*" >&2
  exit 1
}

[[ $# -eq 5 ]] || {
  usage >&2
  exit 1
}

component="$1"
version="$2"
channel="$3"
source_dir="$4"
output_dir="$5"

case "$component" in
  api | desktop | agents) ;;
  *) die "unsupported component: $component" ;;
esac
[[ "$version" =~ ^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)(-beta\.(0|[1-9][0-9]*))?$ ]] \
  || die "invalid release version: $version"
case "$channel" in
  stable | beta) ;;
  *) die "unsupported release channel: $channel" ;;
esac
[[ -n "${RELEASE_COMMIT:-}" ]] || die "RELEASE_COMMIT is required"
[[ -n "${RELEASE_CI_RUN:-}" ]] || die "RELEASE_CI_RUN is required"
[[ -n "${RELEASE_PUBLISHED_AT:-}" ]] || die "RELEASE_PUBLISHED_AT is required"
[[ -d "$source_dir" ]] || die "source directory not found: $source_dir"
[[ ! -e "$output_dir" ]] || die "output already exists: $output_dir"

shopt -s dotglob nullglob
source_files=("$source_dir"/*)
[[ ${#source_files[@]} -gt 0 ]] || die "source directory is empty: $source_dir"

mkdir -p "$output_dir"
cp -R "${source_files[@]}" "$output_dir/"

jq -n \
  --arg version "$version" \
  --arg commit "$RELEASE_COMMIT" \
  --arg ciRun "$RELEASE_CI_RUN" \
  --arg channel "$channel" \
  --arg publishedAt "$RELEASE_PUBLISHED_AT" \
  '{version: $version, commit: $commit, ciRun: $ciRun, channel: $channel, publishedAt: $publishedAt}' \
  > "$output_dir/release.json"

hash_file() {
  local path="$1"
  if command -v sha256sum > /dev/null 2>&1; then
    sha256sum "$path" | awk '{print $1}'
  else
    shasum -a 256 "$path" | awk '{print $1}'
  fi
}

while IFS= read -r path; do
  relative_path="${path#"$output_dir/"}"
  printf '%s  %s\n' "$(hash_file "$path")" "$relative_path"
done < <(find "$output_dir" -type f ! -name SHA256SUMS | LC_ALL=C sort) \
> "$output_dir/SHA256SUMS"
