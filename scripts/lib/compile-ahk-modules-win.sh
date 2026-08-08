# Windows 本机 Ahk2Exe 编译 AHK 模块（供 run-desktop-with-agents.sh source）
# 依赖：jq_r（manifest-v2.sh）、jq、本机 Ahk2Exe.exe 与 AutoHotkey64.exe
# 可选：cygpath（Git Bash）；可用 AHK2EXE_PATH / AHK_BASE_PATH 覆盖探测路径
# 增量：Debug/bin 已有 exe，且旁路 stamp 的 inputs 指纹与当前源码一致则跳过
# 强制重编：COMPILE_AHK_FORCE=1

compile_ahk_is_windows() {
  case "$(uname -s 2> /dev/null || true)" in
    MINGW* | MSYS* | CYGWIN*) return 0 ;;
  esac
  [[ -n "${WINDIR:-}" ]] && return 0
  return 1
}

compile_ahk_to_win_path() {
  local path="$1"
  if command -v cygpath > /dev/null 2>&1; then
    cygpath -w "$path"
    return 0
  fi
  if [[ "$path" =~ ^/([a-zA-Z])/(.*)$ ]]; then
    local drive="${BASH_REMATCH[1]}"
    local rest="${BASH_REMATCH[2]//\//\\}"
    printf '%s:\\%s\n' "$(printf '%s' "$drive" | tr '[:lower:]' '[:upper:]')" "$rest"
    return 0
  fi
  printf '%s\n' "$path"
}

compile_ahk_to_unix_path() {
  local path="$1"
  [[ -n "$path" ]] || return 1
  if command -v cygpath > /dev/null 2>&1; then
    cygpath -u "$path"
    return 0
  fi
  if [[ "$path" =~ ^([a-zA-Z]):[\\/](.*)$ ]]; then
    local drive="${BASH_REMATCH[1]}"
    local rest="${BASH_REMATCH[2]//\\//}"
    printf '/%s/%s\n' "$(printf '%s' "$drive" | tr '[:upper:]' '[:lower:]')" "$rest"
    return 0
  fi
  printf '%s\n' "$path"
}

compile_ahk_find_file() {
  local candidate
  for candidate in "$@"; do
    [[ -n "$candidate" && -f "$candidate" ]] || continue
    printf '%s\n' "$candidate"
    return 0
  done
  return 1
}

compile_ahk_env_unix() {
  # printenv 可安全读取 PROGRAMFILES(X86) 等含括号名；${!name} 在 bash 中非法
  local name="$1"
  local value
  value="$(printenv "$name" 2> /dev/null || true)"
  [[ -n "$value" ]] || return 1
  compile_ahk_to_unix_path "$value"
}

compile_ahk_resolve_override() {
  local raw="$1"
  [[ -n "$raw" ]] || return 1
  if [[ -f "$raw" ]]; then
    printf '%s\n' "$raw"
    return 0
  fi
  local unix
  unix="$(compile_ahk_to_unix_path "$raw" 2> /dev/null || true)"
  [[ -n "$unix" && -f "$unix" ]] || return 1
  printf '%s\n' "$unix"
}

compile_ahk_resolve_compiler() {
  local pf pf86 userprofile home_cache override
  pf="$(compile_ahk_env_unix PROGRAMFILES 2> /dev/null || true)"
  pf86="$(compile_ahk_env_unix 'PROGRAMFILES(X86)' 2> /dev/null || true)"
  userprofile="$(compile_ahk_env_unix USERPROFILE 2> /dev/null || true)"
  home_cache="${HOME:+$HOME/ahk2exe-cache/Ahk2Exe.exe}"

  override="$(compile_ahk_resolve_override "${AHK2EXE_PATH:-}" 2> /dev/null || true)"
  compile_ahk_find_file \
    "$override" \
    "${userprofile:+$userprofile/ahk2exe-cache/Ahk2Exe.exe}" \
    "$home_cache" \
    "${pf:+$pf/AutoHotkey/Compiler/Ahk2Exe.exe}" \
    "${pf:+$pf/AutoHotkey/Ahk2Exe.exe}" \
    "${pf86:+$pf86/AutoHotkey/Compiler/Ahk2Exe.exe}" \
    "/c/Program Files/AutoHotkey/Compiler/Ahk2Exe.exe" \
    "/c/Program Files/AutoHotkey/Ahk2Exe.exe" \
    || return 1
}

compile_ahk_resolve_base() {
  local pf override
  pf="$(compile_ahk_env_unix PROGRAMFILES 2> /dev/null || true)"
  override="$(compile_ahk_resolve_override "${AHK_BASE_PATH:-}" 2> /dev/null || true)"

  compile_ahk_find_file \
    "$override" \
    "${pf:+$pf/AutoHotkey/v2/AutoHotkey64.exe}" \
    "${pf:+$pf/AutoHotkey/AutoHotkey64.exe}" \
    "${pf:+$pf/AutoHotkey/Compiler/AutoHotkey64.exe}" \
    "${pf:+$pf/AutoHotkey/Compiler/Base Files/AutoHotkey64.exe}" \
    "/c/Program Files/AutoHotkey/v2/AutoHotkey64.exe" \
    "/c/Program Files/AutoHotkey/AutoHotkey64.exe" \
    || return 1
}

# 探测本机编译器；成功时把路径写入 COMPILE_AHK_COMPILER / COMPILE_AHK_BASE
compile_ahk_try_resolve_toolchain() {
  COMPILE_AHK_COMPILER=""
  COMPILE_AHK_BASE=""
  compile_ahk_is_windows || return 1

  local compiler base
  compiler="$(compile_ahk_resolve_compiler)" || return 1
  base="$(compile_ahk_resolve_base)" || return 1
  COMPILE_AHK_COMPILER="$compiler"
  COMPILE_AHK_BASE="$base"
  return 0
}

compile_ahk_sha256() {
  local file="$1"
  if command -v sha256sum > /dev/null 2>&1; then
    sha256sum "$file" | awk '{print $1}'
  else
    shasum -a 256 "$file" | awk '{print $1}'
  fi
}

# 影响 exe 的输入指纹（不含 module.json：export-version 每次重写会误伤缓存）
compile_ahk_inputs_digest() {
  local module_src="$1"
  local root_dir="$2"
  local module_id="$3"
  local icon_path="$4"
  local manifest_version="$5"

  local list tmp path rel
  list="$(mktemp)"
  tmp="$(mktemp)"
  {
    printf '%s\n' "$module_src/main.ahk"
    printf '%s\n' "$icon_path"
    if [[ -d "$module_src/src" ]]; then
      find "$module_src/src" -type f -name '*.ahk' 2> /dev/null || true
    fi
    if [[ -d "$root_dir/runtime/agents/lib/ahk" ]]; then
      find "$root_dir/runtime/agents/lib/ahk" -type f -name '*.ahk' 2> /dev/null || true
    fi
  } | sort -u > "$list"

  {
    printf 'module=%s\n' "$module_id"
    printf 'version=%s\n' "$manifest_version"
    while IFS= read -r path; do
      [[ -f "$path" ]] || continue
      rel="${path#"$root_dir"/}"
      printf '%s %s\n' "$(compile_ahk_sha256 "$path")" "$rel"
    done < "$list"
  } > "$tmp"

  compile_ahk_sha256 "$tmp"
  rm -f "$list" "$tmp"
}

compile_ahk_read_stamp_field() {
  local stamp="$1"
  local key="$2"
  [[ -f "$stamp" ]] || return 1
  local line
  line="$(grep -E "^${key}=" "$stamp" 2> /dev/null | head -n 1 || true)"
  [[ -n "$line" ]] || return 1
  printf '%s\n' "${line#"$key"=}"
}

# 编译单个模块到 out_exe（通常为 Debug/bin/.../Modules/<id>/<entry>）；指纹命中则跳过 Ahk2Exe
compile_ahk_module() {
  local module_src="$1"
  local module_id="$2"
  local entry="$3"
  local out_exe="$4"
  local compiler="$5"
  local base_exe="$6"
  local root_dir="$7"

  local runtime builder icon_rel main_script icon_path
  runtime="$(jq_r '.runtime // empty' "$module_src/module.json")"
  builder="$(jq_r '.package.builder // empty' "$module_src/module.json")"
  if [[ "$runtime" != "ahk" ]]; then
    echo "WARN: skip compile module=$module_id (runtime=$runtime, not ahk)" >&2
    return 1
  fi
  if [[ "$builder" != "ahk2exe" ]]; then
    echo "ERROR: AHK module=$module_id must set package.builder=ahk2exe" >&2
    return 1
  fi

  icon_rel="$(jq_r '.package.ahk2exe.icon // empty' "$module_src/module.json")"
  [[ -n "$icon_rel" ]] || {
    echo "ERROR: module=$module_id missing package.ahk2exe.icon" >&2
    return 1
  }
  icon_path="$module_src/${icon_rel//\\//}"
  [[ -f "$icon_path" ]] || {
    echo "ERROR: module=$module_id icon not found: $icon_path" >&2
    return 1
  }

  main_script="$module_src/main.ahk"
  [[ -f "$main_script" ]] || {
    echo "ERROR: module=$module_id missing main.ahk" >&2
    return 1
  }

  local manifest="$root_dir/release-manifest.json"
  local manifest_version module_version4
  manifest_version="$(jq -r --arg id "$module_id" '.components.agents.modules[$id].version // empty' "$manifest")"
  [[ -n "$manifest_version" ]] || {
    echo "ERROR: release-manifest.json missing components.agents.modules.$module_id" >&2
    return 1
  }
  module_version4="${manifest_version}.0"

  local stamp inputs_digest stamped_inputs
  stamp="${out_exe}.ahk2exe.stamp"
  inputs_digest="$(compile_ahk_inputs_digest "$module_src" "$root_dir" "$module_id" "$icon_path" "$manifest_version")"

  mkdir -p "$(dirname "$out_exe")"
  # 以 Debug/bin 现有 exe 为缓存：源码/版本指纹未变则不调 Ahk2Exe
  if [[ "${COMPILE_AHK_FORCE:-}" != "1" && -f "$out_exe" && -f "$stamp" ]]; then
    stamped_inputs="$(compile_ahk_read_stamp_field "$stamp" inputs || true)"
    if [[ -n "$stamped_inputs" && "$stamped_inputs" == "$inputs_digest" ]]; then
      echo "Skipping $entry for module=$module_id (inputs unchanged, version=$manifest_version)"
      return 0
    fi
  fi

  local entry_path compiler_dir main_w
  entry_path="$module_src/__local_compile_entry__.ahk"
  compiler_dir="$(dirname "$compiler")"
  main_w="$(compile_ahk_to_win_path "$main_script")"

  {
    echo "; 本地 staging：把 release-manifest 模块版本写入 PE 元数据"
    echo ";@Ahk2Exe-Set Version, $module_version4"
    echo ";@Ahk2Exe-Set FileVersion, $module_version4"
    echo ";@Ahk2Exe-Set ProductVersion, $module_version4"
    printf '#Include "%s"\n' "$main_w"
  } > "$entry_path"

  rm -f "$out_exe"

  local in_w out_w base_w icon_w
  in_w="$(compile_ahk_to_win_path "$entry_path")"
  out_w="$(compile_ahk_to_win_path "$out_exe")"
  base_w="$(compile_ahk_to_win_path "$base_exe")"
  icon_w="$(compile_ahk_to_win_path "$icon_path")"

  echo "Compiling $entry for module=$module_id (Ahk2Exe, version=$manifest_version)"

  # 直接调用 Ahk2Exe（与 CI Start-Process 同参形）
  # ARG_CONV_EXCL=* 防止 /in 等开关被改写成盘符；勿再包一层 cmd //c
  # （EXCL=* 时 //c 会原样传入，cmd 不认作 /c，会掉进交互壳只打印版本横幅）
  local rc=0
  (
    cd "$compiler_dir"
    MSYS_NO_PATHCONV=1 MSYS2_ARG_CONV_EXCL='*' \
      "$compiler" \
      /in "$in_w" \
      /out "$out_w" \
      /base "$base_w" \
      /icon "$icon_w" \
      /silent
  ) || rc=$?

  rm -f "$entry_path"

  if [[ "$rc" -ne 0 ]]; then
    echo "ERROR: Ahk2Exe failed (exit=$rc) for module=$module_id" >&2
    return 1
  fi
  if [[ ! -f "$out_exe" ]]; then
    echo "ERROR: Ahk2Exe produced no output: $out_exe (module=$module_id)" >&2
    return 1
  fi

  printf 'inputs=%s\n' "$inputs_digest" > "$stamp"
  return 0
}
