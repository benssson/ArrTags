#!/usr/bin/env bash
#
# collect-session-usage.sh
#
# Fetch authoritative per-session token usage, cost, and duration from the
# running OpenCode server, so the orchestrator can record real execution
# metadata in docs/implementation/<task-id>/orchestration.json instead of nulls.
#
# Usage:
#   .opencode/scripts/collect-session-usage.sh <session-id> [<session-id> ...]
#
# Output (one JSON object per line, in argument order):
#   {
#     "session_id": "...",
#     "agent": "implementation-worker",
#     "model": "opencode-go/deepseek-v4.1-flash",
#     "variant": "high",
#     "input_tokens": 222452,
#     "output_tokens": 36959,
#     "reasoning_tokens": 38720,
#     "cache_read_tokens": 16947584,
#     "cache_write_tokens": 0,
#     "total_tokens": 17045715,
#     "cost_usd": 0.129617952,
#     "duration_seconds": 1848
#   }
#
# On a session that cannot be fetched, prints {"session_id": "...", "error": "..."}.
# Exits non-zero when the server itself cannot be reached; the caller should then
# record the affected fields as null (never estimate).
#
# The values are the runtime's own authoritative figures. Do not recompute cost.
#
# Transport: a direct HTTP GET of the resolved server URL's /api/session/<id>
# endpoint. curl is preferred; when curl is absent an HTTP/1.0 GET over bash's
# /dev/tcp is used instead, so the `opencode` CLI is never required on the
# success path. When the server was started with a password
# (OPENCODE_SERVER_PASSWORD in the environment), HTTP basic auth is sent as
# user `opencode`; the https scheme requires curl.
#
# Self-test (run from the repository root, with the server that recorded the
# session still running; OPENCODE_SERVER and OPENCODE_SERVER_PASSWORD override
# the discovered server as in normal use). It fetches a real session id from
# this repository's own records, checks the output parses, and requires a
# non-null total_tokens:
#
#   sid=$(jq -r '.subagents[0].execution.session_id' \
#       docs/implementation/26.4/orchestration.json)
#   .opencode/scripts/collect-session-usage.sh "$sid" \
#       | jq -e 'select(.total_tokens != null) | .total_tokens'
#
# Expected: the number prints and the pipeline exits 0. Against an unreachable
# server the script prints {"session_id":"<sid>","error":"session fetch failed"}
# and exits 1; token and cost fields are never estimated.

set -u

die() {
    printf '{"error":%s}\n' "$(printf '%s' "$1" | jq -Rs .)"
    exit 1
}

[ "$#" -ge 1 ] || die "usage: collect-session-usage.sh <session-id> [<session-id> ...]"

command -v jq >/dev/null 2>&1 || die "jq is required but was not found on PATH"

# Resolve the server URL. Prefer an explicit override, then the port of the
# running background service; when neither is available the fetch fails clearly.
resolve_server() {
    if [ -n "${OPENCODE_SERVER:-}" ]; then
        case "$OPENCODE_SERVER" in
            http://*|https://*) printf '%s\n' "$OPENCODE_SERVER" ;;
            *) printf 'http://%s\n' "$OPENCODE_SERVER" ;;
        esac
        return 0
    fi
    local port
    port="$(ps -eo args= 2>/dev/null \
        | sed -n 's/.*opencode serve.*--port \([0-9][0-9]*\).*/\1/p' \
        | head -1)"
    if [ -n "$port" ]; then
        printf 'http://127.0.0.1:%s\n' "$port"
        return 0
    fi
    return 1
}

SERVER="$(resolve_server)" || SERVER=""

if command -v curl >/dev/null 2>&1; then
    GET_TRANSPORT=curl
else
    GET_TRANSPORT=bash
fi

http_get_bash() {
    # $1 = URL (http:// only). Prints the response body, returns 0 on HTTP 200.
    local url="$1" rest hostport path host port line
    case "$url" in
        http://*) rest="${url#http://}" ;;
        *) return 1 ;;
    esac
    hostport="${rest%%/*}"
    if [ "$hostport" = "$rest" ]; then path="/"; else path="/${rest#*/}"; fi
    host="${hostport%%:*}"
    case "$hostport" in
        *:*) port="${hostport##*:}" ;;
        *) port=80 ;;
    esac
    {
        {
            printf 'GET %s HTTP/1.0\r\n' "$path"
            printf 'Host: %s\r\n' "$hostport"
            printf 'Accept: application/json\r\n'
            printf 'Connection: close\r\n'
            if [ -n "${OPENCODE_SERVER_PASSWORD:-}" ]; then
                printf 'Authorization: Basic %s\r\n' \
                    "$(printf 'opencode:%s' "$OPENCODE_SERVER_PASSWORD" | base64)"
            fi
            printf '\r\n'
        } >&3
        IFS= read -r line <&3 || return 1
        case "$line" in *" 200 "*) ;; *) return 1 ;; esac
        while IFS= read -r line <&3; do
            [ -z "${line%$'\r'}" ] && break
        done
        cat <&3
    } 3<>"/dev/tcp/$host/$port" 2>/dev/null || return 1
}

api_get() {
    # $1 = API path, for example /api/session/<id>
    [ -n "$SERVER" ] || return 1
    local url="${SERVER%/}$1"
    if [ "$GET_TRANSPORT" = curl ]; then
        if [ -n "${OPENCODE_SERVER_PASSWORD:-}" ]; then
            curl -fsS --max-time 60 -u "opencode:$OPENCODE_SERVER_PASSWORD" "$url" </dev/null
        else
            curl -fsS --max-time 60 "$url" </dev/null
        fi
    else
        http_get_bash "$url"
    fi
}

status=0
for sid in "$@"; do
    raw="$(api_get "/api/session/$sid" 2>/dev/null)" || raw=""
    if [ -z "$raw" ]; then
        printf '{"session_id":"%s","error":"session fetch failed"}\n' "$sid"
        status=1
        continue
    fi
    printf '%s' "$raw" | jq -c --arg sid "$sid" '
        if (.data | type) != "object" then
            {session_id: $sid, error: "session not found"}
        else
            .data as $d
            | {
                session_id: $d.id,
                agent: $d.agent,
                model: (($d.model.providerID // "") + "/" + ($d.model.id // "")),
                variant: ($d.model.variant // null),
                input_tokens: ($d.tokens.input // null),
                output_tokens: ($d.tokens.output // null),
                reasoning_tokens: ($d.tokens.reasoning // null),
                cache_read_tokens: ($d.tokens.cache.read // null),
                cache_write_tokens: ($d.tokens.cache.write // null),
                total_tokens: (
                    [$d.tokens.input, $d.tokens.output, $d.tokens.reasoning,
                     $d.tokens.cache.read, $d.tokens.cache.write]
                    | if all(.[]; . != null) then add else null end
                ),
                cost_usd: ($d.cost // null),
                duration_seconds: (
                    ($d.time.created) as $c
                    | (
                        if ($d.time.idle != null and $d.time.idle >= $c) then $d.time.idle
                        elif ($d.time.updated != null and $d.time.updated >= $c) then $d.time.updated
                        else null end
                      ) as $e
                    | if ($c != null and $e != null)
                      then (((($e - $c) / 1000) | floor))
                      else null end
                )
            }
        end'
done

exit "$status"
