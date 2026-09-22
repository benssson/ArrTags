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

**Status:** Complete. Gate 5 met.

**Commit/tag:** tag `v0.1.0-phase5`, at commit
`2a9298fa567d0dc23f535e854336e6f2cbeabbbd` ("Phase 5 complete: Jellyfin artwork
integration", 2026-09-21). Tasks 5.1-5.11 landed in commits `72264a7` through
`be4eda5` in the authoritative execution order (5.1, 5.2, 5.3, 5.4, 5.6, 5.5,
5.7, 5.8, 5.9, 5.10, 5.11); the phase review is in `2a9298f`.

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

### Task 5.11 - Standard server image response path for image-consuming clients

Task 5.11 completes Phase 5. It exercises the supported standard Jellyfin server
image response path that Web and other image-consuming clients use, without
adding a parallel route, middleware, response interceptor, or any production
behavior; it does not add Phase 6 queue, event, or caching wiring.

- Tests: `tests/ArrTags.Tests/JellyfinImageResponseTests.cs` loads the pinned
  host `Jellyfin.Api.dll`, constructs the real
  `Jellyfin.Api.Controllers.ImageController` from `DispatchProxy` host doubles
  and a real `DefaultHttpContext`, and invokes the actual `GetItemImage`,
  `GetItemImageByIndex`, and `GetItemImage2` actions. The 9 host-guarded cases
  cover: the unindexed and indexed and path-form `Primary` routes all returning
  the same server-rendered `PhysicalFileResult` (path and content type);
  the quoted image-tag `ETag`, `Cache-Control: public, max-age=31536000,
  immutable`, `Last-Modified`, `Vary: Accept`, `Content-Disposition: attachment`,
  and DLNA headers; `304 Not Modified` for a matching quoted or bare
  `If-None-Match` and for `If-Modified-Since`; the `no-cache` revalidation
  headers; the requested size/format plumbing into `ImageProcessingOptions`; and
  the `404` pass-through for an unknown item or an item without an image with no
  processor call. A publish-then-read-back case uses the real
  `JellyfinArtworkImageWriter` with a provider double that mirrors the supported
  `ImageSaver` write-and-update flow, serves the item through the real standard
  route, and asserts the derived bytes (not the stale source) plus an untouched
  original source file. Two unguarded cases pin
  `ImageHelper.GetNewImageSize`'s never-upscale clamp.
- Host distance: the existing task 5.1 `JellyfinImageRouteTests` pin the same
  pinned host's route templates and authorization attributes. A live HTTP
  round-trip against a running Jellyfin server was **not** performed for this
  task, and the full ArrTags generation-to-publication pipeline could not be
  driven on a host because the Phase 6 queue/event wiring that triggers
  generation does not exist yet; the in-process publish-then-read-back case uses
  the real ArrTags writer and the real pinned controller.
- Docs: `PLANS.md` marks task 5.11 complete, records the task 5.11 status,
  reconciles the Phase 5 acceptance criteria with the completed-task tests
  (noting that validation is integration-level, not a live-host run), and
  records Gate 5 as met with the Phase 6-wiring and no-live-HTTP limitation; the
  Milestone 5 status row is complete. `README.md` records the completed task and
  updated test totals.

Build and test: `./build.sh restore`, `./build.sh build` (0 warnings, 0 errors),
and `./build.sh test` pass. The default suite passes 973 with 58
environment-guarded skips (1031 total), exactly +2 over the task 5.10 baseline
because the 9 new host-guarded cases skip; with
`ARRTAGS_JELLYFIN_HOST_DIR=/tmp/opencode/jf/jellyfin` the full suite passes 987
with 44 skips (1031 total, 0 failures), unskipping all 14 host-guarded image-route
cases, with no regressions.

## Phase 6 - Caching, updates & performance (Milestone 6)

**Status:** Complete. Gate 6 met at the integration-test level.

**Commit/tag:** tag `v0.1.0-phase6`, at commit
`52dec772b904fd188890123f9fc3e776840bb30a` ("execute post phase 6 review",
2026-09-21). Tasks 6.1-6.9 landed in commits `cf285f5` through `a513216` in the
authoritative execution order (6.1, 6.2, 6.3, 6.4, 6.5, 6.6, 6.7, 6.8, 6.9); the
phase review is in `52dec77`. Phase 6 residuals tracked for Phase 7 rather than
presented as solved: the provider inventory/catalogue cache, runtime configuration
replacement wiring, the reconciliation coverage bound on a scope larger than the
bounded queue, and a safe metrics/diagnostic-status surface.

### Task 6.1 - Short library event handlers and the bounded enqueue boundary

Task 6.1 implements the Phase 6 entry boundary in `src/ArrTags/PluginLifecycle`
and `src/ArrTags/Updates`: a short, synchronous library-event handler that
validates relevance, converts the change into a bounded provider-neutral work
hint, and enqueues it through a narrow, non-blocking boundary
(`docs/architecture.md` section 8; ADR-004; ADR-005). It does not implement the
task 6.2 queue/worker (coalescing by item and connection, single-flight, worker
cancellation, retry classification, and the full overflow policy), the metadata
cache (6.3/6.5), recovery (6.4), fingerprint invalidation (6.6), or webhooks
(6.7).

- `src/ArrTags/PluginLifecycle/LibraryItemChangedEventArgs.cs`: the boundary
  event now carries the bounded change facts a short handler needs - the Jellyfin
  item id, the `LibraryWorkReason` (`Added`, `Updated`, or `Removed`), the
  structural `MediaItemType?` when known, and the `LibraryItemChangeOrigin`
  (`Library` or `Image`). It carries no provider DTO, path, credential, or the
  Jellyfin item.
- `src/ArrTags/PluginLifecycle/JellyfinLibraryEventSource.cs`: the adapter maps
  each Jellyfin `ItemChangeEventArgs` synchronously and in memory. It reads only
  the item id, the structural type switch, and whether
  `ItemUpdateType.ImageUpdate` is set; it performs no provider, rendering, image,
  or library lookup. The mapping is exposed as a static `MapChange` so the rules
  are verifiable without a live host.
- `src/ArrTags/PluginLifecycle/LibraryEventRelevance.cs`: the pure relevance
  policy. It drops an empty item id, an image-only or ArrTags-generated internal
  update, a non-badge-bearing type for an add/update (V1 badge surfaces are Movie
  and Episode only, ADR-006), and any change when no Arr connection is enabled.
  A removal stays relevant regardless of the current type because a previously
  published surface may need reconciliation. Configured library scope and current
  item state are re-validated by the future worker rather than looked up here.
- `src/ArrTags/Updates/LibraryWorkHint.cs` and `LibraryWorkReason.cs`: the
  bounded, provider-neutral unit of work containing only the Jellyfin item id,
  the reason, and the safe configuration generation observed at the boundary.
  It contains no API key, secret lease, credential, provider DTO, path, or
  unbounded payload (ADR-005).
- `src/ArrTags/Updates/IWorkHintSink.cs` and `BoundedWorkHintSink.cs`: the narrow
  enqueue boundary and its minimal implementation. The sink is thread-safe and
  bounded by the ADR-004 `QueueCapacity`; it coalesces a redundant hint for an
  item that already has pending work, drops overflow, never blocks on external
  work, and never throws. A `TryDequeue` primitive is provided for the future
  task 6.2 worker; no worker or processing pipeline is implemented here.
- `src/ArrTags/PluginLifecycle/ArrTagsLifecycleService.cs`: one synchronous
  handler serves `ItemAdded`, `ItemUpdated`, and `ItemRemoved`. It performs the
  relevance check and enqueue, and it contains any unexpected exception so a
  handler failure can never surface to the host's event publisher. The task 5.9
  `ItemRemoved` tracked, bounded drain/tombstone behavior is preserved unchanged
  and now also emits a removal hint through the same boundary.
- `src/ArrTags/PluginLifecycle/ArrTagsServiceRegistrator.cs`: registers
  `IWorkHintSink` as a lazy singleton factory bounded by the current
  `OperationalLimits.QueueCapacity`; registration still performs no startup work.
- Tests: `tests/ArrTags.Tests/LibraryUpdateBoundaryTests.cs` (14 cases) and the
  shared `LibraryEventFixtures` double the boundary. They cover synchronous hint
  enqueue with no coordinator call, dropping when no provider is enabled,
  dropping non-badge and unknown types, dropping image-only and empty-id changes,
  Episode-to-Sonarr and removal relevance, the preserved `ItemRemoved` drain,
  never throwing on a full sink, coalescing, bounded overflow, empty-id
  rejection, FIFO dequeue and coalescing release, the absence of
  provider/renderer/image/secret dependencies in the handler constructor,
  the Jellyfin item-type/image-origin mapping, and DI registration at the
  configured capacity. `LifecycleFoundationTests` keeps its existing lifecycle
  behavior coverage and now supplies the configuration and sink dependencies.

Build and test: `./build.sh restore`, `./build.sh build` (0 warnings, 0 errors),
and `./build.sh test` pass. The default suite passes 987 with 58
environment-guarded skips (1045 total), exactly +14 over the task 5.11 baseline
(973/58/1031), with no new skips and no regressions. No ADR, `RenderVersion`,
renderer behavior, or existing passing behavior was changed. The full queue
worker, coalescing-by-connection, caching, recovery, and invalidation behavior
remains tasks 6.2-6.8.

### Task 6.2 - Bounded coalescing work queue, single-flight, and cancellation-aware workers

Task 6.2 implements the Phase 6 queue and worker mechanics in
`src/ArrTags/Updates`, built on the task 6.1 `IWorkHintSink`/`LibraryWorkHint`
boundary (`docs/architecture.md` sections 4, 5, 8, and 12; ADR-004; ADR-005). It
supersedes the task 6.1 `BoundedWorkHintSink` with the production queue and does
not implement the later Phase 6 tasks: atomic metadata-state publication and
stale-work disposal (6.3), non-terminal artwork-operation recovery and generation
fences (6.4), metadata freshness (6.5), fingerprint-driven invalidation (6.6),
webhooks (6.7), or the restart/outage/pressure test matrix (6.8).

- `src/ArrTags/Updates/WorkItemKey.cs` and `LibraryWorkItem.cs`: the coalescing
  and single-flight identity (Jellyfin item, resolved connection, and image
  surface) and the bounded queued unit (key, reason, safe configuration
  generation). A hint-derived item leaves the connection unresolved with the
  unindexed Primary surface. Neither type carries a credential, secret lease,
  provider DTO, path, or unbounded payload (ADR-005).
- `src/ArrTags/Updates/LibraryWorkQueue.cs` (replacing `BoundedWorkHintSink.cs`):
  the bounded, thread-safe queue. It implements `IWorkHintSink` and coalesces a
  redundant hint or work item for a key that is already pending or in flight, so
  a duplicate event never creates duplicate concurrent processing. Pending work
  is bounded by `OperationalLimits.QueueCapacity` and single-flight by
  `OperationalLimits.PerItemInFlightWork`; both are resolved from the current
  configuration snapshot on each operation, addressing the Phase 5 review LOW
  finding for queue limits. `TryEnqueue` is a short in-memory critical section
  with no external I/O, so overflow coalesces or drops and never blocks the
  library-event publisher. A stopped queue rejects new work.
- `src/ArrTags/Updates/LibraryWorkWorker.cs`: the hosted consumer. It owns a
  fixed, bounded pool of cancellation-aware workers (default four) that dequeue
  asynchronously and dispatch through the narrow `IWorkItemProcessor` boundary.
  It is registered after `ArrTagsLifecycleService` so a host shutdown stops
  accepting and cancels queued/in-flight work before the lifecycle drain
  establishes the durable fence, then awaits the workers within a bounded
  timeout. No fire-and-forget task or unmanaged thread is created.
- `src/ArrTags/Updates/IWorkItemProcessor.cs` and `WorkProcessingResult.cs`: the
  narrow processing/dispatch boundary for tasks 6.3-6.6 and the classified
  outcome. The outcome reuses the provider retry vocabulary
  (`ArrErrorRetryability`): transient (`Later`) outcomes retry, terminal outcomes
  do not.
- `src/ArrTags/Updates/WorkRetryPolicy.cs`: the bounded retry policy. It derives
  the attempt count from `TransientRetryCount` and mirrors the provider-boundary
  exponential backoff (`RetryBackoffInitialSeconds`, `RetryBackoffFactor`,
  `RetryBackoffMaxSeconds`), so queue-level retries cannot become an unbounded
  loop.
- `src/ArrTags/Updates/DeferredWorkItemProcessor.cs`: the Phase 6 placeholder
  dispatch. It completes each dequeued item as a safe no-op until the
  reconciliation pipeline lands, so no metadata or artwork is published yet.
- `src/ArrTags/PluginLifecycle/ArrTagsServiceRegistrator.cs`: registers the
  `LibraryWorkQueue` singleton (capacity and single-flight resolved from the
  current snapshot), maps `IWorkHintSink` to it, registers the placeholder
  `IWorkItemProcessor`, and adds `LibraryWorkWorker` as a hosted service after
  the lifecycle service. Registration still performs no startup work.
- Tests: `tests/ArrTags.Tests/LibraryWorkQueueTests.cs` (16 cases) plus updated
  `LibraryUpdateBoundaryTests` and `LifecycleFoundationTests`. They cover
  coalescing by item and connection (including distinct connections and
  surfaces), coalescing of a redundant hint while work is in flight, single-flight
  per item/surface, rejection of an empty item id, bounded overflow with a
  non-blocking enqueue, FIFO dequeue and capacity release, bounded pending and
  in-flight diagnostics, transient-then-complete retry, no terminal retry, bounded
  attempt abandonment, cancellation during processing and during backoff, stopping
  acceptance and restarting, and the pure retry policy and retry classification.

Build and test: `./build.sh build` (0 warnings, 0 errors) and `./build.sh test`
pass. The default suite passes 1003 with 58 environment-guarded skips (1061
total), exactly +16 over the task 6.1 baseline (987/58/1045), with no new skips
and no regressions. No ADR, `RenderVersion`, renderer behavior, or existing
passing behavior was changed. The Phase 5 review MEDIUM finding that
`ArtworkPublisher.PublishCoreAsync` reads the durable lifecycle fence once is not
made reachable by this task because the placeholder processor does not drive
artwork publication; it remains open for the later task that wires artwork
publication into the worker (6.4/6.6).

### Task 6.3 - Atomic metadata state publication and stale-work disposal

Task 6.3 implements the reconciliation and metadata-state publication path
(`docs/architecture.md` sections 6, 8, and 9; `docs/data-model.md` sections 3.9,
4.9, 6, and 7; ADR-002; ADR-004; ADR-005) and replaces the task 6.2
`DeferredWorkItemProcessor` placeholder with the real processor. It does not
implement metadata freshness/staleness (6.5), non-terminal artwork-operation
recovery and generation fences (6.4), fingerprint-driven artwork
regeneration/invalidation (6.6), webhooks (6.7), or the restart/outage/pressure
test matrix (6.8).

- `src/ArrTags/Reconciliation/MetadataStateEntry.cs`: the canonical, versioned,
  secret-free, provider-neutral metadata state record. It carries the Jellyfin
  item and library scope, provider/connection scope, typed record/file identity,
  match status/method/fingerprint, the last-known-good normalized metadata
  snapshot and its canonical fingerprint, optional provider version/token, the
  fetch timestamp, and an explicit `MetadataStateKind`. The `expiresAt`/
  `staleUntil` fields are recorded but intentionally not computed here; the
  bounded stale-last-known-good policy is task 6.5.
- `src/ArrTags/Reconciliation/MetadataSnapshot.cs` and
  `MetadataRecordIdentity.cs`: serializable, provider-neutral forms of
  `BadgeMetadata` and the abstract connection-scoped `ArrRecordIdentity`, so the
  record round-trips through the plain `System.Text.Json` state boundary without
  a polymorphic discriminator and without provider DTOs.
- `src/ArrTags/Reconciliation/MetadataStateStore.cs`: persists the record through
  the existing versioned cache state boundary (`StateAuthority.Cache`) with the
  stable record kind `metadata-state` and an item/provider record identifier. A
  corrupt or semantically invalid entry is discarded as rebuildable and never
  treated as authoritative; an in-place replacement is one atomic write.
- `src/ArrTags/Reconciliation/IArrMetadataReader.cs`,
  `ArrMetadataReadResult.cs`, `IArrReadClientFactory.cs`, and
  `ArrReadClientFactory.cs`: the provider-neutral reconciliation read boundary
  and the connection-scoped read-client factory. Operational limits are resolved
  from the current configuration snapshot at client creation.
- `src/ArrTags/Providers/Radarr/RadarrMetadataReader.cs` and
  `src/ArrTags/Providers/Sonarr/SonarrMetadataReader.cs`: compose the existing
  read clients, candidate factories, documented matching order, the validated
  `episodeFileId` join, and the metadata mappers, returning a canonical match and
  metadata without leaking provider DTOs.
- `src/ArrTags/Reconciliation/MetadataReconciliationProcessor.cs`: implements
  `IWorkItemProcessor`. It re-reads the current configuration snapshot and
  Jellyfin item, resolves the applicable connection, matches and maps through the
  reader, computes the canonical fingerprint, re-reads the current configuration
  and item immediately before publishing, and only then atomically publishes. A
  changed basis (advanced configuration version, disabled/changed connection,
  removed/changed/ineligible item) or a provider read failure discards the work
  and never publishes or overwrites metadata state. A transient provider failure
  remains retryable; a terminal failure does not. The processor publishes
  metadata state only and never invokes
  `ArtworkGenerationCoordinator`/`ArtworkPublisher`.
- `src/ArrTags/PluginLifecycle/ArrTagsServiceRegistrator.cs`: registers the
  metadata state store, the read-client factory, the two provider readers, and
  the real processor in place of `DeferredWorkItemProcessor`.
  `src/ArrTags/Updates/DeferredWorkItemProcessor.cs` is removed. The task 6.2
  queue, worker, coalescing, single-flight, retry, and shutdown behavior is
  unchanged.
- Tests: `tests/ArrTags.Tests/MetadataStateStoreTests.cs` (10 cases),
  `MetadataReconciliationProcessorTests.cs` (13 cases), `MetadataReaderTests.cs`
  (6 cases), `ReconciliationFixtures.cs`, and an updated
  `LifecycleFoundationTests` registration test. They cover atomic publication,
  in-place fingerprint replacement, corrupt and semantically invalid cache
  discard, reload round-trip, secret-free persistence, discard on advanced
  configuration version or configuration change during processing, disabled
  connection, absent item, ineligible item, and changed item, discard without
  overwriting an existing published state, retryable versus terminal provider
  failure, provider reader matching and metadata mapping, the DI replacement, and
  the absence of an artwork-publication dependency.

Build and test: `./build.sh build` (0 warnings, 0 errors) and `./build.sh test`
pass. The default suite passes 1032 with 58 environment-guarded skips (1090
total), exactly +29 over the task 6.2 baseline (1003/58/1061), with no new skips
and no regressions. No ADR, `RenderVersion`, renderer behavior, or existing
passing behavior was changed. The Phase 5 review MEDIUM finding that
`ArtworkPublisher.PublishCoreAsync` reads the durable lifecycle fence once is not
made reachable by this task because the reconciliation processor does not drive
artwork publication; it remains open for the later task that wires artwork
publication into the worker (6.4/6.6).

### Task 6.4 - Recover non-terminal artwork operations before accepting new work

Task 6.4 drives the task 5.7 durable artwork-operation recovery entry point from
the Phase 6 pipeline (`docs/architecture.md` section 9 "Restart reconciliation";
`docs/data-model.md` sections 3.10.3-3.10.4; ADR-003; ADR-002) and closes the
Phase 5 review MEDIUM lifecycle-fence race. It does not implement metadata
freshness/staleness (6.5), fingerprint-driven artwork regeneration/invalidation
(6.6), webhooks (6.7), or the restart/outage/pressure test matrix (6.8).

- `src/ArrTags/Artwork/ArtworkRecoveryGate.cs`,
  `IArtworkRecoveryGate.cs`, `ArtworkRecoveryGateResult.cs`,
  `ArtworkRecoveryGateOutcome.cs`, and `ArtworkRecoveryScanResult.cs`: the
  provider-neutral per-subject recovery gate and its bounded startup scan. The
  gate reads the durable `ArtworkOperation` record and, when a non-terminal
  operation exists, reconciles it through the existing `ArtworkReconciler` under
  the current durable lifecycle fence and re-reads the record as a postcondition.
  New work proceeds only when the record is absent, already terminal, or reached
  a terminal outcome; a corrupt record, a non-terminal result, or an older
  durable generation fails closed. The gate reuses the pure
  `ArtworkRecoveryDecisions` table and the shared `ArtworkSubjectGate`, exposes
  the authoritative durable generation so accepted work can only supersede it
  through the store's monotonic generation fence, and never recaptures the
  current image as a new source, mutates a changed or unverifiable image, or
  deletes an artifact not proven non-active.
- `src/ArrTags/Updates/ArtworkRecoveringWorkItemProcessor.cs`: composes the gate
  ahead of the unchanged, artwork-free `MetadataReconciliationProcessor` behind
  the `IWorkItemProcessor` boundary. A queued item defers (bounded retry) or
  fails closed instead of being processed while its subject has a non-terminal
  artwork operation.
- `src/ArrTags/PluginLifecycle/ArtworkStartupRecoveryService.cs`: a hosted
  service that runs one bounded, cancellation-aware startup scan over the
  persisted operation records, limited by
  `OperationalLimits.ReconciliationBatchSize`. The scan never blocks host
  startup and is cancelled and awaited within a bounded timeout on shutdown; any
  record beyond the batch is recovered lazily by the per-subject gate before that
  subject's next work item.
- `src/ArrTags/Artwork/ArtworkPublisher.cs`: re-reads and enforces the durable
  lifecycle fence immediately before the first image mutation (aborting the
  operation without an external effect) and again before the final
  `PublishedArtworkState` commit (recording the verified postcondition but
  leaving the final commit to reconciliation). An in-flight publication can no
  longer cross a disable or uninstall drain raised after the operation was
  prepared.
- `src/ArrTags/PluginLifecycle/ArrTagsServiceRegistrator.cs`: registers the
  recovery gate, keeps `MetadataReconciliationProcessor` as the concrete
  artwork-free processor, wires the recovering decorator as `IWorkItemProcessor`,
  and adds the startup recovery service after the lifecycle service and before
  the work worker.
- Tests: `tests/ArrTags.Tests/ArtworkRecoveryGateTests.cs` (15 cases) plus
  updated `MetadataReconciliationProcessorTests` and `LifecycleFoundationTests`
  registration checks. They cover recovery-before-new-work, already-terminal and
  no-operation fast paths, deferral/terminal classification at the decorator,
  corrupt-state fail-closed, fail-closed artifact retention with no mutation,
  externally changed image handling, no-recapture of the active image as a new
  source, generation-fence rejection of stale writes, the startup scan bound and
  cancellation, the configured startup batch, and two interleaved
  drain/publication races that prove no untracked non-terminal publication
  survives the fence.

Build and test: `./build.sh build` (0 warnings, 0 errors) and `./build.sh test`
pass. The default suite passes 1047 with 58 environment-guarded skips (1105
total), exactly +15 over the task 6.3 baseline (1032/58/1090), with no new skips
and no regressions. No ADR, `RenderVersion`, renderer behavior, or existing
passing behavior was changed. The Phase 5 review MEDIUM lifecycle-fence finding
is closed: the publisher enforces the durable fence before the first mutation and
before the final commit, and the interleaved drain/publication tests demonstrate
that no untracked publication survives the fence.

### Task 6.5 - Metadata freshness, bounded stale-last-known-good, and artifact retention

Task 6.5 separates metadata freshness and bounded stale-last-known-good behavior
from artwork retention and eviction, and schedules bounded artifact retention/GC
(`docs/architecture.md` sections 6, 8, 9, and 12; `docs/data-model.md` sections
3.9, 3.10, and 6; ADR-004; ADR-002/ADR-003). It does not implement
fingerprint-driven artwork regeneration/invalidation (6.6), webhooks (6.7), or
the restart/outage test matrix (6.8).

- `src/ArrTags/Reconciliation/MetadataFreshness.cs`: the effective-freshness
  vocabulary (`Fresh`, `Stale`, `Expired`, `Unmatched`, `Unavailable`,
  `Invalid`). Only `Fresh` and `Stale` are usable as current metadata.
- `src/ArrTags/Reconciliation/MetadataStateEntry.cs`: computes `expiresAt` and
  `staleUntil` from the ADR-004 `MetadataStaleWindowMinutes` window, which is the
  total bounded last-known-good lifetime (an observation is fresh for the first
  half of the window and bounded last-known-good for the remaining half, so
  `expiresAt = fetchedAt + window / 2` and `staleUntil = fetchedAt + window`),
  enforces the freshness-boundary invariants on a fresh/stale record, and exposes
  `EvaluateFreshness`, `IsUsableAsCurrent`, `IsExpired`, and a bounded `ToStale`
  conversion that preserves the snapshot and timestamps unchanged.
  `src/ArrTags/Configuration/OperationalLimits.cs` exposes the ADR-004 default
  window constant.
- `src/ArrTags/Reconciliation/MetadataReconciliationProcessor.cs`: publishes the
  computed window from the current snapshot and, on a transient
  (`ProviderUnavailable`) read failure, keeps a still-usable last-known-good
  snapshot as explicit `Stale` without extending the bounded window; after the
  window the snapshot is neither kept nor refreshed as current. The processor
  remains artwork-free.
- `src/ArrTags/Reconciliation/MetadataStateStore.cs`: `ApplyRetention` prunes
  only expired metadata records, so metadata usability and retention are governed
  by freshness rather than the render work-cache policy.
- `src/ArrTags/State/StateRetention.cs` and `StateRepository.cs`:
  `ApplyCacheRetention`/`ApplyRetention` accept an exempt-kind set so metadata
  last-known-good records are never evicted by the render-cache TTL or quota.
- `src/ArrTags/Artwork/ArtifactRetention.cs`: the bounded authoritative artifact
  GC. It protects the active image content hash, a live session's retained source
  artifact, and every artifact referenced by a non-terminal or recovery-blocked
  operation; it retains a terminal-provenance source baseline only for the
  terminal retention window; it reclaims superseded derived render output so the
  authoritative quota can be reused; it applies a bounded just-promoted grace
  period; it re-checks the authoritative references immediately before each
  delete so an artifact re-referenced after the reference snapshot cannot be
  removed; and it fails closed when an authoritative state or operation record
  cannot be validated. It never mutates an image and never calls Jellyfin.
- `src/ArrTags/Artwork/SourceArtifactStore.cs`: bounded artifact identifier
  enumeration, a last-write-time lookup for the just-promoted grace period, and
  an explicit retention delete of the bytes and manifest.
- `src/ArrTags/PluginLifecycle/StateRetentionService.cs`: the hosted, bounded,
  cancellation-aware maintenance loop that schedules `StateRepository.ApplyRetention`,
  metadata freshness retention, and artifact GC in production. It runs its first
  pass on a tracked background task, never blocks startup, and cancels and awaits
  within a bounded timeout on shutdown. `ArrTagsServiceRegistrator` registers the
  artifact retention policy and the hosted service.
- Tests: `tests/ArrTags.Tests/MetadataFreshnessTests.cs` (10 cases),
  `ArtifactRetentionTests.cs` (11 cases), `StateRetentionServiceTests.cs` (4
  cases), plus extended `MetadataReconciliationProcessorTests` and
  `MetadataStateStoreTests`. They cover fresh/stale/expired transitions, the
  computed half/full boundaries, total last-known-good equal to the configured
  window, the ADR-004 default, stale conversion without window extension,
  unmatched expiry, invalid fresh-without-boundaries, metadata retention pruning
  only expired records, repository retention exempting metadata state, outage
  fallback within and after the window, no-LKG outage,
  active/source/non-terminal/recovery-blocked artifact protection, terminal
  provenance retention and release, fail-closed corrupt state, orphan bytes and
  manifest deletion, the just-promoted grace period, quota reclamation on
  repeated publication, render-cache retention never touching artifacts, the
  hosted loop, and an unstarted-stop safety check.

Build and test: `./build.sh build` (0 warnings, 0 errors) and `./build.sh test`
pass. The default suite passes 1075 with 58 environment-guarded skips (1133
total), exactly +28 over the task 6.4 baseline (1047/58/1105), with no new skips
and no regressions. No ADR, `RenderVersion`, renderer behavior, or existing
passing behavior was changed. The Phase 5 review MEDIUM finding #4 (no artifact
garbage collection, the authoritative quota is never reclaimed, and
`ApplyRetention` was invoked only from tests) is addressed: `StateRetentionService`
schedules retention in production and `ArtifactRetention` reclaims superseded
render output so the authoritative quota can be reused on repeated publication,
while active, owned, and recovery-relevant artifacts are never evicted. The
Phase 5 review MEDIUM finding that no production caller selects the retained
source for a repeat publication remains task 6.6.

### Task 6.6 - Fingerprint-driven artwork regeneration and publication invalidation

Task 6.6 wires artwork generation into the Phase 6 pipeline behind a
publication-fingerprint gate, so unchanged inputs do not render or publish again
and a changed input regenerates only the affected artwork.

- `src/ArrTags/Artwork/ArtworkRegenerationPlanner.cs` and
  `ArtworkRegenerationDecision.cs`: the pure fingerprint gate. It computes the
  logical publication fingerprint with the same `RenderFingerprint` and
  `BadgeDefinitionResolver` the renderer uses (source-artwork identity, canonical
  metadata fingerprint, resolved selection, secret-free renderer configuration
  fingerprint, renderer and badge-schema versions) and compares it with the
  persisted `PublishedArtworkState.PublishedFingerprint` and `RendererVersion`.
  It skips with no work when the fingerprint and renderer version are unchanged,
  when the metadata state is not usable as current under the task 6.5 freshness
  policy, when the ownership state does not permit publication, or when an absent
  baseline cannot be re-rendered. A changed metadata, source, configuration,
  renderer, or schema input requires regeneration; a subject without an owned
  session requires a new recoverable source baseline.
- `src/ArrTags/Artwork/ArtworkGenerationCoordinator.cs`: selects the render
  source. An owned `Published`/`NotPublished` session renders from the
  integrity-validated retained original source artifact and never from the current
  active surface, so a repeat publication cannot stack a badge onto a previous
  ArrTags output; a missing, corrupt, or dimension-less retained baseline fails
  closed instead of re-capturing the derived image. The constructor now receives
  the authoritative published state and source artifact stores.
- `src/ArrTags/Reconciliation/MetadataReconciliationResult.cs` and
  `MetadataReconciliationProcessor.cs`: `ReconcileAsync` returns the unchanged
  worker classification plus the live canonical context (identity, match, metadata,
  and the published metadata state entry) when atomic state was published.
  `ProcessAsync` preserves the existing `IWorkItemProcessor` behavior and the
  processor remains artwork-free.
- `src/ArrTags/Updates/ArtworkPublishingWorkItemProcessor.cs`: runs the unchanged
  reconciliation first and, only for a published metadata state, evaluates the gate
  and drives the coordinator for the V1 unindexed Primary surface. A renderer or
  publication failure preserves the current artwork without retrying the already
  successful metadata publication, and cancellation is propagated.
- `src/ArrTags/PluginLifecycle/ArrTagsServiceRegistrator.cs`: registers the
  publishing processor and composes the task 6.4 `ArtworkRecoveringWorkItemProcessor`
  over it over the metadata processor, and passes the published state and source
  artifact stores to the coordinator. Recovery-before-new-work, the publisher's
  durable fence re-checks, and the durable write-ahead protocol are unchanged.
- Tests: `ArtworkPublicationPipelineTests.cs` (6 cases),
  `ArtworkRegenerationPlannerTests.cs` (12 cases),
  `RendererConfigurationIntegrationTests.cs` (1 case), `ArtworkPipelineFixtures.cs`,
  and two retained-source cases in `ArtworkGenerationCoordinatorTests.cs`, plus the
  extended `LifecycleFoundationTests` DI resolution. They cover the unchanged
  no-op, regeneration on each changed input (metadata, source, configuration,
  renderer version, schema version), the retained-source repeat render without
  double-badging, corrupt baseline fail-closed, unusable and expired metadata
  no-publish, blocked and absent ownership states, non-terminal-operation
  preservation, the configuration-to-render integration, and the DI composition.

Build and test: `./build.sh build` (0 warnings, 0 errors) and `./build.sh test`
pass. The default suite passes 1096 with 58 environment-guarded skips (1154
total), exactly +21 over the task 6.5 baseline (1075/58/1133), with no new skips
and no regressions. No ADR, `RenderVersion`, renderer behavior, or existing
passing behavior was changed. The Phase 5 review MEDIUM "Cross-task integration
(5.8 repeat publication / Phase 6 pipeline)" is closed: the coordinator renders a
repeat publication from the retained original and a test proves no double badge.
The Phase 5 review MEDIUM "Test coverage carried forward from Phase 4" is closed
by `RendererConfigurationIntegrationTests`. The Phase 5 review LOW "Robustness /
repeat publication" is addressed by the retained-artifact validation, and the LOW
"Memory / scalability" per-subject gate bound is documented as accepted in
`docs/architecture.md` section 9. The Phase 5 review LOW "Configuration / DI
lifetime" is unchanged and remains tracked for a later task: the artwork
singletons still capture `OperationalLimits` at first resolution.

### Task 6.7 - Inbound Arr webhook boundary (DG-7)

Task 6.7 resolves decision gate DG-7 with `docs/decisions.md` ADR-012 and
implements the authenticated, bounded inbound Arr webhook boundary in
`src/ArrTags/Webhooks`. A webhook is a hint, never a source of truth: it
authenticates with the configured shared secret through the ADR-005 webhook
lease, bounds and tolerantly parses the payload, and feeds the existing bounded,
deduplicated work-hint path so the worker re-reads current Jellyfin and Arr
state.

- `src/ArrTags/Webhooks/ArrTagsWebhookController.cs`: an anonymous plugin
  `ControllerBase` discovered by Jellyfin's plugin controller registration,
  exposing `POST /ArrTags/Webhook/Sonarr` and `POST /ArrTags/Webhook/Radarr`. It
  authenticates the `X-ArrTags-Webhook-Secret` header first, enforces the
  configured payload bound, parses the payload, performs a non-blocking bounded
  submit, and returns only bounded safe status codes (`202`, `400`, `401`,
  `413`). It never logs, returns, or retains the secret, headers, or body.
- `src/ArrTags/Webhooks/WebhookAuthentication.cs`: constant-time authentication
  through `TryAcquire(SecretReference.WebhookAuthentication, version)` and
  `SecretLease.Matches`. A missing configuration, a missing candidate, an
  oversized candidate, a rotated generation, and a mismatch all fail closed with
  the same safe status.
- `src/ArrTags/Webhooks/WebhookBodyReader.cs` and `WebhookBodyReadResult.cs`: a
  hard bounded body read that rejects a declared or streamed oversize before
  buffering the whole body.
- `src/ArrTags/Webhooks/WebhookEventParser.cs`, `WebhookEvent.cs`,
  `WebhookEventType.cs`, and `WebhookParseError.cs`: a bounded, tolerant parser
  (bounded JSON depth and episode count, unknown fields and casing tolerated)
  that maps only the event kind, upgrade flag, and provider record/file hints.
  Empty, malformed, truncated, or wrong-shaped payloads are rejected; an
  unsupported event kind is acknowledged and produces no work.
- `src/ArrTags/Webhooks/WebhookIntake.cs`, `IWebhookIntake.cs`,
  `WebhookCoalescingWindow.cs`, and `WebhookCoalesceKey.cs`: a bounded,
  non-blocking intake that suppresses duplicate, out-of-order, and replayed
  deliveries within a short bounded window and drops overflow without blocking
  or growing without bound.
- `src/ArrTags/Webhooks/WebhookReconciliationResolver.cs`: bounded
  provider-record-to-Jellyfin resolution. It looks up only the Jellyfin items
  ArrTags already associated with the advertised provider record in the
  persisted metadata-state mapping for the resolved connection, bounded by
  `OperationalLimits.ReconciliationBatchSize`; it never trusts the payload as
  current state and never grants permission to work on an arbitrary item.
- `src/ArrTags/Webhooks/WebhookIntakeService.cs`: the hosted consumer that
  resolves accepted events off the request path and enqueues the same bounded
  `LibraryWorkHint` work as every other trigger, with a bounded shutdown.
- `src/ArrTags/Configuration/OperationalLimits.cs`:
  `WebhookMaxPayloadBytes` (default 256 KiB, range 4 KiB to 4 MiB).
- `src/ArrTags/Reconciliation/MetadataStateStore.cs`: a bounded `Enumerate`
  accessor used only by the bounded webhook resolution.
- `src/ArrTags/PluginLifecycle/ArrTagsServiceRegistrator.cs`: registers the
  resolver, the intake, and the hosted intake service after the work worker.
- Tests: `WebhookBoundaryTests.cs` (34 cases) and `WebhookResolutionTests.cs`
  (11 cases) covering authentication success/failure, the constant-time lease
  path, declared and streamed size limits, malformed/truncated/empty/
  wrong-shaped payloads, unsupported event types, replay/duplicate coalescing,
  bounded overflow and coalescing windows, hint mapping, safe status codes, the
  controller registration contract, the no-write/no-publication dependency
  boundary, the new limit validation, provider-record resolution,
  disabled/mismatched/unsupported no-ops, series and episode scoping, the
  batch-size bound, and the hosted service feeding the deduplicated queue.

Build and test: `./build.sh build` (0 warnings, 0 errors) and `./build.sh test`
pass. The default suite passes 1141 with 58 environment-guarded skips (1199
total), exactly +45 over the task 6.6 baseline (1096/58/1154), with no new skips
and no regressions. DG-7 is resolved by ADR-012; `docs/architecture.md` sections
4, 5, 6, 8, 11, 12, and 14 and `docs/data-model.md` sections 3.3, 4.11, and 7
were updated to match the implemented behavior. No queue, worker, publication,
recovery, freshness, or regeneration mechanic was changed and no secret enters a
queue item, cache, fingerprint, or diagnostic.

### Task 6.8 - Restart, shutdown, corruption, outage, recovery, duplicate, pressure, and cancellation verification

Task 6.8 is the Phase 6 verification task. It adds a focused end-to-end test
matrix over the complete Phase 6 pipeline (`docs/architecture.md` sections 5, 6,
8, 9, 11, 12, and 13; `docs/data-model.md` sections 3.9, 3.10.x, 6, and 7; ADR-002
through ADR-005, ADR-009 through ADR-012). It adds no production behavior change
and no new feature; it composes the existing production graph - the durable
stores, the reconciliation processor, the artwork publication pipeline, the
per-subject recovery gate, the lifecycle drain coordinator, and the bounded work
queue and worker - over one plugin data directory using only injectable
host/renderer doubles; the bounded webhook intake and its coalescing/replay
behavior are exercised directly by the duplicate-event test rather than composed
in the harness. No live Jellyfin or Arr
instance is required, and the tests do not skip in this environment.

- `tests/ArrTags.Tests/Phase6Harness.cs`: the composed Phase 6 harness plus the
  injectable host source/image boundary (`Phase6Host`, with pause/cancel/crash
  hooks), the hooked fingerprinting renderer (`Phase6Renderer`), and the
  lifecycle fence provider double. `Phase6Harness.Restart()` rebuilds every
  in-memory service over the same durable directory, so a test proves the
  queue/worker, metadata state, freshness, artwork recovery, and retention make
  the same guarded decisions without in-memory state.
- `tests/ArrTags.Tests/Phase6Doubles.cs`: the blocking processor, the recording
  processor (per-key peak concurrency), and the asynchronous provider-reader
  double used by the shutdown, duplicate/pressure, and cancellation tests.
- `tests/ArrTags.Tests/Phase6RestartTests.cs` (4 cases): metadata state,
  freshness, and published-artwork ownership/provenance survive a restart with no
  redundant render or image mutation; a crashed non-terminal publication is
  recovered before new work (an inner probe asserts the operation was terminal
  first); a restart retention pass prunes only expired metadata while preserving
  the live session, source, and active derived artifacts; and the production
  startup scan recovers the durable non-terminal operation from durable state
  alone.
- `tests/ArrTags.Tests/Phase6ShutdownTests.cs` (4 cases): graceful shutdown
  cancels queued and in-flight work within the bound, rejects new work, and never
  starts abandoned work; a shutdown during the image mutation leaves no partial
  image and no committed state; a shutdown that races the publisher fence leaves a
  verified postcondition that the lifecycle drain then restores with no untracked
  non-terminal operation; and a long retry backoff is cancelled without another
  attempt.
- `tests/ArrTags.Tests/Phase6CorruptionTests.cs` (5 cases): a torn metadata cache
  is discarded and rebuilt after restart without blocking startup and without
  touching published artwork; incompatible and semantically invalid metadata
  cache records are discarded; a corrupt authoritative artwork-state record fails
  closed for the composed pipeline and for the publisher with no image mutation
  and no artifact cleanup; and a corrupt authoritative operation record is
  quarantined and fails closed without blind replay or cleanup.
- `tests/ArrTags.Tests/Phase6OutageTests.cs` (3 cases): a temporary provider
  outage keeps bounded last-known-good metadata (window not extended) and leaves
  the current artwork byte-for-byte unchanged; after the stale window the expired
  snapshot is neither kept nor used and the planner refuses to generate from it;
  and an outage beyond the bounded work retries flags the snapshot as bounded
  stale with the artwork untouched throughout.
- `tests/ArrTags.Tests/Phase6RecoveryTests.cs` (3 cases): the durable generation
  fence rejects a stale operation and a second operation at the same generation,
  accepting only the next generation; the durable lifecycle fence rejects a
  changed-fingerprint publication with no image mutation, after which the disable
  drain restores the baseline; and a corrupt lifecycle fence fails closed toward
  restoration rather than a normal publication window.
- `tests/ArrTags.Tests/Phase6DuplicateEventTests.cs` (3 cases): duplicate and
  out-of-order library events coalesce into one single-flight processing and one
  publication, and a replay reuses the unchanged fingerprint with no second render
  or mutation; duplicate and replayed webhook deliveries coalesce while a distinct
  event kind is admitted and bounded overflow drops; and duplicate hints never
  grow the queue beyond capacity.
- `tests/ArrTags.Tests/Phase6QueuePressureTests.cs` (3 cases): concurrent
  overflow never blocks and stays within the bounded capacity; a slow provider
  while thousands of library events are delivered synchronously never blocks
  delivery and keeps the queue bounded; and overflow drains and then accepts new
  work once capacity is freed.
- `tests/ArrTags.Tests/Phase6CancellationTests.cs` (4 cases): cancellation during
  a provider read, a render, and the image mutation publishes no partial
  metadata, publication state, or image, and preserves the durable intent for a
  later safe recovery; and an already-cancelled work item publishes nothing.

Build and test: `./build.sh build` (0 warnings, 0 errors) and `./build.sh test`
pass. The default suite passes 1170 with 58 environment-guarded skips (1228
total), exactly +29 over the task 6.7 baseline (1141/58/1199), with no new skips
and no regressions; the host-guarded facts and the native Skia facts continue to
skip as expected on this Alpine/musl environment. No production behavior, ADR,
`RenderVersion`, renderer behavior, or existing passing test was changed. Gate 6
is not self-declared: the Phase 6 review that confirms the recorded operational
limits and the phase acceptance criteria is separate.

Observed pre-existing limitation (not introduced or changed by task 6.8): an
invalid authoritative operation or artwork-state record is quarantined on the
read that detects it, and a later read of the same subject then observes the
record as absent. The failing read (the recovery gate for the operation record,
or the publisher/pipeline for the artwork state) is the one that fails closed;
this matches the task 6.4 documented quarantine semantics and the accepted
quarantine/recovery model, and task 6.8 asserts that first-read contract.

### Task 6.9 - Scheduled, post-scan, and manual reconciliation; provider and render concurrency enforcement

Task 6.9 was added to Phase 6 after the independent phase review found a HIGH
deliverable gap and a MEDIUM concurrency-enforcement gap, and it resolves both.
No webhook, metadata-publication, recovery, freshness, or regeneration semantics
were changed.

Reconciliation triggers. `src/ArrTags/Reconciliation` gains the provider-neutral
`LibraryReconciliationService`. It enumerates the candidate movie and episode
items in pages bounded by `OperationalLimits.ReconciliationBatchSize` through the
new `IMediaLibraryEnumerator`/`JellyfinMediaLibraryEnumerator` read boundary
(`src/ArrTags/Media`, backed by the supported `ILibraryManager` query surface),
filters each page through the existing `MediaIdentityFactory` and
`MediaEligibility` boundaries against the current configuration snapshot, checks
cancellation between and inside pages, yields between batches, and enqueues only
the same bounded `LibraryWorkHint` work as every other trigger through
`IWorkHintSink`. It never calls a provider, renderer, publisher, or image API, and
it stops as soon as the durable `ArtworkLifecycleFenceStore` refuses new
publication work. `LibraryWorkReason` gains the bounded `Reconciliation` reason.
A new `LibraryReconciliationResult` reports the bounded inspected/eligible/enqueued
counts and a safe reason.

`ArrTagsReconciliationTask` (`src/ArrTags/PluginLifecycle`) implements the
Jellyfin 12 `IScheduledTask` contract with a default 12-hour interval trigger and
manual execution through Jellyfin's scheduled-task surface; it is cancellable,
progress-reporting, and propagates cancellation so Jellyfin records the task as
cancelled. `ArrTagsPostScanTask` implements the dedicated Jellyfin 12
`ILibraryPostScanTask` extension point that `LibraryManager` invokes after a
media-library scan with the scan's progress and cancellation token; no fallback
limitation had to be recorded because the supported post-scan hook exists. Both
are public concrete plugin types discovered by Jellyfin's assembly scanning
(`ITaskManager.AddTasks(GetExports<IScheduledTask>)` and
`ILibraryManager.AddParts(GetExports<ILibraryPostScanTask>)`) and are also
registered in DI with their dependencies; registration and construction perform
no provider, rendering, or library work.

Concurrency enforcement (ADR-004). `src/ArrTags/Concurrency` adds a bounded,
cancellation-aware `DynamicConcurrencyLimiter` whose effective limit is resolved
from the current configuration snapshot on every acquisition, and a
`ProviderConcurrencyLimiter` that acquires the global then the per-connection
permit (no lock-order cycle, no unbounded queue). The
`ConcurrencyLimitedArrMetadataReader<TReader>` decorator
(`src/ArrTags/Reconciliation`) bounds every provider reconciliation read, and
`ConcurrencyLimitedRenderer` (`src/ArrTags/Rendering`) bounds every render. The
DI registration wraps both provider readers and the renderer with these
decorators while keeping exactly two `IArrMetadataReader` registrations and one
factory-based `IRenderer` registration.

Tests. `ReconciliationTriggerTests` (18 cases) covers page batching by the
configured size, scope/eligibility filtering, episodes, no-enabled-provider
short-circuit, the fence before and during a run, cancellation between pages and
a pre-cancelled token, bounded progress, integration with the real
`LibraryWorkQueue`, the periodic trigger and manual surface, cancellation
propagation, DI registration and assembly-scan discoverability, and the
no-provider/no-publication dependency boundary. `ConcurrencyLimitTests` (9 cases)
proves the configured values actually cap concurrency for the dynamic limiter,
the per-connection and global provider limiters, the reader decorator, and the
render decorator, and that the limits are re-read from the current snapshot on
each acquisition.

Build and test: `./build.sh build` (0 warnings, 0 errors) and `./build.sh test`
pass. The default suite passes 1197 with 58 environment-guarded skips (1255
total), exactly +27 over the task 6.8 baseline (1170/58/1228), with no new skips
and no regressions; the host-guarded and native Skia facts continue to skip as
expected on this Alpine/musl environment. Gate 6 is not self-declared here; the
phase re-review is separate.

Deferrals recorded (not implemented, not presented as solved): a bounded
provider inventory/catalogue cache (provider fetch efficiency), runtime
configuration replacement wiring (`ConfigurationSnapshotService.TryReplace` is
not observed until restart), and a safe metrics/diagnostic-status surface. These
are tracked in the `PLANS.md` Post-V1 Backlog open-items list, in
`docs/implementation-readiness.md`, and (consolidated) in `docs/limitations.md`.

## Phase 7 - Testing & release (Milestone 7)

**Status:** Complete. DG-9 resolved by ADR-013; task 7.1 complete (the full suite passes from a clean rebuild against the declared versions); task 7.2 complete (the live install/upgrade/reload/uninstall verification found the release-blocking versioned-install/data-folder collision); task 7.7 complete (the state root is relocated outside `PluginsPath` by ADR-014, with regression tests and a re-run live verification that now passes, so Phase 7 acceptance criterion 2 is met); task 7.3 complete (the live GOALS success-criteria verification met criteria 1-4 and 9, covered criterion 7 at the contract level, and found release blocker 7.3-F1 - the bundled `SkiaSharp.dll`/`libSkiaSharp.so` collide fatally with the host's own SkiaSharp and abort Jellyfin on the first badge publication - so `GOALS.md` criteria 5, 6, and 8 are not met as shipped and Phase 7 acceptance criteria 3 and 4 remain unchecked); task 7.8 complete (the duplicate SkiaSharp runtime is no longer shipped and the plugin shares the host's SkiaSharp by ADR-015, with regression coverage and a re-run live end-to-end verification that passes, so `GOALS.md` criteria 5 and 8 are met as shipped, with criterion 6 met for render and publication but only partial for provider fetches (`docs/limitations.md` F1), and Phase 7 acceptance criteria 3 and 4 are met); task 7.4 complete (the logs/diagnostics/HTTP-behavior/persisted-state secret-leakage and unbounded-data review executed live on the pinned host returned a negative result - no credential leakage and no unbounded path); task 7.5 complete (the release package builds byte-reproducibly from a clean checkout and the build/release process, inputs, artifact identity, and supported version ranges are recorded in `docs/release/build-and-release.md`, meeting Phase 7 acceptance criterion 5); task 7.6 complete (the known limitations and deferred decisions are consolidated in `docs/limitations.md` so nothing unsupported is presented as available). All five Phase 7 acceptance criteria are met; Gate 7 is not declared because the phase review is separate.

### Decision - Supported provider release ranges and optional-field compatibility (DG-9)

DG-9 is resolved by ADR-013 (`docs/decisions.md`). V1 supports Sonarr 3.x-4.x and
Radarr 3.x-6.x through the pinned `/api/v3` contract, with the in-development
Sonarr v5 surface and Sonarr 2.x and earlier excluded. Compatibility is
behavioural rather than a version-number gate: the probe requires the exact
`appName` and the `/api/v3` contract, records the observed `version` for
diagnostics, keeps absent optional technical fields as explicit unknowns, and
fails closed with `ArrProviderErrorCode.ProviderIncompatible` on a malformed or
missing required field without changing the current usable artwork. Ownership:
task 7.1 exercises the declared provider lines with contract fixtures, task 7.5
records the ranges in the release artifact, and task 7.6 documents the ranges and
the optional-field policy. The provider inventory/catalogue cache, runtime
configuration replacement wiring, the reconciliation coverage bound, the ADR-010
non-canonical runtime comparison, and the metrics/diagnostic-status surface remain
open and are not presented as solved; they are consolidated in
`docs/limitations.md` by task 7.6.

### Task 7.1 - Full unit and integration suite against the declared versions

**Status:** Complete.

The full unit and integration suite runs against the exact declared versions:
`net10.0` with the SDK pinned by `global.json` to `10.0.0` (rollForward
`latestMinor`), `Jellyfin.Controller` and `Jellyfin.Model` `12.0.0` with manifest
`targetAbi: 12.0.0.0`, SkiaSharp `3.119.4` for the `linux-x64` plugin RID,
xunit `2.9.3`, Microsoft.NET.Test.Sdk `18.10.1`, xunit.runner.visualstudio
`3.1.5`, and plugin version `0.1.0.0`. The suite was rebuilt from clean sources:
`src`/`tests` `bin`/`obj` were removed, then `./build.sh restore`,
`./build.sh build`, and `./build.sh test` were run. The build reported 0 warnings
and 0 errors, and the suite reported Failed 0, Passed 1215, Skipped 58, Total
1273 - exactly +18 over the Phase 6 baseline (1197/58/1255) with no new skips and
no regressions. The 58 skips are the existing host-guarded, native-Skia,
packaged-plugin, and non-canonical-runtime facts that skip on this Alpine/musl
environment, unchanged from the baseline.

ADR-013 provider contract fixtures were added in
`tests/ArrTags.Tests/ProviderContractFixtureTests.cs` (18 cases). They exercise
every declared line (Sonarr 3.x and 4.x; Radarr 3.x, 4.x, 5.x and 6.x) through the
probe with the observed version recorded and no numeric gate, cover a fully
populated payload mapping to canonical badge metadata and a sparse/optional-missing
payload mapping to explicit unknowns for both providers, and confirm a missing
required identity field fails closed as `ProviderIncompatible` with `Incompatible`
health. No production behavior changed.

### Task 7.2 - Install, upgrade, reload, and uninstall verification on the pinned host

**Status:** Complete (verification executed; Phase 7 acceptance criterion 2 is
**not met**: a release blocker was found).

The packaged plugin (`./build.sh package` ->
`artifacts/ArrTags_0.1.0.0.zip`, containing `ArrTags.dll`, `SkiaSharp.dll`,
`libSkiaSharp.so`, `ArrTags.deps.json`, `build.yaml`, `THIRD-PARTY-NOTICES.md`,
and `licenses/`) was installed into the pinned Jellyfin `12.0.0` musl host. The
host's real plugin path was determined empirically to be
`<prefix>/data/plugins` (Jellyfin's `ProgramDataPath`/`--datadir`), **not**
`<prefix>/config/plugins`; `docs/testing/jellyfin-12-musl-test-host.md` and
`scripts/provision-jellyfin-test-host.sh` were corrected accordingly. The host
logged `Loaded plugin: ArrTags 0.1.0.0`, wrote an Active `meta.json`, and
reported no load errors, validating the pinned-host load of the
`Plugin(IServiceProvider)` constructor. A real `0.1.0.0` -> `0.1.0.1` upgrade
loaded only the newer version and the host deleted the older versioned folder; a
restart reloaded the plugin exactly once (Jellyfin has no in-process plugin
reload); removing the folder uninstalled cleanly. The host-guarded suite
(`ARRTAGS_JELLYFIN_HOST_DIR=<prefix>/jellyfin`) reported Failed 0, Passed 1229,
Skipped 44, Total 1273 (14 image route/response facts unskipped); the default
suite reported Failed 0, Passed 1215, Skipped 58, Total 1273.

**Release blocker (Phase 7 acceptance criterion 2 not met).** Jellyfin derives a
plugin's `DataFolderPath` as `PluginsPath/<assembly name>` (for ArrTags,
`data/plugins/ArrTags`), and the plugin persists state there. When that data
folder exists alongside the standard versioned install folder
`data/plugins/ArrTags_<version>/`, `PluginManager.DiscoverPlugins` groups them by
manifest name and deletes the install folder on the next host restart (the
`MD5("ArrTags")` auto-manifest GUID sorts after the plugin GUID), so no ArrTags
plugin loads. This was reproduced live with the committed `0.1.0.0` package and
proved against the release `MediaBrowser.Common.dll`. The unversioned layout
(`data/plugins/ArrTags/`, install folder == data folder) and the no-state case
are unaffected. `Plugin.OnUninstalling` could not be exercised live (the
uninstall API returns HTTP `401` while the startup wizard is incomplete); the
drain remains covered in-process by `LifecycleFoundationTests` and
`ArtworkLifecycleTests`, and live `ImageSaver` read-back remains unexercised (no
media library item).

### Task 7.7 - Relocate the plugin state root outside the Jellyfin plugins path

**Status:** Complete. Resolves task 7.2 finding 7.2-F1; Phase 7 acceptance
criterion 2 is now met.

The `Plugin` constructor now calls the public
`BasePlugin.SetAttributes(assemblyFilePath, dataFolderPath, version)` contract
(the `IPluginAssembly` method the host loader itself uses) to re-point
`DataFolderPath` to `Path.Combine(ApplicationPaths.ProgramDataPath, "ArrTags")`,
a sibling of the plugins directory and therefore outside
`ApplicationPaths.PluginsPath`. Jellyfin's derived `PluginsPath/<assembly name>`
is no longer used. `AssemblyFilePath` and `Version` are passed through unchanged,
so `CanUninstall`, configuration-file naming, and version reporting are
preserved. The decision, the collision it avoids, the supported-contract
mechanism, the rejected alternatives, and the V1 no-migration consequence are
recorded in `docs/decisions.md` ADR-014.

Uninstall cleanup was preserved explicitly. Inspection of the pinned host source
(`Emby.Server.Implementations/Updates/InstallationManager.UninstallPlugin` ->
`PluginManager.RemovePlugin` -> `DeletePlugin`) shows the host deletes only
`plugin.Path` (the install folder), never `DataFolderPath`; the Phase 5
assumption that the host deletes the data folder held only for the unversioned
layout where the two were the same directory. `Plugin.OnUninstalling` now runs
the bounded synchronous uninstall drain and, only when the drain result is
complete (every operation terminal and every owned surface restored), deletes
its own relocated state root; an incomplete or cancelled drain retains the
recovery records. Cleanup is best-effort and never throws into the host.

Regression tests. `tests/ArrTags.Tests/PluginStateLocationTests.cs` (4 facts)
asserts the relocated data folder is outside `PluginsPath`, that a versioned
install folder plus a real persisted state write does not create a same-named
`PluginsPath/ArrTags` folder, that a completed uninstall drain removes the state
root, and that an incomplete drain retains it. Both location facts fail against
the previous derivation. `tests/ArrTags.Tests/PluginDiscoveryHostTests.cs` (2
host-guarded facts) executes the pinned host's real `PluginManager` discovery
from `Emby.Server.Implementations.dll` and proves the versioned install folder is
deleted when state lives at `PluginsPath/ArrTags` and is preserved when the state
root comes from the production `Plugin`. The facts skip when
`ARRTAGS_JELLYFIN_HOST_DIR` is unset and fail when it is set but the pinned
assembly is missing.

Live re-verification on the pinned Jellyfin `12.0.0` musl host
(`http://127.0.0.1:8096`, `/tmp/jf`, plugins at `/tmp/jf/data/plugins`). A real
versioned state record was written at the production location with production
code (`Plugin.DataFolderPath` = `/tmp/jf/data/ArrTags`, confirmed by a net10.0
probe that constructed the real `Plugin` and wrote an
`artwork-lifecycle-fence/active.json` through `StateRepository`). Installing the
rebuilt package as `ArrTags_0.1.0.0` and restarting loaded
`Loaded plugin: ArrTags 0.1.0.0` with the state present, and the install folder
and state record both survived the restart (the task 7.2 A/B/A failure is fixed).
A further restart reloaded exactly one instance. A temporary `0.1.0.1` upgrade
loaded only the newer version and the host auto-deleted the older folder; all
temporary version edits were reverted with `git checkout -- build.yaml
Directory.Build.props` and the temporary artifact was removed. Removing the
plugin folder produced zero ArrTags loads and a clean `Startup complete`. Build
0 warnings / 0 errors; default suite Failed 0, Passed 1219, Skipped 60, Total
1279; host-guarded suite (`ARRTAGS_JELLYFIN_HOST_DIR=/tmp/jf/jellyfin`) Failed 0,
Passed 1235, Skipped 44, Total 1279.

### Task 7.3 - GOALS success-criteria verification on the pinned host

**Status:** Complete (verification executed; `GOALS.md` criteria 5, 6, and 8 are
not met as shipped because of release blocker 7.3-F1; Phase 7 acceptance
criteria 3 and 4 remain unchecked).

The `GOALS.md` success criteria were exercised end-to-end on the pinned Jellyfin
`12.0.0` musl host without a live Sonarr/Radarr instance, using new committed
test-support fixtures: `scripts/mock-arr-fixture.cs` (a .NET 10 file-based app,
not part of the solution) and `scripts/mock-arr-fixtures/`, a mock Sonarr/Radarr
`/api/v3` server that enforces `X-Api-Key`, logs every request, and serves
payloads from disk so a changed observation is reproducible; a real Jellyfin
Movie and Series/Episode with local `.nfo` provider ids and real poster images
under `/tmp/7.3/media`; and the committed `artifacts/ArrTags_0.1.0.0.zip`
installed as `data/plugins/ArrTags_0.1.0.0`. Because runtime configuration
replacement is a documented Phase 7 deferral, the plugin was configured by
writing `data/plugins/configurations/ArrTags.xml` and restarting the host.

Met criteria. Criterion 1: the package loaded and remained `Active` with no load
errors. Criterion 2: independent enablement was exercised live - a Sonarr-only
run made only Sonarr calls, a Radarr-only run made only Radarr calls, and both
enabled made both, each with a valid `X-Api-Key` and no request to the disabled
provider. Criterion 3: the movie matched Radarr movie `1`/file `11` by TMDb
`990001` and the episode matched Sonarr series `1`/episode `101`/file `201` by
TVDB `990004`; the persisted metadata state records `Matched`/`ProviderId`.
Criterion 4: actual file quality was retrieved (`Bluray-1080p`/`bluray`/1080 and
`WEBDL-1080p`/`webdl`/1080) together with media-info resolution, dynamic range,
video/audio codecs, channels, custom formats, and upgrade-pending. Criterion 9:
reproducible package and unchanged suite counts (build 0 warnings / 0 errors;
default 1219 passed / 60 skipped / 1279 total; host-guarded 1235/44/1279).

Contract-level only. Criterion 7: `EnhancedCoexistenceTests` passes 7/7, but
Jellyfin Enhanced is not installed on the host, so no live coexistence was
exercised.

Not met as shipped (release blocker 7.3-F1). Criteria 5, 6, and 8 were verified
only after removing the bundled `SkiaSharp.dll` and `libSkiaSharp.so` from the
installed plugin folder. With the committed package, publishing the first badge
through Jellyfin's supported `IProviderManager.SaveImage` path aborts the host
with a fatal `InvalidCastException` between the host default-context
`SkiaSharp.UserDataDelegate` and the plugin-context one
(`/tmp/jf/jellyfin/SkiaSharp.dll` vs
`/tmp/jf/data/plugins/ArrTags_0.1.0.0/SkiaSharp.dll`). The crash is deterministic
(two runs; ~8 ms after the `artwork-operation` record is written), does not occur
with both providers disabled, and does not occur with the plugin uninstalled.
Under the diagnostic SkiaSharp-sharing install the rest of the pipeline is
correct: both badges were served anonymously by `GET /Items/{id}/Images/Primary`
(`image/png`, 30,752 and 32,400 bytes) with the served SHA-256 equal to the
persisted `PublishedArtworkState.ActiveImageIdentity`; the original
`poster.jpg`/`S01E01.jpg` files were byte-unchanged; the retained
`SourceArtifactId` equalled the original poster SHA-256; a changed mock quality
(1080p -> 2160p/HDR10) produced new metadata and publication fingerprints, a new
image tag, and new served bytes; an unchanged re-run left the tag, fingerprint,
and bytes identical; and stopping the mock left the host up with the current
artwork unchanged and the last-known-good metadata marked stale. No production
code or package content was changed by this verification task, and an ADR plus a
`build.yaml`/`PluginPackagingTests` change are required before V1 can be
released.

### Task 7.8 - Host-provided SkiaSharp and live end-to-end re-verification (ADR-015)

**Status:** Complete. Resolves the task 7.3 release blocker 7.3-F1 and re-runs
the live end-to-end verification, so `GOALS.md` criteria 5 and 8 are met as
shipped, with criterion 6 met for render and publication but only partial for
provider fetches (`docs/limitations.md` F1), and Phase 7 acceptance criteria 3
and 4 are met.

The decision is recorded in `docs/decisions.md` ADR-015, which supersedes the
renderer-bundling parts of ADR-010 and the task 4.8 section-3 packaging
constraint, and corrects the native `libSkiaSharp.so` byte-identity claim that
was measured only on the glibc spike host (task 7.3 reviewer finding F2). The
plugin now compiles against the pinned `SkiaSharp` /
`SkiaSharp.NativeAssets.Linux` `3.119.4` packages with
`<ExcludeAssets>runtime</ExcludeAssets>` and no longer ships, copies, or fails on
the renderer runtime. `build.yaml` `artifacts` lists only `ArrTags.dll` and
`ArrTags.deps.json`, and the `PackagePlugin` target stages only the plugin
assembly, dependency manifest, plugin manifest, notices, and licenses. The
package contains no `SkiaSharp.dll` or `libSkiaSharp.so`. `PluginPackagingTests`
asserts the new contract, including a regression that the archive contains
neither duplicate, and the test project references the pinned SkiaSharp runtime
directly so the golden/native renderer tests keep exercising the real stack (the
forced native round trip passes 2/2).

Live re-verification on the pinned Jellyfin `12.0.0` musl host with
`artifacts/ArrTags_0.1.0.0.zip` (568,004 bytes): installed as
`data/plugins/ArrTags_0.1.0.0`, it loaded with `Loaded plugin: ArrTags 0.1.0.0`
and no plugin-folder SkiaSharp assembly load, and no `[ERR]`/`[FTL]`. After the
prior plugin image state was cleared and the items were reset to their original
sidecar posters, the ArrTags scheduled reconciliation published both badges with
0 `[FTL]`/`InvalidCastException`; `GET /Items/{id}/Images/Primary` served
`image/png` 6,112 and 8,315 bytes whose SHA-256 equalled the persisted
`PublishedArtworkState.ActiveImageIdentity` (and the metadata-store `poster.png`);
the original `poster.jpg`/`S01E01.jpg` files were byte-unchanged; the persisted
`SourceArtifactId` equalled the original poster SHA-256; changed mock metadata
(1080p -> 2160p/HDR10) produced new metadata/publication fingerprints, new image
tags, and new served bytes while an unchanged re-run left them identical; a
simulated provider outage (503) left the host up (10/10 HTTP 200) with the
artwork unchanged and both metadata states marked stale; and removing the plugin
folder produced zero loads and a clean `Startup complete`. Build 0 warnings / 0
errors; default suite Failed 0, Passed 1218, Skipped 60, Total 1278; host-guarded
suite (`ARRTAGS_JELLYFIN_HOST_DIR=/tmp/jf/jellyfin`) Failed 0, Passed 1234,
Skipped 44, Total 1278 (the -1 total is the net `PluginPackagingTests` change:
two removed renderer-bundling facts and one added duplicate-asset regression
fact).

### Task 7.4 - Secret-leakage and unbounded-data review (logs, diagnostics, HTTP behavior, persisted state)

**Status:** Complete. Negative result - the review found no credential leakage and
no unbounded path, so no production fix was required and no product code changed.

Real provider and webhook traffic was generated on the pinned Jellyfin `12.0.0`
musl host with distinctive sentinel secrets
(`SONARR-SENTINEL-KEY-1b8d4f6a-DO-NOT-LEAK`,
`RADARR-SENTINEL-KEY-7f3a9c2e-DO-NOT-LEAK`,
`WEBHOOK-SENTINEL-SECRET-9e2c7a5d-DO-NOT-LEAK`, and a wrong
`WRONG-SONARR-SENTINEL-KEY-aaaa1111-DO-NOT-LEAK`) across the successful and
failing paths (provider success, `401` authentication failure, malformed
response, oversized response, request timeout, and webhook success,
missing-secret, wrong-secret, oversized, and malformed deliveries). No sentinel
appeared in `/tmp/jf/console.log`, `/tmp/jf/log`, the plugin state under
`/tmp/jf/data/ArrTags`, the installed plugin folder, or the mock request log. The
only host file that contained a sentinel was Jellyfin's persisted plugin
configuration `data/plugins/configurations/ArrTags.xml`, which ADR-005 designates
as the single source of truth. The plugin source contains no logging call, and
`SecretLease`, `ArrProviderError`, and `ArtworkOperationErrors` bound and redact
diagnostic text. Persisted state records are versioned and SHA-256
integrity-tagged with traversal-safe identifiers and contained only hashes,
identifiers, fingerprints, and timestamps. The anonymous webhook routes returned
uniform `401`/`413`/`400`/`202` outcomes through the constant-time versioned
lease with no secret or sensitive detail reflected; the standard image route
authorization remains Jellyfin's and the plugin exposes only the two webhook
`POST` routes. Every operational bound has an explicit ADR-004/ADR-012 value
enforced at its boundary. Build 0 warnings / 0 errors; default suite Failed 0,
Passed 1218, Skipped 60, Total 1278; host-guarded suite Failed 0, Passed 1234,
Skipped 44, Total 1278; 145 focused boundary/redaction/retention tests pass. Gate
7 is not declared.

### Task 7.5 - Reproducible release package from a clean checkout

**Status:** Complete. Phase 7 acceptance criterion 5 is met: the release
artifact is byte-reproducible and the build process is documented.

The task 7.8 reviewer found the produced `artifacts/ArrTags_0.1.0.0.zip` was not
byte-reproducible: MSBuild's `ZipDirectory` task enumerated the staging
directory in filesystem order and stamped each entry with the source file's
modification time, so consecutive `./build.sh package` runs produced the same
size and identical extracted contents but different SHA-256. The `PackagePlugin`
target now only stages the exact release files (`ArrTags.dll`,
`ArrTags.deps.json`, `build.yaml`, `THIRD-PARTY-NOTICES.md`, and `licenses/`)
into `artifacts/staging`, and `./build.sh package` invokes a new deterministic
packer, `scripts/pack-release.cs` (a .NET 10 file-based app; not part of
`ArrTags.slnx` and adding no plugin dependency). The packer writes entries in
ordinal order of their forward-slash relative path and stamps every entry with
one fixed ZIP timestamp (`2000-01-01 00:00:00`), using the pinned SDK's
deterministic deflate implementation, so identical staged bytes always produce
an identical archive.

Two SDK git-derived inputs that also changed the assembly between checkouts were
suppressed so a build from a git working tree matches a `.git`-less clean
export. `src/ArrTags/ArrTags.csproj` sets `PathMap` to map the project directory
to the fixed root `/_/ArrTags` (the absolute PDB path was otherwise embedded in
the debug directory, shifting the whole assembly). `Directory.Build.props` sets
`IncludeSourceRevisionInInformationalVersion=false` (the SDK otherwise appends
the checkout's git commit id to `AssemblyInformationalVersion`) and
`SuppressImplicitGitSourceLink=true` (the SDK otherwise generates a git-derived
`*.sourcelink.json` with the repository URL and commit and feeds it to the
compiler).

Clean-checkout evidence: two independent clean exports (526 files each, no
`.git`, `bin`, `obj`, or `artifacts`, at different absolute paths) were restored
in locked mode, built, tested, and packaged; a third run in the first export
after wiping its build outputs was also packaged. All produced the identical
`artifacts/ArrTags_0.1.0.0.zip`: 567,856 bytes, SHA-256
`bd10b9b6bf5d31049082d27625b18ba127eb6e2860a454fe2d3c35ccebaaee51`, 7 entries
(`ArrTags.deps.json` 5,899 B, `ArrTags.dll` 1,145,344 B,
`THIRD-PARTY-NOTICES.md` 1,368 B, `build.yaml` 695 B,
`licenses/DejaVu-Fonts-License.txt` 8,816 B, `licenses/SkiaSharp-LICENSE.txt`
1,129 B, `licenses/SkiaSharp-THIRD-PARTY-NOTICES.txt` 139,775 B), with no
`SkiaSharp.dll`/`libSkiaSharp.so`. The repository working tree (with `.git`)
produced the same hash. Build 0 warnings / 0 errors in every tree; default suite
Failed 0, Passed 1218, Skipped 60, Total 1278; host-guarded suite
(`ARRTAGS_JELLYFIN_HOST_DIR=/tmp/jf/jellyfin`) Failed 0, Passed 1234, Skipped 44,
Total 1278 - both equal to the task 7.8/7.4 baseline because the task adds no
product test. The commands, pinned toolchain/inputs, ADR-013 supported version
ranges (Jellyfin `12.0.0`/`targetAbi: 12.0.0.0`, `net10.0`, Sonarr 3.x-4.x,
Radarr 3.x-6.x on `/api/v3`), artifact identity and per-entry hashes,
verification steps, and the release checklist are recorded in
`docs/release/build-and-release.md`. No product behavior changed.

### Task 7.6 - Known limitations and deferred decisions

**Status:** Complete. Documentation-only. The project's known limitations and
deferred decisions are consolidated into one canonical current-state document,
`docs/limitations.md`, so no deferred or unverified capability is presented as
available. It records the open functional/operational deferrals (no provider
inventory/catalogue cache, so Phase 6 acceptance criterion 1 remains partially
met; runtime configuration replacement not wired to Jellyfin's configuration
save path; no bounded secret-free metrics/diagnostic-status surface; the
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
uninstall-drain retention), and two decision-record notes (the webhook `401`
framework ProblemDetails body versus ADR-012's "no body" text, recorded as a
minor gap for a future ADR-012 clarification - task 7.4 finding 7.4-R2; and the
optional `ArtworkSubjectGate` idle-eviction hardening - finding 7.4-R1).

The criteria status is stated explicitly: all five Phase 7 acceptance criteria
are met; `GOALS.md` criteria 1-4, 8, and 9 are met as shipped; criterion 5 is
met as shipped but requires the host's SkiaSharp; criterion 6 is met for render
and publication but only partial for provider fetches; and criterion 7 is met
only at the contract level. Carried documentation nits were also fixed
where they affected accuracy: the `PLANS.md` Project Status paragraph and
Milestone 7 row now list every completed Phase 7 task (finding 7.5-F5), and
ADR-015 now labels the pinned host `linux-musl-x64` (finding 7.8-R6) and states
that `ExcludeAssets=runtime` still leaves the `SkiaSharp.NativeAssets.Linux`
`runtimeTargets` in `ArrTags.deps.json` even though the native files are not
staged into the package (finding 7.8-R3). `README.md`,
`docs/implementation-readiness.md`, `docs/architecture.md`, and
`docs/release/build-and-release.md` point to the consolidated record. No source,
test, packaging, or behavior change. Build 0 warnings / 0 errors; default suite
Failed 0, Passed 1218, Skipped 60, Total 1278; host-guarded suite
(`ARRTAGS_JELLYFIN_HOST_DIR=/tmp/jf/jellyfin`) Failed 0, Passed 1234, Skipped 44,
Total 1278. Gate 7 is not declared.
