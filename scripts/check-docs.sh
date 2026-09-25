#!/usr/bin/env bash
# ArrTags documentation-integrity checks.
#
# This is a manual gate: the repository has no CI. Run it before committing
# documentation changes:
#   source /config/arrtags-env.sh   # if dotnet is not on PATH
#   scripts/check-docs.sh
#
# It verifies the invariants described in docs/INDEX.md: generated status
# blocks are current, compatibility stubs stay non-normative and short, indexes
# cover their files, the recorded artifact identity agrees with the release
# record, and the doc budget holds.
set -uo pipefail

cd "$(dirname "$0")/.." || exit 1
fail=0
err() { echo "FAIL: $*" >&2; fail=1; }
ok() { echo "ok: $*"; }

# 1. Generated status blocks are current and plan state is consistent.
if command -v dotnet >/dev/null 2>&1; then
    if out=$(dotnet run scripts/render-docs-state.cs -- --check 2>&1); then
        ok "generated status blocks are current; plan state is consistent"
    else
        err "documentation state check failed:"
        printf '%s\n' "$out" | sed 's/^/    /' >&2
    fi
else
    echo "skip: dotnet not on PATH; source /config/arrtags-env.sh to check generated blocks"
fi

# 2. Compatibility stubs stay short and non-normative.
for f in docs/architecture.md docs/data-model.md docs/decisions.md \
         docs/changelog.md docs/limitations.md docs/project-status.md \
         docs/implementation-readiness.md; do
    if [ ! -f "$f" ]; then err "missing stub $f"; continue; fi
    n=$(wc -l < "$f")
    [ "$n" -le 40 ] || err "$f is $n lines; a stub must stay under 40"
    if grep -qE '^## ' "$f"; then err "$f contains a normative '## ' heading"; fi
done
ok "compatibility stubs checked"

# 3. Decisions index covers every ADR file.
adr_files=$(find docs/decisions -maxdepth 1 -name 'ADR-*.md' | wc -l)
adr_rows=$(grep -cE '^\| ADR-[0-9]+' docs/decisions/00-index.md)
[ "$adr_files" -eq "$adr_rows" ] || err "decisions index rows ($adr_rows) != ADR files ($adr_files)"
ok "decisions index ($adr_files ADRs)"

# 4. Limitations index covers every ID.
missing=0
for id in $(grep -hoE '^### [A-Za-z0-9-]+\.' \
        docs/limitations/open.md docs/limitations/accepted.md \
        docs/limitations/exclusions.md docs/limitations/archive.md 2>/dev/null \
        | sed -E 's/^### //; s/\.$//' | sort -u); do
    if ! grep -qF "| \`$id\` |" docs/limitations/00-index.md; then
        err "limitation $id missing from index"
        missing=1
    fi
done
[ "$missing" -eq 0 ] && ok "limitations index covers all IDs"

# 5. Normative indexes cover every section file.
for d in docs/architecture docs/data-model; do
    for f in "$d"/[0-9]*.md; do
        b=$(basename "$f")
        case "$b" in 00-index.md) continue ;; esac
        if ! grep -qF "($b)" "$d/00-index.md"; then err "$d index missing $b"; fi
    done
done
ok "architecture and data-model indexes checked"

# 6. Artifact identity agrees between state.json and the release record.
sha=$(grep -oE '"sha256": "[0-9a-f]{64}"' docs/plan/state.json | head -1 | grep -oE '[0-9a-f]{64}')
md5=$(grep -oE '"md5": "[0-9a-f]{32}"' docs/plan/state.json | head -1 | grep -oE '[0-9a-f]{32}')
bytes=$(grep -oE '"bytes": [0-9]+' docs/plan/state.json | head -1 | grep -oE '[0-9]+')
release_norm=$(tr -d ',' < docs/release/build-and-release.md)
if [ -n "$sha" ] && [ -n "$md5" ] && [ -n "$bytes" ]; then
    grep -qF "$sha" <<< "$release_norm" \
        || err "state.json artifact sha256 not found in docs/release/build-and-release.md"
    grep -qF "$md5" <<< "$release_norm" \
        || err "state.json artifact md5 not found in docs/release/build-and-release.md"
    grep -qE "(^|[^0-9])${bytes} bytes" <<< "$release_norm" \
        || err "state.json artifact byte count not found in docs/release/build-and-release.md"
    ok "artifact identity agrees (sha256, md5, bytes)"
else
    err "incomplete artifact identity in docs/plan/state.json"
fi

# 7. Doc budget for the always-read surfaces.
budget() { n=$(wc -l < "$1"); [ "$n" -le "$2" ] || err "$1 is $n lines (budget $2)"; }
budget PLANS.md 400
budget docs/status.md 140
budget docs/INDEX.md 220
budget docs/plan/README.md 200
budget docs/agent-contracts.md 160
budget AGENTS.md 90
ok "doc budget checked"

# 8. Relative markdown links in current-state docs resolve.
# History trees (implementation, reviews, research) and archived plans are
# exempt: they are never rewritten to match newer paths.
link_fail=0
while IFS= read -r f; do
    dir="$(dirname "$f")"
    while IFS= read -r t; do
        [ -n "$t" ] || continue
        case "$t" in http*|\#*|mailto:*) continue ;; esac
        t="${t%%#*}"
        [ -z "$t" ] && continue
        if [ ! -e "$dir/$t" ]; then
            err "dangling link in $f -> $t"
            link_fail=1
        fi
    done < <(grep -oE '\]\([^)]+\)' "$f" 2>/dev/null | sed -E 's/^\]\(//; s/\)$//')
done < <(find . -name '*.md' \
    -not -path './.git/*' -not -path './.opencode/node_modules/*' \
    -not -path './docs/implementation/*' -not -path './docs/reviews/*' \
    -not -path './docs/research/*' -not -path './docs/plan/archive/*' | sort)
[ "$link_fail" -eq 0 ] && ok "current-state links resolve"

if [ "$fail" -eq 0 ]; then
    echo "check-docs: PASS"
else
    echo "check-docs: FAIL" >&2
fi
exit "$fail"
