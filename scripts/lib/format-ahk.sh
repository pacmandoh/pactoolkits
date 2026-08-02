#!/usr/bin/env bash

# AutoHotkey v2 format via thqby vscode-autohotkey2-lsp (pinned VSIX + LSP formatting).
# Matches VS Code AutoHotkey2.FormatOptions used in .vscode/settings.json.

AHK2_LSP_VERSION="${AHK2_LSP_VERSION:-3.0.10}"
AHK2_LSP_SHA256="${AHK2_LSP_SHA256:-112b21a59936cc6f000ea873aa988060afd6ea35a69724b3c9fdf5219c655998}"
AHK2_LSP_URL="${AHK2_LSP_URL:-https://github.com/thqby/vscode-autohotkey2-lsp/releases/download/v${AHK2_LSP_VERSION}/vscode-autohotkey2-lsp-${AHK2_LSP_VERSION}.vsix}"
AHK_FORMAT_ROOTS=(runtime/agents)
AHK2_FORMAT_MJS="${AHK2_FORMAT_MJS:-$ROOT_DIR/scripts/lib/ahk2-format.mjs}"

ahk2_cache_dir() {
  echo "${AHK2_LSP_DIR:-$ROOT_DIR/.tmp/ahk2-lsp/${AHK2_LSP_VERSION}}"
}

ahk2_load_nvm_node() {
  command -v node > /dev/null && return 0

  local nvm_dir="${NVM_DIR:-$HOME/.nvm}"
  local nvm_sh="$nvm_dir/nvm.sh"
  [[ -s "$nvm_sh" ]] || return 0

  local had_nounset=0
  case $- in
    *u*)
      had_nounset=1
      set +u
      ;;
  esac

  # shellcheck source=/dev/null
  . "$nvm_sh"

  if [[ -f "$ROOT_DIR/.nvmrc" ]]; then
    nvm use --silent > /dev/null 2>&1 || true
  else
    nvm use --silent default > /dev/null 2>&1 || true
  fi

  if ((had_nounset)); then
    set -u
  fi
}

ahk2_sha256() {
  local file="$1"
  if command -v sha256sum > /dev/null; then
    sha256sum "$file" | awk '{print $1}'
  else
    shasum -a 256 "$file" | awk '{print $1}'
  fi
}

require_ahk_format_tools() {
  ahk2_load_nvm_node
  command -v node > /dev/null || {
    echo "ERROR: node not found (Node.js is required for AHK format; run 'nvm use' or set a default nvm alias)" >&2
    exit 1
  }
  command -v curl > /dev/null || {
    echo "ERROR: curl not found (required to download ahk2 LSP VSIX)" >&2
    exit 1
  }
  command -v unzip > /dev/null || {
    echo "ERROR: unzip not found (required to extract ahk2 LSP VSIX)" >&2
    exit 1
  }
  [[ -f "$AHK2_FORMAT_MJS" ]] || {
    echo "ERROR: missing AHK format helper: $AHK2_FORMAT_MJS" >&2
    exit 1
  }
}

ensure_ahk2_lsp_server() {
  local cache
  cache="$(ahk2_cache_dir)"
  local server="$cache/extension/server/dist/server.js"
  local vsix="$cache/vscode-autohotkey2-lsp-${AHK2_LSP_VERSION}.vsix"
  local stamp="$cache/.sha256"

  if [[ -f "$server" && -f "$stamp" ]] && [[ "$(cat "$stamp")" == "$AHK2_LSP_SHA256" ]]; then
    printf '%s\n' "$server"
    return 0
  fi

  mkdir -p "$cache"
  echo "ahk2 LSP: fetching v${AHK2_LSP_VERSION}" >&2
  curl -fsSL "$AHK2_LSP_URL" -o "$vsix"

  local actual
  actual="$(ahk2_sha256 "$vsix")"
  if [[ "$actual" != "$AHK2_LSP_SHA256" ]]; then
    echo "ERROR: ahk2 LSP VSIX sha256 mismatch" >&2
    echo "  expected: $AHK2_LSP_SHA256" >&2
    echo "  actual:   $actual" >&2
    rm -f "$vsix"
    exit 1
  fi

  rm -rf "$cache/extension"
  unzip -qo "$vsix" "extension/server/dist/*" -d "$cache"
  [[ -f "$server" ]] || {
    echo "ERROR: ahk2 LSP server.js missing after extract: $server" >&2
    exit 1
  }
  printf '%s\n' "$AHK2_LSP_SHA256" > "$stamp"
  printf '%s\n' "$server"
}

find_ahk_files() {
  local root
  for root in "${AHK_FORMAT_ROOTS[@]}"; do
    [[ -d "$ROOT_DIR/$root" ]] || continue
    find "$ROOT_DIR/$root" -type f -name '*.ahk' -print
  done | LC_ALL=C sort
}

# FORMAT_CHANGED：相对路径；全量：绝对路径（与历史 find 行为一致）
collect_ahk_format_files() {
  local file root
  if format_changed_enabled; then
    while IFS= read -r file; do
      [[ "$file" == *.ahk ]] || continue
      for root in "${AHK_FORMAT_ROOTS[@]}"; do
        if [[ "$file" == "$root"/* ]]; then
          printf '%s\n' "$ROOT_DIR/$file"
          break
        fi
      done
    done < <(format_collect_changed_files)
  else
    find_ahk_files
  fi
}

run_ahk_format() {
  local mode="$1"
  require_ahk_format_tools

  local -a files=()
  local file
  while IFS= read -r file; do
    [[ -n "$file" ]] || continue
    files+=("$file")
  done < <(collect_ahk_format_files)

  if ((${#files[@]} == 0)); then
    if format_changed_enabled; then
      echo "ahk format: no changed .ahk files (FORMAT_CHANGED=1); skip"
    else
      echo "ahk format: no .ahk files under ${AHK_FORMAT_ROOTS[*]}; skip"
    fi
    return 0
  fi

  local server
  server="$(ensure_ahk2_lsp_server)"
  if format_changed_enabled; then
    echo "ahk format (${mode}): ${#files[@]} changed file(s) via thqby lsp v${AHK2_LSP_VERSION} (FORMAT_CHANGED=1)"
  else
    echo "ahk format (${mode}): ${#files[@]} file(s) via thqby lsp v${AHK2_LSP_VERSION}"
  fi
  node "$AHK2_FORMAT_MJS" --server "$server" "--${mode}" -- "${files[@]}"
}

format_ahk_apply() {
  run_ahk_format write
}

format_ahk_check() {
  run_ahk_format check
}
