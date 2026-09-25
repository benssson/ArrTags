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
# frontmatter or forbidden status/severity tokens remain, and every task with a
# worker report has a reviewer report unless explicitly exempted.
set -uo pipefail

cd "$(dirname "$0")/.." || exit 1
fail=0
section_fail=0
err() { echo "FAIL: $*" >&2; fail=1; section_fail=1; }
begin() { section_fail=0; }
end() { [ "$section_fail" -eq 0 ] && echo "ok: $*"; }

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

if [ "$fail" -eq 0 ]; then
    echo "check-agents: PASS"
else
    echo "check-agents: FAIL" >&2
fi
exit "$fail"
