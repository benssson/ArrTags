# ArrTags known limitations and deferred decisions

This document is the canonical current-state record of what ArrTags V1 does
**not** yet do, or has **not** yet verified, so that no unsupported or
unverified capability is presented as available. It is owned by Phase 7 task
7.6 and consolidates the deferred decisions previously scattered across
`PLANS.md` (Post-V1 Backlog), `docs/implementation-readiness.md`,
`docs/release/build-and-release.md`, and the Phase 6/7 review findings.

It does **not** redefine V1 scope: the in-scope and out-of-scope lists in
`GOALS.md` still govern. Items here are either deliberate V1 exclusions,
implementation-time deferrals, or verification gaps caused by the available
environment. Each item names its evidence (an ADR, a task report under
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
| 6 | Poster updates occur without unnecessary repeated processing | Met for render/publication; partial for provider fetches | No provider inventory/catalogue cache (item F1); render and publication are fingerprint-gated. |
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

### F1. Provider inventory/catalogue cache is not implemented

Every reconciliation work item still re-reads the whole provider library
(`/api/v3/movie`, `/api/v3/series`) and then the per-record file resource
(`/api/v3/moviefile?movieId=`, `/api/v3/episode?…&includeEpisodeFile=true`,
`/api/v3/episodeFile?seriesId=`); there is no shared, short-TTL provider
inventory cache and no provider revision-token fetch skip. Each scheduled,
post-scan, and manual reconciliation enqueues up to `QueueCapacity` work hints
(default 512, ADR-004), and each hint performs its own provider read.

- Evidence: Phase 6 review MEDIUM item; `PLANS.md` Phase 6 acceptance criterion
  1 (accepted as partially met) and Post-V1 Backlog; `docs/architecture.md`
  section 12; task 7.4 review (`LibraryWorkQueue` capacity 512).
- Consequence: on a large library, one full provider-library read occurs per
  work item, so the `GOALS.md` Reliability/Performance goal "avoid unnecessary
  API requests to Sonarr and Radarr" is only partially met for provider
  fetches. Render and publication are fingerprint-gated and are not affected.

### F2. Runtime configuration replacement is not wired to Jellyfin's save path

`ConfigurationSnapshotService.TryReplace` is implemented and validated but is
not connected to Jellyfin's configuration-update mechanism.

- Evidence: task 7.7, 7.3, and 7.8 worker reports; `PLANS.md` Post-V1 Backlog;
  `docs/implementation-readiness.md`.
- Consequence: a saved webhook-secret, provider enable/disable, badge/selector,
  or DG-6 limit change is **not observed until the process restarts**. The live
  tasks 7.3 and 7.8 configured the plugin by writing
  `data/plugins/configurations/ArrTags.xml` and restarting. The bounded work
  queue, provider/render concurrency limiters, freshness window, and retention
  interval already resolve their values from the current snapshot per
  operation, so wiring the replacement is the remaining step.

### F3. No bounded, secret-free metrics/diagnostic-status surface

Queue depth, provider health, matching, cache, rendering, and stale-data
counters exist internally, but there is no bounded, secret-free user-facing or
diagnostic status surface.

- Evidence: `docs/architecture.md` line 1114 (documented as an open limitation
  here); `PLANS.md` Post-V1 Backlog.
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

### V5. Live `Plugin.OnUninstalling` drain is not exercised

Exercising the uninstall hook live requires an authenticated admin uninstall,
but `PluginsController` is `[Authorize(Policy = Policies.RequiresElevation)]`
and the pinned host's startup wizard is incomplete, so `DELETE
/Plugins/<guid>/<version>` returns HTTP `401`. The live uninstall verification
removed the plugin folder manually, which does not invoke `OnUninstalling`.

- Evidence: tasks 7.2 and 7.7 worker reports.
- Coverage: the drain and its state-root cleanup are covered in-process by
  `LifecycleFoundationTests`, `ArtworkLifecycleTests`, and
  `PluginStateLocationTests` (completed-drain removal and incomplete-drain
  retention).

### V6. Host-guarded facts are skipped without the pinned host

The host route/response, plugin-discovery, native-Skia, and package-content
facts skip unless `ARRTAGS_JELLYFIN_HOST_DIR` points at the pinned host (and,
for package content, `./build.sh package` has been run). The default suite is
1,218 passed / 60 skipped / 1,278 total; the host-guarded suite is 1,234 passed
/ 44 skipped / 1,278 total.

- Evidence: tasks 7.5/7.8 worker reports; `docs/release/build-and-release.md`.
- Consequence: the default `./build.sh test` run does not exercise the real host
  discovery, route, or native-render paths.

## Packaging and release limitations

### P1. Byte-reproducibility depends on the pinned toolchain

Byte-identity of `artifacts/ArrTags_0.1.0.0.zip` (567,856 bytes, SHA-256
`bd10b9b6bf5d31049082d27625b18ba127eb6e2860a454fe2d3c35ccebaaee51`) is
demonstrated with the pinned toolchain (`global.json` pins SDK `10.0.0` with
`latestMinor`; validated with `10.0.401`). A different .NET SDK version could in
principle change compiler or deflate output.

- Evidence: task 7.5 worker/reviewer reports and
  `docs/release/build-and-release.md`.
- Also: no automated test guards byte-reproducibility. `PluginPackagingTests`
  asserts package contents (required entries, no duplicate SkiaSharp runtime),
  not archive byte-identity; reproducibility is verified by re-running
  `./build.sh package` and comparing SHA-256.
- Verification note: the current 567,856-byte artifact (`bd10b9b6…`) was itself
  installed and exercised end-to-end on the pinned Jellyfin `12.0.0` host by the
  Phase 7 review (badge published, 0 `[FTL]`, served bytes matching the persisted
  `ActiveImageIdentity`), which closes the task 7.6 reviewer finding 7.6-R1 for
  the final artifact.

### P2. The shipped assembly has reduced debug metadata for reproducibility

`Directory.Build.props` sets `IncludeSourceRevisionInInformationalVersion=false`
and `SuppressImplicitGitSourceLink=true`, and `src/ArrTags/ArrTags.csproj` maps
the source path via `PathMap`, so a build from a git working tree matches a
`.git`-less clean export. As a result the shipped `ArrTags.dll` has
`AssemblyInformationalVersion` `0.1.0.0` with no commit suffix and the PDB
carries no SourceLink mapping.

- Evidence: task 7.5 worker report (reviewer finding 7.5-F3) and
  `docs/release/build-and-release.md`.
- Consequence: `AssemblyVersion`/`FileVersion`/`ProductVersion` remain
  `0.1.0.0` and no runtime behavior depends on the suppressed metadata, but
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

## Deliberate V1 scope exclusions

These are recorded in `GOALS.md` (Initially out of scope), ADR-006, ADR-008, and
ADR-009, and remain excluded unless `GOALS.md` is deliberately changed: Jellyfin
versions before 12; modifying original media files; writing or managing
Sonarr/Radarr metadata; a general poster-management system; user-specific
badges; non-poster artwork; and unapproved external services. Configured
Jellyfin-to-Arr path fallback is deferred out of V1 by ADR-008; V1 badge
surfaces are unindexed Movie and Episode `Primary` posters only (ADR-006 /
ADR-009); Series/Season aggregation remains post-V1.
