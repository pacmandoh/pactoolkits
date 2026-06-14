#!/usr/bin/env bash
set -euo pipefail
echo "WARN: release-ui.sh is deprecated; use ./scripts/release-desktop.sh" >&2
exec "$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)/release-desktop.sh" "$@"
