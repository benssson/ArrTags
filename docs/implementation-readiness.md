# Implementation Readiness

## Status

**Status:** Ready for implementation

**Basis:** ADR-002 and ADR-003; findings in
`docs/reviews/pre-implementation-review-02.md` are resolved for the two
architectural blockers.

## Implementation Gate

No architectural blockers remain for the persisted-artwork publication path.
V1 implementation may begin after the listed pre-implementation actions and
exact Jellyfin ABI validation are completed.

## Blockers

- [x] Define `PublishedArtworkState` ownership evidence, retained-source
  artifacts, active-image identity, manual-change detection, and guarded
  restoration behavior. See `docs/decisions.md` ADR-002 and
  `docs/data-model.md` sections 3.10.1-3.10.2.
- [x] Define crash-consistent publication recovery across source capture,
  rendering, `SaveImage`, Jellyfin item update, provenance persistence, restart,
  disable, uninstall, and item removal. See `docs/decisions.md` ADR-003 and
  `docs/data-model.md` sections 3.10.3-3.10.4.

## Resolved Blocker 1

ArrTags now retains an immutable source artifact or an explicit absent baseline,
persists the expected active-image identity and plugin ownership/publication
tokens, and treats any changed or unverifiable image as externally changed for
restoration purposes. Repeated ArrTags publications retain the first source
artifact instead of treating an earlier derived image as a new original.

The exact source capture/readback mechanism and host-specific `SaveImage` storage
behavior remain implementation-time validation. They are not ownership
semantics and must not be used to reopen this resolved design blocker.

## Resolved Blocker 2

Publication and restoration now use a durable `ArtworkOperation` write-ahead
record with immutable staged artifacts, explicit before/after identities,
generation fencing, postcondition-based restart reconciliation, and
fail-closed recovery. Lifecycle fences drain disable/uninstall work, while
confirmed item removal creates a tombstone without issuing image mutations.

The protocol does not claim a distributed transaction with Jellyfin. It makes
uncertain operations recoverable or explicitly `RecoveryBlocked`, and retains
provenance and artifacts until cleanup is proven safe.

## Pre-Implementation Actions

- [ ] Remove or demote the duplicate architecture section in
  `docs/architecture.md` and align the planner/research phase descriptions.
- [ ] Extend the canonical match model to represent Sonarr series, episode, and
  episode-file identity explicitly.
- [ ] Decide V1 item/image scope, including series/season posters, indexed
  images, alternate versions, stacked parts, and aggregate quality behavior.
- [ ] Decide episode policies for specials, anime/absolute numbering,
  double-episodes, multi-episode files, remote items, and path fallback.
- [ ] Define connection-to-library routing and ambiguity behavior for multiple
  Sonarr/Radarr instances.
- [ ] Define the Sonarr catalogue cache, provider inventory strategy, supported
  provider-version matrix, and cross-provider normalization rules.
- [ ] Decide what happens to active derived artwork after metadata becomes stale:
  retain, restore, or publish an unbadged source image.
- [ ] Define webhook authentication, replay protection, rate limits, payload
  bounds, and provider-record-to-Jellyfin resolution, or defer webhooks.
- [ ] Record concrete queue, concurrency, retry, timeout, response-size,
  provenance-retention, storage, and stale-state limits.
- [ ] Define Jellyfin Enhanced duplicate-badge and Spoiler Guard behavior.

## Implementation-Time Questions

- Pin the exact Jellyfin 12 patch, target framework, packages, and `targetAbi`
  through the existing foundation gate.
- Validate `SaveImage` storage behavior and the selected source-artwork capture
  and restoration implementation on the target host configuration.
- Choose the renderer library, image format, fonts, and bounded artifact storage.
- Implement per-item generation tokens, publication serialization, and
  ArrTags-generated event-loop suppression.
- Finalize DTO nullability, optional-field compatibility, provider fixtures, and
  normalization tests.
- Split the broad milestones into bounded coding tasks and their lowest-cost
  unit/integration tests.

## Ready for Implementation

- [x] Persisted derived artwork is the accepted V1 delivery mechanism.
- [x] Native image response interception is rejected as the V1 mechanism.
- [x] Jellyfin standard image routes remain responsible for authorization,
  image tags, resizing, and response caching.
- [x] Original media files and Jellyfin's image-cache directory are not directly
  modified.
- [x] Sonarr and Radarr remain separate integration boundaries with a shared
  canonical metadata pipeline.
- [x] Actual file quality is separated from quality-profile policy.
- [x] Provider outages, unmatched items, render failures, and cancellation must
  leave current usable artwork unchanged.
- [x] Publication and restoration use durable operation intent, staged artifacts,
  postcondition recovery, lifecycle fences, and fail-closed ambiguity handling.
- [x] Jellyfin Enhanced internals are not a dependency.

## Post-V1 Backlog

- SignalR-based provider updates.
- Additional metadata providers.
- User-specific badges.
- Non-poster artwork and additional image surfaces.
- Series/season aggregate quality badges.
- Expanded provider-version support.
- Advanced anime, alternate-version, and multi-source aggregation.
- Deeper Jellyfin Enhanced integration.
- Extended technical metadata and additional visual primitives.
