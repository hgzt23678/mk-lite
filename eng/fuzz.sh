#!/usr/bin/env bash
set -euo pipefail

repository_root=$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)
target=${1:-activitystreams}
duration_seconds=${2:-3600}

case "$target" in
  activitystreams|html) ;;
  *)
    echo "target must be activitystreams or html" >&2
    exit 2
    ;;
esac

if ! command -v afl-fuzz >/dev/null 2>&1; then
  echo "afl-fuzz is required; install a pinned AFL++ package in the fuzz runner image" >&2
  exit 2
fi

if ! [[ "$duration_seconds" =~ ^[0-9]+$ ]] || (( duration_seconds < 60 || duration_seconds > 604800 )); then
  echo "duration-seconds must be between 60 and 604800" >&2
  exit 2
fi

work_directory=$(mktemp -d)
publish_directory="$work_directory/publish"
findings_directory="$work_directory/findings"

dotnet publish "$repository_root/tools/ActivityPub.Fuzz/ActivityPub.Fuzz.csproj" \
  --configuration Release --output "$publish_directory"
dotnet tool run sharpfuzz "$publish_directory/ActivityPub.Federation.dll"

export ACTIVITYPUB_FUZZ_TARGET="$target"
export AFL_SKIP_BIN_CHECK=1
timeout --signal=INT --kill-after=30 "$duration_seconds" \
  afl-fuzz -i "$repository_root/tools/ActivityPub.Fuzz/corpus/$target" \
  -o "$findings_directory" -- \
  dotnet "$publish_directory/ActivityPub.Fuzz.dll"

if find "$findings_directory" -path '*/crashes/id:*' -type f -print -quit | grep -q .; then
  echo "fuzz crashes were found under $findings_directory" >&2
  exit 1
fi

echo "fuzz run completed without a recorded crash; artifacts: $findings_directory"
