#!/bin/sh
# Spike F: how large a file gets through, and what refuses it?
#
#   ./run.sh            # direct to Kestrel, and through the Vite dev proxy
#
# Builds and starts SpikeF.Api on loopback, starts Vite 8 (the copy installed in web/) with
# only the /api proxy, then drives every endpoint with curl at sizes either side of each
# limit. Upload files are generated in a temp directory (filler bytes, no data) and deleted.
# The containerised run is separate: ./run.sh container
set -e

HERE="$(cd "$(dirname "$0")" && pwd)"
REPO="$HERE/../.."
API_PORT=5199
VITE_PORT=5198
TMPD="$(mktemp -d)"
# curl.exe on Windows cannot read an MSYS path embedded in -F "file=@..."; give it a Windows one.
command -v cygpath >/dev/null 2>&1 && TMPD="$(cygpath -m "$TMPD")"
PIDS=""
cleanup() { for p in $PIDS; do kill "$p" 2>/dev/null || true; done; rm -rf "$TMPD"; }
trap cleanup EXIT

MODE="${1:-local}"
if [ "$MODE" = container ]; then
    (cd "$HERE/SpikeF.Api" && dotnet publish -c Release -t:PublishContainer -v q -p:ContainerImageTag=spike >/dev/null)
    CID="$(docker run -d --rm -p 127.0.0.1:$API_PORT:8080 spike-f-api:spike)"
    PIDS=""
    trap 'docker rm -f "$CID" >/dev/null 2>&1; rm -rf "$TMPD"' EXIT
    TARGETS="container=http://127.0.0.1:$API_PORT"
else
    (cd "$HERE/SpikeF.Api" && dotnet build -v q >/dev/null)
    dotnet "$HERE/SpikeF.Api/bin/Debug/net10.0/SpikeF.Api.dll" --urls "http://127.0.0.1:$API_PORT" >"$TMPD/api.log" 2>&1 &
    PIDS="$PIDS $!"
    SPIKE_API="http://127.0.0.1:$API_PORT" node "$REPO/web/node_modules/vite/bin/vite.js" \
        --config "$HERE/vite.config.mjs" --port $VITE_PORT --strictPort --host 127.0.0.1 >"$TMPD/vite.log" 2>&1 &
    PIDS="$PIDS $!"
    TARGETS="direct=http://127.0.0.1:$API_PORT vite=http://127.0.0.1:$VITE_PORT"
fi

for t in $TARGETS; do
    url="${t#*=}"
    i=0; until curl -s -o /dev/null "$url/api/none" 2>/dev/null; do i=$((i+1)); [ $i -gt 60 ] && { echo "$t never came up"; cat "$TMPD"/*.log 2>/dev/null; exit 1; }; sleep 1; done
done

mk() { python -c "import sys; open(sys.argv[1],'wb').write(b'a'*int(sys.argv[2]))" "$TMPD/$1" "$2"; }
MIB=1048576
mk small 1024
mk spill 204800
mk at_limit $MIB
mk over_limit $((MIB + 1))
mk kestrel_at 30000000
mk kestrel_over $((30000000 + 1))

# hit LABEL URL CURL-ARGS... : status, then the response body's first 160 characters
hit() {
    label="$1"; url="$2"; shift 2
    rm -f "$TMPD/body"
    out="$(curl -s -o "$TMPD/body" -w '%{http_code}' "$@" "$url" 2>&1)" || out="curl-failed($?)"
    printf '  %-44s %s  %s\n' "$label" "$out" "$(head -c 160 "$TMPD/body" 2>/dev/null | tr '\n' ' ')"
}

for t in $TARGETS; do
    name="${t%%=*}"; base="${t#*=}/api"
    echo "=== $name ($base)"
    hit "A iformfile, no antiforgery decision, 1 KiB" "$base/a-iformfile"        -F "file=@$TMPD/small;type=text/csv"
    hit "B iformfile, antiforgery off, 1 KiB"        "$base/b-iformfile-noaf"    -F "file=@$TMPD/small;type=text/csv"
    hit "B iformfile, 200 KiB (spill to disk?)"      "$base/b-iformfile-noaf"    -F "file=@$TMPD/spill;type=text/csv"
    hit "B iformfile, 30,000,000-byte file"          "$base/b-iformfile-noaf"    -F "file=@$TMPD/kestrel_at;type=text/csv"
    hit "C iformfile limited, file = 1 MiB"          "$base/c-iformfile-limited" -F "file=@$TMPD/at_limit;type=text/csv"
    hit "C iformfile limited, file = 1 MiB + 1"      "$base/c-iformfile-limited" -F "file=@$TMPD/over_limit;type=text/csv"
    hit "C iformfile limited, 30,000,000-byte file"  "$base/c-iformfile-limited" -F "file=@$TMPD/kestrel_at;type=text/csv"
    hit "D raw, text/csv, 1 MiB"                     "$base/d-raw" -H "Content-Type: text/csv" --data-binary "@$TMPD/at_limit"
    hit "D raw, text/csv, 1 MiB + 1"                 "$base/d-raw" -H "Content-Type: text/csv" --data-binary "@$TMPD/over_limit"
    hit "D raw, chunked (no Content-Length), 1 MiB + 1" "$base/d-raw" -H "Content-Type: text/csv" -H "Transfer-Encoding: chunked" --data-binary "@$TMPD/over_limit"
    hit "D raw, posted as a multipart form"          "$base/d-raw" -F "file=@$TMPD/small;type=text/csv"
    hit "E raw default, 30,000,000 bytes"            "$base/e-raw-default" -H "Content-Type: text/csv" --data-binary "@$TMPD/kestrel_at"
    hit "E raw default, 30,000,001 bytes"            "$base/e-raw-default" -H "Content-Type: text/csv" --data-binary "@$TMPD/kestrel_over"
done

if [ "$MODE" != container ]; then
    # Over-limit through the proxy is a race: Kestrel answers 413 and closes while the proxy
    # may still be sending, and then the proxy reports 502. Counted, not sampled once.
    for size in over_limit kestrel_over; do
        printf '=== vite, raw over the limit (%s), 20 tries: ' "$size"
        url="http://127.0.0.1:$VITE_PORT/api/d-raw"; [ $size = kestrel_over ] && url="http://127.0.0.1:$VITE_PORT/api/e-raw-default"
        for i in $(seq 20); do curl -s -o /dev/null -w '%{http_code}
' -H "Content-Type: text/csv" --data-binary "@$TMPD/$size" "$url"; done | sort | uniq -c | tr '
' ' '
        echo
    done
    echo "=== API log: exceptions"
    grep -E "Exception|antiforgery|anti-forgery" "$TMPD/api.log" | sed 's/^/  /' | sort | uniq -c | head -10 || true
fi
