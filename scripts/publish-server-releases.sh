#!/usr/bin/env bash
set -euo pipefail

die() {
  printf 'ERROR: %s\n' "$*" >&2
  exit 1
}

atomic_link() {
  local target="$1"
  local link="$2"
  local tmp="$3"
  local old="${tmp}.old"

  ln -s "$target" "$tmp" || return 1
  # 已有 symlink 若指向目录，mv 会跟进目录；先挪走旧 symlink
  if [[ -L "$link" || -e "$link" ]]; then
    mv -f "$link" "$old" || {
      rm -f "$tmp"
      return 1
    }
    if ! mv -f "$tmp" "$link"; then
      mv -f "$old" "$link"
      rm -f "$tmp"
      return 1
    fi
    rm -f "$old"
  else
    mv -f "$tmp" "$link" || return 1
  fi
}

[[ $# -eq 6 ]] || die "usage: publish-server-releases.sh ROOT INCOMING CHANNEL API_VERSION DESKTOP_VERSION AGENTS_VERSION"

release_root="${1%/}"
incoming_id="$2"
channel="$3"
api_version="$4"
desktop_version="$5"
agents_version="$6"

[[ "$release_root" =~ ^/[A-Za-z0-9._-]+(/[A-Za-z0-9._-]+)+$ ]] \
  || die "release root must be a scoped absolute path"
[[ "$incoming_id" =~ ^[A-Za-z0-9._-]+$ ]] || die "invalid incoming id: $incoming_id"
[[ "$incoming_id" != "." && "$incoming_id" != ".." ]] || die "invalid incoming id: $incoming_id"
case "$channel" in
  stable) pointer="current" ;;
  beta) pointer="beta" ;;
  *) die "unsupported release channel: $channel" ;;
esac

components=(api desktop agents)
versions=("$api_version" "$desktop_version" "$agents_version")
incoming_root="$release_root/.incoming/$incoming_id"
pointer_previous=()
pointer_existed=()
publish_new=()

for index in "${!components[@]}"; do
  component="${components[$index]}"
  version="${versions[$index]}"
  [[ "$version" =~ ^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)(-beta\.(0|[1-9][0-9]*))?$ ]] \
    || die "invalid $component version: $version"
  source_dir="$incoming_root/$component"
  target_dir="$release_root/$component/releases/$version"
  if [[ -d "$source_dir" ]]; then
    [[ -f "$source_dir/SHA256SUMS" ]] || die "$component SHA256SUMS not found"
    [[ -f "$source_dir/release.json" ]] || die "$component release.json not found"
    (cd "$source_dir" && sha256sum -c SHA256SUMS)
    jq -e \
      --arg version "$version" \
      --arg channel "$channel" \
      '.version == $version and .channel == $channel' \
      "$source_dir/release.json" > /dev/null \
      || die "incoming $component release metadata does not match version and channel"
  fi

  if [[ -d "$target_dir" ]]; then
    [[ -f "$target_dir/SHA256SUMS" && -f "$target_dir/release.json" ]] \
      || die "existing $component release is incomplete: $target_dir"
    (cd "$target_dir" && sha256sum -c SHA256SUMS)
    jq -e \
      --arg version "$version" \
      --arg channel "$channel" \
      '.version == $version and .channel == $channel' \
      "$target_dir/release.json" > /dev/null \
      || die "existing $component release metadata does not match version and channel"
    publish_new+=(false)
  elif [[ -e "$target_dir" ]]; then
    die "$component release path is not a directory: $target_dir"
  elif [[ -d "$source_dir" ]]; then
    publish_new+=(true)
  else
    die "incoming $component release not found: $source_dir"
  fi

  component_root="$release_root/$component"
  if [[ -e "$component_root/$pointer" && ! -L "$component_root/$pointer" ]]; then
    die "$component pointer is not a symbolic link: $component_root/$pointer"
  fi
  if [[ -L "$component_root/$pointer" ]]; then
    pointer_existed+=(true)
    pointer_previous+=("$(readlink "$component_root/$pointer")")
  else
    pointer_existed+=(false)
    pointer_previous+=("")
  fi
  [[ ! -e "$component_root/.${pointer}.${incoming_id}" && ! -L "$component_root/.${pointer}.${incoming_id}" ]] \
    || die "temporary pointer already exists: $component_root/.${pointer}.${incoming_id}"
done

committed=()
pointers_updated=0
rollback() {
  local index component version component_root target_dir source_dir restore_pointer
  trap - ERR
  for ((index = pointers_updated - 1; index >= 0; index--)); do
    component="${components[$index]}"
    component_root="$release_root/$component"
    if [[ "${pointer_existed[$index]}" == "true" ]]; then
      restore_pointer="$component_root/.${pointer}.restore.${incoming_id}"
      atomic_link "${pointer_previous[$index]}" "$component_root/$pointer" "$restore_pointer"
    else
      rm -f "$component_root/$pointer"
    fi
  done
  for ((index = ${#committed[@]} - 1; index >= 0; index--)); do
    component="${committed[$index]}"
    case "$component" in
      api) version="$api_version" ;;
      desktop) version="$desktop_version" ;;
      agents) version="$agents_version" ;;
    esac
    target_dir="$release_root/$component/releases/$version"
    source_dir="$incoming_root/$component"
    [[ ! -d "$target_dir" || -e "$source_dir" ]] || mv "$target_dir" "$source_dir"
  done
  rm -rf "$incoming_root"
}
trap rollback ERR

for index in "${!components[@]}"; do
  component="${components[$index]}"
  version="${versions[$index]}"
  component_root="$release_root/$component"
  mkdir -p "$component_root/releases"
  if [[ "${publish_new[$index]}" == "true" ]]; then
    mv "$incoming_root/$component" "$component_root/releases/$version"
    committed+=("$component")
  fi
done

pointers_current=true
for index in "${!components[@]}"; do
  component="${components[$index]}"
  version="${versions[$index]}"
  component_root="$release_root/$component"
  if [[ "$(readlink "$component_root/$pointer" 2> /dev/null)" != "releases/$version" ]]; then
    pointers_current=false
    break
  fi
done

if [[ "$pointers_current" == "false" ]]; then
  for index in "${!components[@]}"; do
    component="${components[$index]}"
    version="${versions[$index]}"
    component_root="$release_root/$component"
    temporary_pointer="$component_root/.${pointer}.${incoming_id}"
    expected="releases/$version"
    atomic_link "$expected" "$component_root/$pointer" "$temporary_pointer"
    pointers_updated=$((pointers_updated + 1))
  done
fi

trap - ERR
if [[ -d "$incoming_root" ]]; then
  for index in "${!components[@]}"; do
    if [[ "${publish_new[$index]}" == "false" ]]; then
      rm -rf "$incoming_root/${components[$index]}"
    fi
  done
  rmdir "$incoming_root"
fi
