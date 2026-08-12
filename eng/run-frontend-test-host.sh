#!/usr/bin/env bash
set -euo pipefail

script_dir="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
repo_root="$(cd -- "$script_dir/.." && pwd)"
dotnet_command="${DOTNET_HOST_PATH:-}"
if [[ -z "$dotnet_command" ]]; then
  dotnet_command="$(command -v dotnet || true)"
fi

if [[ -z "$dotnet_command" && -x /root/.dotnet/dotnet ]]; then
  dotnet_command=/root/.dotnet/dotnet
fi

if [[ -z "$dotnet_command" ]]; then
  echo "The .NET SDK executable was not found." >&2
  exit 127
fi

test_host_project="$repo_root/tests/ActivityPub.Misskey.Blazor.TestHost/ActivityPub.Misskey.Blazor.TestHost.csproj"
publish_directory="${FRONTEND_TEST_PUBLISH_DIRECTORY:-$repo_root/artifacts/frontend-test-host/publish}"

"$dotnet_command" publish "$test_host_project" \
  --configuration Release \
  --no-restore \
  --output "$publish_directory" \
  -p:UseAppHost=false

cd "$publish_directory"
exec env ASPNETCORE_ENVIRONMENT=Production \
  "$dotnet_command" "$publish_directory/ActivityPub.Misskey.Blazor.TestHost.dll" \
  --urls "http://127.0.0.1:${FRONTEND_TEST_PORT:-5099}"
