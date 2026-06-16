#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"

cd "$ROOT_DIR"
run_format() {
  dotnet format PacToolkits.sln whitespace "$@" --verbosity minimal
  dotnet format PacToolkits.sln style "$@" --severity warn --verbosity minimal
  dotnet format PacToolkits.sln analyzers "$@" --severity warn --verbosity minimal
}

run_format "$@"
