# ArrTags Execution Plan

## Project Status

**Status:** Planning / pre-implementation

**Current position:** The goals, V1 architecture, and canonical data model are
drafted. No implementation milestone is complete. Milestone 1 is the next
execution target.

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
| 1 | Plugin foundation | Not started | Plugin loads on the selected Jellyfin 12 ABI with valid configuration and lifecycle behavior. |
| 2 | Sonarr & Radarr integration | Not started | Both providers can be configured independently, probed, queried read-only, and mapped into canonical observations. |
| 3 | Media matching | Not started | Eligible movies, series, and episodes match only with validated identity evidence. |
| 4 | Badge rendering | Not started | Canonical metadata renders deterministically within configured limits, with safe pass-through on failure. |
| 5 | Jellyfin artwork integration | Not started | Derived poster artwork is published through Jellyfin's supported image APIs without modifying media files or bypassing normal image delivery. |
| 6 | Caching, updates & performance | Not started | Reconciliation, invalidation, persistence, and bounded work avoid unnecessary requests and processing. |
| 7 | Testing & release | Not started | Required unit/integration/acceptance checks pass and the plugin can be built and packaged reproducibly. |

## Milestones

### 1. Plugin foundation

**Objective:** Establish a minimal, loadable Jellyfin 12 plugin and the
configuration, dependency-injection, lifecycle, and state boundaries required by
the remaining milestones.

**Deliverables:**

- Jellyfin 12-compatible project, package versions, target framework, plugin
  manifest, and `targetAbi` recorded and reproducible.
- Thin plugin entry point with plugin identity, configuration ownership, and
  data-folder ownership.
- Parameterless service registrator with the initial service boundaries from the
  architecture.
- Immutable configuration snapshot and validation path for independently
  enabled Sonarr and Radarr connections, badge behavior, image eligibility,
  update policy, and operational limits.
- Protected handling of API keys and webhook secrets. Secrets are absent from
  logs, diagnostics, cache keys, fingerprints, and domain snapshots.
- Startup and shutdown behavior that does not perform an unbounded provider
  refresh or full-library render synchronously.
- Versioned plugin state boundary under `DataFolderPath`, including atomic
  writes and safe handling of corrupt or incompatible state.

**Tasks:**

- [ ] Confirm the exact Jellyfin 12 patch, .NET target, package versions, and
  plugin manifest ABI against the intended host.
- [ ] Create the plugin entry point and configuration persistence using Jellyfin
  plugin conventions.
- [ ] Register configuration, domain services, hosted work, scheduled work,
  controllers, and artwork publication services without starting provider work
  at registration time.
- [ ] Define configuration validation, replacement snapshots, defaults, and
  redacted administrative diagnostics.
- [ ] Define version values for model/cache data, badge schema, renderer, and
  effective configuration.
- [ ] Implement atomic plugin-owned state read/write boundaries; quarantine
  authoritative artwork-operation records and discard only invalid
  non-authoritative cache entries.
- [ ] Add lifecycle tests for discovery, DI registration, startup, shutdown,
  reload, and cancellation.

**Acceptance criteria:**

- [ ] The plugin installs and loads correctly on the selected Jellyfin 12 ABI.
- [ ] Sonarr and Radarr can be enabled, disabled, and configured independently.
- [ ] Invalid configuration is rejected or retained as the last valid snapshot
  without taking down Jellyfin.
- [ ] Credentials never appear in persisted canonical data, cache identity,
  logs, or error messages.
- [ ] Startup and shutdown leave no unmanaged background work.
- [ ] A restart with missing, corrupt, or incompatible non-authoritative cache
  state rebuilds it without blocking Jellyfin; invalid artwork-operation state
  is quarantined and preserves the current image without blind replay.

**Gate 1:** The ABI and configuration/lifecycle tests pass, and the plugin can
start with both providers disabled.

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
| DG-6 | Queue, timeout, retry, concurrency, image-size, cache, and stale-state defaults. | Milestone 6 |
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
