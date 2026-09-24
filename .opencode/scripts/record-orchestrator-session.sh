#!/usr/bin/env bash
#
# record-orchestrator-session.sh
#
# Record or update a per-session report for the orchestrator's OWN usage.
#
# The orchestrator is the primary agent, not a delegated subagent, so it never
# appears in a per-task or per-phase subagent aggregate. Instead, each
# orchestrator session gets its own report, updated every time the orchestrator
# completes a piece of requested work. Per-session files keep each session's cost
# isolated and make deltas trivial.
#
# Usage:
#   .opencode/scripts/record-orchestrator-session.sh [--note "<what was done>"]
#   .opencode/scripts/record-orchestrator-session.sh [--note "..."] <session-id>
#
# With no session id it identifies the currently running orchestrator session
# (via the active-session list) and records that. A session id may be passed
# explicitly for backfill; it must belong to the `orchestrator` agent.
#
# Writes/updates docs/implementation/orchestrator-sessions/<session-id>.json and
# prints a one-line JSON result with the report path and latest figures.
# Exits non-zero if the server or the session cannot be resolved.

set -u

die() {
    printf '{"error":%s}\n' "$(printf '%s' "$1" | jq -Rs .)"
    exit 1
}

command -v jq >/dev/null 2>&1 || die "jq is required but was not found on PATH"

note=""
sid=""
while [ "$#" -gt 0 ]; do
    case "$1" in
        --note)
            [ "$#" -ge 2 ] || die "--note requires a value"
            note="$2"
            shift 2
            ;;
        -*)
            die "unknown option: $1"
            ;;
        *)
            [ -z "$sid" ] || die "only one session id may be given"
            sid="$1"
            shift
            ;;
    esac
done

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
    if [ -n "$SERVER" ]; then
        opencode api --server "$SERVER" get "$1" </dev/null
    else
        timeout 60 opencode api get "$1" </dev/null
    fi
}

# Resolve the target session id.
if [ -z "$sid" ]; then
    active="$(api_get '/api/session/active' 2>/dev/null)" || active=""
    [ -n "$active" ] || die "could not list active sessions from the OpenCode server"
    matches=""
    for id in $(printf '%s' "$active" | jq -r '.data | keys[]' 2>/dev/null); do
        a="$(api_get "/api/session/$id" 2>/dev/null | jq -r '.data.agent // empty' 2>/dev/null)"
        [ "$a" = "orchestrator" ] && matches="$matches $id"
    done
    # shellcheck disable=SC2086
    set -- $matches
    [ "$#" -gt 0 ] || die "no running orchestrator session found"
    [ "$#" -eq 1 ] || die "multiple running orchestrator sessions found:$matches"
    sid="$1"
fi

raw="$(api_get "/api/session/$sid" 2>/dev/null)" || raw=""
[ -n "$raw" ] || die "could not fetch session $sid"

agent="$(printf '%s' "$raw" | jq -r '.data.agent // empty' 2>/dev/null)"
[ "$agent" = "orchestrator" ] || die "session $sid is not an orchestrator session (agent: ${agent:-unknown})"

usage="$(printf '%s' "$raw" | jq -c '
  .data as $d
  | {
      session_id: $d.id,
      agent: $d.agent,
      model: (($d.model.providerID // "") + "/" + ($d.model.id // "")),
      variant: ($d.model.variant // null),
      title: ($d.title // null),
      tokens: {
        input: ($d.tokens.input // null),
        output: ($d.tokens.output // null),
        reasoning: ($d.tokens.reasoning // null),
        cache: {
          read: ($d.tokens.cache.read // null),
          write: ($d.tokens.cache.write // null)
        }
      },
      total_tokens: (
        [$d.tokens.input, $d.tokens.output, $d.tokens.reasoning,
         $d.tokens.cache.read, $d.tokens.cache.write]
        | if all(.[]; . != null) then add else null end
      ),
      cost_usd: ($d.cost // null),
      duration_seconds: (
        ($d.time.created) as $c
        | (if ($d.time.idle != null and $d.time.idle >= $c) then $d.time.idle
           elif ($d.time.updated != null and $d.time.updated >= $c) then $d.time.updated
           else null end) as $e
        | if ($c != null and $e != null) then ((($e - $c) / 1000) | floor) else null end
      )
    }')" || die "could not parse session $sid"

repo_root="$(git rev-parse --show-toplevel 2>/dev/null || pwd)"
outdir="$repo_root/docs/implementation/orchestrator-sessions"
report="$outdir/$sid.json"
now="$(date -u +%Y-%m-%dT%H:%M:%SZ)"

first="$now"
prev="[]"
if [ -f "$report" ]; then
    existing_first="$(jq -r '.first_recorded_at // empty' "$report" 2>/dev/null)"
    [ -n "$existing_first" ] && first="$existing_first"
    existing_updates="$(jq -c '.updates // []' "$report" 2>/dev/null)"
    [ -n "$existing_updates" ] && prev="$existing_updates"
fi

mkdir -p "$outdir"

jq -n \
    --argjson u "$usage" \
    --arg now "$now" \
    --arg first "$first" \
    --arg note "$note" \
    --argjson prev "$prev" '
  $u + {
    first_recorded_at: $first,
    last_updated_at: $now,
    updates: (
      ($prev | last) as $l
      | if ($l != null
            and $l.cost_usd == $u.cost_usd
            and $l.total_tokens == $u.total_tokens
            and $l.duration_seconds == $u.duration_seconds
            and (($l.note // "") == $note))
        then $prev
        else ($prev + [{
          at: $now,
          cost_usd: $u.cost_usd,
          total_tokens: $u.total_tokens,
          duration_seconds: $u.duration_seconds
        } + (if $note == "" then {} else {note: $note} end)])
        end
    )
  }
' > "$report.tmp" || die "could not render report for $sid"
mv "$report.tmp" "$report" || die "could not write $report"

printf '{"report":"%s","session_id":"%s","cost_usd":%s,"total_tokens":%s,"updates":%s}\n' \
    "$report" "$sid" \
    "$(printf '%s' "$usage" | jq -c '.cost_usd')" \
    "$(printf '%s' "$usage" | jq -c '.total_tokens')" \
    "$(jq -c '.updates | length' "$report")"
