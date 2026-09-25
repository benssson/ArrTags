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
