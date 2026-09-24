# ArrTags known limitations and deferred decisions

This document is the canonical current-state record of what ArrTags does
**not** yet do, or has **not** yet verified, so that no unsupported or
unverified capability is presented as available. It covers the shipped V1 scope
and the v1.1 work; the **Resolved limitations** section retains items that were
previously listed here and are now resolved, with their resolving change, so the
current-state record stays traceable. It is owned by Phase 7 task
7.6 and consolidates the deferred decisions previously scattered across
`PLANS.md` (Post-V1 Backlog), `docs/implementation-readiness.md`,
`docs/release/build-and-release.md`, and the Phase 6/7 review findings.

It does **not** redefine V1 scope: the in-scope and out-of-scope lists in
`GOALS.md` still govern. Items here are either deliberate V1 exclusions,
implementation-time deferrals, verification gaps caused by the available
environment, or resolved items retained for traceability. Each item names its
evidence (an ADR, a task report under
`docs/implementation/`, or a documented review finding).

Architecture and accepted decisions remain authoritative in
`docs/architecture.md` and `docs/decisions.md`.

## V1 success criteria status

`GOALS.md` lists nine success criteria. The live verification task 7.3 and its
superseding task 7.8 exercise them on the pinned Jellyfin `12.0.0` musl host
against the committed mock Sonarr/Radarr fixture.

| # | `GOALS.md` success criterion | Status | Limitation |
| --- | --- | --- | --- |
| 1 | Installs and loads correctly on Jellyfin 12 | Met | — |
| 2 | Sonarr and Radarr configured independently | Met | — |
| 3 | Jellyfin movie/TV item matched to its Arr item | Met | — |
| 4 | Metadata (e.g. quality) retrieved through the API | Met | — |
| 5 | Metadata rendered as a badge on the poster | Met as shipped | Requires the host to supply a compatible SkiaSharp (item F5); no bundled fallback. |
| 6 | Poster updates occur without unnecessary repeated processing | Met for render/publication and for the provider library read within a window | One provider library read per connection serves every work item in the reconciliation window, and the ArrTags-side invalidation trigger set is wired (item F1 resolved, task 11.4); render and publication are fingerprint-gated. Remaining bounds: Sonarr per-series episode reads are O(series) per window and a sparse invalidation window repopulates the whole library (item F1). |
| 7 | Operates correctly alongside Jellyfin Enhanced | Met at the contract level only | Jellyfin Enhanced is not installed on the pinned host (item V3). |
| 8 | Failures do not adversely affect Jellyfin | Met as shipped | — |
| 9 | Buildable and testable reproducibly | Met | Reproducibility is guaranteed only for the pinned SDK (item P1). |

Phase 7 acceptance criteria as recorded in `PLANS.md`:

1. The full test suite passes from a clean checkout — **met** by task 7.1.
2. The packaged plugin loads and operates on the declared Jellyfin 12 ABI —
   **met** by task 7.7.
3. Sonarr and Radarr movie/television scenarios pass with unchanged and changed
   metadata — **met** by task 7.8.
4. Provider, matching, rendering, artwork, cache, and lifecycle failures do not
   adversely affect Jellyfin — **met** by task 7.8.
5. The release artifact and build process are reproducible and documented —
   **met** by task 7.5.

All five Phase 7 acceptance criteria are met. Gate 7 and the Phase 7 phase
review are separate and are not declared by this document.

## Functional and operational limitations (deferred, not implemented)

### F3. No bounded, secret-free metrics/diagnostic-status surface

Queue depth, provider health, matching, cache, rendering, and stale-data
counters exist internally, but there is no bounded, secret-free user-facing or
diagnostic status surface.

- Evidence: `docs/architecture.md` lines 1209-1212 (documented as an open
  limitation here); `PLANS.md` Post-V1 Backlog.
- Consequence: operators have no supported in-product view of queue depth or
  provider health. Architecture section 12 states this as a recommendation, not
  a hard V1 gate.

### F4. Reconciliation coverage is bounded by `QueueCapacity`

The scheduled and post-scan scopes are enumerated from the start of a
deterministic order and the queue drops overflow, so a single run over a scope
larger than `QueueCapacity` (default 512) covers only a bounded prefix.
Successive runs re-cover the same prefix rather than advancing; there is no
persisted enumeration cursor, stale/unknown-only enqueue, or direct pipeline
drive.

- Evidence: Phase 6 review MEDIUM item; `docs/architecture.md` lines 472-478;
  `PLANS.md` Post-V1 Backlog.
- Consequence: for a scope larger than `QueueCapacity`, some items are not
  reconciled by a single run and successive runs do not guarantee full
  coverage. Event, webhook, and per-item triggers are not affected.

### F5. The renderer depends on a host-supplied SkiaSharp with no bundled fallback

V1 compiles against the pinned `SkiaSharp`/`SkiaSharp.NativeAssets.Linux`
`3.119.4` but ships no renderer runtime and takes the managed assembly and
native library from the Jellyfin host (ADR-015, superseding the bundling parts
of ADR-010).

- Evidence: task 7.8 worker report and ADR-015; `docs/architecture.md` section 9.
- Consequence: a host that does not provide a compatible SkiaSharp would make
  rendering fail closed (pass-through/preserve current artwork) rather than use
  a bundled copy. Validated only on the pinned Jellyfin `12.0.0`
  `linux-musl-x64` host; the plugin makes no RID-specific claim and does not
  distinguish musl from glibc.

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

## Verification coverage limits

These capabilities are implemented, but the available environment does not
contain the live counterpart needed for end-to-end verification.

### V1. No live Sonarr or Radarr instance

Provider behavior was exercised against the committed mock fixture
(`scripts/mock-arr-fixture.cs` plus `scripts/mock-arr-fixtures/`) and the
ADR-013 provider-contract fixtures (`ProviderContractFixtureTests`), not a real
Sonarr `3.x`-`4.x` / Radarr `3.x`-`6.x` release instance.

- Evidence: tasks 7.1, 7.3, 7.8 worker reports.
- Consequence: real-release payload-shape variance beyond the committed
  fixtures is untested. Compatibility is behavioural per ADR-013 (no
  version-number gate), so the contract path is covered but not each release's
  complete field set.

### V2. ADR-010 non-canonical cross-runtime comparison is unselected and unrun

The ADR-010 tolerant cross-runtime comparison requires a second explicitly
selected non-canonical Linux runtime and its golden set under
`tests/ArrTags.Tests/Goldens/non-canonical/`. No such runtime has been selected
or recorded, and the comparison is skipped by
`NonCanonicalRuntimeFactAttribute` while the golden set is absent.

- Evidence: tasks 4.11 and 7.1; `PLANS.md` Phase 4 status;
  `docs/implementation-readiness.md`.
- Consequence: the native/tolerance half of the ADR-010 test strategy is
  recorded as an environment limitation, not executed. Same-runtime golden and
  byte-determinism coverage passes.

### V3. Jellyfin Enhanced coexistence is contract-level only

`EnhancedCoexistenceTests` (7/7) asserts the production assembly has no Enhanced
reference, the renderer/publication surface has no spoiler/hidden/suppression
branch, and badge output varies only with existing ArrTags configuration.
Jellyfin Enhanced is not installed on the pinned host, so no live coexistence
was exercised.

- Evidence: task 5.10 and task 7.3/7.8 worker reports; ADR-011.
- Consequence: `GOALS.md` criterion 7 is met only at the contract level. The
  user-facing overlap/placement behavior in a live Enhanced install is
  unverified.

### V4. Live `IProviderManager.SaveImage` read-back is manual, not an automated live test

Task 5.11's in-process standard-route test invokes the real pinned
`ImageController` actions but supplies `DispatchProxy` doubles for
`ILibraryManager`, `IProviderManager`, and `IImageProcessor`; there is no media
library item at that level and no automatable live HTTP round-trip test exists
in the suite.

- Evidence: `tests/ArrTags.Tests/JellyfinImageResponseTests.cs`;
  `docs/research/jellyfin-12-architecture.md`; task 5.11.
- Partial mitigation: task 7.8's manual live run published through the **real**
  host `IProviderManager.SaveImage` with no double, and
  `GET /Items/{id}/Images/Primary` served bytes whose SHA-256 equalled the
  persisted `PublishedArtworkState.ActiveImageIdentity`. The remaining gap is
  that this manual confirmation is not covered by an automated live test.

### V5. Live `Plugin.OnUninstalling` drain is not covered by an automated live test

Exercising the uninstall hook live requires an authenticated admin uninstall.
The task 7.2/7.7 live runs could not perform one because the pinned host's
startup wizard was incomplete, so `DELETE /Plugins/<guid>/<version>` returned
HTTP `401` and the live verification removed the plugin folder manually, which
does not invoke `OnUninstalling`. The `0.1.0` release audit did exercise the
hook: with the wizard complete, an authenticated `DELETE
/Plugins/40322d52-5680-449f-b33e-e01836ee2f46/0.1.0.0` returned `204`, the
versioned install folder was removed, the relocated state root was removed by
the completed-drain cleanup, and the served movie image was restored to the
original source poster.

- Evidence: tasks 7.2 and 7.7 worker reports; release-review finding RR-14
  (`docs/implementation/final-review/release-review.json`); the release audit's
  authenticated live uninstall.
- Coverage: the drain and its state-root cleanup remain the responsibility of
  the automated in-process tests `LifecycleFoundationTests`,
  `ArtworkLifecycleTests`, and `PluginStateLocationTests` (completed-drain
  removal and incomplete-drain retention). The live uninstall is manual
  evidence, not an automated live test.

### V6. Host-guarded facts are skipped without the pinned host

The host route/response, plugin-discovery, dashboard/configuration, native-Skia,
and package-content facts are environment-guarded and skip unless their guard is
enabled: `ARRTAGS_JELLYFIN_HOST_DIR` enables the 19 pinned-host route/response,
plugin-discovery, dashboard, and configuration round-trip facts (it does not
enable the native-Skia facts); `ARRTAGS_SKIA_COMPAT=1` enables the 43
native-render/decode facts (39 `SkiaNativeFact` + 4 `SkiaNativeTheory`);
`ARRTAGS_NONCANONICAL_GOLDENS` enables the 1 non-canonical cross-runtime
comparison; and a produced `artifacts/ArrTags_1.1.0.0.zip` is required for the 3
`PackagedPluginFact` package-content facts.

The current `1.1.0.0` release matrix is: default suite with the archive present
Failed 0, Passed 1,494, Skipped 63, Total 1,557 (the 63 skips are the 19 host +
43 native + 1 non-canonical facts), or Failed 0, Passed 1,491, Skipped 66, Total
1,557 from a clean checkout that tests before packaging; host-guarded suite (with
`ARRTAGS_JELLYFIN_HOST_DIR` set and the archive present) Failed 0, Passed 1,513,
Skipped 44, Total 1,557, where the host directory unskips only the 19 host facts
and the 44 remaining are the 43 native facts plus the 1 non-canonical comparison;
and with the pinned native SkiaSharp runtime forced (`ARRTAGS_SKIA_COMPAT=1`,
with the pinned native dependency directory on `LD_LIBRARY_PATH`, host directory
unset) Failed 0, Passed 1,575, Skipped 20, Total 1,595, where the 43 native facts
unskip (the Total grows from 1,557 to 1,595 as the enabled native theories
expand) and the remaining 20 are the 19 host facts plus the 1 non-canonical
comparison. The previous `1.0.1.0` release matrix (default 1,228 passed / 60
skipped / 1,288 total; host-guarded 1,244 passed / 44 skipped / 1,288 total) is
historical. Task 14.2 recorded this v1.1 matrix (test-quality review finding
TQ-8).

- Evidence: tasks 7.5/7.8 worker reports; task 14.2 worker report;
  `docs/release/build-and-release.md`.
- Consequence: the default `./build.sh test` run does not exercise the real host
  discovery, route, or native-render paths.

## Packaging and release limitations

### P1. Byte-reproducibility depends on the pinned toolchain

Byte-identity of `artifacts/ArrTags_1.1.0.0.zip` (594,931 bytes, SHA-256
`85730fe7b3fb8b03c86a87228dc1043d42844b372a9493d4caf5bba4a7e836e1`, MD5
`547beb2f7d83cd256d3a3ce7bb7e7620`, the current `1.1.0.0` identity) is
demonstrated with the pinned toolchain (`global.json` pins SDK `10.0.0` with
`latestMinor`; validated with `10.0.401`). A different .NET SDK version could in
principle change compiler or deflate output.

- Evidence: task 7.5 worker/reviewer reports and
  `docs/release/build-and-release.md`.
- Also: no automated test guards byte-reproducibility. `PluginPackagingTests`
  asserts package contents (required entries, no duplicate SkiaSharp runtime),
  not archive byte-identity; reproducibility is verified by re-running
  `./build.sh package` and comparing SHA-256.
- Verification note: the pre-fix 567,856-byte artifact (`bd10b9b6…`) was itself
  installed and exercised end-to-end on the pinned Jellyfin `12.0.0` host by the
  Phase 7 review (badge published, 0 `[FTL]`, served bytes matching the persisted
  `ActiveImageIdentity`), which closed the task 7.6 reviewer finding 7.6-R1 for
  that artifact. The post-SEC-1 `0.1.0.0` artifact was stable across repeated
  `./build.sh package` runs and was live-verified on the same host for the SEC-1
  boundary (`docs/changelog.md`); the re-run release review re-verifies the
  end-to-end publication on the new artifact. Phase 8 task 8.3 then bumped the
  version to `1.0.1.0`, changing only version/release metadata; the `1.0.1.0`
  artifact was byte-stable across repeated `./build.sh package` runs (the Phase 8
  task 8.4 and 8.5 runs produced the identical SHA-256). Phase 14 task 14.2 then
  rebuilt at `1.1.0.0`; the current `1.1.0.0` artifact is byte-stable across
  repeated clean builds (the two task 14.2 runs — the second after wiping
  `bin`/`obj`/`artifacts` and re-restoring — produced the identical SHA-256
  `85730fe7…`).

### P2. The shipped assembly has reduced debug metadata for reproducibility

`Directory.Build.props` sets `IncludeSourceRevisionInInformationalVersion=false`
and `SuppressImplicitGitSourceLink=true`, and `src/ArrTags/ArrTags.csproj` maps
the source path via `PathMap`, so a build from a git working tree matches a
`.git`-less clean export. As a result the shipped `ArrTags.dll` has
`AssemblyInformationalVersion` `1.1.0.0` with no commit suffix and the PDB
carries no SourceLink mapping.

- Evidence: task 7.5 worker report (reviewer finding 7.5-F3) and
  `docs/release/build-and-release.md`.
- Consequence: `AssemblyVersion`/`FileVersion`/`ProductVersion` remain
  `1.1.0.0` and no runtime behavior depends on the suppressed metadata, but
  source-level debugging of a released assembly is harder. This is a deliberate
  reproducibility trade-off, not an oversight.

### P3. `PackagePlugin=true` stages but does not write the archive

`dotnet build src/ArrTags/ArrTags.csproj -p:PackagePlugin=true` now only stages
the release files into `artifacts/staging`; it does not write
`artifacts/ArrTags_<version>.zip`. `./build.sh package` is the documented entry
point that invokes `scripts/pack-release.cs` to write the archive.

- Evidence: task 7.5 worker report; `docs/release/build-and-release.md`.
- Consequence: an external caller that invoked the MSBuild flag directly must
  run `./build.sh package` (or the packer) to produce a release archive.

### P4. SkiaSharp license notices ship although SkiaSharp is no longer redistributed

The package still contains `licenses/SkiaSharp-LICENSE.txt` and
`licenses/SkiaSharp-THIRD-PARTY-NOTICES.txt` even though it no longer ships
`SkiaSharp.dll` or `libSkiaSharp.so`.

- Evidence: task 7.8 worker report; ADR-015.
- This is deliberate: the plugin still compiles against the pinned SkiaSharp and
  retains the bundled DejaVu font notices; the notices are kept to avoid
  dropping attribution.

### P5. Pre-release orphaned state and uninstall drain behavior

There are no released V1 installs, so no migration is performed. An unreleased
developer install may still have an orphaned `PluginsPath/ArrTags` data folder;
while it exists it would trigger the discovery collision that ADR-014 resolves,
so it must be deleted once. A normal V1 install writes state to
`ProgramDataPath/ArrTags` and never creates that folder.

- Evidence: task 7.7 worker report; ADR-014.
- Separately, an incomplete or cancelled uninstall drain retains the relocated
  state root by design (so unresolved artwork can be reconciled); an operator
  must remove it or a reinstall must reconcile it. The completed-drain path
  removes it.

### P6. The `v1.1.0` release publication, manifest push, and asset upload are pending

Phase 14 prepares the `1.1.0.0` release, but the publication is not yet
performed: task 14.2 regenerated the `manifest.json` entry in the working tree,
and task 14.5 commits the manifest and creates the annotated tag `v1.1.0`; the
push, the GitHub release, and the `ArrTags_1.1.0.0.zip` asset upload all remain
the user's manual step. The public repository `benssson/ArrTags` therefore does
not serve a manifest that lists `1.1.0.0`, so the standard Jellyfin plugin-catalog
install cannot resolve the `1.1.0.0` release until the user runs the publish
step. The earlier `1.0.1.0` preparation (Phase 8 tasks 8.1-8.6) is Phase 8
history and is recorded in `docs/changelog.md`.

- Evidence: task 14.2 worker report; for the historical `1.0.1.0` state, task 8.6
  worker/reviewer reports (`docs/implementation/8.6/`).
- Consequence: the current release is prepared but deliberately left unpublished;
  the user must run `scripts/publish-release.sh` (push the prepared commit/tag and
  create the GitHub release with the asset upload) or the equivalent steps.
  Until then no catalog-install or download-from-Releases claim holds.

## Decision-record notes

### D1. Webhook `401` response body versus ADR-012 wording

Live requests to `POST /ArrTags/Webhook/{Sonarr,Radarr}` return `401` with an
ASP.NET Core `[ApiController]` framework `application/problem+json`
ProblemDetails body containing only `type`, `title`, `status`, and `traceId`.
ADR-012 says every authentication failure returns the same bounded `401` "with
no body". No secret, header, body, or configuration detail is reflected, and
task 7.4 describes the actual behavior accurately.

- Evidence: task 7.4 reviewer finding 7.4-R2.
- Disposition: recorded here as a minor documentation/behavior gap for a future
  ADR-012 clarification. The ADR-012 decision text is intentionally left
  unchanged by task 7.6; nothing is presented as available that is not.

### D2. Optional hardening not done: `ArtworkSubjectGate` has no idle eviction

`ArtworkSubjectGate.Gates` is a process-local static
`ConcurrentDictionary<string, SemaphoreSlim>` keyed by Jellyfin item id plus
image surface, with no `TryRemove`/`Clear`/LRU bound. One semaphore is retained
per distinct `(item, surface)` ever processed for the process lifetime.

- Evidence: task 7.4 reviewer finding 7.4-R1.
- Disposition: optional future hardening (for example drop idle entries on the
  lifecycle drain or an LRU bound). Growth is not attacker-controllable, is
  bounded in practice by the number of distinct Jellyfin library items (which
  Jellyfin itself holds in memory), and is not a log, diagnostics, or
  persisted-state path, so it does not change the task 7.4 negative result.

## Release-review accepted limitations

The `0.1.0` release audit
(`docs/implementation/final-review/release-review.json`) returned
`SHIP_WITH_ACCEPTED_LIMITATIONS`. The following accepted items are recorded here
so that they sit alongside the other V1 limitations.

### RR-1. Phases 1-3 have no independent phase-review records

The per-task report-persistence workflow starts at `docs/implementation/4.6`,
and the per-phase `phase-review.json` records start at
`docs/implementation/phase-4/`; `docs/implementation/phase-1/`, `phase-2/`, and
`phase-3/` do not exist and contain no phase-review record, and there are no
per-task worker/reviewer reports for phases 1-3. Their Gate 1-3 claims therefore
rest on the `docs/changelog.md` phase sections, the `PLANS.md`
acceptance-criteria checkboxes, the tags `v0.1.0-phase1`..`3`, and the
accumulated suite counts, none of which was independently audited at the time.

- Evidence: release-review finding RR-1
  (`docs/implementation/final-review/release-review.json`); absence of
  `docs/implementation/phase-1..3/` review files; the release audit's
  independent clean-export suite and live end-to-end run.
- Consequence: a process/evidence gap, not a demonstrated correctness defect.
  The integrated behaviour those phases produce (plugin load, configuration
  boundary, provider reads, matching, state boundary) is exercised by the later
  phases, the full test suite, and the release audit's independent live run.

### RR-15. Mock fixture API keys appear in tracked documentation

The mock fixture API keys `radarrkey123`/`sonarrkey456` (and the documentation
webhook example) appear in tracked files, including
`docs/testing/jellyfin-12-musl-test-host.md`.

- Evidence: release-review finding RR-15
  (`docs/implementation/final-review/release-review.json`);
  `docs/testing/jellyfin-12-musl-test-host.md`; `scripts/mock-arr-fixture.cs`
  logs only whether a key is present/valid.
- Consequence: none. These are deliberate local mock-server test values, never
  production credentials, and the mock fixture never logs key values.

## Security-review accepted limitations

The `0.1.0` security audit
(`docs/implementation/final-review/security-review.json`; attempt 1 preserved at
`security-review.attempt-1.json`) returned `APPROVED` with no open BLOCKER, HIGH,
or MEDIUM. SEC-1 (MEDIUM) is **not** an accepted limitation: it was fixed by task
`release-0.1.0-sec1` and independently reproduced live (uniform `401` for every
content type before any body read; the configured payload bound effective for
authenticated JSON). The residual findings below are accepted and recorded here
alongside the other V1 limitations.

### SEC-2. State-root resolution falls back to a shared temp path (LOW, open)

When `IPluginManager` is unavailable or the loaded plugin instance cannot be
found, `CreateStateRepository` roots the entire authoritative plugin state at
`Path.Combine(Path.GetTempPath(), "ArrTags")` — a predictable, shared location
where another local user could pre-create or observe a same-named directory. It
is not triggered on the pinned host (the plugin is found and `DataFolderPath` is
`ProgramDataPath/ArrTags`) and is not remotely reachable.

- Evidence: security-review finding SEC-2
  (`docs/implementation/final-review/security-review.json`);
  `src/ArrTags/PluginLifecycle/ArrTagsServiceRegistrator.cs:259-261` (the line
  reference was updated by the v1.1 release security review, task 14.4, because
  the file grew in v1.1; the finding itself is unchanged).
- Consequence: if the fallback ever engaged, authoritative state would live under
  a shared temp path. Accepted for V1; optional post-V1 hardening is to fail
  closed or use a host-private directory instead.

### SEC-3. The state-envelope integrity hash does not cover envelope metadata (INFORMATIONAL, noted)

The versioned state envelope's SHA-256 (`PayloadSha256`) covers only
`PayloadJson`; `SchemaVersion`, `Kind`, `RecordId`, `Authority`, `Terminal`, and
`UpdatedUtc` are not covered, so retention trusts the unauthenticated
`Terminal`/`UpdatedUtc` fields.

- Evidence: security-review finding SEC-3;
  `src/ArrTags/State/StateEnvelopeCodec.cs:83-113`; `StateRetention.cs:154-165`.
- Consequence: the hash detects only `PayloadJson` corruption. The envelope reader
  separately validates only `SchemaVersion`; `Kind`, `RecordId`, and `Authority`
  are not validated by the envelope, and `Terminal`/`UpdatedUtc` are trusted by
  retention (`StateRetention.IsTerminalAndExpired`). Metadata tampering is
  therefore not detected by the hash, which bounds `StateEnvelopeCodec` to a
  corruption detector rather than a tamper MAC. This is not an escalation because
  local write access to the state root is already the administrative boundary.
  Optional post-V1 hardening is to cover the metadata in the hash.

### SEC-4. Authentication error bodies carry framework `application/problem+json` detail (INFORMATIONAL, noted)

Every webhook failure returns an ASP.NET Core `[ApiController]`
`application/problem+json` ProblemDetails body (`type`, `title`, `status`,
`traceId`), which is the already-recorded `D1` gap versus ADR-012's "no body"
wording; the SEC-1 fix narrowed it by removing the pre-auth framework form-limit
detail. No secret, header, candidate, body, route, plugin, or item-existence
detail is reflected.

- Evidence: security-review finding SEC-4; this file's `D1` entry.
- Consequence: the pre-auth framework detail is gone, but the residual generic
  ProblemDetails body remains the already-recorded `D1` body-versus-ADR-012
  wording gap. Accepted for V1; optional post-V1 hardening is to normalise it.

### SEC-5. Secrets persist at rest only in Jellyfin's plugin configuration XML, and ArrTags logging is bounded and redacted (INFORMATIONAL, noted)

The Sonarr/Radarr API keys and the inbound webhook shared secret are persisted
only in Jellyfin's plugin configuration XML and returned by Jellyfin's
authenticated, elevation-gated plugin-configuration API to administrators. This
is the accepted ADR-005 single-source-of-truth design.

Since v1.1 task 10.2 ArrTags logs through the host's
`Microsoft.Extensions.Logging` `ILogger<T>`/`ILoggerFactory` pipeline
(ADR-020 clause 1) at the bounded, validated, secret-free `LogVerbosity` setting
(`Off`/`Error`/`Warning`/`Information`/`Debug`/`Trace`, default `Warning`), read
from the current configuration snapshot and applied without a restart (ADR-020
clauses 2 and 3). Every log call emits only bounded, already-redacted values per
the ADR-020 clause 4 allowlist (`ArrProviderError` code/retryability/message, the
non-secret connection identity, configuration version, bounded reason codes and
enums, item/record identifiers, and counts); it never emits an API key, the
webhook secret, a `SecretLease` value, an `X-Api-Key`/`X-ArrTags-Webhook-Secret`
header, a raw request/response body, a full provider payload, or the mutable
`PluginConfiguration`. The emitted data shape is identical at every verbosity
level, so raising verbosity cannot expand a redacted value into a secret-bearing
one. Log volume is bounded by the code-owned `LogThrottle` (ADR-020 clause 6;
`docs/architecture.md` section 12), and verbosity is not output-affecting
(ADR-020 clause 5). The logging path is covered by a dedicated security review
(task 10.3; report at `docs/implementation/10.3/security-review.json`).

The two plugin-initiated outbound surfaces are consistent: the host logging
pipeline (above) and the bounded, secret-free configuration-rejection
activity-log entry recorded in SEC-9 (ADR-021). Neither emits a secret or a
configuration candidate value, and neither expands with verbosity. SEC-9 remains
the only plugin-initiated administrator-visible *notification*; logging is a
diagnostic mechanism (ADR-020 clause 7).

- Evidence: security-review finding SEC-5 (rewritten by v1.1 task 10.3);
  `docs/decisions.md` ADR-005, ADR-020, and ADR-021; `src/ArrTags/Logging/`;
  `docs/architecture.md` sections 6, 11, and 12; this file's SEC-9.
- Consequence: secrets remain confined to Jellyfin's plugin configuration XML at
  rest, and the diagnostic logging path is a bounded, redacted, reviewed surface
  that cannot leak a secret. The SEC-9 activity-log surface is likewise bounded
  and secret-free.

### SEC-6. The `ArtworkSubjectGate` process-lifetime bound (INFORMATIONAL, noted)

`ArtworkSubjectGate.Gates` retains one `SemaphoreSlim` per distinct
`(item, surface)` ever processed for the process lifetime, with no idle
eviction. This is the already-recorded `D2` item: not attacker-controllable and
bounded in practice by the host's distinct library items.

- Evidence: security-review finding SEC-6; this file's `D2` entry.
- Consequence: process-lifetime growth bounded in practice by the host's distinct
  library items and not attacker-controllable; accepted as the `D2` item, with
  optional post-V1 idle eviction as hardening.

### SEC-7. A declared non-JSON content type is now rejected with a bounded 400 (INFORMATIONAL, noted)

Post-SEC-1 behaviour change: after authentication, a request that declares a
non-JSON content type is rejected with a bounded `400` before any body read, so a
syntactically valid JSON body sent with an unrelated content type (for example
`text/plain`) is likewise rejected. An absent content type is still tolerated as
JSON (`202`/`400`/`413` as appropriate), and `application/json`, `text/json`, and
`+json` structured suffixes are accepted. Sonarr and Radarr send
`application/json`, so no real delivery is affected; the choice of `400` over
`415` is deliberate (matching the existing ADR-012 payload-rejection status).

- Evidence: security-review finding SEC-7;
  `src/ArrTags/Webhooks/WebhookAuthenticationFilter.cs`;
  `docs/implementation/release-0.1.0-sec1/worker-report.json`.
- Consequence: an authenticated request declaring a non-JSON content type is
  rejected with a bounded `400`; Sonarr and Radarr send `application/json`, so no
  real delivery is affected. Accepted for V1.

### SEC-8. No per-client authentication throttling on the anonymous webhook route (INFORMATIONAL, noted)

The anonymous webhook route applies no ArrTags-level per-client throttling to
authentication attempts (ADR-012's "rate limiting" is bounded intake/coalescing
of accepted deliveries, not auth throttling). The exposure is mitigated by the
high-entropy administrator-configured shared secret, the 1024-character
candidate bound, the constant-time compare, and the uniform fail-closed `401`.

- Evidence: security-review finding SEC-8; `docs/decisions.md` ADR-012.
- Consequence: optional post-V1 hardening only (for example per-source throttling
  or reverse-proxy rate limiting).

### SEC-9. Configuration-rejection activity-log surfacing is a new bounded, secret-free administrator-visible surface (INFORMATIONAL, noted)

v1.1 task 9.3 (ADR-021) adds one plugin-initiated outbound
administrator-visible surface: on a rejected configuration save the plugin
writes exactly one activity-log entry through the host `IActivityManager`. The
entry has a fixed name and type (`ArrTagsConfigurationRejected`), an empty user
id, `Warning` severity, and an overview built only from bounded, secret-free
validation reasons (at most eight, each and every field truncated to the
`ActivityLog` column bounds). It contains no API key, base URL, webhook secret,
color value, numeric candidate value, request body, or header. The write is a
bounded synchronous wait and is fully contained: it never throws into the host
and a write failure does not affect the rejection or the retained last-valid
configuration. The host activity-log read and its websocket are elevation-gated
to administrators. The coupling to `Jellyfin.Database.Implementations` is
isolated behind the plugin-owned `IConfigurationRejectionNotifier` boundary. A
valid save writes no entry.

Volume and spam (SEC-9.3-02): each rejected save writes one entry, so an
administrator who repeatedly saves an invalid configuration produces repeated
identical entries; the trigger is administrator-only (the elevation-gated
`PluginsController` POST), and consecutive-duplicate suppression is deliberately
not implemented (ADR-021 clause 6). The reason text is sanitized at the boundary
(control characters stripped, whitespace collapsed, count capped at eight, each
reason and every field truncated to the `ActivityLog` bounds) so a reason cannot
inject markup, a line break, or a malformed value (SEC-9.3-03). The only
production caller is `Plugin.TryNotifyRejected`, which passes the validator's
secret-free messages; the interface documents that invariant. The rejection is
atomic with respect to persistence (SEC-9.3-01): the candidate is validated
before the host base implementation persists anything, an invalid candidate is
never written, and the save sequence is serialized so concurrent saves cannot
leave the running snapshot, the in-memory configuration, and the persisted file
divergent.

- Evidence: `docs/decisions.md` ADR-021;
  `docs/research/jellyfin-expert/configuration-save-failure-surfacing.json`;
  `src/ArrTags/Configuration/IConfigurationRejectionNotifier.cs`,
  `src/ArrTags/PluginLifecycle/JellyfinConfigurationRejectionNotifier.cs`;
  `tests/ArrTags.Tests/ConfigurationRejectionNotifierTests.cs`,
  `tests/ArrTags.Tests/ConfigurationActivationTests.cs`.
- Consequence: a new, reviewed, bounded, secret-free outbound surface; the
  activity-log entry is the only plugin-initiated administrator-visible
  notification and it carries no candidate value or secret. SEC-5 records this
  entry's scope alongside the v1.1 task 10.3 logging redaction contract.

**v1.1 release-review accepted limitations.** The v1.1 release audit
(`docs/implementation/14.4/security-review.json`) returned
`PASS_WITH_FINDINGS` with no open BLOCKER or HIGH. The items below are the
audit's accepted residuals (SEC-10 to SEC-12) alongside the carried
`0.1.0`/task-level items above, which the release audit re-verified as
unchanged.

### SEC-10. Configured provider API keys and the webhook shared secret are not length-bounded (LOW, accepted)

Configuration validation bounds every operational limit and renderer value, but
it does not bound the length of `ArrConnectionConfiguration.ApiKey` or
`PluginConfiguration.WebhookSecret`. The webhook authentication path is
anonymous, and `SecretLease.Matches` allocates the expected and candidate UTF-8
byte arrays before the constant-time comparison, so each request to
`POST /ArrTags/Webhook/{Sonarr,Radarr}` allocates memory proportional to the
configured secret length, even though the untrusted candidate is already bounded
to 1024 characters (so a secret longer than 1024 characters can never
authenticate). There is no default secret, and an unconfigured webhook fails
closed with `401` before any comparison.

- Evidence: release security-review finding SEC-14.4-01
  (`docs/implementation/14.4/security-review.json`);
  `src/ArrTags/Configuration/PluginConfiguration.cs:42` and
  `src/ArrTags/Configuration/ArrConnectionConfiguration.cs:22` (unbounded string
  properties); `src/ArrTags/Configuration/PluginConfigurationValidator.cs:40-68`
  (no length check); `src/ArrTags/Webhooks/WebhookAuthentication.cs:71-92`
  (candidate bound 1024); `src/ArrTags/Secrets/SecretLease.cs:80-82` (byte arrays
  allocated per attempt).
- Consequence: accepted for v1.1. This requires an administrator to save an
  implausibly long secret and is a memory-amplification/availability hardening
  gap, not a credential disclosure. Post-V1 hardening is to bound the configured
  secret lengths in `PluginConfigurationValidator` (at most the 1024-character
  candidate bound for the webhook secret) and/or compare lengths before
  allocating in `SecretLease.Matches`.

### SEC-11. Library-scope entries are not count/length-bounded or validated as library identifiers (LOW, accepted)

`PluginConfigurationValidator.ValidateLibraryScope` rejects only blank and
duplicate entries, so a saved configuration can carry an unbounded number of
arbitrarily long `EnabledLibraries` entries (bounded in practice only by the host
request-body and configuration-file limits). Reconciliation and webhook
resolution parse each entry with `Guid.TryParse` per item, and an entry that is
not a library GUID is silently ignored, so a non-empty scope made only of
unparseable entries fails the scope check closed for every item and silently
stops badge work instead of surfacing a validation error. The trigger is the
elevation-gated `PluginsController` save (or direct XML editing), not a remote
input.

- Evidence: release security-review finding SEC-14.4-02
  (`docs/implementation/14.4/security-review.json`);
  `src/ArrTags/Configuration/PluginConfigurationValidator.cs:77-94`;
  `src/ArrTags/Media/MediaEligibility.cs:29-48`;
  `src/ArrTags/Configuration/PluginConfigurationSnapshot.cs:183-187`; the
  settings page submits only the host's real library ids
  (`src/ArrTags/Configuration/config.html`, `enabledLibrary` checkboxes).
- Consequence: accepted for v1.1. Post-V1 hardening is to bound the entry count
  and per-entry length and validate the GUID format, surfacing a failure through
  the existing ADR-021 rejection path.

### SEC-12. Configured base-URL user information is not rejected (INFORMATIONAL, accepted)

Configuration validation accepts any absolute `http`/`https` URL, including one
with embedded user information (for example
`https://user:password@sonarr.example:8989`). ArrTags' own connection identity
excludes user information and the API key, the plugin never logs a request URI,
and credentials travel only in the `X-Api-Key` header, so the plugin's log call
sites cannot emit the value; however, framework-level `HttpClient` logging (host
category, outside the plugin `LogVerbosity` gate) could include the request URI.
This is pre-existing (recorded by the task 10.3 security review as
SEC-10.3-4) and requires an administrator to embed credentials in the base URL.

- Evidence: release security-review finding SEC-14.4-04
  (`docs/implementation/14.4/security-review.json`);
  `src/ArrTags/Configuration/PluginConfigurationValidator.cs:70-75`;
  `src/ArrTags/Providers/ArrConnectionId.cs:74-98` (identity excludes user
  information); `src/ArrTags/Secrets/SecretLease.cs:56-57` (`X-Api-Key` header).
- Consequence: accepted for v1.1. Post-V1 hardening is to reject a base URL whose
  user-information component is non-empty.

## Deliberate V1 scope exclusions

These are recorded in `GOALS.md` (Initially out of scope), ADR-006, ADR-008, and
ADR-009, and remain excluded unless `GOALS.md` is deliberately changed: Jellyfin
versions before 12; modifying original media files; writing or managing
Sonarr/Radarr metadata; a general poster-management system; user-specific
badges; non-poster artwork; and unapproved external services. Configured
Jellyfin-to-Arr path fallback is deferred out of V1 by ADR-008; V1 badge
surfaces are unindexed Movie and Episode `Primary` posters only (ADR-006 /
ADR-009); Series/Season aggregation remains post-V1.
