#!/bin/sh
# Spike J: CSRF tokens in a single-page app, refused by the application's own filter.
#
#   ./run.sh
#
# Starts SpikeJ.Web on http://localhost:5293 (the app) and :5294 (an attacker page, same
# site), drives both with SpikeJ.Driver in Chromium, prints the API's refusal log, then stops
# it. No database.
set -e

HERE="$(cd "$(dirname "$0")" && pwd)"
LOG="$(mktemp)"
PID=""
cleanup() { [ -n "$PID" ] && kill "$PID" 2>/dev/null || true; rm -f "$LOG"; }
trap cleanup EXIT

(cd "$HERE" && dotnet build SpikeJ.Web -v q >/dev/null && dotnet build SpikeJ.Driver -v q >/dev/null)
dotnet "$HERE/SpikeJ.Web/bin/Debug/net10.0/SpikeJ.Web.dll" --urls "http://localhost:5293;http://localhost:5294" >"$LOG" 2>&1 &
PID=$!
for u in http://localhost:5293/ http://localhost:5294/; do
    i=0; until curl -s -o /dev/null "$u"; do i=$((i+1)); [ $i -gt 60 ] && { echo "$u never came up"; cat "$LOG"; exit 1; }; sleep 1; done
done

dotnet "$HERE/SpikeJ.Driver/bin/Debug/net10.0/SpikeJ.Driver.dll" http://localhost:5293 http://localhost:5294

echo "=== the filter's refusals, as the API logged them"
grep -F '[api]' "$LOG" || true
echo "=== API log: exceptions"
grep -E "Exception" "$LOG" | grep -v AntiforgeryValidationException | sort | uniq -c | head -10 || true
