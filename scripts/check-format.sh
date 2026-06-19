#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"

# shellcheck source=lib/format-text.sh
source "$ROOT_DIR/scripts/lib/format-text.sh"
# shellcheck source=lib/format-xaml.sh
source "$ROOT_DIR/scripts/lib/format-xaml.sh"

cd "$ROOT_DIR"

run_dotnet_format() {
  dotnet format PacToolkits.sln whitespace "$@" --verbosity minimal
  dotnet format PacToolkits.sln style "$@" --severity warn --verbosity minimal
  dotnet format PacToolkits.sln analyzers "$@" --severity warn --verbosity minimal
}

run_dotnet_format --no-restore --verify-no-changes
format_text_check
format_xaml_check
