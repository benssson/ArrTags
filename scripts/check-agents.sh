#!/usr/bin/env bash
# ArrTags agent report-contract checks.
#
# Manual gate (the repository has no CI). Run alongside scripts/check-docs.sh:
#   source /config/arrtags-env.sh
#   scripts/check-agents.sh
#
# It verifies the contract in docs/agent-contracts.md: the contract exists and is
# linked, review/research agents reference it, no legacy frontmatter remains, no
# forbidden status/severity tokens are used, declared report kinds are covered,
# and every task with a worker report has a reviewer report unless exempted.
set -uo pipefail

cd "$(dirname "$0")/.." || exit 1
fail=0
err() { echo "FAIL: $*" >&2; fail=1; }
ok() { echo "ok: $*"; }

contract="docs/agent-contracts.md"
for f in "$contract" docs/INDEX.md AGENTS.md; do
    [ -f "$f" ] || err "missing $f"
done
grep -q 'docs/agent-contracts.md' docs/INDEX.md || err "docs/INDEX.md does not link $contract"
grep -q 'docs/agent-contracts.md' AGENTS.md || err "AGENTS.md does not link $contract"
ok "contract present and linked"

# Review/research agents must reference the contract.
for f in .opencode/agents/*.md; do
    base=$(basename "$f")
    case "$base" in
        implementation-worker.md|implementation-planner.md|documentation-maintainer.md|orchestrator.md) continue ;;
    esac
    grep -q 'docs/agent-contracts.md' "$f" || err "$base does not reference $contract"
done
ok "review/research agents reference the contract"

# No legacy V1 frontmatter fields.
if grep -rnE '^permission:|^variant:' .opencode/agents/*.md >/dev/null 2>&1; then
    err "legacy frontmatter (permission:/variant:) remains in an agent prompt"
fi
ok "no legacy frontmatter fields"

# No forbidden status/severity tokens (CONCERNS, bare INFO severity).
if grep -rnE '"severity": "INFO"|CONCERNS' .opencode/agents/*.md >/dev/null 2>&1; then
    err "forbidden status/severity token (INFO or CONCERNS) in an agent prompt"
fi
ok "status/severity tokens conform"

# The contract must cover every report kind used by the prompts.
for tok in worker-report.json reviewer-report.json test-quality-review.json \
           security-review.json phase-review.json release-review.json \
           live-verification.json documentation-update.json documentation-review.json; do
    grep -qF "$tok" "$contract" || err "contract does not mention $tok"
done
ok "contract covers the report kinds"

# No stale default paths.
if grep -rnF -e 'docs/testing/live-host-verification.json' -e 'docs/reviews/architecture-review.json' .opencode/agents/*.md >/dev/null 2>&1; then
    err "stale default report path remains in an agent prompt"
fi
ok "no stale default report paths"

# Worker/reviewer report pairing (exemptions honored).
exempt=$(grep -oE '"task": "[^"]+"' docs/implementation/review-exemptions.json 2>/dev/null \
    | sed -E 's/.*"task": "([^"]+)"/\1/' | tr '\n' ' ')
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
ok "worker/reviewer report pairing checked"

if [ "$fail" -eq 0 ]; then
    echo "check-agents: PASS"
else
    echo "check-agents: FAIL" >&2
fi
exit "$fail"
