#!/usr/bin/env bash
set -euo pipefail

repository_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
environment_file="${ACTIVITYPUB_ENV_FILE:-$repository_root/.env}"

if [[ ! -f "$environment_file" ]]; then
  echo "Create $repository_root/.env from .env.example or set ACTIVITYPUB_ENV_FILE." >&2
  exit 1
fi

set -a
# shellcheck source=/dev/null
source "$environment_file"
set +a

: "${AP_OIDC_ALICE_PASSWORD:?set AP_OIDC_ALICE_PASSWORD}"
: "${AP_VAULT_TOKEN_FILE:?set AP_VAULT_TOKEN_FILE}"
: "${ACTIVITYPUB_PASTURE_PORT:=2971}"

session_directory="$(dirname "$AP_VAULT_TOKEN_FILE")/oidc-session"
token_file="$session_directory/token.json"
mkdir -p "$session_directory"
chmod 0700 "$session_directory"
umask 077

if [[ -n "${AP_TAILSCALE_ORIGIN:-}" ]]; then
  if [[ ! "$AP_TAILSCALE_ORIGIN" =~ ^https://[A-Za-z0-9.-]+(:[0-9]{1,5})?$ ]]; then
    echo "AP_TAILSCALE_ORIGIN must be an HTTPS origin without a path, query, or fragment." >&2
    exit 1
  fi
  connect=(--connect-timeout 10 --max-time 30)
  authority="$AP_TAILSCALE_ORIGIN/oidc/realms/pasture"
  redirect_uri="$AP_TAILSCALE_ORIGIN/auth/callback"
else
  connect=(
    --connect-to "activitypub:80:127.0.0.1:$ACTIVITYPUB_PASTURE_PORT"
    --connect-timeout 10
    --max-time 30
  )
  authority="http://activitypub/oidc/realms/pasture"
  redirect_uri="http://activitypub/auth/callback"
fi
client_id="activitypub-web"

base64url_sha256() {
  openssl dgst -binary -sha256 | openssl base64 -A | tr '+/' '-_' | tr -d '='
}

run_authorization_code_flow() {
  flow_mode="$1"
  verifier="$(openssl rand -base64 72 | tr -d '\n=+/' | cut -c1-86)"
  challenge="$(printf '%s' "$verifier" | base64url_sha256)"
  state="$(openssl rand -hex 24)"
  nonce="$(openssl rand -hex 24)"
  cookie_jar="$session_directory/cookies.txt"
  authorize_body="$session_directory/authorize.html"
  login_headers="$session_directory/login.headers"
  login_response="$session_directory/login-response.html"
  token_temp="$session_directory/token.tmp"
  cleanup_exchange_files() {
    rm -f -- "$cookie_jar" "$authorize_body" "$login_headers" "$login_response" "$token_temp"
  }
  trap cleanup_exchange_files RETURN EXIT INT TERM
  : > "$cookie_jar"

  curl --silent --show-error --fail "${connect[@]}" \
    --cookie "$cookie_jar" \
    --cookie-jar "$cookie_jar" \
    --output "$authorize_body" \
    --get "$authority/protocol/openid-connect/auth" \
    --data-urlencode "client_id=$client_id" \
    --data-urlencode 'response_type=code' \
    --data-urlencode "redirect_uri=$redirect_uri" \
    --data-urlencode 'scope=openid activitypub.read activitypub.write' \
    --data-urlencode "state=$state" \
    --data-urlencode "nonce=$nonce" \
    --data-urlencode "code_challenge=$challenge" \
    --data-urlencode 'code_challenge_method=S256'

  login_action="$(grep -o 'action="[^"]*"' "$authorize_body" | head -n 1 | sed -e 's/^action="//' -e 's/"$//' -e 's/&amp;/\&/g')"
  if [[ "$login_action" != "$authority"/login-actions/* ]]; then
    echo "OIDC provider did not return the expected login action." >&2
    exit 1
  fi
  if ! grep -q 'data-misskey-version="12.119.2"' "$authorize_body" ||
     ! grep -q 'data-misskey-component="MkSignin"' "$authorize_body" ||
     grep -q '/auth/credentials' "$authorize_body"; then
    echo "OIDC provider did not return the pinned Keycloak-hosted MkSignin theme." >&2
    exit 1
  fi

  login_status="$(curl --silent --show-error "${connect[@]}" \
    --cookie "$cookie_jar" \
    --cookie-jar "$cookie_jar" \
    --dump-header "$login_headers" \
    --output "$login_response" \
    --write-out '%{http_code}' \
    --request POST "$login_action" \
    --data-urlencode 'username=alice' \
    --data-urlencode "password=$AP_OIDC_ALICE_PASSWORD" \
    --data-urlencode 'credentialId=')"
  if [[ "$login_status" != 302 ]]; then
    echo "OIDC login did not produce an authorization-code redirect." >&2
    exit 1
  fi

  callback="$(awk 'BEGIN{IGNORECASE=1} /^location:/{sub(/\r$/,""); sub(/^[^:]+:[[:space:]]*/,""); print; exit}' "$login_headers")"
  code="$(printf '%s' "$callback" | sed -nE 's/.*[?&]code=([^&]+).*/\1/p')"
  returned_state="$(printf '%s' "$callback" | sed -nE 's/.*[?&]state=([^&]+).*/\1/p')"
  if [[ -z "$code" || "$returned_state" != "$state" ]]; then
    echo "OIDC callback code or state validation failed." >&2
    exit 1
  fi

  if [[ "$flow_mode" == "verify" ]]; then
    echo "OIDC authorization-code login verified; exchange credentials were removed."
    return
  fi

  token_status="$(curl --silent --show-error "${connect[@]}" \
    --output "$token_temp" \
    --write-out '%{http_code}' \
    --request POST "$authority/protocol/openid-connect/token" \
    --header 'Content-Type: application/x-www-form-urlencoded' \
    --data-urlencode 'grant_type=authorization_code' \
    --data-urlencode "client_id=$client_id" \
    --data-urlencode "redirect_uri=$redirect_uri" \
    --data-urlencode "code=$code" \
    --data-urlencode "code_verifier=$verifier")"
  if [[ "$token_status" != 200 ]] ||
    ! jq -e '.access_token and .refresh_token and .id_token and .token_type == "Bearer"' "$token_temp" >/dev/null; then
    echo "OIDC authorization-code exchange failed." >&2
    exit 1
  fi

  chmod 0600 "$token_temp"
  mv "$token_temp" "$token_file"
  echo "OIDC authorization-code + PKCE token issued; credentials remain outside the repository."
}

case "${1:-}" in
  issue)
    run_authorization_code_flow issue
    ;;
  verify)
    run_authorization_code_flow verify
    ;;
  path)
    printf '%s\n' "$token_file"
    ;;
  *)
    echo "Usage: eng/pasture-oidc.sh issue|verify|path" >&2
    exit 1
    ;;
esac
