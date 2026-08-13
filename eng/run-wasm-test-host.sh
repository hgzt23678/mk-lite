#!/usr/bin/env bash
set -euo pipefail

export PATH="/root/.dotnet:${PATH}"
export ASPNETCORE_ENVIRONMENT=Development
export ASPNETCORE_URLS=http://127.0.0.1:5101
export WASM_TEST_PUBLIC_BASE_URI=http://127.0.0.1:5101/
export Logging__LogLevel__Default=Warning

repository_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "${repository_root}"

exec dotnet run \
  --project tests/ActivityPub.Misskey.Blazor.WasmTestHost/ActivityPub.Misskey.Blazor.WasmTestHost.csproj \
  --configuration Release \
  --no-launch-profile
