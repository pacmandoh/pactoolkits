#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
CONFIGURATION="Debug"
STAGE_ONLY="false"

usage() {
  cat << 'USAGE'
Usage:
  run-desktop-with-agents.sh [--configuration Debug|Release] [--stage-only]

Builds Desktop and Agents Host, stages the packaged Agents/Modules layout next
to the Desktop executable, then runs Desktop without rebuilding.

On Windows (Git Bash/MSYS), AHK modules are compiled with the local Ahk2Exe
when Ahk2Exe.exe + AutoHotkey64.exe are found (override with AHK2EXE_PATH /
AHK_BASE_PATH). Keeps Debug/bin module exes across staging; skips Ahk2Exe when
source/version fingerprint matches the sidecar stamp (COMPILE_AHK_FORCE=1
forces rebuild). Otherwise falls back to artifacts or a prebuilt entry under
the module source tree.

Options:
  --configuration C    Build configuration: Debug or Release (default: Debug).
  --stage-only         Build and stage files without starting Desktop.
  -h, --help           Show this help.
USAGE
}

while [[ $# -gt 0 ]]; do
  case "$1" in
    --configuration)
      CONFIGURATION="${2:-}"
      shift 2
      ;;
    --stage-only)
      STAGE_ONLY="true"
      shift
      ;;
    -h | --help)
      usage
      exit 0
      ;;
    *)
      echo "ERROR: unknown arg: $1" >&2
      usage >&2
      exit 1
      ;;
  esac
done

case "$CONFIGURATION" in
  Debug | Release) ;;
  *)
    echo "ERROR: --configuration must be Debug or Release" >&2
    exit 1
    ;;
esac

# shellcheck source=manifest-v2.sh
source "$ROOT_DIR/scripts/manifest-v2.sh"
# shellcheck source=lib/compile-ahk-modules-win.sh
source "$ROOT_DIR/scripts/lib/compile-ahk-modules-win.sh"

for command in dotnet jq; do
  command -v "$command" > /dev/null 2>&1 || {
    echo "ERROR: required command not found: $command" >&2
    exit 1
  }
done

COMPILE_AHK_COMPILER=""
COMPILE_AHK_BASE=""
if compile_ahk_try_resolve_toolchain; then
  echo "Windows AHK toolchain: compiler=$COMPILE_AHK_COMPILER"
  echo "Windows AHK toolchain: base=$COMPILE_AHK_BASE"
elif compile_ahk_is_windows; then
  echo "WARN: Windows detected but Ahk2Exe/AutoHotkey64 not found; falling back to artifacts or source entry" >&2
  echo "WARN: install AutoHotkey v2 + Ahk2Exe, or set AHK2EXE_PATH / AHK_BASE_PATH" >&2
fi

desktop_project="$ROOT_DIR/apps/desktop-avalonia/src/PacToolkits.Desktop.Avalonia.csproj"
host_project="$ROOT_DIR/runtime/agents/host/PacToolkits.Agents.Host.csproj"
desktop_out="$ROOT_DIR/apps/desktop-avalonia/src/bin/$CONFIGURATION/net10.0"
host_out="$ROOT_DIR/runtime/agents/host/bin/$CONFIGURATION/net10.0"
agents_out="$desktop_out/Agents"

"$ROOT_DIR/scripts/export-version.sh"
"$ROOT_DIR/scripts/check-version.sh"

dotnet build "$desktop_project" -c "$CONFIGURATION" --no-restore -v minimal
dotnet build "$host_project" -c "$CONFIGURATION" --no-restore -v minimal

[[ -d "$desktop_out" ]] || {
  echo "ERROR: Desktop output missing: $desktop_out" >&2
  exit 1
}
[[ -d "$host_out" ]] || {
  echo "ERROR: Agents Host output missing: $host_out" >&2
  exit 1
}

# 重建目标目录，使本地布局与发布包保持完全一致的模块集合
case "$agents_out" in
  "$ROOT_DIR/apps/desktop-avalonia/src/bin/"*/net10.0/Agents) ;;
  *)
    echo "ERROR: refusing to replace unexpected Agents path: $agents_out" >&2
    exit 1
    ;;
esac

# 整目录挪走 Modules，避免 rm Agents 时丢掉 Debug/bin 已编好的 exe+stamp
modules_park=""
if [[ -d "$agents_out/Modules" ]]; then
  modules_park="$(mktemp -d "${TMPDIR:-/tmp}/pac-agents-modules.XXXXXX")"
  mv "$agents_out/Modules" "$modules_park/Modules"
fi

rm -rf "$agents_out"
mkdir -p "$agents_out"
cp -R "$host_out/." "$agents_out/"

if [[ -f "$host_out/Agents.exe" ]]; then
  cp "$host_out/Agents.exe" "$agents_out/Agents.exe"
elif [[ -f "$host_out/Agents" ]]; then
  # Unix apphost 保留可执行格式，仅统一文件名以复用发布环境的 Host 路径解析
  cp "$host_out/Agents" "$agents_out/Agents.exe"
  chmod +x "$agents_out/Agents.exe"
  rm -f "$agents_out/Agents"
else
  echo "ERROR: Agents Host executable missing under $host_out" >&2
  [[ -n "$modules_park" ]] && rm -rf "$modules_park"
  exit 1
fi

if [[ -n "$modules_park" && -d "$modules_park/Modules" ]]; then
  rm -rf "$agents_out/Modules"
  mv "$modules_park/Modules" "$agents_out/Modules"
  rmdir "$modules_park" 2> /dev/null || rm -rf "$modules_park"
else
  mkdir -p "$agents_out/Modules"
fi

module_count=0
keep_modules=()
for module_src in "$ROOT_DIR/runtime/agents/modules"/*; do
  [[ -d "$module_src" && -f "$module_src/module.json" ]] || continue

  module_id="$(jq_r '.id // empty' "$module_src/module.json")"
  entry="$(jq_r '.entry["win-x64"] // empty' "$module_src/module.json")"
  [[ -n "$module_id" && -n "$entry" ]] || {
    echo "ERROR: invalid module manifest: $module_src/module.json" >&2
    exit 1
  }

  module_out="$agents_out/Modules/$module_id"
  mkdir -p "$module_out"
  for file in module.json settings.json settings.schema.json; do
    [[ -f "$module_src/$file" ]] || {
      echo "ERROR: missing $file for module=$module_id" >&2
      exit 1
    }
    cp "$module_src/$file" "$module_out/$file"
  done

  staged_entry="$module_out/$entry"
  if [[ -n "$COMPILE_AHK_COMPILER" ]]; then
    compile_ahk_module \
      "$module_src" \
      "$module_id" \
      "$entry" \
      "$staged_entry" \
      "$COMPILE_AHK_COMPILER" \
      "$COMPILE_AHK_BASE" \
      "$ROOT_DIR"
    # 同步到 artifacts，便于非 Windows 机或下次无编译器时复用
    artifacts_entry="$ROOT_DIR/artifacts/agents/win-x64/Modules/$module_id/$entry"
    mkdir -p "$(dirname "$artifacts_entry")"
    cp "$staged_entry" "$artifacts_entry"
  else
    compiled="$ROOT_DIR/artifacts/agents/win-x64/Modules/$module_id/$entry"
    if [[ -f "$compiled" ]]; then
      cp "$compiled" "$staged_entry"
    elif [[ -f "$module_src/$entry" ]]; then
      cp "$module_src/$entry" "$staged_entry"
    else
      echo "WARN: module=$module_id has no compiled $entry; discovery/settings work, start is unavailable" >&2
    fi
  fi

  keep_modules+=("$module_id")
  module_count=$((module_count + 1))
done

# 去掉源码树已不存在的模块目录，避免旧 exe 残留
if [[ -d "$agents_out/Modules" ]]; then
  for leftover in "$agents_out/Modules"/*; do
    [[ -d "$leftover" ]] || continue
    leftover_id="$(basename "$leftover")"
    keep=false
    for id in "${keep_modules[@]+"${keep_modules[@]}"}"; do
      [[ "$id" == "$leftover_id" ]] && {
        keep=true
        break
      }
    done
    [[ "$keep" == true ]] || rm -rf "$leftover"
  done
fi

[[ "$module_count" -gt 0 ]] || {
  echo "ERROR: no modules found under runtime/agents/modules" >&2
  exit 1
}

echo "Desktop packaged-layout staging ready:"
echo "- output: $desktop_out"
echo "- Agents: $agents_out"
echo "- modules: $module_count"

if [[ "$STAGE_ONLY" == "true" ]]; then
  exit 0
fi

exec dotnet run \
  --project "$desktop_project" \
  -c "$CONFIGURATION" \
  --no-build \
  --no-restore
