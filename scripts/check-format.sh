#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"

# shellcheck source=lib/format-changed.sh
source "$ROOT_DIR/scripts/lib/format-changed.sh"
# shellcheck source=lib/format-dotnet.sh
source "$ROOT_DIR/scripts/lib/format-dotnet.sh"
# shellcheck source=lib/format-text.sh
source "$ROOT_DIR/scripts/lib/format-text.sh"
# shellcheck source=lib/format-xaml.sh
source "$ROOT_DIR/scripts/lib/format-xaml.sh"
# shellcheck source=lib/format-ahk.sh
source "$ROOT_DIR/scripts/lib/format-ahk.sh"

cd "$ROOT_DIR"

run_dotnet_format --verify-no-changes
format_text_check
format_xaml_check
format_ahk_check
