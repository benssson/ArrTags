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
# plan state lists the on-disk review evidence, the phase-gate records carry the
# contract's gate-conformance and orchestration shape, and from phase 26 each
# phase-level aggregate reconciles with the recorded invocations.
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

# First phase number at which the phase-level aggregate consistency rule is
# enforced. Phases before 26 predate it and are immutable history. Must match
# the contract's "Enforcement scope" section; a disagreement is itself a failure.
AGGREGATE_APPLIES_FROM_PHASE=26

# Absolute tolerance for recomputed aggregate cost_usd sums. Double-precision
# summation-order noise at these magnitudes is ~1e-13, and the project's
# recorded float-sum noise reaches the phase-17 P17-F1 scale (5.2e-8); a real
# error (a dropped or added invocation, ~0.01 and up) must still fail. Token
# and duration sums are compared exactly.
COST_TOLERANCE_USD=0.000001

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

# The all-phases rule: a task that has a worker report and is not exempt lists
# its reviewer report in the canonical plan state. This check is deliberately
# not gated by APPLIES_FROM_PHASE, so de-listing a historical reviewer report
# below the phase scope still fails. From APPLIES_FROM_PHASE the on-disk
# evidence check below is stricter and runs independently of this one.
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
end "plan state lists the reviewer report from every phase"

# The canonical plan state must carry the on-disk report evidence the commit
# gate requires, so state.json cannot under-report what was reviewed. From
# APPLIES_FROM_PHASE onward, the reports array lists the base report for every
# kind that exists on disk for the task and every preserved attempt on disk for
# every kind it lists (contract "Where reports go" rule 5). Both the current
# `.attempt-<n>` file form and the legacy `-attempt-<n>` form are matched.
begin
state="docs/plan/state.json"
if [ ! -f "$state" ]; then
    err "missing $state"
elif ! command -v jq >/dev/null 2>&1; then
    err "jq is required to check $state but was not found on PATH"
else
    report_kinds="worker-report.json reviewer-report.json test-quality-review.json security-review.json live-verification.json documentation-review.json"
    for d in docs/implementation/*/; do
        b=$(basename "$d")
        case "$b" in
            phase-*|final-review|planning|orchestrator-sessions|release-*|publish-release-changelog) continue ;;
        esac
        [ -f "$d/worker-report.json" ] || continue
        case " $exempt " in
            *" $b "*) continue ;;
        esac
        p=${b%%.*}
        case "$p" in ''|*[!0-9]*) continue ;; esac
        [ "$p" -ge "$APPLIES_FROM_PHASE" ] || continue
        rel="docs/implementation/$b"
        ondisk=""
        for k in $report_kinds; do
            stem=${k%.json}
            if [ -f "$d/$k" ]; then
                ondisk="${ondisk}${rel}/$k"$'\n'
            fi
            for f in "$d/$stem".attempt-*.json "$d/$stem"-attempt-*.json; do
                [ -f "$f" ] || continue
                ondisk="${ondisk}${rel}/$(basename "$f")"$'\n'
            done
        done
        missing=$(jq -r --arg id "$b" --rawfile files <(printf '%s' "$ondisk") '
                def basename: split("/") | last;
                (["worker-report.json","reviewer-report.json","test-quality-review.json",
                  "security-review.json","live-verification.json",
                  "documentation-review.json"]) as $kinds
                | ($files | split("\n") | map(select(length > 0))) as $ondisk
                | ([.phases[].tasks[] | select(.id == $id) | (.reports // [])[]] | unique) as $listed
                | ($kinds | map(rtrimstr(".json"))) as $stems
                | ([$ondisk[]
                    | . as $f
                    | ($f | basename) as $n
                    | if ($kinds | index($n)) != null then $f
                      elif ($stems
                            | map(. as $s | select($n | test("^" + $s + "(\\.|-)?attempt-[0-9]+\\.json$")))
                            | first) as $s
                           | any($listed[];
                                 (basename == ($s + ".json"))
                                 or (basename | test("^" + $s + "(\\.|-)?attempt-[0-9]+\\.json$")))
                      then $f
                      else empty
                      end]) as $required
                | ($required - $listed) | join(", ")
            ' "$state") || missing="(report list evaluation failed)"
        if [ -n "$missing" ]; then
            err "task $b has on-disk report evidence not listed in $state reports: $missing"
        fi
    done
fi
end "plan state lists the on-disk report evidence"

# Contract enforcement scope must not drift from the script.
begin
grep -q "from phase $APPLIES_FROM_PHASE" "$contract" \
    || err "APPLIES_FROM_PHASE=$APPLIES_FROM_PHASE is not stated in $contract (Enforcement scope)"
end "contract enforcement scope agrees with the script"

# Aggregate enforcement scope must not drift from the script either.
begin
grep -q "from phase $AGGREGATE_APPLIES_FROM_PHASE" "$contract" \
    || err "AGGREGATE_APPLIES_FROM_PHASE=$AGGREGATE_APPLIES_FROM_PHASE is not stated in $contract (Enforcement scope)"
end "contract aggregate enforcement scope agrees with the script"

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

# Phase-level aggregate consistency, from AGGREGATE_APPLIES_FROM_PHASE onward:
# the phase record must carry a `subagents` array, and its aggregate's numeric
# fields must equal the values recomputed from the task-level records plus the
# phase record's own phase-reviewer entries. Phases before 26 predate the rule;
# their records are immutable history and skipped.
#
# Recomputation definitions (the guard and the records must not disagree):
# * A phase's task records are the docs/implementation/<n>.<k>/orchestration.json
#   files on disk. task_count is the number of those records;
#   task_level_invocations_per_task maps each id to that record's `.subagents |
#   length`; task_level_invocations is their sum.
# * The phase record's `subagents` array carries the task-record invocations plus
#   any phase-reviewer entries. subagent_invocations_total =
#   task_level_invocations + phase_reviewer_invocations (a phase-reviewer entry
#   recorded inside a task record is already part of the task-level sums and is
#   not counted twice).
# * phase_reviewer_invocations is the number of phase-reviewer-role entries in
#   the phase record's own `subagents` array (0 when the release review was the
#   gate and no separate phase-reviewer record exists); the phase_reviewer_*
#   cost/token/duration sums are over those same entries.
# * role_mix maps each role to its count over all recorded invocations: the
#   task-record invocations plus the phase record's own phase-reviewer entries,
#   so it sums to subagent_invocations_total (the phase-20 worked example
#   includes phase-reviewer: 2).
# * review_rounds and test_quality_review_rounds count implementation-reviewer
#   and test-quality-reviewer invocations (attempt-0 FAILED included);
#   rework_rounds is the sum of the task records' `.rework | length` (never the
#   phase `tasks`-array figures).
# * task_level_* cost/token/duration sums are over task-record invocations; the
#   unsuffixed cost_usd / total_tokens / duration_seconds = task-level +
#   phase-reviewer.
# * first_pass_approval_rate and note are prose: not recomputed, not checked.
# * total_tokens and duration_seconds compare exactly; cost_usd compares within
#   an absolute COST_TOLERANCE_USD (see its definition above).
begin
if ! command -v jq >/dev/null 2>&1; then
    err "jq is required to check phase aggregates but was not found on PATH"
else
    for d in docs/implementation/phase-*/; do
        p=$(basename "$d")
        p=${p#phase-}
        case "$p" in ''|*[!0-9]*) continue ;; esac
        [ "$p" -ge "$AGGREGATE_APPLIES_FROM_PHASE" ] || continue
        o="$d/orchestration.json"
        [ -f "$o" ] || continue
        if ! jq -e . "$o" >/dev/null 2>&1; then
            continue # already reported by the phase-gate section above
        fi
        if [ "$(jq -r '.subagents | type' "$o")" != "array" ]; then
            err "phase $p orchestration record lacks a subagents array"
        fi
        # A phase review on disk must be accounted for in the record's own
        # invocations: at least one phase-reviewer entry in `subagents`, and a
        # `phase_reviewer_invocations` equal to the count of those entries. The
        # scope constant above grandfathers phases before 26; a missing or
        # non-array `subagents` already fails the shape check.
        phase_review_files=0
        for f in "$d"/phase-review*.json; do
            [ -f "$f" ] && phase_review_files=1
        done
        if [ "$phase_review_files" -eq 1 ] && [ "$(jq -r '.subagents | type' "$o")" = "array" ]; then
            pr_entries=$(jq -r '[.subagents[] | select(.role == "phase-reviewer")] | length' "$o") || pr_entries=""
            case "$pr_entries" in
                ''|*[!0-9]*)
                    err "phase $p phase-reviewer invocations in subagents could not be counted"
                    ;;
                *)
                    if [ "$pr_entries" -eq 0 ]; then
                        err "phase $p has a phase review on disk but records no phase-reviewer invocation"
                    fi
                    recorded_pr=$(jq -r '.aggregate.phase_reviewer_invocations // "missing"' "$o") \
                        || recorded_pr="unreadable"
                    if [ "$recorded_pr" != "$pr_entries" ]; then
                        err "phase $p aggregate phase_reviewer_invocations is $recorded_pr but the record has $pr_entries phase-reviewer invocation(s)"
                    fi
                    ;;
            esac
        fi
        task_files=()
        for t in docs/implementation/"$p".*/orchestration.json; do
            [ -f "$t" ] || continue
            task_files+=("$t")
        done
        task_records="[]"
        if [ "${#task_files[@]}" -gt 0 ]; then
            task_records=$(jq -c -n '
                    [inputs | {id: (input_filename | split("/") | .[-2]),
                               subagents: (.subagents // []),
                               rework: (.rework // [])}]
                ' "${task_files[@]}") \
                || { err "phase $p task records are unreadable"; continue; }
        fi
        mismatches=$(printf '%s' "$task_records" | jq -r --slurpfile phase "$o" \
                --argjson tol "$COST_TOLERANCE_USD" '
                def abs: if . < 0 then -. else . end;
                ($phase[0]) as $o
                | . as $t
                | ($o.aggregate // {}) as $a
                | if ($a | type) != "object" then "aggregate is not an object"
                  else
                    ([$t[].subagents[]]) as $inv
                    | ([($o.subagents // [])
                        | if type == "array" then .[] else empty end
                        | select(.role == "phase-reviewer")]) as $pr
                    | {
                        task_count: ($t | length),
                        task_level_invocations_per_task:
                            ($t | map({key: .id, value: (.subagents | length)}) | from_entries),
                        task_level_invocations: ($inv | length),
                        subagent_invocations_total: (($inv | length) + ($pr | length)),
                        phase_reviewer_invocations: ($pr | length),
                        role_mix: (reduce (($inv + $pr)[]) as $s ({}; .[$s.role // ""] += 1)),
                        review_rounds: ([$inv[] | select(.role == "implementation-reviewer")] | length),
                        test_quality_review_rounds: ([$inv[] | select(.role == "test-quality-reviewer")] | length),
                        rework_rounds: ([$t[] | .rework | length] | add // 0),
                        task_level_cost_usd: ([$inv[] | .execution.cost_usd // 0] | add // 0),
                        task_level_total_tokens: ([$inv[] | .execution.total_tokens // 0] | add // 0),
                        task_level_duration_seconds: ([$inv[] | .execution.duration_seconds // 0] | add // 0),
                        phase_reviewer_cost_usd: ([$pr[] | .execution.cost_usd // 0] | add // 0),
                        phase_reviewer_total_tokens: ([$pr[] | .execution.total_tokens // 0] | add // 0),
                        phase_reviewer_duration_seconds: ([$pr[] | .execution.duration_seconds // 0] | add // 0)
                      } as $e
                    | ($e + {
                        cost_usd: ($e.task_level_cost_usd + $e.phase_reviewer_cost_usd),
                        total_tokens: ($e.task_level_total_tokens + $e.phase_reviewer_total_tokens),
                        duration_seconds: ($e.task_level_duration_seconds + $e.phase_reviewer_duration_seconds)
                      }) as $e
                    | [
                        (["task_count","task_level_invocations","subagent_invocations_total",
                          "phase_reviewer_invocations","review_rounds","test_quality_review_rounds",
                          "rework_rounds","task_level_total_tokens","task_level_duration_seconds",
                          "phase_reviewer_total_tokens","phase_reviewer_duration_seconds",
                          "total_tokens","duration_seconds"]
                         | .[] as $f
                         | if $a[$f] == $e[$f] then empty
                           else "\($f): recorded \($a[$f]) != recomputed \($e[$f])" end),
                        (if $a.task_level_invocations_per_task == $e.task_level_invocations_per_task then empty
                         else "task_level_invocations_per_task: recorded \($a.task_level_invocations_per_task) != recomputed \($e.task_level_invocations_per_task)" end),
                        (if $a.role_mix == $e.role_mix then empty
                         else "role_mix: recorded \($a.role_mix) != recomputed \($e.role_mix)" end),
                        (["task_level_cost_usd","phase_reviewer_cost_usd","cost_usd"]
                         | .[] as $f
                         | if (($a[$f] | type) == "number")
                              and ((($a[$f] - $e[$f]) | abs) <= $tol) then empty
                           else "\($f): recorded \($a[$f]) != recomputed \($e[$f]) (tolerance \($tol))" end)
                      ]
                    | join("; ")
                  end
            ') || { err "phase $p aggregate could not be evaluated"; continue; }
        if [ -n "$mismatches" ]; then
            err "phase $p aggregate does not reconcile with the task records: $mismatches"
        fi
    done
fi
end "phase aggregates reconcile with the task records"

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
