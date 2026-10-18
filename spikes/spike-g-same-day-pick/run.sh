#!/bin/sh
# Spike G: which balance does v_net_worth_daily count when two sources report the same day?
#
#   ./run.sh
#
# Starts a THROWAWAY Postgres container (removed on exit), builds the schema with the real
# EF Core migrations, then: 02 records what the unfixed view returns, 03 applies the ruled
# tie-break, 04 asserts it. The password is generated per run and never written down.
# Needs Docker and the repo-local `dotnet ef` tool. Never touches the dev database.
set -e

HERE="$(cd "$(dirname "$0")" && pwd)"
REPO="$HERE/../.."
NAME="spike-g-pg-$$"
IMAGE="${SPIKE_IMAGE:-postgres:18.3}"
PGPASSWORD="$(python3 -c 'import secrets; print(secrets.token_hex(16))' 2>/dev/null || python -c 'import secrets; print(secrets.token_hex(16))')"
export PGPASSWORD

cleanup() { docker rm -f "$NAME" >/dev/null 2>&1 || true; }
trap cleanup EXIT

docker run -d --name "$NAME" -e POSTGRES_PASSWORD="$PGPASSWORD" -p 127.0.0.1::5432 "$IMAGE" >/dev/null
PORT="$(docker port "$NAME" 5432/tcp | head -1 | sed 's/.*://')"
until MSYS_NO_PATHCONV=1 docker exec "$NAME" pg_isready -U postgres -q; do sleep 1; done
sleep 2  # the image restarts postgres once after initdb

psql_in() {
    MSYS_NO_PATHCONV=1 docker exec -i "$NAME" psql --no-psqlrc -X -q -v ON_ERROR_STOP=1 -U postgres -d spike_g
}

echo "create database spike_g;" | MSYS_NO_PATHCONV=1 docker exec -i "$NAME" psql -X -q -U postgres -d postgres
docker exec "$NAME" psql -X -At -U postgres -c "select version()"

(cd "$REPO" && dotnet ef database update --project src/Infrastructure \
    --connection "Host=localhost;Port=$PORT;Username=postgres;Password=$PGPASSWORD;Database=spike_g" \
    | grep -E "Applying|Done")

for f in 01-fixture.sql 02-unspecified.sql 03-fixed-view.sql 04-assertions.sql; do
    echo ""
    echo "=== $f ==="
    psql_in < "$HERE/$f"
done
