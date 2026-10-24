#!/bin/sh
# Spike H: Identity's in-box passkeys, end to end in Chromium, on a throwaway PostgreSQL.
#
#   ./run.sh
#
# Starts postgres:18.3 on a random loopback port (password generated per run, never written
# down), runs SpikeH.Web on http://localhost:5299 against it, drives it with SpikeH.Driver
# (Chromium + a CDP virtual authenticator), then removes everything it started.
set -e

HERE="$(cd "$(dirname "$0")" && pwd)"
NAME="spike-h-pg-$$"
PW="$(python3 -c 'import secrets; print(secrets.token_hex(16))' 2>/dev/null || python -c 'import secrets; print(secrets.token_hex(16))')"
WEB_PID=""
cleanup() {
    [ -n "$WEB_PID" ] && kill "$WEB_PID" 2>/dev/null || true
    docker rm -f "$NAME" >/dev/null 2>&1 || true
}
trap cleanup EXIT

docker run -d --name "$NAME" -e POSTGRES_PASSWORD="$PW" -p 127.0.0.1::5432 postgres:18.3 >/dev/null
PORT="$(docker port "$NAME" 5432/tcp | head -1 | sed 's/.*://')"
until MSYS_NO_PATHCONV=1 docker exec "$NAME" pg_isready -U postgres -q; do sleep 1; done
sleep 2

(cd "$HERE" && dotnet build SpikeH.Web -v q >/dev/null && dotnet build SpikeH.Driver -v q >/dev/null)
ConnectionStrings__spike="Host=localhost;Port=$PORT;Username=postgres;Password=$PW;Database=spike_h" \
    dotnet "$HERE/SpikeH.Web/bin/Debug/net10.0/SpikeH.Web.dll" --urls http://localhost:5299 &
WEB_PID=$!
i=0; until curl -s -o /dev/null http://localhost:5299/; do i=$((i+1)); [ $i -gt 60 ] && { echo "web never came up"; exit 1; }; sleep 1; done

dotnet "$HERE/SpikeH.Driver/bin/Debug/net10.0/SpikeH.Driver.dll" http://localhost:5299
