#!/usr/bin/env bash
set -euo pipefail

repository_root=$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)
dotnet_command=${DOTNET_COMMAND:-dotnet}
container_name="activitypub-load-postgres-$$"
app_log=$(mktemp)
app_pid=""

cleanup() {
  if [[ -n "$app_pid" ]]; then
    kill -TERM "$app_pid" 2>/dev/null || true
    wait "$app_pid" 2>/dev/null || true
  fi
  docker rm --force "$container_name" >/dev/null 2>&1 || true
  rm -f "$app_log"
}
trap cleanup EXIT

docker run --detach --name "$container_name" \
  --env POSTGRES_DB=activitypub_load \
  --env POSTGRES_USER=activitypub \
  --env POSTGRES_PASSWORD=load-test-only-password \
  --publish 127.0.0.1::5432 \
  postgres:17-alpine >/dev/null

postgres_port=$(docker port "$container_name" 5432/tcp | sed 's/.*://')
postgres_ready=false
for _ in $(seq 1 60); do
  if docker exec "$container_name" pg_isready --username activitypub --dbname activitypub_load >/dev/null 2>&1; then
    postgres_ready=true
    break
  fi
  sleep 1
done
if [[ "$postgres_ready" != true ]]; then
  docker logs "$container_name" >&2
  exit 1
fi

export ASPNETCORE_ENVIRONMENT=Development
export ASPNETCORE_URLS=http://127.0.0.1:5080
export ConnectionStrings__ActivityPub="Host=127.0.0.1;Port=${postgres_port};Database=activitypub_load;Username=activitypub;Password=load-test-only-password"
export Workers__InboxEnabled=false
export Workers__DeliveryEnabled=false
export KeyManagement__Enabled=false
export Media__Enabled=false

cd "$repository_root"
"$dotnet_command" run --project src/ActivityPub.Api --configuration Release --no-build -- migrate
"$dotnet_command" run --project src/ActivityPub.Api --configuration Release --no-build >"$app_log" 2>&1 &
app_pid=$!

for _ in $(seq 1 60); do
  if curl --fail --silent --header 'Host: social.example.invalid' http://127.0.0.1:5080/health/ready >/dev/null 2>&1; then
    break
  fi
  if ! kill -0 "$app_pid" 2>/dev/null; then
    cat "$app_log" >&2
    exit 1
  fi
  sleep 1
done
curl --fail --silent --header 'Host: social.example.invalid' http://127.0.0.1:5080/health/ready >/dev/null

"$dotnet_command" run --project tools/ActivityPub.Load --configuration Release --no-build -- \
  http://127.0.0.1:5080/nodeinfo/2.0 \
  "${LOAD_DURATION_SECONDS:-15}" \
  "${LOAD_CONCURRENCY:-32}" \
  social.example.invalid
