# ArrTags Execution Plan

## Project Status

**Status:** Phase 1 in progress

**Current position:** The goals, V1 architecture, and canonical data model are
drafted and the architectural blockers are resolved. Tasks 1.1 (documentation
alignment), 1.2 (project and test scaffold), 1.3 (canonical Sonarr identity
model), 1.4 (operational defaults and limits), and 1.5 (plugin entry point and
configuration) are complete: the architecture is the single V1 architecture
reference, the research documents are marked as evidence, the canonical model
represents Sonarr series, episode, and current episode-file identity with a
typed, connection-scoped identity, the accepted operational defaults and
validation rules are recorded in ADR-004 and `docs/architecture.md` section 12,
the configuration foundation validates connections, limits, and scope and exposes
an immutable replacement snapshot with last-valid retention and secret redaction,
and the plugin builds on `net10.0` against the pinned Jellyfin `12.0.0` packages
with `targetAbi: 12.0.0.0`. Foundation tests pass without a live Arr instance and
discovery/load was validated against a Jellyfin `12.0.0.0` host. Milestone 1 is
in progress and is the current execution target.

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
| 1 | Plugin foundation | In progress | Plugin loads on the selected Jellyfin 12 ABI with valid configuration and lifecycle behavior. |
| 2 | Sonarr & Radarr integration | Not started | Both providers can be configured independently, probed, queried read-only, and mapped into canonical observations. |
| 3 | Media matching | Not started | Eligible movies, series, and episodes match only with validated identity evidence. |
| 4 | Badge rendering | Not started | Canonical metadata renders deterministically within configured limits, with safe pass-through on failure. |
| 5 | Jellyfin artwork integration | Not started | Derived poster artwork is published through Jellyfin's supported image APIs without modifying media files or bypassing normal image delivery. |
| 6 | Caching, updates & performance | Not started | Reconciliation, invalidation, persistence, and bounded work avoid unnecessary requests and processing. |
| 7 | Testing & release | Not started | Required unit/integration/acceptance checks pass and the plugin can be built and packaged reproducibly. |

## Milestones

### 1. Plugin foundation

**Objective:** Establish a minimal, loadable Jellyfin 12 plugin foundation: the
project, configuration, dependency-injection, lifecycle, and versioned state
boundaries required by the remaining milestones.

**Phase 1 concept:** This milestone is executed as the ordered Phase 1 tasks
below. The four remaining tasks recorded in
`docs/implementation-readiness.md` ("Phase 1 Implementation Tasks") map to
tasks 1.1, 1.3, 1.4, and 1.8. Architecture and data-model detail stays
authoritative in `docs/architecture.md` and `docs/data-model.md` and is
referenced here rather than duplicated.

| Readiness task | Phase 1 task |
| --- | --- |
| Remove/demote duplicate architecture section; align planner/research wording | 1.1 |
| Extend canonical match model for Sonarr series, episode, and episode-file identity | 1.3 |
| Establish validated operational defaults and limits | 1.4 |
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

**Status:** Complete (implementation). Intended-host acceptance remains part of
task 1.8.

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

**Status:** Complete (specification). Runtime enforcement, boundary tests, and
representative-load validation land with the configuration and state foundation
tasks (1.5 and 1.7) and the performance milestone.

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
non-eviction; stale-window behavior. Runtime tests land with tasks 1.5 and 1.7
and the performance milestone.

**Acceptance criteria:** Every required limit has an explicit value, unit,
validation rule, and safe failure behavior; tests show limits prevent unbounded
work; authoritative state cannot be evicted as ordinary cache data.

**Definition of done:** Defaults are recorded with explicit validation rules and
safe failure behavior and approved for the initial foundation; runtime testing
under representative load is part of tasks 1.5 and 1.7 and the performance
milestone.

#### 1.5 Plugin entry point and configuration

**Status:** Complete. The parameterless `BasePlugin<PluginConfiguration>` entry
point remains Jellyfin-owned and unchanged; the configuration model, validator,
and immutable replacement-snapshot service are implemented and covered by
foundation tests. Dependency-injection wiring remains task 1.6.

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

**Objective:** Register foundation services without starting provider or
rendering work during registration or startup.

**Dependencies:** 1.5.

**Affected components:** `IPluginServiceRegistrator`, hosted worker lifecycle,
scheduled-task registration, initial controller/service registrations.

**Work:**

- Register configuration, domain services, state services, queue abstractions,
  hosted work, and future provider boundaries.
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

**Objective:** Prove the actual plugin works against the pinned Jellyfin
compatibility set.

**Dependencies:** 1.2, 1.5, 1.6, 1.7.

**Affected components:** build/package output, plugin manifest, intended
Jellyfin `12.0.0` host, validation checklist.

**Required host validation:**

- Build with .NET SDK `10.0.0`.
- Install the generated package on the intended Jellyfin `12.0.0` host.
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
on the intended host.

**Definition of done:** Gate 1 is met and the milestone can be marked complete.

**Phase 1 acceptance criteria:**

- [ ] The plugin builds, installs, and loads correctly on Jellyfin `12.0.0`
  with target framework `net10.0` and manifest `targetAbi: 12.0.0.0`.
- [ ] Sonarr and Radarr can be enabled, disabled, and configured independently.
- [ ] Invalid configuration is rejected or retained as the last valid snapshot
  without taking down Jellyfin.
- [ ] Credentials never appear in persisted canonical data, cache identity,
  logs, or error messages.
- [ ] Startup and shutdown leave no unmanaged background work.
- [ ] A restart with missing, corrupt, or incompatible non-authoritative cache
  state rebuilds it without blocking Jellyfin; invalid artwork-operation state
  is quarantined and preserves the current image without blind replay.
- [ ] The canonical model represents Sonarr series, episode, and episode-file
  identity explicitly and excludes provider DTOs.
- [ ] All operational defaults have explicit values, validation rules, and safe
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

- [ ] Define the shared provider-client boundary and connection identity rules.
- [ ] Register dedicated named or typed clients through Jellyfin's standard
  HTTP client factory; do not create raw clients per request.
- [ ] Implement Radarr v3 reads for local movies and current movie-file data,
  including the dedicated file request when enabled fields are not embedded.
- [ ] Implement Sonarr v3 reads for series, episodes, and episode files,
  joining files by the validated episode file identifier.
- [ ] Map provider data into `ArrProvider`, `ArrConnection`, and
  `BadgeMetadata` without leaking provider DTOs past the boundary.
- [ ] Preserve unknown technical values as unknown rather than false or empty
  claims, and bound custom values before they can reach a badge.
- [ ] Add tests for authentication failures, unavailable services, malformed
  responses, optional fields, version drift, cancellation, and retries.

**Acceptance criteria:**

- [ ] Each provider can be probed and queried independently.
- [ ] No integration path calls an Arr write endpoint, database, or lookup
  endpoint for routine refreshes.
- [ ] Actual file quality is available where the provider reports it; a quality
  profile is never presented as actual file quality.
- [ ] Missing, incomplete, invalid, and unsupported provider data produces a
  bounded domain outcome and does not fail a Jellyfin request.
- [ ] API keys, webhook secrets, and sensitive request details are redacted from
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
- Optional path fallback only through configured, normalized path mappings.

**Tasks:**

- [ ] Build Jellyfin `MediaIdentity` snapshots from supported item types and
  library scope.
- [ ] Implement candidate selection, evidence recording, and deterministic
  `MediaMatch` fingerprints.
- [ ] Reject zero-candidate and multiple-candidate matches rather than guessing
  from title or year.
- [ ] Implement the documented movie, series, and episode matching order.
- [ ] Define and test the numbering policy for specials, anime, and multi-episode
  records before enabling number fallback.
- [ ] Add configured path normalization only if the configuration decision gate
  approves it; never assume host and container paths are equivalent.
- [ ] Verify that local Arr record and file IDs are always scoped by connection.

**Acceptance criteria:**

- [ ] A supported Jellyfin movie can match its Radarr movie with validated
  provider identity.
- [ ] A supported series and episode can match their Sonarr records using the
  approved identity and numbering policy.
- [ ] Ambiguous, missing, virtual, remote, and unsupported cases produce no new
  badge and a safe diagnostic status.
- [ ] Titles and years are never sole proof of an automatic match.
- [ ] Changing match evidence changes the match fingerprint and invalidates the
  dependent metadata state.

**Gate 3:** Representative movie, series, episode, mismatch, ambiguity, and
numbering cases pass without guessed matches.

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

**Tasks:**

- [ ] Implement metadata selectors against `BadgeMetadata`, not provider DTO
  paths.
- [ ] Define the initial badge field set, text rules, contrast behavior,
  placement, scale, margins, and output format policy.
- [ ] Keep unknown technical values distinct from confirmed negative values.
- [ ] Implement render request and result fingerprints containing every
  output-affecting value, including renderer and badge schema versions.
- [ ] Enforce image and text limits before decode, draw, and encode work.
- [ ] Test dimensions, format behavior, truncation, layout, cancellation, and
  renderer failure pass-through.

**Acceptance criteria:**

- [ ] The same source image, canonical metadata, configuration, request
  parameters, and renderer version produce the same logical output.
- [ ] The renderer never claims a value based only on missing provider data.
- [ ] Quality badges show actual observed quality, not requested quality policy.
- [ ] Original source bytes are not modified by rendering.
- [ ] Every rendering failure leaves the current usable artwork unchanged and
  records a bounded, non-secret diagnostic.

**Gate 4:** Renderer unit tests pass for normal, unknown, oversized, malformed,
cancelled, and failed inputs.

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

- [ ] Confirm the exact supported Jellyfin item-image publication ABI and route
  variants.
- [ ] Implement source-artwork provenance and guarded restoration state before
  publishing derived artwork.
- [ ] Publish completed artwork through Jellyfin's supported item-image APIs;
  do not write media-folder posters or Jellyfin's image cache directly.
- [ ] Persist a durable `ArtworkOperation` before `SaveImage`, including before
  and candidate-after identities, artifact references, generation, and
  ownership/publication tokens.
- [ ] Reconcile uncertain `SaveImage`, item update, and provenance persistence
  outcomes by postcondition; never blindly replay or delete an active artifact.
- [ ] Preserve the current usable artwork when source capture or rendering
  cannot safely complete.
- [ ] Fence and drain publication operations during disable/uninstall, and
  tombstone confirmed item removal without issuing image mutations.
- [ ] Add the configured disable/limit policy for duplicate or overlapping
  badges; do not depend on Jellyfin Enhanced internals.
- [ ] Test Web and image-consuming clients through the supported server image
  response path.

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

- [ ] Keep library event handlers short: validate relevance, enqueue a bounded
  hint, and return without external I/O or rendering.
- [ ] Implement coalescing by item and connection, single-flight work, worker
  cancellation, retry classification, and queue overflow behavior.
- [ ] Publish metadata state atomically only after current item/configuration
  validation; discard stale long-running work.
- [ ] Recover non-terminal artwork operations before accepting new work for the
  same item/image surface, using before/after identity postconditions and
  generation fences.
- [ ] Separate metadata freshness and bounded stale-last-known-good behavior from
  artwork retention and eviction.
- [ ] Regenerate and republish only affected artwork when a metadata fingerprint
  changes; invalidate relevant publication/work state on schema, renderer, or
  configuration changes.
- [ ] Validate and bound webhook authentication, content, rate, and work scope;
  treat webhooks as hints rather than source of truth.
- [ ] Test restart, shutdown, corruption, outage, recovery, duplicate events,
  queue pressure, and cancellation behavior.

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

- [ ] Run all unit and integration tests against the exact declared versions.
- [ ] Verify the plugin installs, upgrades, reloads, and uninstalls safely.
- [ ] Verify all success criteria in `GOALS.md`, including independent provider
  configuration, matching, quality retrieval, poster output, update behavior,
  Enhanced compatibility, graceful failure, and reproducible builds.
- [ ] Review logs, diagnostics, HTTP behavior, and persisted state for secret
  leakage or unbounded data.
- [ ] Build the release package from a clean checkout and record the commands,
  inputs, artifact identity, and supported version ranges.
- [ ] Document known limitations and any deferred decision without presenting
  unsupported behavior as available.

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
| DG-2 | Initial supported item and image types, including whether series/season posters are disabled or use an explicit aggregate policy. | Milestones 3-5 |
| DG-3 | Initial badge fields, templates, placement, contrast, output format, text limits, and request-size policy. | Milestone 4 |
| DG-4 | Episode numbering rules, including specials, anime, absolute numbering, double episodes, and multi-episode files. | Milestone 3 |
| DG-5 | Whether path mappings are needed, and their connection-scoped representation. | Milestone 3 |
| DG-6 | Queue, timeout, retry, concurrency, image-size, cache, and stale-state defaults. Foundation defaults are resolved by ADR-004; Milestone 6 may tune within the documented validation ranges. | Milestone 6 |
| DG-7 | Webhook exposure, authentication, payload limits, and secret administration flow. | Milestone 6 |
| DG-8 | Jellyfin Enhanced duplicate-badge defaults and Spoiler Guard behavior. | Milestone 5 |
| DG-9 | Supported live Sonarr/Radarr release ranges and optional-field compatibility policy. | Milestones 2 and 7 |

## Risks and Mitigations

| Risk | Impact | Mitigation / trigger |
| --- | --- | --- |
| Jellyfin 12 artwork ABI differs from assumptions. | Publication or restoration fails, or standard image delivery is disrupted. | Complete the supported item-image publication spike early and pin the ABI; leave current artwork unchanged on failure. |
| Provider responses vary by version or omit technical fields. | Incorrect or unstable badges. | Defensive mapping, explicit unknown states, capability tracking, and contract tests across declared versions. |
| Jellyfin and Arr identities cannot be proven equivalent. | Badges appear on the wrong item. | Provider IDs first, scoped IDs, explicit path mappings only, and no automatic badge for ambiguity. |
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

The following remain excluded from this plan unless `GOALS.md` is deliberately
changed: Jellyfin versions before 12, modifying original media files, writing or
managing Sonarr/Radarr metadata, a general poster-management system,
user-specific badges, non-poster artwork, and unapproved external services.
