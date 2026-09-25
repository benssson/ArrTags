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

# 1. Generated status blocks are current.
if command -v dotnet >/dev/null 2>&1; then
    if dotnet run scripts/render-docs-state.cs -- --check >/dev/null 2>&1; then
        ok "generated status blocks are current"
    else
        err "generated status blocks are stale (run: dotnet run scripts/render-docs-state.cs)"
    fi
else
    echo "skip: dotnet not on PATH; source /config/arrtags-env.sh to check generated blocks"
fi

# 2. Compatibility stubs stay short and non-normative.
for f in docs/architecture.md docs/data-model.md docs/decisions.md \
         docs/changelog.md docs/project-status.md docs/implementation-readiness.md; do
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
if [ -n "$sha" ]; then
    grep -qF "$sha" docs/release/build-and-release.md \
        || err "state.json artifact sha256 not found in docs/release/build-and-release.md"
    ok "artifact identity agrees"
else
    err "no artifact sha256 in docs/plan/state.json"
fi

# 7. Doc budget for the always-read surfaces.
budget() { n=$(wc -l < "$1"); [ "$n" -le "$2" ] || err "$1 is $n lines (budget $2)"; }
budget PLANS.md 400
budget docs/status.md 140
budget docs/INDEX.md 220
ok "doc budget checked"

if [ "$fail" -eq 0 ]; then
    echo "check-docs: PASS"
else
    echo "check-docs: FAIL" >&2
fi
exit "$fail"
