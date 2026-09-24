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

set -u

die() {
    printf '{"error":%s}\n' "$(printf '%s' "$1" | jq -Rs .)"
    exit 1
}

[ "$#" -ge 1 ] || die "usage: collect-session-usage.sh <session-id> [<session-id> ...]"

command -v jq >/dev/null 2>&1 || die "jq is required but was not found on PATH"

# Resolve the server URL. Prefer an explicit override, then the port of the
# running background service, then fall back to the CLI's own discovery.
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

api_get() {
    # $1 = API path
    if [ -n "$SERVER" ]; then
        opencode api --server "$SERVER" get "$1" </dev/null
    else
        # No explicit server: rely on the CLI's background-service discovery.
        timeout 60 opencode api get "$1" </dev/null
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
