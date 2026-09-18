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
  specified in `docs/data-model.md` section 3.4.1.
- Connect `ConfigurationSnapshotService` to Jellyfin's configuration save path;
  no core worker consumes the snapshot yet.
- Add artifact byte storage and per-surface `ArtworkOperation` journaling to the
  state boundary; only bounded metadata/envelope state exists today.
- Register queue, renderer, and scheduled-task services as those components are
  introduced; the provider HTTP clients and the versioned credential resolver
  are registered in tasks 2.2 and 2.3.
- Resolve `SaveImage` storage/readback and source-capture behavior on the target
  host configuration.

### Phase 2 (Sonarr & Radarr integration) - in progress

Tasks 2.1-2.3 are complete. The shared provider-client boundary and connection
identity (task 2.1), dedicated `IHttpClientFactory` client registration
(task 2.2), and the versioned credential boundary (ADR-005, task 2.3) are
implemented. The read-only Radarr v3 client probes
`GET /api/v3/system/status`, reads the local library through `GET /api/v3/movie`,
and reads the fully populated current file through the dedicated
`GET /api/v3/moviefile?movieId=` endpoint with bounded timeout, cancellation,
retry/backoff, response-size limits, and redacted errors.

Remaining Phase 2 work:

1. Task 2.4: Sonarr v3 reads for series, episodes, and episode files, joining
   files by the validated episode-file identifier.
2. Tasks 2.5-2.6: map provider data into canonical `ArrProvider`,
   `ArrConnection`, and `BadgeMetadata`, keeping actual file quality separate
   from quality-profile policy and unknown values unknown.
3. Task 2.7: provider authentication-failure, unavailability, malformed,
   optional-field, version-drift, cancellation, and retry tests.
