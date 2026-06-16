#!/usr/bin/env bash

PRETTIER_VERSION="${PRETTIER_VERSION:-3.5.3}"
SHFMT="${SHFMT:-shfmt}"
TEXT_FORMAT_PATHS=(scripts docs .github/workflows)
SHFMT_FLAGS=(-i 2 -ci -bn -sr)

require_text_format_tools() {
  command -v "$SHFMT" > /dev/null || {
    echo "ERROR: shfmt not found (set SHFMT or install https://github.com/mvdan/sh)" >&2
    exit 1
  }
  command -v npx > /dev/null || {
    echo "ERROR: npx not found (Node.js required for Prettier)" >&2
    exit 1
  }
}

find_script_shell_files() {
  find scripts -name '*.sh' -type f -print0
}

format_text_apply() {
  require_text_format_tools

  local shell_files=()
  while IFS= read -r -d '' file; do
    shell_files+=("$file")
  done < <(find_script_shell_files)

  if ((${#shell_files[@]} > 0)); then
    "$SHFMT" "${SHFMT_FLAGS[@]}" -w "${shell_files[@]}"
  fi

  npx --yes "prettier@${PRETTIER_VERSION}" --write "${TEXT_FORMAT_PATHS[@]}"
}

format_text_check() {
  require_text_format_tools

  local shell_files=()
  while IFS= read -r -d '' file; do
    shell_files+=("$file")
  done < <(find_script_shell_files)

  if ((${#shell_files[@]} > 0)); then
    "$SHFMT" "${SHFMT_FLAGS[@]}" -d "${shell_files[@]}"
  fi

  npx --yes "prettier@${PRETTIER_VERSION}" --check "${TEXT_FORMAT_PATHS[@]}"
}
