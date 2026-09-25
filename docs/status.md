# ArrTags status

Current shipped state. The block between the markers is rendered from
[`docs/plan/state.json`](plan/state.json) by `scripts/render-docs-state.cs`; do
not hand-edit it. Everything outside the markers is a short, stable header.

<!-- BEGIN GENERATED: status -->
**Current release:** `1.1.0.0` (tag `v1.1.0`) — COMPLETE; the GitHub release,
asset upload, and manifest push are not yet performed.

**Active scope:** none. No release scope is currently accepted for planning
(`docs/planning/` is empty); the F6-F8 limitations are candidates for a future
release.

**Artifact:** `artifacts/ArrTags_1.1.0.0.zip` — 594,931 bytes, 7 entries;
SHA-256 `85730fe7b3fb8b03c86a87228dc1043d42844b372a9493d4caf5bba4a7e836e1`;
MD5 `547beb2f7d83cd256d3a3ce7bb7e7620`.

**Test suite (`1.1.0.0`)** (failed / passed / skipped / total): default
0 / 1,494 / 63 / 1,557; host-guarded 0 / 1,513 / 44 / 1,557; forced-native
0 / 1,575 / 20 / 1,595.

**Verification:** live pinned-host matrix
`docs/implementation/14.3/live-verification.json` (all eight rows pass);
release security review `docs/implementation/14.4/security-review.json`
(0 open BLOCKER/HIGH); release review
`docs/implementation/final-review/release-review.json`
(`SHIP_WITH_ACCEPTED_LIMITATIONS`).

**Phases:** 1-14 complete; Gates 1-14 met. Completed plans are archived in
`docs/plan/archive/`.

**Open limitations:** 6 (`F3`, `F4`, `F5`, `F6`, `F7`, `F8`); see
`docs/limitations/00-index.md`.
<!-- END GENERATED: status -->

## Detail

- Release process and the full per-release artifact record:
  [`docs/release/build-and-release.md`](release/build-and-release.md).
- Open, accepted, excluded, and resolved limitations:
  [`docs/limitations/00-index.md`](limitations.md).
- Completed plans and their task history: [`docs/plan/archive/`](plan/archive/).
- Completed work by release: [`docs/changelog/`](changelog/).
- What to read for a task: [`docs/INDEX.md`](INDEX.md).
