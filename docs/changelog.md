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
tests. Media matching (Phase 3) is now in progress.

### Phase 3 (Media matching) - in progress

Tasks 3.1, 3.2, 3.3, 3.4, and 3.5 are complete; the Milestone 3 gate is not yet
met.

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

Build and test: 0 warnings, 0 errors; 313 tests pass. DG-5 is resolved by
ADR-008: configured path mapping and normalization are deferred out of V1, so
task 3.6 adds no runtime implementation. The remaining Phase 3 task is
connection-scope verification (3.7).
