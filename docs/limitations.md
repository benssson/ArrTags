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

### F2. A saved configuration change is activated at runtime but existing posters are not promptly re-rendered

`ConfigurationSnapshotService.TryReplace` is now wired to Jellyfin's
configuration-update mechanism (task 9.3), so a saved change is activated
without a host restart. The remaining gap is that a saved change does not
promptly re-render existing posters: a work item whose `ConfigurationVersion` is
older than the current snapshot is skipped, so existing posters update only on
the next library event, webhook, post-scan, or scheduled run until the bounded,
non-blocking post-save reconciliation trigger (task 9.4) lands.

- Evidence: task 7.7, 7.3, and 7.8 worker reports; `PLANS.md` Phase 9 tasks 9.3
  and 9.4; `docs/implementation-readiness.md`.
- Consequence: before task 9.3, a saved webhook-secret, provider enable/disable,
  badge/selector, or DG-6 limit change was **not observed until the process
  restarted**, and the live tasks 7.3 and 7.8 configured the plugin by writing
  `data/plugins/configurations/ArrTags.xml` and restarting. That restart
  consequence is resolved: the bounded work queue, provider/render concurrency
  limiters, freshness window, retention interval, badge definitions, and the
  renderer output policy resolve their values from the current snapshot per
  operation, so the replaced snapshot is observed by subsequent work. The
  remaining consequence is re-render promptness only: an already-published
  poster is not re-rendered by the save itself until task 9.4 adds the trigger.
- Current state (v1.1): task 9.2 added the dashboard settings page, which reads
  and saves the configuration through Jellyfin's elevation-gated
  `PluginsController` path, so an operator no longer has to edit
  `ArrTags.xml` by hand. Task 9.3 overrides `Plugin.UpdateConfiguration` to
  validate the candidate before the base implementation persists it: a valid
  candidate is persisted and activated at runtime, while an invalid candidate is
  rejected before persistence (it is never written to `ArrTags.xml`) and the last
  valid public snapshot and private secret generation remain active and
  persisted. The post-save reconciliation trigger (task 9.4) and the final
  Goal A verification that records F2 as resolved (task 9.5) remain.

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

The host route/response, plugin-discovery, native-Skia, and package-content
facts skip unless `ARRTAGS_JELLYFIN_HOST_DIR` points at the pinned host (and,
for package content, `./build.sh package` has been run). The default suite is
1,228 passed / 60 skipped / 1,288 total; the host-guarded suite is 1,244 passed
/ 44 skipped / 1,288 total.

- Evidence: tasks 7.5/7.8 worker reports; `docs/release/build-and-release.md`.
- Consequence: the default `./build.sh test` run does not exercise the real host
  discovery, route, or native-render paths.

## Packaging and release limitations

### P1. Byte-reproducibility depends on the pinned toolchain

Byte-identity of `artifacts/ArrTags_1.0.1.0.zip` (568,248 bytes, SHA-256
`de4c34841d77b5ff74b6bc9edeb515a4c5fcc5a9b09d7d24a2da5d64d31b4b8c`, MD5
`16baa5a7324b8e14fdb113d84b944d09`, the current `1.0.1.0` identity) is
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
  version to `1.0.1.0`, changing only version/release metadata; the current
  `1.0.1.0` artifact is byte-stable across repeated `./build.sh package` runs
  (the Phase 8 task 8.4 and 8.5 runs produced the identical SHA-256).

### P2. The shipped assembly has reduced debug metadata for reproducibility

`Directory.Build.props` sets `IncludeSourceRevisionInInformationalVersion=false`
and `SuppressImplicitGitSourceLink=true`, and `src/ArrTags/ArrTags.csproj` maps
the source path via `PathMap`, so a build from a git working tree matches a
`.git`-less clean export. As a result the shipped `ArrTags.dll` has
`AssemblyInformationalVersion` `1.0.1.0` with no commit suffix and the PDB
carries no SourceLink mapping.

- Evidence: task 7.5 worker report (reviewer finding 7.5-F3) and
  `docs/release/build-and-release.md`.
- Consequence: `AssemblyVersion`/`FileVersion`/`ProductVersion` remain
  `1.0.1.0` and no runtime behavior depends on the suppressed metadata, but
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

### P6. The `v1.0.1` release publication, manifest push, and asset upload are pending

Phase 8 tasks 8.1-8.6 complete the `1.0.1.0` release preparation, but the release
is not yet published: the annotated tag `v1.0.1` and the `manifest.json` commit
`8cba85b` exist only in the local repository and have not been pushed, no GitHub
release exists, and the `ArrTags_1.0.1.0.zip` asset has not been uploaded. The
public repository `benssson/ArrTags` therefore does not yet serve a manifest that
lists `1.0.1.0`, so the standard Jellyfin plugin-catalog install cannot resolve
ArrTags until the user runs the publish step.

- Evidence: task 8.6 worker/reviewer reports (`docs/implementation/8.6/`);
  `git rev-parse origin/main` = `09596e0` while the local `v1.0.1` tag
  dereferences to `8cba85b`.
- Consequence: the release is prepared but deliberately left unpublished; the
  user must run `scripts/publish-release.sh` (push the prepared commit/tag and
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
  `src/ArrTags/PluginLifecycle/ArrTagsServiceRegistrator.cs:178-180`.
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

### SEC-5. Secrets are stored at rest only in Jellyfin's plugin configuration XML (INFORMATIONAL, noted)

The Sonarr/Radarr API keys and the inbound webhook shared secret are persisted
only in Jellyfin's plugin configuration XML and returned by Jellyfin's
authenticated, elevation-gated plugin-configuration API to administrators. This
is the accepted ADR-005 single-source-of-truth design; the plugin has no logging
call sites, so no plugin log path can leak a secret. As of v1.1 task 9.3 the
plugin still has no diagnostic log (`ILogger`/Serilog) call sites; the only
plugin-initiated outbound administrator-visible surface is the bounded,
secret-free configuration-rejection activity-log entry recorded in SEC-9
(ADR-021). This is a scoping note only; the full SEC-5 rewrite that reconciles
SEC-9 is Phase 10 task 10.3.

- Evidence: security-review finding SEC-5; `docs/decisions.md` ADR-005 and
  ADR-021; this file's SEC-9.
- Consequence: none. This is the accepted ADR-005 design, and the plugin has no
  log path that could leak a secret. The SEC-9 activity-log surface is bounded
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
  notification and it carries no candidate value or secret. SEC-5 is scoped by
  this entry and is reconciled fully by Phase 10 task 10.3.

## Deliberate V1 scope exclusions

These are recorded in `GOALS.md` (Initially out of scope), ADR-006, ADR-008, and
ADR-009, and remain excluded unless `GOALS.md` is deliberately changed: Jellyfin
versions before 12; modifying original media files; writing or managing
Sonarr/Radarr metadata; a general poster-management system; user-specific
badges; non-poster artwork; and unapproved external services. Configured
Jellyfin-to-Arr path fallback is deferred out of V1 by ADR-008; V1 badge
surfaces are unindexed Movie and Episode `Primary` posters only (ADR-006 /
ADR-009); Series/Season aggregation remains post-V1.
