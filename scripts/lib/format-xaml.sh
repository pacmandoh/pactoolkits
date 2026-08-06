XAML_STYLER_CONFIG="${XAML_STYLER_CONFIG:-Settings.XamlStyler}"
AXAML_ROOT="${AXAML_ROOT:-apps/desktop-avalonia/src}"
XAML_STYLER_PACKAGE="${XAML_STYLER_PACKAGE:-xamlstyler.console@3.2501.8}"

collect_xaml_format_files() {
  local file
  while IFS= read -r file; do
    [[ "$file" == "$AXAML_ROOT"/* ]] || continue
    case "$file" in
      *.axaml | *.xaml) printf '%s\n' "$file" ;;
    esac
  done < <(format_collect_changed_files)
}

run_xaml_styler() {
  local passive="$1"
  local -a args=(-i -c "$XAML_STYLER_CONFIG")
  if [[ "$passive" == "1" ]]; then
    args+=(-p)
  fi

  if format_changed_enabled; then
    local -a files=()
    local file
    while IFS= read -r file; do
      [[ -n "$file" ]] || continue
      files+=("$file")
    done < <(collect_xaml_format_files)

    if ((${#files[@]} == 0)); then
      echo "xaml format: no changed .axaml files (FORMAT_CHANGED=1); skip"
      return 0
    fi

    echo "xaml format: ${#files[@]} changed file(s) (FORMAT_CHANGED=1)"
    local joined
    joined="$(
      IFS=,
      printf '%s' "${files[*]}"
    )"
    dotnet tool exec "$XAML_STYLER_PACKAGE" -- -f "$joined" "${args[@]}"
  else
    dotnet tool exec "$XAML_STYLER_PACKAGE" -- \
      -d "$AXAML_ROOT" \
      -r \
      "${args[@]}"
  fi
}

format_xaml_apply() {
  run_xaml_styler 0
}

format_xaml_check() {
  run_xaml_styler 1
}
