#!/usr/bin/env bash
set -euo pipefail

repo_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cache_dir="${repo_dir}/.cache/upstream"
mastodon_dir="${cache_dir}/mastodon-4.6.2"
misskey_dir="${cache_dir}/misskey-12.119.2"

fetch_upstreams() {
  mkdir -p "${cache_dir}"
  if [[ ! -d "${mastodon_dir}/.git" ]]; then
    git clone --depth 1 --filter=blob:none --sparse --branch v4.6.2 \
      https://github.com/mastodon/mastodon.git "${mastodon_dir}"
  fi
  git -C "${mastodon_dir}" sparse-checkout set --no-cone \
    '/config/routes.rb' '/config/routes/' '/app/controllers/api/' '/app/serializers/' '/app/presenters/' '/app/validators/'

  if [[ ! -d "${misskey_dir}/.git" ]]; then
    git clone --depth 1 --filter=blob:none --sparse --branch 12.119.2 \
      https://github.com/misskey-dev/misskey.git "${misskey_dir}"
  fi
  git -C "${misskey_dir}" sparse-checkout set \
    packages/backend/src/server/api packages/backend/src/misc packages/backend/src/config packages/client/src
}

case "${1:-generate}" in
  fetch)
    fetch_upstreams
    ;;
  generate)
    fetch_upstreams
    node "${repo_dir}/eng/generate-api-inventory.mjs"
    ;;
  check)
    fetch_upstreams
    node "${repo_dir}/eng/generate-api-inventory.mjs" --check
    ;;
  *)
    echo "usage: eng/api-inventory.sh [fetch|generate|check]" >&2
    exit 64
    ;;
esac
