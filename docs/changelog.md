# Changelog

This changelog records completed milestones, their verification evidence, and
current integration progress. Architecture and accepted limits live in
`docs/architecture.md`, `docs/data-model.md`, and `docs/decisions.md`; they are
referenced here, not duplicated.

## Phase 1 - Plugin foundation (Milestone 1)

**Status:** Complete. Gate 1 met.

**Commit/tag:** tag `v0.1.0-phase1`, at commit
`3aac81e7e0f6a0bdb29075c2c021b44632178ba4` ("Phase 1 complete: Jellyfin 12
plugin foundation", 2026-09-17). Tasks 1.1-1.8 landed in commits `91cdb14`
through `3aac81e`.

### Files and components introduced

- Repository scaffold: `ArrTags.slnx`, `Directory.Build.props`, `global.json`,
  `build.sh`, `build.yaml`, and the package target in `src/ArrTags/ArrTags.csproj`.
- `src/ArrTags/Plugin.cs` - `BasePlugin<PluginConfiguration>` entry point.
- `src/ArrTags/Configuration/` - `ArrConnectionConfiguration`,
  `PluginConfiguration`, `OperationalLimits`, `PluginConfigurationValidator`,
  `ConfigurationValidationResult`, `PluginConfigurationSnapshot`,
  `ConfigurationSnapshotService`.
- `src/ArrTags/PluginLifecycle/` - `ArrTagsServiceRegistrator`,
  `ArrTagsLifecycleService`, `JellyfinLibraryEventSource`,
  `ILibraryEventSource`, `LibraryItemChangedEventArgs`.
- `src/ArrTags/State/` - `StateRepository`, `PluginStatePaths`,
  `StateEnvelope`, `StateEnvelopeCodec`, `AtomicFileWriter`,
  `StateQuarantine`, `StateRetention`, `StateAuthority`, `StateReadStatus`,
  `StateReadResult<T>`, `StateResults`.
- `tests/ArrTags.Tests/` - `PluginFoundationTests`, `ConfigurationFoundationTests`,
  `LifecycleFoundationTests`, `StateBoundaryTests` (43 tests total).
- Documentation: `docs/architecture.md` (single authoritative reference),
  `docs/data-model.md` sections 3.4/3.4.1, `docs/decisions.md` ADR-004,
  `docs/implementation-readiness.md`, `PLANS.md` status.

### Public APIs and interfaces introduced

- Plugin identity: `Plugin` with GUID `40322d52-5680-449f-b33e-e01836ee2f46`.
- Configuration boundary: `ConfigurationSnapshotService.Current` /
  `TryReplace(...)` returning `ConfigurationValidationResult`; immutable
  `PluginConfigurationSnapshot` with last-valid retention and secret exclusion.
- Operational limits: `OperationalLimits.Validate(ICollection<string>)` and
  `Clone()` enforcing the ADR-004 ranges.
- State boundary: `StateRepository.Read<T>` / write / quarantine APIs using
  `StateAuthority` (cache versus authoritative), `StateReadResult<T>`,
  `PluginStatePaths` (traversal-safe), and atomic envelope writes.
- Lifecycle: `IPluginServiceRegistrator.RegisterServices(...)`,
  `ILibraryEventSource`, and `IHostedService` subscribe/unsubscribe behavior with
  no provider or full-library work.
- Canonical identity model specification (task 1.3): connection-scoped
  `SonarrIdentity` (series, episode, episode-file), `RadarrIdentity`, and
  `ArrFileIdentity` with explicit `Present`/`Absent`. Specification only; types
  and tests land with the integration and matching milestones.

### Configuration and plugin manifest status

- `build.yaml`: `name: ArrTags`, `guid: 40322d52-...`, `version: 0.1.0.0`,
  `targetAbi: 12.0.0.0`, `framework: net10.0`, artifact `ArrTags.dll`.
- `Directory.Build.props` pins version `0.1.0.0`; `global.json` pins SDK
  `10.0.0` with `latestMinor` roll-forward; lock files are committed for both
  projects.
- Jellyfin packages pinned to exactly `12.0.0` (`Jellyfin.Controller`,
  `Jellyfin.Model`, with `ExcludeAssets="runtime"`), enforced by
  `packages.lock.json` and a dependency-graph test forbidding `12.1.x`.
- Sonarr and Radarr connections are independently configurable and disabled by
  default; validation covers URLs, finite timeouts, limits, library scope, and
  secret redaction.

### Build and test results

Validated in this check of the tagged tree with .NET SDK `10.0.401`
(`DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1`, no live Arr instance):

- `./build.sh restore` - succeeded, up to date in locked mode.
- `./build.sh build` - succeeded, 0 warnings, 0 errors.
- `./build.sh test` - 43 passed, 0 failed, 0 skipped (~430 ms).
- `./build.sh package` - produced `artifacts/ArrTags_0.1.0.0.zip`.

### Jellyfin host validation results

Recorded Phase 1 evidence (see `docs/implementation-readiness.md`): the
generated package was installed on the pinned Jellyfin `12.0.0` host (portable
`v12.0` amd64, Ubuntu 24.04, .NET runtime `10.0.11` / host `10.0.12`) with both
providers disabled. The host reported `Loaded plugin: ArrTags 0.1.0.0`, wrote
`meta.json` with `targetAbi: 12.0.0.0` and `status: Active`, completed startup,
restart, and shutdown with no load errors, unmanaged background work, or secret
leakage.

### Outstanding implementation TODOs

- Implement the canonical identity types (`SonarrIdentity`, `RadarrIdentity`,
  `ArrFileIdentity`) as code with the fingerprint and serialization tests
  specified in `docs/data-model.md` section 3.4.1. The types, equality,
  connection scoping, and the metadata fingerprint are implemented and tested
  in task 2.5; persisted cache/state serialization tests land with the cache
  milestone.
- Connect `ConfigurationSnapshotService` to Jellyfin's configuration save path;
  no core worker consumes the snapshot yet.
- Add artifact byte storage and per-surface `ArtworkOperation` journaling to the
  state boundary; only bounded metadata/envelope state exists today.
- Register queue, renderer, and scheduled-task services as those components are
  introduced; the provider HTTP clients and the versioned credential resolver
  are registered in tasks 2.2 and 2.3.
- Resolve `SaveImage` storage/readback and source-capture behavior on the target
  host configuration.

### Phase 2 (Sonarr & Radarr integration) - complete

Tasks 2.1-2.7 are complete and Milestone 2 is complete with Gate 2 met. The
shared provider-client boundary and connection
identity (task 2.1), dedicated `IHttpClientFactory` client registration
(task 2.2), and the versioned credential boundary (ADR-005, task 2.3) are
implemented. The read-only Radarr v3 client probes
`GET /api/v3/system/status`, reads the local library through `GET /api/v3/movie`,
and reads the fully populated current file through the dedicated
`GET /api/v3/moviefile?movieId=` endpoint with bounded timeout, cancellation,
retry/backoff, response-size limits, and redacted errors.

The read-only Sonarr v3 client (task 2.4) probes `GET /api/v3/system/status`,
reads the local library through `GET /api/v3/series`, reads episodes through
`GET /api/v3/episode?seriesId=&includeEpisodeFile=true`, and reads the series
file inventory through `GET /api/v3/episodeFile?seriesId=` under the same
bounded and redacted policy. The current episode file is joined by the
validated `episodeFileId == episodeFile.id` rule: the embedded file is trusted
only when its identifier matches, the series inventory is the fallback, and a
missing association resolves to no file identity rather than an unrelated file.

Task 2.5 adds the canonical identity and metadata mapping boundary:

- `src/ArrTags/Providers/ArrFilePresence.cs`, `ArrFileIdentity.cs`,
  `ArrRecordIdentity.cs`, `SonarrIdentity.cs`, and `RadarrIdentity.cs` -
  connection-scoped, typed record and file identity with explicit
  `Present`/`Absent` file presence that is never encoded as zero or empty.
- `src/ArrTags/Metadata/` - `BadgeMetadata` with quality, resolution,
  dynamic-range, Dolby Vision, codec, channel, audio-feature, source,
  upgrade-pending, custom-badge, and extension fields, plus the value
  descriptors and origin/dynamic-range/audio-feature enums, and a deterministic
  metadata fingerprint over the schema version, identity, and all
  badge-affecting values.
- `src/ArrTags/Providers/Radarr/RadarrMetadataMapper.cs` and
  `src/ArrTags/Providers/Sonarr/SonarrMetadataMapper.cs` - map validated
  provider DTOs into canonical `BadgeMetadata`, read actual file quality only
  from the current file resource (never a quality profile), validate the
  supplied file against the authoritative `movieFileId`/`episodeFileId`
  association, and keep provider DTOs out of canonical types.
- `tests/ArrTags.Tests/CanonicalIdentityTests.cs` and
  `tests/ArrTags.Tests/MetadataMappingTests.cs` - identity scoping, explicit
  file presence, actual-quality mapping, unknown-value propagation, fingerprint
  stability, and DTO/profile non-leakage (22 new tests).

Radarr's `RadarrMediaInfoResource` DTO now includes the confirmed `width` and
`height` media-info fields used for inspected resolution mapping.

Task 2.6 hardens canonical unknown-value and custom-value semantics:

- `src/ArrTags/Metadata/BadgeMetadata.cs` - `AudioFeatures` is nullable so an
  absent feature set is unknown and an empty set is a reported codec with no
  known feature; the fingerprint distinguishes the two states and the badge
  schema version is now `2`. Custom badge values are bounded at construction:
  blank values dropped, control characters removed, at most
  `MaxCustomBadgeCount` (32) values kept in provider order, and each value
  truncated to `MaxCustomBadgeLength` (128) characters.
- `src/ArrTags/Providers/Radarr/RadarrMetadataMapper.cs` and
  `src/ArrTags/Providers/Sonarr/SonarrMetadataMapper.cs` - return no audio
  feature set when the audio codec is not reported.
- `tests/ArrTags.Tests/MetadataMappingTests.cs` - unknown-versus-empty audio
  features, fingerprint distinction, and custom-value count, order, length, and
  control-character behavior (7 new tests).

Task 2.7 adds the provider failure matrix:

- `tests/ArrTags.Tests/ProviderFailureMatrixTests.cs` - 64 cases covering both
  providers for authentication failures (`401`/`403`, missing credential lease
  with no HTTP call, redacted messages), unavailable services (`409`, `429`,
  `5xx`, unreachable connection, timeout), malformed and oversized responses
  (including a chunked response without `Content-Length`), tolerant
  optional-field deserialization and unknown-value mapping, version drift
  (future version, casing, extra fields, missing version) versus a missing
  provider identity, cancellation during retry backoff, bounded retries,
  non-retried permanent failures, and API-key header reapplication on retries.

Milestone 2 is complete: both integrations are read-only, probe and query
independently, map to canonical observations, and pass the contract and failure
tests. Media matching (Phase 3) followed and is complete.

### Phase 3 (Media matching) - complete

Tasks 3.1 through 3.8 are complete and the Milestone 3 gate is met.

- Task 3.1 (`src/ArrTags/Media/`): canonical `MediaIdentity` snapshots for
  Movie, Series, Season, and Episode, deterministic collection-folder/library
  scope, and V1 badge-surface eligibility limited to Movie and Episode posters
  (ADR-006).
- Task 3.2 (`src/ArrTags/Matching/`): provider-neutral candidate selection
  (`MatchCandidate`, `CandidateMatchRule`, `ProviderIdMatchRule`,
  `CandidateSelector`), recorded per-rule `MatchEvidence`, and the canonical
  `MediaMatch` result with a deterministic `MatchFingerprint` over the record
  identity and matching evidence.
- Task 3.3 (`src/ArrTags/Matching/MediaMatchPolicy.cs`): the match status policy
  maps a `CandidateSelection` to the canonical `MediaMatch`. Zero survivors
  become `NotFound`, multiple survivors become `Ambiguous`, and exactly one
  survivor becomes `Matched`. Rejected outcomes carry no record identity and a
  safe, provider-neutral reason and never fall back to title or year; a matched
  outcome records the deciding method, the connection-scoped record identity,
  and the agreeing provider identifier only for a `ProviderId` decision.
- `tests/ArrTags.Tests/MediaMatchPolicyTests.cs` - zero/multiple rejection,
  unique acceptance and evidence, title/year immunity, and `Number` decisions
  without provider identifiers (8 new tests).
- Task 3.4 (`src/ArrTags/Matching/MatchRuleOrder.cs`,
  `src/ArrTags/Matching/MediaMatcher.cs`,
  `src/ArrTags/Matching/MatchProviderIdKeys.cs`,
  `src/ArrTags/Providers/Radarr/RadarrMatchCandidateFactory.cs`,
  `src/ArrTags/Providers/Sonarr/SonarrMatchCandidateFactory.cs`): the documented
  matching order. Movie to Radarr is TMDb then IMDb; Series to Sonarr is TVDB
  then TMDb then IMDb; Episode to Sonarr is the episode TVDB id. `MediaMatcher`
  applies the order and the status policy, enforces the series-before-episode
  rule by scoping episode candidates to the matched series, and produces
  `Unsupported` for cross-provider/structural pairs or episodes without series
  context. The provider factories translate validated Radarr/Sonarr DTOs into
  canonical connection-scoped `MatchCandidate` values. Exact episode-number
  fallback was disabled at the time of task 3.4 and was enabled by task 3.5
  (ADR-007); configured path fallback is deferred out of V1 by ADR-008 (DG-5).
- `tests/ArrTags.Tests/MatchRuleOrderTests.cs`,
  `tests/ArrTags.Tests/MediaMatcherTests.cs`,
  `tests/ArrTags.Tests/MatchCandidateFactoryTests.cs` - rule order, cross-provider
  and structural rejection, TMDb-before-IMDb ordering, IMDb fallback,
  connection-scoped results, series-then-episode scoping, bounded failures, and
  DTO-to-candidate mapping (35 new tests).
- Task 3.5 (`src/ArrTags/Matching/EpisodeNumberingPolicy.cs`,
  `src/ArrTags/Matching/SeasonEpisodeMatchRule.cs`,
  `src/ArrTags/Matching/MatchRuleOrder.cs`): the explicit V1 episode-numbering
  policy resolving DG-4 (ADR-007). Number fallback is enabled after the episode
  TVDB rule and applies only to regular, single episodes: a positive season and
  episode number are required on both sides, season zero specials are excluded,
  and a multi-episode span (`EpisodeNumberEnd > EpisodeNumber`) is excluded.
  Absolute/scene numbering is never an identity key, so absolute-number agreement
  alone never matches. The comparison is exact `(seasonNumber, episodeNumber)`
  equality with no tolerance and never uses title, year, path, or air date;
  ineligible or non-equivalent candidates yield `NotFound`/`Ambiguous` and no
  badge.
- `tests/ArrTags.Tests/EpisodeNumberingPolicyTests.cs` - eligibility, specials,
  spans, missing numbers, item/provider mismatch, exact matching, and bounded
  rejection (13 new tests). `tests/ArrTags.Tests/MediaMatcherTests.cs` - number
  fallback after the series match, series-scoped number fallback, special and
  span exclusion, absolute-number mismatch, and number ambiguity (6 new tests).
- Task 3.7 (`tests/ArrTags.Tests/ConnectionScopingTests.cs`): verification that
  every Arr-local record and file identifier is scoped by its originating
  `ArrConnection`. The suite proves that identical numeric IDs on different
  Radarr connections and on different Sonarr connections produce distinct
  identities, candidates, matches, and fingerprints; that the file identity is
  bounded by its scoped record identity; that `MatchCandidate` and `MediaMatch`
  reject an identity from another connection; that connection scopes are derived
  distinctly from different base URLs; and that the matcher resolves identical
  local IDs to the requested connection for both providers. No production code
  changed: the canonical model already enforces the scoping contract (14 new
  tests).
- Task 3.8 (`src/ArrTags/Media/MediaLocationEligibility.cs`,
  `src/ArrTags/Matching/MediaMatcher.cs`, `src/ArrTags/Media/MediaLocationSummary.cs`,
  `src/ArrTags/Media/MediaIdentityFactory.cs`, `src/ArrTags/Media/MediaEligibility.cs`):
  fail-closed ineligible-location rejection, completing acceptance criterion 3.
  `MediaLocationSummary` now records remote and `.strm` facts in addition to the
  location kind, file protocol, source count, and primary path.
  `MediaLocationEligibility` rejects remote, virtual, offline, unknown, `.strm`,
  and non-local/fileless locations with a bounded, path-free reason, and treats a
  missing location summary as not evaluated so only positively ineligible
  locations are rejected. `MediaMatcher.Match` gates the Movie and Episode badge
  surfaces after the item/provider rule check, and `MediaMatcher.MatchEpisode`
  gates the episode before the parent-series match so ineligible episodes never
  proceed. `MediaEligibility.IsEligible` now also requires an eligible local file
  location. Series and Season remain structural and are not location-gated. No
  path is compared or used as identity (ADR-008), and eligible local-file
  matching is unchanged. `tests/ArrTags.Tests/LocationEligibilityTests.cs`
  covers eligible local Movie/Episode matching, remote/virtual/offline/`.strm`/
  fileless no-badge outcomes, the episode does-not-proceed case, Jellyfin
  location capture, and the combined eligibility gate (13 new tests).

Build and test: 0 warnings, 0 errors; 340 tests pass. DG-5 is resolved by
ADR-008: configured path mapping and normalization are deferred out of V1, so
task 3.6 adds no runtime implementation. Task 3.7 verifies connection scoping,
and task 3.8 verifies fail-closed ineligible-location rejection. The Phase 3 task
list is complete, all Milestone 3 acceptance criteria are satisfied, and Gate 3
is met. Milestone 4 (badge rendering) is next, gated by DG-3.

### Phase 4 (Badge rendering) - complete

**Status:** Complete. Gate 4 met.

**Commit/tag:** tag `v0.1.0-phase4`, at commit
`5629eb98235a20b5535382b70968d3e1bdcd54d1` ("Phase 4 complete: Badge
rendering", 2026-09-19). Tasks 4.1-4.11 landed in commits `a43305d` through
`e5cbf71`; the phase-completion state is recorded in `1b36e18` and the phase
review in `5629eb9`.

Tasks 4.1 through 4.11 are complete, all Milestone 4 acceptance criteria are
satisfied, and Gate 4 is met. DG-3 and ADR-010 are resolved.

- Task 4.1 (`src/ArrTags/Rendering/`): the provider-neutral `BadgeSelector`
  vocabulary (Quality, Resolution, DynamicRange, Source, VideoCodec, Audio,
  CustomBadge, and UpgradePending) with `BadgeSelectorResolver`, `BadgeSelection`,
  and `BadgeValue`. Resolution reads only canonical `BadgeMetadata`, emits
  technical values in the ADR-009 priority order, builds the composite audio value
  from confirmed features then codec then channel count, replaces the generic
  dynamic-range label with `DV` only when Dolby Vision is confirmed, produces one
  candidate per retained custom value, and treats `UpgradePending` as a separate
  `UPGRADE` status only when explicitly true. Unknown and absent values are
  omitted rather than rendered as placeholders or inferred negatives.
  `tests/ArrTags.Tests/BadgeSelectorTests.cs` adds 15 selector tests.
- Task 4.2 (documentation-only): ADR-009 fixes the V1 selector vocabulary, field
  priority, Movie/Episode Primary-poster layout, typography and geometry,
  contrast-validated palette, 24-scalar display limit, PNG/RGB-or-RGBA output,
  source-dimension scaling, high-DPI behavior, and pass-through behavior for
  unknown, incomplete, cancelled, malformed, or failed renders. No rendering code
  is included in this decision closure.
- Task 4.3 (`src/ArrTags/Rendering/BadgeSelectorResolver.cs`,
  `tests/ArrTags.Tests/BadgeUnknownValueTests.cs`): the resolver keeps canonical
  unknown values distinct from confirmed negative values. Tri-state flags
  (Dolby Vision and upgrade-pending) resolve through one explicit
  `ResolveConfirmedTrue` rule so only a confirmed `true` yields a display value
  and neither a confirmed negative nor an unknown value is inferred from the
  other. A confirmed generic dynamic range (for example `SDR`) is displayed while
  an unknown range is omitted; unknown audio features do not suppress a confirmed
  codec and a confirmed empty feature set never infers a feature. The canonical
  fingerprint continues to distinguish unknown from confirmed-negative and
  unknown from confirmed-empty. The new suite adds 11 tests; build and test pass
  with 0 warnings and 366 passing tests.
- Task 4.4 (`src/ArrTags/Rendering/`): the render request and result fingerprints.
  `RenderVersion` owns the code-owned renderer version and the badge schema
  version reference. `RenderOutputPolicy` is the ADR-009/010 output-affecting
  policy (output format, color space, alpha policy, font identity, palette,
  scale policy, and text limits). `RenderFingerprintInput` is a validated,
  immutable snapshot of every output-affecting value: Jellyfin item identity,
  oriented source fingerprint and dimensions, optional metadata fingerprint,
  secret-free configuration fingerprint, ordered resolved `BadgeSelection`,
  output policy, and both versions. `RenderFingerprint` computes the
  result/output fingerprint over the visual inputs and versions (independent of
  item identity) and the request fingerprint (render key) that scopes it to the
  item and poster surface. Every output-affecting value changes the appropriate
  fingerprint; correlation identifiers and timestamps are absent by
  construction. `tests/ArrTags.Tests/RenderFingerprintTests.cs` adds 20 tests.
- Task 4.5 (`src/ArrTags/Rendering/`): the pre-decode/pre-draw/pre-encode limit
  boundary. `RenderLimitGuard` rejects a structurally unusable source descriptor
  and enforces the accepted operational limits before any decode or allocation:
  the exact source byte length against `SourceArtifactLimitBytes` and each
  oriented source side against `MaxImageDimensionPixels`, plus the planned
  derived output surface (per-side dimension bound and the uncompressed RGBA
  surface against `DerivedArtifactLimitBytes`). `BadgeTextNormalizer` enforces
  the ADR-009 text limits from the `RenderOutputPolicy`: non-whitespace control
  scalars are removed, whitespace runs collapse to one space, and labels are
  bounded to 24 Unicode scalar values with end truncation to the first 21
  scalars plus the policy ellipsis. Counting is by Unicode scalar value, not
  UTF-16 character, so supplementary and combining scalars are handled correctly
  and a surrogate pair is never split. `RenderLimitResult` and
  `RenderLimitReason` provide the bounded, non-secret failure/pass-through shape:
  a rejected result carries one safe reason code, never a partial artifact, and
  source bytes are never mutated. `tests/ArrTags.Tests/RenderLimitTests.cs` adds
  39 tests.

- Task 4.7 (`src/ArrTags/ArrTags.csproj`, `src/ArrTags/Rendering/`,
  `src/ArrTags/Resources/`, `licenses/`, `THIRD-PARTY-NOTICES.md`): the pinned
  renderer assets and shipped notices (ADR-010). `SkiaSharp` and
  `SkiaSharp.NativeAssets.Linux` are referenced at the exact Jellyfin 12.0.0 host
  version `3.119.4`; `HarfBuzzSharp` is deliberately not referenced because the
  task 4.8 spike decides whether it is needed. Both `packages.lock.json` files
  were regenerated so locked-mode restore stays valid. The DejaVu Sans Bold 2.37
  font (708,920 bytes, SHA-256
  `5c1247acef7f2b8522a31742c76d6adcb5569bacc0be7ceaa4dc39dd252ce895`) is copied
  into the repository and embedded as the stable logical resource
  `ArrTags.Resources.DejaVuSans-Bold.ttf`. `RenderFontIdentity` records the font
  family, style, version, exact byte length, SHA-256, and resource logical name,
  opens a read-only stream over the exact embedded bytes, and
  `RenderOutputPolicy.Default.FontIdentity` uses the bundled identity;
  `RenderFingerprint` records the identity descriptor, so any changed font field
  changes the output fingerprint. `licenses/DejaVu-Fonts-License.txt`,
  `licenses/SkiaSharp-LICENSE.txt`,
  `licenses/SkiaSharp-THIRD-PARTY-NOTICES.txt`, and `THIRD-PARTY-NOTICES.md` are
  shipped, and the `PackagePlugin` target copies the notices and license files
  into the plugin package. `tests/ArrTags.Tests/BundledFontTests.cs` adds 12
  cases; `RenderFingerprintTests` adds the font-asset-sensitivity case. Only
  notices for components 4.7 actually bundles are included; full native-asset
  plugin-load-context packaging remains the Phase 5 packaging task.

- Task 4.8 (`tests/ArrTags.Tests/SkiaHostCompatibilityTests.cs`,
  `docs/research/skia-host-compatibility.md`, `docs/implementation-readiness.md`):
  the SkiaSharp host-compatibility spike (ADR-010). The pinned Jellyfin 12.0.0
  host's `jellyfin.deps.json` and native files confirm `SkiaSharp`,
  `SkiaSharp.HarfBuzz`, and `SkiaSharp.NativeAssets.Linux` at `3.119.4` plus
  `HarfBuzzSharp` / `HarfBuzzSharp.NativeAssets.Linux` at `8.3.1.5`, with native
  ELF64 x86-64 `libSkiaSharp.so` (11,170,296 bytes, SHA-256
  `66c856ea...26b0cd02`) and `libHarfBuzzSharp.so`; the host's SkiaSharp managed
  and native files are byte-identical to the local NuGet `3.119.4` assets. The
  plugin-load-context behavior was measured against Jellyfin's pinned
  `PluginLoadContext`/`PluginManager` source and on the pinned .NET 10.0.12
  runtime: Jellyfin passes the plugin folder to `AssemblyDependencyResolver`,
  which does not discover a manifest inside it, then loads every DLL in the
  folder into the plugin context. A plugin shipping no SkiaSharp resolves the
  host's shared managed SkiaSharp and native library; a plugin shipping
  `SkiaSharp.dll` uses its own copy, and `libSkiaSharp.so` is found only when it
  sits next to `SkiaSharp.dll` in the plugin folder root. The Phase 5 constraint
  is therefore to ship the managed and root-level native SkiaSharp assets in the
  plugin folder. The guarded `PinnedSkiaRuntimeDecodesDrawsAndEncodesARoundTripPng`
  test decodes a synthetic PNG, draws with the bundled DejaVu Sans Bold font
  from embedded bytes, encodes a non-interlaced 8-bit PNG, and decodes it back on
  the pinned `3.119.4` native library. `HarfBuzzSharp` is confirmed unnecessary
  for ADR-009's single-line bounded labels, so the 4.7 omission stands.
  `PluginDirectoryStyleResolutionDoesNotDiscoverBundledManagedSkiaSharp` is an
  unguarded regression guard for the measured resolver behavior. Evidence,
  commands, and the Phase 5 validation list are in
  `docs/research/skia-host-compatibility.md`.

- Task 4.9 (`src/ArrTags/Rendering/`, `tests/ArrTags.Tests/`): the
  provider-neutral renderer service and drawing engine (ADR-009 and ADR-010).
  The contract adds `SourceImageInput` (immutable, verified-SHA-256 source
  descriptor with content type and oriented dimensions), the minimal
  `BadgeDefinition` snapshot with a code-owned V1 default (all selectors enabled,
  `{value}` technical templates, fixed `UPGRADE` status text),
  `BadgeDefinitionResolver` (wraps `BadgeSelectorResolver` and applies the
  bounded templates), `RenderRequest`, the three-variant `RenderResult`
  (rendered/pass-through/failed) with `RenderStatus`, `RenderPassThroughReason`,
  and `RenderFailureReason`, and the `IRenderer` boundary. `SkiaBadgeRenderer`
  implements the real SkiaSharp decode/draw/encode path: it revalidates the
  bounded request, enforces the source/output limits and the 4.5:1 contrast
  policy before decode, loads the embedded DejaVu Sans Bold bytes with no host
  font fallback, applies EXIF orientation to pixels before layout, packs the
  bottom-left two-row/three-pill rail and the independent top-right status pill
  via the pure `BadgeLayoutEngine`, and encodes a fixed-settings non-interlaced
  8-bit sRGB PNG with RGB for opaque output and straight-alpha RGBA otherwise
  (canonical transparent-pixel RGB). `BadgeGeometry` owns the ADR-009 reference
  geometry and scale, `BadgeContrast`/`RgbColor` own the WCAG validation,
  `SourceOrientation`/`SourceOrientationExtensions` own the pure orientation
  dimensions, `SkiaOrientation` owns the internal pixel transform, and
  `BadgeTextNormalizer.Shorten` adds the layout-stage end-truncation rule. The
  renderer never branches on provider kind, never paints outside the safe area
  (including short posters), checks cancellation at each documented checkpoint,
  never returns a partial artifact, and never mutates the source bytes. New
  tests: `BadgeLayoutTests`, `BadgeContrastTests`, `BadgeDefinitionTests`,
  `RenderResultTests`, `RenderRequestTests`, `SourceOrientationTests`,
  `SkiaBadgeRendererDecisionTests` (unguarded decisions), and the
  environment-guarded `SkiaBadgeRendererRenderTests` (nine real render cases,
  including RGB/RGBA format, source-alpha preservation, palette placement,
  sRGB/metadata chunks, EXIF orientation, unsupported input, source
  immutability, and byte determinism).

Build and test: Task 4.9 adds the renderer service, drawing engine, and tests.
Build and test pass with 0 warnings and 514 passing tests plus 10
environment-guarded Skia skips (524 total); the forced native run with
`ARRTAGS_SKIA_COMPAT=1` and the pinned sysroot on the loader path passes all 524
tests, including the nine task 4.9 render cases and the task 4.8 round trip.
`./build.sh package` produced `artifacts/ArrTags_0.1.0.0.zip` with the same
contents as 4.7. Tasks 4.10 and 4.11 remain. Gate 4 is not yet met.

- Task 4.6 (`tests/ArrTags.Tests/RendererBehavior*.cs`): the renderer behavior
  matrix for dimensions, format, truncation, layout, cancellation, and failure
  pass-through, added as test-only work with no production change. Seven files
  add 51 cases (40 unguarded and 11 environment-guarded real render cases):
  `RendererBehaviorFixtures`, `RendererBehaviorDimensionTests`,
  `RendererBehaviorFormatTests`, `RendererBehaviorTruncationTests`,
  `RendererBehaviorLayoutTests`, `RendererBehaviorCancellationTests`, and
  `RendererBehaviorFailureTests`. The unguarded cases assert the
  `clamp(width / 1000, 0.5, 4.0)` geometry scale, the 24-scalar truncation and
  width-fitting rules, the two-row/three-pill rail, safe-area bounds on narrow
  and short posters, the confirmed-true-only top-right `UPGRADE` pill,
  cancellation precedence and its bounded result, and every pre-decode
  pass-through/failure decision (missing metadata, no displayable value,
  ineligible surface, non-matched result, unavailable source, source-byte and
  source-dimension and derived-output limits, low contrast, invalid color,
  missing font resource). The guarded cases assert real decode/encode source
  dimensions for opaque, alpha, and EXIF orientation-3/8 sources, structural PNG
  format behavior (non-interlaced 8-bit RGB versus RGBA, straight semi-transparent
  alpha, canonical transparent-pixel RGB, `sRGB` with no retained `iCCP`/`eXIf`/
  `tIME`/`tEXt`/`zTXt`/`iTXt`), and the malformed/unsupported decode failures.
  The renderer's later cancellation checkpoints are not independently reachable
  through the public synchronous contract without production-only test hooks; the
  earliest checkpoint and its precedence are covered and the limitation is
  recorded.

Build and test: Task 4.6 adds only tests. The build passes with 0 warnings and
the default test run passes 554 tests plus 21 environment-guarded Skia skips (575
total); the forced native run with `ARRTAGS_SKIA_COMPAT=1` and the pinned sysroot
on the loader path passes all 575 tests. Tasks 4.10 and 4.11 remain; Gate 4 is
not yet met.

- Task 4.10 (`src/ArrTags/Configuration/`, `tests/ArrTags.Tests/`): the persisted
  renderer configuration, its validation, snapshot mapping, and the secret-free
  renderer configuration fingerprint (ADR-010). `RendererConfiguration` and
  `BadgeSelectorConfiguration` add only the user-adjustable V1 surface: the
  enabled/disabled V1 `BadgeSelector` set with one bounded provider-neutral
  `{value}` template per selector, plus four optional palette overrides for the
  technical and upgrade-status background/text colors. Output format, color
  space, alpha policy, font identity, geometry/reference values, text limits, and
  the renderer version are not representable in configuration and remain
  code-owned per ADR-010; no geometry or placement value is exposed because
  ADR-009 fixes them and ADR-010 does not authorize an override. Defaults
  reproduce ADR-009 exactly: an absent selector keeps the code-owned
  `BadgeDefinition.V1Default` entry, and an unset palette keeps the ADR-009
  colors. `RendererConfiguration.Validate`, invoked by
  `PluginConfigurationValidator`, rejects unknown/duplicate selectors,
  empty/too-long templates or more than one `{value}` placeholder, malformed
  colors, and any style pair below the 4.5:1 contrast minimum with safe,
  secret-free messages. `RendererConfigurationResolver` maps a configuration to
  the ordered `BadgeDefinition` snapshot and the effective `RenderOutputPolicy`
  (configured palette applied, all other values copied from
  `RenderOutputPolicy.Default`); `RendererPalette` canonicalizes a valid override
  to `#RRGGBB`. `PluginConfigurationSnapshot` exposes the validated
  `BadgeDefinitions`, `RendererOutputPolicy`, and
  `RendererConfigurationFingerprint` while remaining immutable and secret-free,
  and `From` keeps working for plain configurations.
  `RendererConfigurationFingerprint.Compute` is a deterministic uppercase SHA-256
  over the renderer configuration schema version, the ordered selector
  enablement/templates, and the effective palette; it excludes credentials, the
  webhook secret, timestamps, and correlation identifiers, normalizes equivalent
  color casing and selector entry order, and is the value supplied to
  `RenderRequest.ConfigurationFingerprint`. The ADR-005 secret boundary is
  unchanged, and `ConfigurationSnapshotService.TryReplace` retains the last valid
  snapshot and private secrets when an invalid candidate is rejected.
  `tests/ArrTags.Tests/RendererConfigurationTests.cs` adds 21 unguarded cases
  (no Skia native runtime required) covering the default mapping, valid/invalid
  selectors and templates, palette acceptance/canonicalization and malformed
  colors, the 4.5:1 contrast boundary, snapshot immutability and secret
  exclusion, fingerprint determinism and sensitivity to every output-affecting
  value, XML persistence round-trip, and last-valid plus private-secret
  retention through `TryReplace`. The configuration is not wired to the Jellyfin
  admin save surface, DI, providers, or the artwork pipeline.

Build and test: Task 4.10 adds the renderer configuration model, snapshot
mapping, fingerprint, and tests. The build passes with 0 warnings and 0 errors;
the default test run passes 575 tests plus 21 environment-guarded Skia skips (596
total), and the forced native run with `ARRTAGS_SKIA_COMPAT=1` and the pinned
sysroot on the loader path passes all 596 tests.

- Task 4.11 (`src/ArrTags/Rendering/SourceColorProfile.cs`,
  `SourceColorProfileKind.cs`, `RenderFailureReason.cs`, `SkiaBadgeRenderer.cs`,
  `tests/ArrTags.Tests/`): the ADR-010 golden-image, byte-determinism,
  PNG-contract, and cross-runtime tolerance test oracle, together with the F2
  supporting production change deferred from task 4.9 and the authorized task
  4.11 EXIF orientation correctness fix. Complete.

  The F2 change adds `SourceColorProfile`/`SourceColorProfileKind` and the safe
  `RenderFailureReason.UnsupportedColorProfile`. The renderer inspects a
  recognized PNG `iCCP` or JPEG `APP2` embedded profile before decode: an input
  without a profile is treated as sRGB, a profile that `SKColorSpace.CreateIcc`
  parses is converted to sRGB by the existing sRGB decode destination, and a
  malformed or unsupported profile fails closed with no artifact. `RenderVersion`
  was not changed because a supported or absent profile renders byte-identical
  output and an invalid profile now produces no artifact instead of a different
  valid one.

  The golden oracle (`tests/ArrTags.Tests/Goldens/`) holds nine committed
  synthetic PNGs plus a manifest, covering an opaque JPEG-like source, an RGB PNG
  source, an RGBA source, orientation, every V1 field, long custom values,
  missing fields, full rail capacity, and the upgrade status. `RenderGoldenTests`
  compares decoded pixel planes (RGB/RGBA in channel order), dimensions, alpha
  behavior, the encoded bytes, the recorded SHA-256, and the deterministic output
  fingerprint with the exact comparator;
  `CommittedGoldenManifestIsCompleteAndSelfConsistent` validates the committed
  files' IHDR and hashes without the native renderer. There is no writer,
  auto-update, or auto-approval path, and both a substituted golden PNG and a
  corrupted manifest fingerprint were verified to fail the suite.
  `RenderGoldenFixtures`/`RenderImageFixtures` build all synthetic sources,
  including a deterministic minimal RGB ICC profile, one- and two-marker oriented
  JPEGs, and a metadata-bearing PNG (`tEXt`/`tIME`/`eXIf`).

  `RenderDeterminismTests` and `RenderDeterminismProcessProbe` prove identical
  decoded pixels, output hash, and PNG bytes across repeated renders, a changed
  item identity, a changed observation timestamp, different source stream chunk
  sizes, and a separate fresh `dotnet test` process that renders the canonical
  fixture and writes its artifact for byte comparison.

  `RenderPngContractTests` asserts non-interlaced 8-bit RGB/RGBA output, the
  fixed `sRGB` declaration, stripped `iCCP`/`eXIf`/`tIME`/`tEXt`/`zTXt`/`iTXt`
  metadata, canonical transparent-pixel RGB, straight source-alpha preservation,
  and rejection of malformed and unsupported embedded profiles, with a
  supported-profile conversion case.

  `CrossRuntimePixelComparator` implements the ADR-010 rule: dimensions, channel
  count, alpha, and any non-anti-aliased pixel must match exactly, and only
  one-step per-channel differences within 0.1 percent of pixels are tolerated.
  `CrossRuntimePixelComparatorTests` unit-tests the pass/fail/boundary behavior
  unguarded, and `RendererCrossRuntimeTests` runs the canonical exact half
  against every golden. The comparison is data-driven for a future
  `Goldens/non-canonical/` set; because only the pinned canonical runtime exists
  in this environment the tolerant half is a recorded environment limitation, not
  a fabricated result, and `NonCanonicalRuntimeFactAttribute` skips it until the
  second runtime's golden set is supplied in the testing/release milestone.
  `SkiaNativeTheoryAttribute` extends the established `ARRTAGS_SKIA_COMPAT=1`
  guard to the parameterized fixtures; with the flag set and no pinned runtime
  the guarded cases fail rather than skip.

  The authorized task 4.11 correctness fix corrects a genuine pre-existing
  renderer defect in `SkiaOrientation.Apply`: for the dimension-swapping EXIF
  orientations the translation origin used the oriented `height`/`width` instead
  of the source dimensions, so an opaque 500x750 JPEG with EXIF orientation 6
  rendered with 125,000 fully transparent pixels (one third of the 750x500
  output) and clipped content, orientation 7 left 250,000 transparent and lost
  the corner marker, and orientation 8 left 187,500 transparent. The transforms
  now translate about `source.Height`/`source.Width`.
  `RenderOrientationTests` proves all eight orientations are fully opaque with no
  introduced transparency and place the two corner markers in the expected
  quadrants, and the committed `orientation` golden now encodes the corrected
  dimension-swapping orientation 6. Because this is an output-affecting drawing
  change, `RenderVersion.CurrentRendererVersion` advanced from 1 to 2 and the
  committed golden manifest was regenerated; the badge schema version is
  unchanged and the eight non-orientation fixtures stay byte-identical while all
  output fingerprints advance with the renderer version.

Build and test: Task 4.11 adds the test oracle, the F2 production change, and the
orientation correctness fix and version bump. The build passes with 0 warnings
and 0 errors. The default test run passes 593 tests plus 40 environment-guarded
skips (633 total), and the forced native run with `ARRTAGS_SKIA_COMPAT=1` and the
pinned sysroot on the loader path passes 655 tests with one non-canonical-golden
skip (656 total). `./build.sh package` produced
`artifacts/ArrTags_0.1.0.0.zip`. Task 4.11 is complete; the only remaining Phase
4 validation gap is the non-canonical cross-runtime runtime selection, which is
deferred to the testing/release milestone.

Phase 4 is complete: all Milestone 4 acceptance criteria are satisfied and Gate
4 is met. The forced native renderer run with `ARRTAGS_SKIA_COMPAT=1` and the
pinned sysroot passes 655 tests with one deferred non-canonical cross-runtime
skip (656 total); the default `./build.sh test` run passes 593 tests with 40
environment-guarded skips. The renderer produces deterministic, bounded,
non-interlaced 8-bit sRGB PNG output (RGB for opaque sources, straight-alpha
RGBA otherwise), never claims a value from missing provider data, never modifies
source bytes, and returns a bounded non-secret failure/pass-through result on
every failure path. Phase 5 (Jellyfin artwork integration) had not started at
that point and required explicit user approval to begin.

## Phase 5 - Jellyfin artwork integration (Milestone 5)

**Status:** In progress. Tasks 5.1, 5.2, 5.3, 5.4, 5.6, 5.5, 5.7, and 5.8 complete; tasks 5.9 through 5.11 not started.

### Task 5.1 - Jellyfin item-image publication ABI and route confirmation

Task 5.1 confirms the exact supported Jellyfin 12.0.0 item-image publication and
read ABI, the standard `ImageController` route variants, the read/write
authorization split, and Jellyfin's ownership of image tags, caching, and
resizing. The findings are recorded in
`docs/research/jellyfin-12-architecture.md` section 4.4, with the route table in
section 3.1 and the response/authorization behavior in section 3.3. This
confirms an ABI; it does not reopen ADR-001, ADR-002, ADR-003, ADR-006, or
ADR-009.

- Publication surface (confirmed from the pinned `12.0.0`
  `MediaBrowser.Controller.dll` and its XML docs): the stream overload
  `IProviderManager.SaveImage(BaseItem item, Stream source, string mimeType,
  ImageType type, int? imageIndex, CancellationToken cancellationToken)`, plus
  the URL (`string url`) and filesystem-path (`string source, string mimeType,
  ..., bool? saveLocallyWithMedia`) overloads. `mimeType` selects the file
  extension, `imageIndex` defaults to `0` for single-image surfaces, and the
  caller must still run the item update flow.
- Read/information surface (confirmed): `BaseItem.GetImageInfo(ImageType, int)`
  returning `MediaBrowser.Controller.Entities.ItemImageInfo`, `BaseItem.ImageInfos`,
  `MediaBrowser.Model.Dto.ImageInfo` (API DTO with `ImageTag`/`Width`/`Height`/
  `Size`), `IImageProcessor.GetImageCacheTag(BaseItem, ItemImageInfo)` (MD5 of
  item path plus image modified ticks in 12.0.0; a cache validator, not a
  content hash), `IImageProcessor.GetImageDimensions`, and
  `ILibraryManager.UpdateImagesAsync`/`ConvertImageToLocal`/`GetItemById<T>`.
- Repository update (confirmed): `BaseItem.UpdateToRepositoryAsync(ItemUpdateType,
  CancellationToken)` with `ItemUpdateType.ImageUpdate = 4`.
- Route variants (confirmed from the pinned host `Jellyfin.Api.dll` and its
  live OpenAPI document): `Items/{itemId}/Images/{imageType}` (GET/HEAD,
  `GetItemImage`/`HeadItemImage`), `Items/{itemId}/Images/{imageType}/{imageIndex}`
  (GET/HEAD), `Items/{itemId}/Images/{imageType}/{imageIndex}/{tag}/{format}/{maxWidth}/{maxHeight}/{percentPlayed}/{unplayedCount}`
  (GET/HEAD, `GetItemImage2`), `Items/{itemId}/Images` (GET,
  `GetItemImageInfos`), and the POST/DELETE write routes
  (`SetItemImage`/`SetItemImageByIndex`/`DeleteItemImage`/`DeleteItemImageByIndex`/`UpdateItemImageIndex`).
- Delivery behavior (confirmed from the pinned source/artifact): a `tag` query
  parameter yields a quoted ETag and `Cache-Control: public, max-age=31536000,
  immutable` with `If-None-Match` `304` handling; without a tag `Cache-Control:
  public` and `If-Modified-Since` apply; `ImageHelper.GetNewImageSize` never
  upscales; the sealed `Jellyfin.Drawing.ImageProcessor` owns the
  `resized-images` cache.
- Authorization (route/status observations live-confirmed; pinned OpenAPI shows
  `security=null` for the GET/HEAD item-image operations): the GET/HEAD
  item-image read actions carry no `[Authorize]` attribute. They resolve the
  item through `_libraryManager.GetItemById<BaseItem>(itemId, User.GetUserId())`;
  for an anonymous request `ClaimsPrincipalExtensions.GetUserId()` returns
  `default(Guid)`, which maps to a null user, and `ItemIsVisible(item, null)`
  returns true for any non-null item. A known item's image is therefore served
  anonymously (HTTP 200); only an unknown item id (or an item with no image of
  the requested type) yields `404`. The item-image information action and all
  write actions carry `[Authorize]`/`[Authorize(Policy = RequiresElevation)]`.
  Later Phase 5 tasks must not assume a `401` from the read image route and must
  not treat it as an authorization boundary.
- Selected V1 surfaces: the unindexed `Primary` poster for Movie and Episode
  only (ADR-006/ADR-009); indexed and alternate poster surfaces are out of V1.

Automated confirmation: `tests/ArrTags.Tests/JellyfinImageAbiTests.cs` (11
unguarded cases reflecting the pinned NuGet assemblies) and
`tests/ArrTags.Tests/JellyfinImageRouteTests.cs` (5 cases guarded by
`ARRTAGS_JELLYFIN_HOST_DIR`, reflecting the pinned host `Jellyfin.Api.dll`).
The guarded route confirmation passes 5/5 against
`/tmp/opencode/jf/jellyfin`. Still needing live-host validation (not ABI
uncertainty): the exact on-disk representation `SaveImage` produces and the
post-publication read-back content hash, multi-RID packaging (task 5.4), and
end-to-end standard-route delivery (task 5.11).

Build and test: `./build.sh restore`, `./build.sh build` (0 warnings, 0 errors),
and `./build.sh test` pass. The default suite passes 604 tests with 45
environment-guarded skips (649 total). The forced native run with
`ARRTAGS_SKIA_COMPAT=1` and the pinned sysroot passes 666 with 6 skips (672
total: the five task 5.1 route cases and the non-canonical golden placeholder);
adding `ARRTAGS_JELLYFIN_HOST_DIR` passes 671 with only the non-canonical golden
skip. The guarded task 5.1 route cases pass 5/5 when
`ARRTAGS_JELLYFIN_HOST_DIR` points at the pinned host.

### Task 5.2 - Source-artwork provenance and guarded restoration state

Task 5.2 implements the provider-neutral, plugin-owned source-artwork provenance
and guarded restoration state required before any derived artwork is published.
It follows `docs/data-model.md` sections 3.10.1 and 3.10.2 and ADR-002. It does
not implement the `ArtworkOperation` write-ahead journal (5.6), the Jellyfin host
source adapter (5.3), publication (5.5), reconciliation (5.7), or lifecycle
fencing (5.9), and it performs no Jellyfin image mutation.

- `src/ArrTags/Artwork/ArtworkImageSurface` is the canonical image-surface
  identity. The V1 surface is the unindexed `Primary` poster; the optional index
  is retained for the data model, and the state invariant rejects an indexed
  surface as out of V1 scope. No path is ever part of an identity.
- `ActiveImageIdentity` is the observable identity: surface, explicit
  `Present`/`Absent` presence, content SHA-256, byte length, width/height,
  modification time, and the Jellyfin image tag. A present identity may lack a
  hash (an incomplete observation), but it can never prove ownership.
- `ArtworkOwnershipComparer` applies the fail-closed rule. Surface and presence
  must match; a present comparison requires matching content hashes; every
  recorded Jellyfin value must still match when observable. A missing hash, a
  missing observation, or a mismatched surface is `Unknown`; a content, presence,
  or supporting-value difference is `Changed`; only a full match is `Owned`.
- `PublishedArtworkState` carries the `modelVersion`, `jellyfinItemId`,
  `imageSurface`, `state`, `sourcePresence`, `sourceArtifactId`,
  `sourceFingerprint`, `sourceCaptureIdentity`, `ownershipToken`,
  `publicationToken`, `activeImageIdentity`, `publishedFingerprint`,
  `rendererVersion`, `lastOwnershipObservation`, `stateRevision`,
  `lastOperationId`, and `updatedAt` fields. `Validate` enforces a complete
  publication set for `Published`/`RestorePending`, a present active identity
  with a content hash, a retained source artifact plus fingerprint for a present
  baseline (and forbids one for an absent baseline), well-formed bounded opaque
  tokens, a non-indexed surface, and model-version compatibility.
- `PublishedArtworkStateTransitions` is the pure, testable state machine for
  every section 3.10.2 transition: new-session capture, repeated publication that
  reuses the source artifact and ownership token while issuing a new publication
  token and active identity, `OwnershipLost` on mismatch, `OwnershipUnknown` on
  an unobservable identity, `RestorePending` on disable/uninstall,
  `RestoreSource`/`RemoveActiveImage` only after a fresh match and source
  integrity check, `Restored` only after a verified restoration,
  `RestoreBlocked` on a missing/corrupt source or unverifiable result, and
  `Removed` on item removal with no image mutation. A blocked record
  (`OwnershipLost`, `OwnershipUnknown`, `RestoreBlocked`) is never automatically
  re-baselined.
- `SourceArtifactStore` is the authoritative content-addressed immutable store.
  It retains the exact source bytes with MIME type, byte length, and SHA-256
  metadata; validates bounded size, MIME/magic-byte format, and hash; promotes
  atomically from a flushed temporary file; verifies integrity on read; is
  traversal-safe; rejects new work when the authoritative storage quota would be
  exceeded; and persists its manifest through the authoritative state boundary so
  provenance is never evicted as ordinary cache. An absent baseline is an
  explicit state and creates no artifact.
- `PublishedArtworkStateStore` persists the state through the existing versioned,
  integrity-tagged, atomically-written boundary. It enforces the invariants on
  write and quarantines a valid-envelope-but-invalid record rather than returning
  or replaying it. `StateRepository` gained a read-only `Limits` accessor.
- Tests: `tests/ArrTags.Tests/ArtworkProvenanceTests.cs`,
  `SourceArtifactStoreTests.cs`, and `PublishedArtworkStateStoreTests.cs`
  (75 cases) cover the comparison match/mismatch/unknown, surface/presence, and
  missing-hash rules; the state invariants; every documented transition; artifact
  round-trip/integrity/traversal/atomic-promotion/quota/no-cache-eviction
  behavior; and authoritative persistence and quarantine.

Build and test: `./build.sh restore`, `./build.sh build` (0 warnings, 0 errors),
and `./build.sh test` pass. The default suite passes 679 tests with 45
environment-guarded skips (724 total). No ADR, `RenderVersion`, renderer
behavior, or existing passing behavior was changed.

### Task 5.3 - Jellyfin host source adapter

Task 5.3 implements the plugin-owned Jellyfin host source adapter that reads the
unindexed `Primary` source image and supplies the Phase 4 renderer's
`SourceImageInput`, keeping Jellyfin access out of the renderer per ADR-010. It
does not implement packaging (5.4), publication (5.5), the `ArtworkOperation`
journal (5.6), reconciliation (5.7), or lifecycle fencing (5.9).

- `src/ArrTags/Artwork/IArtworkSourceReader`/`ArtworkSourceReader` is the
  host-neutral boundary and core. It enforces the V1 unindexed `Primary` surface,
  the `OperationalLimits` source byte and decoded dimension bounds, and the
  container confinement, and maps every failure to a bounded
  `ArtworkSourceReadResult` without throwing.
- `ArtworkSourceReadResult` carries presence, the exact bounded source bytes, the
  confined content type, byte length, content SHA-256, the post-orientation
  display dimensions, and the Jellyfin identity fields, and constructs a valid
  `SourceImageInput` and the task 5.2 `ActiveImageIdentity` from one read. It
  contains no path, Jellyfin entity, provider DTO, credential, or mutable image
  object.
- `IArtworkImageAccess` is the injectable host seam (bounded bytes plus the
  Jellyfin observation fields). `JellyfinArtworkImageAccess` is the only
  `MediaBrowser.*` file: it resolves the item through `ILibraryManager`, reads
  `BaseItem.GetImageInfo(ImageType.Primary, 0)`, converts a non-local image with
  `ILibraryManager.ConvertImageToLocal` (`removeOnFailure: false`) when
  `IsLocalFile` is false, reads a bounded byte copy, and observes the image tag
  and modification time.
- Oriented dimensions: Jellyfin's `IImageProcessor.GetImageDimensions` and the
  pinned `SkiaEncoder.GetImageSize` return `SKCodec.Info`, the pre-EXIF
  encoded dimensions. `SourceImageDescriptor` reads `SKCodec.Info` and
  `SKCodec.EncodedOrigin` with the pinned SkiaSharp 3.119.4 stack and
  `SourceOrientationExtensions` computes the display dimensions, matching the
  renderer's own `EncodedOrigin` validation. Undecodable headers fail closed.
- Container confinement closes the carried-forward Phase 4 MEDIUM finding: the
  renderer's `SourceColorProfile.Inspect` understands only PNG `iCCP` and JPEG
  `APP2` profiles, so `ArtworkSourceContentType` accepts only PNG and JPEG
  signatures and GIF, WebP, BMP, AVIF, and unrecognized bytes fail closed. The
  renderer's `SourceColorProfile` is unchanged.
- Both the source reader and the Jellyfin access are registered as singleton
  factories in `ArrTagsServiceRegistrator`.
- Tests: `tests/ArrTags.Tests/ArtworkSourceReaderTests.cs`,
  `ArtworkSourceContentTypeTests.cs`, `JellyfinArtworkImageAccessTests.cs`, and
  `SourceImageDescriptorTests.cs` (65 cases, four native-guarded) cover present,
  absent, unsupported-surface, unsupported-container, oversized-byte,
  oversized-dimension, unreadable, hash/length/surface correctness, oriented
  dimensions, supported non-local conversion, boundary neutrality, and DI
  registration.

Build and test: `./build.sh restore`, `./build.sh build` (0 warnings, 0 errors),
and `./build.sh test` pass. The default suite passes 740 tests with 49
environment-guarded skips (789 total). The forced native run with
`ARRTAGS_SKIA_COMPAT=1` and the pinned sysroot passes 813 with 6 skips (819
total). No ADR, `RenderVersion`, renderer behavior, or existing passing behavior
was changed.

### Task 5.4 - Renderer managed and Linux native packaging

Task 5.4 extends the plugin package so the renderer's managed binding, its
matching Linux native asset, the plugin dependency manifest, and the Skia/font
license notices are included and resolve under the host's plugin load context
(ADR-010). It does not implement publication (5.5), the `ArtworkOperation`
journal (5.6), reconciliation (5.7), or lifecycle fencing (5.9).

- `src/ArrTags/ArrTags.csproj`: the `PackagePlugin` target derives the managed
  `SkiaSharp.dll` from `@(RuntimeCopyLocalItems)` and the matching
  `linux-x64` `libSkiaSharp.so` from `@(RuntimeTargetsCopyLocalItems)` (filtered
  by the declared `PluginRuntimeIdentifier`), so the assets come from the
  project's MSBuild-resolved pinned references rather than a hard-coded NuGet
  cache path. Both are staged at the plugin folder root next to `ArrTags.dll`,
  and the build fails when either cannot be resolved. The target also stages
  `ArrTags.deps.json`, `build.yaml`, `THIRD-PARTY-NOTICES.md`, and the
  `licenses/` notices, and deletes any previous archive before zipping so the
  target is idempotent.
- V1 claims only `linux-x64`. The measured Jellyfin 12 plugin load context
  probes only the plugin folder root, not `x64/` or `runtimes/<rid>/native/`
  (`docs/research/skia-host-compatibility.md` section 3), so the package carries
  that single RID's native asset at the root and never loads an arbitrary system
  Skia library. Multi-RID packaging is not part of V1.
- `build.yaml`: `artifacts` now lists `ArrTags.dll`, `SkiaSharp.dll`,
  `libSkiaSharp.so`, and `ArrTags.deps.json`. `assemblies` is left empty so
  Jellyfin's folder scan loads the bundled `SkiaSharp.dll` into the plugin load
  context. The identity, `targetAbi: 12.0.0.0`, and `framework: net10.0` are
  unchanged.
- The produced `artifacts/ArrTags_0.1.0.0.zip` contains, at the root:
  `ArrTags.dll` (940,544 bytes, SHA-256 `6caa3914...`), `SkiaSharp.dll`
  (489,824 bytes, SHA-256 `aaaaa18c68ba1f3a3408b00dff28b11d5705198e17ba9d3aa59222bfd35407c8`),
  `libSkiaSharp.so` (11,170,296 bytes, SHA-256
  `66c856eaf1a47a00b23204c30c6ee407987bf5086ecc0a1a6b4fd67526b0cd02`),
  `ArrTags.deps.json`, `build.yaml`, `THIRD-PARTY-NOTICES.md`, and the three
  `licenses/` notices.
- Tests: `tests/ArrTags.Tests/PluginPackagingTests.cs` adds five unguarded
  packaging-contract facts (`build.yaml` artifacts and identity, the
  `PackagePlugin` target's resolved-asset and RID contract, and the shipped
  notice/license files) and three package-content facts guarded on the presence
  of `artifacts/ArrTags_*.zip` (`build.yaml` artifacts present in the archive,
  the required root files, and the pinned managed/native hashes with an ELF64
  x86-64 header check on the native asset).
- Live host: the package was installed on the pinned Jellyfin 12.0.0 host, which
  logged `Loaded assembly "SkiaSharp, Version=3.119.0.0, ..." from
  ".../ArrTags_0.1.0.0/SkiaSharp.dll"`, `Loaded plugin: "ArrTags" "0.1.0.0"`,
  completed startup with no errors, mapped `ArrTags.dll` and `SkiaSharp.dll`
  from the plugin folder in `/proc/<pid>/maps`, and wrote `meta.json` with
  `targetAbi: 12.0.0.0` and `status: Active`. A byte-for-byte replica of
  Jellyfin's `PluginLoadContext` run over the exact extracted package, with the
  host's managed and native SkiaSharp preloaded, bound the plugin-context
  `SkiaSharp` to the plugin folder, completed a native decode/draw/encode call,
  and mapped the plugin-folder `libSkiaSharp.so` as a distinct second copy.
  A full image render through Jellyfin is not wired until tasks 5.5/5.11.

Build and test: `./build.sh restore`, `./build.sh build` (0 warnings, 0 errors),
and `./build.sh test` pass. The default suite passes 745 with 52
environment-guarded skips (797 total; 748 passed with 49 skips when the package
is present). The forced native run with `ARRTAGS_SKIA_COMPAT=1` and the pinned
sysroot passes 821 with 6 skips (827 total). No ADR, `RenderVersion`, renderer
behavior, or existing passing behavior was changed.

### Task 5.6 - Durable ArtworkOperation write-ahead record and store

Task 5.6 implements the durable provider-neutral `ArtworkOperation` write-ahead
record, its phase and lifecycle-fence rules, and its generation-fenced
authoritative store (ADR-003; `docs/data-model.md` sections 3.10.3 and 3.10.4;
`docs/architecture.md` section 9). It does not call `SaveImage`, run
reconciliation (5.7), or wire lifecycle events (5.9); task 5.5 drives the
write-before-mutation ordering.

- `src/ArrTags/Artwork/ArtworkOperationKind.cs`: `Publication` or `Restoration`,
  selecting the target final state and artifact use.
- `src/ArrTags/Artwork/ArtworkLifecycleFence.cs`: `Normal`, `Disable`,
  `Uninstall`, or `ItemRemoved`. The fence is modeled and evaluated here; the
  lifecycle event wiring that raises it belongs to task 5.9.
- `src/ArrTags/Artwork/ArtworkOperationPhase.cs`: the eight data-model phases
  (`Prepared`, `MutationStarted`, `RepositoryUpdateStarted`,
  `VerificationPending`, `FinalizationPending`, `Committed`, `Aborted`,
  `RecoveryBlocked`), documented as a durable lower-bound marker.
- `src/ArrTags/Artwork/ArtworkOperationPhases.cs`: the pure phase rules.
  `CanAdvance`/`TryAdvance` permit exactly one forward step along the documented
  order or a terminal outcome from any non-terminal phase, refuse backward and
  same-phase moves, and refuse to advance out of `Committed`, `Aborted`, or
  `RecoveryBlocked`. `IsTerminal` and the fail-safe `MayHaveStartedMutation`
  (only `Prepared` proves no mutation was started) encode the lower-bound
  semantics.
- `src/ArrTags/Artwork/ArtworkOperationFencing.cs`: the pure decisions.
  `AllowsNewPublication` is true only for `Normal`; `AllowsNewRestoration` is
  true except for `ItemRemoved`; `IsStale`, `CanSupersede`, and
  `IsSameGeneration` classify generations.
- `src/ArrTags/Artwork/ArtworkOperationErrors.cs`: redacts control characters
  and bounds the optional `lastError` diagnostic to 512 characters.
- `src/ArrTags/Artwork/ArtworkOperation.cs`: the model. It carries every
  data-model 3.10.3 field plus explicit `sourcePresence` and
  `candidateAfterPresence` fields, so the conditional rules are enforceable: an
  operation requires a valid item id, an unindexed surface, and a well-formed
  opaque operation id; a publication requires a present after target, a
  publication token, and a derived artifact; a restoration must target the same
  presence as its source baseline and, for a present source, the retained source
  content, and cannot carry a publication token or derived artifact; a present
  source requires a content-addressed source artifact; a present after target
  requires its SHA-256 content hash; generation and attempt are bounded; tokens
  and artifact references are opaque/bounded with no path or credential field.
  The record never contains credentials, media paths, raw external payloads, or
  artifact bytes.
- `src/ArrTags/Artwork/ArtworkOperationStore.cs`: persists the record through the
  existing versioned, integrity-tagged, atomically-written `StateRepository` /
  `StateAuthority.Authoritative` boundary, keyed per item/image surface so only
  one operation can exist for a subject at a time. It validates on write,
  quarantines a valid-envelope-but-invalid payload rather than replaying it,
  fences writes by the monotonic generation (a stale generation cannot overwrite
  a newer durable record; a newer generation supersedes; the same generation may
  only advance the same operation through a legal phase), and marks only terminal
  phases (`Committed`, `Aborted`, or `RecoveryBlocked`) eligible for
  terminal-provenance retention so a non-terminal operation is never pruned. A
  tombstoned item-removal operation is terminal because its phase is `Aborted`,
  not because of its lifecycle fence.
- `docs/data-model.md` section 3.10.3 records the two explicit presence fields.
- Tests: `tests/ArrTags.Tests/ArtworkOperationTests.cs` and
  `tests/ArrTags.Tests/ArtworkOperationStoreTests.cs` (101 cases with the shared
  `ArtworkOperationFixtures`) cover every conditional requirement,
  absent-versus-present after target, token bounds, `lastError` redaction, the
  phase enum and every legal/illegal transition, the fence decisions, durability
  across a reconstructed `StateRepository`, generation fencing,
  one-non-terminal-per-subject, authoritative quarantine on a corrupt or
  semantically invalid record, and terminal-versus-non-terminal retention.

Build and test: `./build.sh restore`, `./build.sh build` (0 warnings, 0 errors),
and `./build.sh test` pass. The default suite passes 849 with 49
environment-guarded skips (898 total), exactly +101 over the task 5.4 baseline
(748/49/797 with the package present), with no regressions. No ADR,
`RenderVersion`, renderer behavior, or existing passing behavior was changed.

### Task 5.5 - Publish completed artwork through the supported item-image APIs

Task 5.5 implements the provider-neutral single-subject publication
orchestration and the single Jellyfin image-mutation implementation in
`src/ArrTags/Artwork` (ADR-002/ADR-003; `docs/architecture.md` section 9 steps
4-10). It consumes the task 5.2 provenance state and source-artifact store, the
task 5.3 host source adapter, and the task 5.6 durable `ArtworkOperation`
journal. It does not implement the task 5.7 recovery decision table or the task
5.9 lifecycle fencing.

- `src/ArrTags/Artwork/ArtworkImageMutationResult.cs`: the bounded outcome of one
  supported image-save or item-update call (`Succeeded`, `ItemNotFound`,
  `UnsupportedSurface`, `InvalidContent`, `Failed`), with a redacted/bounded
  reason and no path or entity.
- `src/ArrTags/Artwork/IArtworkImageWriter.cs`: the injectable host-neutral
  mutation boundary, mirroring `IArtworkImageAccess`. It exposes the image save
  and the normal item update as separate calls so the caller can persist the
  intervening durable phases.
- `src/ArrTags/Artwork/JellyfinArtworkImageWriter.cs`: the single Jellyfin
  12.0.0 implementation. It uses the supported
  `IProviderManager.SaveImage(BaseItem, Stream, string, ImageType, int?,
  CancellationToken)` stream overload with the durable derived bytes and
  `image/png`, then `BaseItem.UpdateToRepositoryAsync(ItemUpdateType.ImageUpdate,
  ...)`, matching the standard item-image controller. It never writes a
  media-folder poster, Jellyfin's image cache, or an item-image path directly and
  never uses the filesystem-path overload (which deletes its source) or a URL
  overload. All `MediaBrowser.*` references for publication are confined here.
- `src/ArrTags/Artwork/ArtworkPublicationRequest.cs`,
  `ArtworkPublicationOutcome.cs`, and `ArtworkPublicationResult.cs`: the bounded
  single-subject request and result. The request carries only the item, surface,
  and completed render result, so a caller cannot inject a derived image as a new
  source.
- `src/ArrTags/Artwork/ArtworkPublisher.cs`: the host-neutral orchestration. It
  serializes per item/surface, reads the authoritative `PublishedArtworkState`,
  observes and retains the exact source baseline (or an explicit absent baseline)
  through `IArtworkSourceReader` and `SourceArtifactStore`, promotes the
  validated render output to a durable derived artifact, and writes a `Prepared`
  `ArtworkOperation` before any image mutation. It revalidates the before identity
  immediately before mutation and aborts without `SaveImage` on a mismatch or an
  unobservable identity, marking the operation `Aborted`; a previously published
  session also persists the ownership outcome (`OwnershipLost`/`OwnershipUnknown`),
  while an initial capture persists no `PublishedArtworkState`. It durably
  advances through `MutationStarted`, `RepositoryUpdateStarted`,
  `VerificationPending`, and `FinalizationPending`, commits the final
  `PublishedArtworkState` (new publication token, active identity, state revision,
  operation id; retained source artifact and ownership token), and only then
  marks the operation `Committed`. Failures and uncertainties leave the current
  artwork unchanged, record a bounded non-secret diagnostic, and never commit;
  a pre-existing non-terminal or recovery-blocked operation blocks new work. Repeat publication
  verifies the prior publication is still active, reuses the first source
  artifact and ownership token, issues a new publication token, and never captures
  an ArrTags output as a source. No event, queue, or library-scan wiring is added.
- `src/ArrTags/PluginLifecycle/ArrTagsServiceRegistrator.cs`: registers
  `IArtworkImageWriter`, `SourceArtifactStore`, `PublishedArtworkStateStore`,
  `ArtworkOperationStore`, and `ArtworkPublisher` with the existing lazy factory
  pattern and no startup work.
- Tests: `tests/ArrTags.Tests/ArtworkPublisherTests.cs` and
  `tests/ArrTags.Tests/JellyfinArtworkImageWriterTests.cs` (23 cases) cover the
  happy path with the durable phase ordering observed at the external calls,
  absent baseline, before-identity mismatch and unobservable-before aborts with
  no `SaveImage` call, readback mismatch and item-update failure not committing,
  repeat-publication source/ownership-token reuse with a new publication token,
  ownership-lost, stale-baseline, and non-terminal-operation blocking, bounded
  cancellation, derived-artifact size and hash rejection, state path/secret
  hygiene, the stream overload being used instead of the deleting path or URL
  overload, the normal image update flow, and boundary-neutrality.

Build and test: `./build.sh restore`, `./build.sh build` (0 warnings, 0 errors),
and `./build.sh test` pass. The default suite passes 872 with 49
environment-guarded skips (921 total), exactly +23 over the task 5.6 baseline
(849/49/898 with the package present), with no regressions. No ADR,
`RenderVersion`, renderer behavior, or existing passing behavior was changed.

### Task 5.7 - Postcondition reconciliation of uncertain publication outcomes

Task 5.7 implements provider-neutral, postcondition-based reconciliation for the
durable `ArtworkOperation` journal (ADR-003; `docs/data-model.md` section 3.10.4;
`docs/architecture.md` section 9). It does not add event, queue, startup-scan, or
library-event wiring (Phase 6) and does not implement the guarded restoration
mutation (task 5.9).

- `src/ArrTags/Artwork/ArtworkReconciliationAction.cs`,
  `ArtworkRecoveryDecision.cs`, and `ArtworkRecoveryDecisions.cs`: the pure
  implementation of the 3.10.4 decision table. `Evaluate` takes the durable
  operation, the associated `PublishedArtworkState`, a fresh active-image
  observation, and confirmed item absence, and returns a bounded action
  (`NothingToReconcile`, `Resume`, `AbortFenced`, `CompleteAfter`,
  `FinalStateDurable`, `OwnershipLost`, `OwnershipUnknown`, `ItemRemoved`, or
  `RecoveryBlocked`). It performs no I/O and no image mutation.
- `src/ArrTags/Artwork/ArtworkReconciliationResult.cs` and
  `ArtworkReconciliationOutcome.cs`: the bounded result of one reconciliation
  attempt, carrying only a bounded outcome, reason, operation id, and state.
- `src/ArrTags/Artwork/ArtworkReconciler.cs`: the registered, provider-neutral
  invocable boundary. It serializes with normal publication through
  `ArtworkSubjectGate`, reads the authoritative state and durable operation
  (quarantining an invalid record rather than replaying it), distinguishes a
  confirmed missing item from an unobservable image, evaluates the table, and
  delegates execution to `ArtworkPublisher.ExecuteRecoveryAsync`. A failure or
  uncertainty is contained and never escapes as an exception.
- `src/ArrTags/Artwork/ArtworkPublisher.cs`: the deterministic publication
  protocol was extracted into a shared recoverable execution path used by both
  the normal publication route and reconciliation, so the supported `SaveImage`,
  the durable phase ordering, the readback, and the final-state commit are never
  reimplemented. Resume re-reads the before identity immediately before mutation
  and reads the retained derived artifact rather than the active image. An
  after-identity match ensures the normal item update is persisted and commits
  the intended final state; an observable mismatch records `OwnershipLost` and
  aborts; an unobservable image records `OwnershipUnknown`; a confirmed missing
  item writes an `ItemRemoved` tombstone with no image mutation; a durable final
  state completes the journal; and invalid state or a missing/corrupt required
  artifact enters `RecoveryBlocked` with no replay or cleanup. No artifact is
  deleted, so an artifact not proven non-active is retained. The per-subject
  serialization gate moved to the shared internal `ArtworkSubjectGate` so
  publication and reconciliation cannot interleave.
- `src/ArrTags/Artwork/ArtworkOperation.cs` and `docs/data-model.md` section
  3.10.3: a publication operation now records the logical
  `candidatePublicationFingerprint` and the `rendererVersion` that produced its
  derived artifact, so an after-match recovery commits the target
  `PublishedArtworkState` without re-rendering or misattributing the renderer
  version. Both fields are required for a publication and forbidden for a
  restoration.
- `src/ArrTags/PluginLifecycle/ArrTagsServiceRegistrator.cs`: registers
  `ArtworkReconciler` with the existing lazy factory pattern and no startup work.
- Tests: `tests/ArrTags.Tests/ArtworkReconcilerTests.cs` (24 cases) cover the
  prepared and mutation-started before-match resume/retry, the candidate and
  observed-after completion, the durable-final-state completion, ownership loss
  and uncertainty, the item-absent tombstone with no mutation, the lifecycle
  fence abort, before-identity revalidation on resume, missing derived/source
  artifacts entering `RecoveryBlocked`, resolution of a previously
  `RecoveryBlocked` operation, restoration resume deferral, no-op for a terminal
  or absent operation, cancellation, containment of a reader exception, the DI
  registration, and boundary-neutrality. `ArtworkOperationFixtures` and the
  `ArtworkOperationTests` helper supply the new required publication fields.

Build and test: `./build.sh restore`, `./build.sh build` (0 warnings, 0 errors),
and `./build.sh test` pass. The default suite passes 896 with 49
environment-guarded skips (945 total), exactly +24 over the task 5.5 baseline,
with no regressions. No ADR, `RenderVersion`, renderer behavior, or existing
passing behavior was changed.

### Task 5.8 - Preserve the current usable artwork when source capture or rendering cannot safely complete

Task 5.8 implements the provider-neutral, single-subject artwork generation
coordination that composes the host source adapter, the renderer, and the durable
publisher (`docs/architecture.md` section 9; `docs/data-model.md` sections
3.7-3.8). It does not add event, queue, or library-scan wiring (Phase 6), the
lifecycle fencing and guarded restoration mutation (task 5.9), Enhanced
coexistence (task 5.10), route/client tests (task 5.11), or caching and
invalidation.

- `src/ArrTags/Artwork/ArtworkGenerationRequest.cs`: the canonical caller input
  (Jellyfin item id, V1 surface, `MediaIdentity`, `MediaMatch`, optional
  `BadgeMetadata`, ordered `BadgeDefinition` snapshot, secret-free configuration
  fingerprint, effective `RenderOutputPolicy`, `OperationalLimits`, and the
  renderer/badge schema versions). It carries no source bytes, source artifact,
  provider DTO, path, entity, or credential.
- `src/ArrTags/Artwork/ArtworkGenerationCoordinator.cs`: observes the current
  source through `IArtworkSourceReader`, builds the `SourceImageInput` and
  `RenderRequest`, calls `IRenderer.RenderAsync`, and only publishes through
  `ArtworkPublisher` when the render is `Rendered`. An absent source returns a
  no-op; a failed (unavailable, unsupported, or oversized) source read, a render
  pass-through, and a failed render preserve the current artwork and never call
  the renderer or publisher respectively. Cancellation is honored, no exception
  escapes, and the coordinator never calls Jellyfin directly.
- `src/ArrTags/Artwork/ArtworkGenerationOutcome.cs` and
  `ArtworkGenerationResult.cs`: the bounded result distinguishing published,
  no-source/absent, source-unavailable, render-pass-through (including missing
  metadata and an ineligible match), render-failed, publication-not-completed,
  blocked, and cancelled, with the safe source/render/publication classification
  and no secret, path, entity, or artifact content.
- Source consistency: the exact source observation the coordinator rendered from
  is supplied to the publisher's new-session capture, so the retained provenance
  baseline and the derived artifact describe the same source. This is an additive
  plugin-internal `ArtworkPublisher.PublishAsync` overload; the public overload
  keeps its previous behavior, and the before-mutation revalidation is unchanged,
  so a source that changes after the observation still prevents the mutation.
- Missing metadata and an ineligible match rely on the existing renderer
  pass-through convention (`NoMetadata`, `MatchNotEligible`); the coordinator
  adds no new badge policy and performs no mutation on those paths.
- `src/ArrTags/PluginLifecycle/ArrTagsServiceRegistrator.cs`: registers
  `IRenderer` (`SkiaBadgeRenderer`) and `ArtworkGenerationCoordinator` with the
  existing lazy factory pattern and no startup work.
- Tests: `tests/ArrTags.Tests/ArtworkGenerationCoordinatorTests.cs` (23 cases)
  cover the published path; absent source with skipped renderer/publisher; failed
  source reads for unreadable, unsupported, oversized, and oversized-dimension
  classifications; render pass-through and failure; the real renderer's
  null-metadata and ineligible-match pass-through; renderer exception
  containment; publication failure, blocked publication, and publication
  cancellation; cancellation before start and during the source read; a source
  change after the render observation aborting without mutation; invalid bounded
  input; the shared source observation with the retained provenance baseline;
  state/path hygiene; the provider-neutral boundary; and DI registration. All
  preservation paths assert zero image mutation.

Build and test: `./build.sh restore`, `./build.sh build` (0 warnings, 0 errors),
and `./build.sh test` pass. The default suite passes 919 with 49
environment-guarded skips (968 total), exactly +23 over the task 5.7 baseline,
with no regressions. No ADR, `RenderVersion`, renderer behavior, or existing
passing behavior was changed.

### Task 5.9 - Fence and drain publication operations during disable/uninstall, and tombstone confirmed item removal

Task 5.9 implements the durable lifecycle fence, the disable/uninstall drain and
guarded restoration, the supported image-removal primitive, and confirmed
item-removal tombstoning (`docs/architecture.md` section 9; `docs/data-model.md`
sections 3.10.1-3.10.4; ADR-002/ADR-003). It does not add Enhanced coexistence
(task 5.10), route/client tests (task 5.11), or the broader queue, event,
scheduled-scan, caching, or invalidation pipeline (Phase 6) beyond the minimal
`ItemRemoved` tombstone handling and the host lifecycle fence triggers it
requires.

- `src/ArrTags/Artwork/ArtworkLifecycleFenceRecord.cs` and
  `ArtworkLifecycleFenceStore.cs`: the versioned, integrity-tagged, atomically
  written authoritative record and store for the active `Normal`/`Disable`/
  `Uninstall`/`ItemRemoved` fence. A missing record is `Normal`; an invalid
  record is quarantined and fails closed; the record never contains a credential,
  path, entity, or provider payload.
- `src/ArrTags/Artwork/ArtworkLifecycleCoordinator.cs`,
  `IArtworkLifecycleCoordinator.cs`, `ArtworkLifecycleResult.cs`, and
  `ArtworkRemovalResult.cs`: the provider-neutral lifecycle service. It records
  the durable fence, reconciles every non-terminal operation to a terminal result
  before creating the guarded restoration, restores every still-owned
  `Published` surface, reports `Incomplete` when a restoration is blocked,
  externally changed, or uncertain while retaining all recovery records, and
  tombstones a confirmed item removal with no Jellyfin image call. A
  recovery-blocked operation is reported as incomplete and no artifact or journal
  record is deleted eagerly.
- `src/ArrTags/Artwork/ArtworkPublisher.cs`: reads the durable fence before
  accepting new work and refuses it unless the fence is a valid `Normal`, records
  the fence on the operation it creates, and adds a guarded `RestoreAsync` that
  reuses the same durable phases, immediate before-mutation revalidation, and
  readback postcondition commit as publication. An absent baseline removes the
  ArrTags image; a present baseline restores the retained source; an unverifiable
  result enters `RecoveryBlocked` and `RestoreBlocked`.
- `src/ArrTags/Artwork/IArtworkImageWriter.cs` and
  `JellyfinArtworkImageWriter.cs`: the supported
  `BaseItem.DeleteImageAsync(ImageType, int)` removal primitive, which removes
  the local image file when the image is local, removes the image information,
  and persists the normal `ItemUpdateType.ImageUpdate` repository update. No
  media file or image-cache entry is ever deleted directly and all
  `MediaBrowser.*` references stay confined to that single implementation file.
- `src/ArrTags/Artwork/ArtworkRecoveryDecisions.cs` and
  `ArtworkReconciler.cs`: an optional active-fence argument and a matching
  `ReconcileAsync` overload, so a disable/uninstall drain aborts an in-flight
  prepared publication (`AbortFenced`) instead of resuming it, and a restoration
  resume now executes the guarded restoration instead of reporting `Deferred`.
- `src/ArrTags/Artwork/ArtworkLifecycleCoordinator.cs`: the drain classifies each
  reconciled operation by its durable phase rather than the transient
  reconciliation outcome, so an operation that becomes `RecoveryBlocked`
  (including through an `OwnershipUnknown` or non-throwing source-read failure)
  yields `ArtworkLifecycleOutcome.Incomplete`; the item-removal path only claims a
  tombstone after the reconciler actually aborted the in-flight operation and returns
  `Blocked` with `tombstoned: false` when the reconciler cannot reach the
  `ItemRemoved` decision (for example an invalid/quarantined state or operation).
- `src/ArrTags/Artwork/ArtworkLifecycleFenceStore.cs`: an invalid/corrupt durable
  fence is read without quarantining it away and a normal reset never overwrites
  it, so the publication read path stays fail-closed until an explicit recovery
  decision.
- `src/ArrTags/State/StateRepository.cs` and `PluginStatePaths.cs`: a bounded,
  deterministic authoritative enumeration and a kind-directory accessor so the
  coordinator can list durable operations and states.
- `src/ArrTags/PluginLifecycle/ArrTagsLifecycleService.cs`: `StartAsync` clears a
  stale fence when the host loaded the plugin active; `StopAsync` unsubscribes
  and performs a bounded `DrainForHostShutdownAsync` plus tracked item-removal
  confirmations; the formerly no-op `ItemRemoved` handler now runs a tracked,
  bounded confirmation task so synchronous library event delivery is never
  blocked. A plain shutdown resolves `Normal` and is a no-op.
- `src/ArrTags/PluginLifecycle/IPluginLifecycleFenceProvider.cs` and
  `JellyfinPluginLifecycleState.cs` (plus `IPluginLifecycleFenceProvider` on the
  artwork side): the host-neutral fence source and its single Jellyfin
  implementation, which maps the persisted plugin manifest status to the fence
  through the supported `IPluginManager`.
- `src/ArrTags/Plugin.cs`: the supported `OnUninstalling` hook records the
  `Uninstall` fence and performs a bounded synchronous drain because the pinned
  host deletes the plugin data folder immediately after the hook returns; it
  never throws into the host and leaves a blocked or uncertain restoration
  untouched.
- `src/ArrTags/PluginLifecycle/ArrTagsServiceRegistrator.cs`: registers the fence
  store, the fence provider, the coordinator, and the interface mapping with the
  existing lazy factory pattern and no startup work.
- Trigger boundary: there is no Jellyfin disable hook. Disable is observed from
  the persisted manifest status during the graceful `StopAsync` of the still
  loaded instance; uninstall comes from `OnUninstalling`; item removal is
  confirmed by a fresh read. A disable that is only observed after the plugin has
  been unloaded (for example a disable followed by a hard kill without a graceful
  shutdown) is not detectable from inside the plugin; the durable fence still
  prevents new publication work while it is present. See
  `docs/architecture.md` section 9.
- Tests: `tests/ArrTags.Tests/ArtworkLifecycleTests.cs` (28 cases) plus focused
  additions to `ArtworkReconcilerTests`, `ArtworkPublisherTests`
  (`RemoveImageUsesTheSupportedDeletionFlowAndNeverTheProvider`),
  `LifecycleFoundationTests`, and `StateBoundaryTests` cover fence durability
  across a reconstructed `StateRepository`, missing/invalid fences and the
  not-overwritten-by-reset fail-closed rule, publication refusal under every
  non-normal fence, present- and absent-baseline guarded restoration,
  reconcile-before-restore ordering, blocked/changed/uncertain retention and
  incomplete reporting (including a recovery-blocked source-read failure),
  source-artifact absence, bounded cancellation, the confirmed item-removal
  tombstone with zero image calls, the not-confirmed, reader-failure, and
  invalid-state-no-tombstone paths, the host status-to-fence mapping, the bounded
  hosted shutdown drain, the `Plugin.OnUninstalling` lazy resolution and
  failure-containment, bounded deterministic traversal-safe
  `StateRepository.Enumerate`, DI registration, and boundary-neutrality.

Build and test: `./build.sh restore`, `./build.sh build` (0 warnings, 0 errors),
and `./build.sh test` pass. The default suite passes 964 with 49
environment-guarded skips (1013 total), exactly +45 over the task 5.8 baseline,
with no regressions. No ADR, `RenderVersion`, renderer behavior, or existing
passing behavior was changed.

### Task 5.10 - Jellyfin Enhanced coexistence policy (DG-8)

Task 5.10 resolves decision gate DG-8 with `docs/decisions.md` ADR-011 and adds
the coexistence policy and tests. It does not add automatic duplicate/overlap
suppression, an Enhanced-internals dependency, or any new configuration, and it
does not add route/client tests (task 5.11) or Phase 6 queue, event, or caching
wiring. No production behavior, `RenderVersion`, renderer behavior, or existing
ADR was changed; ADR-011 is the only ADR added.

- `docs/decisions.md` ADR-011: records the resolved policy. ArrTags does not
  implement automatic duplicate-badge detection, overlap suppression, or a
  dependency on Jellyfin Enhanced internals; Jellyfin Enhanced can choose its
  own overlay placement, so overlap handling is deferred to the user. ArrTags
  badge output is controlled only by the existing `BadgeMoviePosters`/
  `BadgeEpisodePosters` poster enable flags and the renderer selector
  enablement. Enhanced's Spoiler Guard has no material effect on ArrTags badge
  display, so ArrTags renders its derived badge normally with no special
  spoiler/hidden handling.
- `docs/architecture.md` section 10: states the coexistence policy; the
  configuration section records that no separate coexistence field is persisted;
  the testing section, the decisions-required list (item 8), and the data-model
  `enhancedCoexistencePolicy` rows are aligned with ADR-011.
- `PLANS.md`: DG-8 is marked resolved by ADR-011, task 5.10 is checked with a
  status paragraph, the Milestone 5 status row and Enhanced acceptance criterion
  are updated, and the Enhanced risk mitigation and Phase 5 deliverable are
  aligned with the resolved policy.
- `docs/implementation-readiness.md`: Former Action Disposition item 10 is marked
  resolved by ADR-011, a "Resolved DG-8" section is added, and the
  implementation-time question is closed for the policy (route confirmation
  remains task 5.11).
- Tests: `tests/ArrTags.Tests/EnhancedCoexistenceTests.cs` (7 cases) assert that
  the production assembly references no Enhanced assembly and declares no
  Enhanced/spoiler/suppression type; that the policy surface and the renderer/
  publication reason enums expose no Enhanced, spoiler, hidden, blur, suppress,
  or overlap member; that Movie/Episode poster eligibility and renderer badge
  selection vary only with the existing poster and selector flags; and that the
  renderer decision is a pure function of the canonical render request with no
  hidden global state. The tests run without a live Jellyfin host, a live
  Enhanced install, or the Skia native runtime.

Build and test: `./build.sh restore`, `./build.sh build` (0 warnings, 0 errors),
and `./build.sh test` pass. The default suite passes 971 with 49
environment-guarded skips (1020 total), exactly +7 over the task 5.9 baseline,
with no regressions.
