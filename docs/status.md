# ArrTags status

Current shipped state. The block between the markers is rendered from
[`docs/plan/state.json`](plan/state.json) by `scripts/render-docs-state.cs`; do
not hand-edit it. Everything outside the markers is a short, stable header.

<!-- BEGIN GENERATED: status -->
**Current release:** `1.1.0.0` (tag `v1.1.0`) — COMPLETE; the GitHub release, asset upload, and manifest push are not yet performed.

**Active scope:** `docs/planning/v1.2.md`. v1.2 (Phases 15-21) is accepted: goals G6-G9 and limitations F3, F4, F6, F7, F8. Decision gates DG-15..DG-22; decision records ADR-022..ADR-028 are accepted (Accepted (v1.2)). DG-19, DG-20, and DG-21 (the F4, F6, and F7 mechanisms) are resolved by the v1.2 plan and their ADRs, so their dependent tasks are no longer gated. F5 is accepted as a known shipped limitation (reclassified from Open) and is excluded. Phase 21 additionally carries corrective tasks 21.7-21.14: the 21.5 release security review recorded the new open MEDIUM finding SEC-21.5-01 (the provider clients follow HTTP redirects, so X-Api-Key is re-sent to a 3xx Location origin, contrary to ADR-005), which the user decided to fix now and re-verify rather than accept, and the 21.10 review re-verified the two carried LOW log-subject residuals SEC-21.5-02 (the subject removes Cc scalars only, so Cf format characters and Zl/Zp separators reach the log verbatim) and SEC-21.5-03 (the file-name component uses host-platform Path.GetFileName semantics, so a Windows-style or UNC path is emitted whole on the pinned Linux host) as still open, which the user also decided to fix rather than accept. Tasks 21.12, 21.13, and 21.14 re-derive the deliverables of 21.8, 21.9, and 21.10 against the fully corrected build; SEC-21.5-04 duplicates the already-accepted SEC-2 and is not part of this fix. 21.6 (changelog, release readiness, manifest commit, annotated v1.2.0 tag) runs last and updates the release record for the new artifact identity.

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
