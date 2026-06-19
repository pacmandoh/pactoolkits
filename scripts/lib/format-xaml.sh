#!/usr/bin/env bash

XAML_STYLER_CONFIG="${XAML_STYLER_CONFIG:-Settings.XamlStyler}"
AXAML_ROOT="${AXAML_ROOT:-apps/desktop-avalonia/src}"

format_xaml_apply() {
  dotnet tool run xstyler -- \
    -d "$AXAML_ROOT" \
    -r \
    -i \
    -c "$XAML_STYLER_CONFIG"
}

format_xaml_check() {
  dotnet tool run xstyler -- \
    -d "$AXAML_ROOT" \
    -r \
    -i \
    -p \
    -c "$XAML_STYLER_CONFIG"
}
