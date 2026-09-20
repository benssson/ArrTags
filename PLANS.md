# ArrTags Execution Plan

## Project Status

**Status:** Phases 1-4 complete; Phase 5 in progress. Milestone 1 (plugin foundation), Milestone 2 (Sonarr and Radarr integration), Milestone 3 (media matching, tasks 3.1 through 3.8), and Milestone 4 (badge rendering, tasks 4.1 through 4.11) are complete; Gates 1, 2, 3, and 4 are met. Phase 5 tasks 5.1 (confirm the item-image publication ABI and route variants), 5.2 (source-artwork provenance and guarded restoration state), 5.3 (the Jellyfin host source adapter), 5.4 (renderer managed/native packaging), 5.6 (the durable `ArtworkOperation` write-ahead record and store), 5.5 (publish completed artwork through Jellyfin's supported item-image APIs), and 5.7 (postcondition reconciliation of uncertain publication outcomes) are complete; the remaining Phase 5 tasks (5.8 through 5.11) are not started.

**Current position:** The goals, V1 architecture, and canonical data model are
drafted and the architectural blockers are resolved. Tasks 1.1 (documentation
alignment), 1.2 (project and test scaffold), 1.3 (canonical Sonarr identity
model), 1.4 (operational defaults and limits), 1.5 (plugin entry point and
configuration), 1.6 (DI registration and lifecycle foundation), 1.7
(versioned plugin state boundary), and 1.8 (Jellyfin 12 build, discovery, and
host validation) are complete: the
architecture is the single V1 architecture reference, the research documents are
marked as evidence, the canonical model represents Sonarr series, episode, and
current episode-file identity with a typed, connection-scoped identity, the
accepted operational defaults and validation rules are recorded in ADR-004 and
`docs/architecture.md` section 12, the configuration foundation validates
connections, limits, and scope and exposes an immutable replacement snapshot with
last-valid retention and secret redaction, the parameterless service registrator
wires the configuration snapshot, state repository, library-event boundary, and
an idle hosted lifecycle that subscribes and unsubscribes deterministically
without provider, rendering, or full-library work, the state boundary provides
versioned integrity-tagged records with atomic writes, cache discard versus
authoritative quarantine, traversal-safe paths, and bounded retention, and the
plugin builds on `net10.0` against the pinned Jellyfin `12.0.0` packages with
`targetAbi: 12.0.0.0`. Foundation tests pass without a live Arr instance and the
generated package was installed, discovered, loaded, started, restarted, and
shut down cleanly on a Jellyfin `12.0.0.0` host. Milestone 1 is complete and
Gate 1 is met. Milestone 2 is complete and Gate 2 is met: tasks 2.1 (shared
provider-client boundary and connection identity), 2.2 (dedicated
`IHttpClientFactory` client registration), 2.3 (versioned credential boundary
and read-only Radarr v3 reads), 2.4 (read-only Sonarr v3 series, episode, and
episode-file reads with the validated episode-file join), 2.5 (canonical
`ArrProvider`/`ArrConnection`/`BadgeMetadata` mapping with connection-scoped
record/file identity), and 2.6 (explicit unknown technical values and bounded
custom values), and provider failure-matrix tests (2.7) are complete. All Phase 2
tasks meet their acceptance criteria. Phase 3 media matching
tasks 3.1 (canonical `MediaIdentity` snapshots), 3.2 (provider-neutral candidate
selection, evidence recording, and deterministic `MediaMatch` fingerprints), 3.3
(zero- and multiple-candidate rejection with the match status policy), 3.4 (the
documented movie, series, and episode matching order with provider-specific
candidate assembly), 3.5 (the explicit episode-numbering policy in ADR-007), 3.6
(DG-5 resolved by ADR-008 with path fallback deferred out of V1), 3.7
(connection-scoped Arr record and file identity verification), and 3.8
(fail-closed ineligible-location rejection) are complete. All Phase 3 tasks meet
their acceptance criteria; the Milestone 3 acceptance criteria and Gate 3 are
verified.

Decision Gate DG-3 is now resolved by ADR-009. The V1 badge fields, priority,
poster layout, typography, contrast, text bounds, PNG output, scaling, and
pass-through behavior are fixed for implementation. Phase 4 is complete:
tasks 4.1 (metadata selectors), 4.2 (rendering specification), 4.3
(unknown-value semantics), 4.4 (fingerprints), 4.5 (limit enforcement), 4.7
(pinned assets and font), 4.8 (SkiaSharp host-compatibility spike), 4.9
(provider-neutral renderer service and drawing engine), 4.6 (renderer
behavior matrix), and 4.10 (renderer configuration model, snapshot, and
fingerprint) are complete. Task 4.11 (golden-image, byte-determinism,
PNG-contract, and cross-runtime tolerance tests) is complete, including the
task 4.9 F2 color-profile fail-closed supporting change and the authorized
task 4.11 fix of the EXIF dimension-swapping orientation transforms with a
renderer version bump to 2. All Milestone 4 acceptance criteria are satisfied
and Gate 4 is met: the forced native renderer run passes 655 tests with one
non-canonical cross-runtime skip (656 total), covering normal, unknown,
oversized, malformed, cancelled, and failed inputs. The only deferred Phase 4
validation is the ADR-010 non-canonical cross-runtime comparison, which requires
a second explicitly selected Linux runtime and is tracked for the
testing/release milestone.

Phase 5 (Jellyfin artwork integration) has begun. Task 5.1 is complete: the
supported Jellyfin 12.0.0 item-image publication/read ABI and the standard
`ImageController` route variants are pinned with evidence in
`docs/research/jellyfin-12-architecture.md` section 4.4, with a new unguarded ABI
test and a host-guarded route/authorization test. Task 5.2 is complete: the
provider-neutral source-artwork provenance and guarded restoration state model,
its pure logical transitions, the content-addressed authoritative source-artifact
store, and the authoritative `PublishedArtworkState` persistence are implemented
and covered by focused tests. Task 5.3 is complete: the plugin-owned Jellyfin host
source adapter reads the unindexed `Primary` image through the pinned 12.0.0 read
surface, confines accepted source containers to the PNG/JPEG profiles the renderer
inspects, derives the post-orientation display dimensions from the exact bytes,
and supplies the renderer's `SourceImageInput` plus the task 5.2
`ActiveImageIdentity` from one bounded read. Task 5.4 is complete: the plugin
package now carries the renderer's managed `SkiaSharp.dll`, the matching
`linux-x64` native `libSkiaSharp.so` and the dependency manifest at the plugin
folder root, with the `build.yaml` artifact list updated to match, and both the
live pinned host and a replicated plugin load context confirm the assets resolve
under the plugin load context. Task 5.6 is complete: the durable provider-neutral
`ArtworkOperation` write-ahead model, its phase and lifecycle-fence rules, and
its generation-fenced authoritative store are implemented and covered by focused
tests, so a complete publication/restoration intent can be persisted before any
Jellyfin image mutation. Task 5.5 is complete: the single-subject durable
publication orchestration and the single Jellyfin image-mutation implementation
are implemented in `src/ArrTags/Artwork`, driving the write-ahead ordering
through the supported stream `SaveImage` and `UpdateToRepositoryAsync(ImageUpdate)`
flow with a fail-closed revalidation and readback. Task 5.7 is complete: the
provider-neutral postcondition reconciliation service applies the data-model
3.10.4 decision table to a durable operation, resumes the deterministic
publication only under the current generation and lifecycle fence without
recapturing a source, commits the intended final state when the after identity is
observed, records ownership loss or uncertainty without mutating the image,
tombstones a confirmed item removal without an image call, and blocks or
quarantines invalid state without replay or cleanup. The remaining Phase 5 tasks
(5.8 through 5.11 in the authoritative order) are not started.

**V1 outcome:** A Jellyfin 12 plugin that independently reads Sonarr and Radarr
metadata, matches it to eligible Jellyfin media, and asynchronously publishes
configurable derived poster artwork through Jellyfin's supported image APIs
without modifying original media files or external services.

**Current constraints:**

- Jellyfin 12 is the only supported Jellyfin version.
- Sonarr and Radarr are the only external metadata providers.
- The integration is read-only.
- The canonical domain model is provider-neutral after the integration boundary.
- Missing, stale, ambiguous, or unavailable data must degrade to a safe
  unchanged-artwork result rather than affect Jellyfin operations.
- Generated images may become persisted Jellyfin artwork through the supported
  item-image APIs. Original source artwork remains recoverable through
  plugin-owned provenance state; original media files and Jellyfin's image cache
  are never modified directly.

## How To Use This Plan

- Keep milestone order unchanged. A milestone may be worked on only after its
  predecessor's gate is met, unless a task is explicitly marked as research or
  a test spike.
- Task numbers are stable identifiers only, not an execution sequence. Each
  phase's **Authoritative Phase X execution order** is the canonical sequence:
  walk it from the first entry and select the first task that is not complete,
  after verifying that task's documented prerequisites. When the execution order
  reorders task numbers, the execution order wins.
- Update task checkboxes and the status table as work lands; do not mark a
  milestone complete until every acceptance criterion is verified.
- Record decisions that change an architecture assumption in
  `docs/decisions.md`, then update `docs/architecture.md` or
  `docs/data-model.md` before implementation relies on them.
- Keep provider DTOs, Jellyfin entities, credentials, and implementation
  details at their boundaries. The matching, metadata, rendering, and cache
  pipeline consumes the canonical models.
- Every milestone must leave Jellyfin safe when the provider, cache, matcher, or
  renderer fails.

## Milestone Status

| # | Milestone | Status | Exit gate |
| --- | --- | --- | --- |
| 1 | Plugin foundation | Complete | Plugin loads on the selected Jellyfin 12 ABI with valid configuration and lifecycle behavior. |
| 2 | Sonarr & Radarr integration | Complete | Both providers can be configured independently, probed, queried read-only, and mapped into canonical observations. |
| 3 | Media matching | Complete | Eligible movies, series, and episodes match only with validated identity evidence. |
| 4 | Badge rendering | Complete | Canonical metadata renders deterministically within configured limits, with safe pass-through on failure. |
| 5 | Jellyfin artwork integration | In progress (5.1, 5.2, 5.3, 5.4, 5.6, 5.5, 5.7 complete) | Derived poster artwork is published through Jellyfin's supported image APIs without modifying media files or bypassing normal image delivery. |
| 6 | Caching, updates & performance | Not started | Reconciliation, invalidation, persistence, and bounded work avoid unnecessary requests and processing. |
| 7 | Testing & release | Not started | Required unit/integration/acceptance checks pass and the plugin can be built and packaged reproducibly. |

## Milestones

### 1. Plugin foundation

**Objective:** Establish a minimal, loadable Jellyfin 12 plugin foundation: the
project, configuration, dependency-injection, lifecycle, and versioned state
boundaries required by the remaining milestones.

**Phase 1 concept:** This milestone is executed as the ordered Phase 1 tasks
below. The tasks recorded in `docs/implementation-readiness.md` ("Phase 1
Implementation Tasks") map to tasks 1.1, 1.3, 1.4, 1.5, 1.6, 1.7, and 1.8.
Architecture and data-model detail stays authoritative in `docs/architecture.md`
and `docs/data-model.md` and is referenced here rather than duplicated.

| Readiness task | Phase 1 task |
| --- | --- |
| Remove/demote duplicate architecture section; align planner/research wording | 1.1 |
| Extend canonical match model for Sonarr series, episode, and episode-file identity | 1.3 |
| Establish validated operational defaults and limits | 1.4 |
| Implement the plugin entry point and immutable configuration boundary | 1.5 |
| Implement dependency-injection registration and the hosted service lifecycle | 1.6 |
| Establish the versioned plugin state boundary | 1.7 |
| Build/load the actual plugin against the pinned Jellyfin 12.0.0 set | 1.8 |

**Phase 1 sequence and dependencies:**

| Order | Task | Depends on |
| --- | --- | --- |
| 1.1 | Documentation alignment | — |
| 1.2 | Project and test scaffold | 1.1 |
| 1.3 | Canonical Sonarr identity model | 1.2 |
| 1.4 | Operational defaults and limits | 1.2, 1.3 |
| 1.5 | Plugin entry point and configuration | 1.2, 1.4 |
| 1.6 | DI registration and lifecycle foundation | 1.5 |
| 1.7 | Versioned plugin state boundary | 1.2, 1.4, 1.5 |
| 1.8 | Jellyfin 12 build, discovery, and host validation | 1.2, 1.5, 1.6, 1.7 |

#### 1.1 Documentation alignment

**Status:** Complete.

**Objective:** Remove conflicting planning guidance so the accepted architecture
is the sole implementation reference.

**Dependencies:** None.

**Affected files:** `docs/architecture.md`, `docs/research/jellyfin-12-architecture.md`,
`docs/research/poster-rendering-strategies.md`, `docs/research/media-metadata-mapping.md`.

**Work:**

- Remove or explicitly demote the duplicate architecture section at the end of
  `docs/architecture.md`; keep one authoritative phase/milestone sequence.
- Mark the research documents as evidence and reference material, not competing
  architecture or phase definitions.
- Cross-reference ADR-001 wherever research discusses rejected image-delivery
  alternatives.
- Clarify that reverse provider-to-Jellyfin resolution remains deferred until
  webhook scope is decided.

**Tests:** Manual consistency review; search for stale phase names, obsolete
middleware delivery claims, and contradictory scope statements.

**Acceptance criteria:** One authoritative architecture and milestone sequence
exists; research does not override accepted decisions; no resolved architectural
decision is reopened.

**Definition of done:** Documentation diff reviewed and cross-document
contradictions removed.

#### 1.2 Project and test scaffold

**Status:** Complete. Intended-host acceptance is covered by task 1.8.

**Objective:** Create the minimum reproducible Jellyfin 12 plugin project needed
for implementation and validation.

**Dependencies:** 1.1; compatibility pins in `docs/implementation-readiness.md`.

**Affected components:** repository root (`global.json`, solution file), plugin
project under `src/`, plugin manifest, test project under `tests/`, build and
package configuration.

**Work:**

- Target `net10.0` with .NET SDK baseline `10.0.0`.
- Pin every referenced Jellyfin host package to exactly `12.0.0`; forbid a
  `12.1.x` transitive upgrade.
- Add the plugin manifest with `targetAbi: 12.0.0.0`.
- Establish source, test, and package-output boundaries.
- Add deterministic restore/build/test/package commands.

**Tests:** Restore and compile; dependency-graph check for forbidden Jellyfin
upgrades; manifest ABI assertion.

**Acceptance criteria:** A clean checkout restores and builds with SDK `10.0.0`;
the package has the expected plugin identity and ABI; tests run without a live
Arr instance.

**Definition of done:** A clean checkout produces a buildable plugin artifact.

#### 1.3 Canonical Sonarr identity model

**Status:** Complete (model specification). Implementation and tests land with
the provider, matching, and fingerprint work in later milestones.

**Objective:** Represent Sonarr series, episode, and current episode-file
identity explicitly before provider or matching code consumes it.

**Dependencies:** 1.2; `docs/data-model.md`; Sonarr research.

**Affected components:** canonical model, `MediaMatch`, `BadgeMetadata.recordIdentity`,
match/metadata fingerprint generation, `docs/data-model.md`.

**Work:**

- Add a typed, connection-scoped Sonarr identity containing the Sonarr series
  ID, Sonarr episode ID, and current episode-file ID.
- Preserve the existing Radarr movie/file identity shape.
- Represent shared episode files without implying one file belongs to one
  episode; make missing file identity explicit rather than zero/empty.
- Include all identity components in match and metadata fingerprints.
- Keep provider DTOs outside the canonical model.

**Tests:** Equality and fingerprint tests per component; connection-scoping
tests; missing-file, mismatched `episodeFileId`, shared-file, and changed-file-ID
tests; versioned snapshot serialization tests; secret-exclusion tests.

**Acceptance criteria:** A matched Sonarr episode cannot be represented without
distinguishing series, episode, and current-file identity; a changed
episode-file ID changes the dependent fingerprint; provider DTOs do not leak
into the canonical model.

**Definition of done:** `docs/data-model.md`, interfaces, and tests describe and
enforce the same identity contract.

#### 1.4 Operational defaults and limits

**Status:** Complete. The accepted values, validation rules, and safe failure
behavior are recorded in ADR-004 and `docs/architecture.md` section 12.
Configuration-load enforcement and boundary tests are implemented in task 1.5;
state quota and retention enforcement are implemented in task 1.7.
Representative-load validation remains the performance milestone.

**Objective:** Establish validated initial bounds for foundation-level work.

**Dependencies:** 1.2; 1.3 for identity and state sizing.

**Affected components:** configuration model and validator, operational-limits
value object, queue/HTTP/retry/artifact/stale-state policies,
`docs/architecture.md`, `docs/decisions.md`.

**Work:**

- Define explicit limits for queue capacity and per-item single-flight work,
  provider and render concurrency, HTTP timeout, retry count/backoff, maximum
  provider response size, source/derived artifact sizes, provenance retention,
  plugin storage/cache quota, and maximum stale-last-known-good duration.
- Validate limits at configuration load time.
- Ensure active provenance and non-terminal artwork operations are never
  evicted as ordinary cache entries.
- Define safe behavior on storage-quota exhaustion: reject new derived work and
  preserve current artwork.
- Record the selected values and rationale in the authoritative documentation.

**Accepted baseline:** The values, units, validation ranges, and safe failure
behavior are recorded in `docs/architecture.md` section 12, and the decision and
rationale are recorded in `docs/decisions.md` ADR-004. Later milestones may tune
values within the documented ranges without reopening ADR-004.

**Tests:** Boundary/invalid-value validation; retry classification and timeout;
queue overflow and concurrency; response-size rejection; storage quota and
non-eviction; stale-window behavior. Boundary and storage-quota tests are
implemented in tasks 1.5 and 1.7; retry, queue, response-size, and stale-window
behavior tests land with the provider, caching, and performance milestones.

**Acceptance criteria:** Every required limit has an explicit value, unit,
validation rule, and safe failure behavior; tests show limits prevent unbounded
work; authoritative state cannot be evicted as ordinary cache data.

**Definition of done:** Defaults are recorded with explicit validation rules and
safe failure behavior and approved for the initial foundation; configuration-load
and state enforcement are implemented in tasks 1.5 and 1.7, and representative-load
validation remains the performance milestone.

#### 1.5 Plugin entry point and configuration

**Status:** Complete. The parameterless `BasePlugin<PluginConfiguration>` entry
point remains Jellyfin-owned and unchanged; the configuration model, validator,
and immutable replacement-snapshot service are implemented and covered by
foundation tests. Dependency-injection registration is completed in task 1.6.

**Objective:** Implement the thin plugin entry point and immutable configuration
boundary.

**Dependencies:** 1.2, 1.4.

**Affected components:** `Plugin`, `PluginConfiguration`, configuration
validator/snapshot service, administrative diagnostics.

**Work:**

- Add the parameterless `BasePlugin<PluginConfiguration>` entry point with
  plugin identity and `DataFolderPath` ownership.
- Support independently enabled Sonarr and Radarr connections, disabled by
  default.
- Use replacement snapshots rather than sharing mutable configuration with
  workers.
- Validate URLs, finite timeouts, TLS policy, limits, and library/image scope.
- Redact API keys and webhook secrets from logs, diagnostics, fingerprints, and
  canonical snapshots; preserve the last valid snapshot when a replacement is
  invalid.

**Tests:** Plugin construction/identity; valid and invalid configuration;
independent provider enablement; snapshot replacement and last-valid-snapshot;
secret redaction.

**Acceptance criteria:** Both providers can stay disabled without external I/O;
invalid configuration cannot bring down Jellyfin; secrets are absent from
diagnostics and domain state.

**Definition of done:** Configuration behavior is deterministic and covered by
automated tests.

#### 1.6 DI registration and lifecycle foundation

**Status:** Complete (foundation). The parameterless `IPluginServiceRegistrator`
registers the configuration snapshot service, the versioned state repository, the
library-event boundary, and an idle hosted lifecycle service. The hosted service
subscribes to library events only for its own lifetime and unsubscribes
deterministically on shutdown, restart, cancellation, and disposal, with no
provider, rendering, or full-library work during registration or startup. Domain,
provider, queue, and scheduled-task registrations land with the milestones that
introduce those components. Host validation of the wired lifecycle is part of
task 1.8.

**Objective:** Register foundation services without starting provider or
rendering work during registration or startup.

**Dependencies:** 1.5.

**Affected components:** `IPluginServiceRegistrator`, hosted lifecycle, library
event boundary, configuration and state service registration. Scheduled-task,
controller, provider, and queue registrations are added with the milestones that
introduce those components.

**Work:**

- Register configuration, state services, the library-event boundary, and hosted
  work. Domain, provider, queue, and scheduled-task registrations are added with
  the milestones that introduce them.
- Keep registration parameterless and compatible with Jellyfin 12 conventions.
- Start no unbounded refresh or full-library processing synchronously.
- Subscribe and unsubscribe library events within the hosted-service lifecycle.
- Ensure cancellation and shutdown await all owned work.

**Tests:** DI resolution; startup and shutdown; reload and cancellation;
verification that no provider API is called during registration/startup; event
subscription cleanup.

**Acceptance criteria:** The plugin starts with both providers disabled; no
unmanaged background work survives shutdown; registration performs no provider,
rendering, or full-library work.

**Definition of done:** Lifecycle tests pass against the pinned Jellyfin test
environment or a compatible integration harness.

#### 1.7 Versioned plugin state boundary

**Status:** Complete. Versioned envelopes with a schema version and SHA-256
payload integrity, atomic flush-and-rename writes, cache discard versus
authoritative quarantine, traversal-safe path resolution, and bounded cache and
terminal-provenance retention are implemented and covered by foundation tests.
Artifact byte storage and per-surface artwork-operation journaling land with the
artwork and caching milestones.

**Objective:** Establish safe plugin-owned state storage under Jellyfin's
`DataFolderPath`.

**Dependencies:** 1.2, 1.4, 1.5.

**Affected components:** state repository, versioned records and manifests,
atomic file writer, quarantine/recovery handling.

**Work:**

- Define versioned state envelopes and integrity metadata.
- Write via temporary file, flush, and atomic replacement.
- Separate ordinary cache state from authoritative artwork-operation state.
- Rebuild or discard invalid non-authoritative cache entries; quarantine invalid
  authoritative records instead of treating them as absent.
- Apply bounded storage and retention policies.

**Tests:** Atomic write/read; corrupt, torn, incompatible, and missing state;
quarantine; restart recovery; permission and path-traversal.

**Acceptance criteria:** Corrupt cache state does not prevent startup; invalid
authoritative state never triggers blind replay or cleanup; state contains no
credentials or unbounded external payloads.

**Definition of done:** State recovery behavior is deterministic and documented.

#### 1.8 Jellyfin 12 build, discovery, and host validation

**Status:** Complete. The plugin restores, builds, tests, and packages with the
pinned `net10.0` / Jellyfin `12.0.0` compatibility set. The generated package was
installed on the pinned Jellyfin `12.0.0` host (portable `v12.0` amd64 build),
reported as loaded with `targetAbi: 12.0.0.0` and status `Active`, and completed
startup, restart, and shutdown with no load errors, unmanaged background work, or
secret leakage.

**Objective:** Prove the actual plugin works against the pinned Jellyfin
compatibility set.

**Dependencies:** 1.2, 1.5, 1.6, 1.7.

**Affected components:** build/package output, plugin manifest, pinned
Jellyfin `12.0.0` host, validation checklist.

**Required host validation:**

- Build with .NET SDK `10.0.0`.
- Install the generated package on the pinned Jellyfin `12.0.0` host.
- Verify plugin discovery and load, and `targetAbi: 12.0.0.0`.
- Start with both providers disabled; verify configuration loading, DI
  registration, startup, shutdown, and reload.
- Confirm the installed host runtime patch and OS/runtime packaging.
- Review logs for load errors, unmanaged background work, or secret leakage.

**Tests:** Clean restore/build/package; plugin discovery smoke test; host
startup/shutdown smoke test; manifest and ABI verification; runtime
compatibility against the actual host.

**Acceptance criteria:** The plugin builds, installs, is discovered, and loads
on Jellyfin `12.0.0`; the host accepts `targetAbi: 12.0.0.0`; both providers
remain disabled without provider calls; lifecycle and configuration tests pass
on the pinned host.

**Definition of done:** Gate 1 is met and the milestone can be marked complete.

**Phase 1 acceptance criteria:**

- [x] The plugin builds, installs, and loads correctly on Jellyfin `12.0.0`
  with target framework `net10.0` and manifest `targetAbi: 12.0.0.0`.
- [x] Sonarr and Radarr can be enabled, disabled, and configured independently.
- [x] Invalid configuration is rejected or retained as the last valid snapshot
  without taking down Jellyfin.
- [x] Credentials never appear in persisted canonical data, cache identity,
  logs, or error messages.
- [x] Startup and shutdown leave no unmanaged background work.
- [x] A restart with missing, corrupt, or incompatible non-authoritative cache
  state rebuilds it without blocking Jellyfin; invalid artwork-operation state
  is quarantined and preserves the current image without blind replay.
- [x] The canonical model represents Sonarr series, episode, and episode-file
  identity explicitly and excludes provider DTOs.
- [x] All operational defaults have explicit values, validation rules, and safe
  failure behavior.

**Gate 1:** The ABI and configuration/lifecycle tests pass, the plugin can start
with both providers disabled, and tasks 1.1-1.8 meet their acceptance criteria.

### 2. Sonarr & Radarr integration

**Objective:** Implement independent, read-only Arr clients and translate
provider responses into the canonical provider, match-input, and badge metadata
models.

**Deliverables:**

- Dedicated `IHttpClientFactory`-based Sonarr and Radarr clients using the v3
  API contract.
- Connection probing with authentication, URL-base, version, health, and
  capability results that do not expose credentials.
- Defensive DTO parsing and provider-to-canonical mapping for current movie,
  series, episode, and file resources.
- Actual file quality and available technical metadata, separated from quality
  profile policy.
- Bounded timeout, cancellation, retry, and redacted error behavior.

**Tasks:**

- [x] 2.1 Define the shared provider-client boundary and connection identity rules.
- [x] 2.2 Register dedicated named or typed clients through Jellyfin's standard
  HTTP client factory; do not create raw clients per request.
- [x] 2.3 Implement Radarr v3 reads for local movies and current movie-file data,
  including the dedicated file request when enabled fields are not embedded.
- [x] 2.4 Implement Sonarr v3 reads for series, episodes, and episode files,
  joining files by the validated episode file identifier.
- [x] 2.5 Map provider data into `ArrProvider`, `ArrConnection`, and
  `BadgeMetadata` without leaking provider DTOs past the boundary.
- [x] 2.6 Preserve unknown technical values as unknown rather than false or empty
  claims, and bound custom values before they can reach a badge.
- [x] 2.7 Add tests for authentication failures, unavailable services, malformed
  responses, optional fields, version drift, cancellation, and retries.

**Task 2.1 status:** Complete. The shared provider-client boundary and connection
identity rules are implemented in `src/ArrTags/Providers`. The canonical
`ArrProvider` and `ArrConnection` identities, the connection-scoped
`ArrConnectionId` (derived from provider kind and normalized base URL, excluding
the API key and user information), connection health and TLS policy, derived
capabilities, bounded redacted `ArrProviderError` outcomes, the
`IArrProviderClient` probe boundary, and the secret-free
`ArrConnectionCatalog` mapping from `PluginConfigurationSnapshot` are covered by
`ProviderBoundaryTests`. Dedicated `IHttpClientFactory` client registration,
concrete Radarr/Sonarr reads, and canonical metadata mapping remain the
following tasks.

**Task 2.2 status:** Complete. Jellyfin's standard `IHttpClientFactory` is used
rather than raw `HttpClient` instances. `ArrTagsServiceRegistrator` registers a
named client per provider kind and TLS policy (`ArrHttpClientNames`) so the
pooled message handler matches the connection's certificate policy; the relaxed
handler is only installed for the explicit `AllowInsecure` connection policy.
`IArrHttpClientFactory`/`ArrHttpClientFactory` build a connection-scoped client
with the configured base URL, finite timeout, and `Accept: application/json`
without ever reading an API key. Credential persistence and access are now
defined by ADR-005: the configuration boundary owns a private versioned secret
snapshot and issues short-lived leases without exposing values to workers'
mutable configuration or canonical state. The concrete Radarr client implements
that boundary (task 2.3); the Sonarr client follows in task 2.4. Registration
adds no startup work and both providers remain disabled by default.

**Phase 2 credential boundary status:** Resolved by `docs/decisions.md` ADR-005,
with the contract reflected in `docs/architecture.md`, `docs/data-model.md`, and
`docs/implementation-readiness.md`. The resolver boundary was implemented and
tested in task 2.3; no additional architecture decision is required for API-key
access. Webhook route exposure, replay handling, and payload policy remain
separate later decisions.

**Task 2.3 status:** Complete. The ADR-005 credential boundary is implemented in
`src/ArrTags/Secrets`: typed `SecretReference` slots, a non-serializable
`SecretLease`, and the singleton `IPluginSecretResolver` owned by
`ConfigurationSnapshotService`. The configuration boundary now publishes an
atomic public-snapshot/private-secret generation with a monotonic
`configurationVersion`; `ArrConnection` carries the safe API-key reference and
the configuration version so a lease is valid only for the generation it came
from, and invalid replacements and rotations leave the active secret unchanged
until the new generation is durable.

The read-only Radarr v3 client is implemented in
`src/ArrTags/Providers/Radarr`. `RadarrClient` probes `GET /api/v3/system/status`,
reads the local library through `GET /api/v3/movie`, and reads the fully
populated current file through the dedicated `GET /api/v3/moviefile?movieId=`
endpoint that includes custom-format and media-info fields. Every read acquires
a version-matched API-key lease, applies it only as `X-Api-Key`,
applies the bounded timeout, cancellation, retry/backoff, response-size, and
redacted error policy, and returns an `ArrProviderReadResult<T>` instead of
throwing. Provider DTOs stay in `ArrTags.Providers.Radarr` and do not enter
canonical state. Malformed, oversized, unauthorized, unavailable, and
incompatible responses map to bounded `ArrProviderError` outcomes. Tests cover
the credential boundary and the Radarr reads without a live Arr instance.
`BadgeMetadata` mapping and the actual-versus-profile quality semantics remain
tasks 2.5 and 2.6.

**Task 2.4 status:** Complete. The read-only Sonarr v3 client is implemented in
`src/ArrTags/Providers/Sonarr`. `SonarrClient` probes
`GET /api/v3/system/status`, reads the local library through `GET /api/v3/series`,
reads episodes through
`GET /api/v3/episode?seriesId=&includeEpisodeFile=true`, and reads the series
file inventory through `GET /api/v3/episodeFile?seriesId=`. It reuses the same
ADR-005 version-matched API-key lease, bounded timeout, cancellation,
retry/backoff, response-size, and redacted-error policy as the Radarr client,
and returns `ArrProviderReadResult<T>` outcomes instead of throwing.

The validated current-file join is implemented by
`SonarrEpisodeFileResolver.Resolve`: it trusts the embedded `episodeFile` only
when its identifier equals the episode's authoritative `episodeFileId`, falls
back to the series inventory otherwise, and returns no file identity (rather
than an unrelated file) when the episode has no current file or the referenced
identifier is missing. Provider DTOs stay in `ArrTags.Providers.Sonarr` and do
not enter canonical state. Tests cover the reads, the join, and bounded
failures without a live Arr instance. Canonical `BadgeMetadata` mapping was
completed in task 2.5; unknown-value and custom-value semantics were completed
in task 2.6, and provider failure-matrix tests remain task 2.7.

**Task 2.5 status:** Complete. The canonical identity and metadata models are
implemented in `src/ArrTags/Providers` and `src/ArrTags/Metadata`. Connection
scoped `ArrFileIdentity` (explicit `Present`/`Absent`, never zero), the typed
`ArrRecordIdentity` base with `SonarrIdentity` (series, episode, and current
file) and `RadarrIdentity` (movie and current file), and the provider-neutral
`BadgeMetadata` with quality, resolution, dynamic-range, codec, channel, audio
feature, source, upgrade-pending, custom-badge, and extension fields are covered
by `CanonicalIdentityTests` and `MetadataMappingTests`.

`RadarrMetadataMapper` and `SonarrMetadataMapper` translate validated provider
DTOs into the canonical shapes. The mappers read actual file quality only from
the current file resource (never the movie or series quality profile), validate
that a supplied file matches the authoritative record file association
(`movieFileId` and `episodeFileId`), keep absent file identity explicit, and
preserve unreported technical values as unknown rather than false. Every
`BadgeMetadata` carries a deterministic metadata fingerprint over the badge
schema version, the provider and connection-scoped identity, and all
badge-affecting values, excluding the observation timestamp and any credential.
Provider DTOs remain in `ArrTags.Providers.Radarr` and `ArrTags.Providers.Sonarr`
and do not appear in any canonical type. Custom-value bounding and three-state
unknown hardening were completed in task 2.6; provider failure-matrix tests
remain task 2.7.

**Task 2.6 status:** Complete. Canonical `BadgeMetadata` now preserves unknown
technical values explicitly. `AudioFeatures` is nullable: `null` means the
provider did not report usable audio codec data, while an empty set means a
reported codec yielded no known feature. The metadata fingerprint distinguishes
those two states, and the badge schema version was incremented to 2 to mark the
meaning change. `RadarrMetadataMapper` and `SonarrMetadataMapper` return no
feature set when the audio codec is absent. Custom badge values are bounded at
the canonical boundary before they can reach a badge: blank values are dropped,
control characters are removed, at most `MaxCustomBadgeCount` (32) values are
kept in provider order, and each value is truncated to `MaxCustomBadgeLength`
(128) characters. That is a defensive metadata bound; the renderer's separate
display/truncation policy is now defined by ADR-009.
`MetadataMappingTests` covers unknown-versus-empty audio features, the
fingerprint distinction, and custom-value count, order, length, and
control-character behavior. Provider failure-matrix tests are implemented in
task 2.7.

**Task 2.7 status:** Complete. `ProviderFailureMatrixTests` exercises the shared
read boundary for both providers across the documented failure classes without a
live Arr instance. Authentication checks cover `401`/`403` on reads and probes,
a missing credential lease failing closed with no HTTP call, and redacted
messages. Unavailable-service checks cover `409`, `429`, `5xx`, unreachable
connections, and request timeouts mapping to `ProviderUnavailable` after the
bounded retry count. Malformed checks cover non-JSON, empty, `null`,
wrong-shaped, truncated, and oversized responses (both with and without a
`Content-Length`), which map to `InvalidResponse` and are never retried.
Optional-field checks confirm omitted movie, series, episode, and file fields
deserialize tolerantly and remain unknown rather than false. Version-drift
checks confirm a future version, changed casing, unknown extra fields, and a
missing version probe successfully as informational, while a missing or
non-matching provider identity still reports `Incompatible`. Cancellation during
retry backoff stops further attempts, retries are bounded, non-transient
statuses (`400`/`422`, `404`, `401`/`403`) are not retried, and every retry
reapplies the API key as a header without placing it in the URL.

**Acceptance criteria:**

- [x] Each provider can be probed and queried independently.
- [x] No integration path calls an Arr write endpoint, database, or lookup
  endpoint for routine refreshes.
- [x] Actual file quality is available where the provider reports it; a quality
  profile is never presented as actual file quality.
- [x] Missing, incomplete, invalid, and unsupported provider data produces a
  bounded domain outcome and does not fail a Jellyfin request.
- [x] API keys, webhook secrets, and sensitive request details are redacted from
  diagnostics.

**Gate 2:** Both integrations pass contract and failure tests and produce the
same canonical shape for equivalent badge-relevant observations.

### 3. Media matching

**Objective:** Match eligible Jellyfin items to exactly one scoped Sonarr or
Radarr record using stable provider identity first and explicit fallback rules.

**Deliverables:**

- Provider-neutral `MediaIdentity` and `MediaMatch` implementation.
- Movie-to-Radarr matching by Jellyfin TMDb ID, then IMDb ID.
- Series-to-Sonarr matching by TVDB ID and validated stable-ID fallback where
  available.
- Episode-to-Sonarr matching by episode TVDB ID, then validated season/episode
  numbers after the series match.
- Explicit handling for no match, ambiguity, unsupported item, missing file,
  virtual item, remote item, specials, anime numbering, double episodes, and
  multi-episode files.
- V1 path fallback is deferred out of scope by ADR-008; no host/container path
  equivalence is assumed and no path-only match is accepted.

**Tasks:**

- [x] 3.1 Build Jellyfin `MediaIdentity` snapshots from the supported canonical
  item types (`Movie`, `Series`, `Season`, `Episode`) and library scope. Library
  scope entries are collection-folder/library identifiers, and V1 badge surfaces
  are Movie and Episode posters; Series/Season are structural only (ADR-006).
- [x] 3.2 Implement candidate selection, evidence recording, and deterministic
  `MediaMatch` fingerprints.
- [x] 3.3 Reject zero-candidate and multiple-candidate matches rather than
  guessing from title or year.
- [x] 3.4 Implement the documented movie, series, and episode matching order.
- [x] 3.5 Define and test the numbering policy for specials, anime, and
  multi-episode records before enabling number fallback.
- [x] 3.6 Resolve DG-5. Configured path normalization and path fallback are
  deferred out of V1 by ADR-008; this is a documentation-only closure and adds
  no configuration or runtime matching rule.
- [x] 3.7 Verify that local Arr record and file IDs are always scoped by
  connection.
- [x] 3.8 Reject ineligible item locations (remote, virtual, offline, `.strm`,
  fileless, and otherwise non-local) with a fail-closed no-badge outcome before
  provider matching, completing acceptance criterion 3. Paths remain
  non-identity context only under ADR-008.

**Task 3.1 status:** Complete. Canonical `MediaIdentity` snapshots (item type,
Jellyfin item id, collection-folder/library id, normalized provider ids, title,
production year, parent-series context, raw season/episode/end numbers, raw
location and media-source summary, and a Jellyfin source fingerprint) are built
for Movie, Series, Season, and Episode in `src/ArrTags/Media`. Library scope is
applied deterministically against collection-folder/library identifiers: an
empty scope means no restriction, and a non-empty scope with an unresolvable
library fails closed. V1 badge-surface eligibility remains limited to Movie and
Episode posters and honours the configured poster flags; Series and Season stay
structural and produce no badge. Provider DTOs and provider-specific concepts
are excluded, and the builder captures raw Jellyfin facts only, leaving the DG-4
episode-numbering policy to the matching task. DG-5 is now resolved by ADR-008;
path mapping is out of V1. Jellyfin library
lookups are isolated behind the `IMediaLibraryResolver` boundary so the snapshot
logic is covered without a live host. Remaining Milestone 3 tasks were not started at
the time of task 3.1; task 3.2 is recorded below.

**Task 3.2 status:** Complete. The provider-neutral matching foundation lives in
`src/ArrTags/Matching`. `MatchCandidate` is the canonical, connection-scoped
description of one Arr record that could match (concrete `ArrRecordIdentity`,
normalized external provider identifiers, optional title/year/numbering/path
context) and it refuses a record identity that is not scoped to its own
connection and provider kind. `CandidateMatchRule` and `ProviderIdMatchRule`
define one ordered, evidence-keyed identity comparison, and `CandidateSelector`
applies the ordered rules as identity fallbacks, recording a `MatchEvidence` step
per rule (method, key, matched value, candidate and match counts) and returning a
`CandidateSelection` of survivors. The first rule with a match decides the
selection, so later rules are fallbacks and never widen the search; title and
year are never used as identity. `MediaMatch` is the canonical result
(`status`, `method`, typed `recordIdentity`, normalized `matchedProviderIds`,
safe `ambiguityReason`, and `matchedAt`) with input validation that a
`Matched` status requires a concrete method and a connection/provider-scoped
record identity and that a non-matched status cannot carry one. `MediaMatch`
computes a deterministic `MatchFingerprint` over the match schema version, the
Jellyfin subject, the connection and provider scope, the status and method, the
full record identity including explicit file presence, and the matching provider
identifiers; the fingerprint is independent of observation timestamps and never
contains a credential. Mapping the survivor count to a `Matched`/`NotFound`/
`Ambiguous` status (task 3.3), the provider-specific candidate assembly and
documented rule order (task 3.4), the numbering policy (3.5), and configured path
mapping (3.6) are deliberately not implemented here; ADR-008 defers path mapping
out of V1.

**Task 3.3 status:** Complete. The match status policy lives in
`src/ArrTags/Matching/MediaMatchPolicy.cs`. `MediaMatchPolicy.Resolve` maps a
provider-neutral `CandidateSelection` into the canonical `MediaMatch`: zero
survivors produce `NotFound`, more than one survivor produces `Ambiguous`, and
exactly one survivor produces `Matched`. The zero- and multiple-candidate
outcomes carry no record identity and a safe, provider-neutral reason, and they
are never resolved by falling back to candidate title or production year. A
`Matched` result uses the deciding rule's `MediaMatchMethod`, the surviving
candidate's connection-scoped `ArrRecordIdentity`, and records the agreeing
provider identifier only for a `ProviderId` decision, so `Number` or
`ConfiguredPath` decisions add no provider identifiers. Provider-specific
candidate assembly and the documented movie/series/episode rule order were
completed in task 3.4; the episode numbering policy (task 3.5) is complete,
configured path mapping is deferred out of V1 by ADR-008, and the connection-
scope verification task (3.7) remains to be implemented.

**Task 3.4 status:** Complete. The documented matching order is implemented in
`src/ArrTags/Matching/MatchRuleOrder.cs`, `MediaMatcher.cs`, and
`MatchProviderIdKeys.cs`, with provider-specific candidate assembly in
`src/ArrTags/Providers/Radarr/RadarrMatchCandidateFactory.cs` and
`src/ArrTags/Providers/Sonarr/SonarrMatchCandidateFactory.cs`. `MatchRuleOrder`
returns the ordered identity rules per item type and provider kind: Movie to
Radarr uses TMDb then IMDb; Series to Sonarr uses TVDB then the other stable
provider ids Sonarr reports locally (TMDb, then IMDb); Episode to Sonarr uses
the episode TVDB id. `MediaMatcher.Match` applies that order and resolves the
outcome through the task 3.3 status policy, and `MediaMatcher.MatchEpisode`
enforces the documented "after the series match" ordering: it matches the parent
series first, then evaluates only episode candidates whose connection-scoped
`SonarrIdentity.seriesId` equals the matched series, so an episode is never
matched against another series' records. Unsupported item/provider pairs
(Movie/Sonarr, Series/Radarr, Season either provider, Episode/Radarr) and
episodes without parent series context produce `Unsupported` with a safe,
provider-neutral reason instead of a guessed match. The Radarr and Sonarr
candidate factories translate validated provider DTOs into canonical
connection-scoped `MatchCandidate` values, mapping the documented provider ids
and descriptive context and keeping provider resources at the integration
boundary; an episode candidate is always anchored to its series identity. Exact
season/episode number fallback is enabled by task 3.5 after the episode TVDB
rule; configured path fallback remains disabled by ADR-008, and the rule order
test asserts no `ConfiguredPath` rule is present. Tests in `MatchRuleOrderTests`,
`MediaMatcherTests`, and `MatchCandidateFactoryTests` cover the rule order,
cross-provider and structural rejection, TMDb-before-IMDb ordering, IMDb
fallback, connection-scoped results, series-then-episode scoping, bounded
failures, and DTO-to-candidate mapping without a live Arr instance.

**Task 3.5 status:** Complete. The explicit V1 episode-numbering policy
(decision gate DG-4, recorded in ADR-007) lives in
`src/ArrTags/Matching/EpisodeNumberingPolicy.cs` and is applied by
`src/ArrTags/Matching/SeasonEpisodeMatchRule.cs`. Number fallback is enabled in
`MatchRuleOrder` after the episode TVDB id and applies only to regular, single
episodes: the Jellyfin identity and the Sonarr candidate must each have a
positive season and episode number, season zero specials are excluded, and a
multi-episode span (`EpisodeNumberEnd > EpisodeNumber`) is excluded because one
Jellyfin item can map to multiple Sonarr episode records. An end number equal to
the start is treated as a single episode. Absolute, scene, and other alternate
numbering is never an identity key, so absolute-number agreement alone never
matches; anime and other series still require the episode TVDB id when normal
number fallback is ineligible. Configured path fallback is deferred out of V1 by
ADR-008. The comparison is exact `(seasonNumber,
episodeNumber)` equality with no tolerance and never uses title, year, path, or
air date. Ineligible or non-equivalent candidates do not satisfy the rule, so
the existing status policy yields `NotFound`/`Ambiguous` and no badge. Tests in
`EpisodeNumberingPolicyTests` cover eligibility, specials, spans, missing
numbers, item/provider mismatch, exact matching, and bounded rejection; new
`MediaMatcherTests` cases cover number fallback after the series match,
series-scoped number fallback, special and span exclusion, absolute-number
mismatch, and number ambiguity.

**Task 3.6 status:** Complete as an architecture/documentation decision only.
ADR-008 resolves DG-5 by deferring configured, connection-scoped path mappings
and path normalization out of V1. V1 never compares Jellyfin and Arr paths,
never assumes host/container equivalence, and never emits a `ConfiguredPath`
match. Items that lack the approved provider-ID or regular-number evidence remain
unmatched with no new badge. No source or test implementation was added for this
task; task 3.7 completed the connection-scoping verification recorded below.

**Task 3.7 status:** Complete. Verification confirmed that every Arr-local record
and file identifier is scoped by its originating `ArrConnection`; no production
change was needed because the canonical model already enforces the contract.
`ArrRecordIdentity` and its `SonarrIdentity`/`RadarrIdentity` shapes always carry
the connection and include it in equality, hashing, and `ToString`, and
`MatchCandidate` and `MediaMatch` both reject a record identity whose connection
does not match the match scope. `ArrFileIdentity` has no connection of its own and
is scoped only through its enclosing record identity, so consumers must resolve
files through the scoped identity. New tests in
`tests/ArrTags.Tests/ConnectionScopingTests.cs` (14 tests) prove that identical
numeric IDs on two Radarr connections and on two Sonarr connections produce
distinct identities, candidates, matches, and metadata fingerprints; that
identical local IDs across provider kinds are distinct; that connection scopes
are derived distinctly from different base URLs and are used as the provider
instance identity; and that the matcher resolves identical local IDs to the
requested connection for both providers. Build and test pass with 0 warnings and
327 passing tests at the time of task 3.7.

**Task 3.8 status:** Complete. The fail-closed V1 location policy is implemented
in `src/ArrTags/Media/MediaLocationEligibility.cs` and applied by
`src/ArrTags/Matching/MediaMatcher.cs`, completing acceptance criterion 3.
`MediaLocationSummary` now captures whether the item or any media source is
remote and whether any path is a `.strm` reference, in addition to the existing
location kind, file-protocol, source-count, and primary-path facts.
`MediaLocationEligibility` rejects remote, `MediaSourceInfo.IsRemote`, virtual,
offline, unknown, `.strm`, and non-local/fileless locations with a bounded,
path-free reason, and treats a missing location summary as not evaluated so only
positively ineligible locations are rejected. `MediaMatcher.Match` applies the
gate to the V1 badge surfaces (Movie and Episode) after the item/provider rule
check, and `MediaMatcher.MatchEpisode` applies it to the episode before any
series match, so an ineligible episode never proceeds into provider matching.
Series and Season remain structural and are intentionally not location-gated.
`MediaEligibility.IsEligible` now also requires an eligible local file location.
No path is compared or used as identity (ADR-008), and eligible local-file
matching behavior is unchanged. Tests in
`tests/ArrTags.Tests/LocationEligibilityTests.cs` (10 facts plus a 3-case theory,
13 test cases) cover eligible local Movie and Episode matching, remote, virtual,
offline, `.strm`, and fileless no-badge outcomes, the episode does-not-proceed
case, the Jellyfin location capture, and the combined eligibility gate. Build and
test pass with 0 warnings and 340 passing tests. All Milestone 3 acceptance
criteria and Gate 3 are now satisfied.

**Acceptance criteria:**

- [x] A supported Jellyfin movie can match its Radarr movie with validated
  provider identity.
- [x] A supported series and episode can match their Sonarr records using the
  approved identity and numbering policy.
- [x] Ambiguous, missing, virtual, remote, and unsupported cases produce no new
  badge and a safe diagnostic status.
- [x] Titles and years are never sole proof of an automatic match.
- [x] Changing match evidence changes the match fingerprint and invalidates the
  dependent metadata state.

**Gate 3:** Met. Representative movie, series, episode, mismatch, ambiguity,
numbering, and ineligible-location cases pass without guessed matches; build and
test pass with 0 warnings and 340 passing tests.

### 4. Badge rendering

**Objective:** Convert canonical `BadgeMetadata` and configuration into a
deterministic, bounded visual result without coupling the renderer to Sonarr,
Radarr, or Jellyfin artwork storage.

**Deliverables:**

- Configurable badge definitions for the V1 metadata fields supported by the
  selected provider data, including quality and relevant technical values.
- Provider-neutral selection, fallback, styling, placement, ordering, and
  visibility behavior.
- Versioned renderer with deterministic output and complete render fingerprints.
- Bounded image input/output, dimensions, text, concurrency, and cancellation.
- Pass-through behavior for unknown metadata, unsupported input, decode/encode
  failure, cancellation, and resource-limit violations.
- Pinned, plugin-owned renderer stack (SkiaSharp managed/native packages and the
  bundled DejaVu Sans Bold 2.37 font) with shipped license notices (ADR-010).

**Tasks:**

- [x] 4.1 Implement metadata selectors against `BadgeMetadata`, not provider DTO
  paths.
- [x] 4.2 Define the initial badge field set, text rules, contrast behavior,
  placement, scale, margins, and output format policy.
- [x] 4.3 Keep unknown technical values distinct from confirmed negative values.
- [x] 4.4 Implement render request and result fingerprints containing every
  output-affecting value, including renderer and badge schema versions.
- [x] 4.5 Enforce image and text limits before decode, draw, and encode work.
- [x] 4.7 Pin the SkiaSharp managed and Linux native asset packages to exact
  versions, embed the DejaVu Sans Bold 2.37 font as a plugin resource, and ship
  the font and Skia license notices (ADR-010).
- [x] 4.8 Spike SkiaSharp compatibility with the Jellyfin 12 host: confirm the
  host's pinned SkiaSharp/HarfBuzzSharp version and native library name, verify
  whether the plugin resolves that shared version or needs its own isolated
  copy, and prove one decode/draw/encode round trip on the pinned Linux runtime
  before building the drawing engine (ADR-010).
- [x] 4.9 Implement the provider-neutral renderer service and drawing engine: the
  `RenderAsync`/`RenderRequest`/`SourceImageInput`/`RenderResult` contract,
  ADR-009 selectors, priority, rail layout, typography, truncation, and
  contrast, and the ADR-010 sRGB PNG encode, alpha, and metadata policy
  (ADR-010).
- [x] 4.6 Test dimensions, format behavior, truncation, layout, cancellation, and
  renderer failure pass-through.
- [x] 4.10 Extend the immutable configuration model, snapshot, and validator with
  enabled V1 selectors, bounded templates, and contrast-validated palette/style
  overrides, including the secret-free renderer configuration fingerprint
  (ADR-010).
- [x] 4.11 Add golden-image, byte-determinism, PNG-contract, and cross-runtime
  tolerance tests with synthetic fixtures and no auto-approval of changed
  goldens (ADR-010).

**Authoritative Phase 4 execution order:** 4.1, 4.2, 4.3, 4.4, 4.5, 4.7, 4.8,
4.9, 4.6, 4.10, 4.11. The task list above is listed in this execution order and
must be walked in it. Task 4.6 was reordered after task 4.9 because its stated
behavior tests (format behavior, layout, cancellation, and renderer failure
pass-through) cannot compile or execute before the renderer service contract,
drawing engine, and pinned renderer assets exist; it was reported blocked in
`docs/implementation/4.6/worker-report.json` and must not be attempted before
tasks 4.7 through 4.9.

**Task 4.1 status:** Complete. The provider-neutral `BadgeSelector` vocabulary
(Quality, Resolution, DynamicRange, Source, VideoCodec, Audio, CustomBadge, and
UpgradePending) is resolved against canonical `BadgeMetadata` by
`BadgeSelectorResolver`. Resolution reads only the canonical model, emits
technical values in the ADR-009 priority order, builds the composite audio value
from confirmed features then codec then channel count, replaces the generic
dynamic-range label with `DV` only when Dolby Vision is confirmed, produces one
candidate per retained custom value, and treats `UpgradePending` as a separate
`UPGRADE` status only when explicitly true. Unknown and absent values are
omitted rather than rendered as placeholders or inferred negatives. Build and
test pass with 0 warnings and 355 passing tests (15 new selector tests).

**Task 4.2 status:** Complete as the DG-3 documentation decision. ADR-009 fixes
the V1 selector vocabulary, field priority, Movie/Episode Primary-poster
layout, typography and geometry, contrast-validated palette, 24-scalar display
limit, PNG/RGB-or-RGBA output, source-dimension scaling, high-DPI behavior, and
pass-through behavior for unknown, incomplete, cancelled, malformed, or failed
renders. No rendering code is included in this decision closure.

**Task 4.3 status:** Complete. The selector resolver keeps the canonical
unknown-versus-confirmed-negative distinction intact. Tri-state technical flags
(Dolby Vision and upgrade-pending) are resolved through one explicit
`ResolveConfirmedTrue` rule: only a confirmed `true` yields a display value, so a
confirmed negative and an unknown value are both omitted but never inferred from
one another. A confirmed generic dynamic range (for example `SDR`) is still
displayed, while an unknown range is omitted; unknown audio features do not
suppress a confirmed codec, and a confirmed empty feature set never infers a
feature. No canonical value is mutated: the metadata fingerprint continues to
distinguish unknown from confirmed-negative and unknown from confirmed-empty, and
`tests/ArrTags.Tests/BadgeUnknownValueTests.cs` proves the behavior with 11 new
tests. Build and test pass with 0 warnings and 366 passing tests.

**Task 4.4 status:** Complete. The renderer now has a provider-neutral,
output-affecting fingerprint boundary. `RenderVersion` owns the code-owned
renderer and badge schema versions. `RenderOutputPolicy` holds the ADR-009/010
output-affecting policy (format, color space, alpha policy, font identity,
palette, scale policy, and text limits). `RenderFingerprintInput` is a validated,
immutable snapshot of every output-affecting value, and `RenderFingerprint`
computes two deterministic SHA-256 fingerprints: the result/output fingerprint
(source, dimensions, metadata, configuration, resolved badge values, output
policy, and both versions; independent of item identity) and the request
fingerprint (render key) that scopes it to the Jellyfin item and poster surface.
Correlation identifiers and timestamps are absent by construction. The new
`RenderFingerprintTests` suite adds 20 tests proving determinism, item
independence of the output identity, sensitivity to every output-affecting
value and version, timestamp exclusion, and input validation. Build and test
pass with 0 warnings and 386 passing tests.

**Task 4.5 status:** Complete. The renderer now has a provider-neutral
pre-decode/pre-draw/pre-encode limit boundary. `RenderLimitGuard` validates a
bounded source descriptor (exact byte length and oriented dimensions) against
the accepted `OperationalLimits`: non-positive descriptors are rejected as
malformed, byte length is compared with `SourceArtifactLimitBytes`, and each
oriented side is compared with `MaxImageDimensionPixels` before any decode or
allocation. The planned derived output surface is validated with the same
per-side bound plus a conservative pre-encode comparison of the uncompressed
RGBA surface against `DerivedArtifactLimitBytes`. `BadgeTextNormalizer` removes
non-whitespace control scalars, collapses whitespace runs to one space, trims
the ends, and enforces the ADR-009 text limit using the `RenderOutputPolicy`
values: at most 24 Unicode scalar values, with end truncation to the first 21
scalars plus `...`. It counts scalar values rather than UTF-16 characters,
handles supplementary and combining scalars, and never splits a surrogate pair.
`RenderLimitReason` and `RenderLimitResult` provide the bounded non-secret
failure/pass-through shape: a rejected result carries exactly one safe reason
code and never a partial artifact, and source bytes are never mutated. The new
`RenderLimitTests` suite adds 39 tests. Build and test pass with 0 warnings and
425 passing tests.

**Task 4.7 status:** Complete. The renderer stack is now pinned and the bundled
font is plugin-owned. `src/ArrTags/ArrTags.csproj` references `SkiaSharp` and
`SkiaSharp.NativeAssets.Linux` at the exact Jellyfin 12.0.0 host version
`3.119.4` (no `HarfBuzzSharp`; that decision remains task 4.8), and both
`packages.lock.json` files were regenerated so locked-mode restore stays valid.
`DejaVuSans-Bold.ttf` (708,920 bytes, SHA-256
`5c1247acef7f2b8522a31742c76d6adcb5569bacc0be7ceaa4dc39dd252ce895`, version
2.37) is embedded as the stable logical resource
`ArrTags.Resources.DejaVuSans-Bold.ttf`. The new `RenderFontIdentity` type records
the family, style, version, exact byte length, SHA-256, and resource logical
name, opens a read-only stream over the exact embedded bytes, and
`RenderOutputPolicy.Default.FontIdentity` is that identity; `RenderFingerprint`
records its full descriptor, so any changed font field changes the output
fingerprint. `licenses/DejaVu-Fonts-License.txt`,
`licenses/SkiaSharp-LICENSE.txt`, and
`licenses/SkiaSharp-THIRD-PARTY-NOTICES.txt`, plus `THIRD-PARTY-NOTICES.md`,
ship with the plugin and the `PackagePlugin` target copies them into the plugin
zip. `tests/ArrTags.Tests/BundledFontTests.cs` adds 12 cases and
`RenderFingerprintTests` gains a font-asset-sensitivity case. Build and test pass
with 0 warnings and 438 passing tests (13 new). Full native-asset
plugin-load-context packaging remains the Phase 5 packaging task per ADR-010.

**Task 4.8 status:** Complete. The spike confirmed the pinned Jellyfin 12.0.0
host renderer stack from its `jellyfin.deps.json` and native files: `SkiaSharp`,
`SkiaSharp.HarfBuzz`, and `SkiaSharp.NativeAssets.Linux` at `3.119.4`, and
`HarfBuzzSharp` / `HarfBuzzSharp.NativeAssets.Linux` at `8.3.1.5`, with ELF64
x86-64 `libSkiaSharp.so` and `libHarfBuzzSharp.so`; the host's SkiaSharp managed
and native files are byte-identical to the local NuGet `3.119.4` assets. Plugin
resolution was measured against Jellyfin's pinned `PluginLoadContext` /
`PluginManager` source and on the pinned .NET 10.0.12 runtime: Jellyfin passes
the plugin folder to `AssemblyDependencyResolver`, which does not discover a
deps.json inside that folder, and then loads every DLL in the folder into the
plugin load context. A plugin that ships no SkiaSharp resolves the host's shared
managed SkiaSharp and native library; a plugin that ships `SkiaSharp.dll` gets
its own copy, and its `libSkiaSharp.so` is found only when it sits next to
`SkiaSharp.dll` in the plugin folder root. The resulting Phase 5 constraint is to
place the managed and root-level native SkiaSharp assets in the plugin folder.
The decode/draw/encode proof lives in the environment-guarded
`SkiaHostCompatibilityTests` suite: it decodes a synthetic PNG, draws with the
bundled DejaVu Sans Bold font from embedded bytes, encodes a non-interlaced
8-bit PNG, and decodes it back, all on the pinned SkiaSharp `3.119.4` native
library. `HarfBuzzSharp` is not needed for ADR-009's single-line bounded labels,
so the 4.7 omission stands. Findings, evidence, and exact commands are recorded
in `docs/research/skia-host-compatibility.md`. Default `./build.sh test` passes
with 0 warnings and 439 passing tests plus 1 environment-guarded skip; the forced
native run passes both compatibility cases.

**Task 4.9 status:** Complete. The provider-neutral renderer service and the
SkiaSharp drawing engine are implemented in `src/ArrTags/Rendering` under
ADR-009 and ADR-010. `SourceImageInput` is an immutable, bounded descriptor
carrying the exact source bytes (copied), content type, oriented dimensions, and
a verified non-empty SHA-256 identity, with no path, Jellyfin entity, provider
DTO, credential, or mutable image object. `BadgeDefinition` is the minimal
provider-neutral snapshot (selector, enabled flag, one bounded template) with a
code-owned V1 default that enables every selector, uses `{value}` for technical
templates, and uses the fixed `UPGRADE` status text; it is not yet persisted into
plugin configuration (task 4.10). `RenderRequest` carries the source, canonical
`MediaIdentity`/`MediaMatch`, optional `BadgeMetadata`, the ordered definition
snapshot, the output policy, the accepted operational limits (cloned), the
secret-free configuration fingerprint, and both versions. `RenderResult` is one
of three bounded variants: rendered (complete PNG artifact, `image/png`, oriented
dimensions, output hash, deterministic output fingerprint), pass-through, or
failed with exactly one safe reason code and never a partial artifact.

`IRenderer.RenderAsync` is served by `SkiaBadgeRenderer`. It resolves the ordered
`BadgeSelection` through `BadgeSelectorResolver` (never branching on provider
kind), enforces the source byte/dimension and derived-output limits before
decode, validates the palette contrast, decodes with SkiaSharp, applies EXIF
orientation to pixels before layout, detects meaningful alpha, packs the
bottom-left two-row/three-pill rail and the independent top-right status pill
using `BadgeLayoutEngine`, and encodes a fixed-settings non-interlaced 8-bit
sRGB PNG with RGB for opaque output and RGBA (straight alpha, canonical
transparent-pixel RGB) otherwise. `BadgeGeometry` holds the ADR-009 reference
geometry (24 px inset, 8 px pill/row gaps, 48 px pill height, 8 px radius, 12/7
px padding, 28 px font) and the `clamp(width / 1000, 0.5, 4.0)` scale;
`BadgeTextNormalizer.Shorten` supplies the layout-stage end-truncation rule; and
the status/technical rail never paint outside the safe area on a short poster.
Cancellation is checked before decode, after decode, between layout and drawing,
and before finalization; a cancellation or exception discards all partial output
and never mutates the source bytes. The renderer output is deterministic: the
same request produces identical bytes, output hash, and output fingerprint.

Default `./build.sh test` passes with 0 warnings and 514 passing tests plus 10
environment-guarded Skia skips; with `ARRTAGS_SKIA_COMPAT=1` and the pinned
sysroot on the loader path the full suite passes all 524 tests, including the
nine real decode/draw/encode render cases (RGB/RGBA dimensions and channels,
source-alpha preservation, palette placement, sRGB/metadata chunks, EXIF
orientation, unsupported-input failure, source immutability, and byte
determinism). The dedicated behavior matrix (4.6) and the renderer
configuration model (4.10) are complete; golden/cross-runtime determinism tests
were completed in task 4.11.

**Task 4.6 status:** Complete. The dedicated renderer behavior matrix is
implemented as seven test-only files under `tests/ArrTags.Tests/`
(`RendererBehaviorFixtures`, `RendererBehaviorDimensionTests`,
`RendererBehaviorFormatTests`, `RendererBehaviorTruncationTests`,
`RendererBehaviorLayoutTests`, `RendererBehaviorCancellationTests`, and
`RendererBehaviorFailureTests`) with 51 new cases (40 unguarded and 11
environment-guarded real render cases) and no production change. Dimensions: the
pure scale theory proves `clamp(width / 1000, 0.5, 4.0)` scales the ADR-009
geometry (pill height and inset) for widths 320/500/1000/2000/5000, and guarded
renders prove a 320x480 opaque source, a 2000x3000 opaque source, a 720x480 alpha
source, an EXIF orientation-3 JPEG, and an EXIF orientation-8 JPEG all keep the
oriented source dimensions and the IHDR dimensions with no upscaling or
downscaling. Format: guarded structural parsing proves a non-interlaced 8-bit PNG,
RGB (color type 2) for opaque output and RGBA (color type 6) for meaningful
alpha, an `sRGB` chunk, an `IEND` terminator, and no retained
`iCCP`/`eXIf`/`tIME`/`tEXt`/`zTXt`/`iTXt` source metadata; a semi-transparent
source pixel keeps straight alpha (alpha 128 and RGB within one channel step), and
a hand-crafted source with non-zero hidden RGB on a fully transparent pixel is
canonicalized to `#00000000`. Truncation: a resolved 40-scalar custom value
normalizes to a 21-scalar prefix plus `...` (24 total), a 24-scalar value is
unchanged, whitespace/control scalars are normalized first, a value that cannot
fit any pill is omitted without expanding lower-priority values, width fitting
shortens an over-wide 24-scalar label further while staying within the 24-scalar
bound, and width fitting never splits a supplementary scalar. Layout: the rail
packs three pills per row across two rows, overflow moves to the next row,
lower-priority candidates are omitted once both rows are full, no pill is split
across rows, every pill stays inside the safe area of narrow and short posters,
the independent top-right `UPGRADE` status pill resolves only from a confirmed
true value and never overlaps the rail, and the technical rail is anchored
bottom-left. Cancellation: a cancelled token is observed before decode and before
the ineligible-surface, source-limit, and contrast decisions, returning
`Failed(Cancelled)` with no artifact and an unchanged source; the renderer's
documented later checkpoints are not independently reachable through the public
synchronous contract without production-only test hooks, so the earliest
checkpoint is the one deterministically exercised and that limitation is recorded.
Failure/pass-through: ineligible Series/Season surfaces, a non-matched result,
missing metadata, no displayable value, and an unavailable source pass through;
source-byte, source-dimension, and derived-output limit violations, low contrast,
an invalid color policy, a missing font resource, unsupported source bytes, and a
descriptor that does not match the decoded dimensions all return exactly one
bounded `Failed` reason with no partial PNG and an unchanged source. Default
`./build.sh test` passes with 0 warnings and 554 passing tests plus 21
environment-guarded Skia skips (575 total); with `ARRTAGS_SKIA_COMPAT=1` and the
pinned sysroot on the loader path the full 575-test suite passes. The renderer
configuration model was completed in task 4.10; the golden/cross-runtime
determinism tests were completed in task 4.11.

**Task 4.10 status:** Complete. The persisted, immutable-replacement
configuration model now carries the user-adjustable V1 renderer configuration
under ADR-010. `RendererConfiguration` (with the `BadgeSelectorConfiguration`
entries) expresses only the enabled/disabled V1 `BadgeSelector` set and one
bounded provider-neutral `{value}` template per selector, plus four optional
palette overrides (technical and upgrade-status background/text). Output format,
color space, alpha policy, font identity, geometry/reference values, text limits,
and the renderer version remain code-owned and are deliberately not representable
in configuration; no geometry or placement value is exposed because ADR-009
fixes them and ADR-010 does not authorize a user-selectable override for them.
Defaults reproduce ADR-009 exactly: an absent selector keeps the code-owned
`BadgeDefinition.V1Default` entry (all selectors enabled, `{value}` technical
templates, fixed `UPGRADE` status text) and an unset palette keeps the ADR-009
colors.

`RendererConfiguration.Validate` (called from `PluginConfigurationValidator`)
rejects unknown or duplicate selector entries, empty/too-long templates or more
than one `{value}` placeholder, malformed color values, and any style pair whose
WCAG contrast is below `BadgeContrast.MinimumRatio` (4.5:1) with a safe,
secret-free message. `RendererConfigurationResolver` maps a configuration to the
ordered `BadgeDefinition` snapshot (canonical selector order; first valid entry
per selector; tolerant fallback to defaults for a configuration that has not been
validated) and to the effective `RenderOutputPolicy` (configured palette applied,
every other value copied from `RenderOutputPolicy.Default`). `RendererPalette`
canonicalizes a valid override to `#RRGGBB` so an equivalent color cannot change
the fingerprint. `PluginConfigurationSnapshot` exposes the validated
`BadgeDefinitions`, `RendererOutputPolicy`, and the secret-free
`RendererConfigurationFingerprint`; it remains immutable and secret-free, and
`From` keeps working for callers/tests that build from a plain configuration.

`RendererConfigurationFingerprint.Compute` is a deterministic uppercase SHA-256
over the renderer configuration schema version, the ordered selector
enablement/templates, and the effective palette. It excludes credentials, the
webhook secret, timestamps, and correlation identifiers by construction, so
rotating a secret cannot change it; equivalent color casing and selector entry
order are normalized. The value is the one supplied to
`RenderRequest.ConfigurationFingerprint`. The ADR-005 secret boundary is
unchanged: no credential or webhook secret enters the renderer configuration,
the snapshot renderer fields, or the fingerprint. `ConfigurationSnapshotService`
`TryReplace` still activates a candidate only when it validates, so an invalid
renderer configuration leaves the last valid public snapshot (and private
secrets) active.

`tests/ArrTags.Tests/RendererConfigurationTests.cs` adds 21 unguarded cases
covering the default-to-ADR-009 mapping, valid/invalid selector and template
configuration, palette acceptance and canonicalization, malformed colors, the
4.5:1 contrast boundary (`#767676`/`#FFFFFF` accepted, `#777777`/`#FFFFFF`
rejected, `#757575`/`#000000` accepted, `#747474`/`#000000` rejected),
snapshot immutability and secret exclusion, fingerprint determinism and
sensitivity to every output-affecting value, XML round-trip persistence, and
last-valid retention plus private-secret retention through
`ConfigurationSnapshotService.TryReplace`. No Skia native runtime is required.
Build and test pass with 0 warnings and 575 passing tests plus 21
environment-guarded Skia skips (596 total); the forced native run is unchanged.
The renderer configuration is not wired to the Jellyfin admin save surface, DI,
providers, or the artwork pipeline (not a Phase 4 prerequisite), and the
golden/cross-runtime determinism tests were completed in task 4.11.

**Task 4.11 status:** Complete. The ADR-010 test oracle is in place: committed,
repository-owned synthetic decoded-pixel goldens for the full fixture list with
no auto-approval or writer path; byte-determinism across repeated renders, item
identity, observation timestamp, stream chunking, and a separate fresh process;
PNG-contract tests for non-interlaced 8-bit RGB/RGBA, the fixed `sRGB`
declaration, stripped metadata, canonical transparent-pixel RGB, straight
source-alpha preservation, and malformed/unsupported embedded color-profile
rejection; and an ADR-010 cross-runtime tolerance comparator with unguarded
boundary unit tests and an exact canonical-runtime run. New unguarded files are
`CrossRuntimePixelComparator`, `CrossRuntimePixelComparatorTests`,
`RenderGoldenFixtures`, `RenderGoldenTests`, `RenderImageFixtures`,
`RenderPngContractTests`, `RendererCrossRuntimeTests`,
`RenderDeterminismTests`, `RenderDeterminismProcessProbe`,
`RenderOrientationTests`, `SkiaNativeTheoryAttribute`, and
`NonCanonicalRuntimeFactAttribute`; `tests/ArrTags.Tests/Goldens/` holds the nine
committed PNGs and their manifest. Default `./build.sh test` passes 593 tests
with 40 environment-guarded skips (633 total); the forced native run with
`ARRTAGS_SKIA_COMPAT=1` and the pinned sysroot passes 655 tests with one
non-canonical-golden skip (656 total). The suite fails closed on a mutated
golden (verified separately by substituting a different committed PNG and by
corrupting a manifest fingerprint) and there is no test path that writes or
regenerates a golden.

The F2 supporting production change deferred from task 4.9 is implemented:
`SourceColorProfile`/`SourceColorProfileKind` inspect a recognized PNG `iCCP` or
JPEG `APP2` embedded ICC profile, treat an input without a profile as sRGB, and
fail closed with the new safe `RenderFailureReason.UnsupportedColorProfile` when
an embedded profile cannot be parsed by `SKColorSpace.CreateIcc`; a supported
profile is converted to sRGB by the existing sRGB decode destination.
`RenderPngContractTests` asserts both rejection and conversion. A supported or
absent profile renders byte-identical output and an invalid profile now produces
no artifact, so the F2 change alone did not require a version bump.

The authorized task 4.11 correctness fix corrects the pre-existing EXIF
dimension-swapping orientation defect. `SkiaOrientation.Apply` had used the
*oriented* `height`/`width` as the translation origin for EXIF orientations 5-8
instead of the source dimensions, so an opaque 500x750 JPEG with orientation 6
rendered to 750x500 with 125,000 fully transparent pixels and clipped content,
orientation 7 left 250,000 transparent, and orientation 8 left 187,500
transparent. The transforms now translate about `source.Height`/`source.Width`;
`RenderOrientationTests` proves all eight orientations are fully opaque and place
two distinct corner markers in the expected quadrants, and the committed
`orientation` golden now encodes the corrected dimension-swapping orientation 6.
Because this is an output-affecting drawing change, `RenderVersion` was bumped
from 1 to 2 and the committed golden manifest was regenerated; the badge schema
version is unchanged. The orientation golden's decoded pixels are unchanged in
shape and the other eight fixtures stay byte-identical while all output
fingerprints advance with the renderer version.

The non-canonical cross-runtime half could not be executed: only the pinned
canonical runtime (and the identical Jellyfin host runtime) exists in this
environment, so the ADR-010 0.1 percent anti-aliased-text tolerance is
implemented and unit-tested and the exact canonical half runs against every
golden, but no second runtime is available. The comparison is data-driven: an
optional `Goldens/non-canonical/` set (same manifest shape) runs through the
tolerant comparator when supplied, and is reported as a skipped
`NonCanonicalRuntimeFact` while absent. The recommended resolution is to select
and record the second explicitly supported non-canonical Linux runtime and
produce its golden set in the testing/release milestone; this remaining
environment limitation is recorded in `docs/implementation-readiness.md`.

**ADR-010 implementation tasks:** ADR-010 adds the renderer library, bundled
font, PNG/alpha/color, service-contract, configuration, and test-oracle work
listed above. These are Phase 4 implementation tasks because they are entirely
renderer-local and testable without Jellyfin artwork publication. The
renderer-side `SourceImageInput` contract is Phase 4; the Jellyfin host adapter
that reads the unindexed `Primary` source image and supplies its bytes remains
Phase 5 source-capture work. Publication, provenance, restoration, caching,
stale-artwork lifecycle, and Enhanced coexistence are not pulled into Phase 4.

**Acceptance criteria:**

- [x] The same source image, canonical metadata, configuration, request
  parameters, and renderer version produce the same logical output.
- [x] The renderer never claims a value based only on missing provider data.
- [x] Quality badges show actual observed quality, not requested quality policy.
- [x] Original source bytes are not modified by rendering.
- [x] Every rendering failure leaves the current usable artwork unchanged and
  records a bounded, non-secret diagnostic.

**Gate 4:** Met. Renderer unit tests pass for normal, unknown, oversized,
malformed, cancelled, and failed inputs; the forced native run with the pinned
SkiaSharp runtime passes 655 tests (one deferred non-canonical cross-runtime
skip). At the time Gate 4 was met, Phase 5 had not started and required explicit
user approval; Phase 5 task 5.1 has since begun under that approval.

### 5. Jellyfin artwork integration

**Objective:** Publish derived badge artwork through supported Jellyfin 12
item-image APIs while preserving original source artwork through plugin-owned
provenance and coexisting with Jellyfin Enhanced.

**Deliverables:**

- Version-pinned publication spike covering the selected item/image types,
  source-artwork capture, supported image publication, and standard image-route
  delivery.
- Artwork publisher using Jellyfin's supported item-image APIs; no MVC filter or
  middleware response interception.
- Eligibility checks for item type, image type, library scope, configuration,
  match state, and current metadata.
- Correct publication and restoration behavior, with standard Jellyfin image
  tags, authorization, cache headers, conditional requests, and client delivery
  verified after publication.
- Crash-recoverable artwork operations with durable staged artifacts, write-ahead
  publication intent, postcondition reconciliation, and guarded lifecycle
  handling for disable, uninstall, and item removal.
- Jellyfin Enhanced coexistence policy and tests, including spoiler/hidden image
  behavior.

**Tasks:**

- [x] 5.1 Confirm the exact supported Jellyfin item-image publication ABI and
  route variants.
- [x] 5.2 Implement source-artwork provenance and guarded restoration state
  before publishing derived artwork.
- [x] 5.3 Implement the Jellyfin host source adapter that reads the unindexed
  `Primary` source image and supplies the Phase 4 renderer's `SourceImageInput`
  bytes, content type, dimensions, and hash; keep Jellyfin access out of the
  renderer (ADR-010).
- [x] 5.4 Extend plugin packaging so the renderer's managed dependencies, Linux
  native assets, dependency manifest, and Skia/font license notices are included
  in the plugin zip and resolve under the host's plugin load context; the
  current `PackagePlugin` target copies only the main assembly (ADR-010).
- [x] 5.5 Publish completed artwork through Jellyfin's supported item-image APIs;
  do not write media-folder posters or Jellyfin's image cache directly.
- [x] 5.6 Persist a durable `ArtworkOperation` before `SaveImage`, including
  before and candidate-after identities, artifact references, generation, and
  ownership/publication tokens.
- [x] 5.7 Reconcile uncertain `SaveImage`, item update, and provenance
  persistence outcomes by postcondition; never blindly replay or delete an
  active artifact.
- [ ] 5.8 Preserve the current usable artwork when source capture or rendering
  cannot safely complete.
- [ ] 5.9 Fence and drain publication operations during disable/uninstall, and
  tombstone confirmed item removal without issuing image mutations.
- [ ] 5.10 Add the configured disable/limit policy for duplicate or overlapping
  badges; do not depend on Jellyfin Enhanced internals.
- [ ] 5.11 Test Web and image-consuming clients through the supported server
  image response path.

**Authoritative Phase 5 execution order:** 5.1, 5.2, 5.3, 5.4, 5.6, 5.5, 5.7,
5.8, 5.9, 5.10, 5.11. Task IDs are stable references only; this execution order
is the canonical sequence. The order is derived from the documented dependencies,
not from task numbering:

- 5.1 has no prerequisites and confirms the item-image publication ABI and route
  variants that the ABI-dependent tasks rely on.
- 5.2 precedes 5.5 because source-artwork provenance and guarded restoration
  state must exist before derived artwork is published.
- 5.3 precedes 5.5 because the publisher consumes the `SourceImageInput` captured
  by the host source adapter.
- 5.4 precedes 5.5 because the renderer's managed dependencies, native assets,
  dependency manifest, and license notices must resolve under the host plugin
  load context before publication is exercised on a host.
- 5.6 precedes 5.5 because the durable `ArtworkOperation` must be persisted
  before `SaveImage`.
- 5.7 depends on 5.5 and 5.6; 5.8 depends on 5.3 and 5.5; 5.9 depends on 5.5,
  5.6, and 5.7; 5.10 depends on 5.5 and the DG-8 Enhanced coexistence decision;
  5.11 depends on 5.1 and 5.5.

**Task 5.1 status:** Complete. The exact supported Jellyfin 12.0.0 item-image
publication/read ABI, the standard `ImageController` route variants, the
read/write authorization split, and Jellyfin's ownership of image tags, caching,
and resizing are pinned with evidence tied to the pinned artifacts and recorded
in `docs/research/jellyfin-12-architecture.md` section 4.4 (with the route table
in section 3.1 and the response/authorization behavior in section 3.3). The
publication surface is
`MediaBrowser.Controller.Providers.IProviderManager.SaveImage(BaseItem, Stream,
string, ImageType, int?, CancellationToken)` plus its URL and path overloads; the
read surface is `BaseItem.GetImageInfo`/`ImageInfos`, `ItemImageInfo`,
`ImageInfo`, `IImageProcessor.GetImageCacheTag`/`GetImageDimensions`, and
`ILibraryManager.UpdateImagesAsync`/`ConvertImageToLocal`; the repository update
is `BaseItem.UpdateToRepositoryAsync(ItemUpdateType.ImageUpdate, ...)`. The
confirmed routes are `Items/{itemId}/Images/{imageType}` (GET/HEAD),
`Items/{itemId}/Images/{imageType}/{imageIndex}` (GET/HEAD),
`Items/{itemId}/Images/{imageType}/{imageIndex}/{tag}/{format}/{maxWidth}/{maxHeight}/{percentPlayed}/{unplayedCount}`
(GET/HEAD), `Items/{itemId}/Images` (GET), and the POST/DELETE write routes. The
GET/HEAD item-image read actions carry no `[Authorize]`; they resolve the item
through `GetItemById<BaseItem>(itemId, User.GetUserId())`, where an anonymous
user id is empty and maps to a null user, so a known item's image is served
anonymously (HTTP 200) and only an unknown item id or missing image yields 404.
The item-image information action and all write actions require authorization,
and the pinned host OpenAPI shows `security=null` for the GET/HEAD item-image
operations. Later Phase 5 tasks must not assume a 401 from the read image route
or treat it as an authorization boundary. A new unguarded ABI test
(`JellyfinImageAbiTests`, 11 cases) reflects the pinned `12.0.0` NuGet
assemblies, and a new host-guarded route/authorization test
(`JellyfinImageRouteTests`, 5 cases) reflects the pinned host `Jellyfin.Api.dll`
via `ARRTAGS_JELLYFIN_HOST_DIR`. The host-guarded route confirmation was
executed against `/tmp/opencode/jf/jellyfin` (5/5 passed, alongside the 11/11
ABI cases) and cross-checked against the live pinned host's generated OpenAPI
document. V1 remains the
unindexed `Primary` poster for Movie and Episode only (ADR-006/ADR-009);
indexed/alternate poster surfaces are out of V1. The ABI is confirmed; the exact
on-disk representation, post-publication read-back, multi-RID packaging, and
end-to-end standard-route delivery remain later Phase 5 validation, not ABI
uncertainty.

**Task 5.2 status:** Complete. The provider-neutral, plugin-owned source-artwork
provenance and guarded restoration state model is implemented in
`src/ArrTags/Artwork`. `ArtworkImageSurface` is the canonical image-surface
identity (V1 unindexed `Primary`; the optional index is retained for the data
model and the state invariant rejects an indexed surface as out of V1 scope).
`ActiveImageIdentity` records the observable surface/presence, content SHA-256,
byte length, dimensions, modification time, and Jellyfin image tag, with an
explicit absent baseline and no path. `ArtworkOwnershipComparer` implements the
fail-closed ADR-002 rule: surface and presence must match, a present identity
requires a matching content hash, and every recorded Jellyfin value must still
match when observable; a missing hash or unavailable observation yields
`Unknown`, never ownership. `PublishedArtworkState` carries every data-model
3.10.1 field and state, and `Validate` enforces the documented invariants (a
complete publication set, a present active identity with a hash, a retained
source artifact plus fingerprint for a present baseline, opaque bounded tokens,
and model-version compatibility). `PublishedArtworkStateTransitions` implements
every section 3.10.2 guarded logical transition as pure decisions and commits
(new session capture, repeated publication that reuses the source artifact and
ownership token while issuing a new publication token and active identity,
`OwnershipLost`/`OwnershipUnknown`, `RestorePending`, `Restored`,
`RestoreBlocked`, `Removed`, and an observation refresh); it performs no image
mutation and refuses to re-baseline a blocked record. `SourceArtifactStore`
provides a content-addressed immutable store under the plugin data folder that
records the exact bytes plus MIME type, length, and SHA-256, atomically promotes
after bounded size/MIME/magic-byte/hash validation, validates integrity on read,
is traversal-safe, rejects new work when the authoritative storage quota would be
exceeded, and persists its manifest through the authoritative state boundary so
provenance is never evicted as ordinary cache. `PublishedArtworkStateStore`
persists the state through the existing versioned, integrity-tagged,
atomically-written boundary, enforcing the invariants on write and quarantining a
valid-envelope-but-invalid record rather than replaying it. `StateRepository`
gained a read-only `Limits` accessor so the store shares the configured bounds.
The durable `ArtworkOperation` journal (5.6), the Jellyfin host source adapter
(5.3), publication (5.5), reconciliation (5.7), and lifecycle fencing (5.9)
remain later tasks. New tests (`ArtworkProvenanceTests`,
`SourceArtifactStoreTests`, `PublishedArtworkStateStoreTests`, 75 cases) cover
the comparison match/mismatch/unknown/surface/presence/missing-hash rules, the
state invariants, every transition, artifact round-trip/integrity/traversal/
atomic-promotion/no-cache-eviction behavior, and authoritative persistence and
quarantine. No ADR, `RenderVersion`, renderer behavior, or existing passing
behavior was changed.

**Task 5.3 status:** Complete. The plugin-owned, host-neutral Jellyfin host
source adapter is implemented in `src/ArrTags/Artwork`. `IArtworkSourceReader`
returns an `ArtworkSourceReadResult` that carries presence, the exact bounded
source bytes, the confined content type, byte length, content SHA-256, the
post-orientation display dimensions, and the Jellyfin identity fields, and it
can construct both a valid `SourceImageInput` and the task 5.2
`ActiveImageIdentity` from one read. `ArtworkSourceReader` is the host-neutral
core: it enforces the V1 unindexed `Primary` surface, the `OperationalLimits`
source byte and decoded dimension bounds, and the container confinement, and it
maps every failure to a bounded reason without throwing. `JellyfinArtworkImageAccess`
is the Jellyfin 12.0.0 implementation: it resolves the item through
`ILibraryManager`, reads `BaseItem.GetImageInfo(ImageType.Primary, 0)`, converts a
non-local image to a local file through the supported `ConvertImageToLocal`
API when required, reads a bounded byte copy, and observes the image tag and
modification time; all `MediaBrowser.*` references are confined to that file and
`src/ArrTags/Rendering` remains Jellyfin-free. Oriented dimension handling is
pinned: Jellyfin reports the pre-EXIF-orientation encoded dimensions
(`SkiaEncoder.GetImageSize` returns `SKCodec.Info`), so `SourceImageDescriptor`
derives the true display dimensions from the exact bytes with the pinned
SkiaSharp 3.119.4 codec and `SourceOrientationExtensions`, guaranteeing parity
with the renderer's own `EncodedOrigin` validation. Content-type confinement
closes the carried-forward Phase 4 MEDIUM finding: only `image/png` and
`image/jpeg` signatures are accepted and GIF, WebP, BMP, AVIF, and unrecognized
bytes fail closed, so a malformed profile in an uninspected container can never
be treated as sRGB; the renderer's `SourceColorProfile` was not modified.
`IArtworkImageAccess` is the injectable host seam that makes the adapter testable
without a live host. New tests (`ArtworkSourceReaderTests`,
`ArtworkSourceContentTypeTests`, `JellyfinArtworkImageAccessTests`,
`SourceImageDescriptorTests`, 65 cases with four native-guarded) cover present,
absent, unsupported-surface, unsupported-container, oversized-byte,
oversized-dimension, unreadable, hash/length/surface correctness, oriented
dimensions, supported non-local conversion, and boundary-neutrality checks. The
default suite passes 740 with 49 guarded skips (789 total) and the forced native
suite passes 813 with 6 skips (819 total), with 0 warnings and no regressions. No
ADR, `RenderVersion`, renderer behavior, or existing passing behavior was changed.

**Task 5.4 status:** Complete. The plugin package now ships the renderer's
runtime closure at the plugin folder root, and both the live pinned host and a
replicated plugin load context confirm it resolves under the host's plugin load
context. `src/ArrTags/ArrTags.csproj`'s `PackagePlugin` target takes the managed
`SkiaSharp.dll` and the matching `linux-x64` `libSkiaSharp.so` from the project's
MSBuild-resolved runtime assets (`RuntimeCopyLocalItems` and
`RuntimeTargetsCopyLocalItems`) rather than a hard-coded NuGet cache path, stages
them next to `ArrTags.dll` along with `ArrTags.deps.json`, `build.yaml`,
`THIRD-PARTY-NOTICES.md`, and the `licenses/` notices, and fails the build when
either renderer asset is not resolved. The `linux-x64` RID is declared by
`PluginRuntimeIdentifier` (the measured layout: the Jellyfin 12 plugin load
context probes only the plugin folder root, not `x64/` or
`runtimes/<rid>/native/`). `build.yaml`'s `artifacts` now lists `ArrTags.dll`,
`SkiaSharp.dll`, `libSkiaSharp.so`, and `ArrTags.deps.json`; the identity,
`targetAbi: 12.0.0.0`, and `framework: net10.0` are unchanged, and `assemblies`
is left empty so Jellyfin's folder scan loads the bundled `SkiaSharp.dll`.
V1 claims only `linux-x64`; other Linux RIDs are not claimed and no arbitrary
system Skia library is loaded. Multi-RID packaging is not part of V1. The
package is verified by `tests/ArrTags.Tests/PluginPackagingTests.cs` (five
unguarded contract facts plus three package-content facts guarded on the
presence of `artifacts/ArrTags_*.zip`) and by manual inspection of
`./build.sh package` output. Live-host validation installed the package on the
pinned Jellyfin 12.0.0 host: the log records loading `SkiaSharp, Version=3.119.0.0`
from the plugin folder and `Loaded plugin: "ArrTags" "0.1.0.0"`, startup
completed, `/proc/<pid>/maps` maps `ArrTags.dll` and `SkiaSharp.dll` from the
plugin folder, and the host wrote `meta.json` with `targetAbi: 12.0.0.0` and
`status: Active`. A replicated `PluginLoadContext` run over the exact extracted
package, with the host's managed and native SkiaSharp preloaded, bound the
plugin-context `SkiaSharp` to the plugin folder, completed a native
decode/draw/encode call, and mapped the plugin-folder `libSkiaSharp.so` as a
second copy. A full image render through Jellyfin is not wired until tasks
5.5/5.11, so that path is not claimed. Default `./build.sh test` passes 745 with
52 guarded skips (797 total; 748/49/797 when the package is present) and the
forced native run passes 821 with 6 skips (827 total), with 0 warnings and no
regressions. No ADR, `RenderVersion`, renderer behavior, or existing passing
behavior was changed.

**Task 5.6 status:** Complete. The durable, provider-neutral `ArtworkOperation`
write-ahead record, its phase/lifecycle-fence rules, and its authoritative store
are implemented in `src/ArrTags/Artwork`, following ADR-003, `docs/data-model.md`
sections 3.10.3 and 3.10.4, and `docs/architecture.md` section 9. The model
carries every documented field (`modelVersion`, `operationId`, `kind`,
`jellyfinItemId`, `imageSurface`, `generation`, `ownershipToken`,
`priorPublicationToken`, `publicationToken`, `expectedBeforeIdentity`,
`candidateAfterContentSha256`, `observedAfterIdentity`, `sourceArtifactId`,
`derivedArtifactId`, `phase`, `lifecycleFence`, `attempt`, `lastError`,
`createdAt`/`updatedAt`) plus explicit `sourcePresence` and
`candidateAfterPresence` fields that make the documented conditional
requirements enforceable, and `Validate` enforces every conditional rule (a
valid item/surface/operation id; a publication requires a publication token and
a derived artifact; a restoration requires a source reference; a present after
target requires its content hash; a present source requires a content-addressed
source artifact; generation and attempt are bounded; tokens are opaque and
bounded; no path or credential field). `ArtworkOperationPhase` matches the
data-model table exactly and is documented as a durable lower-bound marker;
`ArtworkOperationPhases` is the pure phase-advance helper (stepwise forward order,
terminal outcomes from any non-terminal phase, no backward or past-terminal
advance), and `ArtworkOperationFencing` provides the pure generation and
lifecycle-fence decisions (`AllowsNewPublication`, `AllowsNewRestoration`,
`IsStale`, `CanSupersede`, `IsSameGeneration`) without any lifecycle event
wiring. `ArtworkOperationErrors` redacts control characters and bounds
`lastError`. `ArtworkOperationStore` persists through the existing versioned,
integrity-tagged, atomically-written `StateRepository`/`StateAuthority.Authoritative`
boundary keyed per item/image surface, validates on write, quarantines a
valid-envelope-but-invalid payload rather than replaying it, fences writes by the
monotonic generation (a stale generation cannot overwrite a newer durable
record; a newer generation supersedes; the same generation may only advance the
same operation through a legal phase), and marks only terminal phases
(`Committed`, `Aborted`, or `RecoveryBlocked`) eligible for terminal-provenance
retention so a non-terminal operation is never pruned. A tombstoned item-removal
operation is terminal because its phase is `Aborted`, not because of its
lifecycle fence. The store performs no
image mutation and does not call `SaveImage`; task 5.5 drives the ordering.
`docs/data-model.md` section 3.10.3 records the two explicit presence fields. New
tests (`ArtworkOperationTests`, `ArtworkOperationStoreTests`, 101 cases) cover
every conditional requirement, absent-versus-present after target, token bounds,
`lastError` redaction, the phase enum and every legal/illegal transition, the
fence decisions, durability across a reconstructed `StateRepository`, generation
fencing, one-non-terminal-per-subject, authoritative quarantine on a corrupt or
semantically invalid record, and terminal-versus-non-terminal retention. Default
`./build.sh test` passes 849 with 49 guarded skips (898 total) and 0 warnings; no
ADR, `RenderVersion`, renderer behavior, or existing passing behavior was
changed.

**Task 5.5 status:** Complete. The provider-neutral single-subject publication
orchestration and the single Jellyfin image-mutation implementation are
implemented in `src/ArrTags/Artwork`, driving the authoritative durable
publication protocol (`docs/architecture.md` section 9 steps 4-10; ADR-002,
ADR-003) through the confirmed Jellyfin 12.0.0 ABI. `IArtworkImageWriter` is the
host-neutral mutation boundary and `JellyfinArtworkImageWriter` is its only
implementation: it resolves the item through `ILibraryManager`, calls the
supported `IProviderManager.SaveImage(BaseItem, Stream, string, ImageType, int?,
CancellationToken)` stream overload with the durable derived bytes and
`image/png`, and then calls `BaseItem.UpdateToRepositoryAsync(ItemUpdateType.ImageUpdate,
...)`, which is the same supported flow the standard item-image controller uses.
It never writes a media-folder poster, Jellyfin's image cache, or an item-image
path directly, never uses the filesystem-path overload that deletes its source,
and never publishes derived bytes through a URL overload; all `MediaBrowser.*`
references for publication are confined to that one file and
`src/ArrTags/Rendering` stays Jellyfin-free. `ArtworkPublisher` is the
host-neutral orchestration: it reads the authoritative `PublishedArtworkState`,
observes and confines the current source through `IArtworkSourceReader`, retains
a present baseline (or records an explicit absent baseline) in the task 5.2
`SourceArtifactStore`, promotes the validated render output to a durable derived
artifact, builds the exact `expectedBeforeIdentity` from a fresh source read and
the `candidateAfterContentSha256` from the derived artifact, and writes a
`Prepared` `ArtworkOperation` before any external image mutation. It re-reads and
revalidates the before identity immediately before mutation; a mismatch or an
unobservable identity never calls `SaveImage`, and the operation is marked
`Aborted`. When a previously published session exists, the failed check also
persists the ownership outcome (`OwnershipLost`/`OwnershipUnknown`); when no prior
publication session exists (an initial capture), it persists no
`PublishedArtworkState`. It durably advances through
`MutationStarted`, `RepositoryUpdateStarted`, `VerificationPending`, and
`FinalizationPending` before each external step; and it commits the final
`PublishedArtworkState` (new publication token, active identity, state revision,
and operation id while retaining the source artifact and ownership token) before
marking the operation `Committed`. Any failure or uncertainty leaves the current
artwork unchanged, records a bounded non-secret diagnostic, and never marks the
operation committed; the recovery decision table itself remains task 5.7, and a
pre-existing non-terminal or recovery-blocked operation blocks new work. Repeat publication verifies
the prior publication is still active, reuses the first source artifact and
ownership token, and issues a new publication token, so an ArrTags output is
never captured as a new source. The publisher is registered with the existing
lazy DI pattern (no startup work) as a single entry point Phase 6 can drive; no
event, queue, or library-scan wiring is added. New tests
(`ArtworkPublisherTests`, `JellyfinArtworkImageWriterTests`, 23 cases) cover the
happy path with durable phase ordering, absent baseline, before-identity mismatch
and unobservable-before aborts with no `SaveImage` call, readback mismatch and
item-update failure not committing, repeat publication source/ownership-token
reuse with a new publication token, ownership-lost and stale-baseline blocking,
non-terminal-operation blocking, bounded cancellation, derived-artifact and
derived-hash rejection, state path/secret hygiene, the stream overload being used
instead of the deleting path or URL overload, the normal image update flow, and
boundary-neutrality. Default `./build.sh test` passes 872 with 49 guarded skips
(921 total) and 0 warnings, exactly +23 over the task 5.6 baseline; no ADR,
`RenderVersion`, renderer behavior, or existing passing behavior was changed.

**Task 5.7 status:** Complete. The provider-neutral, postcondition-based
reconciliation service is implemented in `src/ArrTags/Artwork` following
ADR-003, `docs/data-model.md` section 3.10.4, and `docs/architecture.md`
section 9 ("Crash-consistent publication and recovery" and "Restart
reconciliation"). `ArtworkRecoveryDecisions` is the pure implementation of the
3.10.4 decision table: it returns a bounded action (`NothingToReconcile`,
`Resume`, `AbortFenced`, `CompleteAfter`, `FinalStateDurable`, `OwnershipLost`,
`OwnershipUnknown`, `ItemRemoved`, or `RecoveryBlocked`) from the durable
operation, the associated `PublishedArtworkState`, a fresh active-image
observation, and item absence, and it performs no I/O or image mutation.
`ArtworkReconciler` is the registered invocable boundary: it serializes with
normal publication through the shared `ArtworkSubjectGate`, reads the
authoritative state and durable operation (quarantining an invalid record),
re-observes the item and active image, distinguishes a confirmed missing item
from an unobservable image, and delegates execution to the publisher's
deterministic protocol. `ArtworkPublisher` now exposes an internal recoverable
execution path that the normal publication route and reconciliation share, so
the supported `SaveImage`, the durable phase ordering, the readback, and the
final-state commit are never reimplemented. A before-identity match resumes or
retries the same deterministic operation only when the generation and lifecycle
fence permit it and never recaptures a source; the publisher reads the retained
derived artifact instead of the active image. An after-identity match ensures the
normal item update is persisted and commits the intended final state. An
observable mismatch records `OwnershipLost` and aborts; an unobservable image
records `OwnershipUnknown` and leaves the image untouched; a confirmed missing
item writes an `ItemRemoved` tombstone and performs no image mutation; a durable
final state completes the journal without a further mutation; and an invalid
operation, state, or required artifact enters `RecoveryBlocked` with no replay or
cleanup. Reconciliation performs no artifact deletion, so an artifact that is not
proven non-active is retained. The publisher records the logical publication
fingerprint and renderer version on a publication operation (`ArtworkOperation`
gained the optional `candidatePublicationFingerprint` and `rendererVersion`
fields, documented in data-model 3.10.3) so an after-match recovery can commit the
target state without re-rendering. `ArtworkReconciler` is registered with the
existing lazy DI pattern and adds no startup work; the event/queue/startup-scan
wiring belongs to Phase 6, and the guarded restoration mutation remains the
lifecycle task 5.9 (restoration resume is reported as `Deferred` and left in
place). New tests (`ArtworkReconcilerTests`, 24 cases) cover every branch of the
decision table, the lifecycle-fence abort, before-identity revalidation on
resume, source-artifact retention (no recapture), derived/source artifact
corruption entering `RecoveryBlocked`, the item-absent tombstone with no
mutation, the durable-final-state completion, resolution of a previously
`RecoveryBlocked` operation, cancellation, containment of a reader exception,
the DI registration, and boundary-neutrality. Default `./build.sh test` passes
896 with 49 guarded skips (945 total) and 0 warnings, exactly +24 over the task
5.5 baseline; no ADR, `RenderVersion`, renderer behavior, or existing passing
behavior was changed.

**Acceptance criteria:**

- [ ] A standard Jellyfin poster response contains the configured derived image
  when valid metadata exists.
- [ ] Disabling ArrTags restores the original source when the active image is
  still ArrTags-owned.
- [ ] Original media files remain byte-for-byte untouched.
- [ ] Failed publication, missing metadata, and failed rendering preserve the
  current usable artwork.
- [ ] Crashes before, during, and after `SaveImage` and item persistence recover
  to a committed publication, a safe abort, or an explicit recovery-blocked
  state without losing source provenance.
- [ ] Restart reconciliation never overwrites an externally changed image and
  never treats an uncertain operation as proof of ownership.
- [ ] Disable, uninstall, and confirmed item removal leave no untracked
  non-terminal operation or unsafe cleanup obligation.
- [ ] ArrTags does not interfere with Jellyfin Enhanced, including configured
  duplicate handling and Spoiler Guard expectations.

**Gate 5:** Publication and standard image-route integration tests pass for the
selected Jellyfin 12 ABI, and source preservation/restoration is demonstrated.

### 6. Caching, updates & performance

**Objective:** Make metadata refresh and image generation asynchronous, bounded,
restart-safe, and efficient without allowing stale or partial work to become
authoritative.

**Deliverables:**

- Versioned metadata cache for last-known-good canonical snapshots and bounded
  artwork publication/provenance state, with optional bounded render work cache.
- Durable per-item/image-surface artwork-operation journal and staged-artifact
  manifests for publication and restoration recovery.
- Bounded, deduplicating, cancellation-aware work queue and hosted workers.
- Reconciliation from Jellyfin item events, post-scan work, scheduled/manual
  work, and authenticated Arr webhook hints.
- Fingerprint-driven invalidation for match, metadata, image source,
  configuration, renderer, and schema changes.
- Concrete defaults for queue, concurrency, retries, timeouts, image limits,
  cache TTL/size, and stale-data window.
- Safe metrics or status for queue depth, provider health, matching, cache,
  rendering, and stale data without secrets or unbounded payloads.

**Tasks:**

- [ ] 6.1 Keep library event handlers short: validate relevance, enqueue a
  bounded hint, and return without external I/O or rendering.
- [ ] 6.2 Implement coalescing by item and connection, single-flight work, worker
  cancellation, retry classification, and queue overflow behavior.
- [ ] 6.3 Publish metadata state atomically only after current item/configuration
  validation; discard stale long-running work.
- [ ] 6.4 Recover non-terminal artwork operations before accepting new work for
  the same item/image surface, using before/after identity postconditions and
  generation fences.
- [ ] 6.5 Separate metadata freshness and bounded stale-last-known-good behavior
  from artwork retention and eviction.
- [ ] 6.6 Regenerate and republish only affected artwork when a metadata
  fingerprint changes; invalidate relevant publication/work state on schema,
  renderer, or configuration changes.
- [ ] 6.7 Validate and bound webhook authentication, content, rate, and work
  scope; treat webhooks as hints rather than source of truth.
- [ ] 6.8 Test restart, shutdown, corruption, outage, recovery, duplicate events,
  queue pressure, and cancellation behavior.

**Authoritative Phase 6 execution order:** 6.1, 6.2, 6.3, 6.4, 6.5, 6.6, 6.7,
6.8. Task IDs are stable references only; this execution order is the canonical
sequence. The order is derived from the documented dependencies, not from task
numbering:

- 6.1 has no prerequisites: the event handler and enqueue boundary is the entry
  point for all later queue work.
- 6.2 depends on 6.1 and provides the queue, coalescing, single-flight, and
  cancellation behavior.
- 6.3 depends on 6.2 because atomic metadata-state publication and stale-work
  disposal operate on queued work.
- 6.4 depends on 6.2 and 6.3 and reuses the queue's generation fences before
  accepting new work for an item/image surface.
- 6.5 depends on 6.3 because freshness/staleness policy is applied when
  metadata state is published.
- 6.6 depends on 6.3 and 6.4 because fingerprint-driven invalidation must run
  through the publication and recovery boundaries.
- 6.7 depends on 6.1 because bounded webhook hints enter through the same
  handler/enqueue boundary.
- 6.8 depends on 6.1 through 6.7 because it tests the restart, outage, recovery,
  duplicate-event, pressure, and cancellation behavior of the complete phase.

**Acceptance criteria:**

- [ ] Unchanged metadata and source state do not repeatedly fetch, render, or
  publish work unnecessarily.
- [ ] A changed Arr file, source image, badge configuration, or renderer version
  produces the required new fingerprint and render result.
- [ ] Temporary provider outages use bounded last-known-good state only within
  policy, then leave the current usable artwork unchanged.
- [ ] Queue overflow and slow providers never block Jellyfin library event
  delivery indefinitely.
- [ ] Restart or cancellation cannot publish partial state or partial image
  output.
- [ ] Plugin cache and provenance entries are bounded where applicable,
  versioned, recoverable, and free of credentials.

**Gate 6:** Load, outage, restart, invalidation, and recovery tests meet the
recorded operational limits without degrading Jellyfin operations.

### 7. Testing & release

**Objective:** Validate the complete V1 behavior against the selected Jellyfin
12 ABI and supported Sonarr/Radarr versions, then produce a reproducible
release artifact and operational documentation.

**Deliverables:**

- Unit test coverage for configuration, DTO mapping, matching, metadata
  semantics, fingerprints, queue behavior, cache recovery, and rendering.
- Integration test coverage for plugin discovery, DI, lifecycle, image routes,
  Arr failures, webhooks, conditional responses, and Enhanced coexistence.
- End-to-end acceptance checks for movie and television poster flows.
- Reproducible build, package, versioning, and release checklist.
- User/admin documentation for configuration, supported behavior, failure modes,
  badge policy, and Jellyfin Enhanced coexistence.

**Tasks:**

- [ ] 7.1 Run all unit and integration tests against the exact declared versions.
- [ ] 7.2 Verify the plugin installs, upgrades, reloads, and uninstalls safely.
- [ ] 7.3 Verify all success criteria in `GOALS.md`, including independent
  provider configuration, matching, quality retrieval, poster output, update
  behavior, Enhanced compatibility, graceful failure, and reproducible builds.
- [ ] 7.4 Review logs, diagnostics, HTTP behavior, and persisted state for secret
  leakage or unbounded data.
- [ ] 7.5 Build the release package from a clean checkout and record the
  commands, inputs, artifact identity, and supported version ranges.
- [ ] 7.6 Document known limitations and any deferred decision without presenting
  unsupported behavior as available.

**Authoritative Phase 7 execution order:** 7.1, 7.2, 7.3, 7.4, 7.5, 7.6. Task IDs
are stable references only; this execution order is the canonical sequence. The
order is derived from the documented dependencies, not from task numbering:

- 7.1 has no prerequisites and runs the full suite against the exact declared
  versions.
- 7.2 depends on 7.1 and verifies install, upgrade, reload, and uninstall safety.
- 7.3 depends on 7.1 and 7.2 because the `GOALS.md` success criteria span
  provider, matching, rendering, artwork, update, and lifecycle behavior.
- 7.4 depends on 7.1 and reviews logs, diagnostics, HTTP behavior, and persisted
  state produced by the test and install runs.
- 7.5 depends on 7.1 through 7.4 because the release package may be built only
  after the tests and checks pass.
- 7.6 depends on 7.1, 7.3, and 7.5 so the documented limitations and deferred
  decisions match the verified behavior and the release artifact.

**Acceptance criteria:**

- [ ] The full test suite passes from a clean checkout.
- [ ] The packaged plugin loads and operates on the declared Jellyfin 12 ABI.
- [ ] Sonarr and Radarr movie/television scenarios pass with unchanged and
  changed metadata.
- [ ] Provider, matching, rendering, artwork, cache, and lifecycle failures do
  not adversely affect Jellyfin.
- [ ] The release artifact and build process are reproducible and documented.

**Gate 7:** All V1 success criteria are checked, release blockers are resolved,
and the artifact is approved for release.

## Decision Gates

These decisions must be resolved and recorded before the dependent work becomes
an implementation assumption.

| Gate | Decision | Required before |
| --- | --- | --- |
| DG-1 | Exact Jellyfin 12 patch, package versions, target framework, and manifest ABI. | Milestone 1 implementation |
| DG-2 | Initial supported item and image types, including whether series/season posters are disabled or use an explicit aggregate policy. Resolved by ADR-006: V1 badge surfaces are Movie and Episode posters; Series/Season are structural and aggregation remains post-V1. | Milestones 3-5 |
| DG-3 | Initial badge fields, templates, placement, contrast, output format, text limits, and request-size policy. Resolved by ADR-009: V1 uses provider-neutral bounded badges on unindexed Movie and Episode Primary posters, lossless PNG at source dimensions, and fail-closed pass-through for unknown or failed input. | Milestone 4 |
| DG-4 | Episode numbering rules, including specials, anime, absolute numbering, double episodes, and multi-episode files. Resolved by ADR-007: number fallback is limited to regular single episodes; season zero specials, multi-episode spans, and absolute/scene numbering are excluded. | Milestone 3 |
| DG-5 | Whether path mappings are needed, and their connection-scoped representation. Resolved by ADR-008: configured path fallback is deferred out of V1, so V1 has no path mapping schema, normalization, or `ConfiguredPath` rule. | Milestone 3 |
| DG-6 | Queue, timeout, retry, concurrency, image-size, cache, and stale-state defaults. Foundation defaults are resolved by ADR-004; Milestone 6 may tune within the documented validation ranges. | Milestone 6 |
| DG-7 | Webhook exposure, authentication, payload limits, replay handling, and route administration flow. Secret persistence and versioned access are resolved by ADR-005. | Milestone 6 |
| DG-8 | Jellyfin Enhanced duplicate-badge defaults and Spoiler Guard behavior. | Milestone 5 |
| DG-9 | Supported live Sonarr/Radarr release ranges and optional-field compatibility policy. | Milestones 2 and 7 |

## Risks and Mitigations

| Risk | Impact | Mitigation / trigger |
| --- | --- | --- |
| Jellyfin 12 artwork ABI differs from assumptions. | Publication or restoration fails, or standard image delivery is disrupted. | Complete the supported item-image publication spike early and pin the ABI; leave current artwork unchanged on failure. |
| Provider responses vary by version or omit technical fields. | Incorrect or unstable badges. | Defensive mapping, explicit unknown states, capability tracking, and contract tests across declared versions. |
| Jellyfin and Arr identities cannot be proven equivalent. | Badges appear on the wrong item. | Provider IDs first, scoped IDs, no V1 path fallback, and no automatic badge for ambiguity. |
| Provider outages or slow requests affect Jellyfin. | Library scans or image requests degrade. | Asynchronous bounded work, finite timeouts, cancellation, stale policy, and current-artwork preservation. |
| Rendered output becomes stale after metadata or artwork changes. | Users see outdated badges. | Include all output-affecting inputs in fingerprints and invalidate only after atomic state publication. |
| Generated artwork is published incorrectly. | Original artwork is lost or Enhanced behavior is disrupted. | Require source provenance, guarded restoration, supported item-image APIs, and tests for manual image changes. |
| Cache/state corruption survives restart. | Repeated failures or unavailable badges. | Versioned records, atomic writes, integrity checks, quarantine/discard, and rebuild tests. |
| Webhook payloads trigger unbounded or unauthorized work. | Security or resource exhaustion. | Shared-secret authentication, bounded payloads, rate/coalescing limits, and re-read current provider state. |
| Enhanced and ArrTags show overlapping information. | Confusing or duplicated client presentation. | Explicit coexistence configuration, no Enhanced internals, and tests with quality tags and spoiler behavior enabled/disabled. |

## Post-V1 Backlog

These items are deferred until the V1 gates are complete. They remain within the
existing goals or are explicitly identified as future schema work; they do not
expand V1 scope by themselves.

- Add additional normalized badge metadata already identified in the data model,
  such as bit depth, frame rate, scan type, language, subtitles, release group,
  edition, custom-format score, certification, stream count, or provider
  extensions, when reliable source semantics and configuration are defined.
- Add further badge definitions and visual primitives without coupling them to
  provider DTOs or changing the render pipeline contract.
- Add additional supported item or image surfaces only after defining an
  explicit aggregation and eligibility policy; do not infer aggregate quality
  from one child file.
- Expand the supported Sonarr/Radarr version range after compatibility tests and
  capability rules are available.
- Consider support for another metadata service only if a later scope decision
  requires it and the provider-neutral integration boundary remains valid.
- Reconsider configured, connection-scoped Jellyfin-to-Arr path fallback only
  through a new architecture decision defining its namespaces, normalization,
  ambiguity, and location-safety contract.

The following remain excluded from this plan unless `GOALS.md` is deliberately
changed: Jellyfin versions before 12, modifying original media files, writing or
managing Sonarr/Radarr metadata, a general poster-management system,
user-specific badges, non-poster artwork, and unapproved external services.
