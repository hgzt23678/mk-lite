#!/bin/ash

# fediverse-pasture's Pleroma image unconditionally downloads Hex on every
# container start. The pasture network is deliberately internal, so runtime
# package downloads must not be required. The pinned image already contains
# its dependencies; this entrypoint preserves its database initialization and
# migration flow while omitting only that network-dependent bootstrap step.

set -eu

set -a
# shellcheck source=/dev/null
. /opt/pleroma/environment.sh
set +a

echo "-- Waiting for database..."
while ! pg_isready \
  -U "${DB_USER:-postgres}" \
  -d "postgres://${DB_HOST:-db}:5432/${DB_NAME:-postgres}" \
  -t 1; do
  sleep 1
done

cd /opt/pleroma

# The upstream marker lives in the container layer and is lost whenever
# Compose recreates the container. Use the durable PostgreSQL state instead,
# otherwise the fixture user creation is repeated and startup fails.
if ! PGPASSWORD="${DB_PASS:-}" psql \
  -h "${DB_HOST:-db}" \
  -U "${DB_USER:-postgres}" \
  -d postgres \
  -Atqc "select exists (select 1 from users where nickname = 'full')" \
  2>/dev/null | grep -qx t; then
  ../scripts/init.sh
fi

echo "-- Running migrations..."
mix ecto.migrate

exec "$@"
