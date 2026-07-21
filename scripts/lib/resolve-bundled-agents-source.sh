#!/usr/bin/env bash
# 供 package-desktop.yml 步骤共用；依赖环境变量 AGENTS_RUNTIME
# download-artifact 可能嵌套 PacToolkits-Agents-<runtime>-<version>/，也可能平铺在 _agents_artifacts/

resolve_bundled_agents_source() {
  local exe_name="$1"
  local version="$2"
  local candidate
  for candidate in \
    "_agents_artifacts/PacToolkits-Agents-${AGENTS_RUNTIME}-${version}/${exe_name}" \
    "_agents_artifacts/${exe_name}"; do
    if [[ -f "$candidate" ]]; then
      printf '%s\n' "$candidate"
      return 0
    fi
  done
  return 1
}
