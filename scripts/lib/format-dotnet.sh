# Single-pass whitespace + style + analyzers (same coverage as the old 3-subcommand sequence).
# FORMAT_CHANGED=1 时只 --include git 变更的 *.cs（见 format-changed.sh）

run_dotnet_format() {
  local -a cmd=(
    dotnet format PacToolkits.sln
    --severity warn
    --verbosity minimal
    --no-restore
  )

  if format_changed_enabled; then
    local -a cs=()
    local file
    while IFS= read -r file; do
      cs+=("$file")
    done < <(format_collect_changed_files '*.cs')

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
