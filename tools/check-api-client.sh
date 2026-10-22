#!/usr/bin/env bash
# Regenerates the OpenAPI document and the TS client types, then fails if either differs from the commit.
# A failure means: run `dotnet build src/Host/Strategy.Host && (cd web && npm run api:generate)` and commit.
set -euo pipefail
cd "$(dirname "$0")/.."
dotnet build src/Host/Strategy.Host/Strategy.Host.csproj -c Release --nologo -v quiet
(cd web && npm run --silent api:generate)
git diff --exit-code -- web/openapi web/src/api/schema.d.ts
echo "OpenAPI document and TS client are up to date."
