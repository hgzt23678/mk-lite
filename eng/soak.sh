#!/usr/bin/env bash
set -euo pipefail

repository_root=$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)
target_uri=${1:-https://localhost:8443/health/live}
duration_seconds=${2:-86400}
concurrency=${3:-32}
cycle_seconds=${4:-300}
artifact_directory=${5:-"$repository_root/artifacts/soak-$(date -u +%Y%m%dT%H%M%SZ)"}
dotnet_command=${DOTNET_COMMAND:-dotnet}

for value_name in duration_seconds concurrency cycle_seconds; do
  value=${!value_name}
  if ! [[ "$value" =~ ^[0-9]+$ ]]; then
    echo "$value_name must be an integer" >&2
    exit 2
  fi
done
if (( duration_seconds < 3600 || duration_seconds > 604800 )); then
  echo "duration_seconds must be between 3600 and 604800" >&2
  exit 2
fi
if (( concurrency < 1 || concurrency > 2048 || cycle_seconds < 30 || cycle_seconds > 3600 )); then
  echo "concurrency or cycle_seconds is outside the supported range" >&2
  exit 2
fi

mkdir -p "$artifact_directory"
results_file="$artifact_directory/cycles.jsonl"
metadata_file="$artifact_directory/metadata.json"
summary_file="$artifact_directory/summary.json"
started_epoch=$(date -u +%s)
jq -n \
  --arg target "$target_uri" \
  --arg startedAt "$(date -u +%Y-%m-%dT%H:%M:%SZ)" \
  --arg commit "$(git -C "$repository_root" rev-parse HEAD 2>/dev/null || printf uncommitted)" \
  --argjson duration "$duration_seconds" \
  --argjson concurrency "$concurrency" \
  --argjson cycle "$cycle_seconds" \
  '{target:$target,startedAt:$startedAt,commit:$commit,durationSeconds:$duration,concurrency:$concurrency,cycleSeconds:$cycle}' \
  > "$metadata_file"

cycle=0
failed_cycles=0
while (( $(date -u +%s) - started_epoch < duration_seconds )); do
  elapsed=$(( $(date -u +%s) - started_epoch ))
  remaining=$(( duration_seconds - elapsed ))
  current_cycle=$cycle_seconds
  if (( remaining < current_cycle )); then
    current_cycle=$remaining
  fi
  if (( current_cycle < 1 )); then
    break
  fi

  cycle=$(( cycle + 1 ))
  cycle_started=$(date -u +%Y-%m-%dT%H:%M:%SZ)
  if output=$("$dotnet_command" run --project "$repository_root/tools/ActivityPub.Load/ActivityPub.Load.csproj" \
      --configuration Release --no-build -- "$target_uri" "$current_cycle" "$concurrency" 2>&1); then
    jq -c --argjson cycle "$cycle" --arg startedAt "$cycle_started" \
      '. + {cycle:$cycle,startedAt:$startedAt,processExitSucceeded:true}' <<<"$output" >> "$results_file"
  else
    failed_cycles=$(( failed_cycles + 1 ))
    jq -nc --argjson cycle "$cycle" --arg startedAt "$cycle_started" --arg error "$output" \
      '{cycle:$cycle,startedAt:$startedAt,processExitSucceeded:false,error:($error[0:4000])}' >> "$results_file"
  fi
done

jq -s \
  --arg completedAt "$(date -u +%Y-%m-%dT%H:%M:%SZ)" \
  --argjson failedCycles "$failed_cycles" \
  '{completedAt:$completedAt,cycles:length,failedCycles:$failedCycles,requests:(map(.requests // 0)|add),succeeded:(map(.succeeded // 0)|add),failedRequests:(map(.failed // 0)|add),minimumRps:(map(select(.requests != null).requestsPerSecond)|min // 0),maximumP99Milliseconds:(map(select(.requests != null).p99Milliseconds)|max // 0)}' \
  "$results_file" > "$summary_file"

cat "$summary_file"
if (( failed_cycles > 0 )) || (( $(jq -r '.failedRequests' "$summary_file") > 0 )); then
  exit 1
fi
