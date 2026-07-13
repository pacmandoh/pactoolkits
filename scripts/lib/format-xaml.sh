#!/usr/bin/env bash

XAML_STYLER_CONFIG="${XAML_STYLER_CONFIG:-Settings.XamlStyler}"
AXAML_ROOT="${AXAML_ROOT:-apps/desktop-avalonia/src}"
XAML_STYLER_PACKAGE="${XAML_STYLER_PACKAGE:-xamlstyler.console@3.2501.8}"

format_xaml_apply() {
  dotnet tool exec "$XAML_STYLER_PACKAGE" -- \
    -d "$AXAML_ROOT" \
    -r \
    -i \
    -c "$XAML_STYLER_CONFIG"
}

format_xaml_check() {
  dotnet tool exec "$XAML_STYLER_PACKAGE" -- \
    -d "$AXAML_ROOT" \
    -r \
    -i \
    -p \
    -c "$XAML_STYLER_CONFIG"
}
