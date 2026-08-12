#!/usr/bin/env bash
set -euo pipefail

repository_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
tailscale_port="${ACTIVITYPUB_TAILSCALE_PORT:-9443}"
pasture_port="${ACTIVITYPUB_PASTURE_PORT:-2971}"

if ! [[ "$tailscale_port" =~ ^[0-9]+$ ]] || ((tailscale_port < 1 || tailscale_port > 65535)); then
  echo "ACTIVITYPUB_TAILSCALE_PORT must be a TCP port from 1 through 65535." >&2
  exit 1
fi

if [[ "$(tailscale status --json | jq -r '.BackendState')" != "Running" ]]; then
  echo "Tailscale is not running." >&2
  exit 1
fi

tailscale_host="$(tailscale status --json | jq -er '.Self.DNSName | rtrimstr(".")')"
if [[ ! "$tailscale_host" =~ ^[A-Za-z0-9.-]+\.ts\.net$ ]]; then
  echo "Tailscale did not return a valid MagicDNS hostname." >&2
  exit 1
fi

export AP_TAILSCALE_HOST="$tailscale_host"
export AP_TAILSCALE_PORT="$tailscale_port"
export AP_TAILSCALE_ORIGIN="https://$tailscale_host:$tailscale_port"
export ACTIVITYPUB_PASTURE_TAILSCALE=true

check_route_available() {
  existing_proxy="$(tailscale serve status --json 2>/dev/null |
    jq -r --arg address "$tailscale_host:$tailscale_port" '.Web[$address].Handlers["/"].Proxy // empty')"
  if [[ -n "$existing_proxy" && "$existing_proxy" != "http://127.0.0.1:$pasture_port" ]]; then
    echo "Tailscale Serve port $tailscale_port already proxies to a different service; refusing to replace it." >&2
    exit 1
  fi
}

verify() {
  # The first request after replacing the Interactive Server container can overlap OIDC
  # metadata warm-up.  Keep each contract bounded, but use the same cold-start budget as the
  # authorization challenge so a healthy deployment is not reported as failed at 20 seconds.
  config="$(curl --silent --show-error --fail --max-time 45 "$AP_TAILSCALE_ORIGIN/api/frontend/config")"
  jq -e \
    --arg origin "$AP_TAILSCALE_ORIGIN" \
    '.authority == ($origin + "/oidc/realms/pasture") and
     .redirectUri == ($origin + "/auth/callback") and
     .postLogoutRedirectUri == ($origin + "/")' \
    <<<"$config" >/dev/null

  discovery="$(curl --silent --show-error --fail --max-time 45 \
    "$AP_TAILSCALE_ORIGIN/oidc/realms/pasture/.well-known/openid-configuration")"
  jq -e \
    --arg issuer "$AP_TAILSCALE_ORIGIN/oidc/realms/pasture" \
    '.issuer == $issuer and
     (.authorization_endpoint | startswith($issuer + "/"))' \
    <<<"$discovery" >/dev/null

  IFS=$'\t' read -r login_status login_location <<<"$(
    curl --silent --show-error --max-time 45 --output /dev/null \
      --write-out '%{http_code}\t%{redirect_url}' \
      "$AP_TAILSCALE_ORIGIN/auth/login?returnUrl=%2Fapp%2F"
  )"
  if [[ "$login_status" != "302" ]] ||
     [[ "$login_location" != "$AP_TAILSCALE_ORIGIN/?auth=signin"* ]]; then
    echo "Tailnet login entry did not return the Misskey sign-in dialog route." >&2
    exit 1
  fi

  IFS=$'\t' read -r challenge_status challenge_location <<<"$(
    # The first post-deploy challenge warms ASP.NET metadata and Keycloak PAR state. Keep the
    # contract strict, but allow that cold path more time than ordinary read-only probes.
    curl --silent --show-error --max-time 45 --output /dev/null \
      --write-out '%{http_code}\t%{redirect_url}' \
      "$AP_TAILSCALE_ORIGIN/auth/external?returnUrl=%2Fapp%2F"
  )"
  if [[ "$challenge_status" != "302" ]] ||
     [[ "$challenge_location" != "$AP_TAILSCALE_ORIGIN/oidc/realms/pasture/protocol/openid-connect/auth"* ]]; then
    echo "Tailnet OIDC login challenge did not return the configured authorization endpoint." >&2
    exit 1
  fi

  curl --silent --show-error --fail --max-time 45 --output /dev/null "$AP_TAILSCALE_ORIGIN/"
  curl --silent --show-error --fail --max-time 45 --output /dev/null "$AP_TAILSCALE_ORIGIN/health/ready"
  echo "Tailnet frontend and OIDC discovery are ready at $AP_TAILSCALE_ORIGIN/"
}

case "${1:-}" in
  up)
    check_route_available
    bash "$repository_root/eng/pasture.sh" up
    tailscale serve --bg --https="$tailscale_port" "http://127.0.0.1:$pasture_port"
    verify
    ;;
  test)
    check_route_available
    verify
    ;;
  status)
    echo "Tailnet origin: $AP_TAILSCALE_ORIGIN"
    bash "$repository_root/eng/pasture.sh" status
    tailscale funnel status
    ;;
  stop)
    tailscale serve --https="$tailscale_port" off
    ;;
  down)
    tailscale serve --https="$tailscale_port" off
    bash "$repository_root/eng/pasture.sh" down
    ;;
  *)
    echo "Usage: eng/pasture-tailscale.sh up|test|status|stop|down" >&2
    exit 1
    ;;
esac
