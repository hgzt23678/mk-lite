#!/usr/bin/env bash
set -euo pipefail

repository_root=$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)
old_image=${OLD_IMAGE:-activitypub-server:rolling-old}
new_image=${NEW_IMAGE:-activitypub-server:rolling-new}
project=${COMPOSE_PROJECT_NAME:-activitypub-rolling-drill}
artifact_directory=${ARTIFACT_DIRECTORY:-"$repository_root/artifacts/rolling-$(date -u +%Y%m%dT%H%M%SZ)"}
https_port=${AP_HTTPS_PORT:-8443}
probe_log="$artifact_directory/probes.jsonl"
mkdir -p "$artifact_directory"

generated_vault_token_file=""
if [[ -z "${AP_VAULT_TOKEN_FILE:-}" ]]; then
  : "${AP_VAULT_TOKEN:?set AP_VAULT_TOKEN}"
  generated_vault_token_file=$(mktemp)
  printf '%s' "$AP_VAULT_TOKEN" >"$generated_vault_token_file"
  chmod 0444 "$generated_vault_token_file"
  export AP_VAULT_TOKEN_FILE="$generated_vault_token_file"
fi

compose() {
  docker compose -p "$project" \
    -f "$repository_root/docker-compose.yml" \
    -f "$repository_root/deploy/rolling/docker-compose.rolling.yml" "$@"
}

wait_healthy() {
  local service=$1
  local deadline=$(( $(date -u +%s) + 180 ))
  while (( $(date -u +%s) < deadline )); do
    local container
    container=$(compose ps -q "$service")
    if [[ -n "$container" ]] && [[ $(docker inspect --format '{{if .State.Health}}{{.State.Health.Status}}{{else}}{{.State.Status}}{{end}}' "$container") == healthy ]]; then
      return
    fi
    sleep 2
  done
  echo "$service did not become healthy" >&2
  compose logs "$service" >&2
  exit 1
}

wait_edge() {
  local deadline=$(( $(date -u +%s) + 60 ))
  while (( $(date -u +%s) < deadline )); do
    if curl --insecure --fail --silent --connect-timeout 2 --max-time 5 \
      "https://localhost:${https_port}/health/live" >/dev/null; then
      return
    fi
    sleep 1
  done
  echo "Caddy TLS endpoint did not become ready" >&2
  compose logs caddy >&2
  exit 1
}

cleanup() {
  if [[ -n "${probe_pid:-}" ]]; then
    kill "$probe_pid" 2>/dev/null || true
    wait "$probe_pid" 2>/dev/null || true
  fi
  compose --profile rolling down --volumes --remove-orphans >/dev/null 2>&1 || true
  if [[ -n "$generated_vault_token_file" ]]; then
    rm -f -- "$generated_vault_token_file"
  fi
}
trap cleanup EXIT

docker image inspect "$old_image" >/dev/null
docker image inspect "$new_image" >/dev/null
export ACTIVITYPUB_IMAGE="$old_image"
export ACTIVITYPUB_CANARY_IMAGE="$new_image"
compose up -d api worker caddy
wait_healthy api
wait_healthy worker
wait_edge

before_counts=$(compose exec -T postgres psql -U activitypub -d activitypub -Atc \
  "SELECT (SELECT count(*) FROM activitypub.activities)::text || ':' || (SELECT count(*) FROM activitypub.deliveries)::text")
(
  while true; do
    timestamp=$(date -u +%Y-%m-%dT%H:%M:%S.%3NZ)
    if status=$(curl --insecure --silent --show-error --output /dev/null --write-out '%{http_code}' \
        --connect-timeout 2 --max-time 5 "https://localhost:${https_port}/health/live") && [[ "$status" == 200 ]]; then
      jq -nc --arg timestamp "$timestamp" '{timestamp:$timestamp,success:true}'
    else
      jq -nc --arg timestamp "$timestamp" --arg status "${status:-connection-error}" \
        '{timestamp:$timestamp,success:false,status:$status}'
    fi
    sleep 0.2
  done
) > "$probe_log" &
probe_pid=$!

export ACTIVITYPUB_IMAGE="$new_image"
compose run --rm migrate
export ACTIVITYPUB_IMAGE="$old_image"
compose --profile rolling up -d --no-deps api-canary worker-canary
wait_healthy api-canary
wait_healthy worker-canary
compose stop --timeout 60 api worker
export ACTIVITYPUB_IMAGE="$new_image"
compose --profile rolling up -d --no-deps api worker
wait_healthy api
wait_healthy worker
compose --profile rolling stop --timeout 60 api-canary worker-canary
sleep 5

kill "$probe_pid"
wait "$probe_pid" 2>/dev/null || true
probe_pid=
after_counts=$(compose exec -T postgres psql -U activitypub -d activitypub -Atc \
  "SELECT (SELECT count(*) FROM activitypub.activities)::text || ':' || (SELECT count(*) FROM activitypub.deliveries)::text")
failures=$(jq -s 'map(select(.success == false)) | length' "$probe_log")
jq -n \
  --arg oldImage "$old_image" \
  --arg newImage "$new_image" \
  --arg beforeCounts "$before_counts" \
  --arg afterCounts "$after_counts" \
  --argjson probeFailures "$failures" \
  '{oldImage:$oldImage,newImage:$newImage,beforeCounts:$beforeCounts,afterCounts:$afterCounts,probeFailures:$probeFailures,passed:($probeFailures == 0 and $beforeCounts == $afterCounts)}' \
  | tee "$artifact_directory/result.json"

if (( failures != 0 )) || [[ "$before_counts" != "$after_counts" ]]; then
  exit 1
fi
