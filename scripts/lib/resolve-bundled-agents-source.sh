#!/usr/bin/env bash
# Shared by package-desktop.yml steps. Expects AGENTS_RUNTIME in env.
# download-artifact may nest as PacToolkits-Agents-<runtime>-<version>/ or flatten into _agents_artifacts/.

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
