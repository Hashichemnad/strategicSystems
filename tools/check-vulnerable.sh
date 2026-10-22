#!/usr/bin/env bash
# Fails when any NuGet package (direct or transitive) has a High or Critical advisory (CLAUDE.md §16).
set -euo pipefail
cd "$(dirname "$0")/.."
dotnet list Strategy.slnx package --vulnerable --include-transitive --format json | python3 tools/check_vulnerable.py
