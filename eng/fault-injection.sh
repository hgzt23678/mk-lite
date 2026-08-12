#!/usr/bin/env bash
set -euo pipefail

repository_root=$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)
project=${COMPOSE_PROJECT_NAME:-activitypub-chaos-drill}
artifact_directory=${ARTIFACT_DIRECTORY:-"$repository_root/artifacts/chaos-$(date -u +%Y%m%dT%H%M%SZ)"}
mkdir -p "$artifact_directory"

generated_vault_token_file=""
if [[ -z "${AP_VAULT_TOKEN_FILE:-}" ]]; then
  : "${AP_VAULT_TOKEN:?set AP_VAULT_TOKEN}"
  generated_vault_token_file=$(mktemp)
  printf '%s' "$AP_VAULT_TOKEN" >"$generated_vault_token_file"
  # Compose bind-mounts file-backed secrets and therefore preserves host mode.
  # The container is non-root (UID 1654), so this short-lived random path must
  # be readable without changing the production image user.
  chmod 0444 "$generated_vault_token_file"
  export AP_VAULT_TOKEN_FILE="$generated_vault_token_file"
fi

compose() {
  docker compose -p "$project" \
    -f "$repository_root/docker-compose.yml" \
    -f "$repository_root/deploy/chaos/docker-compose.chaos.yml" "$@"
}

proxy_toggle() {
  compose exec -T toxiproxy /toxiproxy-cli toggle "$1" >/dev/null
}

probe() {
  compose run --rm --no-deps api dependency-probe "$@"
}

expect_probe_failure() {
  local name=$1
  shift
  if "$@" >"$artifact_directory/$name.stdout.log" 2>"$artifact_directory/$name.stderr.log"; then
    echo "$name unexpectedly succeeded" >&2
    exit 1
  fi
}

cleanup() {
  compose exec -T toxiproxy /toxiproxy-cli reset >/dev/null 2>&1 || true
  compose down --volumes --remove-orphans >/dev/null 2>&1 || true
  if [[ -n "$generated_vault_token_file" ]]; then
    rm -f -- "$generated_vault_token_file"
  fi
}
trap cleanup EXIT

build_arguments=(--build)
if [[ "${CHAOS_SKIP_BUILD:-false}" == "true" ]]; then
  build_arguments=()
fi
compose up -d "${build_arguments[@]}" api worker toxiproxy
probe postgres | tee "$artifact_directory/postgres-baseline.json"
probe vault | tee "$artifact_directory/vault-baseline.json"
probe media | tee "$artifact_directory/media-baseline.json"
protected_payload=$(probe data-protection-protect | tail -1 | jq -er '.protectedPayload')

proxy_toggle s3
expect_probe_failure s3-outage probe media
proxy_toggle s3
probe media | tee "$artifact_directory/s3-recovered.json"
expect_probe_failure s3-access-denied compose run --rm --no-deps \
  -e AWS_ACCESS_KEY_ID=intentionally-invalid \
  -e AWS_SECRET_ACCESS_KEY=intentionally-invalid api dependency-probe media

proxy_toggle clamav
expect_probe_failure clamav-outage probe media
proxy_toggle clamav
probe media | tee "$artifact_directory/clamav-recovered.json"

proxy_toggle vault
expect_probe_failure vault-outage probe vault
proxy_toggle vault
probe vault | tee "$artifact_directory/vault-recovered.json"
expect_probe_failure vault-access-denied compose run --rm --no-deps \
  -v "$repository_root/deploy/chaos/invalid-vault-token:/tmp/invalid-vault-token:ro" \
  -e VaultTransit__TokenFile=/tmp/invalid-vault-token api dependency-probe vault

compose exec -T toxiproxy /toxiproxy-cli toxic add -t latency -a latency=15000 vault >/dev/null
expect_probe_failure vault-latency probe vault
compose exec -T toxiproxy /toxiproxy-cli toxic remove -n latency_downstream vault >/dev/null
probe vault | tee "$artifact_directory/vault-latency-recovered.json"

proxy_toggle postgres
expect_probe_failure postgres-outage probe postgres
proxy_toggle postgres
probe postgres | tee "$artifact_directory/postgres-recovered.json"
probe data-protection-unprotect "$protected_payload" | tee "$artifact_directory/data-protection-after-db-recovery.json"
expect_probe_failure postgres-access-denied compose run --rm --no-deps \
  -e 'ConnectionStrings__ActivityPub=Host=toxiproxy;Port=15432;Database=activitypub;Username=activitypub;Password=intentionally-invalid;Timeout=3' \
  api dependency-probe postgres

quarantined=$(compose exec -T postgres psql -U activitypub -d activitypub -Atc \
  "SELECT count(*) FROM activitypub.media WHERE state = 'Quarantined'")
jq -n \
  --arg completedAt "$(date -u +%Y-%m-%dT%H:%M:%SZ)" \
  --argjson quarantinedMedia "$quarantined" \
  '{completedAt:$completedAt,scenarios:["s3-outage","s3-denied","clamav-outage","vault-outage","vault-denied","vault-latency","postgres-outage","postgres-denied","data-protection-after-db-recovery"],quarantinedMedia:$quarantinedMedia,passed:($quarantinedMedia >= 2)}' \
  | tee "$artifact_directory/result.json"

if (( quarantined < 2 )); then
  echo "dependency failures did not leave inspectable quarantined media records" >&2
  exit 1
fi
