# ArrTags Execution Plan

## Current State

Current project status lives in one place: [`docs/status.md`](docs/status.md),
rendered from [`docs/plan/state.json`](docs/plan/state.json). The milestone
table below is the per-phase scheduling view for the active scope; completed
phases are archived in [`docs/plan/archive/`](docs/plan/archive/).

## Active Planning Scope

<!-- BEGIN GENERATED: active-scope -->
**Active plan:** `docs/planning/v1.2.md`.
<!-- END GENERATED: active-scope -->

**v1.2 is accepted.** Its scope, goals, decision gates, dependency order, task
outline, and verification requirements are defined in
[`docs/planning/v1.2.md`](docs/planning/v1.2.md). v1.2 is Phases 15-21 (version
`1.2.0.0`, release tag `v1.2.0`, phase tags `v1.2.0-phase<N>`); the v1.1 release
(Phases 9-14) is complete and archived. The v1.2 decision records ADR-022 through
ADR-028 are **accepted** (`Accepted (v1.2)`); decision gates DG-19, DG-20, and
DG-21 (mechanisms for F4, F6, and F7) are resolved by the accepted plan and their
ADRs, so their dependent tasks are no longer gated. F3, F4, F6, F7, and F8 are
promoted by v1.2 and remain **Open** until their resolving phase records the
resolution; F5 is accepted as a known shipped limitation (not Open) and is not
promoted.

The orchestrator determines the active scope from this pointer. When a new scope
is accepted, its plan is placed under `docs/planning/`, this pointer is updated
to name it, and the implementation-planner derives its phases into this file
using the existing conventions. Phases continue the global numbering, and prior
phases are never renumbered or reopened; on completion a release's phases are
moved verbatim to [`docs/plan/archive/`](docs/plan/archive/).

## How To Use This Plan

- Keep milestone order unchanged. A milestone may be worked on only after its
  predecessor's gate is met, unless a task is explicitly marked as research or
  a test spike.
- Task numbers are stable identifiers only, not an execution sequence. Each
  phase's **Authoritative Phase X execution order** is the canonical sequence:
  walk it from the first entry and select the first task that is not complete,
  after verifying that task's documented prerequisites. When the execution order
  reorders task numbers, the execution order wins.
- Update task checkboxes and `docs/plan/state.json` as work lands; do not mark a
  milestone complete until every acceptance criterion is verified. `docs/status.md`
  and the milestone table are generated from `state.json` (see
  `docs/plan/README.md`).
- Record decisions that change an architecture assumption as a new ADR under
  `docs/decisions/`, then update `docs/architecture/00-index.md` or
  `docs/data-model/00-index.md` before implementation relies on them.
- Keep provider DTOs, Jellyfin entities, credentials, and implementation
  details at their boundaries. The matching, metadata, rendering, and cache
  pipeline consumes the canonical models.
- Every milestone must leave Jellyfin safe when the provider, cache, matcher, or
  renderer fails.

## Active Phases (v1.2)

**Scope:** The accepted v1.2 scope is [`docs/planning/v1.2.md`](docs/planning/v1.2.md)
(Goals G6-G9 and limitations F3, F4, F6, F7, F8; decision gates DG-15..DG-22;
dependency order section 5; task outline section 6; documentation checklist
section 7; verification requirements section 9). Its decision records
ADR-022..ADR-028 are **accepted** (`Accepted (v1.2)`); DG-19, DG-20, and DG-21
(the F4, F6, and F7 mechanisms) are resolved by the accepted plan and their
ADRs, so their dependent tasks are no longer gated. F5 is accepted as a known
shipped limitation and excluded. Phases 1-14 are complete and are not
restructured, reordered, or reopened; v1.2 is additive, starting at Phase 15.

**Phase set and ordering.** The phases follow the v1.2 dependency order: logging
first (cross-cutting; amends a security contract), then the settings byte-unit and
restart-required work, then the F3 diagnostics surface that extends the same page,
then the output-affecting Extra Large size, then the F4/F6 reconciliation work,
then the F7 artwork pass-through change, and the v1.2 release last. A phase is
finalized only after its `phase-reviewer` gate passes and the phase tag
`v1.2.0-phase<N>` is created; phases do not auto-advance.

**v1.2 phase map and task-outline mapping.** The flat task outline in
[`docs/planning/v1.2.md`](docs/planning/v1.2.md) section 6 maps as follows:
V1.2-1 (planning) is complete with this package; V1.2-2 -> 15.1-15.4; V1.2-3 ->
16.1-16.6; V1.2-4 -> 17.1-17.4; V1.2-5 -> 18.1-18.3; V1.2-6 -> 19.1-19.5;
V1.2-7 -> 20.1-20.3; V1.2-8 -> 21.1-21.6.

### 15. Plugin log subject identifier and render classification

**Goal:** G9 and F8 (ADR-026; ADR-020 clause 4 as amended).
**Objective:** Replace the Jellyfin item id in log output with a bounded file
name (item-id fallback when no path is available) and emit the bounded render
classification, under an amended ADR-020 clause 4, with a fresh security review.
**Deliverables:** a bounded log-subject policy/helper; threading through the
identity-bearing sites and explicit fallbacks at the sites without an identity;
the render classification on the artwork log line; the ADR-020 amendment and the
logging security review.
**Tasks:**
- [x] 15.1 Bounded log-subject policy and helper
- [x] 15.2 Thread the bounded log subject through the identity-bearing and fallback sites
- [ ] 15.3 Emit the bounded render classification in the artwork log
- [ ] 15.4 Logging documentation, ADR-020 amendment, and security review
**Authoritative Phase 15 execution order:** 15.1, 15.2, 15.3, 15.4
**Phase acceptance criteria:** the file-name-only subject (bounded, control
characters stripped, item-id fallback) is emitted where a path is available and
the item id where it is not, including the mixed `MetadataReconciliationProcessor`
discard site that falls back to the item id when no path exists; no directory or
secret appears in any log output; the bounded classification is emitted; ADR-020
clause 4 is amended (adding the subject and the explicit directory/full-path
exclusion) and the log path is security-reviewed.
**Gate 15:** Met when tasks 15.1-15.4 meet their acceptance criteria, the logging
security review is recorded with no open BLOCKER/HIGH, the phase review is
approved, and the tag `v1.2.0-phase15` is created.

### 16. Settings byte units and restart-required surfacing

**Goal:** G6 and G8 (ADR-028).
**Objective:** Expose byte-denominated limits in a fixed binary unit per field
(MB, with the webhook payload limit in KB) with exact byte round-trip at the
64 KiB minima as well as the defaults and maxima, and add restart-required note
text plus a modal reminder on change for settings that only take effect after a
host restart.
**Deliverables:** a bytes<->unit conversion helper with per-field MB/KB mapping
and unit-labelled validation ranges; the authoritative, code-evidenced
restart-required set; restart-required note text on the settings page; the modal
reminder (subject to the `jellyfin-expert` spike) with note text as the fail-open
behavior; ADR-028 and the canonical documentation.
**Tasks:**
- [ ] 16.1 Byte/unit conversion helper and per-field MB/KB mapping (explicit off-step rejection)
- [ ] 16.2 Authoritative restart-required set with code evidence
- [ ] 16.3 Restart-required note text on the settings page
- [ ] 16.4 Modal-on-change research spike (`jellyfin-expert`)
- [ ] 16.5 Modal-on-change implementation (fail-open to note text)
- [ ] 16.6 G6/G8 documentation and integration verification
**Authoritative Phase 16 execution order:** 16.1, 16.2, 16.3, 16.4, 16.5, 16.6
**Phase acceptance criteria:** byte limits are displayed in a fixed binary unit
per field with exact min/default/max round-trip (including the 64 KiB minima) and
the persisted representation stays bytes; a value off its field's fixed step is
rejected by an explicit step-multiple check (task 16.1), since the existing range
validation does not enforce step alignment; the restart-required set is
code-owned, evidenced, and testable with mixed settings classified
restart-required; every restart-required setting carries note text; the modal
fires on change when available and falls back to the note text otherwise.
**Gate 16:** Met when tasks 16.1-16.6 meet their acceptance criteria, the modal
spike has recorded the API decision, the phase review is approved, and the tag
`v1.2.0-phase16` is created.

### 17. Read-only diagnostics status surface

**Goal:** F3 (ADR-025).
**Objective:** Add a read-only status panel to the existing ArrTags settings page,
backed by a new administrator-authenticated plugin endpoint exposing bounded,
secret-free counters.
**Deliverables:** a bounded diagnostics snapshot model derived from existing
bounded sources; an elevation-gated read-only endpoint; the read-only settings
panel; ADR-025, canonical documentation, and a security review of the endpoint
and panel.
**Tasks:**
- [ ] 17.1 Bounded diagnostics metrics model and instrumentation
- [ ] 17.2 Administrator-authenticated status endpoint
- [ ] 17.3 Read-only status panel on the settings page
- [ ] 17.4 F3 documentation and security review
**Authoritative Phase 17 execution order:** 17.1, 17.2, 17.3, 17.4
**Phase acceptance criteria:** the endpoint is read-only, requires elevation, and
returns a fixed counter set (queue depth/in-flight, provider health, matching
failures, cache hits/misses, render failures, stale metadata) with no secret,
path, item name, provider payload, or unbounded collection; the panel is served
from the anonymous static page resource but renders no data when the endpoint is
not authorized (fail-closed); F3 is recorded as resolved.
**Gate 17:** Met when tasks 17.1-17.4 meet their acceptance criteria, the endpoint
security review is recorded with no open BLOCKER/HIGH, the phase review is
approved, and the tag `v1.2.0-phase17` is created. Resolves F3.

### 18. Extra Large badge size

**Goal:** G7 (ADR-027).
**Objective:** Add `BadgeSize.ExtraLarge` (factor 2.0), extend the
`BadgeGeometry.SizeFactor` switch and the `RendererConfiguration` validation
coverage for the new value, order the settings dropdown
Small/Medium/Large/Extra Large, and record and verify the `RenderVersion`
decision with golden coverage.
**Deliverables:** the Extra Large enum member and geometry factor with safe-area
clamping; the dropdown ordering; the ADR-027 version decision (no advance,
existing goldens byte-identical, new Extra Large goldens); documentation.
**Tasks:**
- [ ] 18.1 Extra Large enum, geometry factor, and settings dropdown ordering
- [ ] 18.2 Golden coverage and version-decision verification
- [ ] 18.3 G7 documentation and integration verification
**Authoritative Phase 18 execution order:** 18.1, 18.2, 18.3
**Phase acceptance criteria:** Extra Large renders at 2.0x Medium and never
paints outside the safe area; the dropdown order is Small, Medium, Large, Extra
Large; existing goldens are byte-identical and the version decision is recorded;
new Extra Large goldens are deterministic and the fingerprint includes the
non-default size.
**Gate 18:** Met when tasks 18.1-18.3 meet their acceptance criteria, the golden
oracle verifies the version decision, the phase review is approved, and the tag
`v1.2.0-phase18` is created.

### 19. Reconciliation coverage and version-aware coalescing

**Goal:** F4 and F6 (ADR-022; ADR-023). **Decision gates:** DG-19 and DG-20 are
resolved by the accepted ADRs, so the Phase 19 implementation tasks are no longer
gated.
**Objective:** Guarantee successive-run coverage of a scope larger than
`QueueCapacity` and stop a post-save re-render from being dropped by version-blind
coalescing.
**Deliverables:** the accepted F4 mechanism (ADR-022, Accepted (v1.2): a persisted
`(SortName, itemId)` cursor plus a bounded enqueue-outcome vocabulary and
resume-capable enumerator) with a bounded `Cache`-authority state record and
scope-reset rule; coverage tests including the coalesce/in-flight/overflow and
library-mutation cases; the accepted F6 mechanism (ADR-023, Accepted (v1.2): a
`ConfigurationStale` discard classification with a bounded re-enqueue after the
in-flight slot is released); coalescing coverage tests; ADR-022/ADR-023 and the
architecture documentation.
**Tasks:**
- [ ] 19.1 F4 mechanism confirmation and persisted-cursor implementation
- [ ] 19.2 F4 successive-run coverage tests
- [ ] 19.3 F6 mechanism confirmation and stale-basis re-enqueue implementation
- [ ] 19.4 F6 coalescing coverage tests
- [ ] 19.5 F4/F6 documentation and integration verification
**Authoritative Phase 19 execution order:** 19.1, 19.2, 19.3, 19.4, 19.5
**Phase acceptance criteria:** successive scheduled/post-scan runs cover a scope
larger than `QueueCapacity` (round-robin) with one run still bounded; a coalesced
or in-flight item does not stall the run and the run stops at the first overflow;
the cursor is a bounded, secret-free `(SortName, itemId)` `Cache` record that
resets on scope change and is stable under library mutation; event/webhook/
per-item/post-save triggers are unchanged; a `ConfigurationStale` discard
re-enqueues one current-version work item after the in-flight slot is released,
bounded per (item, surface, version), while a non-version discard does not
re-enqueue and the version-blind key and single-flight are unchanged.
**Gate 19:** Met when tasks 19.1-19.5 meet their acceptance criteria, the F4 and
F6 coverage tests pass, the phase review is approved, and the tag
`v1.2.0-phase19` is created. Resolves F4 and F6.

### 20. Empty-selection artwork restoration

**Goal:** F7 (ADR-024). **Decision gate:** DG-21 is resolved by the accepted ADR,
so the Phase 20 implementation task is no longer gated.
**Objective:** Restore the retained source baseline (or remove the ArrTags image
when no baseline existed) when an owned session resolves an **empty resolved
selection**, while preserving the current artwork for the other pass-through
reasons, including a non-empty selection that cannot fit (`NoFittingBadge`).
**Deliverables:** the empty-selection restore/removal behavior through the guarded
lifecycle protocol and the internal `ArtworkPublisher.RestoreAsync` entry point; a
distinct `NoFittingBadge` pass-through reason for a non-empty layout-empty render;
unchanged preservation for the other pass-through reasons; ADR-024 plus ADR-009
and ADR-003 amendment notes; coverage tests.
**Tasks:**
- [ ] 20.1 Empty-selection restore/removal behavior
- [ ] 20.2 F7 coverage tests
- [ ] 20.3 F7 documentation and integration verification
**Authoritative Phase 20 execution order:** 20.1, 20.2, 20.3
**Phase acceptance criteria:** an owned empty selection restores the baseline, or
removes the ArrTags image when the baseline was absent, through the
crash-recoverable protocol and the internal `ArtworkPublisher.RestoreAsync` entry
point (resumed on restart by the ordinary recovery gate); the other pass-through
reasons preserve the current artwork and mutate nothing, including a non-empty
selection that cannot fit; a non-owned empty selection stays a pass-through; the
absent-baseline removal is exercised by a direct publisher test with a synthetic
persisted `Published` state without a baseline; coverage tests pass and F7 is
recorded as resolved.
**Gate 20:** Met when tasks 20.1-20.3 meet their acceptance criteria, the coverage
tests pass, the phase review is approved, and the tag `v1.2.0-phase20` is created.
Resolves F7.

### 21. v1.2 release

**Goal:** v1.2 release (outline task V1.2-8).
**Objective:** Bump ArrTags to `1.2.0.0`, reconcile the canonical documentation,
build and package reproducibly, run the full suite and the live pinned-host
verification, run the release security review, and complete the changelog,
release-readiness verification, manifest commit, and the annotated `v1.2.0` tag.
**Deliverables:** version `1.2.0.0` in `build.yaml` and `Directory.Build.props`;
the reconciled canonical documentation; a reproducible
`artifacts/ArrTags_1.2.0.0.zip`; the full default and host-guarded suites passing;
the recorded live pinned-host verification; the release security review; the
changelog, `manifest.json`, and the annotated tag after the release-review gate;
the GitHub publish remains the user's manual step.
**Tasks:**
- [ ] 21.1 Version bump and release metadata
- [ ] 21.2 Canonical documentation reconciliation
- [ ] 21.3 Release build, full suite, reproducible artifact, and release documentation
- [ ] 21.4 Live pinned-host verification
- [ ] 21.5 Release security review
- [ ] 21.6 Changelog, release-readiness verification, manifest commit, and the annotated v1.2.0 tag
**Authoritative Phase 21 execution order:** 21.1, 21.2, 21.3, 21.4, 21.5, 21.6
**Phase acceptance criteria:** the version is `1.2.0.0`; the canonical
documentation reflects v1.2 with no stale claim; `./build.sh package` produces a
reproducible `artifacts/ArrTags_1.2.0.0.zip` with recorded identity; the full
default and host-guarded suites and the live pinned-host matrix pass; the release
security review has no open BLOCKER/HIGH; the annotated tag `v1.2.0` is created
after the release-review gate.
**Gate 21:** Met when tasks 21.1-21.6 meet their acceptance criteria, the full
suite and live matrix pass, the release security review has no open
BLOCKER/HIGH, the phase and release reviews pass, and the annotated tag `v1.2.0`
is created.

## Milestone Status

<!-- BEGIN GENERATED: milestone-status -->
| # | Milestone | Status | Exit gate |
| --- | --- | --- | --- |
| 15 | Plugin log subject identifier and render classification | PLANNED | Gate 15 |
| 16 | Settings byte units and restart-required surfacing | PLANNED | Gate 16 |
| 17 | Read-only diagnostics status surface | PLANNED | Gate 17 |
| 18 | Extra Large badge size | PLANNED | Gate 18 |
| 19 | Reconciliation coverage and version-aware coalescing | PLANNED | Gate 19 |
| 20 | Empty-selection artwork restoration | PLANNED | Gate 20 |
| 21 | v1.2 release | PLANNED | Gate 21 |
<!-- END GENERATED: milestone-status -->

Completed milestones and their gates are recorded in
[`docs/plan/archive/`](docs/plan/archive/) and in `docs/plan/state.json`.

## Completed Milestones

All milestones through Phase 14 (v1.1) are complete and archived verbatim:

- V1 (Phases 1-8): [`docs/plan/archive/v1.md`](docs/plan/archive/v1.md)
- v1.1 (Phases 9-14): [`docs/plan/archive/v1.1.md`](docs/plan/archive/v1.1.md)
- v1.1 scope plan: [`docs/plan/archive/v1.1-plan.md`](docs/plan/archive/v1.1-plan.md)
- V1 readiness history: [`docs/plan/archive/v1-readiness.md`](docs/plan/archive/v1-readiness.md)

Machine state: [`docs/plan/state.json`](docs/plan/state.json).

## Decision Gates

These decisions must be resolved and recorded before the dependent work becomes
an implementation assumption.

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
| v1.2: the log subject exposes a directory path or a sensitive file name. | A filesystem layout or title leaks into the host log. | ADR-026 emits only the bounded file-name component with an item-id fallback, never a directory; redaction tests at every verbosity plus a fresh logging security review (tasks 15.1-15.4). |
| v1.2: the diagnostics endpoint leaks a secret or an unbounded payload. | Credential or item exposure, or a resource-exhaustion vector. | ADR-025: elevation-gated read-only endpoint, fixed bounded shape, no per-item listing, secret-free counters, and a dedicated security review (tasks 17.2 and 17.4). |
| v1.2: the F4 coverage cursor corrupts or stalls reconciliation. | Some items never reconcile, or a run repeats the same prefix. | ADR-022's bounded `Cache`-authority `(SortName, itemId)` cursor with a scope reset and a start fallback on a missing/torn/out-of-range record, the bounded enqueue-outcome vocabulary that does not stall on coalesce/in-flight and stops at the first overflow; coverage tests (tasks 19.1 and 19.2). |
| v1.2: the F6 re-enqueue loops, is coalesced away, or duplicates work. | Queue churn, duplicated rendering, or the post-save re-render still being dropped. | ADR-023's `ConfigurationStale` discard classification, the re-enqueue after the in-flight slot is released, the current-version bound, and the unchanged version-blind key and per-surface single-flight; coalescing coverage tests (tasks 19.3 and 19.4). |
| v1.2: the F7 restore removes correct artwork on a transient failure. | Artwork is lost when it should be preserved. | ADR-024 restores/removes only for an owned empty resolved selection (`NoDisplayableValue`) and preserves the current artwork for every other pass-through reason, including a non-empty layout failure (`NoFittingBadge`); the internal `ArtworkPublisher.RestoreAsync` entry point and the guarded crash-recoverable protocol with coverage tests (tasks 20.1 and 20.2). |
| v1.2: the Extra Large change unexpectedly alters existing output. | Stale goldens or an unnecessary full-library re-render. | ADR-027 requires existing goldens to remain byte-identical and records the no-`RenderVersion`-advance decision; a changed existing golden reopens the decision (tasks 18.1 and 18.2). |

## Post-V1 Backlog

Forward-looking capability work that is not part of any accepted scope. Open,
accepted, and resolved limitations are tracked with evidence in
`docs/limitations/00-index.md`; this list is only the not-yet-scheduled work.

- Additional normalized badge metadata already identified in the data model
  (bit depth, frame rate, scan type, language, subtitles, release group, edition,
  custom-format score, certification, stream count, provider extensions) once
  reliable source semantics and configuration are defined.
- Further badge definitions and visual primitives without coupling them to
  provider DTOs or changing the render pipeline contract.
- Additional item/image surfaces only after an explicit aggregation and
  eligibility policy; do not infer aggregate quality from one child file.
- Expanded Sonarr/Radarr version range after compatibility tests and capability
  rules are available.
- Another metadata service only if a later scope decision requires it and the
  provider-neutral boundary remains valid.
- Configured, connection-scoped Jellyfin-to-Arr path fallback only through a new
  architecture decision defining its namespaces, normalization, ambiguity, and
  location-safety contract (ADR-008).
