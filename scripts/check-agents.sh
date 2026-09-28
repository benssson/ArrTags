#!/usr/bin/env bash
# ArrTags agent report-contract checks.
#
# Manual gate (the repository has no CI). Run alongside scripts/check-docs.sh:
#   source /config/arrtags-env.sh
#   scripts/check-agents.sh
#
# It verifies the contract in docs/agent-contracts.md: the contract exists and is
# linked, review/research agents reference it, every agent's declared default
# report path and status enum agree with the contract table, no legacy
# frontmatter or forbidden status/severity tokens remain, every task with a
# worker report has a reviewer report unless explicitly exempted, the canonical
# plan state lists that review evidence, and the phase-gate records carry the
# contract's gate-conformance and orchestration shape.
set -uo pipefail

cd "$(dirname "$0")/.." || exit 1
fail=0
section_fail=0
err() { echo "FAIL: $*" >&2; fail=1; section_fail=1; }
begin() { section_fail=0; }
end() { [ "$section_fail" -eq 0 ] && echo "ok: $*"; }

# First phase number at which the newer contract rules are enforced. Records for
# earlier phases predate the rules and live under the immutable history tree, so
# they are not rewritten. Must match the "Enforcement scope" section of the
# contract; a disagreement is itself a failure.
APPLIES_FROM_PHASE=20

contract="docs/agent-contracts.md"
begin
for f in "$contract" docs/INDEX.md AGENTS.md; do
    [ -f "$f" ] || err "missing $f"
done
grep -q 'docs/agent-contracts.md' docs/INDEX.md || err "docs/INDEX.md does not link $contract"
grep -q 'docs/agent-contracts.md' AGENTS.md || err "AGENTS.md does not link $contract"
end "contract present and linked"

# Review/research agents must reference the contract.
begin
for f in .opencode/agents/*.md; do
    base=$(basename "$f")
    case "$base" in
        implementation-worker.md|implementation-planner.md|documentation-maintainer.md|orchestrator.md) continue ;;
    esac
    grep -q 'docs/agent-contracts.md' "$f" || err "$base does not reference $contract"
done
end "review/research agents reference the contract"

# No legacy V1 frontmatter fields.
begin
if grep -rnE '^permission:|^variant:' .opencode/agents/*.md >/dev/null 2>&1; then
    err "legacy frontmatter (permission:/variant:) remains in an agent prompt"
fi
end "no legacy frontmatter fields"

# No forbidden status/severity tokens (CONCERNS, bare INFO severity).
begin
if grep -rnE '"severity": "INFO"|CONCERNS' .opencode/agents/*.md >/dev/null 2>&1; then
    err "forbidden status/severity token (INFO or CONCERNS) in an agent prompt"
fi
end "status/severity tokens conform"

# Per-agent agreement: the declared default path and status enum must match the
# contract table for that agent (the table is the machine-readable contract).
begin
while IFS=$'\t' read -r agent path status; do
    [ -n "$agent" ] || continue
    case "$status" in [A-Z]*) ;; *) status="" ;; esac
    f=".opencode/agents/$agent.md"
    [ -f "$f" ] || { err "contract names an unknown agent: $agent"; continue; }
    norm=$(printf '%s' "$path" | sed -E 's/<[^>]*>/X/g')
    if ! grep -qF "$norm" <(sed -E 's/<[^>]*>/X/g' "$f"); then
        err "$agent default path drift: contract '$path' not found in the prompt"
    fi
    if [ -n "$status" ]; then
        decl=$(grep -oE '"(reviewer_status|verifier_status)": "[^"]*"' "$f" | head -1 | sed -E 's/.*: "([^"]*)"/\1/')
        if [ -z "$decl" ] && [ "$agent" = "implementation-worker" ]; then
            decl=$(grep -oE '^  "status": "[^"]*"' "$f" | head -1 | sed -E 's/.*: "([^"]*)"/\1/')
        fi
        if [ -z "$decl" ]; then
            err "$agent declares no status enum but the contract lists one"
            continue
        fi
        a=$(printf '%s' "$status" | tr ',' ' ' | tr '|' ' ' | tr -s ' ' '\n' | grep -v '^$' | sort | tr '\n' ',')
        b=$(printf '%s' "$decl" | tr ',' ' ' | tr '|' ' ' | tr -s ' ' '\n' | grep -v '^$' | sort | tr '\n' ',')
        [ "$a" = "$b" ] || err "$agent status enum drift: contract '$status' vs prompt '$decl'"
    fi
done < <(awk -F'|' '
    /^\| `[a-z][a-z0-9-]*` \|/ {
        a=$2; p=$4; s=$5;
        gsub(/`/, "", a); gsub(/`/, "", p); gsub(/`/, "", s);
        gsub(/^[ \t]+|[ \t]+$/, "", a); gsub(/^[ \t]+|[ \t]+$/, "", p); gsub(/^[ \t]+|[ \t]+$/, "", s);
        print a "\t" p "\t" s;
    }' "$contract")
end "per-agent default path and status enum agree with the contract"

# No stale default report paths.
begin
if grep -rnF -e 'docs/testing/live-host-verification.json' -e 'docs/reviews/architecture-review.json' .opencode/agents/*.md >/dev/null 2>&1; then
    err "stale default report path remains in an agent prompt"
fi
end "no stale default report paths"

# Worker/reviewer report pairing, honoring the exemption file.
begin
exemptions="docs/implementation/review-exemptions.json"
exempt=$(grep -oE '"task"[[:space:]]*:[[:space:]]*"[^"]+"' "$exemptions" 2>/dev/null \
    | sed -E 's/.*"([^"]+)"$/\1/' | tr '\n' ' ')
for cb in $(grep -oE '"covered_by"[^]]*' "$exemptions" 2>/dev/null | grep -oE 'docs/[^"]+\.json'); do
    [ -f "$cb" ] || err "exemption covered_by file is missing: $cb"
done
for d in docs/implementation/*/; do
    b=$(basename "$d")
    case "$b" in
        phase-*|final-review|planning|orchestrator-sessions|release-*|publish-release-changelog) continue ;;
    esac
    [ -f "$d/worker-report.json" ] || continue
    if [ ! -f "$d/reviewer-report.json" ]; then
        case " $exempt " in
            *" $b "*) : ;;
            *) err "task $b has a worker report but no reviewer report (add an exemption only if covered)" ;;
        esac
    fi
done
end "worker/reviewer report pairing checked"

# The canonical plan state must carry the review evidence the commit gate
# requires, so state.json cannot under-report what was reviewed.
begin
state="docs/plan/state.json"
if [ ! -f "$state" ]; then
    err "missing $state"
elif ! command -v jq >/dev/null 2>&1; then
    err "jq is required to check $state but was not found on PATH"
else
    for d in docs/implementation/*/; do
        b=$(basename "$d")
        case "$b" in
            phase-*|final-review|planning|orchestrator-sessions|release-*|publish-release-changelog) continue ;;
        esac
        [ -f "$d/worker-report.json" ] || continue
        case " $exempt " in
            *" $b "*) continue ;;
        esac
        if ! jq -e --arg id "$b" '
                any(.phases[].tasks[];
                    .id == $id and ((.reports // []) | any(test("reviewer-report"))))
            ' "$state" >/dev/null 2>&1; then
            err "task $b has a reviewer report on disk but $state does not list it in reports"
        fi
    done
fi
end "plan state lists the review evidence"

# Contract enforcement scope must not drift from the script.
begin
grep -q "from phase $APPLIES_FROM_PHASE" "$contract" \
    || err "APPLIES_FROM_PHASE=$APPLIES_FROM_PHASE is not stated in $contract (Enforcement scope)"
end "contract enforcement scope agrees with the script"

# Phase gate records: gate conformance in the phase review, and the phase-level
# orchestration shape, from APPLIES_FROM_PHASE onward.
begin
if ! command -v jq >/dev/null 2>&1; then
    err "jq is required to check phase gate records but was not found on PATH"
else
    for d in docs/implementation/phase-*/; do
        p=$(basename "$d")
        p=${p#phase-}
        case "$p" in ''|*[!0-9]*) continue ;; esac
        [ "$p" -ge "$APPLIES_FROM_PHASE" ] || continue
        pr="$d/phase-review.json"
        if [ -f "$pr" ]; then
            if ! jq -e . "$pr" >/dev/null 2>&1; then
                err "phase $p review is not valid JSON"
            elif [ "$(jq -r 'if (.gate_conformance.gate_text_audited? == null)
                                 or (.gate_conformance.matches_plan_of_record? == null)
                             then "missing" else "present" end' "$pr")" = "missing" ]; then
                err "phase $p review lacks the contract's gate_conformance block (gate_text_audited, matches_plan_of_record)"
            fi
            # The advisory agent-process review must be present, and its
            # observations must be evidence-backed when it makes any.
            if [ "$(jq -r 'if has("agent_process") then "present" else "absent" end' "$pr")" = "absent" ]; then
                err "phase $p review lacks the advisory agent_process section"
            else
                for k in scope measurements observations; do
                    jq -e --arg k "$k" '.agent_process | has($k)' "$pr" >/dev/null 2>&1 \
                        || err "phase $p agent_process lacks '$k'"
                done
                # `observations` must be an array: a scalar would otherwise skip
                # the evidence check entirely rather than fail it.
                if [ "$(jq -r '.agent_process.observations | type' "$pr")" != "array" ]; then
                    err "phase $p agent_process.observations is not an array"
                elif [ "$(jq -r '[.agent_process.observations[]? | select((.evidence | type) != "array" or (.evidence | length) == 0 or (.proposed_change // "") == "")] | length' "$pr")" != "0" ]; then
                    err "phase $p has an agent_process observation with no evidence or no proposed_change"
                fi
            fi
        fi
        o="$d/orchestration.json"
        if [ -f "$o" ]; then
            if ! jq -e . "$o" >/dev/null 2>&1; then
                err "phase $p orchestration record is not valid JSON"
            else
                for k in status gate gate_status phase_review phase_review_status; do
                    jq -e --arg k "$k" 'has($k) and (.[$k] != null) and (.[$k] != "")' "$o" >/dev/null 2>&1 \
                        || err "phase $p orchestration record lacks '$k'"
                done
                if [ "$(jq -r '.status // ""' "$o")" = "COMPLETE" ] \
                   && [ "$(jq -r '.gate_status // ""' "$o")" = "PENDING_PHASE_REVIEW" ]; then
                    err "phase $p is COMPLETE but its orchestration record still says gate_status PENDING_PHASE_REVIEW"
                fi
            fi
        fi
    done
fi
end "phase gate conformance and orchestration shape checked"

# The release review carries the same advisory agent-process section. Scoped to
# releases whose work is at or after APPLIES_FROM_PHASE, so a review recorded
# before the contract is not retro-required.
begin
rr="docs/implementation/final-review/release-review.json"
if [ ! -f "$rr" ]; then
    : # no release review recorded yet
elif ! jq -e . "$rr" >/dev/null 2>&1; then
    err "release review is not valid JSON"
else
    # The reviewed release must have work at or after APPLIES_FROM_PHASE. Ask the
    # canonical plan state, not the review's own prose fields: `state.json` knows
    # which phases belong to which release. Release strings differ in arity (a
    # release is "1.2.0", a build version "1.2.0.0"), so compare on major.minor.
    review_mm=$(jq -r '.release // ""' "$rr" 2>/dev/null | cut -d. -f1,2)
    latest_phase=$(jq -r --arg r "$review_mm" '[.phases[]
            | select((.release | split(".") | .[0:2] | join(".")) == $r)
            | .id] | max // 0' docs/plan/state.json 2>/dev/null)
    if [ "${latest_phase:-0}" -ge "$APPLIES_FROM_PHASE" ] || [ "${latest_phase:-0}" -eq 0 ]; then
        if [ "$(jq -r 'if has("agent_process") then "present" else "absent" end' "$rr")" = "absent" ]; then
            err "release review lacks the advisory agent_process section"
        else
            for k in scope measurements observations; do
                jq -e --arg k "$k" '.agent_process | has($k)' "$rr" >/dev/null 2>&1 \
                    || err "release review agent_process lacks '$k'"
            done
            if [ "$(jq -r '.agent_process.observations | type' "$rr")" != "array" ]; then
                err "release review agent_process.observations is not an array"
            elif [ "$(jq -r '[.agent_process.observations[]? | select((.evidence | type) != "array" or (.evidence | length) == 0 or (.proposed_change // "") == "")] | length' "$rr")" != "0" ]; then
                err "release review has an agent_process observation with no evidence or no proposed_change"
            fi
        fi
    fi
fi
end "release agent-process review present"

# A conformance check that a task's implementation depended on must have run
# before the worker was dispatched, not after. Enforced from
# APPLIES_FROM_PHASE onward; earlier records predate the `conformance` block.
begin
if ! command -v jq >/dev/null 2>&1; then
    err "jq is required to check task conformance ordering but was not found on PATH"
else
    for d in docs/implementation/*/; do
        b=$(basename "$d")
        case "$b" in
            phase-*|final-review|planning|orchestrator-sessions|release-*|publish-release-changelog) continue ;;
        esac
        o="$d/orchestration.json"
        [ -f "$o" ] || continue
        p=${b%%.*}
        case "$p" in ''|*[!0-9]*) continue ;; esac
        [ "$p" -ge "$APPLIES_FROM_PHASE" ] || continue
        if ! jq -e . "$o" >/dev/null 2>&1; then
            err "task $b orchestration record is not valid JSON"
            continue
        fi
        if [ "$(jq -r 'if has("conformance") then "present" else "absent" end' "$o")" = "absent" ]; then
            err "task $b has no conformance block; record whether an unverified assumption governed it"
            continue
        fi
        # The first worker invocation, as a 1-based index into `subagents`.
        # Any jq failure here must be loud: a silently defaulted index would skip
        # the ordering rule entirely.
        first_worker=$(jq -r '[.subagents | to_entries[] | select(.value.role == "implementation-worker") | .key + 1] | min // 0' "$o") \
            || { err "task $b has an unreadable subagents list"; continue; }
        case "$first_worker" in
            ''|*[!0-9]*) err "task $b has a non-numeric first worker index: $first_worker"; continue ;;
        esac
        if [ "$(jq -r '.conformance.required // false' "$o")" = "true" ]; then
            n=$(jq -r '(.conformance.reports // []) | length' "$o") \
                || { err "task $b has an unreadable conformance.reports"; continue; }
            case "$n" in
                ''|*[!0-9]*) err "task $b has a non-numeric conformance.reports count: $n"; continue ;;
            esac
            if [ "$n" -eq 0 ]; then
                err "task $b declares conformance.required with no reports"
            fi
            while IFS= read -r rep; do
                [ -n "$rep" ] || continue
                [ -f "$rep" ] || err "task $b cites a conformance report that does not exist: $rep"
                cited=$(jq -r --arg r "$rep" '[.subagents[]? | select((.report // .execution.report // "") == $r)] | length' "$o") \
                    || { err "task $b has an unreadable subagents list"; continue; }
                if [ "$cited" -eq 0 ]; then
                    err "task $b cites conformance report $rep but records no specialist invocation for it"
                fi
            done < <(jq -r '.conformance.reports[]? // empty' "$o")
            # Ordering: every specialist invocation must precede the first worker.
            if [ "$first_worker" -gt 0 ]; then
                late=$(jq -r --argjson w "$first_worker" '[.subagents | to_entries[]
                        | select((.key + 1) > $w)
                        | select(.value.role == "jellyfin-expert" or .value.role == "arr-api-researcher" or .value.role == "architecture-reviewer")
                        | "\(.value.role) at position \(.key + 1)"] | join("; ")' "$o")
                if [ -n "$late" ]; then
                    err "task $b declares a required conformance check but ran $late after the worker was dispatched"
                fi
            fi
        else
            if [ -z "$(jq -r '.conformance.reason // ""' "$o")" ]; then
                err "task $b declares conformance.required false with no reason"
            fi
        fi
    done
fi
end "task conformance checks precede implementation"

if [ "$fail" -eq 0 ]; then
    echo "check-agents: PASS"
else
    echo "check-agents: FAIL" >&2
fi
exit "$fail"
