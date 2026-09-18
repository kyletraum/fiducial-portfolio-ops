#!/bin/sh
# Spike D. Throwaway Postgres, one schema, dropped and rebuilt on every run.
#
#   service postgresql start
#   ./run.sh
#
# Exits non-zero on the first failed assertion. There is no application code
# here and there is not meant to be: the spike exercises the SQL and nothing
# else.
set -e

DB="${SPIKE_DB:-spike_d}"
PSQL="psql --no-psqlrc -v ON_ERROR_STOP=1 -X -q"

if [ "$(id -un)" != "postgres" ] && [ -z "$PGHOST" ] && [ -z "$PGUSER" ]; then
    echo "re-running as the postgres role (set PGHOST/PGUSER to override)"
    exec su postgres -c "cd '$(pwd)' && SPIKE_DB='$DB' sh ./run.sh"
fi

psql --no-psqlrc -X -q -c "create database $DB" postgres 2>/dev/null || true

for f in 01-schema.sql 02-fixture.sql 03-view.sql 04-view-assertions.sql 05-supersession.sql; do
    echo ""
    echo "=== $f ==="
    $PSQL -f "$f" -d "$DB"
done

echo ""
echo "=== spike D: all assertions passed ==="
