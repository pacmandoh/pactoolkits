#!/usr/bin/env bash

XAML_STYLER_CONFIG="${XAML_STYLER_CONFIG:-Settings.XamlStyler}"
AXAML_ROOT="${AXAML_ROOT:-apps/desktop-avalonia/src}"
DOTNET_TOOL_MANIFEST="${DOTNET_TOOL_MANIFEST:-dotnet-tools.json}"

ensure_xaml_styler() {
  [[ -f "$DOTNET_TOOL_MANIFEST" ]] || {
    echo "ERROR: $DOTNET_TOOL_MANIFEST not found (xamlstyler.console required)" >&2
    exit 1
  }
  dotnet tool restore --tool-manifest "$DOTNET_TOOL_MANIFEST"
}

format_xaml_apply() {
  ensure_xaml_styler
  dotnet tool run xstyler -- \
    -d "$AXAML_ROOT" \
    -r \
    -i \
    -c "$XAML_STYLER_CONFIG"
}

format_xaml_check() {
  ensure_xaml_styler
  dotnet tool run xstyler -- \
    -d "$AXAML_ROOT" \
    -r \
    -i \
    -p \
    -c "$XAML_STYLER_CONFIG"
}
