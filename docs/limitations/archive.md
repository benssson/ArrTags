# Resolved limitations (archive)

## Resolved limitations

These items were previously recorded here as open limitations and are now
resolved. They are retained with their evidence and the resolving change so the
current-state record stays traceable; the shipped behaviour is authoritative in
`docs/architecture.md`, `docs/data-model.md`, and `docs/decisions.md`.

### F1. Provider inventory/catalogue cache

**Status:** Resolved (v1.1 Phase 11, task 11.4), at the integration-test level.
One provider library read per connection serves every work item in a
reconciliation window, the ArrTags-side invalidation trigger set is wired, and
the Goal C integration verification is recorded. The live pinned-host
confirmation remains owned by task 14.3.

The bounded per-connection provider inventory cache (ADR-018) is defined (task
11.1), populated and consumed at the provider-client boundary (task 11.2), and
invalidated by the documented ArrTags-side trigger set (task 11.3): a
scheduled/manual or post-scan full reconciliation clears the whole cache before it
enqueues work, a provider webhook removes only the advertised connection's
inventory before its hint is enqueued, and the bounded TTL is the fallback source,
with no provider conditional request, revision token, `history/since` watermark,
or SignalR dependency. On a cache miss the Radarr and Sonarr metadata readers
perform one library read (`/api/v3/movie`, `/api/v3/series`) plus the bulk file
reads, map the whole library to canonical observations, and serve every work item
in the configured TTL window from the cache without another provider request; the
bulk selection endpoints (Radarr `/api/v3/moviefile?movieId=` repeated ids;
Sonarr `/api/v3/episodeFile?episodeFileIds=`, with the embedded per-series episode
file preferred) replace per-item file reads. Concurrent cold readers for one
connection coalesce through a per-connection single-flight population gate, so one
library read serves all of them. Task 11.4 reconciles `docs/architecture.md`
sections 8 and 12 and `docs/data-model.md` section 6 with the shipped behavior,
adds the Goal C integration coverage for the one-read-per-window,
invalidation-source, bounds, and last-known-good facets, and records F1 resolved.

The resolution does not overstate the remaining bounds, which are recorded in
`docs/architecture.md` sections 8 and 12 and `docs/data-model.md` section 6:
cache population is eager whole-library on a miss; for Sonarr the per-series
episode read is one request per series per window (Sonarr exposes no whole-library
episode endpoint), so that provider's episode reads are O(series) rather than
O(connections) per window; a sparse (webhook/single-item) invalidation window
removes the whole connection inventory and repopulates the whole library, so on a
large Sonarr library the per-window request volume can exceed the pre-11.2
per-item read for a sparse window (per-record/per-series invalidation scoping is a
possible future optimization); an observation set that exceeds the configured
record or byte bound is not cached, so subsequent work items use the direct read
until the next successful store; a library read failure during a population fails
the whole window (the reader returns the bounded failure and caches nothing, and
waiting work items re-attempt the population), while a failed bulk/sub-read falls
back to the unchanged direct read rather than failing the window; a
full-reconciliation clear removes every cached connection inventory, including a
disabled connection's entry; and the TTL is never extended by a provider read
failure.

- Evidence: `PLANS.md` Phase 11 tasks 11.1-11.4; `docs/architecture.md` sections
  8 and 12; `docs/data-model.md` section 6; ADR-018; task 11.1-11.4 worker
  reports; `ProviderInventoryCacheIntegrationTests`,
  `InventoryCacheInvalidationTests`, `ArrInventoryCacheTests`,
  `ReconciliationTriggerTests`, and `WebhookResolutionTests`.
- Resolved consequence: on a large library, one full provider-library read occurs
  per connection per TTL window or per invalidating trigger instead of one per
  work item, so the `GOALS.md` Reliability/Performance goal "avoid unnecessary API
  requests to Sonarr and Radarr" is met for the provider library read within a
  window and for event-driven refresh. Render and publication remain
  fingerprint-gated. The live pinned-host confirmation is owned by task 14.3.

### F2. A saved configuration change is activated at runtime and re-renders existing posters via the bounded post-save trigger

**Status:** Resolved (v1.1 Phase 9, task 9.5). The restart requirement is gone
for the values resolved per operation and the bounded post-save re-render is
wired, consistent with ADR-016's consequences; a residual restart requirement
remains for the construction-captured limit values (see below and the ADR-016
decision-record note in `docs/decisions.md`).

`ConfigurationSnapshotService.TryReplace` is wired to Jellyfin's
configuration-update mechanism (task 9.3), so a saved change is activated
without a host restart for the values resolved per operation (see the residual
below). Task 9.4 adds the bounded, non-blocking post-save
reconciliation trigger: a successful replacement requests a reconciliation
through the plugin-owned `IConfigurationReconciliationTrigger` boundary, whose
hosted `ConfigurationReconciliationTrigger` loop runs the existing bounded
`LibraryReconciliationService` off the save thread and enqueues the same
provider-neutral work hints as every other trigger, so existing posters re-render
with the saved settings instead of waiting for the next library event, webhook,
post-scan, or scheduled run. The trigger is never a synchronous full-library
scan, never blocks the save response, and never throws into the host. Task 9.5
composes the full save -> activate -> bounded-reconcile flow in
`GoalAIntegrationTests` (the pinned POST deserialization, the real
`Plugin.UpdateConfiguration` override, the real snapshot service, the real
post-save trigger over the bounded reconciliation service, and the real artwork
publishing pipeline) and records F2 as resolved.

- Evidence: task 7.7, 7.3, 7.8, 9.3, 9.4, and 9.5 worker reports; `PLANS.md`
  Phase 9 tasks 9.3, 9.4, and 9.5; ADR-016; `docs/implementation-readiness.md`.
- Historical consequence: before task 9.3, a saved webhook-secret, provider
  enable/disable, badge/selector, or DG-6 limit change was **not observed until
  the process restarted**, and the live tasks 7.3 and 7.8 configured the plugin
  by writing `data/plugins/configurations/ArrTags.xml` and restarting. That
  restart consequence is resolved for the values that resolve per operation: the
  bounded work queue, provider/render concurrency limiters, metadata freshness,
  badge definitions, and the renderer output policy resolve their values from the
  current snapshot per operation, so the replaced snapshot is observed by
  subsequent work. A residual remains: some singletons capture the
  artifact-size/decode limits and the `StateRepository`-backed render work-cache
  TTL/quota and terminal-provenance retention values from `OperationalLimits` at
  construction, so those particular values still require a host restart; this is
  the pre-existing state/artwork-layer behaviour recorded in
  `docs/architecture.md` section 6, and ADR-016 clause 5's per-operation claim is
  therefore met for queue/concurrency/freshness/badge/renderer-output but not for
  those construction-captured values (see the ADR-016 note in
  `docs/decisions.md`). The re-render promptness consequence is resolved by task
  9.4's bounded post-save trigger.
- Resolved state (v1.1): task 9.2 added the dashboard settings page, which reads
  and saves the configuration through Jellyfin's elevation-gated
  `PluginsController` path, so an operator no longer has to edit
  `ArrTags.xml` by hand. Task 9.3 overrides `Plugin.UpdateConfiguration` to
  validate the candidate before the base implementation persists it: a valid
  candidate is persisted and activated at runtime, while an invalid candidate is
  rejected before persistence (it is never written to `ArrTags.xml`) and the last
  valid public snapshot and private secret generation remain active and
  persisted. Task 9.4 requests the bounded, non-blocking post-save reconciliation
  after a successful replacement, so an already-published poster re-renders
  promptly with the saved settings. Task 9.5 verifies the composed flow at the
  integration-test level without a live host.

### F8. The specific render classification is not surfaced in the artwork log

**Status:** Resolved (v1.2 Phase 15; the classification is emitted by task 15.3,
and the documentation reconciliation and the log-path security review are task
15.4).

`ArtworkGenerationCoordinator.LogOutcome` appends the specific bounded
classification to the single artwork-boundary record when the generation result
carries one: ` (PassThroughReason=<RenderPassThroughReason>)` for a pass-through,
` (FailureReason=<RenderFailureReason>)` for a render failure, and
` (SourceFailureReason=<ArtworkSourceReadFailureReason>)` for a source-read
failure, placed between the bounded outcome and the generic reason. The values
are the bounded enum members the result already carries, named by their enum
kind, so a pass-through, a render failure, and a source-read failure with
similar-looking causes stay distinguishable; no exception message, provider
payload, path, credential, or other unbounded value is reachable. When the
result carries no classification (published, no source, blocked, cancelled, or
publication-not-completed), the qualifier is empty and the record is
byte-identical to the pre-task shape. The ADR-024 `NoFittingBadge` reason is
owned by Phase 20 and is not part of this resolution. `LogRedactionTests` covers
the three classifications and the omission at every verbosity level, with the
source-failure drive proving the reader's raw detail does not reach the log.

The record that emits the classification is the Information-level
artwork-generation line, so it keeps the existing ADR-020 gating (the
classification is not visible at `Off`, `Error`, or `Warning`). The subject on
that line is the bounded file-name log subject (ADR-026, amending ADR-020
clause 4), and the amended log path is security-reviewed for task 15.4
(`docs/implementation/15.4/security-review.json`, PASS_WITH_FINDINGS with no
open BLOCKER/HIGH).

- Evidence: `src/ArrTags/Artwork/ArtworkGenerationCoordinator.cs` (`LogOutcome`,
  `DescribeClassification`); `tests/ArrTags.Tests/LogRedactionTests.cs` (the four
  classification theories over six verbosity levels);
  `docs/implementation/15.3/worker-report.json`;
  `docs/implementation/15.4/worker-report.json`; ADR-020 clause 4 as amended by
  ADR-026; `docs/architecture/12-performance-and-operational-limits.md`.
- Historical consequence: before task 15.3, diagnosing why no badge was rendered
  required eliminating causes from other log lines (or reproducing with tests);
  two attempts with different causes were indistinguishable in the host log.

### F3. No bounded, secret-free metrics/diagnostic-status surface

**Status:** Resolved (v1.2 Phase 17; the bounded metrics model is task 17.1, the
elevation-gated endpoint is task 17.2, the read-only panel is task 17.3, and the
canonical documentation reconciliation, the register resolution, and the
security review are task 17.4).

The shipped read-only diagnostics surface is a "Diagnostics" panel on the
existing settings page backed by one administrator-authenticated plugin route,
`GET ArrTags/Status` (`ArrTagsStatusController`, class-level
`[Authorize(Policy = Policies.RequiresElevation)]`; ADR-025 clauses 1-5). The
route returns the fixed-shape `DiagnosticsSnapshot`: nine top-level fields and 28
leaf paths, namely the update-queue depth and in-flight count; the last observed
Sonarr and Radarr connection health (bounded `ArrConnectionHealth`); the three
non-matched `MediaMatchStatus` classifications (`NotFound`, `Ambiguous`,
`Unsupported`); provider-inventory cache hits and misses; one render-failure
count per declared `RenderFailureReason` (18 classifications); and the
fresh-to-stale metadata-transition count. Every value is a count or a bounded
enum: no path, item name, item identifier list, provider payload, credential,
secret value, or unbounded collection is serialized, and there is no per-item
array. The counters are recorded only at existing bounded boundaries
(inventory-cache hit/miss in the Radarr and Sonarr metadata readers,
per-connection health in the concurrency-limited reader, matching classification
and the stale transition in `MetadataReconciliationProcessor`, and render failure
in `ArtworkGenerationCoordinator`), are in-memory and process-lifetime, and are
never persisted, so they reset on restart and report current process state rather
than durable history. The counter set is a contract: adding or removing a
counter requires a new decision (ADR-025 clause 3). The endpoint is read-only (a
single read route, no write verb, no mutation of configuration, work, or
artwork, and no configuration setting).

The panel is part of the anonymous static settings-page resource but obtains its
data solely from the elevation-gated route, so an authenticated administrator is
required to see any value. It is fail-closed: the container is static-hidden and
empty until a bounded snapshot renders; a rejected or unavailable request and an
unparseable or non-object body leave it cleared. A well-formed but unexpected or
partial-shape object instead renders the fixed 28-row counter table with the
documented bounded fallback (a missing or non-numeric count as `0`, an
unrecognized health value as `Unknown`); counts render only after the bounded
numeric check, and a health value renders only when it is a declared
`ArrConnectionHealth` name. The panel adds no input, form field, or save wiring
and contains no secret literal.

- Evidence: `src/ArrTags/Diagnostics/DiagnosticsSnapshot.cs`,
  `MatchingFailureCounts.cs`, `RenderFailureCounts.cs`, `DiagnosticsMetrics.cs`,
  `DiagnosticsSnapshotProvider.cs`, and `ArrTagsStatusController.cs`;
  `src/ArrTags/Configuration/config.html` (the `diagnosticsSection` block and its
  fail-closed load path); the tasks 17.1-17.3 worker reports and changelog
  entries; `tests/ArrTags.Tests/DiagnosticsSnapshotTests.cs`,
  `DiagnosticsInstrumentationTests.cs`, `DiagnosticsStatusEndpointTests.cs`, and
  `DiagnosticsStatusPanelTests.cs`; ADR-025 with its v1.2 implementation note;
  `docs/implementation/17.4/worker-report.json`; the ADR-025 clause 6 security
  review at `docs/implementation/17.4/security-review.json`.
- Verification bounds (not overstating the resolution): the panel's inline
  JavaScript is not executed by the suite or the documented live matrix (the
  repository deliberately has no JavaScript runtime and the pinned host runs
  `--nowebclient`), so the panel behavior is verified structurally against the
  endpoint model and the pinned web-client source; the endpoint authorization is
  pinned by the in-process MVC pipeline tests, while the host-guarded
  policy-name fact is skipped in this environment (`ARRTAGS_JELLYFIN_HOST_DIR`
  unset), so the live host-side authorization is not confirmed here; and the
  counters are process-lifetime only.
- Historical consequence: before Phase 17, queue depth, provider health,
  matching, cache, rendering, and stale-metadata counters existed internally but
  there was no bounded, secret-free status surface; ADR-020 clause 7 scopes
  logging as a diagnostic mechanism rather than a metrics/status surface, so
  operators had no supported in-product view. The panel and endpoint now provide
  that view without exposing credentials or full external payloads, and
  architecture section 12 records the shipped shape.

### F4. Reconciliation coverage is bounded by `QueueCapacity`

**Status:** Resolved (v1.2 Phase 19; the persisted cursor is task 19.1, the
successive-run coverage matrix is task 19.2, and the canonical documentation
reconciliation and limitation resolution are task 19.5). The mechanism is
ADR-022 as amended for clause 3 by ADR-029.

The scheduled and post-scan whole-scope triggers now advance a persisted,
bounded `Cache`-authority `(SortName, itemId)` reconciliation cursor over the
candidate order and wrap at the end, so successive runs cover a scope larger
than `QueueCapacity` round-robin instead of re-covering the same prefix. The
cursor is a single bounded, versioned, integrity-checked record (fixed kind
`reconciliation-cursor`) carrying only the scope identity and the position, and
it resets to the start on an enabled-library/item-type scope change and on a
missing or torn record. The bounded enqueue boundary reports the ADR-022 clause 1
outcome vocabulary (`Accepted`, `Coalesced`, `InFlight`, `Overflow`, `Stopped`);
`Accepted`, `Coalesced`, and `InFlight` are covered (a coalesced or in-flight
duplicate does not stall the run), an inspected item that correctly needs no work
is covered, and the first `Overflow` or `Stopped` stops the run with the cursor
advanced only over the covered prefix. Events, webhooks, per-item triggers, and
the post-save trigger do not read or write the cursor. Because the pinned
Jellyfin 12 host orders the candidate query by `SortName` then raw `Name` and
offers no keyset/after-key predicate, the resume is identity-anchored: the run
walks the host-ordered pages from the start until it locates the record's unique
`itemId`, returns the page strictly after it, and resets to the start when the
anchor cannot be located (a removed or no-longer-enumerated item).

- Evidence: tasks 19.1 and 19.2 worker/reviewer reports;
  `docs/architecture/08-reconciliation-and-update-flow.md`; ADR-022 as amended by
  ADR-029; `src/ArrTags/Reconciliation/ReconciliationCursorStore.cs` and
  `LibraryReconciliationService.cs`, `src/ArrTags/Media/JellyfinMediaLibraryEnumerator.cs`,
  `src/ArrTags/Updates/WorkHintEnqueueOutcome.cs`, and `LibraryWorkQueue.cs`;
  `ReconciliationCursorStoreTests`, `ReconciliationCursorResumeTests`,
  `MediaLibraryResumeTests`, `SuccessiveRunCoverageMatrixTests`, and
  `LibraryWorkQueueTests`.
- Verification bounds and registered residuals: the coverage matrix drives the
  real enumerator, reconciliation service, cursor store, and bounded queue over
  an `ILibraryManager` double that reproduces the pinned host's documented
  `(SortName, Name)` order and `StartIndex`/`Limit` paging; it does not run the
  host SQL and no live pinned-host run is claimed. The registered residuals are
  accepted items `V12-F4-1` (per-run resume is `O(offset)` rows and a full cycle
  is `O(N²/QueueCapacity)` cumulative skip work over the server-wide candidate
  count `N`), `V12-F4-2` (rows tying on the exact `(SortName, Name)` pair have
  no host-guaranteed order), `V12-F4-3` (the cursor is a `Cache` record subject
  to render-cache age pruning), `V12-F4-4` (an unlocatable anchor resets the run
  to the start), `V12-F4-5` (the host order is an internal implementation
  detail), and `V12-F4-6` (overlapping whole-scope runs are not cross-run
  locked), each in `docs/limitations/00-index.md`.
- Historical consequence: before Phase 19, every scheduled/post-scan run
  re-enumerated from index zero and the queue dropped overflow, so a scope larger
  than `QueueCapacity` was covered only by its prefix and successive runs
  overlapped on that prefix. Event, webhook, and per-item triggers were never
  affected.

### F6. Version-blind work coalescing can drop a post-save re-render

**Status:** Resolved (v1.2 Phase 19; the `DiscardReason` classification and the
stale-basis re-enqueue are task 19.3, the coalescing coverage matrix is task
19.4, and the canonical documentation reconciliation and limitation resolution
are task 19.5). The mechanism is ADR-023.

A discard now carries a bounded `DiscardReason` (`ConfigurationStale`,
`ItemMissing`, `Ineligible`, `ConnectionChanged`, `IdentityUnavailable`), and
exactly one classification, `ConfigurationStale` (the work item's carried
configuration version is no longer current at the discard point, covering both
the initial version check and the pre-publication version re-check), causes the
worker to re-enqueue exactly one fresh work item at the current configuration
version for the same `WorkItemKey`. The re-enqueue happens after
`CompleteProcessing` releases the in-flight slot, so it cannot be coalesced away;
the fresh item carries the current version, so the effective bound is one
re-enqueue per (item, connection, image surface, configuration version) and it
cannot loop. A non-`ConfigurationStale` discard, a same-version discard, a
stopped queue, and a full queue do not re-enqueue (a stopped or full queue drops
the re-enqueue rather than blocking), and `WorkItemKey`, the version-blind
coalescing, and per-item/surface single-flight are unchanged. An item whose work
was pending or in flight when a save activated the new version is therefore
re-rendered by the save instead of waiting for the next trigger.

- Evidence: tasks 19.3 and 19.4 worker/reviewer reports; ADR-023;
  `docs/architecture/08-reconciliation-and-update-flow.md`;
  `src/ArrTags/Updates/DiscardReason.cs`, `WorkProcessingResult.cs`,
  `LibraryWorkWorker.cs`, `LibraryWorkQueue.cs`, and `WorkItemKey.cs`;
  `src/ArrTags/Reconciliation/MetadataReconciliationProcessor.cs`;
  `StaleBasisReenqueueTests`, `CoalescingCoverageMatrixTests`,
  `MetadataReconciliationProcessorTests`, and `LibraryWorkQueueTests`; the task
  6.8 `Phase6Harness` durable-store/renderer/publisher integration.
- Verification bounds and registered residuals: the coverage matrix drives the
  real `Plugin.UpdateConfiguration` save path, the real post-save trigger and
  bounded queue, the real hosted worker, and the real composed processor chain;
  it does not use a live host and the default suite skips the host-guarded facts
  (no live pinned-host run is claimed). Two residuals are registered as accepted
  items in `docs/limitations/00-index.md`: `V12-F6-1` (the configuration version
  advancing between the metadata-state publication and the artwork stage of the
  same pass is unchanged; that pass performs no artwork work and the new artwork
  applies on the item's next trigger) and `V12-F6-2` (the bounded drop of the
  re-enqueue is not logged or counted).
- Historical consequence: before task 19.3, a post-save hint carrying the new
  configuration version was coalesced away when the item already had pending or
  in-flight work under the previous version, and the outstanding item discarded
  itself as a stale basis and completed without re-enqueueing, so the item kept
  its old artwork until a later trigger admitted it.
