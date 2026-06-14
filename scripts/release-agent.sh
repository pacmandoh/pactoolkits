#!/usr/bin/env bash
set -euo pipefail
echo "WARN: release-agent.sh is deprecated; use ./scripts/release-agent-injector-ahk.sh" >&2
exec "$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)/release-agent-injector-ahk.sh" "$@"
