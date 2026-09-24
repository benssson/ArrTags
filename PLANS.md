# ArrTags Execution Plan

## Project Status

**Status:** Phases 1-8 complete; the release tags `v0.1.0` (`a634d61`) and
`v1.0.1` (`8cba85b`) exist locally, with the repository `manifest.json`
committed at `v1.0.1` but no GitHub release, no asset upload, and no push yet.
Phase 8 - Release distribution is **complete** (tasks 8.1-8.6 complete, all seven
Phase 8 acceptance criteria met, Gate 8 met - Phase 8 review approved in
`docs/implementation/phase-8/phase-review.json`): ArrTags
is prepared to be installable through the standard Jellyfin plugin catalog from
the public repository `benssson/ArrTags` once the user pushes the manifest and
publishes the GitHub release; `README.md` is the end-user guide, stale
user-visible metadata is corrected, and the `v1.0.1` release (plugin version
`1.0.1.0`, git tag `v1.0.1`, commit `8cba85b`) is prepared. The commit, tag, and
manifest are local only; the actual GitHub release, asset upload, and manifest
push remain a manual user step with `scripts/publish-release.sh`. No functional
plugin behavior changes in Phase 8 beyond version/release metadata. Phase 7 record: tasks 7.1, 7.2, 7.3, 7.4, 7.5,
7.6, 7.7, and 7.8 are complete; all five Phase 7 acceptance criteria are met; Gate 7 is met (Phase 7 review approved; tag `v0.1.0-phase7`); the task 7.2 live verification on the pinned Jellyfin `12.0.0` musl host found a release-blocking defect in the standard versioned install layout - once the plugin had persisted state under its Jellyfin-derived data folder `PluginsPath/ArrTags`, the next host restart treated that data folder and the versioned install folder `PluginsPath/ArrTags_<version>` as two versions of the same-named plugin, deleted the install folder, and loaded no ArrTags plugin - which task 7.7 resolves by relocating the plugin state root to `ProgramDataPath/ArrTags` outside `PluginsPath` (ADR-014) and re-running the live install/upgrade/reload/uninstall verification, now passing with the install folder and state preserved, so Phase 7 acceptance criterion 2 is met; the task 7.3 GOALS verification executed live end-to-end on the pinned musl host and met success criteria 1-4 and 9, covering criteria 5-8 only under a diagnostic SkiaSharp-sharing install or at the contract level, and found release blocker 7.3-F1 - the committed package's bundled `SkiaSharp.dll`/`libSkiaSharp.so` collide fatally with the host's own SkiaSharp and abort Jellyfin during the first badge publication - so `GOALS.md` criteria 5, 6, and 8 are not met as shipped and Phase 7 acceptance criteria 3 and 4 remain unchecked; task 7.8 resolves the duplicate-SkiaSharp packaging blocker 7.3-F1 by ADR-015 (the plugin compiles against the pinned SkiaSharp but shares the host's runtime instead of bundling it) and re-runs the live end-to-end verification, which now passes, so `GOALS.md` criteria 5 and 8 are met as shipped, with criterion 6 met for render and publication but only partial for provider fetches (`docs/limitations.md` F1), and Phase 7 acceptance criteria 3 and 4 are met; task 7.4 is complete (the logs/diagnostics/HTTP-behavior/persisted-state secret-leakage and unbounded-data review on the live pinned host found no credential leakage and no unbounded path, a negative result; see the task 7.4 status); task 7.5 is complete - the release package is built from a clean checkout and is now byte-reproducible across clean builds, with the commands, inputs, artifact identity, and supported version ranges recorded in `docs/release/build-and-release.md`, so Phase 7 acceptance criterion 5 is met; task 7.6 is complete - the known limitations and deferred decisions are consolidated in `docs/limitations.md` so nothing unsupported is presented as available; see the task 7.6 status). DG-9 (supported live Sonarr/Radarr release ranges and optional-field compatibility policy) is resolved by ADR-013: supported ranges are Sonarr 3.x-4.x and Radarr 3.x-6.x on the `/api/v3` contract, absent optional fields map to explicit unknown values, and a malformed required field fails closed as `ProviderIncompatible` with no version-number gate. Phase 6 tasks 6.1 through 6.9 are complete and Gate 6 is met at the integration-test level (tag `v0.1.0-phase6`): the bounded coalescing queue and hosted workers, atomic basis-revalidated metadata-state publication, artwork-operation recovery before new work, the metadata freshness policy separated from artwork retention and bounded artifact GC, fingerprint-gated artwork regeneration with retained-source repeat publication, the ADR-012 authenticated bounded webhook boundary, the restart/outage/corruption/pressure verification matrix, and the scheduled, post-scan, and manual/periodic reconciliation triggers with provider/render concurrency enforcement. Residual Phase 6 items were left open at Phase 6 and are consolidated in `docs/limitations.md` rather than presented as solved: a provider inventory/catalogue cache (provider metadata is still fetched per work item, so Phase 6 acceptance criterion 1 is only partially met), wiring runtime configuration replacement, a safe metrics/diagnostic-status surface, and the `QueueCapacity`-bounded reconciliation prefix. Phase 7 later performed live end-to-end verification on the pinned Jellyfin `12.0.0` musl host against the committed mock Arr fixture (tasks 7.3 and 7.8). Milestone 1 (plugin foundation), Milestone 2 (Sonarr and Radarr integration), Milestone 3 (media matching, tasks 3.1 through 3.8), and Milestone 4 (badge rendering, tasks 4.1 through 4.11) are complete; Gates 1, 2, 3, and 4 are met. Phase 5 tasks 5.1 (confirm the item-image publication ABI and route variants), 5.2 (source-artwork provenance and guarded restoration state), 5.3 (the Jellyfin host source adapter), 5.4 (renderer managed/native packaging), 5.6 (the durable `ArtworkOperation` write-ahead record and store), 5.5 (publish completed artwork through Jellyfin's supported item-image APIs), 5.7 (postcondition reconciliation of uncertain publication outcomes), 5.8 (preserve the current usable artwork when source capture or rendering cannot safely complete), 5.9 (fence and drain publication operations during disable/uninstall and tombstone confirmed item removal), and 5.10 (the configured disable/limit policy for duplicate or overlapping badges, resolved by ADR-011), and 5.11 (standard server image-response integration tests for Web and other image-consuming clients) are complete; Phase 5 is complete and Gate 5 is met for the pinned 12.0.0 ABI at the integration-test level (validated in-process against the real pinned `ImageController` and the real ArrTags publication boundary; no live HTTP round-trip was performed). Phase 9 (v1.1) is complete (Gate 9 met; tag `v1.1.0-phase9`): task 9.1 (configuration round-trip spike, blocking prerequisite) is complete - the pinned elevation-gated `PluginsController` POST deserializes with `Jellyfin.Extensions.Json.JsonDefaults.Options`, which does not populate a get-only `Collection<T>`, so `PluginConfiguration.EnabledLibraries` and `RendererConfiguration.Selectors` are now settable (null-coalescing) and round-trip through the supported save path, with the persisted XML shape unchanged and existing `ArrTags.xml` files still loadable; task 9.2 (dashboard settings page and embedded page resource) is complete - `Plugin` implements `IHasWebPages` with one secret-free embedded `Configuration/config.html` page (logical name `ArrTags.Configuration.config.html`) that reads and writes the user-adjustable configuration through Jellyfin's existing administrator-gated API, with the anonymous static page-resource endpoint explicitly accepted by ADR-016 clause 6 and recorded in `docs/architecture.md` section 6, and the pinned `DashboardController` serving and authorization behavior confirmed by host-guarded tests that are skipped without `ARRTAGS_JELLYFIN_HOST_DIR`; task 9.3 (elevation-gated save path and runtime activation) is complete - `Plugin.UpdateConfiguration` validates a saved candidate before the base implementation persists it and, for a valid candidate, activates it at runtime without a host restart; an invalid candidate is rejected before persistence (it is never persisted), the last valid public snapshot and private secret generation remain active and persisted, the save sequence is serialized so concurrent saves cannot diverge, the rejection is surfaced as a bounded secret-free activity-log entry, the override never throws into the host, and it adds no custom configuration-save route; task 9.4 (bounded post-save reconciliation trigger) is complete - a successful replacement requests a bounded, non-blocking reconciliation through the plugin-owned `IConfigurationReconciliationTrigger` boundary, whose hosted `ConfigurationReconciliationTrigger` runs the existing bounded `LibraryReconciliationService` off the save thread and enqueues the same provider-neutral work hints as every other trigger, coalescing redundant requests to at most one bounded rerun, so existing posters re-render with the saved settings instead of waiting for the next library event, webhook, post-scan, or scheduled run; the trigger is never a synchronous full-library scan, never blocks the save response, and never throws into the host; task 9.5 (Goal A documentation and integration verification) is complete - the canonical documents (`docs/architecture.md` section 6, `docs/data-model.md` 3.12, `docs/limitations.md` F2, and `README.md`) describe the shipped behaviour, limitation F2 is recorded as resolved, and the new `GoalAIntegrationTests` composes the full save -> activate -> bounded-reconcile flow (pinned POST deserialization, the real `Plugin.UpdateConfiguration` override, the real snapshot service, the real post-save trigger over the bounded reconciliation service, and the real artwork publishing pipeline) without a live host; all six Phase 9 acceptance criteria (including the v1.1 Goal A acceptance criteria) are met at the integration-test level. Gate 9 is met (the Phase 9 review is approved in `docs/implementation/phase-9/phase-review.json`) and the annotated tag `v1.1.0-phase9` is created; the live Goal A confirmation remains owned by task 14.3. Phase 10 (v1.1) is complete (Gate 10 met; tag `v1.1.0-phase10`): tasks 10.1 (logging foundation, verbosity configuration, and fingerprint exclusion), 10.2 (bounded, redacted log call sites and volume bounds), and 10.3 (SEC-5 rewrite, documentation, and logging security review) are complete; all five Phase 10 acceptance criteria are met at the integration-test level; the logging security review is recorded at `docs/implementation/10.3/security-review.json` (PASS_WITH_FINDINGS, 0 open BLOCKER/HIGH/MEDIUM); Gate 10 is met (the Phase 10 review is approved in `docs/implementation/phase-10/phase-review.json`) and the annotated tag `v1.1.0-phase10` is created; the live pinned-host logging confirmation is owned by task 14.3. Phase 11 (v1.1) provider inventory cache is complete at the integration-test level (tasks 11.1-11.4): the bounded, secret-free, per-connection provider inventory cache (ADR-018) is defined with validated TTL/record/byte limits, populated and consumed at the provider-client boundary so one library read per connection serves every work item in a reconciliation window, and invalidated by the documented ArrTags-side trigger set (provider webhook, Jellyfin library refresh/post-scan, scheduled/manual and post-save reconciliation, and the bounded TTL fallback); task 11.4 reconciles `docs/architecture.md` sections 8 and 12 and `docs/data-model.md` section 6 with the shipped cache behavior, records limitation F1 as resolved, and adds the Goal C integration coverage for the one-read-per-window, invalidation-source, bounds, and last-known-good facets, so all four Phase 11 acceptance criteria are met at the integration-test level; Gate 11 is met (the Phase 11 review is approved in `docs/implementation/phase-11/phase-review.json`, APPROVED_WITH_FINDINGS with 0 open BLOCKER/HIGH/MEDIUM) and the annotated tag `v1.1.0-phase11` is created, and the live pinned-host confirmation is owned by task 14.3. Phase 12 (v1.1) badge value allowlist and badge size/position is in progress: tasks 12.1 (allowlist configuration, bounds, validation, and fingerprint), 12.2 (allowlist resolution and renderer filtering order), and 12.3 (badge size/position configuration and layout engine) are complete, and task 12.4 (coordinated schema/`RenderVersion` advance, golden regeneration, and documentation) remains, so Gate 12 is not yet met and the `v1.1.0-phase12` tag is not yet created. Task 12.3 adds the global `BadgePosition` (four corners plus center, default bottom-left) and `BadgeSize` (Small/Medium/Large, default medium) settings (ADR-019 clauses 1-5 and 7), validates them with bounded secret-free messages, carries them on the resolved `RenderOutputPolicy`, positions the technical rail per anchor with the derived status-pill placement (top-right except top-right anchor, then top-left), computes `effectiveScale = clamp(width / 1000, 0.5, 4.0) * sizeFactor` clamped so the badge still fits the safe area, and includes a non-default position or size in both the renderer configuration fingerprint and the render fingerprint while keeping the V1 default identity-neutral. `RendererConfiguration.CurrentSchemaVersion` is still 1, `RenderVersion.CurrentRendererVersion` is still 2, and no golden under `tests/ArrTags.Tests/Goldens/` was regenerated.

**Release security fix (SEC-1):** The 0.1.0 release security review found that
MVC model binding read form/multipart request bodies before the anonymous
webhook boundary authenticated, so the uniform `401` and the configured
`WebhookMaxPayloadBytes` bound did not hold for those content types. A
pre-binding authorization filter now authenticates before any body read for
every content type and rejects a non-JSON body with a bounded `400`, with
regression tests over the real MVC pipeline and a live re-verification on the
pinned Jellyfin `12.0.0` host; no provider, state, artwork, or configuration
behavior changed. The rebuilt `artifacts/ArrTags_0.1.0.0.zip` (568,244 bytes,
SHA-256 `f6b6a515c76b93926e940cebd48918f864935893b2ae7d03f59b17e38e0f9ffd`)
replaces the pre-fix `bd10b9b6…` identity; the release and security reviews are
re-run against it. See `docs/changelog.md`.

**Current position:** The goals, V1 architecture, and canonical data model are
drafted and the architectural blockers are resolved. Tasks 1.1 (documentation
alignment), 1.2 (project and test scaffold), 1.3 (canonical Sonarr identity
model), 1.4 (operational defaults and limits), 1.5 (plugin entry point and
configuration), 1.6 (DI registration and lifecycle foundation), 1.7
(versioned plugin state boundary), and 1.8 (Jellyfin 12 build, discovery, and
host validation) are complete: the
architecture is the single V1 architecture reference, the research documents are
marked as evidence, the canonical model represents Sonarr series, episode, and
current episode-file identity with a typed, connection-scoped identity, the
accepted operational defaults and validation rules are recorded in ADR-004 and
`docs/architecture.md` section 12, the configuration foundation validates
connections, limits, and scope and exposes an immutable replacement snapshot with
last-valid retention and secret redaction, the parameterless service registrator
wires the configuration snapshot, state repository, library-event boundary, and
an idle hosted lifecycle that subscribes and unsubscribes deterministically
without provider, rendering, or full-library work, the state boundary provides
versioned integrity-tagged records with atomic writes, cache discard versus
authoritative quarantine, traversal-safe paths, and bounded retention, and the
plugin builds on `net10.0` against the pinned Jellyfin `12.0.0` packages with
`targetAbi: 12.0.0.0`. Foundation tests pass without a live Arr instance and the
generated package was installed, discovered, loaded, started, restarted, and
shut down cleanly on a Jellyfin `12.0.0.0` host. Milestone 1 is complete and
Gate 1 is met. Milestone 2 is complete and Gate 2 is met: tasks 2.1 (shared
provider-client boundary and connection identity), 2.2 (dedicated
`IHttpClientFactory` client registration), 2.3 (versioned credential boundary
and read-only Radarr v3 reads), 2.4 (read-only Sonarr v3 series, episode, and
episode-file reads with the validated episode-file join), 2.5 (canonical
`ArrProvider`/`ArrConnection`/`BadgeMetadata` mapping with connection-scoped
record/file identity), and 2.6 (explicit unknown technical values and bounded
custom values), and provider failure-matrix tests (2.7) are complete. All Phase 2
tasks meet their acceptance criteria. Phase 3 media matching
tasks 3.1 (canonical `MediaIdentity` snapshots), 3.2 (provider-neutral candidate
selection, evidence recording, and deterministic `MediaMatch` fingerprints), 3.3
(zero- and multiple-candidate rejection with the match status policy), 3.4 (the
documented movie, series, and episode matching order with provider-specific
candidate assembly), 3.5 (the explicit episode-numbering policy in ADR-007), 3.6
(DG-5 resolved by ADR-008 with path fallback deferred out of V1), 3.7
(connection-scoped Arr record and file identity verification), and 3.8
(fail-closed ineligible-location rejection) are complete. All Phase 3 tasks meet
their acceptance criteria; the Milestone 3 acceptance criteria and Gate 3 are
verified.

Decision Gate DG-3 is now resolved by ADR-009. The V1 badge fields, priority,
poster layout, typography, contrast, text bounds, PNG output, scaling, and
pass-through behavior are fixed for implementation. Phase 4 is complete:
tasks 4.1 (metadata selectors), 4.2 (rendering specification), 4.3
(unknown-value semantics), 4.4 (fingerprints), 4.5 (limit enforcement), 4.7
(pinned assets and font), 4.8 (SkiaSharp host-compatibility spike), 4.9
(provider-neutral renderer service and drawing engine), 4.6 (renderer
behavior matrix), and 4.10 (renderer configuration model, snapshot, and
fingerprint) are complete. Task 4.11 (golden-image, byte-determinism,
PNG-contract, and cross-runtime tolerance tests) is complete, including the
task 4.9 F2 color-profile fail-closed supporting change and the authorized
task 4.11 fix of the EXIF dimension-swapping orientation transforms with a
renderer version bump to 2. All Milestone 4 acceptance criteria are satisfied
and Gate 4 is met: the forced native renderer run passes 655 tests with one
non-canonical cross-runtime skip (656 total), covering normal, unknown,
oversized, malformed, cancelled, and failed inputs. The only deferred Phase 4
validation is the ADR-010 non-canonical cross-runtime comparison, which requires
a second explicitly selected Linux runtime and is tracked for the
testing/release milestone.

Phase 5 (Jellyfin artwork integration) is complete. Task 5.1 is complete: the
supported Jellyfin 12.0.0 item-image publication/read ABI and the standard
`ImageController` route variants are pinned with evidence in
`docs/research/jellyfin-12-architecture.md` section 4.4, with a new unguarded ABI
test and a host-guarded route/authorization test. Task 5.2 is complete: the
provider-neutral source-artwork provenance and guarded restoration state model,
its pure logical transitions, the content-addressed authoritative source-artifact
store, and the authoritative `PublishedArtworkState` persistence are implemented
and covered by focused tests. Task 5.3 is complete: the plugin-owned Jellyfin host
source adapter reads the unindexed `Primary` image through the pinned 12.0.0 read
surface, confines accepted source containers to the PNG/JPEG profiles the renderer
inspects, derives the post-orientation display dimensions from the exact bytes,
and supplies the renderer's `SourceImageInput` plus the task 5.2
`ActiveImageIdentity` from one bounded read. Task 5.4 is complete: the plugin
package now carries the renderer's managed `SkiaSharp.dll`, the matching
`linux-x64` native `libSkiaSharp.so` and the dependency manifest at the plugin
folder root, with the `build.yaml` artifact list updated to match, and both the
live pinned host and a replicated plugin load context confirm the assets resolve
under the plugin load context. Task 5.6 is complete: the durable provider-neutral
`ArtworkOperation` write-ahead model, its phase and lifecycle-fence rules, and
its generation-fenced authoritative store are implemented and covered by focused
tests, so a complete publication/restoration intent can be persisted before any
Jellyfin image mutation. Task 5.5 is complete: the single-subject durable
publication orchestration and the single Jellyfin image-mutation implementation
are implemented in `src/ArrTags/Artwork`, driving the write-ahead ordering
through the supported stream `SaveImage` and `UpdateToRepositoryAsync(ImageUpdate)`
flow with a fail-closed revalidation and readback. Task 5.7 is complete: the
provider-neutral postcondition reconciliation service applies the data-model
3.10.4 decision table to a durable operation, resumes the deterministic
publication only under the current generation and lifecycle fence without
recapturing a source, commits the intended final state when the after identity is
observed, records ownership loss or uncertainty without mutating the image,
tombstones a confirmed item removal without an image call, and blocks or
quarantines invalid state without replay or cleanup. Task 5.8 is complete: the
single-subject generation coordinator composes the host source adapter, the
renderer, and the durable publisher and preserves the current usable artwork on
an absent source, a failed source read, a render pass-through, or a failed
render. Task 5.9 is complete: the durable active lifecycle fence, the
disable/uninstall drain and guarded restoration through the supported item-image
save/removal APIs, and confirmed item-removal tombstoning are implemented, and
the reconciler now executes a restoration resume instead of deferring it. Task
5.10 is complete: the DG-8 Jellyfin Enhanced coexistence policy is resolved by
ADR-011 (no automatic duplicate/overlap detection or suppression, no
Enhanced-internals dependency, and no special spoiler/hidden handling; the
existing poster and selector enable flags remain the user's control surface),
with coexistence tests that assert the production assembly has no Enhanced
reference, the renderer/publication surface has no spoiler/hidden/suppression
branch, and badge output varies only with the existing ArrTags configuration.
Task 5.11 is complete: the supported standard Jellyfin server image response
path is exercised in-process against the pinned host `ImageController`, covering
the unindexed and indexed `Primary` routes, the path-form route, content type
and served bytes, the image-tag `ETag` and `304` conditional response,
`immutable`/`public` cache headers, `no-cache` revalidation, requested
size/format and the never-upscale clamp, non-200 pass-through, and a
publish-then-read-back through the real ArrTags image-mutation boundary and the
standard route. A live HTTP round-trip against a running host was not performed;
the task 5.1 route tests pin the route templates and the task 5.11 facts invoke
the real pinned controller actions and response pipeline. Phase 5 is complete
and Gate 5 is met for the pinned 12.0.0 ABI at the integration-test level.

**V1 outcome:** A Jellyfin 12 plugin that independently reads Sonarr and Radarr
metadata, matches it to eligible Jellyfin media, and asynchronously publishes
configurable derived poster artwork through Jellyfin's supported image APIs
without modifying original media files or external services.

**Current constraints:**

- Jellyfin 12 is the only supported Jellyfin version.
- Sonarr and Radarr are the only external metadata providers.
- The integration is read-only.
- The canonical domain model is provider-neutral after the integration boundary.
- Missing, stale, ambiguous, or unavailable data must degrade to a safe
  unchanged-artwork result rather than affect Jellyfin operations.
- Generated images may become persisted Jellyfin artwork through the supported
  item-image APIs. Original source artwork remains recoverable through
  plugin-owned provenance state; original media files and Jellyfin's image cache
  are never modified directly.

## Active Planning Scope

The accepted scope currently being planned or executed is:

`docs/planning/v1.1.md`

The orchestrator determines the active scope from this pointer (see the
orchestrator's Active Scope Discovery rule). Update this pointer when a new
accepted scope supersedes the current one.

The accepted v1.1 scope is derived into **Phases 9-14** below. V1 Phases 1-8 are
complete and are not restructured, reordered, or reopened. The v1.1 phase set,
the V1.1-1..V1.1-8 task-outline mapping, and each phase's authoritative
execution order are recorded in "v1.1 Milestones (Phases 9-14)". Phase 9 tasks
9.1-9.5 are complete and Phase 9 is complete: all six Phase 9 acceptance
criteria (including the v1.1 Goal A acceptance criteria) are met at the
integration-test level, Gate 9 is met (the Phase 9 review is approved in
`docs/implementation/phase-9/phase-review.json`), and the annotated tag
`v1.1.0-phase9` is created; the live Goal A confirmation is owned by task 14.3.
Phase 10 is complete: tasks 10.1 (logging foundation, verbosity configuration,
and fingerprint exclusion), 10.2 (bounded, redacted log call sites and volume
bounds), and 10.3 (SEC-5 rewrite, documentation, and logging security review) are
complete; all five Phase 10 acceptance criteria are met at the integration-test
level; the logging security review is recorded at
`docs/implementation/10.3/security-review.json` (PASS_WITH_FINDINGS, 0 open
BLOCKER/HIGH/MEDIUM); Gate 10 is met (the Phase 10 review is approved in
`docs/implementation/phase-10/phase-review.json`) and the annotated tag
`v1.1.0-phase10` is created; the live pinned-host logging confirmation is owned by
task 14.3. Phase 11 (Provider inventory cache and library-refresh-driven refresh)
is complete at the integration-test level: tasks 11.1 (inventory cache model,
bounds, and limits), 11.2 (provider-client integration and bulk reads), 11.3
(ArrTags-side invalidation), and 11.4 (Goal C documentation and integration
verification) are complete. The canonical,
secret-free, in-memory per-connection cache shape and the inventory TTL and
record/byte limits are defined, validated at configuration load, and documented
in `docs/data-model.md` section 6 and `docs/architecture.md` sections 8 and 12;
the provider metadata readers now populate and consume the cache at the
provider-client boundary, so one library read per connection serves a
reconciliation window, and the Radarr `moviefile?movieId=` repeatable selector
and the Sonarr `episodeFile?episodeFileIds=` repeatable selector replace
per-item file reads. The ArrTags-side invalidation surface is now wired to every
source (provider webhook, Jellyfin library refresh/post-scan, scheduled/manual
and post-save reconciliation, and the bounded TTL fallback), so a provider change
is noticed promptly without a provider conditional request, revision token,
`history/since` watermark, or SignalR dependency, and the periodic scheduled
reconciliation still runs on its unchanged interval. Task 11.4 records limitation
F1 as resolved and adds the Goal C integration coverage, so all four Phase 11
acceptance criteria are met at the integration-test level; Gate 11 is met (the
Phase 11 review is approved in `docs/implementation/phase-11/phase-review.json`,
APPROVED_WITH_FINDINGS with 0 open BLOCKER/HIGH/MEDIUM) and the annotated tag
`v1.1.0-phase11` is created, and the live pinned-host confirmation is owned by
task 14.3. V1.1-1 (this
plan, ADR-016..ADR-020, and the GOALS.md/PLANS.md
pointers) is already complete at commit `5ec8ae2` and is not re-planned as open
work.

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
  `docs/decisions.md`, then update `docs/architecture.md` or
  `docs/data-model.md` before implementation relies on them.
- Keep provider DTOs, Jellyfin entities, credentials, and implementation
  details at their boundaries. The matching, metadata, rendering, and cache
  pipeline consumes the canonical models.
- Every milestone must leave Jellyfin safe when the provider, cache, matcher, or
  renderer fails.

## Milestone Status

| # | Milestone | Status | Exit gate |
| --- | --- | --- | --- |
| 1 | Plugin foundation | Complete | Plugin loads on the selected Jellyfin 12 ABI with valid configuration and lifecycle behavior. |
| 2 | Sonarr & Radarr integration | Complete | Both providers can be configured independently, probed, queried read-only, and mapped into canonical observations. |
| 3 | Media matching | Complete | Eligible movies, series, and episodes match only with validated identity evidence. |
| 4 | Badge rendering | Complete | Canonical metadata renders deterministically within configured limits, with safe pass-through on failure. |
| 5 | Jellyfin artwork integration | Complete | Derived poster artwork is published through Jellyfin's supported image APIs without modifying media files or bypassing normal image delivery. |
| 6 | Caching, updates & performance | Complete | Reconciliation, invalidation, persistence, and bounded work avoid unnecessary requests and processing. |
| 7 | Testing & release | Complete (tasks 7.1-7.8 complete; all five Phase 7 acceptance criteria are met; task 7.7 relocates the state root outside `PluginsPath` by ADR-014 and the re-run live verification passes, so Phase 7 acceptance criterion 2 is met; task 7.8 resolves the task 7.3 release blocker 7.3-F1 by ADR-015 and the re-run live end-to-end verification passes, so `GOALS.md` criteria 5 and 8 are met as shipped, with criterion 6 met for render and publication but only partial for provider fetches (`docs/limitations.md` F1), and Phase 7 acceptance criteria 3 and 4 are met; task 7.4 complete (the logs/diagnostics/HTTP/persisted-state secret-leakage and unbounded-data review found no credential leakage or unbounded path); task 7.5 complete - the release package builds byte-reproducibly from a clean checkout and its commands, inputs, artifact identity, and supported version ranges are recorded in `docs/release/build-and-release.md`, so Phase 7 acceptance criterion 5 is met; task 7.6 complete - the known limitations and deferred decisions are consolidated in `docs/limitations.md`; Gate 7 is met (Phase 7 review approved; tag `v0.1.0-phase7`)) | Required unit/integration/acceptance checks pass and the plugin can be built and packaged reproducibly. |
| 8 | Release distribution | Complete (tasks 8.1-8.6 complete; all seven Phase 8 acceptance criteria are met; the annotated tag `v1.0.1` exists at commit `8cba85b` with the committed repository `manifest.json`; the GitHub release and asset upload remain the user's manual step; Gate 8 is met - Phase 8 review approved in `docs/implementation/phase-8/phase-review.json`) | `README.md` is end-user-facing, the repository `manifest.json` is committed with the annotated `v1.0.1` tag, the plugin metadata and version are correct, and the GitHub release publication is left to the user. |
| 9 | Dashboard settings UI and runtime configuration activation (v1.1) | Complete (Phase 9 tasks 9.1-9.5 complete: the get-only `Collection<T>` round-trip was proven to fail under the pinned `JsonDefaults.Options`, so `EnabledLibraries`/`Renderer.Selectors` are now settable and round-trip; `Plugin` implements `IHasWebPages` with an embedded secret-free settings page; `Plugin.UpdateConfiguration` validates and activates a saved candidate at runtime without a restart with last-valid snapshot and private-secret retention; a successful replacement requests a bounded, non-blocking post-save reconciliation so existing posters re-render promptly; and `GoalAIntegrationTests` verifies the composed save -> activate -> bounded-reconcile flow, so limitation F2 is resolved. The Goal A acceptance criteria are met at the integration-test level; Gate 9 is met (the Phase 9 review is approved in `docs/implementation/phase-9/phase-review.json`) and the `v1.1.0-phase9` tag is created; the live Goal A confirmation is owned by task 14.3) | A dashboard settings page loads and saves through the elevation-gated path, a valid change applies without restart with last-valid retention, and a successful save triggers a bounded reconciliation; the get-only `Collection<T>` round-trip is proven. Phase tag `v1.1.0-phase9`. |
| 10 | Logging with configurable verbosity (v1.1) | Complete (tasks 10.1-10.3 complete; all five Phase 10 acceptance criteria are met at the integration-test level; the logging security review is recorded at `docs/implementation/10.3/security-review.json` with 0 open BLOCKER/HIGH/MEDIUM; Gate 10 is met (the Phase 10 review is approved in `docs/implementation/phase-10/phase-review.json`) and the annotated tag `v1.1.0-phase10` is created; the live pinned-host logging confirmation is owned by task 14.3) | The plugin logs through the host pipeline at a bounded, validated, secret-free configurable verbosity; redaction is proven at every level; SEC-5 is rewritten and the logging path is security-reviewed. Phase tag `v1.1.0-phase10`. |
| 11 | Provider inventory cache and library-refresh-driven refresh (v1.1) | Complete at the integration-test level (tasks 11.1-11.4 complete: the canonical, secret-free, in-memory per-connection inventory cache shape and the inventory TTL and record/byte limits are defined, validated at configuration load, and documented in `docs/data-model.md` section 6 and `docs/architecture.md` sections 8 and 12; the provider metadata readers populate and consume the cache at the provider-client boundary so one library read per connection serves a reconciliation window, using the Radarr repeatable `movieId` and Sonarr repeatable `episodeFileIds` bulk selectors instead of per-item file reads; the ArrTags-side invalidation surface is wired to the provider webhook, Jellyfin library refresh/post-scan, scheduled/manual and post-save reconciliation, and the bounded TTL fallback, with no provider conditional request, revision token, `history/since`, or SignalR dependency, and the periodic scheduled reconciliation continues on its unchanged interval; task 11.4 records limitation F1 as resolved and adds the Goal C integration coverage for the one-read-per-window, invalidation-source, bounds, and last-known-good facets, so all four Phase 11 acceptance criteria are met at the integration-test level; Gate 11 is met (the Phase 11 review is approved in `docs/implementation/phase-11/phase-review.json`) and the annotated tag `v1.1.0-phase11` is created) | One provider library read per connection serves a reconciliation window; invalidation is ArrTags-side; the cache is bounded, secret-free, and validated; provider failure keeps bounded last-known-good. Phase tag `v1.1.0-phase11`. |
| 12 | Badge value allowlist and badge size/position (v1.1) | In progress (Phase 12 tasks 12.1-12.4; tasks 12.1, 12.2, and 12.3 are complete; Goals B and E; ADR-017 and ADR-019) | A configured allowlist restricts rendering to listed values and a configured size/anchor affects the badge with per-anchor rail packing, status-pill placement, and safe-area bounds; one coordinated schema/`RenderVersion` advance with regenerated goldens. Phase tag `v1.1.0-phase12`. |
| 13 | README and documentation pass (v1.1) | Not started (Phase 13 tasks 13.1-13.2; Goal D) | The palette override fields are documented with meaning, default colors, and the 4.5:1 contrast rule; the README documents the v1.1 features; the canonical current-state docs are reconciled with no stale claim. Phase tag `v1.1.0-phase13`. |
| 14 | v1.1 release | Not started (Phase 14 tasks 14.1-14.5) | The plugin builds and packages reproducibly at `1.1.0.0`; the full suite and the live pinned-host matrix pass; a fresh security review covers logging, SEC-5, and the settings save path; the changelog/manifest are updated and the annotated `v1.1.0` tag is created after the release-reviewer gate. |

## Milestones

### 1. Plugin foundation

**Objective:** Establish a minimal, loadable Jellyfin 12 plugin foundation: the
project, configuration, dependency-injection, lifecycle, and versioned state
boundaries required by the remaining milestones.

**Phase 1 concept:** This milestone is executed as the ordered Phase 1 tasks
below. The tasks recorded in `docs/implementation-readiness.md` ("Phase 1
Implementation Tasks") map to tasks 1.1, 1.3, 1.4, 1.5, 1.6, 1.7, and 1.8.
Architecture and data-model detail stays authoritative in `docs/architecture.md`
and `docs/data-model.md` and is referenced here rather than duplicated.

| Readiness task | Phase 1 task |
| --- | --- |
| Remove/demote duplicate architecture section; align planner/research wording | 1.1 |
| Extend canonical match model for Sonarr series, episode, and episode-file identity | 1.3 |
| Establish validated operational defaults and limits | 1.4 |
| Implement the plugin entry point and immutable configuration boundary | 1.5 |
| Implement dependency-injection registration and the hosted service lifecycle | 1.6 |
| Establish the versioned plugin state boundary | 1.7 |
| Build/load the actual plugin against the pinned Jellyfin 12.0.0 set | 1.8 |

**Phase 1 sequence and dependencies:**

| Order | Task | Depends on |
| --- | --- | --- |
| 1.1 | Documentation alignment | — |
| 1.2 | Project and test scaffold | 1.1 |
| 1.3 | Canonical Sonarr identity model | 1.2 |
| 1.4 | Operational defaults and limits | 1.2, 1.3 |
| 1.5 | Plugin entry point and configuration | 1.2, 1.4 |
| 1.6 | DI registration and lifecycle foundation | 1.5 |
| 1.7 | Versioned plugin state boundary | 1.2, 1.4, 1.5 |
| 1.8 | Jellyfin 12 build, discovery, and host validation | 1.2, 1.5, 1.6, 1.7 |

#### 1.1 Documentation alignment

**Status:** Complete.

**Objective:** Remove conflicting planning guidance so the accepted architecture
is the sole implementation reference.

**Dependencies:** None.

**Affected files:** `docs/architecture.md`, `docs/research/jellyfin-12-architecture.md`,
`docs/research/poster-rendering-strategies.md`, `docs/research/media-metadata-mapping.md`.

**Work:**

- Remove or explicitly demote the duplicate architecture section at the end of
  `docs/architecture.md`; keep one authoritative phase/milestone sequence.
- Mark the research documents as evidence and reference material, not competing
  architecture or phase definitions.
- Cross-reference ADR-001 wherever research discusses rejected image-delivery
  alternatives.
- Clarify that reverse provider-to-Jellyfin resolution remains deferred until
  webhook scope is decided.

**Tests:** Manual consistency review; search for stale phase names, obsolete
middleware delivery claims, and contradictory scope statements.

**Acceptance criteria:** One authoritative architecture and milestone sequence
exists; research does not override accepted decisions; no resolved architectural
decision is reopened.

**Definition of done:** Documentation diff reviewed and cross-document
contradictions removed.

#### 1.2 Project and test scaffold

**Status:** Complete. Intended-host acceptance is covered by task 1.8.

**Objective:** Create the minimum reproducible Jellyfin 12 plugin project needed
for implementation and validation.

**Dependencies:** 1.1; compatibility pins in `docs/implementation-readiness.md`.

**Affected components:** repository root (`global.json`, solution file), plugin
project under `src/`, plugin manifest, test project under `tests/`, build and
package configuration.

**Work:**

- Target `net10.0` with .NET SDK baseline `10.0.0`.
- Pin every referenced Jellyfin host package to exactly `12.0.0`; forbid a
  `12.1.x` transitive upgrade.
- Add the plugin manifest with `targetAbi: 12.0.0.0`.
- Establish source, test, and package-output boundaries.
- Add deterministic restore/build/test/package commands.

**Tests:** Restore and compile; dependency-graph check for forbidden Jellyfin
upgrades; manifest ABI assertion.

**Acceptance criteria:** A clean checkout restores and builds with SDK `10.0.0`;
the package has the expected plugin identity and ABI; tests run without a live
Arr instance.

**Definition of done:** A clean checkout produces a buildable plugin artifact.

#### 1.3 Canonical Sonarr identity model

**Status:** Complete (model specification). Implementation and tests land with
the provider, matching, and fingerprint work in later milestones.

**Objective:** Represent Sonarr series, episode, and current episode-file
identity explicitly before provider or matching code consumes it.

**Dependencies:** 1.2; `docs/data-model.md`; Sonarr research.

**Affected components:** canonical model, `MediaMatch`, `BadgeMetadata.recordIdentity`,
match/metadata fingerprint generation, `docs/data-model.md`.

**Work:**

- Add a typed, connection-scoped Sonarr identity containing the Sonarr series
  ID, Sonarr episode ID, and current episode-file ID.
- Preserve the existing Radarr movie/file identity shape.
- Represent shared episode files without implying one file belongs to one
  episode; make missing file identity explicit rather than zero/empty.
- Include all identity components in match and metadata fingerprints.
- Keep provider DTOs outside the canonical model.

**Tests:** Equality and fingerprint tests per component; connection-scoping
tests; missing-file, mismatched `episodeFileId`, shared-file, and changed-file-ID
tests; versioned snapshot serialization tests; secret-exclusion tests.

**Acceptance criteria:** A matched Sonarr episode cannot be represented without
distinguishing series, episode, and current-file identity; a changed
episode-file ID changes the dependent fingerprint; provider DTOs do not leak
into the canonical model.

**Definition of done:** `docs/data-model.md`, interfaces, and tests describe and
enforce the same identity contract.

#### 1.4 Operational defaults and limits

**Status:** Complete. The accepted values, validation rules, and safe failure
behavior are recorded in ADR-004 and `docs/architecture.md` section 12.
Configuration-load enforcement and boundary tests are implemented in task 1.5;
state quota and retention enforcement are implemented in task 1.7.
Representative-load validation remains the performance milestone.

**Objective:** Establish validated initial bounds for foundation-level work.

**Dependencies:** 1.2; 1.3 for identity and state sizing.

**Affected components:** configuration model and validator, operational-limits
value object, queue/HTTP/retry/artifact/stale-state policies,
`docs/architecture.md`, `docs/decisions.md`.

**Work:**

- Define explicit limits for queue capacity and per-item single-flight work,
  provider and render concurrency, HTTP timeout, retry count/backoff, maximum
  provider response size, source/derived artifact sizes, provenance retention,
  plugin storage/cache quota, and maximum stale-last-known-good duration.
- Validate limits at configuration load time.
- Ensure active provenance and non-terminal artwork operations are never
  evicted as ordinary cache entries.
- Define safe behavior on storage-quota exhaustion: reject new derived work and
  preserve current artwork.
- Record the selected values and rationale in the authoritative documentation.

**Accepted baseline:** The values, units, validation ranges, and safe failure
behavior are recorded in `docs/architecture.md` section 12, and the decision and
rationale are recorded in `docs/decisions.md` ADR-004. Later milestones may tune
values within the documented ranges without reopening ADR-004.

**Tests:** Boundary/invalid-value validation; retry classification and timeout;
queue overflow and concurrency; response-size rejection; storage quota and
non-eviction; stale-window behavior. Boundary and storage-quota tests are
implemented in tasks 1.5 and 1.7; retry, queue, response-size, and stale-window
behavior tests land with the provider, caching, and performance milestones.

**Acceptance criteria:** Every required limit has an explicit value, unit,
validation rule, and safe failure behavior; tests show limits prevent unbounded
work; authoritative state cannot be evicted as ordinary cache data.

**Definition of done:** Defaults are recorded with explicit validation rules and
safe failure behavior and approved for the initial foundation; configuration-load
and state enforcement are implemented in tasks 1.5 and 1.7, and representative-load
validation remains the performance milestone.

#### 1.5 Plugin entry point and configuration

**Status:** Complete. The parameterless `BasePlugin<PluginConfiguration>` entry
point remains Jellyfin-owned and unchanged; the configuration model, validator,
and immutable replacement-snapshot service are implemented and covered by
foundation tests. Dependency-injection registration is completed in task 1.6.

**Objective:** Implement the thin plugin entry point and immutable configuration
boundary.

**Dependencies:** 1.2, 1.4.

**Affected components:** `Plugin`, `PluginConfiguration`, configuration
validator/snapshot service, administrative diagnostics.

**Work:**

- Add the parameterless `BasePlugin<PluginConfiguration>` entry point with
  plugin identity and `DataFolderPath` ownership.
- Support independently enabled Sonarr and Radarr connections, disabled by
  default.
- Use replacement snapshots rather than sharing mutable configuration with
  workers.
- Validate URLs, finite timeouts, TLS policy, limits, and library/image scope.
- Redact API keys and webhook secrets from logs, diagnostics, fingerprints, and
  canonical snapshots; preserve the last valid snapshot when a replacement is
  invalid.

**Tests:** Plugin construction/identity; valid and invalid configuration;
independent provider enablement; snapshot replacement and last-valid-snapshot;
secret redaction.

**Acceptance criteria:** Both providers can stay disabled without external I/O;
invalid configuration cannot bring down Jellyfin; secrets are absent from
diagnostics and domain state.

**Definition of done:** Configuration behavior is deterministic and covered by
automated tests.

#### 1.6 DI registration and lifecycle foundation

**Status:** Complete (foundation). The parameterless `IPluginServiceRegistrator`
registers the configuration snapshot service, the versioned state repository, the
library-event boundary, and an idle hosted lifecycle service. The hosted service
subscribes to library events only for its own lifetime and unsubscribes
deterministically on shutdown, restart, cancellation, and disposal, with no
provider, rendering, or full-library work during registration or startup. Domain,
provider, queue, and scheduled-task registrations land with the milestones that
introduce those components. Host validation of the wired lifecycle is part of
task 1.8.

**Objective:** Register foundation services without starting provider or
rendering work during registration or startup.

**Dependencies:** 1.5.

**Affected components:** `IPluginServiceRegistrator`, hosted lifecycle, library
event boundary, configuration and state service registration. Scheduled-task,
controller, provider, and queue registrations are added with the milestones that
introduce those components.

**Work:**

- Register configuration, state services, the library-event boundary, and hosted
  work. Domain, provider, queue, and scheduled-task registrations are added with
  the milestones that introduce them.
- Keep registration parameterless and compatible with Jellyfin 12 conventions.
- Start no unbounded refresh or full-library processing synchronously.
- Subscribe and unsubscribe library events within the hosted-service lifecycle.
- Ensure cancellation and shutdown await all owned work.

**Tests:** DI resolution; startup and shutdown; reload and cancellation;
verification that no provider API is called during registration/startup; event
subscription cleanup.

**Acceptance criteria:** The plugin starts with both providers disabled; no
unmanaged background work survives shutdown; registration performs no provider,
rendering, or full-library work.

**Definition of done:** Lifecycle tests pass against the pinned Jellyfin test
environment or a compatible integration harness.

#### 1.7 Versioned plugin state boundary

**Status:** Complete. Versioned envelopes with a schema version and SHA-256
payload integrity, atomic flush-and-rename writes, cache discard versus
authoritative quarantine, traversal-safe path resolution, and bounded cache and
terminal-provenance retention are implemented and covered by foundation tests.
Artifact byte storage and per-surface artwork-operation journaling land with the
artwork and caching milestones.

**Objective:** Establish safe plugin-owned state storage under Jellyfin's
`DataFolderPath`.

**Dependencies:** 1.2, 1.4, 1.5.

**Affected components:** state repository, versioned records and manifests,
atomic file writer, quarantine/recovery handling.

**Work:**

- Define versioned state envelopes and integrity metadata.
- Write via temporary file, flush, and atomic replacement.
- Separate ordinary cache state from authoritative artwork-operation state.
- Rebuild or discard invalid non-authoritative cache entries; quarantine invalid
  authoritative records instead of treating them as absent.
- Apply bounded storage and retention policies.

**Tests:** Atomic write/read; corrupt, torn, incompatible, and missing state;
quarantine; restart recovery; permission and path-traversal.

**Acceptance criteria:** Corrupt cache state does not prevent startup; invalid
authoritative state never triggers blind replay or cleanup; state contains no
credentials or unbounded external payloads.

**Definition of done:** State recovery behavior is deterministic and documented.

#### 1.8 Jellyfin 12 build, discovery, and host validation

**Status:** Complete. The plugin restores, builds, tests, and packages with the
pinned `net10.0` / Jellyfin `12.0.0` compatibility set. The generated package was
installed on the pinned Jellyfin `12.0.0` host (portable `v12.0` amd64 build),
reported as loaded with `targetAbi: 12.0.0.0` and status `Active`, and completed
startup, restart, and shutdown with no load errors, unmanaged background work, or
secret leakage.

**Objective:** Prove the actual plugin works against the pinned Jellyfin
compatibility set.

**Dependencies:** 1.2, 1.5, 1.6, 1.7.

**Affected components:** build/package output, plugin manifest, pinned
Jellyfin `12.0.0` host, validation checklist.

**Required host validation:**

- Build with .NET SDK `10.0.0`.
- Install the generated package on the pinned Jellyfin `12.0.0` host.
- Verify plugin discovery and load, and `targetAbi: 12.0.0.0`.
- Start with both providers disabled; verify configuration loading, DI
  registration, startup, shutdown, and reload.
- Confirm the installed host runtime patch and OS/runtime packaging.
- Review logs for load errors, unmanaged background work, or secret leakage.

**Tests:** Clean restore/build/package; plugin discovery smoke test; host
startup/shutdown smoke test; manifest and ABI verification; runtime
compatibility against the actual host.

**Acceptance criteria:** The plugin builds, installs, is discovered, and loads
on Jellyfin `12.0.0`; the host accepts `targetAbi: 12.0.0.0`; both providers
remain disabled without provider calls; lifecycle and configuration tests pass
on the pinned host.

**Definition of done:** Gate 1 is met and the milestone can be marked complete.

**Phase 1 acceptance criteria:**

- [x] The plugin builds, installs, and loads correctly on Jellyfin `12.0.0`
  with target framework `net10.0` and manifest `targetAbi: 12.0.0.0`.
- [x] Sonarr and Radarr can be enabled, disabled, and configured independently.
- [x] Invalid configuration is rejected or retained as the last valid snapshot
  without taking down Jellyfin.
- [x] Credentials never appear in persisted canonical data, cache identity,
  logs, or error messages.
- [x] Startup and shutdown leave no unmanaged background work.
- [x] A restart with missing, corrupt, or incompatible non-authoritative cache
  state rebuilds it without blocking Jellyfin; invalid artwork-operation state
  is quarantined and preserves the current image without blind replay.
- [x] The canonical model represents Sonarr series, episode, and episode-file
  identity explicitly and excludes provider DTOs.
- [x] All operational defaults have explicit values, validation rules, and safe
  failure behavior.

**Gate 1:** The ABI and configuration/lifecycle tests pass, the plugin can start
with both providers disabled, and tasks 1.1-1.8 meet their acceptance criteria.

### 2. Sonarr & Radarr integration

**Objective:** Implement independent, read-only Arr clients and translate
provider responses into the canonical provider, match-input, and badge metadata
models.

**Deliverables:**

- Dedicated `IHttpClientFactory`-based Sonarr and Radarr clients using the v3
  API contract.
- Connection probing with authentication, URL-base, version, health, and
  capability results that do not expose credentials.
- Defensive DTO parsing and provider-to-canonical mapping for current movie,
  series, episode, and file resources.
- Actual file quality and available technical metadata, separated from quality
  profile policy.
- Bounded timeout, cancellation, retry, and redacted error behavior.

**Tasks:**

- [x] 2.1 Define the shared provider-client boundary and connection identity rules.
- [x] 2.2 Register dedicated named or typed clients through Jellyfin's standard
  HTTP client factory; do not create raw clients per request.
- [x] 2.3 Implement Radarr v3 reads for local movies and current movie-file data,
  including the dedicated file request when enabled fields are not embedded.
- [x] 2.4 Implement Sonarr v3 reads for series, episodes, and episode files,
  joining files by the validated episode file identifier.
- [x] 2.5 Map provider data into `ArrProvider`, `ArrConnection`, and
  `BadgeMetadata` without leaking provider DTOs past the boundary.
- [x] 2.6 Preserve unknown technical values as unknown rather than false or empty
  claims, and bound custom values before they can reach a badge.
- [x] 2.7 Add tests for authentication failures, unavailable services, malformed
  responses, optional fields, version drift, cancellation, and retries.

**Task 2.1 status:** Complete. The shared provider-client boundary and connection
identity rules are implemented in `src/ArrTags/Providers`. The canonical
`ArrProvider` and `ArrConnection` identities, the connection-scoped
`ArrConnectionId` (derived from provider kind and normalized base URL, excluding
the API key and user information), connection health and TLS policy, derived
capabilities, bounded redacted `ArrProviderError` outcomes, the
`IArrProviderClient` probe boundary, and the secret-free
`ArrConnectionCatalog` mapping from `PluginConfigurationSnapshot` are covered by
`ProviderBoundaryTests`. Dedicated `IHttpClientFactory` client registration,
concrete Radarr/Sonarr reads, and canonical metadata mapping remain the
following tasks.

**Task 2.2 status:** Complete. Jellyfin's standard `IHttpClientFactory` is used
rather than raw `HttpClient` instances. `ArrTagsServiceRegistrator` registers a
named client per provider kind and TLS policy (`ArrHttpClientNames`) so the
pooled message handler matches the connection's certificate policy; the relaxed
handler is only installed for the explicit `AllowInsecure` connection policy.
`IArrHttpClientFactory`/`ArrHttpClientFactory` build a connection-scoped client
with the configured base URL, finite timeout, and `Accept: application/json`
without ever reading an API key. Credential persistence and access are now
defined by ADR-005: the configuration boundary owns a private versioned secret
snapshot and issues short-lived leases without exposing values to workers'
mutable configuration or canonical state. The concrete Radarr client implements
that boundary (task 2.3); the Sonarr client follows in task 2.4. Registration
adds no startup work and both providers remain disabled by default.

**Phase 2 credential boundary status:** Resolved by `docs/decisions.md` ADR-005,
with the contract reflected in `docs/architecture.md`, `docs/data-model.md`, and
`docs/implementation-readiness.md`. The resolver boundary was implemented and
tested in task 2.3; no additional architecture decision is required for API-key
access. Webhook route exposure, replay handling, and payload policy remain
separate later decisions.

**Task 2.3 status:** Complete. The ADR-005 credential boundary is implemented in
`src/ArrTags/Secrets`: typed `SecretReference` slots, a non-serializable
`SecretLease`, and the singleton `IPluginSecretResolver` owned by
`ConfigurationSnapshotService`. The configuration boundary now publishes an
atomic public-snapshot/private-secret generation with a monotonic
`configurationVersion`; `ArrConnection` carries the safe API-key reference and
the configuration version so a lease is valid only for the generation it came
from, and invalid replacements and rotations leave the active secret unchanged
until the new generation is durable.

The read-only Radarr v3 client is implemented in
`src/ArrTags/Providers/Radarr`. `RadarrClient` probes `GET /api/v3/system/status`,
reads the local library through `GET /api/v3/movie`, and reads the fully
populated current file through the dedicated `GET /api/v3/moviefile?movieId=`
endpoint that includes custom-format and media-info fields. Every read acquires
a version-matched API-key lease, applies it only as `X-Api-Key`,
applies the bounded timeout, cancellation, retry/backoff, response-size, and
redacted error policy, and returns an `ArrProviderReadResult<T>` instead of
throwing. Provider DTOs stay in `ArrTags.Providers.Radarr` and do not enter
canonical state. Malformed, oversized, unauthorized, unavailable, and
incompatible responses map to bounded `ArrProviderError` outcomes. Tests cover
the credential boundary and the Radarr reads without a live Arr instance.
`BadgeMetadata` mapping and the actual-versus-profile quality semantics remain
tasks 2.5 and 2.6.

**Task 2.4 status:** Complete. The read-only Sonarr v3 client is implemented in
`src/ArrTags/Providers/Sonarr`. `SonarrClient` probes
`GET /api/v3/system/status`, reads the local library through `GET /api/v3/series`,
reads episodes through
`GET /api/v3/episode?seriesId=&includeEpisodeFile=true`, and reads the series
file inventory through `GET /api/v3/episodeFile?seriesId=`. It reuses the same
ADR-005 version-matched API-key lease, bounded timeout, cancellation,
retry/backoff, response-size, and redacted-error policy as the Radarr client,
and returns `ArrProviderReadResult<T>` outcomes instead of throwing.

The validated current-file join is implemented by
`SonarrEpisodeFileResolver.Resolve`: it trusts the embedded `episodeFile` only
when its identifier equals the episode's authoritative `episodeFileId`, falls
back to the series inventory otherwise, and returns no file identity (rather
than an unrelated file) when the episode has no current file or the referenced
identifier is missing. Provider DTOs stay in `ArrTags.Providers.Sonarr` and do
not enter canonical state. Tests cover the reads, the join, and bounded
failures without a live Arr instance. Canonical `BadgeMetadata` mapping was
completed in task 2.5; unknown-value and custom-value semantics were completed
in task 2.6, and provider failure-matrix tests remain task 2.7.

**Task 2.5 status:** Complete. The canonical identity and metadata models are
implemented in `src/ArrTags/Providers` and `src/ArrTags/Metadata`. Connection
scoped `ArrFileIdentity` (explicit `Present`/`Absent`, never zero), the typed
`ArrRecordIdentity` base with `SonarrIdentity` (series, episode, and current
file) and `RadarrIdentity` (movie and current file), and the provider-neutral
`BadgeMetadata` with quality, resolution, dynamic-range, codec, channel, audio
feature, source, upgrade-pending, custom-badge, and extension fields are covered
by `CanonicalIdentityTests` and `MetadataMappingTests`.

`RadarrMetadataMapper` and `SonarrMetadataMapper` translate validated provider
DTOs into the canonical shapes. The mappers read actual file quality only from
the current file resource (never the movie or series quality profile), validate
that a supplied file matches the authoritative record file association
(`movieFileId` and `episodeFileId`), keep absent file identity explicit, and
preserve unreported technical values as unknown rather than false. Every
`BadgeMetadata` carries a deterministic metadata fingerprint over the badge
schema version, the provider and connection-scoped identity, and all
badge-affecting values, excluding the observation timestamp and any credential.
Provider DTOs remain in `ArrTags.Providers.Radarr` and `ArrTags.Providers.Sonarr`
and do not appear in any canonical type. Custom-value bounding and three-state
unknown hardening were completed in task 2.6; provider failure-matrix tests
remain task 2.7.

**Task 2.6 status:** Complete. Canonical `BadgeMetadata` now preserves unknown
technical values explicitly. `AudioFeatures` is nullable: `null` means the
provider did not report usable audio codec data, while an empty set means a
reported codec yielded no known feature. The metadata fingerprint distinguishes
those two states, and the badge schema version was incremented to 2 to mark the
meaning change. `RadarrMetadataMapper` and `SonarrMetadataMapper` return no
feature set when the audio codec is absent. Custom badge values are bounded at
the canonical boundary before they can reach a badge: blank values are dropped,
control characters are removed, at most `MaxCustomBadgeCount` (32) values are
kept in provider order, and each value is truncated to `MaxCustomBadgeLength`
(128) characters. That is a defensive metadata bound; the renderer's separate
display/truncation policy is now defined by ADR-009.
`MetadataMappingTests` covers unknown-versus-empty audio features, the
fingerprint distinction, and custom-value count, order, length, and
control-character behavior. Provider failure-matrix tests are implemented in
task 2.7.

**Task 2.7 status:** Complete. `ProviderFailureMatrixTests` exercises the shared
read boundary for both providers across the documented failure classes without a
live Arr instance. Authentication checks cover `401`/`403` on reads and probes,
a missing credential lease failing closed with no HTTP call, and redacted
messages. Unavailable-service checks cover `409`, `429`, `5xx`, unreachable
connections, and request timeouts mapping to `ProviderUnavailable` after the
bounded retry count. Malformed checks cover non-JSON, empty, `null`,
wrong-shaped, truncated, and oversized responses (both with and without a
`Content-Length`), which map to `InvalidResponse` and are never retried.
Optional-field checks confirm omitted movie, series, episode, and file fields
deserialize tolerantly and remain unknown rather than false. Version-drift
checks confirm a future version, changed casing, unknown extra fields, and a
missing version probe successfully as informational, while a missing or
non-matching provider identity still reports `Incompatible`. Cancellation during
retry backoff stops further attempts, retries are bounded, non-transient
statuses (`400`/`422`, `404`, `401`/`403`) are not retried, and every retry
reapplies the API key as a header without placing it in the URL.

**Acceptance criteria:**

- [x] Each provider can be probed and queried independently.
- [x] No integration path calls an Arr write endpoint, database, or lookup
  endpoint for routine refreshes.
- [x] Actual file quality is available where the provider reports it; a quality
  profile is never presented as actual file quality.
- [x] Missing, incomplete, invalid, and unsupported provider data produces a
  bounded domain outcome and does not fail a Jellyfin request.
- [x] API keys, webhook secrets, and sensitive request details are redacted from
  diagnostics.

**Gate 2:** Both integrations pass contract and failure tests and produce the
same canonical shape for equivalent badge-relevant observations.

### 3. Media matching

**Objective:** Match eligible Jellyfin items to exactly one scoped Sonarr or
Radarr record using stable provider identity first and explicit fallback rules.

**Deliverables:**

- Provider-neutral `MediaIdentity` and `MediaMatch` implementation.
- Movie-to-Radarr matching by Jellyfin TMDb ID, then IMDb ID.
- Series-to-Sonarr matching by TVDB ID and validated stable-ID fallback where
  available.
- Episode-to-Sonarr matching by episode TVDB ID, then validated season/episode
  numbers after the series match.
- Explicit handling for no match, ambiguity, unsupported item, missing file,
  virtual item, remote item, specials, anime numbering, double episodes, and
  multi-episode files.
- V1 path fallback is deferred out of scope by ADR-008; no host/container path
  equivalence is assumed and no path-only match is accepted.

**Tasks:**

- [x] 3.1 Build Jellyfin `MediaIdentity` snapshots from the supported canonical
  item types (`Movie`, `Series`, `Season`, `Episode`) and library scope. Library
  scope entries are collection-folder/library identifiers, and V1 badge surfaces
  are Movie and Episode posters; Series/Season are structural only (ADR-006).
- [x] 3.2 Implement candidate selection, evidence recording, and deterministic
  `MediaMatch` fingerprints.
- [x] 3.3 Reject zero-candidate and multiple-candidate matches rather than
  guessing from title or year.
- [x] 3.4 Implement the documented movie, series, and episode matching order.
- [x] 3.5 Define and test the numbering policy for specials, anime, and
  multi-episode records before enabling number fallback.
- [x] 3.6 Resolve DG-5. Configured path normalization and path fallback are
  deferred out of V1 by ADR-008; this is a documentation-only closure and adds
  no configuration or runtime matching rule.
- [x] 3.7 Verify that local Arr record and file IDs are always scoped by
  connection.
- [x] 3.8 Reject ineligible item locations (remote, virtual, offline, `.strm`,
  fileless, and otherwise non-local) with a fail-closed no-badge outcome before
  provider matching, completing acceptance criterion 3. Paths remain
  non-identity context only under ADR-008.

**Task 3.1 status:** Complete. Canonical `MediaIdentity` snapshots (item type,
Jellyfin item id, collection-folder/library id, normalized provider ids, title,
production year, parent-series context, raw season/episode/end numbers, raw
location and media-source summary, and a Jellyfin source fingerprint) are built
for Movie, Series, Season, and Episode in `src/ArrTags/Media`. Library scope is
applied deterministically against collection-folder/library identifiers: an
empty scope means no restriction, and a non-empty scope with an unresolvable
library fails closed. V1 badge-surface eligibility remains limited to Movie and
Episode posters and honours the configured poster flags; Series and Season stay
structural and produce no badge. Provider DTOs and provider-specific concepts
are excluded, and the builder captures raw Jellyfin facts only, leaving the DG-4
episode-numbering policy to the matching task. DG-5 is now resolved by ADR-008;
path mapping is out of V1. Jellyfin library
lookups are isolated behind the `IMediaLibraryResolver` boundary so the snapshot
logic is covered without a live host. Remaining Milestone 3 tasks were not started at
the time of task 3.1; task 3.2 is recorded below.

**Task 3.2 status:** Complete. The provider-neutral matching foundation lives in
`src/ArrTags/Matching`. `MatchCandidate` is the canonical, connection-scoped
description of one Arr record that could match (concrete `ArrRecordIdentity`,
normalized external provider identifiers, optional title/year/numbering/path
context) and it refuses a record identity that is not scoped to its own
connection and provider kind. `CandidateMatchRule` and `ProviderIdMatchRule`
define one ordered, evidence-keyed identity comparison, and `CandidateSelector`
applies the ordered rules as identity fallbacks, recording a `MatchEvidence` step
per rule (method, key, matched value, candidate and match counts) and returning a
`CandidateSelection` of survivors. The first rule with a match decides the
selection, so later rules are fallbacks and never widen the search; title and
year are never used as identity. `MediaMatch` is the canonical result
(`status`, `method`, typed `recordIdentity`, normalized `matchedProviderIds`,
safe `ambiguityReason`, and `matchedAt`) with input validation that a
`Matched` status requires a concrete method and a connection/provider-scoped
record identity and that a non-matched status cannot carry one. `MediaMatch`
computes a deterministic `MatchFingerprint` over the match schema version, the
Jellyfin subject, the connection and provider scope, the status and method, the
full record identity including explicit file presence, and the matching provider
identifiers; the fingerprint is independent of observation timestamps and never
contains a credential. Mapping the survivor count to a `Matched`/`NotFound`/
`Ambiguous` status (task 3.3), the provider-specific candidate assembly and
documented rule order (task 3.4), the numbering policy (3.5), and configured path
mapping (3.6) are deliberately not implemented here; ADR-008 defers path mapping
out of V1.

**Task 3.3 status:** Complete. The match status policy lives in
`src/ArrTags/Matching/MediaMatchPolicy.cs`. `MediaMatchPolicy.Resolve` maps a
provider-neutral `CandidateSelection` into the canonical `MediaMatch`: zero
survivors produce `NotFound`, more than one survivor produces `Ambiguous`, and
exactly one survivor produces `Matched`. The zero- and multiple-candidate
outcomes carry no record identity and a safe, provider-neutral reason, and they
are never resolved by falling back to candidate title or production year. A
`Matched` result uses the deciding rule's `MediaMatchMethod`, the surviving
candidate's connection-scoped `ArrRecordIdentity`, and records the agreeing
provider identifier only for a `ProviderId` decision, so `Number` or
`ConfiguredPath` decisions add no provider identifiers. Provider-specific
candidate assembly and the documented movie/series/episode rule order were
completed in task 3.4; the episode numbering policy (task 3.5) is complete,
configured path mapping is deferred out of V1 by ADR-008, and the connection-
scope verification task (3.7) remains to be implemented.

**Task 3.4 status:** Complete. The documented matching order is implemented in
`src/ArrTags/Matching/MatchRuleOrder.cs`, `MediaMatcher.cs`, and
`MatchProviderIdKeys.cs`, with provider-specific candidate assembly in
`src/ArrTags/Providers/Radarr/RadarrMatchCandidateFactory.cs` and
`src/ArrTags/Providers/Sonarr/SonarrMatchCandidateFactory.cs`. `MatchRuleOrder`
returns the ordered identity rules per item type and provider kind: Movie to
Radarr uses TMDb then IMDb; Series to Sonarr uses TVDB then the other stable
provider ids Sonarr reports locally (TMDb, then IMDb); Episode to Sonarr uses
the episode TVDB id. `MediaMatcher.Match` applies that order and resolves the
outcome through the task 3.3 status policy, and `MediaMatcher.MatchEpisode`
enforces the documented "after the series match" ordering: it matches the parent
series first, then evaluates only episode candidates whose connection-scoped
`SonarrIdentity.seriesId` equals the matched series, so an episode is never
matched against another series' records. Unsupported item/provider pairs
(Movie/Sonarr, Series/Radarr, Season either provider, Episode/Radarr) and
episodes without parent series context produce `Unsupported` with a safe,
provider-neutral reason instead of a guessed match. The Radarr and Sonarr
candidate factories translate validated provider DTOs into canonical
connection-scoped `MatchCandidate` values, mapping the documented provider ids
and descriptive context and keeping provider resources at the integration
boundary; an episode candidate is always anchored to its series identity. Exact
season/episode number fallback is enabled by task 3.5 after the episode TVDB
rule; configured path fallback remains disabled by ADR-008, and the rule order
test asserts no `ConfiguredPath` rule is present. Tests in `MatchRuleOrderTests`,
`MediaMatcherTests`, and `MatchCandidateFactoryTests` cover the rule order,
cross-provider and structural rejection, TMDb-before-IMDb ordering, IMDb
fallback, connection-scoped results, series-then-episode scoping, bounded
failures, and DTO-to-candidate mapping without a live Arr instance.

**Task 3.5 status:** Complete. The explicit V1 episode-numbering policy
(decision gate DG-4, recorded in ADR-007) lives in
`src/ArrTags/Matching/EpisodeNumberingPolicy.cs` and is applied by
`src/ArrTags/Matching/SeasonEpisodeMatchRule.cs`. Number fallback is enabled in
`MatchRuleOrder` after the episode TVDB id and applies only to regular, single
episodes: the Jellyfin identity and the Sonarr candidate must each have a
positive season and episode number, season zero specials are excluded, and a
multi-episode span (`EpisodeNumberEnd > EpisodeNumber`) is excluded because one
Jellyfin item can map to multiple Sonarr episode records. An end number equal to
the start is treated as a single episode. Absolute, scene, and other alternate
numbering is never an identity key, so absolute-number agreement alone never
matches; anime and other series still require the episode TVDB id when normal
number fallback is ineligible. Configured path fallback is deferred out of V1 by
ADR-008. The comparison is exact `(seasonNumber,
episodeNumber)` equality with no tolerance and never uses title, year, path, or
air date. Ineligible or non-equivalent candidates do not satisfy the rule, so
the existing status policy yields `NotFound`/`Ambiguous` and no badge. Tests in
`EpisodeNumberingPolicyTests` cover eligibility, specials, spans, missing
numbers, item/provider mismatch, exact matching, and bounded rejection; new
`MediaMatcherTests` cases cover number fallback after the series match,
series-scoped number fallback, special and span exclusion, absolute-number
mismatch, and number ambiguity.

**Task 3.6 status:** Complete as an architecture/documentation decision only.
ADR-008 resolves DG-5 by deferring configured, connection-scoped path mappings
and path normalization out of V1. V1 never compares Jellyfin and Arr paths,
never assumes host/container equivalence, and never emits a `ConfiguredPath`
match. Items that lack the approved provider-ID or regular-number evidence remain
unmatched with no new badge. No source or test implementation was added for this
task; task 3.7 completed the connection-scoping verification recorded below.

**Task 3.7 status:** Complete. Verification confirmed that every Arr-local record
and file identifier is scoped by its originating `ArrConnection`; no production
change was needed because the canonical model already enforces the contract.
`ArrRecordIdentity` and its `SonarrIdentity`/`RadarrIdentity` shapes always carry
the connection and include it in equality, hashing, and `ToString`, and
`MatchCandidate` and `MediaMatch` both reject a record identity whose connection
does not match the match scope. `ArrFileIdentity` has no connection of its own and
is scoped only through its enclosing record identity, so consumers must resolve
files through the scoped identity. New tests in
`tests/ArrTags.Tests/ConnectionScopingTests.cs` (14 tests) prove that identical
numeric IDs on two Radarr connections and on two Sonarr connections produce
distinct identities, candidates, matches, and metadata fingerprints; that
identical local IDs across provider kinds are distinct; that connection scopes
are derived distinctly from different base URLs and are used as the provider
instance identity; and that the matcher resolves identical local IDs to the
requested connection for both providers. Build and test pass with 0 warnings and
327 passing tests at the time of task 3.7.

**Task 3.8 status:** Complete. The fail-closed V1 location policy is implemented
in `src/ArrTags/Media/MediaLocationEligibility.cs` and applied by
`src/ArrTags/Matching/MediaMatcher.cs`, completing acceptance criterion 3.
`MediaLocationSummary` now captures whether the item or any media source is
remote and whether any path is a `.strm` reference, in addition to the existing
location kind, file-protocol, source-count, and primary-path facts.
`MediaLocationEligibility` rejects remote, `MediaSourceInfo.IsRemote`, virtual,
offline, unknown, `.strm`, and non-local/fileless locations with a bounded,
path-free reason, and treats a missing location summary as not evaluated so only
positively ineligible locations are rejected. `MediaMatcher.Match` applies the
gate to the V1 badge surfaces (Movie and Episode) after the item/provider rule
check, and `MediaMatcher.MatchEpisode` applies it to the episode before any
series match, so an ineligible episode never proceeds into provider matching.
Series and Season remain structural and are intentionally not location-gated.
`MediaEligibility.IsEligible` now also requires an eligible local file location.
No path is compared or used as identity (ADR-008), and eligible local-file
matching behavior is unchanged. Tests in
`tests/ArrTags.Tests/LocationEligibilityTests.cs` (10 facts plus a 3-case theory,
13 test cases) cover eligible local Movie and Episode matching, remote, virtual,
offline, `.strm`, and fileless no-badge outcomes, the episode does-not-proceed
case, the Jellyfin location capture, and the combined eligibility gate. Build and
test pass with 0 warnings and 340 passing tests. All Milestone 3 acceptance
criteria and Gate 3 are now satisfied.

**Acceptance criteria:**

- [x] A supported Jellyfin movie can match its Radarr movie with validated
  provider identity.
- [x] A supported series and episode can match their Sonarr records using the
  approved identity and numbering policy.
- [x] Ambiguous, missing, virtual, remote, and unsupported cases produce no new
  badge and a safe diagnostic status.
- [x] Titles and years are never sole proof of an automatic match.
- [x] Changing match evidence changes the match fingerprint and invalidates the
  dependent metadata state.

**Gate 3:** Met. Representative movie, series, episode, mismatch, ambiguity,
numbering, and ineligible-location cases pass without guessed matches; build and
test pass with 0 warnings and 340 passing tests.

### 4. Badge rendering

**Objective:** Convert canonical `BadgeMetadata` and configuration into a
deterministic, bounded visual result without coupling the renderer to Sonarr,
Radarr, or Jellyfin artwork storage.

**Deliverables:**

- Configurable badge definitions for the V1 metadata fields supported by the
  selected provider data, including quality and relevant technical values.
- Provider-neutral selection, fallback, styling, placement, ordering, and
  visibility behavior.
- Versioned renderer with deterministic output and complete render fingerprints.
- Bounded image input/output, dimensions, text, concurrency, and cancellation.
- Pass-through behavior for unknown metadata, unsupported input, decode/encode
  failure, cancellation, and resource-limit violations.
- Pinned, plugin-owned renderer stack (SkiaSharp managed/native packages and the
  bundled DejaVu Sans Bold 2.37 font) with shipped license notices (ADR-010).

**Tasks:**

- [x] 4.1 Implement metadata selectors against `BadgeMetadata`, not provider DTO
  paths.
- [x] 4.2 Define the initial badge field set, text rules, contrast behavior,
  placement, scale, margins, and output format policy.
- [x] 4.3 Keep unknown technical values distinct from confirmed negative values.
- [x] 4.4 Implement render request and result fingerprints containing every
  output-affecting value, including renderer and badge schema versions.
- [x] 4.5 Enforce image and text limits before decode, draw, and encode work.
- [x] 4.7 Pin the SkiaSharp managed and Linux native asset packages to exact
  versions, embed the DejaVu Sans Bold 2.37 font as a plugin resource, and ship
  the font and Skia license notices (ADR-010).
- [x] 4.8 Spike SkiaSharp compatibility with the Jellyfin 12 host: confirm the
  host's pinned SkiaSharp/HarfBuzzSharp version and native library name, verify
  whether the plugin resolves that shared version or needs its own isolated
  copy, and prove one decode/draw/encode round trip on the pinned Linux runtime
  before building the drawing engine (ADR-010).
- [x] 4.9 Implement the provider-neutral renderer service and drawing engine: the
  `RenderAsync`/`RenderRequest`/`SourceImageInput`/`RenderResult` contract,
  ADR-009 selectors, priority, rail layout, typography, truncation, and
  contrast, and the ADR-010 sRGB PNG encode, alpha, and metadata policy
  (ADR-010).
- [x] 4.6 Test dimensions, format behavior, truncation, layout, cancellation, and
  renderer failure pass-through.
- [x] 4.10 Extend the immutable configuration model, snapshot, and validator with
  enabled V1 selectors, bounded templates, and contrast-validated palette/style
  overrides, including the secret-free renderer configuration fingerprint
  (ADR-010).
- [x] 4.11 Add golden-image, byte-determinism, PNG-contract, and cross-runtime
  tolerance tests with synthetic fixtures and no auto-approval of changed
  goldens (ADR-010).

**Authoritative Phase 4 execution order:** 4.1, 4.2, 4.3, 4.4, 4.5, 4.7, 4.8,
4.9, 4.6, 4.10, 4.11. The task list above is listed in this execution order and
must be walked in it. Task 4.6 was reordered after task 4.9 because its stated
behavior tests (format behavior, layout, cancellation, and renderer failure
pass-through) cannot compile or execute before the renderer service contract,
drawing engine, and pinned renderer assets exist; it was reported blocked in
`docs/implementation/4.6/worker-report.json` and must not be attempted before
tasks 4.7 through 4.9.

**Task 4.1 status:** Complete. The provider-neutral `BadgeSelector` vocabulary
(Quality, Resolution, DynamicRange, Source, VideoCodec, Audio, CustomBadge, and
UpgradePending) is resolved against canonical `BadgeMetadata` by
`BadgeSelectorResolver`. Resolution reads only the canonical model, emits
technical values in the ADR-009 priority order, builds the composite audio value
from confirmed features then codec then channel count, replaces the generic
dynamic-range label with `DV` only when Dolby Vision is confirmed, produces one
candidate per retained custom value, and treats `UpgradePending` as a separate
`UPGRADE` status only when explicitly true. Unknown and absent values are
omitted rather than rendered as placeholders or inferred negatives. Build and
test pass with 0 warnings and 355 passing tests (15 new selector tests).

**Task 4.2 status:** Complete as the DG-3 documentation decision. ADR-009 fixes
the V1 selector vocabulary, field priority, Movie/Episode Primary-poster
layout, typography and geometry, contrast-validated palette, 24-scalar display
limit, PNG/RGB-or-RGBA output, source-dimension scaling, high-DPI behavior, and
pass-through behavior for unknown, incomplete, cancelled, malformed, or failed
renders. No rendering code is included in this decision closure.

**Task 4.3 status:** Complete. The selector resolver keeps the canonical
unknown-versus-confirmed-negative distinction intact. Tri-state technical flags
(Dolby Vision and upgrade-pending) are resolved through one explicit
`ResolveConfirmedTrue` rule: only a confirmed `true` yields a display value, so a
confirmed negative and an unknown value are both omitted but never inferred from
one another. A confirmed generic dynamic range (for example `SDR`) is still
displayed, while an unknown range is omitted; unknown audio features do not
suppress a confirmed codec, and a confirmed empty feature set never infers a
feature. No canonical value is mutated: the metadata fingerprint continues to
distinguish unknown from confirmed-negative and unknown from confirmed-empty, and
`tests/ArrTags.Tests/BadgeUnknownValueTests.cs` proves the behavior with 11 new
tests. Build and test pass with 0 warnings and 366 passing tests.

**Task 4.4 status:** Complete. The renderer now has a provider-neutral,
output-affecting fingerprint boundary. `RenderVersion` owns the code-owned
renderer and badge schema versions. `RenderOutputPolicy` holds the ADR-009/010
output-affecting policy (format, color space, alpha policy, font identity,
palette, scale policy, and text limits). `RenderFingerprintInput` is a validated,
immutable snapshot of every output-affecting value, and `RenderFingerprint`
computes two deterministic SHA-256 fingerprints: the result/output fingerprint
(source, dimensions, metadata, configuration, resolved badge values, output
policy, and both versions; independent of item identity) and the request
fingerprint (render key) that scopes it to the Jellyfin item and poster surface.
Correlation identifiers and timestamps are absent by construction. The new
`RenderFingerprintTests` suite adds 20 tests proving determinism, item
independence of the output identity, sensitivity to every output-affecting
value and version, timestamp exclusion, and input validation. Build and test
pass with 0 warnings and 386 passing tests.

**Task 4.5 status:** Complete. The renderer now has a provider-neutral
pre-decode/pre-draw/pre-encode limit boundary. `RenderLimitGuard` validates a
bounded source descriptor (exact byte length and oriented dimensions) against
the accepted `OperationalLimits`: non-positive descriptors are rejected as
malformed, byte length is compared with `SourceArtifactLimitBytes`, and each
oriented side is compared with `MaxImageDimensionPixels` before any decode or
allocation. The planned derived output surface is validated with the same
per-side bound plus a conservative pre-encode comparison of the uncompressed
RGBA surface against `DerivedArtifactLimitBytes`. `BadgeTextNormalizer` removes
non-whitespace control scalars, collapses whitespace runs to one space, trims
the ends, and enforces the ADR-009 text limit using the `RenderOutputPolicy`
values: at most 24 Unicode scalar values, with end truncation to the first 21
scalars plus `...`. It counts scalar values rather than UTF-16 characters,
handles supplementary and combining scalars, and never splits a surrogate pair.
`RenderLimitReason` and `RenderLimitResult` provide the bounded non-secret
failure/pass-through shape: a rejected result carries exactly one safe reason
code and never a partial artifact, and source bytes are never mutated. The new
`RenderLimitTests` suite adds 39 tests. Build and test pass with 0 warnings and
425 passing tests.

**Task 4.7 status:** Complete. The renderer stack is now pinned and the bundled
font is plugin-owned. `src/ArrTags/ArrTags.csproj` references `SkiaSharp` and
`SkiaSharp.NativeAssets.Linux` at the exact Jellyfin 12.0.0 host version
`3.119.4` (no `HarfBuzzSharp`; that decision remains task 4.8), and both
`packages.lock.json` files were regenerated so locked-mode restore stays valid.
`DejaVuSans-Bold.ttf` (708,920 bytes, SHA-256
`5c1247acef7f2b8522a31742c76d6adcb5569bacc0be7ceaa4dc39dd252ce895`, version
2.37) is embedded as the stable logical resource
`ArrTags.Resources.DejaVuSans-Bold.ttf`. The new `RenderFontIdentity` type records
the family, style, version, exact byte length, SHA-256, and resource logical
name, opens a read-only stream over the exact embedded bytes, and
`RenderOutputPolicy.Default.FontIdentity` is that identity; `RenderFingerprint`
records its full descriptor, so any changed font field changes the output
fingerprint. `licenses/DejaVu-Fonts-License.txt`,
`licenses/SkiaSharp-LICENSE.txt`, and
`licenses/SkiaSharp-THIRD-PARTY-NOTICES.txt`, plus `THIRD-PARTY-NOTICES.md`,
ship with the plugin and the `PackagePlugin` target copies them into the plugin
zip. `tests/ArrTags.Tests/BundledFontTests.cs` adds 12 cases and
`RenderFingerprintTests` gains a font-asset-sensitivity case. Build and test pass
with 0 warnings and 438 passing tests (13 new). Full native-asset
plugin-load-context packaging remains the Phase 5 packaging task per ADR-010.

**Task 4.8 status:** Complete. The spike confirmed the pinned Jellyfin 12.0.0
host renderer stack from its `jellyfin.deps.json` and native files: `SkiaSharp`,
`SkiaSharp.HarfBuzz`, and `SkiaSharp.NativeAssets.Linux` at `3.119.4`, and
`HarfBuzzSharp` / `HarfBuzzSharp.NativeAssets.Linux` at `8.3.1.5`, with ELF64
x86-64 `libSkiaSharp.so` and `libHarfBuzzSharp.so`; the host's SkiaSharp managed
and native files are byte-identical to the local NuGet `3.119.4` assets. Plugin
resolution was measured against Jellyfin's pinned `PluginLoadContext` /
`PluginManager` source and on the pinned .NET 10.0.12 runtime: Jellyfin passes
the plugin folder to `AssemblyDependencyResolver`, which does not discover a
deps.json inside that folder, and then loads every DLL in the folder into the
plugin load context. A plugin that ships no SkiaSharp resolves the host's shared
managed SkiaSharp and native library; a plugin that ships `SkiaSharp.dll` gets
its own copy, and its `libSkiaSharp.so` is found only when it sits next to
`SkiaSharp.dll` in the plugin folder root. The resulting Phase 5 constraint is to
place the managed and root-level native SkiaSharp assets in the plugin folder.
The decode/draw/encode proof lives in the environment-guarded
`SkiaHostCompatibilityTests` suite: it decodes a synthetic PNG, draws with the
bundled DejaVu Sans Bold font from embedded bytes, encodes a non-interlaced
8-bit PNG, and decodes it back, all on the pinned SkiaSharp `3.119.4` native
library. `HarfBuzzSharp` is not needed for ADR-009's single-line bounded labels,
so the 4.7 omission stands. Findings, evidence, and exact commands are recorded
in `docs/research/skia-host-compatibility.md`. Default `./build.sh test` passes
with 0 warnings and 439 passing tests plus 1 environment-guarded skip; the forced
native run passes both compatibility cases.

**Task 4.9 status:** Complete. The provider-neutral renderer service and the
SkiaSharp drawing engine are implemented in `src/ArrTags/Rendering` under
ADR-009 and ADR-010. `SourceImageInput` is an immutable, bounded descriptor
carrying the exact source bytes (copied), content type, oriented dimensions, and
a verified non-empty SHA-256 identity, with no path, Jellyfin entity, provider
DTO, credential, or mutable image object. `BadgeDefinition` is the minimal
provider-neutral snapshot (selector, enabled flag, one bounded template) with a
code-owned V1 default that enables every selector, uses `{value}` for technical
templates, and uses the fixed `UPGRADE` status text; it is not yet persisted into
plugin configuration (task 4.10). `RenderRequest` carries the source, canonical
`MediaIdentity`/`MediaMatch`, optional `BadgeMetadata`, the ordered definition
snapshot, the output policy, the accepted operational limits (cloned), the
secret-free configuration fingerprint, and both versions. `RenderResult` is one
of three bounded variants: rendered (complete PNG artifact, `image/png`, oriented
dimensions, output hash, deterministic output fingerprint), pass-through, or
failed with exactly one safe reason code and never a partial artifact.

`IRenderer.RenderAsync` is served by `SkiaBadgeRenderer`. It resolves the ordered
`BadgeSelection` through `BadgeSelectorResolver` (never branching on provider
kind), enforces the source byte/dimension and derived-output limits before
decode, validates the palette contrast, decodes with SkiaSharp, applies EXIF
orientation to pixels before layout, detects meaningful alpha, packs the
bottom-left two-row/three-pill rail and the independent top-right status pill
using `BadgeLayoutEngine`, and encodes a fixed-settings non-interlaced 8-bit
sRGB PNG with RGB for opaque output and RGBA (straight alpha, canonical
transparent-pixel RGB) otherwise. `BadgeGeometry` holds the ADR-009 reference
geometry (24 px inset, 8 px pill/row gaps, 48 px pill height, 8 px radius, 12/7
px padding, 28 px font) and the `clamp(width / 1000, 0.5, 4.0)` scale;
`BadgeTextNormalizer.Shorten` supplies the layout-stage end-truncation rule; and
the status/technical rail never paint outside the safe area on a short poster.
Cancellation is checked before decode, after decode, between layout and drawing,
and before finalization; a cancellation or exception discards all partial output
and never mutates the source bytes. The renderer output is deterministic: the
same request produces identical bytes, output hash, and output fingerprint.

Default `./build.sh test` passes with 0 warnings and 514 passing tests plus 10
environment-guarded Skia skips; with `ARRTAGS_SKIA_COMPAT=1` and the pinned
sysroot on the loader path the full suite passes all 524 tests, including the
nine real decode/draw/encode render cases (RGB/RGBA dimensions and channels,
source-alpha preservation, palette placement, sRGB/metadata chunks, EXIF
orientation, unsupported-input failure, source immutability, and byte
determinism). The dedicated behavior matrix (4.6) and the renderer
configuration model (4.10) are complete; golden/cross-runtime determinism tests
were completed in task 4.11.

**Task 4.6 status:** Complete. The dedicated renderer behavior matrix is
implemented as seven test-only files under `tests/ArrTags.Tests/`
(`RendererBehaviorFixtures`, `RendererBehaviorDimensionTests`,
`RendererBehaviorFormatTests`, `RendererBehaviorTruncationTests`,
`RendererBehaviorLayoutTests`, `RendererBehaviorCancellationTests`, and
`RendererBehaviorFailureTests`) with 51 new cases (40 unguarded and 11
environment-guarded real render cases) and no production change. Dimensions: the
pure scale theory proves `clamp(width / 1000, 0.5, 4.0)` scales the ADR-009
geometry (pill height and inset) for widths 320/500/1000/2000/5000, and guarded
renders prove a 320x480 opaque source, a 2000x3000 opaque source, a 720x480 alpha
source, an EXIF orientation-3 JPEG, and an EXIF orientation-8 JPEG all keep the
oriented source dimensions and the IHDR dimensions with no upscaling or
downscaling. Format: guarded structural parsing proves a non-interlaced 8-bit PNG,
RGB (color type 2) for opaque output and RGBA (color type 6) for meaningful
alpha, an `sRGB` chunk, an `IEND` terminator, and no retained
`iCCP`/`eXIf`/`tIME`/`tEXt`/`zTXt`/`iTXt` source metadata; a semi-transparent
source pixel keeps straight alpha (alpha 128 and RGB within one channel step), and
a hand-crafted source with non-zero hidden RGB on a fully transparent pixel is
canonicalized to `#00000000`. Truncation: a resolved 40-scalar custom value
normalizes to a 21-scalar prefix plus `...` (24 total), a 24-scalar value is
unchanged, whitespace/control scalars are normalized first, a value that cannot
fit any pill is omitted without expanding lower-priority values, width fitting
shortens an over-wide 24-scalar label further while staying within the 24-scalar
bound, and width fitting never splits a supplementary scalar. Layout: the rail
packs three pills per row across two rows, overflow moves to the next row,
lower-priority candidates are omitted once both rows are full, no pill is split
across rows, every pill stays inside the safe area of narrow and short posters,
the independent top-right `UPGRADE` status pill resolves only from a confirmed
true value and never overlaps the rail, and the technical rail is anchored
bottom-left. Cancellation: a cancelled token is observed before decode and before
the ineligible-surface, source-limit, and contrast decisions, returning
`Failed(Cancelled)` with no artifact and an unchanged source; the renderer's
documented later checkpoints are not independently reachable through the public
synchronous contract without production-only test hooks, so the earliest
checkpoint is the one deterministically exercised and that limitation is recorded.
Failure/pass-through: ineligible Series/Season surfaces, a non-matched result,
missing metadata, no displayable value, and an unavailable source pass through;
source-byte, source-dimension, and derived-output limit violations, low contrast,
an invalid color policy, a missing font resource, unsupported source bytes, and a
descriptor that does not match the decoded dimensions all return exactly one
bounded `Failed` reason with no partial PNG and an unchanged source. Default
`./build.sh test` passes with 0 warnings and 554 passing tests plus 21
environment-guarded Skia skips (575 total); with `ARRTAGS_SKIA_COMPAT=1` and the
pinned sysroot on the loader path the full 575-test suite passes. The renderer
configuration model was completed in task 4.10; the golden/cross-runtime
determinism tests were completed in task 4.11.

**Task 4.10 status:** Complete. The persisted, immutable-replacement
configuration model now carries the user-adjustable V1 renderer configuration
under ADR-010. `RendererConfiguration` (with the `BadgeSelectorConfiguration`
entries) expresses only the enabled/disabled V1 `BadgeSelector` set and one
bounded provider-neutral `{value}` template per selector, plus four optional
palette overrides (technical and upgrade-status background/text). Output format,
color space, alpha policy, font identity, geometry/reference values, text limits,
and the renderer version remain code-owned and are deliberately not representable
in configuration; no geometry or placement value is exposed because ADR-009
fixes them and ADR-010 does not authorize a user-selectable override for them.
Defaults reproduce ADR-009 exactly: an absent selector keeps the code-owned
`BadgeDefinition.V1Default` entry (all selectors enabled, `{value}` technical
templates, fixed `UPGRADE` status text) and an unset palette keeps the ADR-009
colors.

`RendererConfiguration.Validate` (called from `PluginConfigurationValidator`)
rejects unknown or duplicate selector entries, empty/too-long templates or more
than one `{value}` placeholder, malformed color values, and any style pair whose
WCAG contrast is below `BadgeContrast.MinimumRatio` (4.5:1) with a safe,
secret-free message. `RendererConfigurationResolver` maps a configuration to the
ordered `BadgeDefinition` snapshot (canonical selector order; first valid entry
per selector; tolerant fallback to defaults for a configuration that has not been
validated) and to the effective `RenderOutputPolicy` (configured palette applied,
every other value copied from `RenderOutputPolicy.Default`). `RendererPalette`
canonicalizes a valid override to `#RRGGBB` so an equivalent color cannot change
the fingerprint. `PluginConfigurationSnapshot` exposes the validated
`BadgeDefinitions`, `RendererOutputPolicy`, and the secret-free
`RendererConfigurationFingerprint`; it remains immutable and secret-free, and
`From` keeps working for callers/tests that build from a plain configuration.

`RendererConfigurationFingerprint.Compute` is a deterministic uppercase SHA-256
over the renderer configuration schema version, the ordered selector
enablement/templates, and the effective palette. It excludes credentials, the
webhook secret, timestamps, and correlation identifiers by construction, so
rotating a secret cannot change it; equivalent color casing and selector entry
order are normalized. The value is the one supplied to
`RenderRequest.ConfigurationFingerprint`. The ADR-005 secret boundary is
unchanged: no credential or webhook secret enters the renderer configuration,
the snapshot renderer fields, or the fingerprint. `ConfigurationSnapshotService`
`TryReplace` still activates a candidate only when it validates, so an invalid
renderer configuration leaves the last valid public snapshot (and private
secrets) active.

`tests/ArrTags.Tests/RendererConfigurationTests.cs` adds 21 unguarded cases
covering the default-to-ADR-009 mapping, valid/invalid selector and template
configuration, palette acceptance and canonicalization, malformed colors, the
4.5:1 contrast boundary (`#767676`/`#FFFFFF` accepted, `#777777`/`#FFFFFF`
rejected, `#757575`/`#000000` accepted, `#747474`/`#000000` rejected),
snapshot immutability and secret exclusion, fingerprint determinism and
sensitivity to every output-affecting value, XML round-trip persistence, and
last-valid retention plus private-secret retention through
`ConfigurationSnapshotService.TryReplace`. No Skia native runtime is required.
Build and test pass with 0 warnings and 575 passing tests plus 21
environment-guarded Skia skips (596 total); the forced native run is unchanged.
The renderer configuration is not wired to the Jellyfin admin save surface, DI,
providers, or the artwork pipeline (not a Phase 4 prerequisite), and the
golden/cross-runtime determinism tests were completed in task 4.11.

**Task 4.11 status:** Complete. The ADR-010 test oracle is in place: committed,
repository-owned synthetic decoded-pixel goldens for the full fixture list with
no auto-approval or writer path; byte-determinism across repeated renders, item
identity, observation timestamp, stream chunking, and a separate fresh process;
PNG-contract tests for non-interlaced 8-bit RGB/RGBA, the fixed `sRGB`
declaration, stripped metadata, canonical transparent-pixel RGB, straight
source-alpha preservation, and malformed/unsupported embedded color-profile
rejection; and an ADR-010 cross-runtime tolerance comparator with unguarded
boundary unit tests and an exact canonical-runtime run. New unguarded files are
`CrossRuntimePixelComparator`, `CrossRuntimePixelComparatorTests`,
`RenderGoldenFixtures`, `RenderGoldenTests`, `RenderImageFixtures`,
`RenderPngContractTests`, `RendererCrossRuntimeTests`,
`RenderDeterminismTests`, `RenderDeterminismProcessProbe`,
`RenderOrientationTests`, `SkiaNativeTheoryAttribute`, and
`NonCanonicalRuntimeFactAttribute`; `tests/ArrTags.Tests/Goldens/` holds the nine
committed PNGs and their manifest. Default `./build.sh test` passes 593 tests
with 40 environment-guarded skips (633 total); the forced native run with
`ARRTAGS_SKIA_COMPAT=1` and the pinned sysroot passes 655 tests with one
non-canonical-golden skip (656 total). The suite fails closed on a mutated
golden (verified separately by substituting a different committed PNG and by
corrupting a manifest fingerprint) and there is no test path that writes or
regenerates a golden.

The F2 supporting production change deferred from task 4.9 is implemented:
`SourceColorProfile`/`SourceColorProfileKind` inspect a recognized PNG `iCCP` or
JPEG `APP2` embedded ICC profile, treat an input without a profile as sRGB, and
fail closed with the new safe `RenderFailureReason.UnsupportedColorProfile` when
an embedded profile cannot be parsed by `SKColorSpace.CreateIcc`; a supported
profile is converted to sRGB by the existing sRGB decode destination.
`RenderPngContractTests` asserts both rejection and conversion. A supported or
absent profile renders byte-identical output and an invalid profile now produces
no artifact, so the F2 change alone did not require a version bump.

The authorized task 4.11 correctness fix corrects the pre-existing EXIF
dimension-swapping orientation defect. `SkiaOrientation.Apply` had used the
*oriented* `height`/`width` as the translation origin for EXIF orientations 5-8
instead of the source dimensions, so an opaque 500x750 JPEG with orientation 6
rendered to 750x500 with 125,000 fully transparent pixels and clipped content,
orientation 7 left 250,000 transparent, and orientation 8 left 187,500
transparent. The transforms now translate about `source.Height`/`source.Width`;
`RenderOrientationTests` proves all eight orientations are fully opaque and place
two distinct corner markers in the expected quadrants, and the committed
`orientation` golden now encodes the corrected dimension-swapping orientation 6.
Because this is an output-affecting drawing change, `RenderVersion` was bumped
from 1 to 2 and the committed golden manifest was regenerated; the badge schema
version is unchanged. The orientation golden's decoded pixels are unchanged in
shape and the other eight fixtures stay byte-identical while all output
fingerprints advance with the renderer version.

The non-canonical cross-runtime half could not be executed: only the pinned
canonical runtime (and the identical Jellyfin host runtime) exists in this
environment, so the ADR-010 0.1 percent anti-aliased-text tolerance is
implemented and unit-tested and the exact canonical half runs against every
golden, but no second runtime is available. The comparison is data-driven: an
optional `Goldens/non-canonical/` set (same manifest shape) runs through the
tolerant comparator when supplied, and is reported as a skipped
`NonCanonicalRuntimeFact` while absent. The recommended resolution is to select
and record the second explicitly supported non-canonical Linux runtime and
produce its golden set in the testing/release milestone; this remaining
environment limitation is recorded in `docs/implementation-readiness.md`.

**ADR-010 implementation tasks:** ADR-010 adds the renderer library, bundled
font, PNG/alpha/color, service-contract, configuration, and test-oracle work
listed above. These are Phase 4 implementation tasks because they are entirely
renderer-local and testable without Jellyfin artwork publication. The
renderer-side `SourceImageInput` contract is Phase 4; the Jellyfin host adapter
that reads the unindexed `Primary` source image and supplies its bytes remains
Phase 5 source-capture work. Publication, provenance, restoration, caching,
stale-artwork lifecycle, and Enhanced coexistence are not pulled into Phase 4.

**Acceptance criteria:**

- [x] The same source image, canonical metadata, configuration, request
  parameters, and renderer version produce the same logical output.
- [x] The renderer never claims a value based only on missing provider data.
- [x] Quality badges show actual observed quality, not requested quality policy.
- [x] Original source bytes are not modified by rendering.
- [x] Every rendering failure leaves the current usable artwork unchanged and
  records a bounded, non-secret diagnostic.

**Gate 4:** Met. Renderer unit tests pass for normal, unknown, oversized,
malformed, cancelled, and failed inputs; the forced native run with the pinned
SkiaSharp runtime passes 655 tests (one deferred non-canonical cross-runtime
skip). At the time Gate 4 was met, Phase 5 had not started and required explicit
user approval; Phase 5 task 5.1 has since begun under that approval.

### 5. Jellyfin artwork integration

**Objective:** Publish derived badge artwork through supported Jellyfin 12
item-image APIs while preserving original source artwork through plugin-owned
provenance and coexisting with Jellyfin Enhanced.

**Deliverables:**

- Version-pinned publication spike covering the selected item/image types,
  source-artwork capture, supported image publication, and standard image-route
  delivery.
- Artwork publisher using Jellyfin's supported item-image APIs; no MVC filter or
  middleware response interception.
- Eligibility checks for item type, image type, library scope, configuration,
  match state, and current metadata.
- Correct publication and restoration behavior, with standard Jellyfin image
  tags, authorization, cache headers, conditional requests, and client delivery
  verified after publication.
- Crash-recoverable artwork operations with durable staged artifacts, write-ahead
  publication intent, postcondition reconciliation, and guarded lifecycle
  handling for disable, uninstall, and item removal.
- Jellyfin Enhanced coexistence policy and tests, including spoiler/hidden image
  behavior. The policy is resolved by ADR-011: no automatic duplicate/overlap
  suppression, no Enhanced-internals dependency, and no special spoiler/hidden
  handling.

**Tasks:**

- [x] 5.1 Confirm the exact supported Jellyfin item-image publication ABI and
  route variants.
- [x] 5.2 Implement source-artwork provenance and guarded restoration state
  before publishing derived artwork.
- [x] 5.3 Implement the Jellyfin host source adapter that reads the unindexed
  `Primary` source image and supplies the Phase 4 renderer's `SourceImageInput`
  bytes, content type, dimensions, and hash; keep Jellyfin access out of the
  renderer (ADR-010).
- [x] 5.4 Extend plugin packaging so the renderer's managed dependencies, Linux
  native assets, dependency manifest, and Skia/font license notices are included
  in the plugin zip and resolve under the host's plugin load context; the
  current `PackagePlugin` target copies only the main assembly (ADR-010).
- [x] 5.5 Publish completed artwork through Jellyfin's supported item-image APIs;
  do not write media-folder posters or Jellyfin's image cache directly.
- [x] 5.6 Persist a durable `ArtworkOperation` before `SaveImage`, including
  before and candidate-after identities, artifact references, generation, and
  ownership/publication tokens.
- [x] 5.7 Reconcile uncertain `SaveImage`, item update, and provenance
  persistence outcomes by postcondition; never blindly replay or delete an
  active artifact.
- [x] 5.8 Preserve the current usable artwork when source capture or rendering
  cannot safely complete.
- [x] 5.9 Fence and drain publication operations during disable/uninstall, and
  tombstone confirmed item removal without issuing image mutations.
- [x] 5.10 Add the configured disable/limit policy for duplicate or overlapping
  badges; do not depend on Jellyfin Enhanced internals. Resolved by ADR-011: no
  automatic duplicate/overlap suppression and no Enhanced-internals dependency;
  user control is through the existing poster and selector enable flags, and
  Spoiler Guard renders normally.
- [x] 5.11 Test Web and image-consuming clients through the supported server
  image response path.

**Authoritative Phase 5 execution order:** 5.1, 5.2, 5.3, 5.4, 5.6, 5.5, 5.7,
5.8, 5.9, 5.10, 5.11. Task IDs are stable references only; this execution order
is the canonical sequence. The order is derived from the documented dependencies,
not from task numbering:

- 5.1 has no prerequisites and confirms the item-image publication ABI and route
  variants that the ABI-dependent tasks rely on.
- 5.2 precedes 5.5 because source-artwork provenance and guarded restoration
  state must exist before derived artwork is published.
- 5.3 precedes 5.5 because the publisher consumes the `SourceImageInput` captured
  by the host source adapter.
- 5.4 precedes 5.5 because the renderer's managed dependencies, native assets,
  dependency manifest, and license notices must resolve under the host plugin
  load context before publication is exercised on a host.
- 5.6 precedes 5.5 because the durable `ArtworkOperation` must be persisted
  before `SaveImage`.
- 5.7 depends on 5.5 and 5.6; 5.8 depends on 5.3 and 5.5; 5.9 depends on 5.5,
  5.6, and 5.7; 5.10 depends on 5.5 and the DG-8 Enhanced coexistence decision;
  5.11 depends on 5.1 and 5.5.

**Task 5.1 status:** Complete. The exact supported Jellyfin 12.0.0 item-image
publication/read ABI, the standard `ImageController` route variants, the
read/write authorization split, and Jellyfin's ownership of image tags, caching,
and resizing are pinned with evidence tied to the pinned artifacts and recorded
in `docs/research/jellyfin-12-architecture.md` section 4.4 (with the route table
in section 3.1 and the response/authorization behavior in section 3.3). The
publication surface is
`MediaBrowser.Controller.Providers.IProviderManager.SaveImage(BaseItem, Stream,
string, ImageType, int?, CancellationToken)` plus its URL and path overloads; the
read surface is `BaseItem.GetImageInfo`/`ImageInfos`, `ItemImageInfo`,
`ImageInfo`, `IImageProcessor.GetImageCacheTag`/`GetImageDimensions`, and
`ILibraryManager.UpdateImagesAsync`/`ConvertImageToLocal`; the repository update
is `BaseItem.UpdateToRepositoryAsync(ItemUpdateType.ImageUpdate, ...)`. The
confirmed routes are `Items/{itemId}/Images/{imageType}` (GET/HEAD),
`Items/{itemId}/Images/{imageType}/{imageIndex}` (GET/HEAD),
`Items/{itemId}/Images/{imageType}/{imageIndex}/{tag}/{format}/{maxWidth}/{maxHeight}/{percentPlayed}/{unplayedCount}`
(GET/HEAD), `Items/{itemId}/Images` (GET), and the POST/DELETE write routes. The
GET/HEAD item-image read actions carry no `[Authorize]`; they resolve the item
through `GetItemById<BaseItem>(itemId, User.GetUserId())`, where an anonymous
user id is empty and maps to a null user, so a known item's image is served
anonymously (HTTP 200) and only an unknown item id or missing image yields 404.
The item-image information action and all write actions require authorization,
and the pinned host OpenAPI shows `security=null` for the GET/HEAD item-image
operations. Later Phase 5 tasks must not assume a 401 from the read image route
or treat it as an authorization boundary. A new unguarded ABI test
(`JellyfinImageAbiTests`, 11 cases) reflects the pinned `12.0.0` NuGet
assemblies, and a new host-guarded route/authorization test
(`JellyfinImageRouteTests`, 5 cases) reflects the pinned host `Jellyfin.Api.dll`
via `ARRTAGS_JELLYFIN_HOST_DIR`. The host-guarded route confirmation was
executed against `/tmp/opencode/jf/jellyfin` (5/5 passed, alongside the 11/11
ABI cases) and cross-checked against the live pinned host's generated OpenAPI
document. V1 remains the
unindexed `Primary` poster for Movie and Episode only (ADR-006/ADR-009);
indexed/alternate poster surfaces are out of V1. The ABI is confirmed; the exact
on-disk representation, post-publication read-back, multi-RID packaging, and
end-to-end standard-route delivery remain later Phase 5 validation, not ABI
uncertainty.

**Task 5.2 status:** Complete. The provider-neutral, plugin-owned source-artwork
provenance and guarded restoration state model is implemented in
`src/ArrTags/Artwork`. `ArtworkImageSurface` is the canonical image-surface
identity (V1 unindexed `Primary`; the optional index is retained for the data
model and the state invariant rejects an indexed surface as out of V1 scope).
`ActiveImageIdentity` records the observable surface/presence, content SHA-256,
byte length, dimensions, modification time, and Jellyfin image tag, with an
explicit absent baseline and no path. `ArtworkOwnershipComparer` implements the
fail-closed ADR-002 rule: surface and presence must match, a present identity
requires a matching content hash, and every recorded Jellyfin value must still
match when observable; a missing hash or unavailable observation yields
`Unknown`, never ownership. `PublishedArtworkState` carries every data-model
3.10.1 field and state, and `Validate` enforces the documented invariants (a
complete publication set, a present active identity with a hash, a retained
source artifact plus fingerprint for a present baseline, opaque bounded tokens,
and model-version compatibility). `PublishedArtworkStateTransitions` implements
every section 3.10.2 guarded logical transition as pure decisions and commits
(new session capture, repeated publication that reuses the source artifact and
ownership token while issuing a new publication token and active identity,
`OwnershipLost`/`OwnershipUnknown`, `RestorePending`, `Restored`,
`RestoreBlocked`, `Removed`, and an observation refresh); it performs no image
mutation and refuses to re-baseline a blocked record. `SourceArtifactStore`
provides a content-addressed immutable store under the plugin data folder that
records the exact bytes plus MIME type, length, and SHA-256, atomically promotes
after bounded size/MIME/magic-byte/hash validation, validates integrity on read,
is traversal-safe, rejects new work when the authoritative storage quota would be
exceeded, and persists its manifest through the authoritative state boundary so
provenance is never evicted as ordinary cache. `PublishedArtworkStateStore`
persists the state through the existing versioned, integrity-tagged,
atomically-written boundary, enforcing the invariants on write and quarantining a
valid-envelope-but-invalid record rather than replaying it. `StateRepository`
gained a read-only `Limits` accessor so the store shares the configured bounds.
The durable `ArtworkOperation` journal (5.6), the Jellyfin host source adapter
(5.3), publication (5.5), reconciliation (5.7), and lifecycle fencing (5.9)
remain later tasks. New tests (`ArtworkProvenanceTests`,
`SourceArtifactStoreTests`, `PublishedArtworkStateStoreTests`, 75 cases) cover
the comparison match/mismatch/unknown/surface/presence/missing-hash rules, the
state invariants, every transition, artifact round-trip/integrity/traversal/
atomic-promotion/no-cache-eviction behavior, and authoritative persistence and
quarantine. No ADR, `RenderVersion`, renderer behavior, or existing passing
behavior was changed.

**Task 5.3 status:** Complete. The plugin-owned, host-neutral Jellyfin host
source adapter is implemented in `src/ArrTags/Artwork`. `IArtworkSourceReader`
returns an `ArtworkSourceReadResult` that carries presence, the exact bounded
source bytes, the confined content type, byte length, content SHA-256, the
post-orientation display dimensions, and the Jellyfin identity fields, and it
can construct both a valid `SourceImageInput` and the task 5.2
`ActiveImageIdentity` from one read. `ArtworkSourceReader` is the host-neutral
core: it enforces the V1 unindexed `Primary` surface, the `OperationalLimits`
source byte and decoded dimension bounds, and the container confinement, and it
maps every failure to a bounded reason without throwing. `JellyfinArtworkImageAccess`
is the Jellyfin 12.0.0 implementation: it resolves the item through
`ILibraryManager`, reads `BaseItem.GetImageInfo(ImageType.Primary, 0)`, converts a
non-local image to a local file through the supported `ConvertImageToLocal`
API when required, reads a bounded byte copy, and observes the image tag and
modification time; all `MediaBrowser.*` references are confined to that file and
`src/ArrTags/Rendering` remains Jellyfin-free. Oriented dimension handling is
pinned: Jellyfin reports the pre-EXIF-orientation encoded dimensions
(`SkiaEncoder.GetImageSize` returns `SKCodec.Info`), so `SourceImageDescriptor`
derives the true display dimensions from the exact bytes with the pinned
SkiaSharp 3.119.4 codec and `SourceOrientationExtensions`, guaranteeing parity
with the renderer's own `EncodedOrigin` validation. Content-type confinement
closes the carried-forward Phase 4 MEDIUM finding: only `image/png` and
`image/jpeg` signatures are accepted and GIF, WebP, BMP, AVIF, and unrecognized
bytes fail closed, so a malformed profile in an uninspected container can never
be treated as sRGB; the renderer's `SourceColorProfile` was not modified.
`IArtworkImageAccess` is the injectable host seam that makes the adapter testable
without a live host. New tests (`ArtworkSourceReaderTests`,
`ArtworkSourceContentTypeTests`, `JellyfinArtworkImageAccessTests`,
`SourceImageDescriptorTests`, 65 cases with four native-guarded) cover present,
absent, unsupported-surface, unsupported-container, oversized-byte,
oversized-dimension, unreadable, hash/length/surface correctness, oriented
dimensions, supported non-local conversion, and boundary-neutrality checks. The
default suite passes 740 with 49 guarded skips (789 total) and the forced native
suite passes 813 with 6 skips (819 total), with 0 warnings and no regressions. No
ADR, `RenderVersion`, renderer behavior, or existing passing behavior was changed.

**Task 5.4 status:** Complete. The plugin package now ships the renderer's
runtime closure at the plugin folder root, and both the live pinned host and a
replicated plugin load context confirm it resolves under the host's plugin load
context. `src/ArrTags/ArrTags.csproj`'s `PackagePlugin` target takes the managed
`SkiaSharp.dll` and the matching `linux-x64` `libSkiaSharp.so` from the project's
MSBuild-resolved runtime assets (`RuntimeCopyLocalItems` and
`RuntimeTargetsCopyLocalItems`) rather than a hard-coded NuGet cache path, stages
them next to `ArrTags.dll` along with `ArrTags.deps.json`, `build.yaml`,
`THIRD-PARTY-NOTICES.md`, and the `licenses/` notices, and fails the build when
either renderer asset is not resolved. The `linux-x64` RID is declared by
`PluginRuntimeIdentifier` (the measured layout: the Jellyfin 12 plugin load
context probes only the plugin folder root, not `x64/` or
`runtimes/<rid>/native/`). `build.yaml`'s `artifacts` now lists `ArrTags.dll`,
`SkiaSharp.dll`, `libSkiaSharp.so`, and `ArrTags.deps.json`; the identity,
`targetAbi: 12.0.0.0`, and `framework: net10.0` are unchanged, and `assemblies`
is left empty so Jellyfin's folder scan loads the bundled `SkiaSharp.dll`.
V1 claims only `linux-x64`; other Linux RIDs are not claimed and no arbitrary
system Skia library is loaded. Multi-RID packaging is not part of V1. The
package is verified by `tests/ArrTags.Tests/PluginPackagingTests.cs` (five
unguarded contract facts plus three package-content facts guarded on the
presence of `artifacts/ArrTags_*.zip`) and by manual inspection of
`./build.sh package` output. Live-host validation installed the package on the
pinned Jellyfin 12.0.0 host: the log records loading `SkiaSharp, Version=3.119.0.0`
from the plugin folder and `Loaded plugin: "ArrTags" "0.1.0.0"`, startup
completed, `/proc/<pid>/maps` maps `ArrTags.dll` and `SkiaSharp.dll` from the
plugin folder, and the host wrote `meta.json` with `targetAbi: 12.0.0.0` and
`status: Active`. A replicated `PluginLoadContext` run over the exact extracted
package, with the host's managed and native SkiaSharp preloaded, bound the
plugin-context `SkiaSharp` to the plugin folder, completed a native
decode/draw/encode call, and mapped the plugin-folder `libSkiaSharp.so` as a
second copy. A full image render through Jellyfin is not wired until tasks
5.5/5.11, so that path is not claimed. Default `./build.sh test` passes 745 with
52 guarded skips (797 total; 748/49/797 when the package is present) and the
forced native run passes 821 with 6 skips (827 total), with 0 warnings and no
regressions. No ADR, `RenderVersion`, renderer behavior, or existing passing
behavior was changed.

**Task 5.6 status:** Complete. The durable, provider-neutral `ArtworkOperation`
write-ahead record, its phase/lifecycle-fence rules, and its authoritative store
are implemented in `src/ArrTags/Artwork`, following ADR-003, `docs/data-model.md`
sections 3.10.3 and 3.10.4, and `docs/architecture.md` section 9. The model
carries every documented field (`modelVersion`, `operationId`, `kind`,
`jellyfinItemId`, `imageSurface`, `generation`, `ownershipToken`,
`priorPublicationToken`, `publicationToken`, `expectedBeforeIdentity`,
`candidateAfterContentSha256`, `observedAfterIdentity`, `sourceArtifactId`,
`derivedArtifactId`, `phase`, `lifecycleFence`, `attempt`, `lastError`,
`createdAt`/`updatedAt`) plus explicit `sourcePresence` and
`candidateAfterPresence` fields that make the documented conditional
requirements enforceable, and `Validate` enforces every conditional rule (a
valid item/surface/operation id; a publication requires a publication token and
a derived artifact; a restoration requires a source reference; a present after
target requires its content hash; a present source requires a content-addressed
source artifact; generation and attempt are bounded; tokens are opaque and
bounded; no path or credential field). `ArtworkOperationPhase` matches the
data-model table exactly and is documented as a durable lower-bound marker;
`ArtworkOperationPhases` is the pure phase-advance helper (stepwise forward order,
terminal outcomes from any non-terminal phase, no backward or past-terminal
advance), and `ArtworkOperationFencing` provides the pure generation and
lifecycle-fence decisions (`AllowsNewPublication`, `AllowsNewRestoration`,
`IsStale`, `CanSupersede`, `IsSameGeneration`) without any lifecycle event
wiring. `ArtworkOperationErrors` redacts control characters and bounds
`lastError`. `ArtworkOperationStore` persists through the existing versioned,
integrity-tagged, atomically-written `StateRepository`/`StateAuthority.Authoritative`
boundary keyed per item/image surface, validates on write, quarantines a
valid-envelope-but-invalid payload rather than replaying it, fences writes by the
monotonic generation (a stale generation cannot overwrite a newer durable
record; a newer generation supersedes; the same generation may only advance the
same operation through a legal phase), and marks only terminal phases
(`Committed`, `Aborted`, or `RecoveryBlocked`) eligible for terminal-provenance
retention so a non-terminal operation is never pruned. A tombstoned item-removal
operation is terminal because its phase is `Aborted`, not because of its
lifecycle fence. The store performs no
image mutation and does not call `SaveImage`; task 5.5 drives the ordering.
`docs/data-model.md` section 3.10.3 records the two explicit presence fields. New
tests (`ArtworkOperationTests`, `ArtworkOperationStoreTests`, 101 cases) cover
every conditional requirement, absent-versus-present after target, token bounds,
`lastError` redaction, the phase enum and every legal/illegal transition, the
fence decisions, durability across a reconstructed `StateRepository`, generation
fencing, one-non-terminal-per-subject, authoritative quarantine on a corrupt or
semantically invalid record, and terminal-versus-non-terminal retention. Default
`./build.sh test` passes 849 with 49 guarded skips (898 total) and 0 warnings; no
ADR, `RenderVersion`, renderer behavior, or existing passing behavior was
changed.

**Task 5.5 status:** Complete. The provider-neutral single-subject publication
orchestration and the single Jellyfin image-mutation implementation are
implemented in `src/ArrTags/Artwork`, driving the authoritative durable
publication protocol (`docs/architecture.md` section 9 steps 4-10; ADR-002,
ADR-003) through the confirmed Jellyfin 12.0.0 ABI. `IArtworkImageWriter` is the
host-neutral mutation boundary and `JellyfinArtworkImageWriter` is its only
implementation: it resolves the item through `ILibraryManager`, calls the
supported `IProviderManager.SaveImage(BaseItem, Stream, string, ImageType, int?,
CancellationToken)` stream overload with the durable derived bytes and
`image/png`, and then calls `BaseItem.UpdateToRepositoryAsync(ItemUpdateType.ImageUpdate,
...)`, which is the same supported flow the standard item-image controller uses.
It never writes a media-folder poster, Jellyfin's image cache, or an item-image
path directly, never uses the filesystem-path overload that deletes its source,
and never publishes derived bytes through a URL overload; all `MediaBrowser.*`
references for publication are confined to that one file and
`src/ArrTags/Rendering` stays Jellyfin-free. `ArtworkPublisher` is the
host-neutral orchestration: it reads the authoritative `PublishedArtworkState`,
observes and confines the current source through `IArtworkSourceReader`, retains
a present baseline (or records an explicit absent baseline) in the task 5.2
`SourceArtifactStore`, promotes the validated render output to a durable derived
artifact, builds the exact `expectedBeforeIdentity` from a fresh source read and
the `candidateAfterContentSha256` from the derived artifact, and writes a
`Prepared` `ArtworkOperation` before any external image mutation. It re-reads and
revalidates the before identity immediately before mutation; a mismatch or an
unobservable identity never calls `SaveImage`, and the operation is marked
`Aborted`. When a previously published session exists, the failed check also
persists the ownership outcome (`OwnershipLost`/`OwnershipUnknown`); when no prior
publication session exists (an initial capture), it persists no
`PublishedArtworkState`. It durably advances through
`MutationStarted`, `RepositoryUpdateStarted`, `VerificationPending`, and
`FinalizationPending` before each external step; and it commits the final
`PublishedArtworkState` (new publication token, active identity, state revision,
and operation id while retaining the source artifact and ownership token) before
marking the operation `Committed`. Any failure or uncertainty leaves the current
artwork unchanged, records a bounded non-secret diagnostic, and never marks the
operation committed; the recovery decision table itself remains task 5.7, and a
pre-existing non-terminal or recovery-blocked operation blocks new work. Repeat publication verifies
the prior publication is still active, reuses the first source artifact and
ownership token, and issues a new publication token, so an ArrTags output is
never captured as a new source. The publisher is registered with the existing
lazy DI pattern (no startup work) as a single entry point Phase 6 can drive; no
event, queue, or library-scan wiring is added. New tests
(`ArtworkPublisherTests`, `JellyfinArtworkImageWriterTests`, 23 cases) cover the
happy path with durable phase ordering, absent baseline, before-identity mismatch
and unobservable-before aborts with no `SaveImage` call, readback mismatch and
item-update failure not committing, repeat publication source/ownership-token
reuse with a new publication token, ownership-lost and stale-baseline blocking,
non-terminal-operation blocking, bounded cancellation, derived-artifact and
derived-hash rejection, state path/secret hygiene, the stream overload being used
instead of the deleting path or URL overload, the normal image update flow, and
boundary-neutrality. Default `./build.sh test` passes 872 with 49 guarded skips
(921 total) and 0 warnings, exactly +23 over the task 5.6 baseline; no ADR,
`RenderVersion`, renderer behavior, or existing passing behavior was changed.

**Task 5.7 status:** Complete. The provider-neutral, postcondition-based
reconciliation service is implemented in `src/ArrTags/Artwork` following
ADR-003, `docs/data-model.md` section 3.10.4, and `docs/architecture.md`
section 9 ("Crash-consistent publication and recovery" and "Restart
reconciliation"). `ArtworkRecoveryDecisions` is the pure implementation of the
3.10.4 decision table: it returns a bounded action (`NothingToReconcile`,
`Resume`, `AbortFenced`, `CompleteAfter`, `FinalStateDurable`, `OwnershipLost`,
`OwnershipUnknown`, `ItemRemoved`, or `RecoveryBlocked`) from the durable
operation, the associated `PublishedArtworkState`, a fresh active-image
observation, and item absence, and it performs no I/O or image mutation.
`ArtworkReconciler` is the registered invocable boundary: it serializes with
normal publication through the shared `ArtworkSubjectGate`, reads the
authoritative state and durable operation (quarantining an invalid record),
re-observes the item and active image, distinguishes a confirmed missing item
from an unobservable image, and delegates execution to the publisher's
deterministic protocol. `ArtworkPublisher` now exposes an internal recoverable
execution path that the normal publication route and reconciliation share, so
the supported `SaveImage`, the durable phase ordering, the readback, and the
final-state commit are never reimplemented. A before-identity match resumes or
retries the same deterministic operation only when the generation and lifecycle
fence permit it and never recaptures a source; the publisher reads the retained
derived artifact instead of the active image. An after-identity match ensures the
normal item update is persisted and commits the intended final state. An
observable mismatch records `OwnershipLost` and aborts; an unobservable image
records `OwnershipUnknown` and leaves the image untouched; a confirmed missing
item writes an `ItemRemoved` tombstone and performs no image mutation; a durable
final state completes the journal without a further mutation; and an invalid
operation, state, or required artifact enters `RecoveryBlocked` with no replay or
cleanup. Reconciliation performs no artifact deletion, so an artifact that is not
proven non-active is retained. The publisher records the logical publication
fingerprint and renderer version on a publication operation (`ArtworkOperation`
gained the optional `candidatePublicationFingerprint` and `rendererVersion`
fields, documented in data-model 3.10.3) so an after-match recovery can commit the
target state without re-rendering. `ArtworkReconciler` is registered with the
existing lazy DI pattern and adds no startup work; the event/queue/startup-scan
wiring belongs to Phase 6, and at the time of task 5.7 the guarded restoration
mutation remained the lifecycle task 5.9 (task 5.9 now executes a restoration
resume in the publisher instead of reporting `Deferred`). New tests
(`ArtworkReconcilerTests`, 24 cases) cover every branch of the
decision table, the lifecycle-fence abort, before-identity revalidation on
resume, source-artifact retention (no recapture), derived/source artifact
corruption entering `RecoveryBlocked`, the item-absent tombstone with no
mutation, the durable-final-state completion, resolution of a previously
`RecoveryBlocked` operation, cancellation, containment of a reader exception,
the DI registration, and boundary-neutrality. Default `./build.sh test` passes
896 with 49 guarded skips (945 total) and 0 warnings, exactly +24 over the task
5.5 baseline; no ADR, `RenderVersion`, renderer behavior, or existing passing
behavior was changed.

**Task 5.8 status:** Complete. The provider-neutral single-subject artwork
generation coordination is implemented in `src/ArrTags/Artwork` following
`docs/architecture.md` section 9 (publication flow and source consistency) and
`docs/data-model.md` sections 3.7-3.8. `ArtworkGenerationRequest` carries the
canonical caller inputs (Jellyfin item id, V1 surface, `MediaIdentity`,
`MediaMatch`, optional `BadgeMetadata`, the ordered `BadgeDefinition` snapshot,
the secret-free configuration fingerprint, the effective `RenderOutputPolicy`,
the `OperationalLimits`, and the renderer/badge schema versions) with no source
bytes, artifact, provider DTO, path, entity, or credential.
`ArtworkGenerationCoordinator` observes the current source through
`IArtworkSourceReader`, builds the `SourceImageInput` and `RenderRequest`, calls
`IRenderer.RenderAsync`, and calls `ArtworkPublisher.PublishAsync` only when the
render is `Rendered`. An absent source returns a no-op; a failed source read
(unavailable, unsupported, oversized, or oversized-dimension), a render
pass-through (including missing metadata and an ineligible match), and a failed
render preserve the current artwork and perform no image mutation; the renderer
is not invoked for an absent or failed source and the publisher is not invoked
unless the render is complete. Missing metadata and an ineligible match rely on
the existing ADR-009/ADR-010 renderer pass-through convention rather than a new
policy. `ArtworkGenerationOutcome` and the bounded `ArtworkGenerationResult`
distinguish published, no-source/absent, source-unavailable, render
pass-through, render-failed, publication-not-completed, blocked, and cancelled,
carrying only the safe source/render/publication classification and never a
secret, path, entity, source bytes, or artifact. Cancellation is honored and no
exception escapes. For source consistency, the exact source observation used for
the render is supplied to the publisher's new-session capture through a new
additive plugin-internal `ArtworkPublisher.PublishAsync` overload, so the
retained provenance baseline and the derived artifact describe the same source;
the public overload and the before-mutation revalidation are unchanged, so a
source that changes after the observation still prevents the mutation. The render
source is the observed active surface; selecting the retained original artifact
as the render source for a repeat publication while an ArrTags session is already
owned (publication-flow step 4) is an explicit boundary of this task and belongs
to the Phase 6 pipeline.
`ArrTagsServiceRegistrator` registers `IRenderer` (`SkiaBadgeRenderer`) and
`ArtworkGenerationCoordinator` with the existing lazy factory pattern and no
startup work; no event, queue, or library-scan wiring is added. New tests
(`ArtworkGenerationCoordinatorTests`, 23 cases) cover the published path; absent
source with skipped renderer/publisher; failed source reads for the unreadable,
unsupported, oversized-byte, and oversized-dimension classifications; render
pass-through and failure; the real renderer's null-metadata and ineligible-match
pass-through conventions; renderer exception containment; publication failure,
blocked publication, and publication cancellation; cancellation before start and
during the source read; a source change after the render observation aborting
without mutation; invalid bounded input; the shared source observation with the
retained provenance baseline; state/path hygiene; the provider-neutral boundary;
and DI registration, asserting zero image mutation on every preservation path. Default
`./build.sh test` passes 919 with 49 guarded skips (968 total) and 0 warnings,
exactly +23 over the task 5.7 baseline; no ADR, `RenderVersion`, renderer
behavior, or existing passing behavior was changed.

**Task 5.9 status:** Complete. The durable lifecycle fence, the disable/uninstall
drain and guarded restoration, the supported image-removal primitive, and
confirmed item-removal tombstoning are implemented in `src/ArrTags/Artwork` and
`src/ArrTags/PluginLifecycle`, following ADR-002/ADR-003, `docs/architecture.md`
section 9 ("Disable, uninstall, and item removal"), and `docs/data-model.md`
sections 3.10.1-3.10.4. `ArtworkLifecycleFenceRecord` is the versioned
authoritative record and `ArtworkLifecycleFenceStore` is its store: the active
`Normal`/`Disable`/`Uninstall`/`ItemRemoved` fence is integrity-tagged, atomically
written, and quarantined on corruption, a missing record is `Normal`, and an
invalid record fails closed. `StateRepository` gained a bounded, deterministic
`Enumerate` scan and `PluginStatePaths` gained a kind-directory accessor so the
coordinator can list durable operations and states without a Jellyfin item
listing. `ArtworkPublisher` reads the durable fence before accepting new work and
refuses it when the fence is not a valid `Normal`, and it records the fence on
the operation it creates. The publisher also gained a guarded restoration path
(`RestoreAsync`) that reuses the same durable `Prepared` ->
`MutationStarted` -> `RepositoryUpdateStarted` -> `VerificationPending` ->
`FinalizationPending` -> `Committed` ordering, the same immediate
before-mutation revalidation, and the same readback postcondition commit as
publication: a present retained source is written through the supported stream
`SaveImage` API, an absent baseline is removed through the new
`IArtworkImageWriter.RemoveImageAsync`, and a resulting surface that does not
match the retained baseline enters `RecoveryBlocked` and the state enters
`RestoreBlocked` without any further automatic mutation.
`JellyfinArtworkImageWriter.RemoveImageAsync` uses the supported
`BaseItem.DeleteImageAsync(ImageType, int)` flow (which removes the local image
file when the image is local, removes the image information, and persists the
normal `ItemUpdateType.ImageUpdate` repository update) and never deletes a media
file or image-cache entry directly; all `MediaBrowser.*` references remain
confined to that single implementation file.
`ArtworkRecoveryDecisions.Evaluate` gained an optional active-fence argument and
`ArtworkReconciler` a matching `ReconcileAsync` overload, so a disable/uninstall
drain aborts an in-flight prepared publication (`AbortFenced`) instead of
resuming it, and the publisher's recovery path now executes a restoration resume
instead of reporting `Deferred`. `ArtworkLifecycleCoordinator` implements the
host-neutral `IArtworkLifecycleCoordinator`: it records the durable fence,
reconciles every non-terminal operation to a terminal result before creating the
guarded restoration, restores every still-owned `Published` surface, leaves the
active image and all recovery records in place and reports `Incomplete` when a
restoration is blocked, externally changed, or uncertain, and tombstones a
confirmed item removal with no Jellyfin image call. The drain classifies each
reconciled operation by its durable phase, not by the transient reconciliation
outcome, so an operation that becomes `RecoveryBlocked` (including through an
`OwnershipUnknown` or non-throwing source-read failure) is never counted as
resolved and the lifecycle result is `Incomplete`. The item-removal result claims
a tombstone only after the reconciler actually aborted the in-flight operation; if the
reconciler cannot reach the `ItemRemoved` decision (for example an
invalid/quarantined state or operation) the result is `Blocked` and no tombstone
is claimed. An invalid/corrupt durable fence is preserved as fail-closed rather
than quarantined away or overwritten with `Normal`, so the publisher read path
stays closed until an explicit recovery decision. Cleanup is always deferred (no
source artifact or journal record is deleted eagerly).
`ArrTagsLifecycleService` now depends on `ILibraryEventSource` and
`IArtworkLifecycleCoordinator`: `StartAsync` clears a stale fence when the host
has loaded the plugin active, `StopAsync` performs a bounded
`DrainForHostShutdownAsync` (a plain shutdown resolves `Normal` and is a no-op)
and awaits tracked item-removal confirmations within the same bound, and the
previously no-op `ItemRemoved` hint now runs a tracked, bounded confirmation task
so synchronous library event delivery is never blocked. `IPluginLifecycleFenceProvider`
is the host-neutral fence-source boundary and `JellyfinPluginLifecycleState` is
its single Jellyfin implementation, which reads the plugin manifest status
through the supported `IPluginManager`; the host has no `OnDisable` hook, so the
supported disable trigger is the graceful shutdown of the still-loaded instance
(which can see the persisted `Disabled` manifest status), and a disable that is
only observed after the plugin is unloaded is not detectable. `Plugin.OnUninstalling`
runs a bounded synchronous drain because the pinned host deletes the plugin data
folder immediately after the hook returns; the hook records the `Uninstall`
fence and restores while the retained source and journal are still present, never
throws into the host, and leaves a blocked or uncertain restoration untouched.
All new services use the existing lazy DI factory pattern (no startup work).
`docs/architecture.md` section 9 documents the exact trigger mapping and the
undetectable-disabled-after-unload boundary. New tests (`ArtworkLifecycleTests`,
28 cases, plus focused additions to `ArtworkReconcilerTests`,
`ArtworkPublisherTests`, `LifecycleFoundationTests`, and `StateBoundaryTests`)
cover fence durability across a reconstructed `StateRepository`,
missing/invalid fences, the invalid-fence-not-overwritten-by-reset fail-closed
rule, publication refusal under every non-normal fence, the present- and
absent-baseline guarded restoration, reconcile-before-restore ordering,
blocked/externally-changed/uncertain retention and incomplete reporting
(including a non-throwing source-read failure that persists `RecoveryBlocked`),
source-artifact absence, bounded cancellation, the confirmed item-removal
tombstone with zero image calls, the not-confirmed, reader-failure, and
invalid-state-no-tombstone paths, the host status-to-fence mapping, the bounded
hosted shutdown drain, the `Plugin.OnUninstalling` lazy resolution and
failure-containment, bounded deterministic and traversal-safe
`StateRepository.Enumerate`, DI registration, and boundary-neutrality. Default
`./build.sh test` passes 964 with 49 guarded skips (1013 total) and 0 warnings,
exactly +45 over the task 5.8 baseline; no ADR, `RenderVersion`, renderer
behavior, or existing passing behavior was changed.

**Task 5.10 status:** Complete. Decision gate DG-8 is resolved by ADR-011, and
the Jellyfin Enhanced coexistence policy is recorded in `docs/architecture.md`
section 10. ArrTags adds no automatic duplicate-badge detection, overlap
suppression, or Enhanced-internals dependency: Jellyfin Enhanced chooses its own
overlay placement, so overlap handling is deferred to the user, and ArrTags badge
output is controlled only by the existing configuration (the
`BadgeMoviePosters`/`BadgeEpisodePosters` poster enable flags and
`RendererConfiguration.Selectors`). Spoiler Guard has no material effect on
ArrTags badge display, so ArrTags renders its derived badge normally and adds no
spoiler/hidden handling or Enhanced filter-ordering dependency. No production
behavior, `RenderVersion`, renderer behavior, or existing ADR was changed. New
tests in `tests/ArrTags.Tests/EnhancedCoexistenceTests.cs` (7 cases) assert that
the production assembly references no Enhanced assembly and declares no
Enhanced/spoiler/suppression type; that the policy surface and the
renderer/publication reason enums expose no Enhanced, spoiler, hidden, blur,
suppress, or overlap member; that Movie/Episode poster eligibility and renderer
badge selection vary only with the existing poster and selector flags; and that
the renderer decision is a pure function of the canonical render request with no
hidden global state. Default `./build.sh test` passes 971 with 49 guarded skips
(1020 total) and 0 warnings, exactly +7 over the task 5.9 baseline.

**Task 5.11 status:** Complete. The supported standard Jellyfin server image
response path that Web and other image-consuming clients use is exercised
in-process against the pinned 12.0.0 host; no parallel route or response
interception was added (ADR-001). A new host-guarded test file
`tests/ArrTags.Tests/JellyfinImageResponseTests.cs` (9 guarded cases plus 2
unguarded resize-contract cases) loads the pinned host `Jellyfin.Api.dll`,
constructs the real `Jellyfin.Api.Controllers.ImageController` with
`DispatchProxy` host doubles and a real `DefaultHttpContext`, and invokes the
actual `GetItemImage`, `GetItemImageByIndex`, and `GetItemImage2` actions. It
asserts the unindexed, indexed, and path-form `Primary` routes all deliver the
same server-rendered `PhysicalFileResult` that the route builds from
`IImageProcessor.ProcessImage` (path and content type); the quoted image-tag
`ETag`, `Cache-Control: public, max-age=31536000, immutable`, `Last-Modified`,
`Vary: Accept`, `Content-Disposition: attachment`, and DLNA headers; `304 Not
Modified` for a matching quoted or bare `If-None-Match` and for
`If-Modified-Since`; the `no-cache` revalidation
headers; the size/format plumbing into `ImageProcessingOptions`; and the `404`
pass-through for an unknown item and for an item with no image (with no processor
call). The pinned core `ImageHelper.GetNewImageSize` never-upscale clamp is
asserted unguarded. A publish-then-read-back case uses the real
`JellyfinArtworkImageWriter` with a provider double that mirrors the supported
`ImageSaver` write-and-update flow, then serves the item through the real
standard route and asserts the derived bytes (not the stale source) are
delivered and the original source file is untouched. The route templates and
read/write authorization split are independently pinned by the existing task 5.1
route/authorization tests over the same pinned `Jellyfin.Api.dll`; a live HTTP
round-trip against a running Jellyfin server was **not** performed for this task,
and the full ArrTags generation-to-publication pipeline could not be driven
end-to-end on a host because the Phase 6 queue/event wiring that triggers
generation does not exist yet. The default `./build.sh test` run (host guard
unset) passes 973 with 58 guarded skips (1031 total, exactly +2 over the task
5.10 baseline because the 9 new host-guarded cases skip); with
`ARRTAGS_JELLYFIN_HOST_DIR=/tmp/opencode/jf/jellyfin` the full suite passes 987
with 44 skips (1031 total, 0 failures), unskipping all 14 host-guarded image-route
cases. No production behavior, `RenderVersion`, renderer, ADR, configuration
schema, or existing passing test was changed.

**Acceptance criteria:**

- [x] A standard Jellyfin poster response contains the configured derived image
  when valid metadata exists. Demonstrated by `JellyfinImageResponseTests`
  (real host `ImageController` actions and the real ArrTags image-mutation
  boundary over the standard route); no live-host HTTP round-trip was performed.
- [x] Disabling ArrTags restores the original source when the active image is
  still ArrTags-owned. Demonstrated at the integration level by the task 5.9
  guarded-restoration cases (`ArtworkLifecycleTests`,
  `DisableDrainRestoresTheRetainedSourceForAPresentBaseline` and the
  absent-baseline, externally-changed, and unverifiable cases) and the task 5.9
  reconciler restore-resume case; no live-host disable/restore run was performed.
- [x] Original media files remain byte-for-byte untouched. Demonstrated by the
  task 5.11 publish-then-read-back case (the source file is retained
  byte-for-byte) and the task 5.5 image-writer tests
  (`SaveImageUsesTheStreamOverloadAndNeverThePathOrUrlOverload`).
- [x] Failed publication, missing metadata, and failed rendering preserve the
  current usable artwork. Demonstrated by the task 5.8 `ArtworkPublisherTests`
  and `ArtworkGenerationCoordinatorTests` containment cases.
- [x] Crashes before, during, and after `SaveImage` and item persistence recover
  to a committed publication, a safe abort, or an explicit recovery-blocked
  state without losing source provenance. Demonstrated by the task 5.6
  `ArtworkOperationStoreTests` and the task 5.7 `ArtworkReconcilerTests`
  resume/abort/recovery-blocked cases.
- [x] Restart reconciliation never overwrites an externally changed image and
  never treats an uncertain operation as proof of ownership. Demonstrated by
  the task 5.7 `ArtworkReconcilerTests`
  (`ExternalChangeRecordsOwnershipLostAndAbortsWithoutMutation`,
  `UnobservableIdentityRecordsOwnershipUnknownAndLeavesTheImageUntouched`).
- [x] Disable, uninstall, and confirmed item removal leave no untracked
  non-terminal operation or unsafe cleanup obligation. Demonstrated by the task
  5.9 `ArtworkLifecycleTests` drain/tombstone cases; no live-host
  disable/uninstall run was performed.
- [x] ArrTags does not interfere with Jellyfin Enhanced, including configured
  duplicate handling and Spoiler Guard expectations. Resolved by ADR-011 and
  covered by `EnhancedCoexistenceTests`: no automatic duplicate/overlap
  suppression, no Enhanced-internals dependency, and no special spoiler/hidden
  handling.

**Gate 5:** Met for the selected Jellyfin 12.0.0 ABI at the integration-test
level. Publication and standard image-route integration tests pass:
`JellyfinImageResponseTests` invokes the real pinned host `ImageController`
actions and response pipeline in-process (route variants, content type, served
bytes, tag/ETag/304, cache headers, `no-cache` revalidation, size/format
plumbing, 404 pass-through and the never-upscale clamp), and the existing task
5.1 `JellyfinImageRouteTests` pin the same host's route templates and
authorization attributes. Source preservation is demonstrated by the
publish-then-read-back case (the original source file is retained byte-for-byte
while the derived image is served by the standard route) and by the task 5.9
guarded-restoration/removal and task 5.7 reconciliation tests. The full ArrTags
generation-to-publication pipeline on a live host is not triggered because the
Phase 6 queue/event wiring that drives generation does not exist yet, and a live
HTTP round-trip against a running Jellyfin server was not performed; the
in-process case uses the real ArrTags writer and the real pinned controller, and
the remaining host-behavior validation is listed in
`docs/research/jellyfin-12-architecture.md` section 4.4.

### 6. Caching, updates & performance

**Objective:** Make metadata refresh and image generation asynchronous, bounded,
restart-safe, and efficient without allowing stale or partial work to become
authoritative.

**Deliverables:**

- Versioned metadata cache for last-known-good canonical snapshots and bounded
  artwork publication/provenance state, with optional bounded render work cache.
- Durable per-item/image-surface artwork-operation journal and staged-artifact
  manifests for publication and restoration recovery.
- Bounded, deduplicating, cancellation-aware work queue and hosted workers.
- Reconciliation from Jellyfin item events, post-scan work, scheduled/manual
  work, and authenticated Arr webhook hints.
- Fingerprint-driven invalidation for match, metadata, image source,
  configuration, renderer, and schema changes.
- Concrete defaults for queue, concurrency, retries, timeouts, image limits,
  cache TTL/size, and stale-data window.
- Safe metrics or status for queue depth, provider health, matching, cache,
  rendering, and stale data without secrets or unbounded payloads.

**Tasks:**

- [x] 6.1 Keep library event handlers short: validate relevance, enqueue a
  bounded hint, and return without external I/O or rendering.
- [x] 6.2 Implement coalescing by item and connection, single-flight work, worker
  cancellation, retry classification, and queue overflow behavior.
- [x] 6.3 Publish metadata state atomically only after current item/configuration
  validation; discard stale long-running work.
- [x] 6.4 Recover non-terminal artwork operations before accepting new work for
  the same item/image surface, using before/after identity postconditions and
  generation fences.
- [x] 6.5 Separate metadata freshness and bounded stale-last-known-good behavior
  from artwork retention and eviction.
- [x] 6.6 Regenerate and republish only affected artwork when a metadata
  fingerprint changes; invalidate relevant publication/work state on schema,
  renderer, or configuration changes.
- [x] 6.7 Validate and bound webhook authentication, content, rate, and work
  scope; treat webhooks as hints rather than source of truth.
- [x] 6.8 Test restart, shutdown, corruption, outage, recovery, duplicate events,
  queue pressure, and cancellation behavior.
- [x] 6.9 Add scheduled (periodic and manual) and post-scan reconciliation that
  enqueues the same bounded work hints, and enforce the configured provider and
  render concurrency limits at their boundaries (Phase 6 review HIGH and MEDIUM).

**Task 6.1 status:** Complete. The library-event entry boundary is implemented in
`src/ArrTags/PluginLifecycle` and `src/ArrTags/Updates`. `JellyfinLibraryEventSource`
maps a Jellyfin change event to a bounded `LibraryItemChangedEventArgs` (item id,
change reason, structural item type, and whether the change is an image-only
update) synchronously and in memory, with no provider, rendering, image, or
library read. `LibraryEventRelevance` validates relevance against the current
public configuration snapshot without I/O: an enabled Arr connection must exist,
an `ItemAdded`/`ItemUpdated` must be a V1 badge-bearing Movie or Episode, and
image-only updates (which include ArrTags' own `ItemUpdateType.ImageUpdate`
publication) are dropped. A relevant change becomes a bounded, provider-neutral
`LibraryWorkHint` carrying only the Jellyfin item id, the reason, and the safe
configuration generation; it never carries a credential, secret lease, provider
DTO, path, or unbounded payload. The hint is enqueued through the narrow
`IWorkHintSink` boundary, whose minimal `BoundedWorkHintSink` implementation is
thread-safe, bounded by the ADR-004 `QueueCapacity`, coalesces a redundant hint
for an item that already has pending work, drops overflow, and never performs
external I/O; it never throws into library-event delivery. `ArrTagsLifecycleService`
keeps every handler synchronous and preserves the task 5.9 `ItemRemoved` tracked,
bounded drain/tombstone behavior unchanged; it also emits a removal hint through
the same boundary. The full coalescing-by-connection, single-flight, cancellation,
retry-classification, worker, and queue-overflow policy remains task 6.2.

**Task 6.2 status:** Complete. The bounded, coalescing, cancellation-aware work
queue and hosted worker are implemented in `src/ArrTags/Updates`.
`WorkItemKey` is the coalescing and single-flight identity (Jellyfin item,
resolved connection, and image surface); `LibraryWorkItem` is the bounded queued
unit (key, reason, safe configuration generation) and `LibraryWorkItem.FromHint`
leaves the connection unresolved with the unindexed Primary surface.
`LibraryWorkQueue` replaces the task 6.1 `BoundedWorkHintSink`, implements the
`IWorkHintSink` enqueue boundary, and coalesces a redundant hint or work item for
a key that is already pending or in flight. It bounds pending work by the ADR-004
`QueueCapacity` and single-flight by `PerItemInFlightWork`, resolving both from
the current configuration snapshot on each operation so a replaced snapshot takes
effect without rebuilding the singleton; `TryEnqueue` never blocks and never
throws for ordinary coalescing or overflow, so the library-event publisher is
never blocked. A stopped queue rejects new work. `LibraryWorkWorker` is the
hosted consumer: a fixed, bounded pool of cancellation-aware workers (default
four) dequeues items, dispatches them through the narrow `IWorkItemProcessor`
boundary, and classifies each outcome with the provider retry vocabulary
(`ArrErrorRetryability`). Transient (`Later`) outcomes retry with the bounded
ADR-004 `TransientRetryCount` and exponential backoff computed by
`WorkRetryPolicy`; terminal and unclassified processor failures do not retry. On
shutdown the worker stops accepting, cancels queued and in-flight work, and
awaits the workers within a bounded timeout, leaving no fire-and-forget task or
unmanaged thread. `DeferredWorkItemProcessor` is the Phase 6 placeholder
dispatch: it completes each dequeued item as a safe no-op until tasks 6.3-6.6
deliver the reconciliation pipeline, so no metadata is published and the
concurrent-publication lifecycle-fence finding from the Phase 5 review remains
open (see `docs/implementation/6.2/worker-report.json`). Focused tests cover
coalescing by item and connection, single-flight, cancellation during processing
and backoff, transient/terminal retry classification and bounded attempts,
overflow, no-blocking enqueue, bounded diagnostics, and restartability.

**Task 6.3 status:** Complete. The canonical metadata state and the real
reconciliation/publication path are implemented in `src/ArrTags/Reconciliation`
with provider composition in `src/ArrTags/Providers/Radarr` and
`src/ArrTags/Providers/Sonarr`. `MetadataStateEntry` is the versioned,
secret-free, provider-neutral record for a match and its last-known-good
normalized metadata (item and library scope, provider/connection scope, typed
record/file identity, match classification and fingerprint, normalized metadata
snapshot and fingerprint, optional provider version/token, fetch timestamp, and
explicit `MetadataStateKind`); the freshness-window fields are recorded but the
stale-last-known-good policy remains task 6.5. `MetadataStateStore` persists the
record through the existing versioned cache state boundary, so a corrupt or
semantically invalid entry is discarded and rebuildable without blocking startup
or publication, and an in-place replacement is a single atomic write.
`MetadataReconciliationProcessor` implements `IWorkItemProcessor`: it re-reads
the current configuration snapshot and Jellyfin item, resolves the applicable
Sonarr/Radarr connection, matches the item through the provider-neutral reader
(`RadarrMetadataReader`/`SonarrMetadataReader` compose the existing read clients,
candidate factories, matching order, episode-file join, and metadata mappers),
computes the canonical metadata fingerprint, re-reads the current configuration
and item immediately before publishing, and only then atomically publishes. A
metadata state is discarded and never published or overwritten when the
configuration version advanced, the connection was disabled or changed, the item
was removed, changed, or is no longer eligible, or when the provider read fails
(a transient failure stays retryable; a terminal failure does not). The
processor publishes metadata state only and never invokes
`ArtworkGenerationCoordinator`/`ArtworkPublisher`, so fingerprint-driven artwork
work remains tasks 6.4 and 6.6. `DeferredWorkItemProcessor` is removed and the
real processor is registered in DI; the task 6.1/6.2 queue, worker, coalescing,
single-flight, retry, and shutdown behavior is unchanged. Focused tests cover
atomic publication, in-place fingerprint replacement, corrupt and
semantically-invalid cache discard, reload round-trip, secret-free persistence,
discard on advanced configuration version/disabled connection/absent or changed
or ineligible item, retryable versus terminal provider failure, no overwrite of
an existing published state on discard, provider reader matching/mapping, and the
DI replacement.

**Task 6.4 status:** Complete. Non-terminal artwork-operation recovery is driven
from the Phase 6 pipeline in `src/ArrTags/Artwork`, `src/ArrTags/Updates`, and
`src/ArrTags/PluginLifecycle`. `ArtworkRecoveryGate`
(`IArtworkRecoveryGate`) is the provider-neutral per-subject gate: it reads the
durable `ArtworkOperation` record and, when a non-terminal operation exists,
reconciles it through the task 5.7 `ArtworkReconciler` under the current durable
lifecycle fence, then re-reads the record as a postcondition. New work proceeds
only when the record is absent, already terminal, or has reached a terminal
outcome; a corrupt record, a recovery that does not reach a terminal outcome, or
an older durable generation fails closed. The gate reuses the pure
`ArtworkRecoveryDecisions` table and the shared `ArtworkSubjectGate`; it never
recaptures the current image as a new source, never mutates a changed or
unverifiable image, and never deletes an artifact not proven non-active. The gate
exposes the authoritative durable generation so accepted new work can only
supersede it through the store's monotonic generation fence.
`ArtworkRecoveringWorkItemProcessor` composes the gate ahead of the unchanged,
artwork-free `MetadataReconciliationProcessor`, so a queued item defers (bounded
retry) or fails closed instead of being processed while its subject has a
non-terminal artwork operation. `ArtworkStartupRecoveryService` runs one bounded,
cancellation-aware startup scan over the persisted operation records, limited by
`OperationalLimits.ReconciliationBatchSize`; the scan is best-effort and
non-blocking, and any record beyond the batch is recovered lazily by the
per-subject gate before that subject's next work item. The Phase 5 review MEDIUM
lifecycle-fence race is closed: `ArtworkPublisher` re-reads and enforces the
durable fence immediately before the first image mutation (aborting the
operation) and again before the final commit (leaving the verified postcondition
non-terminal for recovery), so an in-flight publication cannot cross a
disable/uninstall drain; an interleaved drain/publication test proves no
untracked non-terminal publication survives the fence. Focused tests cover
recovery-before-new-work, deferral/terminal classification, generation-fence
rejection of stale writes, lifecycle-fence abort before mutation, the interleaved
drain/publication race, no-recapture, artifact non-deletion, corrupt-state
fail-closed, and the startup scan bound and cancellation. Metadata freshness
(6.5), fingerprint-driven regeneration (6.6), webhooks (6.7), and the
restart/outage test matrix (6.8) remain.

**Task 6.5 status:** Complete. Metadata freshness is now explicit and bounded,
and it is separated from artwork retention and eviction. `MetadataStateEntry`
computes `expiresAt` and `staleUntil` from
`OperationalLimits.MetadataStaleWindowMinutes` (ADR-004) and carries the explicit
`MetadataStateKind`; `MetadataFreshness` plus
`EvaluateFreshness`/`IsUsableAsCurrent`/`IsExpired` evaluate the effective state
so stale data is never mistaken for current. The configured window is the total
bounded last-known-good lifetime: an observation is fresh for the first half of
the window and may be retained as bounded last-known-good for the remaining half
(`expiresAt = fetchedAt + window / 2`, `staleUntil = fetchedAt + window`); after
`staleUntil` it is `Expired` and is not usable as current, and new artwork
publication must stop or retain the current usable artwork (the artwork pipeline
that consumes this boundary is task 6.6). A transient (`ProviderUnavailable`)
read failure within the window keeps the last-known-good snapshot as explicit
`Stale` without extending the bounded timestamps; after the window the snapshot
is not kept or refreshed as current. `MetadataReconciliationProcessor` publishes
the computed window from the current snapshot and performs the outage fallback.
`MetadataStateStore.ApplyRetention` prunes only expired metadata records, and
`StateRepository.ApplyRetention`/`StateRetention.ApplyCacheRetention` accept an
exempt-kind set so metadata last-known-good state is never evicted by the render
work-cache TTL or quota. `ArtifactRetention` is the bounded authoritative
artifact GC: it proves an artifact is not the active image, not the retained
source of a live ownership session, and not referenced by a non-terminal or
recovery-blocked operation before deleting its bytes and manifest, retains
terminal-provenance source baselines only for the terminal retention window,
reclaims superseded derived render output so the authoritative quota can be
reused, and fails closed when an authoritative record cannot be validated.
`SourceArtifactStore` gained bounded artifact enumeration, a last-write-time
lookup for a bounded just-promoted grace period, and an explicit retention
delete. `StateRetentionService` is the hosted, bounded, cancellation-aware
maintenance loop that schedules state retention, metadata freshness retention,
and artifact GC in production (previously `ApplyRetention` was test-invoked
only). The Phase 5 review MEDIUM finding #4 (no artifact GC; authoritative quota
never reclaimed; `ApplyRetention` unscheduled) is addressed: terminal provenance
and superseded render output are pruned and the authoritative quota is reclaimed
on repeated publication. Focused tests cover fresh/stale/expired transitions,
computed boundaries, outage fallback within and after the window, stale
conversion without window extension, metadata retention exempt from render-cache
eviction, artifact GC proof for active/source/non-terminal/recovery-blocked
references, fail-closed corrupt state, terminal-provenance retention and release,
the just-promoted grace period, quota reclamation on repeated publication, and
the hosted retention schedule. The task 6.1-6.4 queue, worker, metadata
publication, recovery gate, and fence behavior is unchanged. Fingerprint-driven
regeneration (6.6), webhooks (6.7), and the restart/outage test matrix (6.8)
remain.

**Task 6.6 status:** Complete. Artwork generation is now driven from the Phase 6
pipeline behind a publication-fingerprint gate, so unchanged inputs do not render
or publish again and a changed input regenerates only the affected artwork.
`ArtworkRegenerationPlanner` (in `src/ArrTags/Artwork`) is the pure gate: after
metadata state is published it compares the logical publication fingerprint — the
source-artwork identity, the canonical metadata fingerprint, the resolved badge
selection, the secret-free renderer configuration fingerprint, and the
renderer/badge-schema versions — computed with the same `RenderFingerprint` and
`BadgeDefinitionResolver` the renderer uses, against the persisted
`PublishedArtworkState.PublishedFingerprint` and `RendererVersion`. It skips with
no work when the fingerprint and renderer version are unchanged, when the metadata
state is not usable as current under the task 6.5 freshness policy (Expired or
otherwise unusable metadata is never rendered or published), when the ownership
state does not permit publication (`OwnershipLost`, `OwnershipUnknown`,
`RestorePending`, `RestoreBlocked`), or when an absent baseline cannot be
re-rendered; otherwise it requires generation, and a subject with no ownership
session (or a terminal `Restored`/`Removed` session) requires a new recoverable
source baseline. `ArtworkGenerationCoordinator` now selects the render source: for
an owned `Published`/`NotPublished` session it reads and integrity-validates the
retained original source artifact and renders from it — never from the current
active surface — so a repeat publication cannot stack a badge onto the previous
ArrTags output, and a missing, corrupt, or dimension-less baseline fails closed
instead of re-capturing the derived image. `ArtworkPublishingWorkItemProcessor`
(in `src/ArrTags/Updates`) composes the unchanged `MetadataReconciliationProcessor`
with the gate and the coordinator: `MetadataReconciliationProcessor.ReconcileAsync`
returns the worker classification plus the live canonical context (identity,
match, metadata, and the published entry) when atomic state was published, and the
artwork stage runs only for that context. DI composes the task 6.4
`ArtworkRecoveringWorkItemProcessor` over the new
`ArtworkPublishingWorkItemProcessor` over the metadata processor, so
recovery-before-new-work, the publisher's durable fence re-checks, the durable
write-ahead protocol, and the metadata publication semantics are unchanged. A
renderer, schema, or configuration change regenerates against the retained
original while preserving the source artifact, ownership token, and any
non-terminal operation, so authoritative provenance is never destroyed. The Phase
5 review MEDIUM "Cross-task integration (5.8 repeat publication / Phase 6
pipeline)" is closed by the retained-source selection and repeat-publication
tests, and the Phase 5 review MEDIUM "Test coverage carried forward from Phase 4"
is closed by `RendererConfigurationIntegrationTests`, which resolves an overridden
palette and a disabled selector through `PluginConfigurationSnapshot` and asserts
the rendered request and output fingerprint reflect the resolved configuration.
The phase-5 LOW "Robustness / repeat publication" is addressed by validating the
retained source artifact before reuse, and the phase-5 LOW "Memory / scalability"
per-subject gate bound is documented as accepted in `docs/architecture.md` section
9. Focused tests cover the unchanged no-op, regeneration on each changed input
(metadata, source, configuration, renderer version, schema version), the
retained-source repeat render without double-badging, corrupt baseline fail-closed,
unusable/expired metadata no-publish, blocked and absent ownership states, the
config-to-render integration, and the DI composition. The task 6.1-6.5 queue,
worker, metadata publication, recovery gate, freshness, and retention behavior is
unchanged. Webhooks (6.7) and the restart/outage test matrix (6.8) remain.

**Task 6.7 status:** Complete. DG-7 is resolved by `docs/decisions.md` ADR-012,
and the authenticated, bounded inbound Arr webhook boundary is implemented in
`src/ArrTags/Webhooks`. `ArrTagsWebhookController` is an anonymous plugin
`ControllerBase` discovered by Jellyfin's plugin controller registration and
exposes `POST /ArrTags/Webhook/Sonarr` and `POST /ArrTags/Webhook/Radarr`. It
authenticates the `X-ArrTags-Webhook-Secret` header through the ADR-005 webhook
lease (`SecretReference.WebhookAuthentication`,
`TryAcquire(reference, configurationVersion)`, `SecretLease.Matches`) with a
constant-time comparison; a missing configuration, a missing candidate, an
oversized candidate, a rotated generation, and a mismatch all fail closed with
the same bounded `401` and no secret/header/body leakage. `WebhookBodyReader`
enforces `OperationalLimits.WebhookMaxPayloadBytes` (default 256 KiB, range
4 KiB-4 MiB) before buffering and returns `413`; `WebhookEventParser` is
tolerant of unknown fields and casing and bounded in JSON depth and episode
count, maps only the event kind, upgrade flag, and provider record/file hints,
rejects empty/malformed/truncated/wrong-shaped payloads with `400`, and
acknowledges an unsupported event kind with `202` and no work. The bounded,
coalescing `WebhookIntake` suppresses duplicate, out-of-order, and replayed
deliveries within a short window and drops overflow without blocking, and the
hosted `WebhookIntakeService` resolves accepted events off the request path.
`WebhookReconciliationResolver` performs bounded provider-record-to-Jellyfin
resolution: it returns only the Jellyfin items already associated with the
advertised provider record in the persisted metadata-state mapping for the
resolved connection, bounded by `ReconciliationBatchSize`, so a payload item id
is never permission and no Arr write or direct publication occurs. Resolved
items are enqueued through the existing `IWorkHintSink` as the same bounded
`LibraryWorkHint` work as every other trigger, so the worker re-reads current
Jellyfin and Arr state. `MetadataStateStore` gained a bounded `Enumerate`
accessor used only by the resolution, and DI registers the resolver, intake, and
hosted service after the work worker. Focused tests in `WebhookBoundaryTests`
(34 cases) and `WebhookResolutionTests` (11 cases) cover authentication
success/failure, the constant-time lease path, declared and streamed size
limits, malformed/truncated/empty/wrong-shaped payloads, unsupported event
types, replay/duplicate coalescing, bounded overflow and coalescing windows,
hint mapping, the controller registration contract, the no-write/no-publication
dependency boundary, the new limit validation, provider-record resolution,
disabled/mismatched/unsupported no-ops, series and episode scoping, the
batch-size bound, and the hosted service feeding the deduplicated queue. The
task 6.1-6.6 queue, worker, metadata publication, recovery, freshness,
retention, and regeneration behavior is unchanged. The restart/outage test
matrix (6.8) remains.

**Task 6.8 status:** Complete. The phase-wide Phase 6 verification matrix is
implemented in `tests/ArrTags.Tests` with no production behavior change. A
composed `Phase6Harness` builds the full Phase 6 graph (durable stores, the
reconciliation processor, the artwork publication pipeline, the per-subject
recovery gate, the lifecycle drain coordinator, the bounded work queue/worker,
and the injectable host/renderer doubles) over one plugin data directory, and
`Phase6Harness.Restart()` rebuilds every in-memory service over the same durable
directory so the guarded decisions are proven not to rely on in-memory state.
Restart tests cover metadata state and freshness reload, published-artwork
ownership/provenance, non-terminal operation recovery before new work, retention
of the live session and artifacts with only expired metadata pruned, and the
production startup recovery scan. Shutdown tests cover bounded cancellation and
await of queued/in-flight work, no partial image or committed state across a
cancelled mutation, coordination of the worker shutdown with the publisher fence
and the lifecycle drain (leaving no untracked non-terminal publication), and
bounded retry-backoff cancellation. Corruption tests cover torn/incompatible/
semantically invalid metadata-cache discard and rebuild without blocking
startup, and quarantine plus fail-closed (pipeline and publisher) with no image
mutation, no blind replay, and no artifact cleanup for corrupt authoritative
artwork-state and operation records. Outage tests cover bounded last-known-good
within the window without extending it, no keep/use of expired metadata, and an
outage beyond the bounded retries, all leaving the current artwork unchanged.
Recovery tests cover the durable generation fence and the lifecycle fence
(including a corrupt fence that fails closed toward restoration). Duplicate tests
cover duplicate/out-of-order library events and duplicate/replayed webhook
deliveries coalescing with no duplicate render or publication. Queue-pressure
tests cover concurrent overflow, a slow provider under thousands of synchronous
events, and capacity reuse after a drain. Cancellation tests cover a provider
read, a render, the image mutation, and a pre-cancelled item, each publishing no
partial state or image. The default suite passes 1170 with 58 environment-guarded
skips (1228 total), exactly +29 over the task 6.7 baseline (1141/58/1199), with
no new skips and no regressions. Gate 6 is not self-declared here; the phase
review that confirms the recorded operational limits and the phase acceptance
criteria is separate. One pre-existing, documented quarantine limitation is
recorded rather than changed: an invalid authoritative record is quarantined on
the read that detects it, and a later read of the same subject then observes the
record as absent (the detecting read is the one that fails closed, matching the
task 6.4 quarantine semantics).

**Task 6.9 status:** Complete. The Phase 6 review HIGH finding (no scheduled,
post-scan, or manual/periodic reconciliation producer) and its MEDIUM finding
(unenforced provider/render concurrency) are resolved. The bounded,
provider-neutral `LibraryReconciliationService` (`src/ArrTags/Reconciliation`)
enumerates the candidate movie and episode items in pages bounded by
`OperationalLimits.ReconciliationBatchSize` through the new
`IMediaLibraryEnumerator`/`JellyfinMediaLibraryEnumerator` read boundary, filters
each page through the existing `MediaIdentityFactory` and `MediaEligibility`
boundaries against the current configuration snapshot, checks cancellation
between and inside pages, yields between batches, and enqueues only the same
bounded `LibraryWorkHint` work as every other trigger through `IWorkHintSink`; it
never calls a provider, renderer, publisher, or image API. It stops as soon as
the durable `ArtworkLifecycleFenceStore` refuses new publication work, so a
disable or uninstall fence is never crossed. The Jellyfin 12 scheduled task
`ArrTagsReconciliationTask` (`IScheduledTask`) exposes a default 12-hour interval
trigger, appears in Jellyfin's scheduled-task surface, supports manual execution
through that surface, is cancellable and progress-reporting, and propagates
cancellation so Jellyfin records the task as cancelled. The post-scan trigger
`ArrTagsPostScanTask` implements the dedicated Jellyfin 12
`ILibraryPostScanTask` extension point, which `LibraryManager` invokes after a
media-library scan with the scan's progress and cancellation token; no limitation
had to be recorded because the supported post-scan hook exists. Both tasks are
public concrete plugin types discovered by Jellyfin's assembly scanning
(`ITaskManager.AddTasks(GetExports<IScheduledTask>)` and
`ILibraryManager.AddParts(GetExports<ILibraryPostScanTask>)`) and are also
registered in DI with their dependencies; registration and construction perform
no provider, rendering, or library work. The ADR-004 concurrency limits are now
enforced at their boundaries: `ProviderConcurrencyLimiter` acquires the global
and per-connection permits around each reconciliation read through the
`ConcurrencyLimitedArrMetadataReader<TReader>` decorator, and
`ConcurrencyLimitedRenderer` acquires the render permit around each render, both
resolving the current limit from the configuration snapshot on every acquisition
via the bounded, cancellation-aware `DynamicConcurrencyLimiter`. Focused tests
cover the page batching, scope/eligibility filtering, cancellation between pages,
the fence before and during a run, progress reporting, the real bounded queue
integration, the periodic trigger and manual surface, cancellation propagation,
DI registration and assembly-scan discoverability, and the configured provider
and render concurrency caps (per-connection, global, and render). The task
6.1-6.8 queue, worker, metadata publication, recovery, freshness, retention,
regeneration, and webhook behavior is unchanged. The remaining Phase 6 review
MEDIUM/LOW items (provider catalogue/inventory cache, runtime configuration
replacement wiring, and a safe metrics/diagnostic-status surface) are recorded
as Phase 7 tracked deferrals in the Post-V1 Backlog and are not presented as
solved. Gate 6 is not self-declared here; the phase re-review is separate.

**Authoritative Phase 6 execution order:** 6.1, 6.2, 6.3, 6.4, 6.5, 6.6, 6.7,
6.8, 6.9. Task IDs are stable references only; this execution order is the
canonical sequence. The order is derived from the documented dependencies, not
from task numbering:

- 6.1 has no prerequisites: the event handler and enqueue boundary is the entry
  point for all later queue work.
- 6.2 depends on 6.1 and provides the queue, coalescing, single-flight, and
  cancellation behavior.
- 6.3 depends on 6.2 because atomic metadata-state publication and stale-work
  disposal operate on queued work.
- 6.4 depends on 6.2 and 6.3 and reuses the queue's generation fences before
  accepting new work for an item/image surface.
- 6.5 depends on 6.3 because freshness/staleness policy is applied when
  metadata state is published.
- 6.6 depends on 6.3 and 6.4 because fingerprint-driven invalidation must run
  through the publication and recovery boundaries.
- 6.7 depends on 6.1 because bounded webhook hints enter through the same
  handler/enqueue boundary.
- 6.8 depends on 6.1 through 6.7 because it tests the restart, outage, recovery,
  duplicate-event, pressure, and cancellation behavior of the complete phase.
- 6.9 depends on 6.1 through 6.3 because it produces the same bounded work hints
  through the existing queue/worker/reconciliation boundaries; it adds the
  scheduled, post-scan, and manual reconciliation producers and the provider and
  render concurrency enforcement that the Phase 6 review found missing.

**Acceptance criteria:**

- [x] Unchanged metadata and source state do not repeatedly fetch, render, or
  publish work unnecessarily. (Met for render and publication, which are
  fingerprint-gated; provider metadata is still fetched for every work item and a
  provider inventory/catalogue cache remains an open item (consolidated in
  `docs/limitations.md`), so this criterion is accepted as partially met at the
  integration-test level.)
- [x] A changed Arr file, source image, badge configuration, or renderer version
  produces the required new fingerprint and render result.
- [x] Temporary provider outages use bounded last-known-good state only within
  policy, then leave the current usable artwork unchanged.
- [x] Queue overflow and slow providers never block Jellyfin library event
  delivery indefinitely.
- [x] Restart or cancellation cannot publish partial state or partial image
  output.
- [x] Plugin cache and provenance entries are bounded where applicable,
  versioned, recoverable, and free of credentials.

The Phase 6 acceptance criteria are accepted with the Gate 6 review. The review's
HIGH deliverable gap (scheduled, post-scan, and manual/periodic reconciliation)
and its MEDIUM concurrency-enforcement gap were addressed by task 6.9. Acceptance
criterion 1 is accepted as partially met: render and publication are
fingerprint-gated, but provider metadata is still fetched for every work item
because a provider inventory/catalogue cache is deferred and remains an open
item in the Post-V1 Backlog and `docs/limitations.md` rather than presented as
solved. The review's remaining MEDIUM item (reconciliation coverage capped by
the bounded, droppable queue on a scope larger than `QueueCapacity`) remains an
open item there as well.

**Gate 6:** Met at the integration-test level (tag `v0.1.0-phase6`). Load,
outage, restart, invalidation, and recovery tests meet the recorded operational
limits without degrading Jellyfin operations. No live Jellyfin host or live Arr
instance was exercised; the scheduled-task/post-scan discovery and the
provider/render concurrency enforcement are validated in-process against the
pinned 12.0.0 ABI and host source.

### 7. Testing & release

**Objective:** Validate the complete V1 behavior against the selected Jellyfin
12 ABI and supported Sonarr/Radarr versions, then produce a reproducible
release artifact and operational documentation.

**Deliverables:**

- Unit test coverage for configuration, DTO mapping, matching, metadata
  semantics, fingerprints, queue behavior, cache recovery, and rendering.
- Integration test coverage for plugin discovery, DI, lifecycle, image routes,
  Arr failures, webhooks, conditional responses, and Enhanced coexistence.
- End-to-end acceptance checks for movie and television poster flows.
- Reproducible build, package, versioning, and release checklist.
- User/admin documentation for configuration, supported behavior, failure modes,
  badge policy, and Jellyfin Enhanced coexistence.

**Tasks:**

- [x] 7.1 Run all unit and integration tests against the exact declared versions.
- [x] 7.2 Verify the plugin installs, upgrades, reloads, and uninstalls safely.
- [x] 7.3 Verify all success criteria in `GOALS.md`, including independent
  provider configuration, matching, quality retrieval, poster output, update
  behavior, Enhanced compatibility, graceful failure, and reproducible builds.
  (Verification executed; a release blocker was found that made `GOALS.md`
  criteria 5, 6, and 8 fail as shipped. Resolved by task 7.8/ADR-015 and the
  re-run live verification, so criteria 5 and 8 are now met as shipped, with
  criterion 6 met for render and publication but only partial for provider
  fetches (`docs/limitations.md` F1), and Phase 7 acceptance criteria 3 and 4
  are met. See the task 7.3 status and release blocker 7.3-F1.)
- [x] 7.4 Review logs, diagnostics, HTTP behavior, and persisted state for secret
  leakage or unbounded data. (Review executed live on the pinned Jellyfin
  `12.0.0` musl host; negative result - no credential leakage and no unbounded
  path was found. See the task 7.4 status.)
- [x] 7.5 Build the release package from a clean checkout and record the
  commands, inputs, artifact identity, and supported version ranges. (Complete:
  the package is byte-reproducible across clean builds and the release/build
  process, supported version ranges, artifact identity, and release checklist
  are recorded in `docs/release/build-and-release.md`. See the task 7.5 status.)
- [x] 7.6 Document known limitations and any deferred decision without presenting
  unsupported behavior as available. (Complete: the consolidated record is
  `docs/limitations.md`, linked from `README.md`, and reflected in the
  `PLANS.md` Post-V1 Backlog and `docs/implementation-readiness.md`. See the
  task 7.6 status.)
- [x] 7.7 Relocate the persisted plugin state root outside Jellyfin's
  `PluginsPath` so the supported versioned install layout
  (`plugins/ArrTags_<version>`) is not deleted on restart once state exists, add
  a regression test for the versioned-install-plus-state case, and re-run the
  live install, upgrade, reload, and uninstall verification (task 7.2 finding
  7.2-F1; the Phase 5 versioned-install-folder provenance carry-forward).
- [x] 7.8 Stop shipping the duplicate managed `SkiaSharp.dll` and native
  `libSkiaSharp.so` in the plugin package and share the host's SkiaSharp; update
  the packaging contract (`ArrTags.csproj` `PackagePlugin` target and
  `PackageReference` assets, `build.yaml` artifacts, and `PluginPackagingTests`),
  record the decision in a new ADR (superseding the bundling parts of ADR-010),
  and re-run the task 7.3 live end-to-end verification against the committed
  package (task 7.3 finding 7.3-F1).

**Authoritative Phase 7 execution order:** 7.1, 7.2, 7.7, 7.3, 7.8, 7.4, 7.5, 7.6.
Task IDs are stable references only; this execution order is the canonical
sequence. The order is derived from the documented dependencies, not from task
numbering:

- 7.1 has no prerequisites and runs the full suite against the exact declared
  versions.
- 7.2 depends on 7.1 and verifies install, upgrade, reload, and uninstall safety.
- 7.7 depends on 7.1 and 7.2 because it resolves the release-blocking
  versioned-install/data-folder collision that task 7.2 confirmed live and then
  re-runs the live install, upgrade, reload, and uninstall verification.
- 7.3 depends on 7.1, 7.2, and 7.7 because the `GOALS.md` success criteria span
  provider, matching, rendering, artwork, update, and lifecycle behavior.
- 7.8 depends on 7.3 because it resolves the release-blocking duplicate-SkiaSharp
  packaging defect that task 7.3 confirmed live (7.3-F1) and then re-runs the
  live end-to-end `GOALS.md` verification.
- 7.4 depends on 7.1, 7.2, 7.3, 7.7, and 7.8 and reviews logs, diagnostics, HTTP
  behavior, and persisted state produced by the test and install runs.
- 7.5 depends on 7.1 through 7.4 and 7.7 and 7.8 because the release package may
  be built only after the tests and checks pass.
- 7.6 depends on 7.1, 7.3, 7.5, 7.7, and 7.8 so the documented limitations and
  deferred decisions match the verified behavior and the release artifact.

**DG-9 ownership:** DG-9 is resolved by ADR-013. Task 7.1 exercises the declared
provider lines with provider contract fixtures (fully populated and
sparse/optional-missing payloads), task 7.5 records the supported ranges in the
release artifact, and task 7.6 documents the ranges and the optional-field
compatibility policy.

**Task 7.1 status:** Complete. The full unit and integration suite passes from a
clean rebuild against the exact declared versions (`net10.0` with the SDK pinned
by `global.json` to `10.0.0`, `Jellyfin.Controller`/`Jellyfin.Model` `12.0.0` with
manifest `targetAbi: 12.0.0.0`, SkiaSharp `3.119.4` for the `linux-x64` plugin
RID, xunit `2.9.3`, Microsoft.NET.Test.Sdk `18.10.1`,
xunit.runner.visualstudio `3.1.5`, and plugin version `0.1.0.0`): after removing
`src`/`tests` `bin`/`obj`, `./build.sh restore`, `./build.sh build`, and
`./build.sh test` reported build 0 warnings / 0 errors and Failed 0, Passed 1215,
Skipped 58, Total 1273 - exactly +18 over the Phase 6 baseline (1197/58/1255)
with no new skips and no regressions. The ADR-013 provider-contract coverage gap
was closed by `tests/ArrTags.Tests/ProviderContractFixtureTests.cs` (18 cases
covering the Sonarr 3.x/4.x and Radarr 3.x/4.x/5.x/6.x probe lines, fail-closed
missing identity, fully-populated mapping, sparse/optional-missing mapping to
explicit unknowns, and absent file identity). No production behavior changed, and
no live Jellyfin host or live Arr instance was exercised (contract-fixture level).

**Task 7.2 status:** Complete (verification executed; Phase 7 acceptance criterion
2 is **not met**). A live install -> load -> upgrade -> reload -> uninstall
sequence was exercised against the pinned Jellyfin `12.0.0` musl host. The
package (`./build.sh package` -> `artifacts/ArrTags_0.1.0.0.zip`) was installed
into the host's actual plugin path `PREFIX/data/plugins` (Jellyfin's
`ProgramDataPath`/`--datadir`, **not** `config/plugins`) as the versioned folder
`ArrTags_0.1.0.0`; the host logged `Loaded plugin: ArrTags 0.1.0.0`, wrote an
Active `meta.json`, and reported no load errors, which also validates the pinned
host load of the `Plugin(IServiceProvider)` constructor (a Phase 5
carry-forward). A real `0.1.0.1` upgrade loaded only the newer version and the
host deleted the older versioned folder; a restart reloaded the plugin exactly
once (Jellyfin has no in-process plugin reload, so restart is the reload
mechanism); and removing the folder uninstalled cleanly with `Startup complete`
and no errors.

**Task 7.2 release blocker (Phase 7 acceptance criterion 2 not met).** The
plugin's Jellyfin-derived data folder is `PluginsPath/<assembly name>` =
`data/plugins/ArrTags` (proved against the release `MediaBrowser.Common.dll`),
and the plugin persists its state there. When that data folder exists alongside
the standard versioned install folder `data/plugins/ArrTags_<version>/`,
`PluginManager.DiscoverPlugins` treats them as two versions of the same-named
plugin and deletes the install folder on the next host restart (the
`MD5("ArrTags")` auto-manifest GUID sorts after the plugin GUID), so no ArrTags
plugin is loaded. The normal unversioned folder `data/plugins/ArrTags/` (install
folder == data folder) and the no-state case are unaffected. Exercising
`Plugin.OnUninstalling` live requires an authenticated admin uninstall (the
endpoint returns HTTP `401` while the startup wizard is incomplete); the drain is
covered in-process by `LifecycleFoundationTests` and `ArtworkLifecycleTests`.
Live `ImageSaver` read-back remains unexercised (no media library item is
available). Gate 7 is not ready.

**Task 7.7 status:** Complete. Resolves the task 7.2 release blocker (finding
7.2-F1) and re-runs the live verification, so Phase 7 acceptance criterion 2 is
now met. The `Plugin` constructor calls the public
`BasePlugin.SetAttributes(assemblyFilePath, dataFolderPath, version)` contract
(the `IPluginAssembly` method the host loader itself uses) to set
`DataFolderPath` to `Path.Combine(ApplicationPaths.ProgramDataPath, "ArrTags")`, a
sibling of the plugins directory and therefore outside
`ApplicationPaths.PluginsPath`; `AssemblyFilePath` and `Version` are passed
through unchanged. The decision and its consequences are recorded in
`docs/decisions.md` ADR-014. Uninstall cleanup was preserved explicitly: the
pinned host deletes only the install folder, not `DataFolderPath`, so
`Plugin.OnUninstalling` now removes its own relocated state root after a
completed drain and retains the recovery records on an incomplete or cancelled
drain. Regression coverage: `PluginStateLocationTests` (4 facts, of which the two
location facts fail against the previous derivation) and `PluginDiscoveryHostTests`
(2 host-guarded facts that execute the pinned host's real `PluginManager`
discovery from `Emby.Server.Implementations.dll` and prove the versioned install
folder is deleted when state lives at `PluginsPath/ArrTags` and preserved when
the state root comes from the production `Plugin`). Live re-verification on the
pinned Jellyfin `12.0.0` musl host: a real versioned state record was written at
the production location with production code (`/tmp/jf/data/ArrTags`), the
rebuilt package installed as `ArrTags_0.1.0.0` loaded and the install folder and
state record survived restarts (the task 7.2 A/B/A failure is fixed), a restart
reloaded exactly one instance, a temporary `0.1.0.1` upgrade loaded only the
newer version and the host deleted the older folder (all temporary version edits
reverted), and removing the plugin folder produced zero loads and a clean
`Startup complete`. Build 0 warnings / 0 errors; default suite Failed 0, Passed
1219, Skipped 60, Total 1279; host-guarded suite (`ARRTAGS_JELLYFIN_HOST_DIR=
/tmp/jf/jellyfin`) Failed 0, Passed 1235, Skipped 44, Total 1279.

**Task 7.3 status:** Complete (verification executed; at the time, `GOALS.md`
criteria 5, 6, and 8 were **not met as shipped** because of release blocker
7.3-F1, so Phase 7 acceptance criteria 3 and 4 were left unchecked. **Superseded
by task 7.8:** ADR-015 removed the duplicate SkiaSharp runtime and the re-run
live verification passes, so criteria 5 and 8 are now met as shipped, with
criterion 6 met for render and publication but only partial for provider fetches
(`docs/limitations.md` F1), and acceptance criteria 3 and 4 are met). The
`GOALS.md` success criteria were exercised end-to-end on the pinned Jellyfin
`12.0.0` musl host against
committed test-support fixtures: a new single-file mock Sonarr/Radarr `/api/v3`
server (`scripts/mock-arr-fixture.cs`, run with `dotnet run`, plus
`scripts/mock-arr-fixtures/`) that enforces `X-Api-Key`, logs every request, and
serves payloads from disk so a changed observation is reproducible; a real
Jellyfin movie and series/episode with local `.nfo` provider ids and real poster
images; and the committed `artifacts/ArrTags_0.1.0.0.zip` package installed as
`ArrTags_0.1.0.0`. Criteria **1** (loads on Jellyfin 12), **2** (independent
provider configuration: a Sonarr-only run called only Sonarr, a Radarr-only run
called only Radarr, and both-enabled called both, all with a valid `X-Api-Key`
and no call to the disabled provider), **3** (a Jellyfin movie matched Radarr
movie `1`/file `11` by TMDb `990001`, and a Jellyfin episode matched Sonarr
series `1`/episode `101`/file `201` by TVDB `990004`; the persisted metadata
state records `Matched`/`ProviderId`), and **4** (actual file quality retrieved:
`Bluray-1080p`/`bluray`/1080 and `WEBDL-1080p`/`webdl`/1080, plus media-info
resolution, dynamic range, codecs, channels, custom formats, and upgrade-pending,
all distinct from the quality profile) are met. Criterion **9** is met by the
reproducible build/package (build 0 warnings / 0 errors; default suite Failed 0,
Passed 1219, Skipped 60, Total 1279; host-guarded suite Failed 0, Passed 1235,
Skipped 44, Total 1279, both equal to the task 7.7 baseline because the task adds
no product tests). Criterion **7** is covered only at the contract level:
`EnhancedCoexistenceTests` passes 7/7 and confirms no Enhanced reference, no
spoiler/hidden/suppression branch, and badge output varying only with ArrTags
configuration, but Jellyfin Enhanced is not installed on the host, so no live
coexistence was exercised. Criterion **5** (badge rendered and published through
the standard image route with the original source preserved), criterion **6**
(changed metadata republishes and unchanged metadata does not), and criterion
**8** (provider outage leaves the current artwork unchanged and does not affect
Jellyfin) were all verified **only after removing the duplicate SkiaSharp from
the installed plugin folder**, because as shipped criterion 5 and criterion 8
fail: publishing the first badge through Jellyfin's supported
`IProviderManager.SaveImage` path aborts the host with a fatal
`InvalidCastException` (release blocker 7.3-F1). Under that diagnostic install
the movie and episode badges were served anonymously by
`GET /Items/{id}/Images/Primary` (`image/png`, 30,752 and 32,400 bytes) with the
served SHA-256 matching the persisted `PublishedArtworkState.ActiveImageIdentity`,
the original `poster.jpg`/`S01E01.jpg` files were byte-unchanged on disk, the
retained `SourceArtifactId` matched the original poster SHA-256, a changed mock
quality (`1080p` -> `2160p`/HDR10) produced a new metadata fingerprint, a new
publication fingerprint, a new image tag, and new served bytes, an unchanged
re-run left the tag, fingerprint, and bytes identical, and stopping the mock left
the host up with the current artwork unchanged and the last-known-good metadata
marked stale. No `ImageSaver`/`IProviderManager` double was used: the live
Jellyfin host owned the publication and the standard route served the result.
**No production code or package content was changed by this verification task;**
the SkiaSharp removal was applied only to the temporary installed folder for the
diagnostic and the committed package still contains the duplicate assets.

**Task 7.3 release blocker 7.3-F1 (Phase 7 acceptance criteria 3 and 4 not
met).** The committed package ships `SkiaSharp.dll` and `libSkiaSharp.so` at the
plugin folder root (the task 4.8/5.4 packaging decision). On the pinned Jellyfin
`12.0.0` musl host, the host also loads its own `SkiaSharp` in the default load
context (`/tmp/jf/jellyfin/SkiaSharp.dll`, native `/tmp/jf/jellyfin/libSkiaSharp.so`).
When ArrTags publishes its first badge, Jellyfin's image processing inside
`ProviderManager.SaveImage` aborts the process with
`System.InvalidCastException: [A]SkiaSharp.UserDataDelegate cannot be cast to
[B]SkiaSharp.UserDataDelegate`, where A is the host's default-context
`SkiaSharp.dll` and B is the plugin-context
`/tmp/jf/data/plugins/ArrTags_0.1.0.0/SkiaSharp.dll`. The crash is deterministic:
it occurred on two independent runs, it occurs about 8 ms after the
`artwork-operation` record is written (the movie bytes are replaced and the host
dies), and it does not occur with both providers disabled or with the plugin
uninstalled. Removing the bundled `SkiaSharp.dll` and `libSkiaSharp.so` from the
installed plugin folder (so the plugin shares the host's SkiaSharp) made the full
pipeline work, so the conflict is the bundled duplicate, not the plugin's
render/publication logic. This needs a production/packaging decision and a new
ADR before V1 can be released; task 7.3 did not implement it. The release
artifact and the `PluginPackagingTests` that currently assert the bundled
`SkiaSharp.dll`/`libSkiaSharp.so` artifacts therefore still describe the broken
state.

**Task 7.8 status:** Complete. Resolves task 7.3 release blocker 7.3-F1 and
re-runs the live end-to-end verification, so `GOALS.md` criteria 5 and 8 are now
met as shipped, with criterion 6 met for render and publication but only partial
for provider fetches (`docs/limitations.md` F1), and Phase 7 acceptance criteria
3 and 4 are met. The decision
is recorded in `docs/decisions.md` ADR-015, which supersedes the renderer-bundling
parts of ADR-010. `src/ArrTags/ArrTags.csproj` keeps exact `SkiaSharp` /
`SkiaSharp.NativeAssets.Linux` `3.119.4` references with
`<ExcludeAssets>runtime</ExcludeAssets>` (compile-time only), drops the
`PluginRuntimeIdentifier` RID filter, and the `PackagePlugin` target no longer
resolves, copies, or fails on the renderer runtime assets; `build.yaml`
`artifacts` lists only `ArrTags.dll` and `ArrTags.deps.json`. The package now
contains only `ArrTags.dll`, `ArrTags.deps.json`, `build.yaml`,
`THIRD-PARTY-NOTICES.md`, and `licenses/`, with no `SkiaSharp.dll` or
`libSkiaSharp.so`. `PluginPackagingTests` asserts the new contract, including a
regression that the archive contains neither duplicate, and the test project
references the pinned SkiaSharp runtime directly so the golden/native renderer
tests keep exercising the real stack (the forced native round trip passes 2/2).
Live re-verification on the pinned Jellyfin `12.0.0` musl host with the committed
`artifacts/ArrTags_0.1.0.0.zip`: installed as `ArrTags_0.1.0.0`, it loaded with
`Loaded plugin: ArrTags 0.1.0.0` and no plugin-folder SkiaSharp assembly load,
and no `[ERR]`/`[FTL]`. After the plugin's prior image state was cleared and the
items were reset to their original sidecar posters, the ArrTags scheduled
reconciliation published both badges with 0 `[FTL]`/`InvalidCastException`; the
movie and episode `GET /Items/{id}/Images/Primary` responses were `image/png`
6,112 and 8,315 bytes whose SHA-256 equalled the persisted
`PublishedArtworkState.ActiveImageIdentity` (and the metadata-store
`poster.png`), the original `poster.jpg`/`S01E01.jpg` files were byte-unchanged,
the persisted `SourceArtifactId` equalled the original poster SHA-256, changed
mock metadata (1080p -> 2160p/HDR10) produced new metadata and publication
fingerprints, new image tags, and new served bytes while an unchanged re-run left
the fingerprint, tag, and bytes identical, a simulated provider outage (503) left
the host up (10/10 HTTP 200) with the artwork unchanged and both metadata states
marked stale with no `[FTL]`, and removing the plugin folder produced zero loads
and a clean `Startup complete` on restart. `. /config/arrtags-env.sh && ./build.sh
build` reported 0 warnings / 0 errors; default suite Failed 0, Passed 1218,
Skipped 60, Total 1278; host-guarded suite
(`ARRTAGS_JELLYFIN_HOST_DIR=/tmp/jf/jellyfin`) Failed 0, Passed 1234, Skipped 44,
Total 1278. The -1 total versus the task 7.7 baseline 1279 is the net
`PluginPackagingTests` change (two removed renderer-bundling facts and one added
duplicate-asset regression fact).

**Task 7.4 status:** Complete (review executed live; negative result). The logs,
diagnostics, HTTP behavior, and persisted state produced by the live install and
reconciliation runs were reviewed for secret leakage and unbounded data on the
pinned Jellyfin `12.0.0` musl host. Real provider and webhook traffic was
generated with distinctive sentinel secrets
(`SONARR-SENTINEL-KEY-1b8d4f6a-DO-NOT-LEAK`,
`RADARR-SENTINEL-KEY-7f3a9c2e-DO-NOT-LEAK`,
`WEBHOOK-SENTINEL-SECRET-9e2c7a5d-DO-NOT-LEAK`, and a wrong
`WRONG-SONARR-SENTINEL-KEY-aaaa1111-DO-NOT-LEAK`) across the successful and
failing paths - provider success, `401` authentication failure, malformed
response, oversized response, request timeout, and webhook success,
missing-secret, wrong-secret, oversized, and malformed deliveries. No sentinel
value appeared in `/tmp/jf/console.log`, `/tmp/jf/log`, the plugin state under
`/tmp/jf/data/ArrTags`, the installed plugin folder, or the mock request log; the
only file on the host that contained any sentinel was Jellyfin's persisted plugin
configuration `data/plugins/configurations/ArrTags.xml`, which ADR-005 designates
as the single source of truth for the credentials. The plugin source contains no
logging call, `SecretLease`/`ArrProviderError`/`ArtworkOperationErrors` bound and
redact diagnostic text (512-character limits), and no log line, state record, or
webhook response contained the connection credential or an `X-Api-Key`/
`Authorization` value. Persisted state is versioned (`SchemaVersion`, `Kind`,
`RecordId`, `Authority`, `Terminal`) and SHA-256 integrity-tagged
(`PayloadSha256`) with traversal-safe single-segment identifiers, and the
metadata-state, artwork-operation, artwork-state, and source-artifact records
contained only hashes, identifiers, fingerprints, and timestamps. The webhook
routes are anonymous but require the secret through the constant-time versioned
lease: they returned a uniform `401` for missing, wrong, and oversized
candidates, `413` for both content-length and chunked oversized bodies, `400` for
malformed, wrong-shaped, and empty payloads, and `202` for valid deliveries, with
no secret or sensitive detail reflected. The standard image route authorization
is Jellyfin's own and the plugin exposes only the two webhook `POST` routes. Every
operational bound (queue capacity, per-item single-flight, provider/render
concurrency, timeout, retries/backoff, provider response size, webhook payload
size, source/derived artifact size, decoded dimensions, reconciliation batch,
freshness window, cache TTL/quota, authoritative quota, and terminal-provenance
retention) has an explicit ADR-004/ADR-012 value enforced at its boundary rather
than only validated, and no unbounded queue, retry, cache, artifact, payload, or
log path was found. `./build.sh build` reported 0 warnings / 0 errors; the default
suite was Failed 0, Passed 1218, Skipped 60, Total 1278 and the host-guarded suite
(`ARRTAGS_JELLYFIN_HOST_DIR=/tmp/jf/jellyfin`) was Failed 0, Passed 1234,
Skipped 44, Total 1278, both equal to the task 7.8 baseline because this review
adds no product test; the 145 focused secret-boundary, provider-failure,
webhook-resolution, state-boundary, metadata-state, artifact-retention, and
configuration-foundation tests also pass. No production fix was required and no
product code changed. Gate 7 is not declared.

**Task 7.5 status:** Complete. Phase 7 acceptance criterion 5 is met: the
release artifact is byte-reproducible and the build process is documented. The
non-reproducibility the task 7.8 reviewer found (MSBuild's `ZipDirectory`
enumerated the staging directory in filesystem order and stamped entries with
source modification times, so consecutive `./build.sh package` runs produced the
same contents with different SHA-256) is fixed by a deterministic packer. The
MSBuild `PackagePlugin` target now only stages the exact release files
(`ArrTags.dll`, `ArrTags.deps.json`, `build.yaml`, `THIRD-PARTY-NOTICES.md`, and
`licenses/`) into `artifacts/staging`, and `./build.sh package` invokes
`scripts/pack-release.cs` (a .NET 10 file-based app, not part of the solution,
adding no plugin dependency) to write `artifacts/ArrTags_0.1.0.0.zip` with
entries in ordinal order and one fixed ZIP timestamp (`2000-01-01 00:00:00`).
Two SDK git-derived inputs that also changed the assembly were suppressed so a
build from a git working tree matches a `.git`-less clean export:
`src/ArrTags/ArrTags.csproj` maps the project directory to the fixed root
`/_/ArrTags` via `PathMap` (the absolute PDB path was otherwise embedded), and
`Directory.Build.props` sets `IncludeSourceRevisionInInformationalVersion=false`
and `SuppressImplicitGitSourceLink=true`.

Clean-checkout evidence: two independent clean exports (526 files each, no
`.git`/`bin`/`obj`/`artifacts`, at different absolute paths) and a third run in
the first export after wiping its build outputs all produced the identical
archive SHA-256 `bd10b9b6bf5d31049082d27625b18ba127eb6e2860a454fe2d3c35ccebaaee51`
(567,856 bytes, 7 entries); the repository working tree (with `.git`) produced
the same hash. Build 0 warnings / 0 errors in every tree; default suite Failed
0, Passed 1218, Skipped 60, Total 1278; host-guarded suite
(`ARRTAGS_JELLYFIN_HOST_DIR=/tmp/jf/jellyfin`) Failed 0, Passed 1234, Skipped 44,
Total 1278 - both equal to the task 7.8/7.4 baseline because the task changes no
product test. The pinned inputs (`net10.0`, SDK `10.0.x` validated with
`10.0.401`, Jellyfin `12.0.0`/`targetAbi: 12.0.0.0`, plugin version `0.1.0.0`,
SkiaSharp `3.119.4` compile-time-only), the ADR-013 supported provider ranges
(Sonarr 3.x-4.x, Radarr 3.x-6.x on `/api/v3`), the exact commands, the artifact
identity and per-entry hashes, the verification steps, and the release checklist
are recorded in `docs/release/build-and-release.md`. No product behavior
changed; the changes are limited to the packaging/build configuration and the
new build-time packer script.

**Task 7.6 status:** Complete. The project's known limitations and deferred
decisions are consolidated into one canonical current-state document,
`docs/limitations.md`, so no deferred or unverified capability is presented as
available. It records the open functional/operational deferrals (no provider
inventory/catalogue cache, so Phase 6 acceptance criterion 1 remains partially
met; runtime configuration replacement not wired to Jellyfin's save path; no
bounded secret-free metrics/diagnostic-status surface; the
`QueueCapacity`-bounded reconciliation prefix; the host-supplied-SkiaSharp
dependency with no bundled fallback), the verification-coverage gaps (no live
Sonarr/Radarr instance; the unselected/unrun ADR-010 non-canonical cross-runtime
comparison; contract-level-only Jellyfin Enhanced coexistence; the manual-only
live `IProviderManager.SaveImage` read-back; the unexercised live
`Plugin.OnUninstalling` drain; the host-guarded skips in the default suite), the
packaging/release limitations (pinned-toolchain-dependent byte-reproducibility
and no byte-identity test; reduced debug metadata from the task 7.5
reproducibility trade-off; the `PackagePlugin=true` stage-only behavior; the
retained SkiaSharp license notices; pre-release orphaned-state and
uninstall-drain retention), and the two decision-record notes (the webhook `401`
framework ProblemDetails body versus ADR-012's "no body" text, recorded as a
minor gap for a future ADR-012 clarification; and the optional
`ArtworkSubjectGate` idle-eviction hardening). The document also states
explicitly which `GOALS.md` success criteria and Phase 7 acceptance criteria are
met and which carry limitations: all five Phase 7 acceptance criteria are met;
`GOALS.md` criteria 1-4, 8, and 9 are met as shipped; criterion 5 is met as
shipped but requires the host's SkiaSharp; criterion 6 is met for render and
publication but only partial for provider fetches; and criterion 7 is met only
at the contract level.

Carried reviewer documentation nits were fixed where they affected accuracy: the
Project Status paragraph and Milestone 7 row now list every completed Phase 7
task (finding 7.5-F5), and ADR-015 now labels the pinned host
`linux-musl-x64` (finding 7.8-R6) and states that `ExcludeAssets=runtime` still
leaves the `SkiaSharp.NativeAssets.Linux` `runtimeTargets` in
`ArrTags.deps.json` even though the native files are not staged into the package
(finding 7.8-R3). The task is documentation-only: no source, test, packaging, or
behavior change. Build 0 warnings / 0 errors; default suite Failed 0, Passed
1218, Skipped 60, Total 1278; host-guarded suite
(`ARRTAGS_JELLYFIN_HOST_DIR=/tmp/jf/jellyfin`) Failed 0, Passed 1234, Skipped 44,
Total 1278. Gate 7 is not declared; the Phase 7 review is separate.

**Acceptance criteria:**

- [x] The full test suite passes from a clean checkout. Met by task 7.1 from a
  clean rebuild (bin/obj removed, `./build.sh restore` in locked mode, `./build.sh
  build`, `./build.sh test`) against the exact declared versions - build 0
  warnings / 0 errors; full suite Failed 0, Passed 1215, Skipped 58, Total 1273
  (+18 over the Phase 6 baseline 1197/58/1255), with the new
  `ProviderContractFixtureTests` adding 18 ADR-013 provider-contract cases; no new
  skips.
- [x] The packaged plugin loads and operates on the declared Jellyfin 12 ABI.
  Met by task 7.7. Task 7.2 had found that the standard versioned install layout
  deleted the install folder on the next host restart once the plugin persisted
  state under the Jellyfin-derived data folder `data/plugins/ArrTags`; task 7.7
  relocates the state root to `ProgramDataPath/ArrTags` outside `PluginsPath`
  (ADR-014) and the re-run live install/upgrade/reload/uninstall verification
  passes with state present and the install folder preserved.
- [x] Sonarr and Radarr movie/television scenarios pass with unchanged and
  changed metadata. Met by task 7.8's live re-verification of the committed
  package on the pinned host: both providers enabled, the movie/episode matched
  and published, changed mock metadata (1080p -> 2160p/HDR10) produced new
  metadata/publication fingerprints, new image tags, and new served bytes, and
  an unchanged re-run left the fingerprint, tag, and bytes identical. Task 7.3
  separately verified independent provider enablement and matching.
- [x] Provider, matching, rendering, artwork, cache, and lifecycle failures do
  not adversely affect Jellyfin. Met by task 7.8: the first badge publication no
  longer aborts the host (0 `[FTL]`/`InvalidCastException`), a simulated provider
  outage (503) left the host up (10/10 HTTP 200) with the current artwork
  unchanged and the metadata state marked stale, and removing the plugin folder
  produced zero loads and a clean `Startup complete`.
- [x] The release artifact and build process are reproducible and documented.
  Met by task 7.5: `./build.sh package` produces a byte-identical
  `artifacts/ArrTags_0.1.0.0.zip` (SHA-256
  `bd10b9b6bf5d31049082d27625b18ba127eb6e2860a454fe2d3c35ccebaaee51`,
  567,856 bytes) across two independent clean checkouts, a repeated clean build,
  and the repository working tree, and the commands, inputs, supported version
  ranges, artifact identity, and release checklist are recorded in
  `docs/release/build-and-release.md`.

**Gate 7:** All V1 success criteria are checked, release blockers are resolved,
and the artifact is approved for release.

### 8. Release distribution

**Objective:** Make ArrTags installable through the standard Jellyfin plugin
catalog from the public repository, present a user-facing `README.md`, fix stale
user-visible metadata, and prepare/ship the `v1.0.1` release.

**Prerequisites:** Phase 7 complete and Gate 7 met; the release tag `v0.1.0`
exists at HEAD (`a634d61`); the plugin version is `0.1.0.0` in `build.yaml` and
`Directory.Build.props`; the public repository is `benssson/ArrTags` with default
branch `main`. No functional plugin behavior changes in this phase beyond
version/release metadata.

**Deliverables:**

- `README.md` as the end-user guide, with the current contributor/project-status
  content preserved in `docs/project-status.md`.
- `.opencode/agents/*.md` current-state references pointing at
  `docs/project-status.md`.
- Accurate `build.yaml` metadata and plugin version `1.0.1.0` in `build.yaml`
  and `Directory.Build.props`.
- Jellyfin plugin-repository manifest tooling (`scripts/write-manifest.cs`, a
  .NET 10 file-based app consistent with `scripts/pack-release.cs`;
  `scripts/publish-release.sh`) and the committed `manifest.json`.
- Updated `docs/release/build-and-release.md` and `docs/changelog.md`.
- The annotated tag `v1.0.1`; the GitHub release and asset upload remain a
  manual user step.

**Tasks:**

- [x] 8.1 Split `README.md` into an end-user guide and a preserved
  `docs/project-status.md`.
- [x] 8.2 Repoint the agent current-state references at `docs/project-status.md`.
- [x] 8.3 Correct the stale `build.yaml` metadata and bump the version to
  `1.0.1.0`.
- [x] 8.4 Add the Jellyfin plugin-repository manifest tooling and the release
  script.
- [x] 8.5 Update `docs/release/build-and-release.md` and `docs/changelog.md`.
- [x] 8.6 Release-readiness verification, commit the generated `manifest.json`,
  and create the annotated tag `v1.0.1`.

**Authoritative Phase 8 execution order:** 8.1, 8.2, 8.3, 8.4, 8.5, 8.6. Task
IDs are stable references only; this execution order is the canonical sequence.
The order is derived from the documented dependencies, not from task numbering:

- 8.1 has no prerequisites: it creates `docs/project-status.md` (the current
  contributor/project-status content preserved with an H1) and rewrites
  `README.md` as the end-user guide.
- 8.2 depends on 8.1 because the agent current-state references must point at
  the file 8.1 creates.
- 8.3 is independent of 8.1 and 8.2 and may run alongside them: it corrects the
  user-visible `build.yaml` metadata and bumps the version to `1.0.1.0` in
  `build.yaml` and `Directory.Build.props`.
- 8.4 depends on 8.3 because `scripts/write-manifest.cs` reads `build.yaml`
  (version, guid, ABI, changelog) and upserts the version entry in
  `manifest.json`, and `scripts/publish-release.sh` packages the
  `1.0.1.0` artifact.
- 8.5 depends on 8.3 and 8.4: it documents the final version/metadata, the
  repository URL and manifest, the `scripts/publish-release.sh` procedure, and
  the recomputed artifact identity produced by a `1.0.1.0` package build.
- 8.6 depends on 8.3, 8.4, and 8.5. The publish/tag capability comes from 8.3
  (version and metadata) and 8.4 (manifest tooling and release script), and 8.5
  must be final so the tagged commit carries the updated documentation. 8.6
  re-runs the full build/test/package at `1.0.1.0`, runs the publish script's
  dry run and prepare-only mode, confirms the manifest fields, commits
  `manifest.json`, and creates the annotated tag `v1.0.1`. The GitHub release
  and asset upload are explicitly left to the user.

#### 8.1 End-user README and preserved project status

**Status:** Complete. The current `README.md` contributor/project-status content
was moved byte-for-byte into the new `docs/project-status.md` under an
`# ArrTags project status` H1, and `README.md` was rewritten as the end-user
guide covering the plugin description, requirements (Jellyfin `12.0.0` only;
Sonarr `3.x`-`4.x` and/or Radarr `3.x`-`6.x` on `/api/v3`; the host supplies
SkiaSharp per `docs/limitations.md` F5), repository install, manual install, the
`ArrTags.xml` configuration fields with a minimal `PluginConfiguration` example
and the `X-ArrTags-Webhook-Secret` webhook endpoints, update, uninstall, and a
short known-limitations summary linking `docs/limitations.md`; the README links
`docs/project-status.md` and `docs/release/build-and-release.md`. No other file
changed.

**Objective:** Make `README.md` an end-user installation and usage guide and
preserve the current contributor/project-status content in
`docs/project-status.md`.

**Dependencies:** None.

**Affected files:** `README.md`, `docs/project-status.md` (new).

**Work:** Move the current `README.md` contributor/project-status content into
`docs/project-status.md` unchanged (add an H1 such as `# ArrTags project
status`), then rewrite `README.md` as the end-user guide covering: the plugin
description; requirements (Jellyfin `12.0.0` only; Sonarr `3.x`-`4.x` and/or
Radarr `3.x`-`6.x` on `/api/v3`; the host supplies SkiaSharp); repository
install (Dashboard -> Plugins -> Repositories -> add
`https://raw.githubusercontent.com/benssson/ArrTags/main/manifest.json` ->
Catalog -> ArrTags -> Install); a manual install fallback from the release zip;
configuration via `plugins/configurations/ArrTags.xml` plus restart (no web
configuration UI; state the `docs/limitations.md` F2 restart requirement; the
webhook routes `/ArrTags/Webhook/Sonarr` and `/ArrTags/Webhook/Radarr` with the
`X-ArrTags-Webhook-Secret` header); update; uninstall (restores the original
poster and removes `ProgramDataPath/ArrTags`); and a short known-limitations
summary linking `docs/limitations.md`. Link to `docs/project-status.md`.

**Tests:** Manual review that no contributor/status prose remains in
`README.md` and that every claimed install, configuration, update, and uninstall
path matches the shipped behavior and `docs/limitations.md`.

**Acceptance criteria:** `README.md` is end-user-facing and covers requirements,
repository install, manual install, configuration, update, and uninstall; no
contributor/status prose remains in it; the contributor/status content is
preserved in `docs/project-status.md` and linked from `README.md`.

**Documentation impact:** `README.md` (rewritten), `docs/project-status.md`
(new).

#### 8.2 Repoint the agent current-state references

**Status:** Complete. In each of the five affected agent prompts
(`phase-reviewer.md`, `implementation-reviewer.md`,
`documentation-maintainer.md`, `implementation-worker.md`,
`release-reviewer.md`), the canonical-current-state-surface entry
`README.md` - current build/test/structure/next-step statements was replaced
with `docs/project-status.md` - current build/test/structure/next-step
statements; the general `README.md` entries under each prompt's "Required
Inputs" list were left unchanged, and no other file changed.

**Objective:** Point the agent prompts at `docs/project-status.md` instead of
`README.md` for current build/test/structure/next-step statements.

**Dependencies:** 8.1 (the target file must exist).

**Affected files:** `.opencode/agents/phase-reviewer.md`,
`.opencode/agents/implementation-reviewer.md`,
`.opencode/agents/documentation-maintainer.md`,
`.opencode/agents/implementation-worker.md`,
`.opencode/agents/release-reviewer.md`.

**Work:** In each file, replace the canonical-current-state-surface entry
`README.md` - current build/test/structure/next-step statements with
`docs/project-status.md` - current build/test/structure/next-step statements.
The general `README.md` entries under each prompt's "Required Inputs" list are
unchanged, because the end-user `README.md` remains a valid project document.

**Tests:** Search `.opencode/agents/` for the "current
build/test/structure/next-step statements" wording and confirm no `README.md`
entry remains for it and that `docs/project-status.md` is referenced.

**Acceptance criteria:** No agent prompt claims `README.md` is the source of
current build/test/structure/next-step statements; `docs/project-status.md` is
the referenced source.

**Documentation impact:** `.opencode/agents/*.md` only.

#### 8.3 Correct plugin metadata and bump to 1.0.1.0

**Status:** Complete. `build.yaml` `overview`/`description`/`changelog` now
describe the shipped v1 behavior (reads metadata from independently configured
Sonarr and Radarr instances and publishes configurable badges, for example the
actual file quality, onto Jellyfin Movie and Episode posters through Jellyfin's
supported item-image APIs while preserving the original poster), replacing the
false "performs no provider or artwork I/O" foundation text. The version is
`1.0.1.0` in `build.yaml` and in `Directory.Build.props`
(`<Version>`/`<AssemblyVersion>`/`<FileVersion>`); `guid`, `targetAbi`,
`framework`, `category`, `owner`, and the `artifacts` list are unchanged. The
hard-coded `0.1.0.0` install-folder, `meta.json`, and manifest-version literals in
`PluginPackagingTests`, `PluginStateLocationTests`, and `PluginDiscoveryHostTests`
were updated to `1.0.1.0` so the suite passes at the new version. The build,
test, and package commands were re-run and produce
`artifacts/ArrTags_1.0.1.0.zip`; no other file changed.

**Objective:** Fix the stale user-visible `build.yaml` metadata and set the
release version to `1.0.1.0`.

**Dependencies:** None.

**Affected files:** `build.yaml`, `Directory.Build.props`.

**Work:** Rewrite `build.yaml` `overview`/`description`/`changelog` so they
describe the shipped v1 behavior (reads independently configured Sonarr and
Radarr metadata, matches eligible Jellyfin Movie and Episode posters, and renders
and publishes configurable badges through Jellyfin's supported item-image APIs)
and remove the stale "performs no provider or artwork I/O" foundation text. Set
`version: "1.0.1.0"` in `build.yaml` and `<Version>`/`<AssemblyVersion>`/
`<FileVersion>` to `1.0.1.0` in `Directory.Build.props`. Do not change `guid`,
`targetAbi`, `framework`, `category`, `owner`, or the `artifacts` list.

**Tests:** Build at `1.0.1.0`; assert the produced package is
`artifacts/ArrTags_1.0.1.0.zip` and that `build.yaml` version, ABI, and
framework match the declared pins. `PluginPackagingTests` continues to assert the
package contract.

**Acceptance criteria:** `build.yaml` metadata accurately describes the shipped
v1 behavior and the version is `1.0.1.0` in both `build.yaml` and
`Directory.Build.props`.

**Documentation impact:** `build.yaml`, `Directory.Build.props`.

#### 8.4 Jellyfin repository manifest tooling and release script

**Status:** Complete. Added `scripts/write-manifest.cs`, a .NET 10 file-based app
(not part of the solution, no plugin dependency) that reads `build.yaml` and
upserts the Jellyfin plugin-repository version entry in `manifest.json` - plugin
`category`/`guid`/`name`/`description`/`owner`/`overview` and per-version
`checksum` (MD5), `changelog`, `targetAbi`, `sourceUrl`, `timestamp`, `version`,
newest-first - preserving other plugin and version entries and writing stable
indented JSON; it also supports `--print-changelog` for release notes. Added
`scripts/publish-release.sh` with `--dry-run`, `--prepare-only`, and
`--release-only` modes over `./build.sh restore/build/test/package`, SHA-256 and
MD5 computation, manifest generation, git commit/tag/push (with `--no-push` and
`--force`), and a `gh`/GitHub-REST-API release with post-upload MD5 verification.
The dry run generated `manifest.json` (guid
`40322d52-5680-449f-b33e-e01836ee2f46`, version `1.0.1.0`, targetAbi
`12.0.0.0`, sourceUrl
`https://github.com/benssson/ArrTags/releases/download/v1.0.1/ArrTags_1.0.1.0.zip`,
MD5 `16baa5a7324b8e14fdb113d84b944d09`) with no commit, tag, push, or GitHub
call. No other file changed.

**Objective:** Add the tooling that generates the Jellyfin plugin-repository
`manifest.json` and the script that performs the release.

**Dependencies:** 8.3 (the version and metadata the tooling reads).

**Affected files:** `scripts/write-manifest.cs` (new),
`scripts/publish-release.sh` (new), generated `manifest.json` (new).

**Work:**

- `scripts/write-manifest.cs`: a .NET 10 file-based app consistent with
  `scripts/pack-release.cs` (not part of the solution, adding no plugin
  dependency) that reads `build.yaml` and any existing `manifest.json`, and
  upserts the version entry with the correct `guid`, `version`, `targetAbi`,
  `timestamp`, `changelog`, MD5 `checksum`, and `sourceUrl`
  (`https://github.com/benssson/ArrTags/releases/download/v1.0.1/ArrTags_1.0.1.0.zip`),
  writing stable indented JSON.
- `scripts/publish-release.sh`: build/test/package, compute SHA-256 and MD5,
  regenerate the manifest, commit it, create the annotated `v1.0.1` tag if
  absent, push, then create the GitHub release and upload the asset using `gh`
  or a `GITHUB_TOKEN`. It must support a dry run (compute and report with no
  GitHub write, commit, or tag), a prepare-only mode (generate and commit the
  manifest and create the tag without a token) so the agent can prepare the
  release, and a release-only mode for the user's manual publish.

**Tests:** Run the dry run and confirm it computes the checksum and writes
`manifest.json` with the correct guid, version, targetAbi, checksum, and
sourceUrl and performs no GitHub write, commit, or tag. Confirm the checksum
equals `md5sum artifacts/ArrTags_1.0.1.0.zip` and that the manifest parses as
the expected Jellyfin repository shape. Run `bash -n`/shellcheck on the script.

**Acceptance criteria:** `scripts/write-manifest.cs` and
`scripts/publish-release.sh` generate a valid Jellyfin repository manifest
(correct guid, version, targetAbi, MD5 checksum, and sourceUrl) with no GitHub
write during a dry run.

**Documentation impact:** recorded in `docs/release/build-and-release.md` by
task 8.5.

#### 8.5 Release and changelog documentation

**Status:** Complete. In `docs/release/build-and-release.md` the pinned plugin
version is `1.0.1.0`; the "Release artifact identity" section now records
`artifacts/ArrTags_1.0.1.0.zip` (568,248 bytes, SHA-256 `de4c3484…`, MD5
`16baa5a7…`, 7 entries) with a regenerated per-entry SHA-256 table produced by
running `. /config/arrtags-env.sh && ./build.sh package` and hashing the
extracted entries; and a new "Jellyfin plugin repository" section documents the
root `manifest.json`, the repository URL
(`https://raw.githubusercontent.com/benssson/ArrTags/main/manifest.json`), the
manifest fields and MD5 checksum, and the `scripts/publish-release.sh`
`--dry-run`/`--prepare-only`/`--release-only` procedure. The task 7.5/SEC-1
evidence is retained and relabelled as `0.1.0` history so the superseded
`bd10b9b6…`/`f6b6a515…` identities are not presented as current.
`docs/changelog.md` gains the Phase 8 section; `docs/limitations.md` P1/P2,
`docs/project-status.md`, `docs/testing/jellyfin-12-musl-test-host.md`, and the
`Directory.Build.props` version comment (reviewer finding 8.3-R1) are updated to
the `1.0.1.0` identity. `./build.sh package` reproduced the recorded `1.0.1.0`
identity (7 entries, no SkiaSharp runtime). No source, test, packaging, or
behavior change.

**Objective:** Record the repository manifest, repository URL, release
procedure, and recomputed artifact identity, and record Phase 8.

**Dependencies:** 8.3, 8.4.

**Affected files:** `docs/release/build-and-release.md`, `docs/changelog.md`,
`docs/limitations.md`, `docs/project-status.md`,
`docs/testing/jellyfin-12-musl-test-host.md`, `Directory.Build.props` (version
comment only).

**Work:** In `docs/release/build-and-release.md`, update the pinned version and
artifact identity to `1.0.1.0`, and document the Jellyfin repository
`manifest.json`, the repository URL
(`https://raw.githubusercontent.com/benssson/ArrTags/main/manifest.json`), the
`scripts/publish-release.sh` procedure (dry run, prepare-only, and release-only
modes), and the recomputed artifact identity for `artifacts/ArrTags_1.0.1.0.zip`
(size, SHA-256, MD5, entries). In `docs/changelog.md`, add the Phase 8 section
recording the release-distribution work. Reconcile the remaining canonical
current-state surfaces that still carry the `0.1.0.0` release identity:
`docs/limitations.md` (the P1 byte-reproducibility identity and the P2
`AssemblyInformationalVersion` note), `docs/project-status.md` (the current
milestone/status framing and the release-build artifact identity),
`docs/testing/jellyfin-12-musl-test-host.md` (the reproduction step that extracts
`artifacts/ArrTags_0.1.0.0.zip`), and the `Directory.Build.props` version comment
(reviewer finding 8.3-R1). Do not rewrite historical changelog/PLANS entries or
`docs/implementation/*` reports.

**Tests:** Manual consistency review: the recorded artifact identity matches the
built `1.0.1.0` package, the manifest fields match `build.yaml` and
`manifest.json`, and no stale `0.1.0.0` release identity remains in a canonical
current-state surface (only historical entries may retain it).

**Acceptance criteria:** `docs/release/build-and-release.md`, `docs/changelog.md`,
`docs/limitations.md`, `docs/project-status.md`, and
`docs/testing/jellyfin-12-musl-test-host.md` reflect the new state (repository
manifest, repository URL, release procedure, recomputed artifact identity, and
the Phase 8 record), and the `Directory.Build.props` version comment matches
`1.0.1.0`.

**Documentation impact:** the named documents and the `Directory.Build.props`
comment.

#### 8.6 Release-readiness verification, manifest commit, and tag v1.0.1

**Status:** Complete. The full build/test/package ran at `1.0.1.0`: `./build.sh
build` reported 0 warnings / 0 errors; the default suite was Failed 0, Passed
1228, Skipped 60, Total 1288, and the host-guarded suite
(`ARRTAGS_JELLYFIN_HOST_DIR=/tmp/jf/jellyfin`) was Failed 0, Passed 1244,
Skipped 44, Total 1288; `./build.sh package` reproduced
`artifacts/ArrTags_1.0.1.0.zip` (568,248 bytes, SHA-256
`de4c34841d77b5ff74b6bc9edeb515a4c5fcc5a9b09d7d24a2da5d64d31b4b8c`, MD5
`16baa5a7324b8e14fdb113d84b944d09`, 7 entries, no bundled `SkiaSharp.dll`/
`libSkiaSharp.so`). `scripts/publish-release.sh --dry-run` computed the same
checksums and wrote `manifest.json` with no commit, tag, push, or GitHub call,
and `--prepare-only --no-push --skip-build --skip-tests` regenerated
`manifest.json` (guid `40322d52-5680-449f-b33e-e01836ee2f46`, version `1.0.1.0`,
targetAbi `12.0.0.0`, checksum `16baa5a7324b8e14fdb113d84b944d09`, sourceUrl
`https://github.com/benssson/ArrTags/releases/download/v1.0.1/ArrTags_1.0.1.0.zip`),
committed it as `8cba85b` ("Publish ArrTags 1.0.1.0 repository manifest"), and
created the annotated tag `v1.0.1` (tag `v1.0.1^{commit}` =
`8cba85b2288a201f4e2c7eda58d44c71f9c4f096`), with no push. The GitHub release and
asset upload remain the user's manual step with `scripts/publish-release.sh
--release-only`. Gate 8 is met (the Phase 8 review approved in
`docs/implementation/phase-8/phase-review.json`).

**Objective:** Verify the release at `1.0.1.0`, commit the generated manifest,
and create the annotated tag.

**Dependencies:** 8.3, 8.4, 8.5.

**Work:** Full build/test/package at `1.0.1.0`; run the publish script's dry run
and prepare-only mode; confirm the manifest `guid`, `version`, `targetAbi`,
`checksum`, and `sourceUrl` are correct and match the built artifact; commit the
generated `manifest.json`; create the annotated tag `v1.0.1`. Do not create the
GitHub release or upload the asset - that remains the user's manual step with
`scripts/publish-release.sh`.

**Tests:** Default and host-guarded test suites pass; `PluginPackagingTests`
passes; `unzip -l` shows the expected entries; the manifest checksum equals
`md5sum artifacts/ArrTags_1.0.1.0.zip`; the dry run performs no GitHub write.

**Acceptance criteria:** `manifest.json` is committed and the annotated tag
`v1.0.1` exists; the GitHub release and asset upload remain a manual user step.

**Documentation impact:** committed `manifest.json`; the annotated tag.

**Phase 8 acceptance criteria:**

- [x] `README.md` is end-user-facing and covers requirements, repository
  install, manual install, configuration, update, and uninstall; no
  contributor/status prose remains in it.
- [x] The contributor/status content is preserved in `docs/project-status.md`
  and linked from `README.md`.
- [x] `build.yaml` metadata accurately describes the shipped v1 behavior and the
  version is `1.0.1.0` in both `build.yaml` and `Directory.Build.props`.
- [x] `scripts/write-manifest.cs` and `scripts/publish-release.sh` generate a
  valid Jellyfin repository manifest (correct guid, version, targetAbi, MD5
  checksum, and sourceUrl) with no GitHub write during a dry run.
- [x] `manifest.json` is committed and the annotated tag `v1.0.1` exists; the
  GitHub release/asset upload remains a manual user step.
- [x] Canonical docs (`docs/release/build-and-release.md`, `docs/changelog.md`)
  and the agent prompt references reflect the new state.
- [x] No functional plugin behavior changed beyond version/release metadata.

**Gate 8:** Met. All seven Phase 8 acceptance criteria are met and the Phase 8
review is approved (`docs/implementation/phase-8/phase-review.json`,
`reviewer_status: APPROVED`, `phase_complete: true`); the annotated tag `v1.0.1`
is created. The GitHub release publication (which pushes the branch/tag and
uploads the asset) is explicitly left to the user via
`scripts/publish-release.sh --release-only`.

## v1.1 Milestones (Phases 9-14)

**Scope:** The accepted v1.1 scope is [`docs/planning/v1.1.md`](docs/planning/v1.1.md)
(Goals A-F, decision gates DG-10..DG-14, dependency order section 5, task outline
section 6, documentation checklist section 7, verification requirements section
9). Its decision gates are resolved by ADR-016..ADR-020, which are binding for
these phases. V1 Phases 1-8 are complete and are not restructured, reordered, or
reopened; the v1.1 phases are additive, starting at Phase 9. V1.1-1 (the v1.1
plan, ADR-016..ADR-020, and the GOALS.md/PLANS.md pointers) is already complete at
commit `5ec8ae2` and is not re-planned as open work.

**Phase set and ordering.** The phases follow the v1.1 dependency order
(section 5): Goal A first (the delivery vehicle for the new settings; resolves
limitation F2), Goal F next (cross-cutting; rewrites SEC-5), Goal C next
(independent; shares Goal A's runtime-configuration plumbing), Goals B and E last
(both output-affecting), Goal D and the consolidated documentation pass next, and
the v1.1 release last. Goals B and E are deliberately one phase: ADR-017 and
ADR-019 define a **single shared** renderer-configuration schema advance
(`1 -> 2`) and `RenderVersion` advance (`2 -> 3`) with one golden regeneration, so
splitting them across phases would either double the advance or leave one goal's
`RenderVersion` acceptance criterion unmet at its own gate.

**Phase-transition criterion.** A phase is finalized only after its
`phase-reviewer` gate passes (`reviewer_status: APPROVED`,
`phase_complete: true`, `ready_for_next_phase: true`, no unresolved BLOCKER or
HIGH findings) and the phase tag `v1.1.0-phase<N>` is created. Phases do not
auto-advance: the orchestrator must not begin the next phase until the current
phase gate is met and, where the process requires it, the user has approved
continuation. Per-goal acceptance criteria are met at the integration-test level
within the goal phase; the consolidated live pinned-host verification is owned by
task 14.3 (this matches the V1 pattern, where Phase 5 criteria were met at the
integration-test level and live verification landed in Phase 7).

**Decision gates.** DG-10..DG-14 are all resolved (ADR-016..ADR-020) and no
unresolved gate blocks any v1.1 task. ADR-016 clause 7's get-only `Collection<T>`
round-trip test (task 9.1) and the page-resource authorization confirmation
(task 9.2) are verification requirements of an already-resolved gate, not open
decisions.

**v1.1 phase map and task-outline mapping.** The flat task outline in
`docs/planning/v1.1.md` section 6 maps to the phase tasks as follows, so no
planned work is silently dropped:

| Outline task | Goal(s) | Phase tasks |
| --- | --- | --- |
| V1.1-1 | planning | Complete (commit `5ec8ae2`): `docs/planning/v1.1.md`, ADR-016..ADR-020, GOALS.md/PLANS.md pointers. Not open work. |
| V1.1-2 | A | 9.1, 9.2, 9.3, 9.4, 9.5 |
| V1.1-3 | F | 10.1, 10.2, 10.3 |
| V1.1-4 | C | 11.1, 11.2, 11.3, 11.4 |
| V1.1-5 | B | 12.1, 12.2, 12.4 |
| V1.1-6 | E | 12.3, 12.4 |
| V1.1-7 | D + all | 13.1, 13.2 |
| V1.1-8 | release | 14.1, 14.2, 14.3, 14.4, 14.5 |

### 9. Dashboard settings UI and runtime configuration activation

**Goal:** Goal A (ADR-016). **Release:** v1.1.0. **Phase tag:** `v1.1.0-phase9`.

**Objective:** Add a Jellyfin dashboard settings page for ArrTags and wire the
elevation-gated save path to runtime configuration activation, so a saved change
applies without a host restart and triggers a bounded reconciliation that
re-renders affected posters. This resolves limitation F2.

**Deliverables:**

- A proven get-only `Collection<T>` configuration round-trip (or the corrected
  configuration shape) as the blocking prerequisite.
- A Jellyfin dashboard settings page (`IHasWebPages` plus an embedded
  `ArrTags.Configuration.config.html`) that loads and saves the user-adjustable
  settings without embedding or returning any secret.
- An elevation-gated save path through the supported `PluginsController` route,
  with a `Plugin.UpdateConfiguration` override that activates a validated
  replacement and retains the last valid snapshot (and private secrets) on an
  invalid one.
- Runtime activation and a bounded, non-blocking post-save reconciliation
  trigger.
- Documentation updates: `docs/architecture.md` section 6,
  `docs/data-model.md` 3.12, `docs/limitations.md` F2, `README.md`
  Configuration.

**Tasks:**

- [x] 9.1 Configuration round-trip spike (blocking prerequisite).
- [x] 9.2 Dashboard settings page and embedded page resource.
- [x] 9.3 Elevation-gated save path and runtime activation.
- [x] 9.4 Bounded post-save reconciliation trigger.
- [x] 9.5 Goal A documentation and integration verification.

**Authoritative Phase 9 execution order:** 9.1, 9.2, 9.3, 9.4, 9.5. Task IDs are
stable references only; this execution order is the canonical sequence. When the
execution order and task numbering conflict, the execution order wins.

| Order | Task | Depends on |
| --- | --- | --- |
| 9.1 | Configuration round-trip spike | — |
| 9.2 | Dashboard settings page and embedded page resource | 9.1 |
| 9.3 | Elevation-gated save path and runtime activation | 9.1, 9.2 |
| 9.4 | Bounded post-save reconciliation trigger | 9.3 |
| 9.5 | Goal A documentation and integration verification | 9.4 |

#### 9.1 Configuration round-trip spike (blocking prerequisite)

**Status:** Complete. The pinned Jellyfin 12.0.0 elevation-gated
`PluginsController` POST deserializes with
`Jellyfin.Extensions.Json.JsonDefaults.Options`, whose default `System.Text.Json`
object-creation handling does not populate a get-only collection property, so the
get-only `PluginConfiguration.EnabledLibraries` and
`RendererConfiguration.Selectors` were silently dropped on a save. Both are now
settable with a null-coalescing setter (null is treated as empty) and are
populated by the round-trip; the persisted XML shape is unchanged, so existing
`ArrTags.xml` files remain loadable. `ConfigurationRoundTripTests` proves the
round-trip over the pinned options, the XML persist/reload, and one explicit
assertion per collection, with a host-guarded `PluginsController` confirmation
that is skipped without `ARRTAGS_JELLYFIN_HOST_DIR`. `./build.sh build` reported
0 warnings / 0 errors; the default suite was Failed 0, Passed 1234, Skipped 61,
Total 1295. Recorded in `docs/data-model.md` 3.12 and `docs/changelog.md`.

**Objective:** Prove that the supported elevation-gated `PluginsController` POST
round-trip populates ArrTags' get-only `Collection<T>` configuration properties
(`PluginConfiguration.EnabledLibraries`, `RendererConfiguration.Selectors`); if
it does not, change the configuration shape (for example settable collection
properties) before the settings page is built, so a save cannot silently drop
them (ADR-016 clause 7, first bullet).

**Traceability:** Goal A; ADR-016 clause 7; v1.1 DG-10 verification requirement.

**Dependencies:** None. First v1.1 task.

**Affected files/components:** `src/ArrTags/Configuration/PluginConfiguration.cs`,
`src/ArrTags/Configuration/RendererConfiguration.cs`, and the configuration
round-trip test surface under `tests/ArrTags.Tests/`.

**Work:**

- Build a round-trip test that deserializes a representative settings payload
  into `PluginConfiguration` using the same PascalCase `System.Text.Json` options
  the pinned `PluginsController` uses, then persists and reloads through the
  existing XML configuration boundary.
- Assert that `EnabledLibraries` and `RendererConfiguration.Selectors` (and any
  other get-only collection property) are populated rather than silently dropped.
- If the round-trip fails, change the configuration shape so the collections
  survive, record the shape change in `docs/data-model.md` 3.12, and, if it
  changes an accepted configuration contract, surface it as an architecture
  decision before the page is built rather than guessing.

**Tests:** Unit round-trip test over the pinned deserialization options; XML
persist/reload round-trip; an explicit assertion per get-only collection.
Host-guarded confirmation against the pinned `PluginsController` where the pinned
host is available.

**Acceptance criteria:** A test proves the POST round-trip populates the get-only
collection properties, or the configuration shape is changed so it does; a save
cannot silently drop `EnabledLibraries` or `RendererConfiguration.Selectors`.

**Decision gates:** DG-10 resolved by ADR-016; this task owns ADR-016 clause 7
first bullet. No unresolved gate.

**Review:** `test-quality-reviewer` (the honesty and determinism of the blocking
round-trip test).

**Documentation impact:** `docs/data-model.md` 3.12 if the configuration shape
changes.

**Definition of done:** The round-trip test passes (or the shape is corrected and
the test passes), and the settings page is unblocked.

#### 9.2 Dashboard settings page and embedded page resource

**Status:** Complete. `Plugin` implements `MediaBrowser.Model.Plugins.IHasWebPages`
and returns one `PluginPageInfo` (`Name` `ArrTags`, `EnableInMainMenu = false`)
whose `EmbeddedResourcePath` is the embedded `Configuration/config.html` with the
explicit logical name `ArrTags.Configuration.config.html` (ADR-016 clause 1). The
page follows the in-tree Jellyfin page pattern (the `configPage`/
`pluginConfigurationPage`/`configPage` classes, `data-require`, the `pageshow`
event, `ApiClient.getPluginConfiguration`/`updatePluginConfiguration`, and
`Dashboard.processPluginConfigurationUpdateResult`) and reads/writes every
user-adjustable setting in the current configuration model: the Sonarr and Radarr
connections and their API keys, the webhook secret, the Movie/Episode poster
flags, the enabled-library scope, the renderer selectors/templates and palette
overrides, and all operational limits. The three secret inputs are password
fields with no embedded value; the page embeds no secret literal and exposes a
secret only through the same administrator-gated configuration API that already
returns it (ADR-016 clauses 2 and 6). `DashboardSettingsPageTests` covers the
`GetPages()` contract, the embedded resource logical name, the page contract,
field coverage of the configuration model (a temporary rename was used to confirm
the coverage assertion is non-vacuous), the no-unknown-property match, and the
secret-free source. Two host-guarded facts confirm the pinned
`DashboardController` serves the page resource by name and that the
page-resource action is anonymous while `GetConfigurationPages` stays
`[Authorize(Policy = Policies.RequiresElevation)]`; both are skipped without
`ARRTAGS_JELLYFIN_HOST_DIR`. ADR-016 clause 6's explicit acceptance of the
anonymous static page-resource endpoint is recorded in `docs/architecture.md`
section 6. `./build.sh build` reported 0 warnings / 0 errors; the default suite
was Failed 0, Passed 1240, Skipped 63, Total 1303. The elevation-gated
`UpdateConfiguration` activation (9.3) and the post-save reconciliation trigger
(9.4) are not part of this task; limitation F2 therefore remains open and the
README still states that a saved change is not observed until the process
restarts.

**Objective:** Implement `IHasWebPages` on `Plugin` and an embedded read/write
settings page that loads and saves the user-adjustable settings without embedding
or returning any secret, following the in-tree Jellyfin page pattern (ADR-016
clauses 1, 2, 6, and 7 second bullet).

**Traceability:** Goal A; ADR-016 clauses 1, 2, 6, 7.

**Dependencies:** 9.1.

**Affected files/components:** `src/ArrTags/Plugin.cs`,
`src/ArrTags/Configuration/config.html` (new embedded resource),
`src/ArrTags/ArrTags.csproj` (embedded resource logical name), page-resource and
host-guarded tests under `tests/ArrTags.Tests/`, `README.md` Configuration,
`docs/architecture.md` section 6.

**Work:**

- Implement `IHasWebPages.GetPages()` returning one `PluginPageInfo` (name
  `ArrTags`, `EnableInMainMenu = false`) whose `EmbeddedResourcePath` is the
  embedded `Configuration/config.html` with the explicit logical name
  `ArrTags.Configuration.config.html`.
- Build the page read/write for the user-adjustable settings only: provider
  connections and secrets, the webhook secret, poster/library scope, renderer
  selectors/templates/palette, the operational limits, and the settings surface
  the later v1.1 goals add. Embed no secret and expose no secret value beyond
  what Jellyfin's existing administrator configuration API already returns.
- Confirm the anonymous static page-resource behavior on the pinned host
  (host-guarded test or live confirmation) and record the explicit acceptance
  from ADR-016 clause 6.

**Tests:** `GetPages()` returns the expected page; the embedded resource logical
name resolves; a host-guarded `DashboardController` fetch serves the page; the
page source contains no secret or credential literal; the page fields match the
configuration model.

**Acceptance criteria:** A plugin settings page appears in the Jellyfin dashboard
and loads its current configuration; no secret is embedded in the page or
returned beyond Jellyfin's existing admin configuration behavior; the
page-resource authorization behavior is confirmed.

**Decision gates:** DG-10 resolved by ADR-016; this task owns ADR-016 clause 7
second bullet.

**Review:** `security-reviewer` (the new page surface and its secret-free
guarantee).

**Documentation impact:** `README.md` Configuration; `docs/architecture.md`
section 6.

**Definition of done:** The page loads in the dashboard with current values and
contains no secret.

#### 9.3 Elevation-gated save path and runtime activation

**Status:** Complete. `Plugin` overrides `MediaBrowser.Common.Plugins.BasePlugin<T>.UpdateConfiguration`
(ADR-016 clause 4). The override validates the candidate before the base
implementation persists anything, using the same `PluginConfigurationValidator`
the snapshot service uses. A valid candidate is then persisted by
`base.UpdateConfiguration(configuration)` and activated by
`ConfigurationSnapshotService.TryReplace((PluginConfiguration)configuration, out errors)`,
so a saved change is applied without a host restart. An invalid candidate is
rejected before persistence: the override does not call the base implementation,
so a rejected candidate is never written to
`plugins/configurations/ArrTags.xml` and the last valid public snapshot and
private secret map stay active (closing security finding S-9.2-03). The whole
validate/persist/activate sequence is serialized, so concurrent elevation-gated
saves cannot leave the running snapshot, `Plugin.Configuration`, and the persisted
file divergent (security finding SEC-9.3-01). The bounded, secret-free validation
result is retained on the plugin instance for diagnostics. The rejection is also
surfaced to the administrator as exactly one bounded, secret-free activity-log
entry (ADR-021): the plugin-owned `IConfigurationRejectionNotifier` boundary
isolates the host `IActivityManager` coupling behind a Jellyfin-free contract, and
the `JellyfinConfigurationRejectionNotifier` implementation writes a fixed
name/type, `Guid.Empty` user, `Warning` severity entry built only from bounded
validation reasons (at most eight, control characters stripped, truncated to the
512/256 `ActivityLog` column bounds); a valid save writes no entry. The override
contains service-resolution, validation, and notification failures and never
throws into the host, and it adds no custom configuration-save route (ADR-016
clause 3). Services that resolve from the current snapshot per operation (work
queue capacity and in-flight bound, provider/render concurrency, metadata
freshness, badge definitions, and the renderer output policy) observe the replaced
snapshot without rebuilding the singletons (ADR-016 clause 5 first bullet); a
pre-existing subset of singletons captures the artifact-size/decode and
cache/quota/retention limits at construction. New tests
(`tests/ArrTags.Tests/ConfigurationActivationTests.cs`, 11 facts, and
`tests/ArrTags.Tests/ConfigurationRejectionNotifierTests.cs`, 9 facts): a valid
save activates without restart and writes no entry; an invalid save retains the
last valid snapshot and private secrets, does not persist the rejected candidate,
and writes exactly one bounded secret-free activity entry; a concurrent
valid+invalid save keeps the snapshot, in-memory configuration, and persisted file
consistent; the override never throws (including when the snapshot service is
unavailable or the notifier fails); no custom configuration-save route is added;
and a per-operation consumer observes the replacement. `./build.sh build` reported
0 warnings / 0 errors; the default suite was Failed 0, Passed 1260, Skipped 63,
Total 1323 (baseline Failed 0, Passed 1240, Skipped 63, Total 1303; +20 passed,
+20 total). The bounded post-save reconciliation trigger (9.4) is not part of this
task; limitation F2's restart consequence is gone but F2 is not recorded as
resolved until 9.5.

**Objective:** Override `Plugin.UpdateConfiguration` to validate the candidate
before the base implementation persists it: for a valid candidate, call `base`
and then `ConfigurationSnapshotService.TryReplace`, so a saved change is
activated at runtime without a host restart; an invalid candidate is rejected
before persistence (it is never passed to `base`) with the last valid snapshot
(and private secrets) retained; the bounded validation failure is surfaced to the
administrator; the override never throws into the host; and configuration is
saved only through the supported elevation-gated `PluginsController` path with no
custom save route (ADR-016 clauses 3, 4, and 5 first bullet).

**Traceability:** Goal A; ADR-016 clauses 3, 4, 5.

**Dependencies:** 9.1, 9.2.

**Affected files/components:** `src/ArrTags/Plugin.cs`,
`src/ArrTags/Configuration/ConfigurationSnapshotService.cs`, configuration
activation tests, `docs/data-model.md` 3.12, `docs/limitations.md` F2,
`docs/architecture.md` section 6.

**Work:**

- Override `UpdateConfiguration`: validate the candidate first; for a valid
  candidate call `base.UpdateConfiguration(configuration)` and then
  `ConfigurationSnapshotService.TryReplace((PluginConfiguration)configuration,
  out errors)`; an invalid candidate is never passed to `base`, so it is never
  persisted.
- On success, activate the new public snapshot and private secret generation; on
  failure, retain the last valid snapshot and private secrets and surface the
  bounded, secret-free validation failure.
- Ensure the override never throws into the host and that no custom
  configuration-save route is added.
- Verify that services that already resolve limits, concurrency, freshness,
  retention, badge definitions, and the renderer output policy from the current
  snapshot per operation observe the replaced snapshot.

**Tests:** Valid save applies without restart; invalid save retains last-valid and
private secrets; the validation failure is surfaced safely; no throw; no custom
route; per-operation snapshot resolution observes the replacement.

**Acceptance criteria:** Saving a valid change applies it at runtime without a
host restart; an invalid change is rejected and the last valid snapshot is
retained; configuration is saved only through the elevation-gated supported path.

**Decision gates:** DG-10 resolved by ADR-016.

**Review:** `security-reviewer` (elevation boundary, secret retention, no custom
save route).

**Documentation impact:** `docs/data-model.md` 3.12; `docs/limitations.md` F2;
`docs/architecture.md` section 6.

**Definition of done:** Valid/invalid save behavior and secret retention are
deterministic and covered by tests.

#### 9.4 Bounded post-save reconciliation trigger

**Status:** Complete. `Plugin.UpdateConfiguration` now requests the bounded,
non-blocking post-save reconciliation after a successful replacement (ADR-016
clause 5 second bullet). The host coupling is isolated behind the plugin-owned,
Jellyfin-free `IConfigurationReconciliationTrigger` boundary
(`RequestReconciliation()`), which `Plugin` resolves from the host service
provider and calls outside the save gate; a trigger-resolution or trigger
failure is contained and never throws into the host. The production
`ConfigurationReconciliationTrigger` is a hosted singleton: its loop waits on a
bounded request slot (`SemaphoreSlim(0, 1)`) and runs the existing bounded
`LibraryReconciliationService` off the save thread with the new
`LibraryReconciliationSource.PostSave` source, so the save response never waits
for a full-library scan. At most one reconciliation is pending; a redundant
request is coalesced, and a request that arrives while a reconciliation is
running schedules exactly one bounded rerun (a run reads the current snapshot
once when it starts), so the latest replaced snapshot is still observed. On
shutdown the loop is cancelled and awaited within a bounded timeout. No parallel
queue and no synchronous scan is added: the trigger reuses the existing
reconciliation boundary, which enqueues the same provider-neutral
`LibraryWorkHint` work as the scheduled, manual, and post-scan triggers. A work
item whose `ConfigurationVersion` is older than the current snapshot is still
skipped by `ArtworkPublishingWorkItemProcessor`, so the trigger is required for
existing posters to re-render promptly rather than on the next library event,
webhook, post-scan, or scheduled run. New tests
(`tests/ArrTags.Tests/ConfigurationReconciliationTriggerTests.cs`, 10 facts):
a successful save requests exactly one bounded reconciliation and activates the
snapshot; an invalid save requests none; a valid candidate that could not be
activated (the running snapshot service is unavailable) also requests none; a
failing trigger is contained and the save still activates; the save returns while
the reconciliation is blocked in the library enumeration (no synchronous scan and
no save-response blocking); the trigger enqueues bounded hints at the current
snapshot version in `ReconciliationBatchSize` pages; rapid requests coalesce into
exactly one bounded rerun; a blocked reconciliation still lets `StopAsync` return
within its bounded shutdown timeout and the loop observes cancellation; an
existing published poster re-renders after the save while a stale work item is
skipped; and the registrator wires the trigger as a hosted service whose instance
is the same singleton the save path resolves, without starting it.
`LifecycleFoundationTests.RegistratorRegistersFoundationServices` supplies
the media-library enumerator boundary its hosted-service resolution now needs.
`./build.sh build` reported 0 warnings / 0 errors; the default suite was Failed
0, Passed 1270, Skipped 63, Total 1333 (baseline Failed 0, Passed 1260, Skipped
63, Total 1323; +10 passed, +10 total). The final Goal A documentation and
integration verification (9.5) is not part of this task, so limitation F2 is not
yet recorded as resolved.

**Objective:** On a successful replacement, enqueue a bounded, non-blocking
reconciliation through the existing work-hint/reconciliation boundary so saved
settings re-render existing posters promptly; the trigger is never a synchronous
full-library scan, never throws into the host, and never blocks the save
response (ADR-016 clause 5 second bullet).

**Traceability:** Goal A; ADR-016 clause 5.

**Dependencies:** 9.3.

**Affected files/components:** `src/ArrTags/Plugin.cs`, `src/ArrTags/Updates`
(the work-hint/reconciliation boundary), reconciliation trigger tests,
`docs/architecture.md` section 6.

**Work:**

- After a successful `TryReplace`, enqueue a bounded reconciliation through the
  existing `IWorkHintSink`/reconciliation boundary.
- Do not perform a synchronous full-library scan; keep the enqueue bounded and
  non-blocking; contain all exceptions.
- Confirm that a work item whose `ConfigurationVersion` is older than the
  current snapshot is superseded by the trigger, so existing posters update
  without waiting for the next library event, webhook, post-scan, or scheduled
  run.

**Tests:** Bounded enqueue on a successful save; no synchronous scan; no throw
and no save-response blocking; an existing published poster re-renders after the
save; an invalid save triggers no reconciliation.

**Acceptance criteria:** A successful save triggers a bounded reconciliation so
affected posters re-render with the new settings instead of waiting for the next
scheduled run.

**Decision gates:** DG-10 resolved by ADR-016.

**Review:** `test-quality-reviewer` (bounded, non-blocking trigger determinism).

**Documentation impact:** `docs/architecture.md` section 6.

**Definition of done:** The trigger is bounded, non-blocking, and covered by
tests.

#### 9.5 Goal A documentation and integration verification

**Status:** Complete. The named documents are reconciled with the shipped Goal A
behaviour and limitation F2 is recorded as resolved. The new
`tests/ArrTags.Tests/GoalAIntegrationTests.cs` (2 facts) composes the full save
-> activate -> bounded-reconcile flow without a live host: the pinned
elevation-gated `PluginsController` JSON deserialization
(`Jellyfin.Extensions.Json.JsonDefaults.Options`), the real
`Plugin.UpdateConfiguration` override, the real `ConfigurationSnapshotService`,
the real `ConfigurationReconciliationTrigger` over the real bounded
`LibraryReconciliationService`, and the real artwork publishing pipeline. A
valid save round-trips both previously get-only collections
(`EnabledLibraries`, `Renderer.Selectors`), activates the running snapshot
service at the next version without a restart, rotates the private secret
generation (the new lease is valid at the new version and the retired value is
not), persists the collections and the changed renderer template, and requests
exactly one bounded post-save reconciliation whose fresh work item at the new
version re-renders an existing published poster with the saved settings (new
renderer configuration fingerprint and a changed published fingerprint). An
invalid save retains the last valid snapshot and private secrets at the
unchanged version, leaves the persisted `ArrTags.xml` unchanged, writes exactly
one bounded, secret-free activity entry, and requests no reconciliation (the
running trigger loop leaves the bounded queue empty). `./build.sh build`
reported 0 warnings / 0 errors; the default suite was Failed 0, Passed 1272,
Skipped 63, Total 1335 (baseline Failed 0, Passed 1270, Skipped 63, Total 1333;
+2 passed, +2 total, 0 new skips). `docs/limitations.md` records F2 as resolved,
and `docs/architecture.md` section 6, `docs/data-model.md` 3.12, and `README.md`
describe the shipped behaviour.

**Objective:** Complete the Goal A documentation changes and verify the
configuration round-trip, runtime activation, last-valid retention, and post-save
reconciliation at the integration-test level.

**Traceability:** Goal A; ADR-016; v1.1 documentation checklist V1.1-2.

**Dependencies:** 9.4.

**Affected files/components:** `docs/architecture.md` section 6,
`docs/data-model.md` 3.12, `docs/limitations.md` F2, `README.md`, Goal A
integration tests.

**Work:** Reconcile the named documents; add or extend the integration tests that
exercise the save -> activate -> bounded-reconcile flow without a live host.

**Tests:** Configuration round-trip (get-only collections); valid/invalid save
with last-valid retention; secret retention; bounded post-save reconciliation; no
host restart.

**Acceptance criteria:** The Goal A acceptance criteria are met at the
integration-test level; `docs/limitations.md` F2 is recorded as resolved; the
canonical docs describe the shipped behavior.

**Decision gates:** DG-10 resolved by ADR-016.

**Review:** `documentation-maintainer` (canonical current-state reconciliation).

**Documentation impact:** the named documents.

**Definition of done:** Goal A documentation is reconciled and the integration
tests pass.

**Phase 9 acceptance criteria:**

- [x] A plugin settings page appears in the Jellyfin dashboard and loads its
  current configuration.
- [x] Saving a valid change applies it at runtime without a host restart; an
  invalid change is rejected and the last valid snapshot is retained.
- [x] A successful save triggers a bounded reconciliation so affected posters
  re-render with the new settings instead of waiting for the next scheduled run.
- [x] No secret is embedded in the page or returned beyond Jellyfin's existing
  admin configuration behavior.
- [x] Configuration is saved only through the elevation-gated supported path.
- [x] The get-only `Collection<T>` round-trip is proven (or the configuration
  shape is corrected) and no collection is silently dropped.

**Gate 9:** Met when tasks 9.1-9.5 meet their acceptance criteria, the Goal A
acceptance criteria are met at the integration-test level (live confirmation is
owned by task 14.3), the phase review is approved, and the tag `v1.1.0-phase9` is
created. Phase 9 resolves limitation F2.

### 10. Logging with configurable verbosity

**Goal:** Goal F (ADR-020). **Release:** v1.1.0. **Phase tag:** `v1.1.0-phase10`.

**Objective:** Add plugin logging through the host's logging pipeline with a
bounded, validated, secret-free per-plugin verbosity setting that applies without
restart, and rewrite limitation SEC-5 from "no logging call sites" to the
redaction contract. The new logging path is security-reviewed.

**Deliverables:**

- `ILogger<T>`/`ILoggerFactory` logging through plugin DI with `ArrTags.*`
  logger categories.
- A bounded verbosity setting (`Off`/`Error`/`Warning`/`Information`/`Debug`/
  `Trace`, default `Warning`) validated at configuration load and exposed through
  the ADR-016 settings UI and the XML configuration.
- Plugin-owned verbosity gating from the current snapshot; no custom
  `ILoggerProvider`/sink and no replacement of the host `ILoggerFactory`.
- Secret-free log call sites using only bounded, already-redacted types, with
  redaction tests at every verbosity level and bounded log volume.
- The rewritten `docs/limitations.md` SEC-5 and the logging security review.

**Tasks:**

- [x] 10.1 Logging foundation, verbosity configuration, and fingerprint exclusion.
- [x] 10.2 Bounded, redacted log call sites and volume bounds.
- [x] 10.3 SEC-5 rewrite, documentation, and logging security review.

**Authoritative Phase 10 execution order:** 10.1, 10.2, 10.3. Task IDs are stable
references only; this execution order is the canonical sequence. When the
execution order and task numbering conflict, the execution order wins.

| Order | Task | Depends on |
| --- | --- | --- |
| 10.1 | Logging foundation, verbosity configuration, and fingerprint exclusion | 9.4 |
| 10.2 | Bounded, redacted log call sites and volume bounds | 10.1 |
| 10.3 | SEC-5 rewrite, documentation, and logging security review | 10.2 |

#### 10.1 Logging foundation, verbosity configuration, and fingerprint exclusion

**Status:** Complete. The bounded `LogVerbosity` enum (`Off`, `Error`, `Warning`,
`Information`, `Debug`, `Trace`, default `Warning`) is persisted in
`PluginConfiguration`, validated at configuration load, exposed through the
ADR-016 settings page and the XML configuration, and carried on the immutable
`PluginConfigurationSnapshot`. A plugin-owned `ILogVerbosityGate` reads the
current snapshot's verbosity on every call and decides whether a
`Microsoft.Extensions.Logging.LogLevel` is enabled, so a saved change applies
without a restart; `ArrTagsServiceRegistrator` resolves `ILogger<T>`/
`ILoggerFactory` through the host DI and registers no custom `ILoggerProvider`/
sink and does not replace the host `ILoggerFactory`. Verbosity is excluded from
the renderer and configuration output fingerprints and does not change
`RenderVersion`, so changing it never republishes artwork. `LogVerbosityTests`
(26 cases) covers the enum validation, page/XML exposure, DI resolution,
per-level gating, the no-provider/no-factory-replacement check, and the
fingerprint exclusion. `./build.sh build` reported 0 warnings / 0 errors; the
default suite was Failed 0, Passed 1298, Skipped 63, Total 1361 (baseline Failed
0, Passed 1272, Skipped 63, Total 1335; +26 passed, +26 total, 0 new skips).
Recorded in `docs/architecture.md` sections 6 and 12, `docs/data-model.md` 3.12,
and `README.md`.

**Objective:** Add the logging foundation and the bounded verbosity setting, and
exclude verbosity from output fingerprints (ADR-020 clauses 1, 2, 3, and 5).

**Traceability:** Goal F; ADR-020 clauses 1, 2, 3, 5; v1.1 DG-14.

**Dependencies:** 9.4 (the verbosity setting is exposed through the ADR-016 page
and applied through Goal A runtime activation).

**Affected files/components:** `src/ArrTags/Configuration/PluginConfiguration.cs`,
the configuration validator/snapshot, `src/ArrTags/PluginLifecycle`
(DI/logging), the ADR-016 settings page, `docs/architecture.md` sections 6 and
12, `docs/data-model.md` 3.12, `README.md`.

**Work:**

- Add a `LogVerbosity` enum (`Off`, `Error`, `Warning`, `Information`, `Debug`,
  `Trace`) to `PluginConfiguration`, default `Warning`, validated at
  configuration load.
- Expose it through the ADR-016 settings page and the XML configuration.
- Resolve `ILogger<T>`/`ILoggerFactory` through plugin DI with `ArrTags.*`
  category prefixes. Do not register a custom `ILoggerProvider`/sink and do not
  replace the host `ILoggerFactory`.
- Gate log calls by the plugin's own verbosity read from the current
  configuration snapshot.
- Keep verbosity out of the renderer and configuration output fingerprints and
  do not let it change `RenderVersion`.

**Tests:** Enum validation; page/XML exposure; DI resolution; gating per level;
changing verbosity does not change the renderer or configuration fingerprint and
does not change `RenderVersion`; no custom provider is registered.

**Acceptance criteria:** The plugin logs through the host pipeline at a
configurable verbosity; verbosity is bounded, validated, secret-free, and applied
without restart; verbosity does not affect rendered output or output
fingerprints.

**Decision gates:** DG-14 resolved by ADR-020.

**Review:** `test-quality-reviewer` (fingerprint-exclusion determinism).

**Documentation impact:** `docs/architecture.md` sections 6 and 12,
`docs/data-model.md` 3.12, `README.md`.

**Definition of done:** Verbosity is configurable, gated, and provably excluded
from output fingerprints.

#### 10.2 Bounded, redacted log call sites and volume bounds

**Status:** Complete. Every ArrTags boundary now logs through the plugin-owned
`IArrTagsLog<T>`/`ArrTagsLog<T>` facade (in `src/ArrTags/Logging`) instead of
`ILogger<T>` directly. The facade gates on the task 10.1 `ILogVerbosityGate` read
from the current snapshot, emits only bounded, already-redacted values under the
ADR-020 clause 4 redaction contract, and applies one shared, provider-neutral,
thread-safe `LogThrottle` repetition suppressor (ADR-020 clause 6). Call sites
were added at the provider (`ConcurrencyLimitedArrMetadataReader<T>`), matching
(`RadarrMetadataReader`, `SonarrMetadataReader`), metadata
(`MetadataReconciliationProcessor`), artwork (`ArtworkGenerationCoordinator`),
queue (`LibraryWorkWorker`), reconciliation (`LibraryReconciliationService`),
webhook (`WebhookAuthenticationFilter`, `WebhookIntakeService`), and lifecycle
(`ArrTagsLifecycleService`) boundaries. The volume bound is code-owned, not
user-configurable: at most 5 records per category/event per minute, then one
bounded suppression summary per window, with the tracking set capped at 256 keys.
`LogRedactionTests` (65 cases) drives every instrumented boundary with sentinel
secret values present in its secret-bearing inputs at every verbosity level
(Off/Error/Warning/Information/Debug/Trace), asserts no sentinel appears in the
captured host log output, asserts exact emission counts per level, proves a Trace
raise adds no message beyond Debug for every boundary, proves the suppression and
tracking bounds, and proves the registrator resolves the logs with `ArrTags.*`
category prefixes without a custom `ILoggerProvider` or a replaced
`ILoggerFactory`. `./build.sh build` reported 0 warnings / 0 errors; the default
suite was Failed 0, Passed 1363, Skipped 63, Total 1426 (baseline Failed 0, Passed
1298, Skipped 63, Total 1361; +65 passed, +65 total, 0 new skips). Recorded in
`docs/architecture.md` sections 6 and 12.

**Objective:** Instrument the plugin's boundaries with secret-free log calls
under the ADR-020 redaction contract and bound log volume (ADR-020 clauses 4 and
6).

**Traceability:** Goal F; ADR-020 clauses 4, 6.

**Dependencies:** 10.1.

**Affected files/components:** provider, matching, metadata, artwork, queue,
reconciliation, webhook, and lifecycle boundaries under `src/ArrTags/`;
`docs/architecture.md` section 12.

**Work:**

- Add log call sites at the provider, matching, metadata, artwork, queue,
  reconciliation, webhook, and lifecycle boundaries.
- Use only types already proven bounded and redacted (`ArrProviderError`, safe
  `SecretReference`, connection identity, configuration version, bounded reason
  codes). Never log an API key, the webhook secret, a `SecretLease` value, an
  `X-Api-Key`/`X-ArrTags-Webhook-Secret` header, a raw request/response body, a
  full provider payload, or the mutable `PluginConfiguration`.
- Ensure raising verbosity cannot expand a redacted value into a secret-bearing
  one.
- Rate-limit or repetition-suppress high-frequency messages and record the bound
  as a `docs/architecture.md` section 12 limit row.

**Tests:** Sentinel-secret redaction tests at every verbosity level; no secret in
any log output; high-frequency suppression; bounded volume; a verbosity raise does
not expand a redacted value.

**Acceptance criteria:** Every log call is secret-free; redaction is covered by
tests at every level; log volume is bounded and the section 12 limit row is
recorded.

**Decision gates:** DG-14 resolved by ADR-020.

**Review:** `security-reviewer` (the redaction contract).

**Documentation impact:** `docs/architecture.md` section 12.

**Definition of done:** Redaction is proven at every level and volume is bounded.

#### 10.3 SEC-5 rewrite, documentation, and logging security review

**Status:** Complete. The logging security review is recorded at
`docs/implementation/10.3/security-review.json` (PASS_WITH_FINDINGS, 0 open
BLOCKER/HIGH/MEDIUM); the live pinned-host logging confirmation is owned by
task 14.3.
`docs/limitations.md` SEC-5 is rewritten from the "no logging call sites"
negative result to the ADR-020 redaction contract and reconciled with SEC-9;
`docs/architecture.md` sections 6, 11, and 12, `docs/data-model.md` 3.12, and
`README.md` consistently describe the shipped Goal F behavior; and the task 10.2
informational finding that section 12 listed "durations" among emitted value
kinds (none is logged) is corrected. The logging security review (ADR-020
clause 8) was run by the orchestrator after the documentation change and is
recorded at `docs/implementation/10.3/security-review.json`; the verdict is not
recorded here.

**Objective:** Rewrite `docs/limitations.md` SEC-5 to the redaction contract and
obtain a fresh security review of the new logging path (ADR-020 clause 8).

**Traceability:** Goal F; ADR-020 clause 8; v1.1 verification requirements
section 9.

**Dependencies:** 10.2.

**Affected files/components:** `docs/limitations.md` SEC-5,
`docs/architecture.md` sections 6/11/12, `docs/data-model.md` 3.12, `README.md`,
the security-review report.

**Work:** Rewrite SEC-5 from "no logging call sites" to the redaction contract;
update the named documents; run the logging security review and record its report
and any accepted limitations.

**Tests:** Documentation consistency; the security-review report is recorded with
no open BLOCKER/HIGH/MEDIUM or with accepted limitations recorded in
`docs/limitations.md`.

**Acceptance criteria:** SEC-5 is rewritten; the logging path is security-reviewed.

**Decision gates:** DG-14 resolved by ADR-020.

**Review:** `security-reviewer` (logging path and SEC-5).

**Documentation impact:** the named documents.

**Definition of done:** SEC-5 and the logging review are complete and recorded.

**Phase 10 acceptance criteria:**

- [x] The plugin logs through the host logging pipeline at a configurable
  verbosity.
- [x] Verbosity is bounded, validated, secret-free, and applied without restart
  (via Goal A).
- [x] Every log call is secret-free; redaction is covered by tests at every
  level.
- [x] Verbosity does not affect rendered output or output fingerprints.
- [x] SEC-5 is rewritten and the logging path is security-reviewed.

**Gate 10:** Met when tasks 10.1-10.3 meet their acceptance criteria, the logging
security review is recorded with no open BLOCKER/HIGH, the phase review is
approved, and the tag `v1.1.0-phase10` is created.

### 11. Provider inventory cache and library-refresh-driven refresh

**Goal:** Goal C (ADR-018). **Release:** v1.1.0. **Phase tag:** `v1.1.0-phase11`.

**Objective:** Add a bounded, in-memory, per-connection provider inventory cache
so one provider library read serves a reconciliation window, with ArrTags-side
invalidation (webhook, Jellyfin library refresh/post-scan, scheduled/manual
reconciliation, and a bounded TTL fallback). This resolves limitation F1.

**Deliverables:**

- A canonical, secret-free inventory cache shape per `ArrConnection` (the
  provider library list plus the per-record file resources needed for badge
  metadata), distinct from `MetadataCacheEntry`.
- Inventory TTL and entry/size bounds added to `OperationalLimits` and
  `docs/architecture.md` section 12, validated at configuration load.
- Provider-client integration with the bulk selection endpoints so multiple
  records are read in one request.
- ArrTags-side invalidation across the complete trigger set; no provider
  conditional request, revision token, `history/since` watermark, or SignalR
  dependency.
- Documentation updates: `docs/architecture.md` sections 8 and 12,
  `docs/data-model.md` section 6, `docs/limitations.md` F1.

**Tasks:**

- [x] 11.1 Inventory cache model, bounds, and limits.
- [x] 11.2 Provider-client integration and bulk reads.
- [x] 11.3 ArrTags-side invalidation.
- [x] 11.4 Goal C documentation and integration verification.

**Authoritative Phase 11 execution order:** 11.1, 11.2, 11.3, 11.4. Task IDs are
stable references only; this execution order is the canonical sequence. When the
execution order and task numbering conflict, the execution order wins.

| Order | Task | Depends on |
| --- | --- | --- |
| 11.1 | Inventory cache model, bounds, and limits | 9.4 |
| 11.2 | Provider-client integration and bulk reads | 11.1 |
| 11.3 | ArrTags-side invalidation | 11.2 |
| 11.4 | Goal C documentation and integration verification | 11.3 |

#### 11.1 Inventory cache model, bounds, and limits

**Status:** Complete. The canonical, secret-free, in-memory inventory cache
shape (`ArrInventoryCache`/`ArrInventoryCacheEntry`/`ArrInventoryRecordObservation`
in `src/ArrTags/Providers`) and its inventory TTL and per-connection record/byte
limits (`OperationalLimits.InventoryCacheTtlMinutes`/`InventoryCacheMaxRecords`/
`InventoryCacheMaxBytes`, validated at configuration load) are defined and
documented in `docs/data-model.md` section 6 and `docs/architecture.md` sections
8 and 12. The boundary is non-authoritative, rebuilt empty on restart, holds only
canonical `MatchCandidate`/`BadgeMetadata` observations, and keeps the bounded
last-known-good semantics on provider failure without extending the window.
*(Superseded by task 11.4: this is the historical point-in-time task 11.1
wording. Inventory stale-serving on provider failure is not realized in
production — a failed cold population caches nothing and the retained
last-known-good is the per-item `MetadataStateEntry`; the bounded last-known-good
serving is the TTL second half. See the task 11.4 status and
`docs/limitations.md` F1.)* No
provider `ETag`/revision token is assumed. Provider-client integration (11.2) and
ArrTags-side invalidation (11.3) remain, so limitation F1 is not yet resolved.

**Objective:** Define the canonical, secret-free inventory cache shape and its
bounds (ADR-018 clauses 1, 5, 6, and 7).

**Traceability:** Goal C; ADR-018 clauses 1, 5, 6, 7; v1.1 DG-12.

**Dependencies:** 9.4 (the new limits share the Goal A runtime-configuration
plumbing).

**Affected files/components:** `src/ArrTags/Providers` (cache boundary),
`src/ArrTags/Configuration` (`OperationalLimits` and validation),
`docs/data-model.md` section 6, `docs/architecture.md` sections 8 and 12.

**Work:**

- Define the per-`ArrConnection` cache shape holding the provider library list
  (Sonarr `/series`, Radarr `/movie`) and the per-record file resources as
  canonical, secret-free observations.
- Document it in `docs/data-model.md` section 6 as its own subsection, distinct
  from `MetadataCacheEntry` (3.9).
- Add the inventory TTL and cache entry/size bounds to `OperationalLimits` and
  `docs/architecture.md` section 12, validated at configuration load.
- Keep the cache non-authoritative, in-memory, rebuilt on restart, and never
  holding a credential; on provider failure the existing bounded last-known-good
  semantics apply.
- Do not assume provider `ETag`/revision tokens; treat them as optional future
  observations only.

**Tests:** Limit validation bounds; cache secret-free; in-memory rebuild on
restart; non-authoritative last-known-good on provider failure; distinct from
`MetadataCacheEntry`.

**Acceptance criteria:** The cache is bounded and secret-free with validated
TTL/size limits; provider failure keeps the existing bounded last-known-good
behavior.

**Decision gates:** DG-12 resolved by ADR-018.

**Review:** `security-reviewer` (secret-free cache) and `test-quality-reviewer`
(bounds).

**Documentation impact:** `docs/data-model.md` section 6,
`docs/architecture.md` sections 8 and 12.

**Definition of done:** The cache shape and bounds are defined, validated, and
documented.

#### 11.2 Provider-client integration and bulk reads

**Status:** Complete. The bounded provider inventory cache (ADR-018 clauses 2
and 4) is integrated at the provider-client boundary. `ArrInventoryCacheProvider`
resolves the cache from the current configuration snapshot, rebuilding it with
the new validated bounds when the snapshot is replaced, and owns the
per-connection single-flight population gate so concurrent cold readers for one
connection share one library read (and one set of bulk file reads) instead of one
population each. `RadarrMetadataReader` and `SonarrMetadataReader` populate the
cache on a miss (one `api/v3/movie` / `api/v3/series` library read plus the bulk
file reads), serve every work item in the cache window from the canonical
observations without a provider library read, and fall back to the existing
direct read unchanged when the observation set exceeds the record or byte bound
or a bulk file read fails. `RadarrClient` gains the repeatable
`moviefile?movieId=` bulk read and `SonarrClient` gains the repeatable
`episodeFile?episodeFileIds=` bulk read, so per-item file reads are replaced by
bounded bulk requests (chunked by `ReconciliationBatchSize` so a large id list
cannot overflow a request line). The cache stays canonical and secret-free, uses
no provider conditional request or revision token, and a provider failure keeps
the bounded last-known-good observation set until the TTL. *(Superseded by task
11.4: this is the historical point-in-time task 11.2 wording. Inventory
stale-serving on provider failure is not realized in production — a failed cold
population caches nothing and the retained last-known-good is the per-item
`MetadataStateEntry`; the bounded last-known-good serving is the TTL second half.
See the task 11.4 status and `docs/limitations.md` F1.)* The
`MetadataReconciliationProcessor` freshness semantics are unchanged (the
published `MetadataStateEntry` freshness remains the publish time, and the cache
entry's bounded reuse window limits how long an observation set may be reused).
ArrTags-side invalidation (11.3) remains, so limitation F1 is not yet resolved.

**Objective:** Populate and consume the cache at the provider-client boundary so
one library read per connection serves all work items in a reconciliation window,
and use the bulk selection endpoints (ADR-018 clauses 2 and 4).

**Traceability:** Goal C; ADR-018 clauses 2, 4.

**Dependencies:** 11.1.

**Affected files/components:** `src/ArrTags/Providers/Radarr`,
`src/ArrTags/Providers/Sonarr`, `src/ArrTags/Reconciliation` (metadata readers),
provider integration tests.

**Work:** Integrate the cache into the Radarr/Sonarr clients and the metadata
readers; use Radarr `moviefile?movieId=` repeated ids and Sonarr episode/
episodeFile id selection to avoid per-item reads; keep the observations canonical
and secret-free.

**Tests:** One library read per connection serves multiple work items; bulk reads
are used; no per-item library read within a reconciliation window;
canonical/secret-free.

**Acceptance criteria:** One provider library read per connection serves a
reconciliation window; bulk reads avoid per-item reads.

**Decision gates:** DG-12 resolved by ADR-018.

**Review:** `test-quality-reviewer`.

**Documentation impact:** `docs/architecture.md` section 8.

**Definition of done:** The provider reads are served from the cache within a
window and bulk reads are used.

#### 11.3 ArrTags-side invalidation

**Status:** Complete. The ArrTags-side invalidation surface (ADR-018 clause 3) is
wired to every invalidation source. `ArrInventoryCache` gains a bounded,
thread-safe `Invalidate(ArrConnectionId)`/`InvalidateAll()` surface that removes
retained observation sets under the same gate as `TryStore`/`TryGet` (no lock is
held across provider I/O, and it is safe concurrently with reads and
populations); an in-flight single-flight population that completes after an
invalidation may still store the read it took before it, which is bounded by the
configured TTL and repaired by the next webhook, refresh/post-scan, or
scheduled/manual reconciliation. `ArrInventoryCacheProvider` exposes the same
surface against the cache bound to the current configuration snapshot, so a
replaced snapshot's rebuilt cache is invalidated rather than a captured one. An
accepted provider webhook (ADR-012) invalidates the event's provider/connection
from the hosted intake loop before any hint is enqueued (a bounded
invalidate-all when the connection cannot be resolved), and it never blocks or
throws into the Jellyfin request. Jellyfin library refresh/post-scan, manual and
periodic scheduled, and post-save reconciliation all invalidate at the start of
`LibraryReconciliationService.ReconcileAsync`, so the work they enqueue begins a
fresh provider-read window; the periodic scheduled reconciliation still runs on
its unchanged 12-hour default interval and enqueues work as before, so v1.1 is
not refresh-only. The bounded TTL fallback remains in `TryGet` (an expired set is
evicted). No provider conditional request, revision token, `history/since`
watermark, or SignalR dependency is added, and the invalidation API carries only
the non-secret connection identifier. Limitation F1 is not yet resolved; task
11.4 records it resolved after the Goal C integration verification.

**Objective:** Invalidate the cache on provider webhook events (ADR-012), Jellyfin
library refresh/post-scan, scheduled/manual reconciliation, and a bounded TTL
fallback, with no provider conditional request, revision token, `history/since`
watermark, or SignalR dependency; v1.1 is not refresh-only (ADR-018 clause 3).

**Traceability:** Goal C; ADR-018 clause 3.

**Dependencies:** 11.2.

**Affected files/components:** `src/ArrTags/Updates` (triggers),
`src/ArrTags/Webhooks`, `src/ArrTags/Reconciliation` (scheduled/post-scan), the
cache boundary.

**Work:** Wire each invalidation source; keep periodic scheduled reconciliation
alongside the webhook, post-scan, manual, and TTL sources; do not use provider
conditional requests, revision tokens, `history/since`, or SignalR.

**Tests:** Each invalidation source; TTL fallback; no provider conditional/
revision/SignalR dependency; periodic scheduled reconciliation continues.

**Acceptance criteria:** Invalidation is ArrTags-side per ADR-018; no provider
conditional request, revision token, `history/since`, or SignalR dependency;
periodic scheduled reconciliation continues.

**Decision gates:** DG-12 resolved by ADR-018.

**Review:** `test-quality-reviewer`.

**Documentation impact:** `docs/architecture.md` sections 8 and 12.

**Definition of done:** Every invalidation source is wired and tested.

#### 11.4 Goal C documentation and integration verification

**Status:** Complete. The Goal C documentation is reconciled, limitation F1 is
recorded as resolved, and the Goal C integration coverage is in place. The
provider-failure sentences in `docs/architecture.md` sections 8 and 12,
`docs/data-model.md` section 6, and the `ArrInventoryCache`/
`ArrInventoryCacheEntry`/`ArrInventoryCacheState` XML docs now describe the
shipped behavior: a stored observation set is served as bounded last-known-good
through the TTL second half and the TTL is never extended, a library read failure
during a population caches nothing and returns the bounded failure (so the
inventory cache never serves a failed read as current, and the existing per-item
bounded last-known-good metadata state is what retains last-known-good metadata),
and a failed bulk/sub-read falls back to the unchanged direct read. The
previously implied failure-driven inventory last-known-good retention is not
realized in production (`ArrInventoryCacheEntry.WithFailure` and `LastError` have
no production caller) and is no longer claimed. `docs/limitations.md` records F1
as resolved with the remaining bounds stated honestly (Sonarr per-series episode
reads are O(series) per window and a sparse invalidation window repopulates the
whole library; eager whole-library population on a miss; size-bound rejection
falls back to the direct read; library-read failure fails the whole window while
a sub-read falls back; whole-cache clear on a full reconciliation; per-connection
single-flight coalescing present). `README.md` states the resolved limitation,
and `PLANS.md`, `docs/project-status.md`, `docs/changelog.md`,
`docs/implementation-readiness.md`, and `docs/decisions.md` (ADR-018
Consequences) are reconciled. `ProviderInventoryCacheIntegrationTests` gains
three cases (25 total): a composed real-reader/real-cache/real-processor Radarr
library-read failure on a cache miss fails the window and caches nothing while
the per-item bounded last-known-good metadata state is retained as explicit stale
with an unchanged fingerprint and window; a Radarr bulk movie-file read failure
falls back to the direct read instead of failing the window; and a Sonarr
per-series episode read failure for a non-matched series falls back to the direct
read for the matched series. The existing one-read-per-window, bulk-selector,
invalidation-source, bounds, canonical/secret-free, stale-served, and
expired-evicted cases remain. No production code behavior changed; the XML docs
and the Goal C documents were corrected only. `./build.sh build` reported 0
warnings / 0 errors; the default suite was Failed 0, Passed 1421, Skipped 63,
Total 1484 (baseline Failed 0, Passed 1418, Skipped 63, Total 1481; +3 passed,
+3 total, 0 new skips).

**Objective:** Complete the Goal C documentation, record F1 as resolved, and
verify cache invalidation and bounds at the integration-test level.

**Traceability:** Goal C; ADR-018; v1.1 documentation checklist V1.1-4.

**Dependencies:** 11.3.

**Affected files/components:** `docs/architecture.md` sections 8 and 12,
`docs/data-model.md` section 6, `docs/limitations.md` F1,
`docs/decisions.md` ADR-018, Goal C integration tests.

**Work:** Reconcile the named documents; add the integration tests for
one-read-per-window, invalidation sources, bounds, and last-known-good.

**Tests:** One-read-per-window; invalidation sources; bounds; last-known-good.

**Acceptance criteria:** F1 is recorded as resolved; the docs describe the
shipped cache behavior; the integration tests pass.

**Decision gates:** DG-12 resolved by ADR-018.

**Review:** `documentation-maintainer`.

**Documentation impact:** the named documents.

**Definition of done:** Goal C documentation is reconciled, F1 is resolved, and
the integration tests pass.

**Phase 11 acceptance criteria:**

- [x] One provider library read per connection serves a reconciliation window
  instead of one read per work item.
- [x] Invalidation is ArrTags-side (webhook, Jellyfin library refresh/post-scan,
  scheduled/manual reconciliation, bounded TTL); no provider conditional
  request, revision token, or SignalR dependency.
- [x] The cache is bounded and secret-free, with validated TTL/size limits.
- [x] Provider failure keeps the existing bounded last-known-good behavior.

**Gate 11:** Met. Tasks 11.1-11.4 meet their acceptance criteria, the Phase 11
review is approved in `docs/implementation/phase-11/phase-review.json`
(APPROVED_WITH_FINDINGS, 0 open BLOCKER/HIGH/MEDIUM), and the annotated tag
`v1.1.0-phase11` is created. Phase 11 resolves limitation F1. All four Phase 11
acceptance criteria are met at the integration-test level.

### 12. Badge value allowlist and badge size/position

**Goals:** Goal B (ADR-017) and Goal E (ADR-019). **Release:** v1.1.0. **Phase
tag:** `v1.1.0-phase12`.

**Objective:** Add the per-selector badge value allowlist and the configurable
badge size/position, with a single coordinated renderer-configuration schema and
`RenderVersion` advance and regenerated goldens.

**Why one phase:** Both goals are output-affecting, and ADR-017 and ADR-019 define
a **single shared** advance (`RendererConfiguration.CurrentSchemaVersion` 1 -> 2
and `RenderVersion.CurrentRendererVersion` 2 -> 3) with one golden regeneration.
Implementing them as separate phases would either double the version advance or
leave one goal's acceptance criterion ("`RenderVersion` advances") unmet at its
own gate, so they are one phase with one bump and one golden regeneration.

**Deliverables:**

- Per-selector `AllowedValues` on `BadgeSelectorConfiguration`, bounded and
  validated, mapped into the resolved `BadgeDefinition` and included in the
  renderer configuration fingerprint.
- The allowlist filter applied in the documented order (value resolution ->
  allowlist filter -> template -> normalization/truncation -> layout).
- `BadgePosition` (four corners plus center) and `BadgeSize`
  (Small/Medium/Large) on `RendererConfiguration`, carried on the resolved
  `RenderOutputPolicy`, with rail packing, status-pill placement, and safe-area
  behavior defined per anchor.
- The coordinated schema/`RenderVersion` advance and the regenerated goldens.
- Documentation: `docs/architecture.md` section 9, `docs/data-model.md`
  3.6/3.12, `README.md`, `docs/decisions.md` ADR-019, and the superseded notes in
  ADR-009/ADR-010.

**Tasks:**

- [x] 12.1 Allowlist configuration, bounds, validation, and fingerprint.
- [x] 12.2 Allowlist resolution and renderer filtering order.
- [x] 12.3 Badge size/position configuration and layout engine.
- [ ] 12.4 Coordinated schema/`RenderVersion` advance, golden regeneration, and
  documentation.

**Authoritative Phase 12 execution order:** 12.1, 12.2, 12.3, 12.4. Task IDs are
stable references only; this execution order is the canonical sequence. When the
execution order and task numbering conflict, the execution order wins.

| Order | Task | Depends on |
| --- | --- | --- |
| 12.1 | Allowlist configuration, bounds, validation, and fingerprint | 9.4 |
| 12.2 | Allowlist resolution and renderer filtering order | 12.1 |
| 12.3 | Badge size/position configuration and layout engine | 9.4 |
| 12.4 | Coordinated schema/`RenderVersion` advance, golden regeneration, and documentation | 12.1, 12.2, 12.3 |

#### 12.1 Allowlist configuration, bounds, validation, and fingerprint

**Status:** Complete. `BadgeSelectorConfiguration.AllowedValues` (bounded, settable
for the ADR-016 POST round-trip) is validated in `RendererConfiguration.Validate`
with secret-free messages (at most 32 entries per selector, each at most 64
characters, trimmed, no blank or control-character entry, no case-insensitive
duplicate), mapped into `BadgeDefinition.AllowedValues` by
`RendererConfigurationResolver.ResolveDefinitions`, and included in
`RendererConfigurationFingerprint` (case- and order-normalized; an empty
allowlist is identity-neutral, so the default configuration fingerprint is
unchanged). The setting is exposed per selector on the ADR-016 settings page. The
renderer filtering behavior, the schema/`RenderVersion` advance, and the golden
regeneration remain tasks 12.2/12.4.

**Objective:** Add the per-selector allowlist configuration, its bounds and
validation, the resolved `BadgeDefinition` allowlist, and the fingerprint
inclusion (ADR-017 clauses 1, 5, 6 first half, and 7).

**Traceability:** Goal B; ADR-017 clauses 1, 5, 6, 7; v1.1 DG-11.

**Dependencies:** 9.4 (config plumbing and settings-page exposure).

**Affected files/components:** `src/ArrTags/Configuration/BadgeSelectorConfiguration.cs`,
`RendererConfiguration.cs`, `RendererConfigurationFingerprint.cs`,
`src/ArrTags/Rendering/BadgeDefinitionResolver.cs`, the ADR-016 settings page,
`docs/data-model.md` 3.6/3.12, `docs/architecture.md` section 9.

**Work:**

- Add a bounded `AllowedValues` string list to `BadgeSelectorConfiguration`;
  empty means no restriction.
- Validate in `RendererConfiguration.Validate` with secret-free messages: at most
  32 entries per selector, each at most 64 characters, trimmed, control
  characters rejected, blank entries rejected, and case-insensitive duplicates
  rejected.
- Add the resolved allowlist to `BadgeDefinition` and map it in
  `RendererConfigurationResolver.ResolveDefinitions`.
- Include the resolved allowlists in `RendererConfigurationFingerprint`.
- Expose the setting through the ADR-016 settings page. No provider coupling.

**Tests:** Bounds/validation; fingerprint sensitivity to allowlist values and
insensitivity to case/entry-order normalization; secret-free messages; page
exposure.

**Acceptance criteria:** The allowlist is bounded, validated at configuration
load, and secret-free; the renderer configuration fingerprint changes when the
allowlist changes.

**Decision gates:** DG-11 resolved by ADR-017.

**Review:** `test-quality-reviewer`.

**Documentation impact:** `docs/data-model.md` 3.6/3.12,
`docs/architecture.md` section 9.

**Definition of done:** The allowlist is configurable, bounded, validated, and
included in the fingerprint.

#### 12.2 Allowlist resolution and renderer filtering order

**Status:** Complete. `BadgeSelectorResolver.IsAllowed` performs the ADR-017
filter match: an empty allowlist means no restriction; otherwise the resolved
pre-template value is trimmed and compared by a case-insensitive ordinal exact
match, with no substring, wildcard, prefix, or regular-expression matching.
`BadgeDefinitionResolver.Resolve` applies the filter to each resolved pre-template
value (and to the fixed `UpgradePending` status text) before the definition
template, so the order is value resolution -> allowlist filter -> definition
template -> text normalization/truncation -> layout. The filter applies to each
retained `CustomBadge` value independently and to the full `Audio` composite
(features, then codec, then channel count); an allowlisted value that cannot fit
still follows the existing shorten/omit behavior. Unknown/absent values remain
omitted and are never widened. `RendererConfiguration.CurrentSchemaVersion` is
still 1, `RenderVersion.CurrentRendererVersion` is still 2, and no golden under
`tests/ArrTags.Tests/Goldens/` was regenerated; the coordinated schema/`RenderVersion`
advance and golden regeneration are task 12.4. No badge size/position work is
included (task 12.3).

**Objective:** Apply the allowlist filter in the documented order and semantics
(ADR-017 clauses 2, 3, and 4).

**Traceability:** Goal B; ADR-017 clauses 2, 3, 4.

**Dependencies:** 12.1.

**Affected files/components:** `src/ArrTags/Rendering/BadgeSelectorResolver.cs`
and the renderer filtering path, renderer tests.

**Work:**

- Filter resolved values by case-insensitive ordinal exact match against the
  resolved pre-template value, trimmed; no substring, wildcard, prefix, or
  regular-expression matching.
- Apply per retained `CustomBadge` value; match the full `Audio` composite
  (features, then codec, then channel count); permit the fixed `UpgradePending`
  status text.
- Order: value resolution -> allowlist filter -> definition template -> text
  normalization/truncation -> layout. Unknown/absent values remain omitted and
  are never widened.

**Tests:** Exact match/case/trim behavior; per-value custom badge; the audio
composite; upgrade pending; an empty allowlist means no restriction;
unknown/absent unchanged; filter-before-template order; an allowlisted value that
cannot fit still follows shorten/omit.

**Acceptance criteria:** A configured allowlist restricts rendering to the listed
values; unknown/absent values remain omitted; an empty allowlist means no
restriction.

**Decision gates:** DG-11 resolved by ADR-017.

**Review:** `test-quality-reviewer`.

**Documentation impact:** `docs/architecture.md` section 9.

**Definition of done:** The filter order and semantics are implemented and tested.

#### 12.3 Badge size/position configuration and layout engine

**Status:** Complete. `RendererConfiguration` gains the global `Position`
(`BadgePosition`: `BottomLeft` default, `TopLeft`, `TopRight`, `BottomRight`,
`Center`) and `Size` (`BadgeSize`: `Medium` default, `Small`, `Large`) settings
(ADR-019 clauses 1-5 and 7), validated in `RendererConfiguration.Validate` with
bounded, secret-free messages that reject an undefined enum value.
`RendererConfigurationResolver.ResolveOutputPolicy` carries both onto the
resolved `RenderOutputPolicy` (the object passed into `BadgeLayoutEngine.Build`),
falling back to the code-owned default for a tolerantly read undefined value.
`BadgeGeometry.ComputeEffectiveScale` computes
`clamp(width / 1000, 0.5, 4.0) * sizeFactor` (`Small` 0.75, `Medium` 1.0,
`Large` 1.5), clamped so the scaled outer inset leaves a positive safe area, and
the renderer uses the same effective scale for its font. `BadgeLayoutEngine`
positions the rail per anchor: rows stack away from the anchored edge (downward
for top anchors, upward for bottom anchors, vertically centered for `Center`) and
align to the anchored side (left, right, or centered); the status pill stays
top-right except when the anchor is `TopRight`, then top-left; and the top-anchor
first row reserves the status pill plus one gap. Pills still pack in the ADR-009
priority order with the at-most-two-rows / three-pills-per-row limit, shortening,
and omission unchanged; no pill paints outside the safe area. A non-default
position or size is included in both `RendererConfigurationFingerprint` and
`RenderFingerprint.ComputeOutputFingerprint`, while the V1 default is
identity-neutral so the default configuration and output fingerprints are
unchanged and the committed goldens are unaffected. The ADR-016 settings page
exposes the two selects. Placement and size are global only; no per-selector
placement. `RendererConfiguration.CurrentSchemaVersion` is still 1,
`RenderVersion.CurrentRendererVersion` is still 2, and no golden under
`tests/ArrTags.Tests/Goldens/` was regenerated (the coordinated
schema/`RenderVersion` advance and golden regeneration are task 12.4).

**Objective:** Add the global badge position and size settings and the per-anchor
layout behavior (ADR-019 clauses 1-5 and 7).

**Traceability:** Goal E; ADR-019 clauses 1-5, 7; v1.1 DG-13.

**Dependencies:** 9.4 (config plumbing and settings-page exposure).

**Affected files/components:** `src/ArrTags/Configuration/RendererConfiguration.cs`,
`RendererConfigurationFingerprint.cs`, `src/ArrTags/Rendering/BadgeGeometry.cs`,
`BadgeLayoutEngine.cs`, `SkiaBadgeRenderer.cs`, `RenderOutputPolicy`, the ADR-016
settings page, `docs/architecture.md` section 9, `docs/data-model.md` 3.6/3.12.

**Work:**

- Add `BadgePosition` (`TopLeft`, `TopRight`, `BottomLeft`, `BottomRight`,
  `Center`, default `BottomLeft`) and `BadgeSize` (`Small`, `Medium`, `Large`,
  default `Medium`) to `RendererConfiguration`.
- Compute `effectiveScale = clamp(width / 1000, 0.5, 4.0) * sizeFactor`
  (`Small` 0.75, `Medium` 1.0, `Large` 1.5), clamped so the badge still fits the
  safe area.
- Position the technical rail per anchor; keep the `UPGRADE` status pill
  top-right except when the rail anchor is `TopRight`, then top-left. No separate
  status-position setting.
- Rail packing: rows stack away from the anchored edge; rows align to the
  anchored side; pills pack in the ADR-009 priority order; the at-most-two-rows /
  three-pills-per-row limit, shortening, and omission are unchanged.
- Keep the 24-pixel scaled inset and all safe-area/text-limit/contrast/opacity/
  determinism guarantees; no pill paints outside the safe area.
- Carry the position and size on the resolved `RenderOutputPolicy` (passed into
  `BadgeLayoutEngine.Build`) and include them in `RendererConfigurationFingerprint`
  and `RenderFingerprint.ComputeOutputFingerprint`.
- Expose the settings through the ADR-016 settings page. No per-selector
  placement.

**Tests:** Geometry per anchor and size; rail packing/alignment; status-pill
placement (including the `TopRight` -> top-left rule); safe-area bounds on narrow
and short posters; fingerprint sensitivity.

**Acceptance criteria:** A configured size and anchor affect the rendered badge;
rail packing, status-pill placement, and safe-area bounds are defined per anchor;
no badge paints outside the safe area.

**Decision gates:** DG-13 resolved by ADR-019.

**Review:** `test-quality-reviewer`.

**Documentation impact:** `docs/architecture.md` section 9,
`docs/data-model.md` 3.6/3.12.

**Definition of done:** The position/size settings and per-anchor layout are
implemented and tested.

#### 12.4 Coordinated schema/RenderVersion advance, golden regeneration, and documentation

**Status:** Not started.

**Objective:** Advance the renderer-configuration schema and `RenderVersion` once
for both changes, regenerate the committed goldens, and complete the Goal B/E
documentation (ADR-017 clause 6, ADR-019 clause 6).

**Traceability:** Goals B and E; ADR-017 clause 6; ADR-019 clause 6; v1.1
verification requirements section 9.

**Dependencies:** 12.1, 12.2, 12.3.

**Affected files/components:** `RendererConfiguration.CurrentSchemaVersion`,
`RenderVersion.CurrentRendererVersion`, `tests/ArrTags.Tests/Goldens/`,
`docs/architecture.md` section 9, `docs/data-model.md` 3.6/3.12, `README.md`,
`docs/decisions.md` ADR-019 and the superseded notes in ADR-009/ADR-010.

**Work:**

- Advance `RendererConfiguration.CurrentSchemaVersion` from 1 to 2 and
  `RenderVersion.CurrentRendererVersion` from 2 to 3 (one shared advance for
  ADR-017 and ADR-019).
- Regenerate the committed golden manifest and goldens under ADR-010 with no
  auto-approval or writer path; confirm the default (`BottomLeft`, `Medium`, empty
  allowlist) reproduces the V1 output and add goldens for the new anchor/size
  cases.
- Add or refresh byte-determinism and PNG-contract coverage.
- Update the named documentation, including the superseded notes in
  ADR-009/ADR-010.

**Tests:** Golden regeneration with no writer/auto-approve path; byte-determinism;
the existing goldens fail closed on mutation; the schema and renderer versions are
the single advanced values; the fingerprint reflects position/size/allowlist.

**Acceptance criteria:** The change is output-affecting: the renderer
configuration fingerprint changes, `RenderVersion` advances, and the goldens are
regenerated; the default configuration reproduces the V1 output.

**Decision gates:** DG-11 and DG-13 resolved by ADR-017 and ADR-019.

**Review:** `test-quality-reviewer` (golden and byte-determinism honesty).

**Documentation impact:** the named documents.

**Definition of done:** The single coordinated version advance and golden
regeneration are complete and documented.

**Phase 12 acceptance criteria:**

- [ ] A configured allowlist restricts rendering to the listed values.
- [ ] The allowlist is bounded, validated at configuration load, and secret-free.
- [ ] Unknown/absent values remain omitted; an empty allowlist means no
  restriction.
- [ ] A configured size and anchor (four corners plus center) affect the rendered
  badge.
- [ ] Rail packing, status-pill placement, and safe-area bounds are defined per
  anchor; no badge paints outside the safe area.
- [ ] The change is output-affecting: the fingerprint changes, `RenderVersion`
  advances, and the goldens are regenerated.

**Gate 12:** Met when tasks 12.1-12.4 meet their acceptance criteria, the single
coordinated schema/`RenderVersion` advance and golden regeneration are verified,
the phase review is approved, and the tag `v1.1.0-phase12` is created.

### 13. README and documentation pass

**Goal:** Goal D plus the v1.1 documentation reconciliation. **Release:** v1.1.0.
**Phase tag:** `v1.1.0-phase13`.

**Objective:** Document the palette override fields that `README.md` names but
never explains (Goal D), document the v1.1 features, and reconcile the canonical
current-state documentation so no stale claim remains.

**Deliverables:**

- The four palette override fields documented with their meaning, default colors,
  and the 4.5:1 contrast rule.
- README documentation of the settings UI, allowlist, placement/size, verbosity,
  and provider inventory cache behavior, with the stale F1/F2 restart limitation
  notes removed.
- A consistency sweep of `docs/project-status.md`, `docs/changelog.md`, and
  `docs/implementation-readiness.md`.

**Tasks:**

- [ ] 13.1 README palette documentation and v1.1 feature documentation.
- [ ] 13.2 Canonical current-state reconciliation sweep.

**Authoritative Phase 13 execution order:** 13.1, 13.2. Task IDs are stable
references only; this execution order is the canonical sequence. When the
execution order and task numbering conflict, the execution order wins.

| Order | Task | Depends on |
| --- | --- | --- |
| 13.1 | README palette documentation and v1.1 feature documentation | 12.4 |
| 13.2 | Canonical current-state reconciliation sweep | 13.1 |

#### 13.1 README palette documentation and v1.1 feature documentation

**Status:** Not started.

**Objective:** Document `TechnicalBackground`, `TechnicalText`, `StatusBackground`,
and `StatusText` with their meaning, default colors, and the 4.5:1 contrast rule
(Goal D), and update `README.md` for the v1.1 features.

**Traceability:** Goal D; v1.1 documentation checklist V1.1-7.

**Dependencies:** 12.4 (documents the completed v1.1 features).

**Affected files/components:** `README.md`.

**Work:** Add the palette table to `README.md`; document the settings UI,
allowlist, placement/size, verbosity, and inventory cache; remove the "no web
configuration UI" and restart-required claims; keep the incremental README
updates made by Phases 9-12 consistent.

**Tests:** Manual review that each field has a meaning, default color, and the
contrast rule; that no stale "no web configuration UI"/restart/F1/F2 claim
remains; that the documented behavior matches the shipped behavior.

**Acceptance criteria:** The four fields are documented with their meaning,
default colors, and the 4.5:1 contrast rule; `README.md` describes the v1.1
features and no longer claims the removed limitations.

**Decision gates:** None (Goal D has no ADR).

**Review:** `documentation-maintainer`.

**Documentation impact:** `README.md`.

**Definition of done:** The palette and v1.1 feature documentation is accurate and
complete.

#### 13.2 Canonical current-state reconciliation sweep

**Status:** Not started.

**Objective:** Reconcile `docs/project-status.md`, `docs/changelog.md`, and
`docs/implementation-readiness.md` with the v1.1 state.

**Traceability:** v1.1 documentation checklist V1.1-7.

**Dependencies:** 13.1.

**Affected files/components:** `docs/project-status.md`, `docs/changelog.md`,
`docs/implementation-readiness.md`.

**Work:** Reconcile the current-state framing and any stale limitation/status
claims; keep historical entries historical and do not rewrite V1 history.

**Tests:** Search for stale claims (no logging, no web UI, F1/F2 open, V1 phase
status) and confirm each is corrected or intentionally historical.

**Acceptance criteria:** The canonical current-state surfaces describe the v1.1
state with no stale claim presented as current.

**Decision gates:** None.

**Review:** `documentation-maintainer`.

**Documentation impact:** the named documents.

**Definition of done:** The canonical current-state surfaces are consistent with
v1.1.

**Phase 13 acceptance criteria:**

- [ ] The four palette override fields are documented with their meaning, default
  colors, and the 4.5:1 contrast rule.
- [ ] `README.md` documents the v1.1 settings UI, allowlist, placement/size,
  verbosity, and inventory cache behavior.
- [ ] `docs/project-status.md`, `docs/changelog.md`, and
  `docs/implementation-readiness.md` are reconciled with no stale claim.

**Gate 13:** Met when tasks 13.1-13.2 meet their acceptance criteria, the phase
review is approved, and the tag `v1.1.0-phase13` is created.

### 14. v1.1 release

**Goal:** v1.1 release (outline task V1.1-8). **Release:** v1.1.0. **Phase tag:**
none (this phase ends with the release tag `v1.1.0`).

**Objective:** Bump ArrTags to `1.1.0.0`, build and package reproducibly, run the
full suite and the live pinned-host verification, run the release security review
(including the ADR-016 settings UI surface), and complete the changelog/release
documentation and the annotated `v1.1.0` tag.

**Deliverables:**

- Version `1.1.0.0` in `build.yaml` and `Directory.Build.props`.
- A reproducibly built `artifacts/ArrTags_1.1.0.0.zip`.
- The full default and host-guarded test suites passing.
- The recorded live pinned-host verification of the v1.1 matrix.
- The release security-review report and any accepted limitations.
- Updated `docs/changelog.md`, `docs/project-status.md`,
  `docs/release/build-and-release.md`, `manifest.json`, and `build.yaml`.
- The annotated tag `v1.1.0`, created only after the `release-reviewer` gate; the
  GitHub publish remains the user's manual step.

**Tasks:**

- [ ] 14.1 Version bump and release metadata.
- [ ] 14.2 Release build, full suite, reproducible artifact, and release
  documentation.
- [ ] 14.3 Live pinned-host verification.
- [ ] 14.4 Release security review.
- [ ] 14.5 Changelog, release-readiness verification, manifest commit, and the
  annotated `v1.1.0` tag.

**Authoritative Phase 14 execution order:** 14.1, 14.2, 14.3, 14.4, 14.5. Task IDs
are stable references only; this execution order is the canonical sequence. When
the execution order and task numbering conflict, the execution order wins.

| Order | Task | Depends on |
| --- | --- | --- |
| 14.1 | Version bump and release metadata | 13.2 |
| 14.2 | Release build, full suite, reproducible artifact, and release documentation | 14.1 |
| 14.3 | Live pinned-host verification | 14.2 |
| 14.4 | Release security review | 14.3 |
| 14.5 | Changelog, release-readiness verification, manifest commit, and the annotated `v1.1.0` tag | 14.4 |

#### 14.1 Version bump and release metadata

**Status:** Not started.

**Objective:** Set the release version to `1.1.0.0` and update the version
literals and metadata.

**Traceability:** v1.1 release; v1.1 release identity section 1.

**Dependencies:** 13.2.

**Affected files/components:** `build.yaml`, `Directory.Build.props`, the
packaging/state/discovery test version literals.

**Work:** Set `version: "1.1.0.0"` in `build.yaml` and `<Version>`/
`<AssemblyVersion>`/`<FileVersion>` to `1.1.0.0` in `Directory.Build.props`;
update the hard-coded version literals in the packaging/state/discovery tests;
update `build.yaml` `overview`/`description`/`changelog` to describe the shipped
v1.1 behavior. Do not change `guid`, `targetAbi`, `framework`, `category`,
`owner`, or the `artifacts` list.

**Tests:** Build at `1.1.0.0`; the package is `artifacts/ArrTags_1.1.0.0.zip`;
`build.yaml` version/ABI/framework match the declared pins.

**Acceptance criteria:** The version is `1.1.0.0` in `build.yaml` and
`Directory.Build.props`, and the metadata accurately describes the shipped v1.1
behavior.

**Decision gates:** None.

**Review:** implementation-reviewer (default).

**Documentation impact:** `build.yaml`, `Directory.Build.props`.

**Definition of done:** The project builds and packages at `1.1.0.0`.

#### 14.2 Release build, full suite, reproducible artifact, and release documentation

**Status:** Not started.

**Objective:** Build and package reproducibly at `1.1.0.0` and record the artifact
identity, commands, and supported ranges.

**Traceability:** v1.1 verification requirements section 9 (reproducible
build/package at `1.1.0.0`).

**Dependencies:** 14.1.

**Affected files/components:** `artifacts/ArrTags_1.1.0.0.zip`, `manifest.json`,
`docs/release/build-and-release.md`, `build.yaml`.

**Work:** Clean rebuild; run the full default and host-guarded suites; run
`./build.sh package`; regenerate `manifest.json` and
`docs/release/build-and-release.md`; record the SHA-256/MD5 and entries.

**Tests:** Build 0 warnings / 0 errors; both suites pass; the archive is
byte-stable across repeated package runs; the manifest checksum matches the
artifact.

**Acceptance criteria:** The v1.1 artifact is reproducible and documented.

**Decision gates:** None.

**Review:** `test-quality-reviewer` (reproducibility evidence).

**Documentation impact:** `docs/release/build-and-release.md`, `manifest.json`,
`build.yaml`.

**Definition of done:** The reproducible artifact identity is recorded.

#### 14.3 Live pinned-host verification

**Status:** Not started.

**Objective:** Run the documented pinned-host matrix for v1.1 (settings page
load/save, post-save re-render, the configuration-rejection activity-log entry,
cache behavior, and log output/level) and record the reproducible results.

**Traceability:** v1.1 verification requirements section 9; Goals A, C, and F;
ADR-021 (Phase 9 finding P9-F1: the `IActivityManager`/`ActivityLog` host ABI is
pinned-source- and unit-verified but not live-verified).

**Dependencies:** 14.2.

**Affected files/components:** `docs/testing/jellyfin-12-musl-test-host.md`, the
recorded live-verification result.

**Work:** Use the `live-host-verifier` procedure in
`docs/testing/jellyfin-12-musl-test-host.md` against the pinned Jellyfin `12.0.0`
host; record the matrix result in machine-readable form. Include an explicit
matrix line for ADR-021: a rejected configuration save writes a bounded,
secret-free administrator-visible activity-log entry that appears in the
dashboard Activity log, and a valid save writes none.

**Tests:** The live matrix passes; results are reproducible.

**Acceptance criteria:** The live verification is recorded and passes.

**Decision gates:** None.

**Review:** `live-host-verifier`.

**Documentation impact:** `docs/testing/jellyfin-12-musl-test-host.md` and the
recorded result.

**Definition of done:** The live v1.1 matrix is recorded and passes.

#### 14.4 Release security review

**Status:** Not started.

**Objective:** Obtain a fresh security review covering the new logging path and
the SEC-5 rewrite, and the ADR-016 settings page and administrator save path.

**Traceability:** v1.1 verification requirements section 9; ADR-016, ADR-020.

**Dependencies:** 14.3.

**Affected files/components:** the security-review report,
`docs/limitations.md` (accepted limitations).

**Work:** Run the `security-reviewer` audit on the release candidate; record the
report and any accepted limitations in `docs/limitations.md`.

**Tests:** The review report is recorded with no open BLOCKER/HIGH (or accepted
limitations recorded).

**Acceptance criteria:** The release security review is recorded with no open
BLOCKER/HIGH.

**Decision gates:** None.

**Review:** `security-reviewer`.

**Documentation impact:** `docs/limitations.md` (accepted limitations).

**Definition of done:** The release security review is complete and recorded.

#### 14.5 Changelog, release-readiness verification, manifest commit, and the annotated v1.1.0 tag

**Status:** Not started.

**Objective:** Complete the changelog/project-status, verify release readiness,
commit the manifest, and create the annotated `v1.1.0` tag after the
`release-reviewer` gate.

**Traceability:** v1.1 release; v1.1 documentation checklist V1.1-8.

**Dependencies:** 14.4.

**Affected files/components:** `docs/changelog.md`, `docs/project-status.md`,
`manifest.json`, the annotated tag `v1.1.0`.

**Work:** Update `docs/changelog.md` and `docs/project-status.md`; commit
`manifest.json`; run the `release-reviewer` gate; create the annotated tag
`v1.1.0`; leave the push and GitHub release to the user (consistent with V1
limitation P6).

**Tests:** The release-review report exists with `reviewer_status` `APPROVED` or
`APPROVED_WITH_ACCEPTED_LIMITATIONS` and no open BLOCKER/HIGH; the tag `v1.1.0`
exists; the working tree is clean.

**Acceptance criteria:** `docs/changelog.md` and `docs/project-status.md` reflect
v1.1; the release-review gate passes; the annotated tag `v1.1.0` exists; the
GitHub publish remains the user's manual step.

**Decision gates:** None. The release tag is created only after the
`release-reviewer` gate.

**Review:** `release-reviewer` (gate).

**Documentation impact:** `docs/changelog.md`, `docs/project-status.md`,
committed `manifest.json`, the annotated tag.

**Definition of done:** The v1.1 release is prepared, reviewed, and tagged; the
publish remains the user's manual step.

**Phase 14 acceptance criteria:**

- [ ] The version is `1.1.0.0` in `build.yaml` and `Directory.Build.props`.
- [ ] `./build.sh package` produces a reproducible `artifacts/ArrTags_1.1.0.0.zip`
  with recorded identity.
- [ ] The full default and host-guarded suites pass.
- [ ] The live pinned-host verification passes and is recorded.
- [ ] The release security review is recorded with no open BLOCKER/HIGH.
- [ ] `docs/changelog.md` and `docs/project-status.md` reflect v1.1.
- [ ] The annotated tag `v1.1.0` is created after the release-reviewer gate; the
  GitHub publish remains the user's manual step.

**Gate 14:** Met when tasks 14.1-14.5 meet their acceptance criteria, the full
suite and the live pinned-host matrix pass, the release security review has no
open BLOCKER/HIGH, the phase review is approved, and the `release-reviewer` gate
passes so the annotated tag `v1.1.0` can be created. The GitHub release
publication is explicitly left to the user.

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

These items are deferred until the V1 gates are complete. They remain within the
existing goals or are explicitly identified as future schema work; they do not
expand V1 scope by themselves.

**v1.1 promotion:** The provider catalogue/inventory cache (F1) and the runtime
configuration replacement wiring (F2) are promoted into v1.1 and are now tracked
in [`docs/planning/v1.1.md`](docs/planning/v1.1.md) (Goals C and A, resolved by
ADR-018 and ADR-016). They are planned as **Phase 11** (provider inventory cache,
resolves F1) and **Phase 9** (dashboard settings UI and runtime activation,
resolves F2) in "v1.1 Milestones (Phases 9-14)". The remaining open items below
stay deferred unless a later decision promotes them.

- Add additional normalized badge metadata already identified in the data model,
  such as bit depth, frame rate, scan type, language, subtitles, release group,
  edition, custom-format score, certification, stream count, or provider
  extensions, when reliable source semantics and configuration are defined.
- Add further badge definitions and visual primitives without coupling them to
  provider DTOs or changing the render pipeline contract.
- Add additional supported item or image surfaces only after defining an
  explicit aggregation and eligibility policy; do not infer aggregate quality
  from one child file.
- Expand the supported Sonarr/Radarr version range after compatibility tests and
  capability rules are available.
- Consider support for another metadata service only if a later scope decision
  requires it and the provider-neutral integration boundary remains valid.
- Reconsider configured, connection-scoped Jellyfin-to-Arr path fallback only
  through a new architecture decision defining its namespaces, normalization,
  ambiguity, and location-safety contract.

**Open items carried out of Phase 7 (from the Phase 6 review and the Phase 7
reviews).** The v1.1-promoted items below (the provider inventory cache and the
runtime configuration replacement wiring) are now closed; the remaining
non-promoted items stay open and are not presented as solved. They are
consolidated, with their evidence and criteria status, in `docs/limitations.md`
(Phase 7 task 7.6):

- **Promoted to v1.1 (ADR-018):** provider catalogue/inventory cache. As
  recorded in Phase 7, every reconciliation work item then re-read the whole
  provider library and then the per-record file resource, so a large library scan
  performed one full provider-library read per event, and the Phase 6 acceptance
  criterion "unchanged metadata and source state do not repeatedly fetch ...
  work unnecessarily" was therefore only partially met for provider fetches;
  render and publication were correctly fingerprint-gated. v1.1 Phase 11 adds a
  bounded, short-TTL provider inventory/catalogue cache with ArrTags-side
  invalidation; task 11.4 records limitation F1 as resolved, so this item is
  closed. The remaining bounds stay as recorded in `docs/limitations.md` F1
  (nothing is cached on a failed library read, last-known-good is retained by the
  per-item bounded state, and Sonarr per-series episode selection remains
  O(series) per window).
- **Promoted to v1.1 (ADR-016):** runtime configuration replacement wiring. `ConfigurationSnapshotService.TryReplace`
  is implemented and validated, and v1.1 task 9.3 wires it to Jellyfin's
  configuration-update mechanism (`Plugin.UpdateConfiguration`), so a saved
  webhook secret, provider enable/disable, badge/selector change, or DG-6 limit
  change is observed without a restart for the values resolved per operation. The
  bounded work queue, provider/render concurrency limiters, and metadata freshness
  window resolve their values from the current snapshot per operation; some
  construction-captured limits (the artifact-size/decode limits, the render
  work-cache TTL/quota, and terminal-provenance retention) still require a host
  restart (`docs/limitations.md` F2). Task 9.4 adds
  the bounded post-save re-render trigger, so a successful save also re-renders
  existing posters promptly; task 9.5 records limitation F2 as resolved, so this
  item is closed.
- Safe metrics/diagnostic-status surface. Queue depth, provider health, matching,
  cache, rendering, and stale-data counters exist internally but there is no
  bounded, secret-free user-facing or diagnostic status surface. Architecture
  section 12 states this as a recommendation, not a hard gate, so it is deferred
  rather than implemented.
- Reconciliation coverage bound. Scheduled/post-scan/manual runs enumerate from
  the start of a deterministic order, so a scope larger than `QueueCapacity`
  (default 512) covers only a bounded prefix and successive runs overlap rather
  than advancing; a persisted enumeration cursor, stale/unknown-only enqueue, or
  direct pipeline drive is not implemented (`docs/architecture.md` lines
  472-478).
- Optional concurrency hardening. `ArtworkSubjectGate.Gates` is a process-local
  static per-subject `SemaphoreSlim` dictionary with no idle eviction (task 7.4
  reviewer finding 7.4-R1); dropping idle entries on the lifecycle drain or an
  LRU bound is optional future hardening, not a V1 requirement.
- Webhook `401` response wording. Live `401` responses carry the ASP.NET Core
  `[ApiController]` framework `application/problem+json` ProblemDetails
  (`type`/`title`/`status`/`traceId`) while ADR-012 says "no body"; no sensitive
  detail is reflected. Recorded as a minor documentation/behavior gap for a
  future ADR-012 clarification (task 7.4 reviewer finding 7.4-R2); the ADR text
  is intentionally unchanged by task 7.6.

**Task 7.2 release blocker (Phase 7 acceptance criterion 2).** The standard
versioned install layout (`PluginsPath/<Name>_<Version>`) is unsafe once the
plugin has persisted state: the plugin's Jellyfin-derived data folder
`PluginsPath/<assembly name>` and the versioned install folder collide in
`PluginManager.DiscoverPlugins`, which deletes the install folder on the next
host restart and loads no ArrTags plugin (observed live on the pinned Jellyfin
`12.0.0` host; see the task 7.2 status). **Resolved by task 7.7 and ADR-014:**
the plugin state root is relocated to `ProgramDataPath/ArrTags` outside
`PluginsPath`, so it can never be enumerated as a same-named plugin folder, and
the re-run live install/upgrade/reload/uninstall verification passes with state
present and the install folder preserved.

**Task 7.3 release blocker (Phase 7 acceptance criteria 3 and 4) - resolved by
task 7.8.** The committed package's bundled
`SkiaSharp.dll`/`libSkiaSharp.so` (task 4.8/5.4) conflicted fatally with the
pinned host's own SkiaSharp: the first badge publication called Jellyfin's
`ProviderManager.SaveImage`, whose image processing aborted the process with
`InvalidCastException` between the host default-context
`SkiaSharp.UserDataDelegate` and the plugin-context one, killing Jellyfin (task
7.3 finding 7.3-F1). Removing the bundled assets from the installed plugin folder
so the plugin shared the host's SkiaSharp made the full pipeline work, so the
conflict was the duplicate, not the render/publication logic.

**Resolved by task 7.8 and ADR-015:** the plugin compiles against the pinned
SkiaSharp but ships no renderer runtime, `ArrTags.csproj`/`build.yaml` and the
`PluginPackagingTests` describe the host-shared contract, and the live
end-to-end re-verification on the pinned host passes (no crash, published bytes
served and matching the persisted `ActiveImageIdentity`, source posters
preserved, changed/unchanged metadata behavior correct, and a provider outage
leaves the host up with current artwork unchanged). `GOALS.md` criteria 5 and 8
are met as shipped, with criterion 6 met for render and publication but only
partial for provider fetches (`docs/limitations.md` F1).

The following remain excluded from this plan unless `GOALS.md` is deliberately
changed: Jellyfin versions before 12, modifying original media files, writing or
managing Sonarr/Radarr metadata, a general poster-management system,
user-specific badges, non-poster artwork, and unapproved external services.
