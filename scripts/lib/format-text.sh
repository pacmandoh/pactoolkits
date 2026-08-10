PRETTIER_VERSION="${PRETTIER_VERSION:-3.5.3}"
# CI Setup shfmt 从此默认值取版本；本地须安装同版
SHFMT_VERSION="${SHFMT_VERSION:-3.13.1}"
SHFMT="${SHFMT:-shfmt}"
TEXT_FORMAT_PATHS=(scripts docs .github/workflows)
SHFMT_FLAGS=(-i 2 -ci -bn -sr)

load_nvm_node() {
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

  if [[ -f .nvmrc ]]; then
    nvm use --silent > /dev/null 2>&1 || true
  else
    nvm use --silent default > /dev/null 2>&1 || true
  fi

  if ((had_nounset)); then
    set -u
  fi
}

require_text_format_tools() {
  command -v "$SHFMT" > /dev/null || {
    echo "ERROR: shfmt not found (set SHFMT or install https://github.com/mvdan/sh/releases/tag/v${SHFMT_VERSION})" >&2
    exit 1
  }
  local shfmt_actual
  shfmt_actual="$("$SHFMT" --version 2> /dev/null | head -n1 | tr -d '[:space:]')"
  shfmt_actual="${shfmt_actual#v}"
  if [[ "$shfmt_actual" != "$SHFMT_VERSION" ]]; then
    echo "ERROR: shfmt version $shfmt_actual != required $SHFMT_VERSION (install v${SHFMT_VERSION} or set SHFMT/SHFMT_VERSION)" >&2
    exit 1
  fi
  load_nvm_node
  command -v node > /dev/null || {
    echo "ERROR: node not found (Node.js is required for Prettier; run 'nvm use' or set a default nvm alias)" >&2
    exit 1
  }
  command -v npx > /dev/null || {
    echo "ERROR: npx not found (Node.js required for Prettier)" >&2
    exit 1
  }
}

format_text_is_prettier_file() {
  case "$1" in
    *.md | *.yml | *.yaml | *.json | *.js | *.mjs | *.cjs | *.ts | *.tsx | *.css | *.html | *.htm)
      return 0
      ;;
    *)
      return 1
      ;;
  esac
}

format_text_under_paths() {
  local file="$1"
  local root
  for root in "${TEXT_FORMAT_PATHS[@]}"; do
    case "$file" in
      "$root" | "$root"/*) return 0 ;;
    esac
  done
  return 1
}

# 收集 scripts 下 .sh（FORMAT_CHANGED 时仅 git 变更）
collect_shell_format_files() {
  local file
  if format_changed_enabled; then
    while IFS= read -r file; do
      [[ "$file" == scripts/* && "$file" == *.sh ]] || continue
      printf '%s\n' "$file"
    done < <(format_collect_changed_files)
  else
    find scripts -name '*.sh' -type f -print
  fi
}

# 收集 TEXT_FORMAT_PATHS 下 prettier 可解析文件（仅 FORMAT_CHANGED）
collect_prettier_format_files() {
  local file
  while IFS= read -r file; do
    format_text_under_paths "$file" || continue
    format_text_is_prettier_file "$file" || continue
    printf '%s\n' "$file"
  done < <(format_collect_changed_files)
}

format_text_apply() {
  require_text_format_tools

  local -a shell_files=()
  local file
  while IFS= read -r file; do
    [[ -n "$file" ]] || continue
    shell_files+=("$file")
  done < <(collect_shell_format_files)

  if ((${#shell_files[@]} > 0)); then
    if format_changed_enabled; then
      echo "shfmt: ${#shell_files[@]} changed .sh file(s) (FORMAT_CHANGED=1)"
    fi
    "$SHFMT" "${SHFMT_FLAGS[@]}" -w "${shell_files[@]}"
  elif format_changed_enabled; then
    echo "shfmt: no changed .sh files (FORMAT_CHANGED=1); skip"
  fi

  if format_changed_enabled; then
    local -a prettier_files=()
    while IFS= read -r file; do
      [[ -n "$file" ]] || continue
      prettier_files+=("$file")
    done < <(collect_prettier_format_files)

    if ((${#prettier_files[@]} > 0)); then
      echo "prettier: ${#prettier_files[@]} changed file(s) (FORMAT_CHANGED=1)"
      npx --yes "prettier@${PRETTIER_VERSION}" --write "${prettier_files[@]}"
    else
      echo "prettier: no changed text files (FORMAT_CHANGED=1); skip"
    fi
  else
    npx --yes "prettier@${PRETTIER_VERSION}" --write "${TEXT_FORMAT_PATHS[@]}"
  fi
}

format_text_check() {
  require_text_format_tools

  local -a shell_files=()
  local file
  while IFS= read -r file; do
    [[ -n "$file" ]] || continue
    shell_files+=("$file")
  done < <(collect_shell_format_files)

  if ((${#shell_files[@]} > 0)); then
    if format_changed_enabled; then
      echo "shfmt: ${#shell_files[@]} changed .sh file(s) (FORMAT_CHANGED=1)"
    fi
    "$SHFMT" "${SHFMT_FLAGS[@]}" -d "${shell_files[@]}"
  elif format_changed_enabled; then
    echo "shfmt: no changed .sh files (FORMAT_CHANGED=1); skip"
  fi

  if format_changed_enabled; then
    local -a prettier_files=()
    while IFS= read -r file; do
      [[ -n "$file" ]] || continue
      prettier_files+=("$file")
    done < <(collect_prettier_format_files)

    if ((${#prettier_files[@]} > 0)); then
      echo "prettier: ${#prettier_files[@]} changed file(s) (FORMAT_CHANGED=1)"
      npx --yes "prettier@${PRETTIER_VERSION}" --check "${prettier_files[@]}"
    else
      echo "prettier: no changed text files (FORMAT_CHANGED=1); skip"
    fi
  else
    npx --yes "prettier@${PRETTIER_VERSION}" --check "${TEXT_FORMAT_PATHS[@]}"
  fi
}
