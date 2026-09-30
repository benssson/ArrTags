# Accepted limitations and verification/packaging limits

## Accepted functional limitations

### F5. The renderer depends on a host-supplied SkiaSharp with no bundled fallback

V1 compiles against the pinned `SkiaSharp`/`SkiaSharp.NativeAssets.Linux`
`3.119.4` but ships no renderer runtime and takes the managed assembly and
native library from the Jellyfin host (ADR-015, superseding the bundling parts
of ADR-010).

- Evidence: task 7.8 worker report and ADR-015; `docs/architecture/09-persisted-artwork-rendering.md`.
- Consequence: a host that does not provide a compatible SkiaSharp would make
  rendering fail closed (pass-through/preserve current artwork) rather than use
  a bundled copy. Validated only on the pinned Jellyfin `12.0.0`
  `linux-musl-x64` host; the plugin makes no RID-specific claim and does not
  distinguish musl from glibc.
- Disposition: accepted by explicit user decision for v1.2 and reclassified from
  Open to Accepted; a host-supplied SkiaSharp is the accepted runtime model and
  no bundled fallback is planned. Excluded from v1.2 (see
  `docs/planning/v1.2.md` section 10) and matching README's "known shipped
  limitation" framing.

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
  `docs/plan/archive/v1-readiness.md`.
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
comparison; and a produced `artifacts/ArrTags_1.2.0.0.zip` is required for the 3
`PackagedPluginFact` package-content facts.

The current `1.2.0.0` release matrix is: default suite with the archive present
Failed 0, Passed 1,966, Skipped 66, Total 2,032 (the 66 skips are the 20 host +
45 native (41 `SkiaNativeFact` + 4 `SkiaNativeTheory`) + 1 non-canonical facts),
or Failed 0, Passed 1,963, Skipped 69, Total 2,032 from a clean checkout that
tests before packaging; host-guarded suite (with `ARRTAGS_JELLYFIN_HOST_DIR` set
and the archive present) Failed 0, Passed 1,986, Skipped 46, Total 2,032, where
the host directory unskips the 20 host facts and the 46 remaining are the 45
native facts plus the 1 non-canonical comparison; and with the pinned native
SkiaSharp runtime forced (`ARRTAGS_SKIA_COMPAT=1`, with the pinned native
dependency directory on `LD_LIBRARY_PATH`, host directory unset) Failed 0,
Passed 2,053, Skipped 21, Total 2,074, where the 45 native facts unskip (the
Total grows from 2,032 to 2,074 as the enabled native theories expand) and the
remaining 21 are the 20 host facts plus the 1 non-canonical comparison. The
previous `1.1.0.0` release matrix (default 1,494 passed / 63 skipped / 1,557
total; host-guarded 1,513 passed / 44 skipped / 1,557 total; forced-native 1,575
passed / 20 skipped / 1,595 total) and the `1.0.1.0` matrix (default 1,228
passed / 60 skipped / 1,288 total; host-guarded 1,244 passed / 44 skipped / 1,288
total) are historical. Task 21.12 recorded this v1.2 matrix (the prior v1.1
matrix was recorded by task 14.2; test-quality review finding TQ-8).

- Evidence: tasks 7.5/7.8 worker reports; task 14.2 worker report;
  `docs/release/build-and-release.md`.
- Consequence: the default `./build.sh test` run does not exercise the real host
  discovery, route, or native-render paths.

## Packaging and release limitations

### P1. Byte-reproducibility depends on the pinned toolchain

Byte-identity of `artifacts/ArrTags_1.2.0.0.zip` (619,861 bytes, SHA-256
`2a039fc7075f4e4c1a1c785eb0b3757636fc0573da1361ee814cfd4099297268`, MD5
`631fbfa5a4fb58fac5a877eeef761193`, the current corrected `1.2.0.0` release
identity) is demonstrated with the pinned toolchain (`global.json` pins SDK
`10.0.0` with `latestMinor`; validated with `10.0.401`). A different .NET SDK
version could in principle change compiler or deflate output.

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
  boundary (`docs/changelog/00-index.md`); the re-run release review re-verifies the
  end-to-end publication on the new artifact. Phase 8 task 8.3 then bumped the
  version to `1.0.1.0`, changing only version/release metadata; the `1.0.1.0`
  artifact was byte-stable across repeated `./build.sh package` runs (the Phase 8
  task 8.4 and 8.5 runs produced the identical SHA-256). Phase 14 task 14.2 then
  rebuilt at `1.1.0.0`, and that artifact was byte-stable across repeated clean
  builds (the two task 14.2 runs — the second after wiping
  `bin`/`obj`/`artifacts` and re-restoring — produced the identical SHA-256
  `85730fe7…`). The current corrected `1.2.0.0` identity is likewise byte-stable
  across repeated clean builds (three recorded runs produced the identical
  SHA-256 `2a039fc7…` and MD5; `docs/release/build-and-release.md`).

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
history and is recorded in `docs/changelog/00-index.md`.

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
rest on the `docs/changelog/00-index.md` phase sections, the `PLANS.md`
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
the ADR-020 clause 4 allowlist as amended by ADR-026 (`ArrProviderError`
code/retryability/message, the non-secret connection identity, configuration
version, bounded reason codes and enums, item/record identifiers, counts, and the
bounded log subject); it never emits an API key, the webhook secret, a
`SecretLease` value, an `X-Api-Key`/`X-ArrTags-Webhook-Secret` header, a raw
request/response body, a full provider payload, or the mutable
`PluginConfiguration`. The bounded log subject (`LogSubject`, ADR-026) is the
media file-name component only, with control scalars removed and bounded to 128
Unicode scalar values, and the Jellyfin item identifier in `D` format as the
fallback when no usable name is available; the amended clause 4 also adds an
explicit directory, drive, share, and full-path exclusion. The artwork-boundary
line additionally carries the specific bounded render classification
(`PassThroughReason`, `FailureReason`, or `SourceFailureReason`) when the result
carries one. The emitted data shape is identical at every verbosity
level, so raising verbosity cannot expand a redacted value into a secret-bearing
one. Log volume is bounded by the code-owned `LogThrottle` (ADR-020 clause 6;
`docs/architecture/12-performance-and-operational-limits.md`), and verbosity is not output-affecting
(ADR-020 clause 5). The logging path is covered by a dedicated security review
(task 10.3; report at `docs/implementation/10.3/security-review.json`).

The two plugin-initiated outbound surfaces are consistent: the host logging
pipeline (above) and the bounded, secret-free configuration-rejection
activity-log entry recorded in SEC-9 (ADR-021). Neither emits a secret or a
configuration candidate value, and neither expands with verbosity. SEC-9 remains
the only plugin-initiated administrator-visible *notification*; logging is a
diagnostic mechanism (ADR-020 clause 7).

- Evidence: security-review finding SEC-5 (rewritten by v1.1 task 10.3);
  `docs/decisions/00-index.md` ADR-005, ADR-020, and ADR-021; `src/ArrTags/Logging/`;
  `docs/architecture/00-index.md` sections 6, 11, and 12; this file's SEC-9.
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

- Evidence: security-review finding SEC-8; `docs/decisions/00-index.md` ADR-012.
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

- Evidence: `docs/decisions/00-index.md` ADR-021;
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

## v1.1 release-review accepted limitations (Phase 14)

The v1.1 release audit
(`docs/implementation/final-review/release-review.json`) returned
`SHIP_WITH_ACCEPTED_LIMITATIONS`. In addition to the V1 and security residuals
above, the following phase-review and release-process items are accepted for
v1.1. Each names its evidence and its disposition; none is a correctness defect
or a capability overclaim.

### P12R-F1. The absolute default output-fingerprint pin lives only in the forced-native golden path (MEDIUM, accepted)

The committed absolute default output/configuration-fingerprint pin is asserted
only by the `ARRTAGS_SKIA_COMPAT`-gated golden run (Phase 12 finding P12R-F1, ==
TQ-12.3-02 == TQ-12.4-01), so an unconditional default-fingerprint regression
would not fail the default `./build.sh test` suite. Production is
identity-neutral by construction for the V1 default position/size, and the
forced-native matrix leg (Failed 0, Passed 1,575, Skipped 20, Total 1,595) pins
the committed golden/manifest fingerprints and is recorded in
`docs/release/build-and-release.md` and this file's V6.

- Evidence: `docs/implementation/phase-12/phase-review.json` P12R-F1;
  `docs/implementation/phase-12/orchestration.json`; the forced-native matrix leg
  reproduced by this release audit.
- Consequence: a default-suite test-strength gap, not a correctness defect (the
  release-matrix forced-native run and the live task 14.3 run cover the shipped
  output).
- Disposition: accepted for v1.1. Post-V1 hardening: add an unguarded
  default-fingerprint pin test so the default suite guards it directly, and/or
  keep the forced-native run as an explicit release-checklist step.

### V11-G1. The badge value allowlist (Goal B) was not exercised live (LOW, accepted)

The task 14.3 pinned-host matrix live-verified the settings page, runtime
activation, post-save re-render, inventory cache, logging, and the
install/restart/uninstall invariants, including a live `Renderer.Position`/
`Renderer.Size` change; it did not post a configured `AllowedValues` allowlist
(recorded as task 14.3 reviewer finding 14.3-R3 and phase-12 finding P12R-F6).
The allowlist is implementation- and test-verified (Phase 12 review: all six
acceptance criteria MET; `BadgeAllowlistFilterTests`), and its settings-page
round-trip uses the same elevation-gated POST path that was live-verified for the
other renderer settings.

- Evidence: `docs/implementation/14.3/reviewer-report.json` 14.3-R3;
  `docs/implementation/phase-12/phase-review.json` P12R-F6 and the six MET
  criteria; `tests/ArrTags.Tests/BadgeAllowlistFilterTests.cs`.
- Consequence: the Goal B live confirmation remains an unexercised live facet.
- Disposition: accepted for v1.1; optional post-V1: extend the pinned-host
  matrix with one allowlist save/re-render row.

### V11-G2. Phase-review LOW residuals (accepted, tracked)

The Phase 9-13 reviews carry the following non-blocking LOW/INFORMATIONAL
residuals, each tracked in its phase review/orchestration record: P9-F2 (a save
that changes only a non-output-affecting value caused a bounded full-library
provider sweep before the Phase 11 inventory cache landed; superseded by the
cache), P9-F3 (cleared numeric page inputs can produce an opaque host
deserialization error instead of the bounded rejection; no state corruption),
P9-F4 (two task 9.1 test-quality items: host-guarded fact strength and the
never-unregistered ALC resolver), P10-F5 (a future instrumented type omitted from
`RegisterArrTagsLogs` would fail silently; all eleven current types are
registered), P11R-F7 (unwired `ArrInventoryCacheEntry.WithFailure`/`LastError`/
`EvaluateState` members, docs honest), P11R-F8 (no DI-composition test for the
shared inventory singleton), P11R-F9 (the inventory byte bound is a conservative
estimate; the record cap is the hard bound), P11R-F10 (bulk-id chunk size reuses
`ReconciliationBatchSize` rather than a request-line bound), P12R-F2/P13-F3
(stale source XML doc comments on the private `AppendBadgePlacement` helpers),
P12R-F5 (no single end-to-end allowlist/position/size composition test; seams
covered separately), and P13-F4/P13-F5 (record/wording nits in the 13.2
orchestration and the changelog/PLANS scope wording).

- Evidence: `docs/implementation/phase-{9,10,11,12,13}/phase-review.json` and
  `orchestration.json`.
- Consequence: bounded test-strength, record-keeping, or UX-polish debt; no
  correctness, security, or capability impact.
- Disposition: accepted for v1.1; post-V1 cleanup as opportunities arise.

### V11-G3. Carried release LOWs from the `1.0.1.0` audit (accepted)

RR101-1: `README.md` presents the catalog and manual install paths without a
pre-publication caveat, while this file's P6 records that the `v1.1.0` manifest,
tag, and GitHub release are the user's pending manual step; the README becomes
accurate once the user publishes. RR101-2: `scripts/publish-release.sh
--release-only` checks the manifest and local tag but does not pre-verify that
the local artifact exists and its MD5 equals the manifest checksum (the
post-upload verification still rejects a mismatch), and the jq-less checksum
fallback selects the first manifest checksum.

- Evidence: `docs/implementation/final-review/release-review.1.0.1.json`
  RR101-1/RR101-2; `scripts/publish-release.sh`; this file's P6.
- Consequence: a documentation-honesty gap and a manual-publish-step
  fail-slow gap; neither affects the built artifact.
- Disposition: accepted for v1.1 as carried history; optional post-V1: add a
  README caveat note and fail-fast artifact/checksum pre-checks to the publish
  script.

### V11-G4. Stale "owned by task 14.3" pending phrasing retained in archived history (LOW, accepted)

At v1.1, `README.md` (known-limitations bullet) and the F1 status/consequence
lines (now in [`archive.md`](archive.md)) presented the task 14.3 live pinned-host
confirmation as pending, and some Phase 11/12/13 sentences in
`docs/plan/archive/project-status-history.md` and
`docs/plan/archive/v1-readiness.md` retain the same pre-14.3 framing, while task
14.3 is complete and passed all eight matrix rows.

- Evidence: `docs/implementation/14.3/live-verification.json`
  (`overall_verdict` `PASS`); the v1.1 release audit's independent live re-run of
  the same pinned host (install/load, publication, readback identity, source
  preservation, provider outage, restart persistence, uninstall drain; all
  PASS).
- Consequence: task 21.2 corrected the editable current-state surfaces
  (`README.md` and the F1 lines in `archive.md`); `docs/architecture/00-index.md`
  no longer carries the framing either. Only the immutable archived history
  under `docs/plan/archive/` retains the historical phrasing, which the project
  does not rewrite; accepted as history, not as a current-state claim. No code
  or rebuild impact.

## v1.2 phase 16 accepted limitations (G6/G8)

### V12-G6-1. A legacy in-range off-step byte limit is re-quantized or rejected on the next settings save (LOW, accepted)

The pre-16.1 settings page accepted any in-range integer for the seven
byte-denominated limits, so an upgraded `plugins/configurations/ArrTags.xml`
(or a hand-edited one) can carry a value that is not a multiple of its field's
ADR-028 display step - for example 100000 for `ProviderResponseLimitBytes`.

- Behaviour: such a value loads, validates, activates, and serializes unchanged
  and is displayed at the field's fixed precision. On the next save the page
  converts the displayed value back with the explicit step-multiple check: for
  the whole-KiB and whole-MiB fields (`WebhookMaxPayloadBytes`,
  `RenderCacheQuotaBytes`, `ArtifactStorageQuotaBytes`, `InventoryCacheMaxBytes`)
  the display rounds to a whole unit, so the save writes the nearest whole unit,
  adjusting the value by at most half a display unit; for the three
  64-KiB-step MB fields (`ProviderResponseLimitBytes`, `SourceArtifactLimitBytes`,
  `DerivedArtifactLimitBytes`) the save is rejected (native and page validity
  message) until the operator enters a step multiple, because the rounded
  displayed value is not a step multiple.
- Consequence: an upgraded configuration holding such a value must have the
  affected 64-KiB-step field edited to a step multiple before any settings save
  succeeds, and an off-step whole-unit field value is adjusted on the next save.
  The persisted XML is never rewritten without a save and the byte-range
  semantics are unchanged; this is ADR-028 clause 1's reject-rather-than-round
  behavior outside the exact min/default/max round-trip guarantee, not a
  regression.
- Evidence: ADR-028 clause 1; task 16.1 worker report and reviewer finding
  16.1-F2; `tests/ArrTags.Tests/ByteLimitUnitsTests.AnExistingConfigurationWithAnOffStepByteValueStillLoadsUnchanged`;
  `src/ArrTags/Configuration/config.html` (`byteLimitToBytes` step check).
- Disposition: accepted for v1.2 (disclosed by task 16.1, registered by task
  16.6); no code change.

### V12-G8-1. The restart-required modal reminder is a best-effort web-client shim verified structurally, not executed (LOW, accepted)

The modal reminder is implemented and shipped, but it cannot be executed by the
test suite or the pinned live matrix. The repository deliberately has no
JavaScript runtime, so `RestartRequiredReminderTests` and the task 16.6
`GoalG6G8IntegrationTests` pin the reminder's call shape, placement, guard,
detection domain, and message structurally over the embedded settings page; the
modal itself is never executed. The pinned test host's launcher uses
`--nowebclient`, so the documented live matrix does not serve the bundled web
client either.

- Precondition: a dashboard save that changes a restart-required setting, in a
  jellyfin-web build that exposes `window.Dashboard.alert`.
- Behaviour: the page calls `Dashboard.alert({ title, message })`, an
  intentionally retained legacy compatibility shim with an in-source TODO to
  remove it; it is a web-client surface, not a server plugin ABI, and it is
  present only in jellyfin-web. The API's presence and modal semantics were
  confirmed against the pinned host's bundled asset and the matching
  jellyfin-web v12.0 source, not in a live browser.
- Consequence: if a future web client renames or removes `window.Dashboard` or
  its `alert` method, the reminder silently shows nothing; the always-present
  per-setting note text remains and the save behavior is unchanged (ADR-028
  clause 4 fail-open). Dismissing the modal with Escape or the backdrop leaves a
  benign unhandled rejection in the browser console.
- Evidence: `docs/research/jellyfin-expert/modal-notification-api.json` (task
  16.4 spike); task 16.5 worker and reviewer reports; task 16.6 integration
  verification; `docs/architecture/06-configuration-and-persisted-state.md`.
- Disposition: accepted for v1.2; a live-browser probe would require serving the
  bundled web client and logging in, which the pinned matrix does not do.

### V12-G8-2. The global `OperationalLimits.RequestTimeoutSeconds` is inert (INFORMATIONAL, accepted)

`OperationalLimits.RequestTimeoutSeconds` is validated, persisted, exposed on
the settings page, and classified per-operation by the ADR-028 clause 2 audit,
but no runtime consumer reads it.

- Behaviour: editing the global field changes nothing at runtime and nothing
  after a restart; the effective request timeout is the per-connection
  `ArrConnectionConfiguration.RequestTimeoutSeconds`, which is carried into each
  read client.
- Consequence: the settings page shows a field whose change cannot take effect.
  ADR-028 clause 2 is binary and classifies a setting with zero consumers as
  per-operation (a restart would not apply it either), so the field carries no
  restart note; the audit records the inert field explicitly rather than
  omitting it.
- Evidence: `src/ArrTags/Configuration/RestartRequiredSettings.cs` (the audited
  no-consumer note); `docs/implementation/16.2/consumer-evidence.md` section 5;
  `tests/ArrTags.Tests/RestartRequiredSettingsTests.OnlyTheAuditedInertSettingHasNoRuntimeConsumer`;
  task 16.2 reviewer finding 16.2-F2.
- Disposition: accepted for v1.2 as a pre-existing field; removing or aliasing
  it is a separate change outside the phase-16 scope.

## v1.2 phase 17 accepted limitations (F3)

### V12-F3-1. The read-only diagnostics panel is verified structurally, not executed (LOW, accepted)

The read-only "Diagnostics" panel on the settings page is implemented and
shipped (ADR-025; Phase 17 tasks 17.1-17.4), but its inline JavaScript cannot be
executed by the test suite or the pinned live matrix. The repository deliberately
has no JavaScript runtime, so `DiagnosticsStatusPanelTests` pins the fixed
counter table (derived from and checked against the C# `DiagnosticsSnapshot`,
`MatchingFailureCounts`, and `RenderFailureCounts` model), the
`ApiClient.ajax`/`ApiClient.getUrl('ArrTags/Status')` call pair (confirmed
against the pinned bundled web-client source), the bounded count/health readers,
and the fail-closed branches structurally over the embedded settings page; the
fetch, the web client's rejection path, the DOM construction, and the no-data
rendering are never executed. The pinned test host's launcher uses
`--nowebclient`, so the documented live matrix does not serve the bundled web
client either.

- Precondition: an authenticated administrator opening the settings page in a
  running jellyfin-web/12.0 build that exposes `ApiClient.ajax` and
  `ApiClient.getUrl`, with the elevation-gated `GET ArrTags/Status` endpoint
  available.
- Behaviour: the panel fetches once per page show with no retry or polling,
  renders only the fixed 28-row counter table with bounded values (a missing or
  non-numeric count as `0`; a health value only from the declared
  `ArrConnectionHealth` names, otherwise `Unknown`), and clears on a rejected or
  unavailable request or an unparseable or non-object body; a well-formed but
  unexpected or partial-shape object renders the fixed table with the fallback
  values. The call shape and DOM behavior were confirmed structurally against
  the endpoint model and the pinned bundled web-client source, not in a live
  browser.
- Consequence: if a future web client changes `ApiClient.ajax`,
  `ApiClient.getUrl`, or the page script's runtime behavior, the panel would
  silently render nothing or clear instead, and neither the suite nor the pinned
  live matrix would catch it. The endpoint remains read-only, bounded, and
  secret-free, and no other surface depends on the panel. The v1.2 plan assigns
  live verification of the status panel and endpoint to Phase 21 task 21.4, but
  that matrix runs `--nowebclient`, so it does not execute the panel's inline
  JavaScript either; this is a standing v1.2 verification limitation rather than
  a phase-verification boundary.
- Evidence: the `docs/decisions/ADR-025.md` implementation note (verification
  bounds); the F3 verification bounds in `docs/limitations/archive.md`; the task
  17.4 entry in `docs/changelog/v1.2.0.md`;
  `tests/ArrTags.Tests/DiagnosticsStatusPanelTests.cs`; `docs/planning/v1.2.md`
  section 9 (Phase 21 task 21.4); `docs/implementation/17.4/security-review.json`
  SR-17.4-02.
- Disposition: accepted for v1.2; a live-browser probe would require serving the
  bundled web client and logging in, which the pinned matrix does not do.
  Registered by the Phase 17 task 17.4 security-review follow-up (SR-17.4-02).

## v1.2 phase 19 accepted limitations (F4/F6)

The F4 and F6 mechanisms are resolved by Phase 19 (tasks 19.1-19.5; ADR-022 as
amended by ADR-029 and ADR-023). The residuals below are the accepted bounds of
the shipped mechanisms, each derived from the ADR consequences and the phase
reviews; the mechanisms are not presented as unbounded.

### V12-F4-1. The reconciliation resume is O(offset) per run and O(N²/`QueueCapacity`) per full cycle (MEDIUM, accepted)

The pinned Jellyfin 12 host offers no keyset/after-key predicate, so the resume
locates the persisted cursor's unique `itemId` by walking the host-ordered pages
from the start of the order (the ADR-029 bounded linear skip). A run therefore
reads ordered rows proportional to the boundary item's position, and a full
round-robin cycle performs cumulative skip work quadratic in the candidate count.

- Behaviour: one scheduled/post-scan run's resume work is `O(offset)` ordered
  rows (bounded by the server-wide candidate count plus `QueueCapacity`, not by a
  constant); a full cycle over `N` candidates is `O(N²/QueueCapacity)` cumulative
  skip work. `N` is the **server-wide** movie+episode candidate count because the
  enumerator query is not library-scoped
  (`JellyfinMediaLibraryEnumerator` uses a recursive `IncludeItemTypes` query
  with no `ParentId`/library scope), so a late-cycle resume scans a large prefix
  even for a small enabled-library scope.
- Consequence: a very large library takes several reconciliation intervals to
  cover completely and each late-cycle run spends skip work proportional to its
  offset; this is the accepted cost of the only host-supported mechanism
  (ADR-029 clause 3 and its registered residual 1), not a defect. No runtime cost
  measurement was performed; the structural `N + QueueCapacity` bound and its
  growth with `N` are asserted by
  `SuccessiveRunCoverageMatrixTests.OneRunPageReachTracksTheServerWideCandidateCountPlusQueueCapacity`.
- Evidence: ADR-029 Consequences (registered residual 1) and the amendment
  record `docs/implementation/planning/v1.2-f4-clause-3-amendment.json` (F4-R1,
  AR-ADR022C3-03); tasks 19.1 and 19.2 worker reports;
  `src/ArrTags/Media/JellyfinMediaLibraryEnumerator.cs`;
  `tests/ArrTags.Tests/SuccessiveRunCoverageMatrixTests.cs`.
- Disposition: accepted for v1.2. A per-library (`ParentId`-scoped) enumerator
  with one cursor per enabled library is a possible future mitigation of the skip
  cost, but it changes the server-wide query and the single fixed cursor record,
  so it is out of scope (ADR-029 rejected alternatives).

### V12-F4-2. Rows tying on the exact `(SortName, Name)` pair have no host-guaranteed order (MEDIUM, accepted)

The pinned host's effective candidate order is `SortName` ascending then raw
`Name` ascending, which is not a guaranteed strict total order, and no supported
configuration yields an identifier tie-break.

- Precondition: two candidate rows share the exact `(SortName, Name)` pair.
- Behaviour: coverage is guaranteed for rows strictly ordered by that pair and is
  best-effort for rows that tie, which have no host-guaranteed relative order. A
  page boundary inside a tie can re-cover, or in a persistently ordered tie skip,
  an item across a cycle; the ordered-page enumerate/locate mechanism cannot
  distinguish the tied rows.
- Consequence: a tied row may receive no work in a cycle (it is retried in a
  later cycle, because the cursor wraps) or receive bounded duplicate work; no
  strictly ordered item is skipped. This is ADR-029 clause 5's registered
  residual and is exercised as a deferral, never asserted as covered, by
  `SuccessiveRunCoverageMatrixTests.ExactSortNameNameTiesAreBestEffortAndNotCoveredWithinTheCycle`.
- Evidence: ADR-029 clauses 1 and 5 and Consequences (registered residual 2); the
  amendment record F4-R2 (AR-ADR022C3-04); tasks 19.1 and 19.2 worker/reviewer
  reports; `docs/research/jellyfin-expert/reconciliation-cursor-host-order.json`.
- Disposition: accepted for v1.2. A host-ordered identifier tie-break does not
  exist in the supported query surface.

### V12-F4-3. The cursor is a `Cache` record subject to render-cache age pruning (LOW, accepted)

`ReconciliationCursorStore` writes the cursor under `StateAuthority.Cache`
(ADR-022 clause 4), and `StateRetentionService.RunRetentionPass` exempts only
`MetadataStateStore.RecordKind` from the cache age TTL, so the cursor is subject
to the render-cache retention pass.

- Precondition: the effective whole-scope reconciliation interval is longer than
  the configured `RenderCacheTtlMinutes` (default 1440 minutes = 24 h; floor 1
  minute). Under the shipped defaults the interval is 12 h
  (`ArrTagsReconciliationTask.DefaultInterval`), so the cursor is rewritten
  before it can expire.
- Behaviour: when the record is pruned, `ReconciliationCursorStore.Read` observes
  a miss and the next whole-scope run resets to the start of the candidate order,
  re-covering a cycle's prefix from the beginning (the ADR-022 clause 4/5
  missing-record semantics).
- Consequence: a bounded re-cover, not a failure or a skip: every item is still
  covered round-robin across the following runs. The interaction is recorded
  rather than fixed (task 19.1 reviewer finding R-19.1-02); no state is corrupted.
- Evidence: task 19.1 reviewer report R-19.1-02;
  `src/ArrTags/Reconciliation/ReconciliationCursorStore.cs` (the `Cache` write);
  `src/ArrTags/PluginLifecycle/StateRetentionService.cs` (only the metadata-state
  kind is exempt); `src/ArrTags/State/StateRetention.cs` (cache age TTL).
- Disposition: accepted for v1.2; no code change. A cursor exemption (or a
  per-record retention class) is a possible future hardening.

### V12-F4-4. An unlocatable cursor anchor resets the run to the start (LOW, accepted)

ADR-029 clause 4 resets the cursor to the start when the boundary item cannot be
located (it was removed or left the movie/episode candidate enumeration, or the
host order changed) instead of failing the run.

- Precondition: the persisted boundary item is removed or is no longer a
  movie/episode candidate between runs.
- Behaviour: the locate walk reaches the end of the candidate order and reports
  `AnchorNotFound`; the run then enumerates from index 0 and rewrites the cursor
  from that run's coverage.
- Consequence: one cycle's prefix is re-covered after the anchor is lost (a
  bounded but visible cost). An item that merely becomes service-ineligible while
  remaining an enumerable movie/episode candidate is still located by identity,
  so the cursor does not reset for that reason. This is ADR-029 clause 4 and its
  registered residual 3; the reset and no-failure behavior are covered by
  `SuccessiveRunCoverageMatrixTests.UnlocatableAnchorResetsToTheStartAndTheRunDoesNotFail`.
- Evidence: ADR-029 clause 4 and Consequences (registered residual 3); the
  amendment record F4-R3 (AR-ADR022C3-05); task 19.2 worker/reviewer reports.
- Disposition: accepted for v1.2; no code change.

### V12-F4-5. The resume relies on the pinned host's internal candidate order (LOW, accepted)

The identity-anchored locate walk requires the host's effective candidate order
(`SortName` ascending, raw `Name` ascending) to locate the anchor's position.
This order is an internal Jellyfin implementation detail with no plugin
compatibility promise.

- Precondition: a future Jellyfin host changes its default candidate ordering or
  collation.
- Behaviour: the mechanism degrades to a rescan or a reset to the start, not to a
  wrong result: a changed order either still contains the anchor (the locate walk
  finds its new position) or reports `AnchorNotFound`, which resets the cursor per
  `V12-F4-4`.
- Consequence: a host update can cost a bounded re-cover; coverage (round-robin)
  is retained, and the plugin does not depend on cross-version ordering
  stability. The unit coverage matrix reproduces the documented order over an
  `ILibraryManager` double with an ordinal comparer, not the host SQL collation
  (a recorded test substitute, TQ-19.2-01), and no live pinned-host run of the
  mechanism was performed in Phase 19.
- Evidence: ADR-029 clauses 1, 3, and 4 and Consequences (registered residual 4);
  the amendment record F4-R4;
  `docs/research/jellyfin-expert/reconciliation-cursor-host-order.json`; task
  19.2 test-quality review TQ-19.2-01.
- Disposition: accepted for v1.2; no code change.

### V12-F4-6. Overlapping whole-scope runs are not cross-run locked (INFORMATIONAL, accepted)

`LibraryReconciliationService` is a singleton with no cross-run lock: each run
reads the cursor once at start and `AdvanceCursor` persists only the position that
run itself covered (task 19.1 reviewer finding R-19.1-04).

- Precondition: two whole-scope runs (for example a manual scheduled run and a
  post-scan run) overlap.
- Behaviour: the later writer can move the persisted cursor back to an earlier
  covered position, but never forward past an item neither run covered.
- Consequence: the next run re-covers the overlap: bounded extra work, not a
  state-integrity failure and not a skip. No change was required or made.
- Evidence: task 19.1 reviewer report R-19.1-04;
  `src/ArrTags/Reconciliation/LibraryReconciliationService.cs` (one cursor read
  per run; the advance covers that run's own covered prefix).
- Disposition: accepted for v1.2; no code change.

### V12-F6-1. The configuration version can advance between the metadata publication and the artwork stage of the same pass (LOW, accepted)

`ArtworkPublishingWorkItemProcessor` re-checks the carried configuration version
after the metadata state is published and before the artwork decision. That check
returns the successful metadata-publication result rather than a discard, so the
worker does not re-enqueue for it.

- Precondition: a configuration save advances the version after the metadata
  processor's pre-publication version re-check has passed but before the artwork
  stage of the same pass runs (the item's post-save hint was coalesced in that
  window).
- Behaviour: that pass still publishes the metadata state under the pre-save
  version, performs no artwork work, and returns success; there is no
  `DiscardReason` and no re-enqueue, so the new artwork applies when the item is
  next processed by any trigger (library event, webhook, post-save, post-scan, or
  scheduled run).
- Consequence: the same F6 symptom (an item not re-rendered by the save) remains
  for this narrow, race-only window; the pending/in-flight case named by the
  limitation is fixed. ADR-023 clause 1 deliberately scopes `ConfigurationStale`
  to discards, and its rejected alternative forbids extending re-enqueueing to a
  successful-but-stale pass, so the window is accepted rather than silently
  unsuccessful.
- Evidence: task 19.3 reviewer finding 19.3-F1; task 19.3 worker report
  (interpretations and known limitations);
  `src/ArrTags/Updates/ArtworkPublishingWorkItemProcessor.cs` (the version guard
  returns the reconciliation result); `docs/decisions/ADR-023.md` clause 1 and
  its rejected alternative.
- Disposition: accepted for v1.2 (registered by task 19.5); no code change. A
  worker-level or artwork-stage re-enqueue is explicitly out of ADR-023's scope.

### V12-F6-2. The stale-basis re-enqueue's bounded drop is not logged or counted (INFORMATIONAL, accepted)

`LibraryWorkWorker.ReenqueueStaleBasis` enqueues the fresh current-version item
with `_ = _queue.Enqueue(...)` and ignores the returned bounded
`WorkHintEnqueueOutcome`; no log record and no diagnostics counter is written for
the re-enqueue or its outcome.

- Precondition: the configuration-stale discard occurs while the queue is stopped
  (lifecycle fence or shutdown) or at capacity.
- Behaviour: the bounded enqueue reports `Stopped`/`Overflow` and the re-enqueue
  is dropped per ADR-023 clause 3; the worker writes no record of the drop, and
  the ADR-025 diagnostics counters (queue depth/in-flight and the bounded
  classifications) do not include it.
- Consequence: the item keeps its previously published artwork until a later
  trigger (library event, webhook, post-save, post-scan, or scheduled run) admits
  it, and an operator has no diagnostic that the post-save re-render was dropped.
  The drop itself is bounded and deliberately accepted by ADR-023 clause 3, so
  this is an observability residual, not a correctness defect.
- Evidence: `src/ArrTags/Updates/LibraryWorkWorker.cs` (`ReenqueueStaleBasis`);
  ADR-023 clause 3; the ADR-025 counter-set contract (`docs/decisions/ADR-025.md`
  clause 3).
- Disposition: accepted for v1.2 (registered by task 19.5); no code change. A
  bounded log record (or counter) for the dropped re-enqueue is possible future
  hardening.

## v1.2 release-review accepted limitations (F7 ownership recovery)

The v1.2 release audit
(`docs/implementation/final-review/release-review-v1.2.json`, finding RR-V12-1)
registered the following carried operational bound for v1.2.

### V12-F7-1. An item whose active image was re-adopted by a host refresh enters the fail-closed ownership state with no in-product recovery trigger (LOW, accepted)

A host refresh that re-adopts the local sidecar poster as an item's Primary
image desynchronises the served image from the plugin's recorded active
identity, and the fail-closed ownership gate then suppresses further automatic
publication for that item. v1.2 ships no in-product recovery trigger for such an
item; recovery is an out-of-band operator action.

- Precondition: an item whose active image the plugin recorded as published is
  refreshed by the host in a way that re-adopts the local sidecar — a full
  `POST /Library/Refresh` scan, or a targeted per-item
  `POST /Items/{id}/Refresh?MetadataRefreshMode=FullRefresh&ImageRefreshMode=FullRefresh&ReplaceAllMetadata=false`
  such as the one the task 21.13 verifier issued while repairing its own
  library/sidecar setup after the first publication.
- Behaviour: the host's image refresh serves the sidecar in place of the
  plugin-published badge while the persisted artwork-state record can still
  assert the published identity. The publication path then fails closed (the
  task 21.4 characterisation inherited by task 21.13:
  `ArtworkRegenerationPlanner.IsPublicationBlocked` blocks `OwnershipLost`,
  `OwnershipUnknown`, `RestorePending`, and `RestoreBlocked`, and the
  restoration path accepts only the published state), so no automatic trigger —
  library event, webhook, post-save, post-scan, or scheduled reconciliation —
  restores the badge, and the stored record is not self-healed.
- Consequence: the item keeps the re-adopted host image, and a badge can be
  restored only by an out-of-band operator action; v1.2 documents no supported
  in-product procedure for it. The documented prevention is to complete all
  library and sidecar setup and the final full refresh before the first
  publication (the task 21.13 verifier deliberately abandoned its affected
  prefix rather than clear the state out of band, because that would simulate an
  undocumented operator action). The task 21.13 observation is narrower than a
  transition and is stated as such: the episode's served Primary reverted from
  the published 13,751-byte badge to the 3,473-byte source sidecar while the
  artwork-state record continued to assert the published identity (State 1,
  `ActiveImageIdentity` `9B084736…38FB4`, `LastOwnershipObservation.Status 0`),
  and no `OwnershipLost` transition was observed or claimed on that prefix. The
  fail-closed no-recovery substance is inherited from F-21.4-2 (carried through
  F-21.9-2); this run adds no new evidence for the transition itself.
- Evidence: `docs/implementation/21.13/live-verification.json` finding
  F-21.13-2 and its corrected detail;
  `docs/testing/jellyfin-12-musl-test-host.md` ("v1.2 cautions" and the
  "A targeted item refresh can also re-adopt the sidecar (F-21.13-2, LOW)"
  procedure note); `docs/implementation/21.4/live-verification.json` F-21.4-2;
  `docs/implementation/21.9/live-verification.json` F-21.9-2; the release
  section of `docs/changelog/v1.2.0.md`.
- Disposition: accepted for v1.2 by the release review
  (`docs/implementation/final-review/release-review-v1.2.json`, RR-V12-1);
  recovery is an out-of-band operator action and no in-product recovery trigger
  is planned for v1.2. Registered by
  `docs/implementation/final-review/v1.2-limitation-registration.json`.
