# ArrTags status

Current shipped state. The block between the markers is rendered from
[`docs/plan/state.json`](plan/state.json) by `scripts/render-docs-state.cs`; do
not hand-edit it. Everything outside the markers is a short, stable header.

<!-- BEGIN GENERATED: status -->
**Current release:** `1.1.0.0` (tag `v1.1.0`) — COMPLETE; the GitHub release, asset upload, and manifest push are not yet performed.

**Active scope:** `docs/planning/v1.2.md`. v1.2 (Phases 15-21) is accepted: goals G6-G9 and limitations F3, F4, F6, F7, F8. Decision gates DG-15..DG-22; decision records ADR-022..ADR-028 are accepted (Accepted (v1.2)). DG-19, DG-20, and DG-21 (the F4, F6, and F7 mechanisms) are resolved by the v1.2 plan and their ADRs, so their dependent tasks are no longer gated. F5 is accepted as a known shipped limitation (reclassified from Open) and is excluded.

**Artifact:** `artifacts/ArrTags_1.1.0.0.zip` — 594931 bytes, 7 entries;
SHA-256 `85730fe7b3fb8b03c86a87228dc1043d42844b372a9493d4caf5bba4a7e836e1`; MD5 `547beb2f7d83cd256d3a3ce7bb7e7620`.

**Test suite (`1.1.0.0`)** (failed / passed / skipped / total): default
0 / 1494 / 63 / 1557; host-guarded 0 / 1513 / 44 / 1557; forced-native 0 / 1575 / 20 / 1595.

**Verification:** live pinned-host matrix `docs/implementation/14.3/live-verification.json`;
release security review `docs/implementation/14.4/security-review.json`; release review `docs/implementation/final-review/release-review.json`.

**Active phases:** Phase 21; all other phases are complete.

**Open limitations:** none; see `docs/limitations/00-index.md`.
<!-- END GENERATED: status -->

## Detail

- Release process and the full per-release artifact record:
  [`docs/release/build-and-release.md`](release/build-and-release.md).
- Open, accepted, excluded, and resolved limitations:
  [`docs/limitations/00-index.md`](limitations/00-index.md).
- Completed plans and their task history: [`docs/plan/archive/`](plan/archive/).
- Completed work by release: [`docs/changelog/`](changelog/).
- What to read for a task: [`docs/INDEX.md`](INDEX.md).
