#!/usr/bin/env bash
set -euo pipefail

repository_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
versions_file="$repository_root/deploy/pasture/versions.env"

set -a
# shellcheck source=/dev/null
source "$versions_file"
set +a

pasture_repository="https://codeberg.org/funfedidev/fediverse-pasture.git"
pasture_directory="${FEDIVERSE_PASTURE_DIR:-$repository_root/.cache/fediverse-pasture}"
project_name="${FEDIVERSE_PASTURE_PROJECT_NAME:-activitypub-pasture}"
local_environment_file="${ACTIVITYPUB_ENV_FILE:-$repository_root/.env}"

fetch_pasture() {
  if [[ ! -d "$pasture_directory/.git" ]]; then
    mkdir -p "$(dirname "$pasture_directory")"
    git clone --no-checkout "$pasture_repository" "$pasture_directory"
    git -C "$pasture_directory" checkout --detach "$FEDIVERSE_PASTURE_REF"
  fi

  actual_remote="$(git -C "$pasture_directory" remote get-url origin)"
  actual_ref="$(git -C "$pasture_directory" rev-parse HEAD)"
  if [[ "$actual_remote" != "$pasture_repository" ]]; then
    echo "Unexpected fediverse-pasture origin: $actual_remote" >&2
    exit 1
  fi

  if [[ "$actual_ref" != "$FEDIVERSE_PASTURE_REF" ]]; then
    echo "fediverse-pasture is at $actual_ref; expected $FEDIVERSE_PASTURE_REF." >&2
    echo "Move or update FEDIVERSE_PASTURE_DIR explicitly; this script will not overwrite an existing checkout." >&2
    exit 1
  fi
}

require_local_environment() {
  if [[ ! -f "$local_environment_file" ]]; then
    echo "Create $repository_root/.env from .env.example or set ACTIVITYPUB_ENV_FILE." >&2
    exit 1
  fi
}

prepare_oidc_realm() {
  require_local_environment
  set -a
  # shellcheck source=/dev/null
  source "$local_environment_file"
  set +a

  : "${AP_OIDC_ADMIN_PASSWORD:?set AP_OIDC_ADMIN_PASSWORD in the local environment file}"
  : "${AP_OIDC_ALICE_PASSWORD:?set AP_OIDC_ALICE_PASSWORD in the local environment file}"
  : "${AP_OIDC_REALM_FILE:?set AP_OIDC_REALM_FILE in the local environment file}"
  if [[ "$AP_OIDC_REALM_FILE" != /* ]]; then
    echo "AP_OIDC_REALM_FILE must be an absolute path outside the repository." >&2
    exit 1
  fi
  case "$AP_OIDC_REALM_FILE" in
    "$repository_root"/*)
      echo "AP_OIDC_REALM_FILE must be outside the repository." >&2
      exit 1
      ;;
  esac
  if [[ ! "$AP_OIDC_ALICE_PASSWORD" =~ ^[[:alnum:]]{32,}$ ]]; then
    echo "AP_OIDC_ALICE_PASSWORD must contain at least 32 alphanumeric characters." >&2
    exit 1
  fi

  mkdir -p "$(dirname "$AP_OIDC_REALM_FILE")"
  realm_temp="$(mktemp "$(dirname "$AP_OIDC_REALM_FILE")/pasture-realm.XXXXXX")"
  if [[ -n "${AP_TAILSCALE_ORIGIN:-}" ]]; then
    if [[ ! "$AP_TAILSCALE_ORIGIN" =~ ^https://[A-Za-z0-9.-]+(:[0-9]{1,5})?$ ]]; then
      echo "AP_TAILSCALE_ORIGIN must be an HTTPS origin without a path, query, or fragment." >&2
      exit 1
    fi
    sed "s/__AP_OIDC_ALICE_PASSWORD__/$AP_OIDC_ALICE_PASSWORD/g" \
      "$repository_root/deploy/pasture/keycloak-realm.template.json" |
      jq --arg origin "$AP_TAILSCALE_ORIGIN" '
        (.clients[] | select(.clientId == "activitypub-web") | .redirectUris) += [$origin + "/auth/callback"] |
        (.clients[] | select(.clientId == "activitypub-web") | .webOrigins) += [$origin] |
        (.clients[] | select(.clientId == "activitypub-web") | .attributes["post.logout.redirect.uris"]) += "##" + $origin + "/*" |
        (.clients[] | select(.clientId == "activitypub-oauth-bridge") | .redirectUris) += [$origin + "/auth/callback"] |
        (.clients[] | select(.clientId == "activitypub-oauth-bridge") | .webOrigins) += [$origin]
      ' > "$realm_temp"
  else
    sed "s/__AP_OIDC_ALICE_PASSWORD__/$AP_OIDC_ALICE_PASSWORD/g" \
      "$repository_root/deploy/pasture/keycloak-realm.template.json" > "$realm_temp"
  fi
  chown 1000:1000 "$realm_temp"
  chmod 0400 "$realm_temp"
  mv "$realm_temp" "$AP_OIDC_REALM_FILE"
}

compose() {
  compose_files=(
    --file "$repository_root/docker-compose.yml"
    --file "$pasture_directory/fediverse-pasture/mastodon.yml"
    --file "$pasture_directory/fediverse-pasture/misskey.yml"
    --file "$pasture_directory/fediverse-pasture/pleroma.yml"
    --file "$repository_root/deploy/pasture/docker-compose.pasture.yml"
  )
  if [[ "${ACTIVITYPUB_PASTURE_TAILSCALE:-false}" == "true" ]]; then
    : "${AP_TAILSCALE_ORIGIN:?set AP_TAILSCALE_ORIGIN for the Tailscale overlay}"
    : "${AP_TAILSCALE_HOST:?set AP_TAILSCALE_HOST for the Tailscale overlay}"
    : "${AP_TAILSCALE_PORT:?set AP_TAILSCALE_PORT for the Tailscale overlay}"
    compose_files+=(--file "$repository_root/deploy/pasture/docker-compose.tailscale.yml")
  fi

  docker compose \
    --project-name "$project_name" \
    --project-directory "$repository_root" \
    --env-file "$local_environment_file" \
    "${compose_files[@]}" \
    "$@"
}

action="${1:-}"
case "$action" in
  fetch)
    fetch_pasture
    ;;
  config)
    fetch_pasture
    compose config --quiet
    ;;
  up)
    fetch_pasture
    if ! docker network inspect fediverse-pasture >/dev/null 2>&1; then
      docker network create --internal --subnet "$PASTURE_NETWORK_SUBNET" fediverse-pasture >/dev/null
    elif [[ "$(docker network inspect --format '{{.Internal}}' fediverse-pasture)" != "true" ]]; then
      echo "The existing fediverse-pasture network is not isolated (--internal)." >&2
      echo "Stop attached containers and recreate that network explicitly before continuing." >&2
      exit 1
    elif [[ "$(docker network inspect --format '{{range .IPAM.Config}}{{.Subnet}}{{end}}' fediverse-pasture)" != "$PASTURE_NETWORK_SUBNET" ]]; then
      echo "The existing fediverse-pasture network does not use the pinned subnet $PASTURE_NETWORK_SUBNET." >&2
      echo "Stop attached containers and recreate that network explicitly before continuing." >&2
      exit 1
    fi
    compose up --build --detach --wait --wait-timeout 300
    ;;
  down)
    fetch_pasture
    require_local_environment
    compose down
    ;;
  status)
    fetch_pasture
    require_local_environment
    compose ps
    ;;
  logs)
    fetch_pasture
    require_local_environment
    shift
    compose logs --follow "$@"
    ;;
  create-actor)
    if [[ $# -lt 2 || $# -gt 3 ]]; then
      echo "Usage: eng/pasture.sh create-actor <username> [display-name]" >&2
      exit 1
    fi
    fetch_pasture
    require_local_environment
    username="$2"
    display_name="${3:-$2}"
    compose run --rm api create-local-actor "$username" "$display_name"
    ;;
  *)
    echo "Usage: eng/pasture.sh fetch|config|up|down|status|logs [service...]|create-actor <username> [display-name]" >&2
    exit 1
    ;;
esac
