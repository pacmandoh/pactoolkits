#!/usr/bin/env bash
# 供 package-desktop.yml 共用，并通过 AGENTS_RUNTIME 选择目标运行时
# download-artifact 可能保留 artifact 顶层目录，也可能直接展开到 _agents_artifacts

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
