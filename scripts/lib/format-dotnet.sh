#!/usr/bin/env bash

# Single-pass whitespace + style + analyzers (same coverage as the old 3-subcommand sequence).
# FORMAT_CHANGED=1 → --include git-changed *.cs only (agent/local commit gate; CI omits this).

collect_changed_cs_files() {
  {
    git -C "$ROOT_DIR" diff --name-only --diff-filter=ACMR HEAD -- '*.cs'
    git -C "$ROOT_DIR" ls-files --others --exclude-standard -- '*.cs'
  } | sort -u
}

run_dotnet_format() {
  local -a cmd=(
    dotnet format PacToolkits.sln
    --severity warn
    --verbosity minimal
    --no-restore
  )

  if [[ "${FORMAT_CHANGED:-}" == "1" ]]; then
    local -a cs=()
    local file
    while IFS= read -r file; do
      [[ -n "$file" && -f "$ROOT_DIR/$file" ]] || continue
      cs+=("$file")
    done < <(collect_changed_cs_files)

    if ((${#cs[@]} == 0)); then
      echo "dotnet format: no changed .cs files (FORMAT_CHANGED=1); skip"
      return 0
    fi

    echo "dotnet format: ${#cs[@]} changed .cs file(s) (FORMAT_CHANGED=1)"
    cmd+=(--include "${cs[@]}")
  fi

  cmd+=("$@")
  "${cmd[@]}"
}
