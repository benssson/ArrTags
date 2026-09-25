# ArrTags Execution Plan

## Current State

Current project status lives in one place: [`docs/status.md`](docs/status.md),
rendered from [`docs/plan/state.json`](docs/plan/state.json). The milestone
table below is the per-phase scheduling view for the active scope; completed
phases are archived in [`docs/plan/archive/`](docs/plan/archive/).

## Active Planning Scope

**No release scope is currently accepted for planning.** The v1.1 release
(Phases 9-14) is complete and archived, and `docs/planning/` is empty. The v1.2
branch exists with the F6-F8 limitations recorded, but no accepted v1.2 plan is
present, so no phase is scheduled and the orchestrator must not select work.

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
- Update task checkboxes and the status table as work lands; do not mark a
  milestone complete until every acceptance criterion is verified.
- Record decisions that change an architecture assumption in
  `docs/decisions/00-index.md`, then update `docs/architecture/00-index.md` or
  `docs/data-model/00-index.md` before implementation relies on them.
- Keep provider DTOs, Jellyfin entities, credentials, and implementation
  details at their boundaries. The matching, metadata, rendering, and cache
  pipeline consumes the canonical models.
- Every milestone must leave Jellyfin safe when the provider, cache, matcher, or
  renderer fails.

## Milestone Status

<!-- BEGIN GENERATED: milestone-status -->
| # | Milestone | Status | Exit gate |
| --- | --- | --- | --- |
| — | No active milestone | — | No release scope is currently accepted for planning. |
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
