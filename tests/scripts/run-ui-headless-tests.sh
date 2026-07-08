#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
PROJECT="$ROOT/tests/PacToolkits.Desktop.UiTests/PacToolkits.Desktop.UiTests.csproj"

dotnet test "$PROJECT" -c Release --no-restore --no-build -v minimal "$@"
