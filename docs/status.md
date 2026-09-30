# ArrTags status

Current shipped state. The block between the markers is rendered from
[`docs/plan/state.json`](plan/state.json) by `scripts/render-docs-state.cs`; do
not hand-edit it. Everything outside the markers is a short, stable header.

<!-- BEGIN GENERATED: status -->
**Current release:** `1.2.0.0` (tag `v1.2.0`) — COMPLETE; the GitHub release, asset upload, and manifest push are not yet performed.

**Active scope:** none. v1.2 (Phases 15-21, version 1.2.0.0, tag v1.2.0) is COMPLETE: Gates 15-21 are met and its phases were archived verbatim from PLANS.md to docs/plan/archive/v1.2.md, with the accepted v1.2 scope plan preserved at docs/plan/archive/v1.2-plan.md. All phases 1-21 are complete, so PLANS.md holds no active phase sections and docs/planning/ is empty. No release scope is currently accepted for planning: active_scope stays null until the user accepts a new plan, which is placed under docs/planning/, named by this pointer, and whose phases continue the global numbering from Phase 22 without renumbering or reopening prior phases. The v1.2 goals G6-G9 remain in force, decision records ADR-022..ADR-029 are Accepted (v1.2), decision gates DG-15..DG-22 are resolved, and limitations F3, F4, F6, F7, and F8 are resolved by their phases (F5 remains an accepted known shipped limitation).

**Artifact:** `artifacts/ArrTags_1.2.0.0.zip` — 619861 bytes, 7 entries;
SHA-256 `2a039fc7075f4e4c1a1c785eb0b3757636fc0573da1361ee814cfd4099297268`; MD5 `631fbfa5a4fb58fac5a877eeef761193`.

**Test suite (`1.2.0.0`)** (failed / passed / skipped / total): default
0 / 1966 / 66 / 2032; host-guarded 0 / 1986 / 46 / 2032; forced-native 0 / 2053 / 21 / 2074.

**Verification:** live pinned-host matrix `docs/implementation/21.13/live-verification.json`;
release security review `docs/implementation/21.14/security-review.json`; release review `docs/implementation/final-review/release-review-v1.2.json`.

**Phases:** all 21 complete (1-21); Gates 1-21 met. Completed plans are archived in `docs/plan/archive/`.

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
