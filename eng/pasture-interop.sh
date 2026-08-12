#!/usr/bin/env bash
set -euo pipefail

repository_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
environment_file="${ACTIVITYPUB_ENV_FILE:-$repository_root/.env}"

if [[ ! -f "$environment_file" ]]; then
  echo "Pasture environment file is missing." >&2
  exit 1
fi

set -a
# shellcheck source=/dev/null
source "$environment_file"
set +a

: "${AP_VAULT_TOKEN_FILE:?AP_VAULT_TOKEN_FILE is required}"
state_directory="$(dirname "$AP_VAULT_TOKEN_FILE")/interop-auth"
mkdir -p "$state_directory"
chmod 0700 "$state_directory"

require_isolated_network() {
  local internal subnet
  internal="$(docker network inspect --format '{{.Internal}}' fediverse-pasture)"
  subnet="$(docker network inspect --format '{{range .IPAM.Config}}{{.Subnet}}{{end}}' fediverse-pasture)"
  if [[ "$internal" != "true" || "$subnet" != "172.29.0.0/24" ]]; then
    echo "fediverse-pasture must remain internal on 172.29.0.0/24." >&2
    exit 1
  fi
}

write_secret() {
  local destination="$1"
  local temporary
  temporary="$(mktemp "$state_directory/secret.XXXXXX")"
  chmod 0600 "$temporary"
  cat > "$temporary"
  mv "$temporary" "$destination"
}

bootstrap_mastodon() {
  local raw_file token
  raw_file="$(mktemp "$state_directory/mastodon.XXXXXX")"
  chmod 0600 "$raw_file"
  docker exec activitypub-pasture-mastodon-1 sh -lc '
    runtime_file=$(mktemp)
    tr "\000" "\n" < /proc/1/environ |
      grep -E "^(ACTIVE_RECORD_ENCRYPTION_|SECRET_KEY_BASE=|DB_|DATABASE_URL=|REDIS_|CACHE_REDIS_|SIDEKIQ_REDIS_|LOCAL_DOMAIN=|RAILS_ENV=)" > "$runtime_file"
    while IFS="=" read -r key value; do export "$key=$value"; done < "$runtime_file"
    rm -f "$runtime_file"
    bundle exec rails runner '\''
    account = Account.find_by!(username: "hippo", domain: nil)
    app = Doorkeeper::Application.find_or_create_by!(name: "ActivityPub pasture interop", redirect_uri: "urn:ietf:wg:oauth:2.0:oob") do |candidate|
      candidate.scopes = "read write follow push"
    end
    access = Doorkeeper::AccessToken.create!(application_id: app.id, resource_owner_id: account.user.id, scopes: "read write follow push")
    puts access.plaintext_token
    '\''
  ' > "$raw_file" 2>&1
  token="$(tail -n 1 "$raw_file")"
  rm -f "$raw_file"
  if [[ ! "$token" =~ ^[A-Za-z0-9_-]{32,}$ ]]; then
    echo "Mastodon test token bootstrap failed." >&2
    exit 1
  fi
  printf '%s' "$token" | write_secret "$state_directory/mastodon.token"
}

bootstrap_misskey() {
  local token
  token="$(openssl rand -hex 8)"
  printf '%s' "$token" | write_secret "$state_directory/misskey.token"
  printf "UPDATE \"user\" SET token = '%s' WHERE username = 'kitty' AND host IS NULL;\n" "$token" |
    docker exec -i activitypub-pasture-misskey_db-1 \
      psql -v ON_ERROR_STOP=1 -U postgres -d postgres -X >/dev/null
}

bootstrap_pleroma() {
  local password app_file token_file client_id client_secret access_token body
  password="$(sed -n '/=== "pleroma"/,/=== /p' "$repository_root/.cache/fediverse-pasture/docs/index.md" |
    awk -F'`' '/\|.*full.*\|/{print $4; exit}')"
  if [[ -z "$password" ]]; then
    echo "Pleroma pasture credential fixture was not found." >&2
    exit 1
  fi

  app_file="$(mktemp "$state_directory/pleroma-app.XXXXXX")"
  token_file="$(mktemp "$state_directory/pleroma-token.XXXXXX")"
  chmod 0600 "$app_file" "$token_file"
  body='client_name=ActivityPub%20pasture%20interop&redirect_uris=urn%3Aietf%3Awg%3Aoauth%3A2.0%3Aoob&scopes=read%20write%20follow%20push'
  printf '%s' "$body" | docker exec -i activitypub-pasture-api-1 \
    curl --silent --show-error --fail \
      --header 'Content-Type: application/x-www-form-urlencoded' \
      --data-binary @- http://pleroma/api/v1/apps > "$app_file"
  client_id="$(jq -er '.client_id' "$app_file")"
  client_secret="$(jq -er '.client_secret' "$app_file")"
  body="grant_type=password&username=full&password=$(jq -rn --arg value "$password" '$value|@uri')&scope=read%20write%20follow%20push&client_id=$(jq -rn --arg value "$client_id" '$value|@uri')&client_secret=$(jq -rn --arg value "$client_secret" '$value|@uri')"
  printf '%s' "$body" | docker exec -i activitypub-pasture-api-1 \
    curl --silent --show-error --fail \
      --header 'Content-Type: application/x-www-form-urlencoded' \
      --data-binary @- http://pleroma/oauth/token > "$token_file"
  access_token="$(jq -er '.access_token' "$token_file")"
  printf '%s' "$access_token" | write_secret "$state_directory/pleroma.token"
  rm -f "$app_file" "$token_file"
}

verify_remote_token() {
  local service="$1" token_file="$2" url="$3"
  local token
  token="$(<"$token_file")"
  {
    printf 'header = "Authorization: Bearer %s"\n' "$token"
    printf 'url = "%s"\n' "$url"
    printf 'silent\nshow-error\nfail\n'
  } | docker exec -i activitypub-pasture-api-1 curl --config - >/dev/null
  echo "$service authentication=verified"
}

case "${1:-}" in
  bootstrap-auth)
    require_isolated_network
    bootstrap_mastodon
    bootstrap_misskey
    bootstrap_pleroma
    bash "$repository_root/eng/pasture-oidc.sh" issue >/dev/null
    verify_remote_token mastodon "$state_directory/mastodon.token" http://mastodon/api/v1/accounts/verify_credentials
    verify_remote_token pleroma "$state_directory/pleroma.token" http://pleroma/api/v1/accounts/verify_credentials
    echo "misskey authentication=provisioned"
    echo "local-oidc authentication=verified"
    ;;
  state-directory)
    # Deliberately reports only the directory, never secret contents.
    printf '%s\n' "$state_directory"
    ;;
  *)
    echo "Usage: eng/pasture-interop.sh bootstrap-auth|state-directory" >&2
    exit 1
    ;;
esac
