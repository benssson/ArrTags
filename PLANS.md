# ArrTags Execution Plan

## Current State

Current project status lives in one place: [`docs/status.md`](docs/status.md), rendered from
[`docs/plan/state.json`](docs/plan/state.json). The milestone table below is the per-phase
scheduling view for the active scope; completed phases are archived in
[`docs/plan/archive/`](docs/plan/archive/).

## Active Planning Scope

<!-- BEGIN GENERATED: active-scope -->
**Active plan:** none.
<!-- END GENERATED: active-scope -->

**No scope is currently accepted for planning.** v1.2 (Phases 15-21, version `1.2.0.0`, release tag
`v1.2.0`) is complete and archived: the phase text is in
[`docs/plan/archive/v1.2.md`](docs/plan/archive/v1.2.md) and its accepted scope plan is in
[`docs/plan/archive/v1.2-plan.md`](docs/plan/archive/v1.2-plan.md). The v1.2 decision records ADR-022
through ADR-029 are **accepted** (`Accepted (v1.2)`); decision gates DG-15 through DG-22 are resolved. F3,
F4, F6, F7, and F8 were promoted by v1.2 and are resolved by their phases (F8 by Phase 15, F3 by Phase 17,
F4 and F6 by Phase 19, F7 by Phase 20); F5 is accepted as a known shipped limitation (not Open) and is not
promoted.

The orchestrator determines the active scope from this pointer. When a new scope is accepted, its plan is
placed under `docs/planning/`, this pointer is updated to name it, and the implementation-planner derives its
phases into this file using the existing conventions. Phases continue the global numbering, and prior phases
are never renumbered or reopened; on completion a release's phases are moved verbatim to
[`docs/plan/archive/`](docs/plan/archive/).

## How To Use This Plan

- Keep milestone order unchanged. A milestone may be worked on only after its predecessor's gate is met,
  unless a task is explicitly marked as research or a test spike.
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

None: no release scope is currently accepted for planning, so this file carries no active phase
sections. The most recent release, v1.2 (Phases 15-21), is complete; its phase text, task list, and
execution orders are preserved verbatim in
[`docs/plan/archive/v1.2.md`](docs/plan/archive/v1.2.md), and its accepted scope plan in
[`docs/plan/archive/v1.2-plan.md`](docs/plan/archive/v1.2-plan.md). The machine state for those phases
and tasks remains in [`docs/plan/state.json`](docs/plan/state.json).

## Milestone Status

<!-- BEGIN GENERATED: milestone-status -->
| # | Milestone | Status | Exit gate |
| --- | --- | --- | --- |
| — | No active milestone | — | No release scope is currently accepted for planning. |
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
