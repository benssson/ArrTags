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
every failure path. Phase 5 (Jellyfin artwork integration) has not started and
requires explicit user approval to begin.
