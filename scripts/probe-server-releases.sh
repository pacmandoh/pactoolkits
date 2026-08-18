#!/usr/bin/env bash
set -euo pipefail

die() {
  printf 'ERROR: %s\n' "$*" >&2
  exit 1
}

[[ $# -eq 4 ]] || die "usage: probe-server-releases.sh ROOT API_VERSION DESKTOP_VERSION AGENTS_VERSION"

release_root="${1%/}"
api_version="$2"
desktop_version="$3"
agents_version="$4"

[[ "$release_root" =~ ^/[A-Za-z0-9._-]+(/[A-Za-z0-9._-]+)+$ ]] \
  || die "release root must be a scoped absolute path"

components=(api desktop agents)
versions=("$api_version" "$desktop_version" "$agents_version")

for index in "${!components[@]}"; do
  component="${components[$index]}"
  version="${versions[$index]}"
  [[ "$version" =~ ^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)(-beta\.(0|[1-9][0-9]*))?$ ]] \
    || die "invalid $component version: $version"
  target_dir="$release_root/$component/releases/$version"
  if [[ -d "$target_dir" ]]; then
    continue
  fi
  if [[ -e "$target_dir" ]]; then
    die "$component release path is not a directory: $target_dir"
  fi
  printf '%s\n' "$component"
done
