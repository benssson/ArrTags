# ArrTags Execution Plan

## Current State

Current project status lives in one place: [`docs/status.md`](docs/status.md), rendered from
[`docs/plan/state.json`](docs/plan/state.json). The milestone table below is the per-phase
scheduling view for the active scope; completed phases are archived in
[`docs/plan/archive/`](docs/plan/archive/).

## Active Planning Scope

<!-- BEGIN GENERATED: active-scope -->
**Active plan:** `docs/planning/v1.3.md`.
<!-- END GENERATED: active-scope -->

**v1.3 (Phases 22-25 plus the added Phase 26, version `1.3.0.0`, release tag `v1.3.0`) is the accepted active
scope.** Its plan is [`docs/planning/v1.3.md`](docs/planning/v1.3.md) and it is **planned, not implemented**:
goals G10-G12 and limitations F9 and F10, with decision records ADR-030 through ADR-032 **accepted**
(`Accepted (v1.3)`) and decision gates DG-23 through DG-25 **open**. Phase 26 (harness and agent-contract
remediation) was added by the user-approved plan amendment of 2026-10-02, has no goal, no decision gate, and no
ADR, and executes first; it changes no product behavior and no artifact identity. v1.2 (Phases 15-21) is complete and archived: the phase text is in
[`docs/plan/archive/v1.2.md`](docs/plan/archive/v1.2.md) and its accepted scope plan is in
[`docs/plan/archive/v1.2-plan.md`](docs/plan/archive/v1.2-plan.md). Phases 1-21 are not restructured, reordered,
or reopened; v1.3 continues the global numbering at Phase 22.

v1.3 originates in a live operator debugging session on 2026-09-30/2026-10-01, recorded in
[`docs/implementation/planning/v1.3.json`](docs/implementation/planning/v1.3.json). Two open limitations were
registered from it: **F9** (a cancelled artwork mutation seals the subject into a permanently blocked state) and
**F10** (artwork skip, block, and suppressed-selection outcomes are not surfaced in any log or counter). The
accepted historical item `V12-F7-1` is **not** re-registered; ADR-030 adds an in-product remedy for it without
rewriting it.

The orchestrator determines the active scope from this pointer. When a new scope is accepted, its plan is
placed under `docs/planning/`, this pointer is updated to name it, and the implementation-planner derives its
phases into this file using the existing conventions. Phases continue the global numbering, and prior phases
are never renumbered or reopened; on completion a release's phases are moved verbatim to
[`docs/plan/archive/`](docs/plan/archive/).

## How To Use This Plan

- Keep milestone order unchanged. A milestone may be worked on only after its predecessor's gate is met,
  unless a task is explicitly marked as research or a test spike. The only reordering is the recorded,
  user-approved v1.3 amendment: Phase 26 precedes Phase 22.
- Task numbers are stable identifiers only, not an execution sequence. Each phase's **Authoritative Phase X
  execution order** is the canonical sequence: walk it from the first entry and select the first task that is
  not complete, after verifying that task's documented prerequisites. When the execution order reorders task
  numbers, the execution order wins.
- Update task checkboxes and `docs/plan/state.json` as work lands; do not mark a milestone complete until every
  acceptance criterion is verified. `docs/status.md` and the milestone table are generated from `state.json`
  (see [`docs/plan/README.md`](docs/plan/README.md)).
- Record decisions that change an architecture assumption as a new ADR under `docs/decisions/`, then update
  [`docs/architecture/00-index.md`](docs/architecture/00-index.md) or
  [`docs/data-model/00-index.md`](docs/data-model/00-index.md) before implementation relies on them.
- Keep provider DTOs, Jellyfin entities, credentials, and implementation details at their boundaries. The
  matching, metadata, rendering, and cache pipeline consumes the canonical models.
- Every milestone must leave Jellyfin safe when the provider, cache, matcher, or renderer fails.

## Active Phases

**Authoritative v1.3 phase execution order: 26, 22, 23, 24, 25.** The phase number
does not equal the order. Phase 26 was added by the user-approved plan amendment of
2026-10-02 and executes first, ahead of the highest-severity recovery defect; the
product order (recovery, observability, restoration semantics, release) is
otherwise unchanged. Within a phase, that phase's **Authoritative Phase N execution
order** is the canonical task sequence.

The v1.3 product phases follow the accepted dependency order in
[`docs/planning/v1.3.md`](docs/planning/v1.3.md) section 5: the highest-severity
recovery defect first, then the observability work that makes it verifiable, then
the restoration-verification predicate, then the release. A phase is finalized
only after its `phase-reviewer` gate passes and the phase tag
`v1.3.0-phase<N>` is created; phases do not auto-advance.

### 26. Harness and agent-contract remediation

**Goal:** none - Phase 26 is **internal harness maintenance** and is deliberately
outside the goal/DG/ADR machinery: no goal number, no decision-gate number, and no
new ADR. It changes no product behavior, no product code, and no part of the v1.3
artifact, so the `1.3.0.0` artifact identity is unchanged. A phase reviewer must not
read the absent goal as a missing-goal defect.
**Objective:** Put the agent harness and its contract into the state v1.3's product
phases and its build/live/security/release cycle require, by correcting the
agent-process failures the v1.2 advisory reviews recorded. Those failures - stale
identities left live after each artifact re-derivation, inconsistent phase
aggregates, undispositioned security findings, unbatched re-derivation, evidence
collected from a shared tree another agent was mutating - cost v1.2 two full
artifact/live/security re-derivation cycles. Running this phase first prevents
repeating them in v1.3.
**Deliverables:** the amended phase-21 orchestration record (additive: `subagents` and
a recomputed `aggregate`); the agent-contract rules; guard enforcement for the two
recurring record-keeping classes; the seven agent prompt corrections; a working
session-usage collector; and an advisory re-review of which previously advisory
classes the new guards now cover.
**Tasks:**
- [x] 26.1 Amend the phase-21 orchestration record
- [x] 26.2 Agent-contract rules
- [x] 26.3 Guard enforcement
- [ ] 26.4 Agent prompt corrections
- [ ] 26.5 Session-usage collection fix
- [ ] 26.6 Advisory re-review
**Authoritative Phase 26 execution order:** 26.1, 26.2, 26.3, 26.4, 26.5, 26.6
**Phase acceptance criteria:** the phase-21 record satisfies the contract's
phase-level shape and its aggregate reconciles exactly with the task records, added
purely additively with no phase-21 task report modified; the corrective/re-derivation
and invocation/evidence-integrity rules, the tightened reports-array rule, the
phase-gate security-disposition obligation, the enforcement-scope entries, and the
release-phase gate precedent are recorded in `docs/agent-contracts.md`; both new
guards pass on the current tree with phases 20-25 grandfathered for the aggregate
rule, and each has a recorded sensitivity probe showing it fails on a deliberately
broken input and passes when restored; each of the seven named prompts carries its
specific correction and none contradicts the contract; the session-usage collector
fetches a real session in a documented self-test, errors clearly when the server is
unreachable, and never estimates a token or cost figure; and the advisory
re-review records, with evidence, whether the new guards cover the previously
advisory classes.
**Gate 26:** Met when tasks 26.1-26.6 meet their acceptance criteria,
`scripts/check-agents.sh` and `scripts/check-docs.sh` PASS with a recorded
sensitivity probe per new guard, the phase review is approved, and the tag
`v1.3.0-phase26` is created. There is no security-review clause: Phase 26 changes
no product code and exposes no runtime surface.

### 22. Resumable artwork mutation on cancellation

**Goal:** G10 (ADR-030; ADR-003's terminal-outcome set).
**Objective:** Stop a cancelled or bounded-retryable image mutation from sealing a subject into a permanently
blocked state, and give an administrator an in-product way to re-baseline a subject already sealed by a prior
release.
**Deliverables:** a bounded post-mutation outcome classification; resumable durable state on cancellation; the
administrator-gated in-product re-baseline action; regression tests that fail against the pre-fix code; the
security review and canonical documentation.
**Tasks:**
- [ ] 22.1 Bounded post-mutation outcome classification (DG-23)
- [ ] 22.2 Cancellation leaves a resumable operation and state, with regression tests
- [ ] 22.3 Administrator-gated in-product re-baseline action
- [ ] 22.4 F9 documentation, security review, and integration verification
**Authoritative Phase 22 execution order:** 22.1, 22.2, 22.3, 22.4
**Phase acceptance criteria:** a cancellation from the image mutation, the item update, or the post-mutation
verification leaves the durable operation in a non-terminal phase and the state resumable, and the subject
resumes on the next run with no operator action; ADR-003's mutation lower bound is unchanged; a cancelled
mutation is not classified or counted as a failure; the DG-23 outcome set is recorded and covered; the
re-baseline action recovers a synthetic `OwnershipLost`/`RestoreBlocked` subject and never mutates an image
directly; no secret, path, or item name is exposed and the action is elevation-gated; the security review is
recorded with no open BLOCKER/HIGH.
**Gate 22:** Met when tasks 22.1-22.4 meet their acceptance criteria, DG-23 is resolved and recorded, the
security review is recorded with no open BLOCKER/HIGH, the phase review is approved, and the tag
`v1.3.0-phase22` is created.

### 23. Bounded artwork outcome observability

**Goal:** G11 (ADR-031; ADR-025 clause 3 as amended).
**Objective:** Emit every bounded regeneration decision and restore-block reason in the bounded artwork log, and
extend the diagnostics snapshot with bounded, value-free artwork-outcome counters and the matching panel rows.
**Deliverables:** the extended counter model; the bounded reason emission on the existing artwork log lines;
instrumentation at the planner, ownership, and restore-block boundaries; the read-only panel rows; the security
review and canonical documentation.
**Tasks:**
- [ ] 23.1 Extended artwork-outcome counter model (DG-24)
- [ ] 23.2 Emit the bounded planner decision and restore-block reasons
- [ ] 23.3 Instrument the artwork-outcome counters at their boundaries
- [ ] 23.4 Read-only settings-page panel for the new counters
- [ ] 23.5 F10 documentation and security review
**Authoritative Phase 23 execution order:** 23.1, 23.2, 23.3, 23.4, 23.5
**Phase acceptance criteria:** all six bounded `ArtworkRegenerationPlanner` skip reasons and the bounded
restore-block reason are emitted at `Information` with the ADR-026 bounded subject; the snapshot gains bounded
regeneration-skipped, ownership-blocked, and restore-blocked counters keyed by code-owned bounded enums; the
snapshot remains fixed-shape with no item identity, name, path, provider payload, credential, or unbounded
collection, and carries counts and classifications only; emission stays bounded by the existing `LogThrottle`;
the panel renders the new counters with its fail-closed behavior and adds no setting; the extended endpoint is
security-reviewed with no open BLOCKER/HIGH.
**Gate 23:** Met when tasks 23.1-23.5 meet their acceptance criteria, DG-24 is resolved and recorded, the
security review is recorded with no open BLOCKER/HIGH, the phase review is approved, and the tag
`v1.3.0-phase23` is created.

### 24. Restoration verification semantics

**Goal:** G12 (ADR-032; the ADR-002/ADR-003 restoration postcondition).
**Objective:** Settle whether the pinned host preserves image bytes across save and read-back, and derive the
restoration verification predicate from that measurement rather than from an inherited assumption.
**Deliverables:** the `jellyfin-expert` byte-fidelity spike note; the DG-25 predicate with a regression test
including its negative case; the ADR-032 and architecture documentation reconciliation.
**Tasks:**
- [ ] 24.1 Pinned-host image save/read-back byte-fidelity spike (`jellyfin-expert`)
- [ ] 24.2 Restoration verification predicate per DG-25, with regression tests
- [ ] 24.3 ADR-032 and architecture documentation reconciliation
**Authoritative Phase 24 execution order:** 24.1, 24.2, 24.3
**Phase acceptance criteria:** the spike records, against the pinned Jellyfin 12 host and for both a JPEG and a
PNG source, whether `SaveImage` followed by the ArrTags read-back is byte-identical and what the host does to
image bytes; the predicate is derived from that result, staying content-SHA-exact when the host is
byte-preserving and otherwise adopting a documented content-derived equivalence rule; the predicate remains
fail-closed for an unobservable read-back, a presence mismatch, and an equivalence failure; the publication
postcondition is reviewed against the same result; no `RenderVersion` or render-fingerprint change is required.
**Gate 24:** Met when tasks 24.1-24.3 meet their acceptance criteria, DG-25 is resolved and recorded, the phase
review is approved, and the tag `v1.3.0-phase24` is created.

### 25. v1.3 release

**Goal:** release.
**Objective:** Ship the v1.3 recovery and observability work as a reviewed, verified release artifact.
**Deliverables:** the version bump to `1.3.0.0` and documentation reconciliation; the full test suite and a
reproducible package; live pinned-host verification; the release security review; the release review, artifact
publication, and the `v1.3.0` tag.
**Tasks:**
- [ ] 25.1 Version bump to `1.3.0.0` and documentation reconciliation
- [ ] 25.2 Full test suite and reproducible build/package
- [ ] 25.3 Live pinned-host verification of the resume, re-baseline, and counters
- [ ] 25.4 Release security review
- [ ] 25.5 Release review, artifact publication, and tag `v1.3.0`
**Authoritative Phase 25 execution order:** 25.1, 25.2, 25.3, 25.4, 25.5
**Phase acceptance criteria:** the full suite passes from a clean checkout; the artifact identity is recorded
and reproducible; the live matrix covers interrupting a restore with a host restart and proving the subject
resumes, running the in-product re-baseline action against a `RestoreBlocked` subject, and confirming the new
counters move for a suppressed selection and a skipped regeneration; the release security review is recorded
with no open BLOCKER/HIGH; the release review is approved and the tag `v1.3.0` is created.
**Gate 25:** Met when tasks 25.1-25.5 meet their acceptance criteria, the release security review and release
review are recorded, and the tag `v1.3.0` is created.

## Milestone Status

<!-- BEGIN GENERATED: milestone-status -->
| # | Milestone | Status | Exit gate |
| --- | --- | --- | --- |
| 22 | Resumable artwork mutation on cancellation | PLANNED | Gate 22 |
| 23 | Bounded artwork outcome observability | PLANNED | Gate 23 |
| 24 | Restoration verification semantics | PLANNED | Gate 24 |
| 25 | v1.3 release | PLANNED | Gate 25 |
| 26 | Harness and agent-contract remediation | PLANNED | Gate 26 |
<!-- END GENERATED: milestone-status -->

Completed milestones and their gates are recorded in
[`docs/plan/archive/`](docs/plan/archive/) and in `docs/plan/state.json`.

## Completed Milestones

All milestones through Phase 21 (v1.2) are complete and archived verbatim:

- V1 (Phases 1-8): [`docs/plan/archive/v1.md`](docs/plan/archive/v1.md)
- v1.1 (Phases 9-14): [`docs/plan/archive/v1.1.md`](docs/plan/archive/v1.1.md)
- v1.2 (Phases 15-21, tag `v1.2.0`): [`docs/plan/archive/v1.2.md`](docs/plan/archive/v1.2.md)
- v1.1 scope plan: [`docs/plan/archive/v1.1-plan.md`](docs/plan/archive/v1.1-plan.md)
- v1.2 scope plan: [`docs/plan/archive/v1.2-plan.md`](docs/plan/archive/v1.2-plan.md)
- V1 readiness history: [`docs/plan/archive/v1-readiness.md`](docs/plan/archive/v1-readiness.md)

Machine state: [`docs/plan/state.json`](docs/plan/state.json).

## Decision Gates

These decisions must be resolved and recorded before the dependent work relies on them.

| Gate | Decision | Required before |
| --- | --- | --- |
| DG-1 | Exact Jellyfin 12 patch, package versions, target framework, and manifest ABI. | Milestone 1 implementation |
| DG-2 | Initial supported item and image types, including whether series/season posters are disabled or use an explicit aggregate policy. Resolved by ADR-006: V1 badge surfaces are Movie and Episode posters; Series/Season are structural and aggregation remains post-V1. | Milestones 3-5 |
| DG-3 | Initial badge fields, templates, placement, contrast, output format, text limits, and request-size policy. Resolved by ADR-009: V1 uses provider-neutral bounded badges on unindexed Movie and Episode Primary posters, lossless PNG at source dimensions, and fail-closed pass-through for unknown or failed input. | Milestone 4 |
| DG-4 | Episode numbering rules, including specials, anime, absolute numbering, double episodes, and multi-episode files. Resolved by ADR-007: number fallback is limited to regular single episodes; season zero specials, multi-episode spans, and absolute/scene numbering are excluded. | Milestone 3 |
| DG-5 | Whether path mappings are needed, and their connection-scoped representation. Resolved by ADR-008: configured path fallback is deferred out of V1, so V1 has no path mapping schema, normalization, or `ConfiguredPath` rule. | Milestone 3 |
| DG-6 | Queue, timeout, retry, concurrency, image-size, cache, and stale-state defaults. Foundation defaults are resolved by ADR-004; Milestone 6 may tune within the documented validation ranges. | Milestone 6 |
| DG-7 | Webhook exposure, authentication, payload limits, replay handling, and route administration flow. Resolved by ADR-012: an anonymous plugin route authenticated by the `X-ArrTags-Webhook-Secret` header through the constant-time versioned webhook lease, a bounded tolerant payload with a configured size limit, a bounded coalescing intake with idempotent replay handling, bounded provider-record-to-Jellyfin resolution into the existing work-hint path, and an administration flow that reuses the ADR-005 `WebhookSecret` slot. Secret persistence and versioned access remain resolved by ADR-005. | Milestone 6 |
| DG-8 | Jellyfin Enhanced duplicate-badge defaults and Spoiler Guard behavior. Resolved by ADR-011: ArrTags adds no automatic duplicate/overlap detection or suppression and no Enhanced-internals dependency; the existing poster and selector enable flags are the user's control surface, and Spoiler Guard has no material effect on ArrTags badge display. | Milestone 5 |
| DG-9 | Supported live Sonarr/Radarr release ranges and optional-field compatibility policy. Resolved by ADR-013: supported ranges are Sonarr 3.x-4.x and Radarr 3.x-6.x on `/api/v3`; absent optional fields map to explicit unknown values and a malformed required field fails closed as `ProviderIncompatible`, with no version-number gate. | Milestones 2 and 7 |
| DG-10 | Page mechanism, save/activation path, get-only collection round-trip, and acceptance of the anonymous static page-resource endpoint. Resolved by ADR-016 (v1.1). The get-only `Collection<T>` round-trip test (task 9.1) and the page-resource live confirmation (task 9.2) are verification requirements of the resolved gate, not open decisions. | Phase 9 |
| DG-11 | Allowlist scope (per-selector vs global), matching/normalization, bound, and unknown/custom-value interaction. Resolved by ADR-017 (v1.1): per-selector, case-insensitive exact match against the resolved value, bounded and validated. | Phase 12 |
| DG-12 | Inventory cache shape, TTL, invalidation sources, and bounds. Resolved by ADR-018 (v1.1). | Phase 11 |
| DG-13 | Size semantics, corner/center anchors, rail packing, and status-pill placement. Resolved by ADR-019 (v1.1): four corners plus center, preset Small/Medium/Large, derived opposite-corner status placement. | Phase 12 |
| DG-14 | Logging mechanism, verbosity model, activation, redaction contract, and volume bounds. Resolved by ADR-020 (v1.1). | Phase 10 |
| DG-15 | v1.2 G6: byte-limit display units (fixed per-field binary MB, KB for the webhook payload), exact display/store round-trip at min/default/max including the 64 KiB minima, and persistence staying bytes. Resolved by the user decision and recorded by ADR-028 (Accepted, v1.2). | Phase 16 |
| DG-16 | v1.2 G7: Extra Large factor/ordering and whether the new enum member requires a `RenderVersion`/schema advance. Resolved by the user decision and recorded by ADR-027 (Accepted, v1.2): factor 2.0, order Small/Medium/Large/Extra Large, no advance, new goldens required. | Phase 18 |
| DG-17 | v1.2 G8: the authoritative restart-required set and the modal mechanism/fallback. The binary classification (restart-required if any consumer captures; mixed settings marked restart-required) and note text are recorded by ADR-028 (Accepted, v1.2), with task 16.2 the single owner of the complete evidenced set; the modal feasibility is owned by the task 16.4 `jellyfin-expert` spike, with note text as the fail-open behavior. | Phase 16 |
| DG-18 | v1.2 F3: status-surface shape, endpoint authorization, and the counter set. Resolved by the user decision and recorded by ADR-025 (Accepted, v1.2): read-only settings-page panel, elevation-gated endpoint, bounded secret-free counters. | Phase 17 |
| DG-19 | v1.2 F4: mechanism for guaranteed successive-run coverage over a scope larger than `QueueCapacity`. Resolved by the v1.2 plan and the accepted ADR-022 (persisted `(SortName, itemId)` cursor, bounded enqueue-outcome vocabulary, resume-capable enumerator). | Phase 19 |
| DG-20 | v1.2 F6: mechanism to stop a post-save re-render being dropped by version-blind coalescing. Resolved by the v1.2 plan and the accepted ADR-023 (`ConfigurationStale`-classified re-enqueue after the in-flight slot is released, bounded by the current version). | Phase 19 |
| DG-21 | v1.2 F7: empty-resolved-selection behavior and its effect on ADR-009/ADR-003. Resolved by the v1.2 plan and the accepted ADR-024 (restore baseline, or remove when absent, for an owned empty resolved selection via the internal `ArtworkPublisher.RestoreAsync`; preserve for the other pass-through reasons including layout-empty). | Phase 20 |
| DG-22 | v1.2 G9: log subject identifier policy and the ADR-020 clause 4 amendment. Resolved by the user decision and recorded by ADR-026 (Accepted, v1.2): bounded file name, item-id fallback; security review required. | Phase 15 |
| DG-23 | v1.3 G10: the classification of each post-`MutationStarted` outcome as cancellation, bounded-retryable failure, or terminal failure, and for each whether the durable record stays non-terminal and resumable. **Open.** Skew: cancellation always stays resumable; whether a bounded-retryable failure also stays resumable, and with what bound, is the user decision. | Phase 22 |
| DG-24 | v1.3 G11: the extended ADR-025 clause 3 counter set, the bounded reason enums exposed, and the confirmation that no value, name, or path is carried. **Open.** The count-only shape and bounded-enum keys are proposed; the user confirms the exact set before implementation. | Phase 23 |
| DG-25 | v1.3 G12: the restoration verification rule, selected against the task 24.1 `jellyfin-expert` spike. **Open.** Stay content-SHA-exact, or adopt a documented content-derived equivalence rule. This is a Task Conformance Precondition, so the spike precedes the predicate. | Phase 24 |

## Risks and Mitigations

| Risk | Impact | Mitigation / trigger |
| --- | --- | --- |
| Jellyfin 12 artwork ABI differs from assumptions. | Publication or restoration fails, or standard image delivery is disrupted. | Complete the supported item-image publication spike early and pin the ABI; leave current artwork unchanged on failure. |
| Provider responses vary by version or omit technical fields. | Incorrect or unstable badges. | Defensive mapping, explicit unknown states, capability tracking, and contract tests across declared versions. |
| Jellyfin and Arr identities cannot be proven equivalent. | Badges appear on the wrong item. | Provider IDs first, scoped IDs, no V1 path fallback, and no automatic badge for ambiguity. |
| Provider outages or slow requests affect Jellyfin. | Library scans or image requests degrade. | Asynchronous bounded work, finite timeouts, cancellation, stale policy, and current-artwork preservation. |
| Rendered output becomes stale after metadata or artwork changes. | Users see outdated badges. | Include all output-affecting inputs in fingerprints and invalidate only after atomic state publication. |
| Generated artwork is published incorrectly. | Original artwork is lost or Enhanced behavior is disrupted. | Require source provenance, guarded restoration, supported item-image APIs, and tests for manual image changes. |
| Cache/state corruption survives restart. | Repeated failures or unavailable badges. | Versioned records, atomic writes, integrity checks, quarantine/discard, and rebuild tests. |
| Webhook payloads trigger unbounded or unauthorized work. | Security or resource exhaustion. | Shared-secret authentication, bounded payloads, rate/coalescing limits, and re-read current provider state. |
| Enhanced and ArrTags show overlapping information. | Confusing or duplicated client presentation. | No automatic duplicate/overlap suppression and no Enhanced-internals dependency (ADR-011); user control through the existing poster and selector enable flags, with coexistence tests covering quality tags and spoiler behavior. |
| v1.1: the get-only `Collection<T>` round-trip drops settings. | Saved library scope or selectors are silently lost on a save. | Task 9.1 is a blocking prerequisite; if the round-trip fails, the configuration shape is corrected and the change recorded before the page is built. |
| v1.1: the settings page or save path exposes a secret or bypasses elevation. | Credential exposure or an unsupported save path. | ADR-016 mandates the elevation-gated `PluginsController` path with no custom route, a secret-free page, and a `security-reviewer` review of the page and save path (tasks 9.2, 9.3, 14.4). |
| v1.1: logging creates a secret-exposure path. | A secret appears in host logs. | ADR-020's redaction contract, per-level redaction tests, bounded volume, and a dedicated logging security review (tasks 10.2, 10.3). |
| v1.1: the output-affecting renderer changes are versioned inconsistently. | Stale artwork or a broken fingerprint/golden oracle. | Goals B and E are one phase with a single coordinated schema/`RenderVersion` advance and one golden regeneration (task 12.4), with fail-closed goldens and no auto-approval path. |
| v1.1: the inventory cache serves stale metadata as current. | Incorrect badges after a provider change. | Bounded TTL and ArrTags-side invalidation from the complete trigger set; the cache stays non-authoritative with bounded last-known-good (ADR-018, tasks 11.1-11.3). |
| v1.2: the log subject exposes a directory path or a sensitive file name. | A filesystem layout or title leaks into the host log. | ADR-026 emits only the bounded file-name component, with an item-id fallback, never a directory; the component is taken on both separators so a Windows-style or UNC path cannot emit a directory chain, and the non-printing scalar categories are stripped; redaction tests at every verbosity plus a fresh logging security review (tasks 15.1-15.4, 21.11). |
| v1.2: the diagnostics endpoint leaks a secret or an unbounded payload. | Credential or item exposure, or a resource-exhaustion vector. | ADR-025: elevation-gated read-only endpoint, fixed bounded shape, no per-item listing, secret-free counters, and a dedicated security review (tasks 17.2 and 17.4). |
| v1.2: the F4 coverage cursor corrupts or stalls reconciliation. | Some items never reconcile, or a run repeats the same prefix. | ADR-022's bounded `Cache`-authority `(SortName, itemId)` cursor with a scope reset and a start fallback on a missing/torn/out-of-range record, the bounded enqueue-outcome vocabulary that does not stall on coalesce/in-flight and stops at the first overflow; coverage tests (tasks 19.1 and 19.2). |
| v1.2: the F6 re-enqueue loops, is coalesced away, or duplicates work. | Queue churn, duplicated rendering, or the post-save re-render still being dropped. | ADR-023's `ConfigurationStale` discard classification, the re-enqueue after the in-flight slot is released, the current-version bound, and the unchanged version-blind key and per-surface single-flight; coalescing coverage tests (tasks 19.3 and 19.4). |
| v1.2: the F7 restore removes correct artwork on a transient failure. | Artwork is lost when it should be preserved. | ADR-024 restores/removes only for an owned empty resolved selection (`NoDisplayableValue`) and preserves the current artwork for every other pass-through reason, including a non-empty layout failure (`NoFittingBadge`); the internal `ArtworkPublisher.RestoreAsync` entry point and the guarded crash-recoverable protocol with coverage tests (tasks 20.1 and 20.2). |
| v1.2: the Extra Large change unexpectedly alters existing output. | Stale goldens or an unnecessary full-library re-render. | ADR-027 requires existing goldens to remain byte-identical and records the no-`RenderVersion`-advance decision; a changed existing golden reopens the decision (tasks 18.1 and 18.2). |
| v1.2: a provider `3xx` re-sends `X-Api-Key` to the `Location` origin. | The API key leaves the configured connection, contrary to ADR-005's "current request only". | Reproduced by the 21.5 review (SEC-21.5-01, MEDIUM). The user chose to fix rather than accept it: all four named provider clients stop following redirects, the insecure-TLS opt-in does not re-enable them, and a transport test proves the key never reaches the redirect target (task 21.7); the artifact, live matrix, and release security review are re-derived against the corrected build (tasks 21.8-21.10). |
| v1.2: the bounded log subject carries a non-printing scalar or a directory chain into the operator log. | A crafted or Windows-style file name reaches the log verbatim, disclosing a user account name and full path, or an invisible/format scalar defeats operator correlation. | Reproduced by the 21.10 review as SEC-21.5-02 and SEC-21.5-03 (both LOW, open). The user chose to fix rather than accept: 21.11 strips the non-printing scalar categories and takes the final component on both `/` and `\` independently of host platform, with tests that fail on the pre-fix code; the artifact, live matrix, and release security review are re-derived against that build (tasks 21.12-21.14). |
| v1.3: a cancelled or bounded-retryable image mutation leaves the durable record in a state no recovery path will pick up. | A poster is permanently stranded with a badge that cannot be removed, recoverable only by hand-editing the authoritative state tree. | Reproduced live on 2026-09-24 (limitation F9). ADR-030 keeps cancellation non-terminal and resumable under ADR-003, preserves the mutation lower bound, reserves `RestoreBlocked` for terminal outcomes, and adds an elevation-gated in-product re-baseline action; regression tests must fail against the pre-fix code and the live matrix interrupts a restore with a host restart (tasks 22.1-22.4, 25.3). |
| v1.3: treating every host-call failure as resumable retries a permanently impossible mutation. | Queue churn and repeated failed image writes on every trigger. | DG-23 fixes the outcome set; only an outcome that establishes the subject cannot be recovered may write a terminal phase, and the retry is bounded by the existing retry policy and per-item single-flight (task 22.1). |
| v1.3: the observability change widens a redaction or unbounded-payload surface. | A secret, path, or item name reaches the log or the diagnostics endpoint, or an unbounded collection is exposed. | ADR-031 carries bounded enum classifications and counts only, never values, names, or paths; the ADR-020 clause 6 throttle still bounds emission; the snapshot stays fixed-shape; the endpoint is security-reviewed before the gate (tasks 23.1-23.5). |
| v1.3: the in-product re-baseline action re-baselines over an image a human set deliberately. | ArrTags captures an operator-chosen poster as its new baseline and badges over it. | The action is explicit, elevation-gated, single-item, and refuses while a non-terminal durable operation exists; it is a state-session action and never writes an image, so ADR-002's fail-closed ownership rule is unchanged (task 22.3). |
| v1.3: the restoration verification predicate is derived from an unverified host assumption. | A visually correct restore is sealed as blocked, or a genuinely different image is accepted as a match. | DG-25 is gated on the task 24.1 spike as a Task Conformance Precondition; any adopted rule is content-derived and bounded, and the fail-closed outcomes are unchanged and tested (tasks 24.1-24.3). |
| v1.3 (Phase 26): a new harness guard is scoped too widely and fails on immutable history. | `check-agents.sh` fails on Phases 20-25, which may not be rewritten, and blocks the phase. | The reports-array guard is scoped from phase 20 (the only unlisted attempts on disk are tasks 7.1, 7.8, 15.2, and 15.4, all below phase 20) and the aggregate guard from phase 26 with phases 20-25 explicitly grandfathered; each guard carries a recorded sensitivity probe (tasks 26.2, 26.3). |
| v1.3 (Phase 26): harness work expands into product or scope work. | Product behavior changes inside an internal phase and the `1.3.0.0` artifact identity is affected. | Every Phase 26 deliverable is a documentation, script, or prompt file; product code, tests, artifacts, and `manifest.json` are out of scope, the phase has no goal, gate, or ADR, and the artifact identity is stated as unchanged (tasks 26.1-26.6). |

## Post-V1 Backlog

Forward-looking capability work that is not part of any accepted scope. Open,
accepted, and resolved limitations are tracked with evidence in
`docs/limitations/00-index.md`; this list is only the not-yet-scheduled work.

- Additional normalized badge metadata already identified in the data model (bit
  depth, frame rate, scan type, language, subtitles, release group, edition,
  custom-format score, certification, stream count, provider extensions) once
  reliable source semantics and configuration are defined.
- Further badge definitions and visual primitives without coupling them to provider
  DTOs or changing the render pipeline contract.
- Additional item/image surfaces only after an explicit aggregation and eligibility
  policy; do not infer aggregate quality from one child file.
- Expanded Sonarr/Radarr version range after compatibility tests and capability
  rules are available.
- Another metadata service only if a later scope decision requires it and the
  provider-neutral boundary remains valid.
- Configured, connection-scoped Jellyfin-to-Arr path fallback only through a new
  architecture decision defining its namespaces, normalization, ambiguity, and
  location-safety contract (ADR-008).
