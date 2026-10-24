#!/bin/sh
# Spike I remainder (gate G-5): a passkey ceremony through the Vite dev proxy, with the
# origin check slice-03.md step 2 specifies.
#
#   ./run.sh
#
# Starts postgres:18.3 on a random loopback port (password generated per run, never written
# down), SpikeI.Web on http://localhost:5297, and two Vite 8 servers (the copy installed in
# web/) proxying /api to it: A on 5295, which is the only origin on the API's allow-list, and
# B on 5296. Drives all three origins with SpikeI.Driver, prints the API's log of what it saw,
# then removes everything it started.
set -e

HERE="$(cd "$(dirname "$0")" && pwd)"
REPO="$HERE/../.."
API_PORT=5297
VITE_A=5295
VITE_B=5296
NAME="spike-i-pg-$$"
LOG="$(mktemp)"
PW="$(python -c 'import secrets; print(secrets.token_hex(16))')"
PIDS=""
cleanup() {
    for p in $PIDS; do kill "$p" 2>/dev/null || true; done
    docker rm -f "$NAME" >/dev/null 2>&1 || true
    rm -f "$LOG" "$LOG.vite"
}
trap cleanup EXIT

docker run -d --name "$NAME" -e POSTGRES_PASSWORD="$PW" -p 127.0.0.1::5432 postgres:18.3 >/dev/null
PORT="$(docker port "$NAME" 5432/tcp | head -1 | sed 's/.*://')"
until MSYS_NO_PATHCONV=1 docker exec "$NAME" pg_isready -U postgres -q; do sleep 1; done
sleep 2

(cd "$HERE" && dotnet build SpikeI.Web -v q >/dev/null && dotnet build SpikeI.Driver -v q >/dev/null)
ConnectionStrings__spike="Host=localhost;Port=$PORT;Username=postgres;Password=$PW;Database=spike_i" \
Spike__AllowedOrigins="http://localhost:$VITE_A" \
    dotnet "$HERE/SpikeI.Web/bin/Debug/net10.0/SpikeI.Web.dll" --urls "http://localhost:$API_PORT" >"$LOG" 2>&1 &
PIDS="$PIDS $!"
for v in $VITE_A $VITE_B; do
    SPIKE_API="http://localhost:$API_PORT" node "$REPO/web/node_modules/vite/bin/vite.js" \
        --config "$HERE/vite.config.mjs" --port $v --strictPort --host localhost >>"$LOG.vite" 2>&1 &
    PIDS="$PIDS $!"
done
for u in "http://localhost:$API_PORT/" "http://localhost:$VITE_A/" "http://localhost:$VITE_B/"; do
    i=0; until curl -s -o /dev/null "$u"; do i=$((i+1)); [ $i -gt 60 ] && { echo "$u never came up"; cat "$LOG" "$LOG.vite"; exit 1; }; sleep 1; done
done

dotnet "$HERE/SpikeI.Driver/bin/Debug/net10.0/SpikeI.Driver.dll" \
    "http://localhost:$VITE_A" "http://localhost:$VITE_B" "http://localhost:$API_PORT"

echo "=== what the API saw (request Origin and Host; ValidateOrigin's verdicts; Set-Cookie attributes)"
grep -F '[api]' "$LOG"
echo "=== API log: exceptions"
grep -E "Exception" "$LOG" | sort | uniq -c | head -10 || true
