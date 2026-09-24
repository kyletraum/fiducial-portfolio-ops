#!/bin/sh
# Spike D's fixture and assertions, run against the schema the EF Core migrations build -
# not against 01-schema.sql. 04-view-assertions.sql says it exists to "notice when the view
# is rewritten in step 2"; this is that rewrite being noticed, or not.
#
#   PG_CONTAINER=<postgres container> PG_PORT=<its host port> ./run-on-migrations.sh
#
# with the container's password exported as PGPASSWORD (read it from the container's
# POSTGRES_PASSWORD; it is generated per volume and never belongs in a file).
#
# Needs `dotnet ef` (repo-local tool) and a Postgres reachable on localhost:$PG_PORT.
# Creates and drops its own database; never touches the dev one.
set -e

: "${PG_CONTAINER:?set PG_CONTAINER}"
: "${PGPASSWORD:?set PGPASSWORD}"
: "${PG_PORT:?set PG_PORT (host port of the container)}"
DB="${SPIKE_DB:-spike_d_on_ef}"
HERE="$(cd "$(dirname "$0")" && pwd)"
REPO="$HERE/../.."

psql_in() {
    MSYS_NO_PATHCONV=1 docker exec -i -e PGPASSWORD="$PGPASSWORD" "$PG_CONTAINER" \
        psql --no-psqlrc -X -q -v ON_ERROR_STOP=1 -U postgres -d "$1"
}

# The spike's scripts, adapted three ways and no others:
#   1. search_path: the migrations build into public, not a `spike` schema;
#   2. app_setting: migration 1 seeds its one row (90), so the fixture's 45 is an UPDATE;
#   3. the wrong CROSS JOIN view is added, because 4.1 asserts against it.
adapt() {
    sed -e 's/^set search_path to spike, public;/set search_path to public;/' \
        -e 's/^insert into app_setting (balance_staleness_days) values (45);/update app_setting set balance_staleness_days = 45;/' \
        "$1"
}

echo "drop database if exists $DB; create database $DB;" | psql_in postgres

(cd "$REPO" && dotnet ef database update --project src/Infrastructure \
    --connection "Host=localhost;Port=$PG_PORT;Username=postgres;Password=$PGPASSWORD;Database=$DB" \
    | grep -E "Applying|Done")

for f in 02-fixture.sql crossjoin 04-view-assertions.sql 05-supersession.sql; do
    echo ""
    echo "=== $f ==="
    if [ "$f" = crossjoin ]; then
        { echo 'set search_path to public;'; sed -n '/^create view v_net_worth_daily_crossjoin/,$p' "$HERE/03-view.sql"; } | psql_in "$DB"
    else
        adapt "$HERE/$f" | psql_in "$DB"
    fi
done

echo "drop database $DB;" | psql_in postgres
echo ""
echo "=== spike D on the EF migrations: all assertions passed ==="
