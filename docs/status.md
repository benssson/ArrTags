# ArrTags status

Current shipped state. The block between the markers is rendered from
[`docs/plan/state.json`](plan/state.json) by `scripts/render-docs-state.cs`; do
not hand-edit it. Everything outside the markers is a short, stable header.

<!-- BEGIN GENERATED: status -->
**Current release:** `1.2.0.0` (tag `v1.2.0`) — COMPLETE; the GitHub release, asset upload, and manifest push are not yet performed.

**Active scope:** `docs/planning/v1.3.md`. v1.3 (Phases 26, 22-25, version 1.3.0.0, release tag v1.3.0) is the accepted active scope; the plan is docs/planning/v1.3.md and it is PLANNED, not implemented. It covers goals G10-G12 and limitations F9 and F10, recorded as ADR-030..ADR-032 (Accepted (v1.3)), with decision gates DG-23..DG-25 open. v1.2 (Phases 15-21) is complete and archived at docs/plan/archive/v1.2.md with its scope plan at docs/plan/archive/v1.2-plan.md. Phases 1-21 are complete and are not renumbered or reopened; v1.3 continues the global numbering from Phase 22. Origin: the 2026-09-30/2026-10-01 live operator debugging session recorded in docs/implementation/planning/v1.3.json. V12-F7-1 remains an accepted historical record and is not re-registered; ADR-030 provides a remedy for it without rewriting it. By the user-approved plan amendment of 2026-10-02, Phase 26 (harness and agent-contract remediation) was added to v1.3 and executes first: the authoritative v1.3 phase execution order is 26, 22, 23, 24, 25. Phase 26 is internal harness maintenance with no goal, no decision gate, and no ADR; it changes no product behavior and no artifact identity. The amendment is recorded in docs/implementation/planning/v1.3-harness-phase.json.

**Artifact:** `artifacts/ArrTags_1.2.0.0.zip` — 619861 bytes, 7 entries;
SHA-256 `2a039fc7075f4e4c1a1c785eb0b3757636fc0573da1361ee814cfd4099297268`; MD5 `631fbfa5a4fb58fac5a877eeef761193`.

**Test suite (`1.2.0.0`)** (failed / passed / skipped / total): default
0 / 1966 / 66 / 2032; host-guarded 0 / 1986 / 46 / 2032; forced-native 0 / 2053 / 21 / 2074.

**Verification:** live pinned-host matrix `docs/implementation/21.13/live-verification.json`;
release security review `docs/implementation/21.14/security-review.json`; release review `docs/implementation/final-review/release-review-v1.2.json`.

**Active phases:** Phase 22, Phase 23, Phase 24, Phase 25, Phase 26; all other phases are complete.

**Open limitations:** 2 (`F9`, `F10`); see `docs/limitations/00-index.md`.
<!-- END GENERATED: status -->

## Detail

- Release process and the full per-release artifact record:
  [`docs/release/build-and-release.md`](release/build-and-release.md).
- Open, accepted, excluded, and resolved limitations:
  [`docs/limitations/00-index.md`](limitations/00-index.md).
- Completed plans and their task history: [`docs/plan/archive/`](plan/archive/).
- Completed work by release: [`docs/changelog/`](changelog/).
- What to read for a task: [`docs/INDEX.md`](INDEX.md).
