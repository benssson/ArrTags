# Implementation Readiness

## Status

**Status:** Phase 1 complete; Milestone 2 complete (Gate 2 met); Phase 3 next

**Basis:** ADR-002, ADR-003, ADR-004, and ADR-005; findings in
`docs/reviews/pre-implementation-review-02.md` are resolved for the two
artwork blockers, and the provider credential-access question is resolved for
Milestone 2 implementation.

## Implementation Gate

No architectural blockers remain for the persisted-artwork publication path or
for provider credential access. The compatibility target is pinned and no
separate pre-implementation prerequisite remains. Phase 1 validated the pinned
target by building and loading the actual plugin on the pinned Jellyfin
`12.0.0` host. ADR-005 resolves the secret persistence and access contract;
implementing that boundary is a prerequisite to authenticated provider reads,
but task 2.3 does not need another architecture decision. The remaining
decisions below are gated by the milestones that need them.

## Blockers

- [x] Define `PublishedArtworkState` ownership evidence, retained-source
  artifacts, active-image identity, manual-change detection, and guarded
  restoration behavior. See `docs/decisions.md` ADR-002 and
  `docs/data-model.md` sections 3.10.1-3.10.2.
- [x] Define crash-consistent publication recovery across source capture,
  rendering, `SaveImage`, Jellyfin item update, provenance persistence, restart,
  disable, uninstall, and item removal. See `docs/decisions.md` ADR-003 and
  `docs/data-model.md` sections 3.10.3-3.10.4.
- [x] Define where API keys and the webhook shared secret are persisted, how
  safe secret references resolve to short-lived credentials, and how immutable
  configuration replacement and rotation fence workers. See
  `docs/decisions.md` ADR-005 and `docs/data-model.md` sections 3.3 and 3.12.

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

## Resolved Credential Boundary

API keys and the webhook shared secret remain persisted in Jellyfin's
`PluginConfiguration`; ArrTags does not introduce a second secret store. A
configuration-owned singleton publishes a private immutable secret snapshot
atomically with the public secret-free snapshot and exposes only a typed,
version-matched `SecretLease` through `IPluginSecretResolver`. Provider clients
use an API-key lease only for the `X-Api-Key` request header. Queue items,
canonical models, cache/state records, diagnostics, and fingerprints contain
only safe references and configuration versions.

Invalid configuration leaves both active snapshots unchanged. Rotation keeps
the safe reference and connection identity stable, increments the configuration
version, gives new work the new lease, and allows only already-acquired leases
to finish bounded in-flight requests. Restart rebuilds the private snapshot
from Jellyfin's persisted configuration. The same private boundary can support
the webhook secret without deciding webhook route exposure or replay policy.

## Compatibility Target

| Value | Pin | Evidence |
| --- | --- | --- |
| Jellyfin server | `12.0.0` | The repository's `v12.0` source tag is commit `6c073e19ddf604b2369c638716164fdab4c952dc`; its `SharedVersion.cs` reports assembly and file version `12.0.0`. |
| Target framework | `net10.0` | Jellyfin 12 server and published host API packages target `net10.0`. |
| .NET SDK baseline | `10.0.0` | The Jellyfin `v12.0` `global.json` pins SDK `10.0.0` and permits latest-minor roll-forward. |
| Jellyfin host packages | `12.0.0` for `Jellyfin.Controller`, `Jellyfin.Model`, `Jellyfin.Common`, `Jellyfin.Data`, `Jellyfin.Extensions`, `Jellyfin.Naming`, and `Jellyfin.MediaEncoding.Keyframes` | Stable `12.0.0` packages are published and the `Jellyfin.Controller` dependency closure is on the same 12.0.0 line. Pin any referenced package in this set exactly; do not allow a 12.1.x transitive upgrade. |
| Plugin manifest `targetAbi` | `12.0.0.0` | Confirmed by the [official Jellyfin unstable plugin manifest](https://repo.jellyfin.org/files/plugin-unstable/manifest.json) entries for Jellyfin 12 plugins. |

`12.0.0` is selected because the repository architecture and research are
pinned to Jellyfin's `v12.0` source line. Jellyfin `12.1.0` is a separate source
and package line and is not silently adopted by this compatibility gate.

The repository `global.json` mirrors the Jellyfin `v12.0` SDK pin. With
`latestMinor` roll-forward the effective SDK is the latest installed `10.0.x`;
the foundation build was validated with SDK `10.0.401`. The plugin project pins
`Jellyfin.Controller` and `Jellyfin.Model` to `12.0.0` with
`ExcludeAssets="runtime"`; the resolved dependency closure (including
`Jellyfin.Common`, `Jellyfin.Data`, `Jellyfin.Database.Implementations`,
`Jellyfin.Extensions`, `Jellyfin.Naming`, and
`Jellyfin.MediaEncoding.Keyframes`) is asserted against `12.0.0` by a foundation
test and recorded in `src/ArrTags/packages.lock.json`.

## Pre-Implementation Actions

No separate pre-implementation prerequisite remains. The compatibility target
is pinned above; build, plugin discovery, and live-load validation were
completed as Phase 1 implementation and acceptance work.

## Former Action Disposition

| # | Former action | Classification | Disposition |
| --- | --- | --- | --- |
| 1 | Remove or demote the duplicate architecture section and align phase descriptions. | Should become a Phase 1 implementation task | Complete the documentation cleanup with the foundation work; it is not a runtime-safety gate. |
| 2 | Extend the canonical match model for Sonarr series, episode, and episode-file identity. | Should become a Phase 1 implementation task | Establish the explicit model and interfaces before provider and matching code consumes them. |
| 3 | Decide V1 item/image scope and aggregate quality behavior. | Resolved for V1 | Resolved by ADR-006: V1 badge surfaces are Movie and Episode posters; Series/Season are structural only and aggregate quality remains in the Post-V1 backlog. Remaining image-surface details (indexed images, alternate versions, stacked parts) stay implementation-time questions. |
| 4 | Decide episode policies for specials, anime/absolute numbering, double episodes, multi-episode files, remote items, and path fallback. | Implementation-time decision | Decide and test before enabling the corresponding matching paths. |
| 5 | Define connection-to-library routing and ambiguity behavior for multiple Arr instances. | Implementation-time decision | Finalize before multi-connection matching and reconciliation work. |
| 6 | Define catalogue caching, provider inventory, provider-version support, and cross-provider normalization. | Implementation-time decision | Define during provider integration and cover the result with bounded inventory and contract tests. |
| 7 | Decide the policy for active derived artwork after metadata becomes stale. | Implementation-time decision | Finalize before stale-state artwork handling and publication invalidation are implemented. |
| 8 | Define webhook security, bounds, replay handling, and provider-record resolution, or defer webhooks. | Implementation-time decision | Decide before webhook work; deferral remains an available V1 scope decision. |
| 9 | Record concrete queue, concurrency, retry, timeout, response-size, retention, storage, and stale-state limits. | Should become a Phase 1 implementation task | Complete for the foundation defaults; recorded in ADR-004 and `docs/architecture.md` section 12, with runtime enforcement and tuning in later milestones. |
| 10 | Define Jellyfin Enhanced duplicate-badge and Spoiler Guard behavior. | Implementation-time decision | Finalize and test before the Jellyfin artwork integration milestone. |
| 11 | Define secret persistence, safe references, credential access, rotation, and webhook-secret reuse. | Should become a Milestone 2 implementation task | Resolved by ADR-005 and implemented in task 2.3; the credential-boundary tests pass before authenticated provider reads. |

## Already Satisfied

- [x] The Jellyfin `12.0.0` / `net10.0` compatibility target, host package
  versions, and plugin `targetAbi` are pinned and documented above.
- [x] API-key and webhook-secret persistence and versioned access are defined
  without exposing values to canonical state or workers' mutable configuration.
- [x] V1 badge surfaces are Movie and Episode posters, and library scope uses
  Jellyfin collection-folder/library identifiers (ADR-006).

The former pre-implementation actions that became Phase 1 implementation tasks
(1, 2, and 9) are complete. The remaining former actions are implementation-time
decisions gated by the matching, rendering, artwork, caching, and release
milestones and are tracked by the decision gates in `PLANS.md`. The
architectural blockers are resolved and remain checked in the Blockers section
above.

## Phase 1 Implementation Tasks

- [x] Remove or demote the duplicate architecture section in
  `docs/architecture.md` and align the planner/research phase descriptions.
- [x] Extend the canonical match model to represent Sonarr series, episode, and
  episode-file identity explicitly. See `docs/data-model.md` sections 3.4 and
  3.4.1.
- [x] Establish initial validated defaults for queue, concurrency, retry,
  timeout, response-size, provenance-retention, storage, and stale-state limits.
  Values, units, validation ranges, and safe failure behavior are recorded in
  `docs/architecture.md` section 12 and ADR-004. Configuration-load enforcement
  and boundary tests are implemented in task 1.5; state quota and retention
  enforcement are implemented in task 1.7. Representative-load validation
  remains the performance milestone.
- [x] Implement the plugin entry point and immutable configuration boundary:
  connection and operational-limit validation, replacement snapshots with
  last-valid retention, and secret-free diagnostics. See
  `src/ArrTags/Configuration`. The Jellyfin `BasePlugin<PluginConfiguration>`
  entry point is unchanged.
- [x] Implement dependency-injection registration and the hosted service
  lifecycle without starting provider, rendering, or full-library work during
  registration or startup, including library-event subscription cleanup and
  cancellation-aware shutdown. See `src/ArrTags/PluginLifecycle`. The parameterless
  `IPluginServiceRegistrator` registers the configuration snapshot, state
  repository, library-event boundary, and an idle hosted lifecycle service that
  unsubscribes deterministically on shutdown, restart, cancellation, and disposal.
- [x] Establish the versioned plugin state boundary under `DataFolderPath`:
  schema-versioned envelopes with SHA-256 payload integrity, atomic
  flush-and-rename writes, cache discard versus authoritative quarantine,
  traversal-safe paths, and bounded cache and terminal-provenance retention. See
  `src/ArrTags/State`. Dependency-injection wiring is completed in task 1.6.
- [x] Build and load the actual plugin against the pinned compatibility set on
  the pinned Jellyfin `12.0.0` host; verify plugin discovery and
  `targetAbi: 12.0.0.0` compatibility. See the host validation evidence below.

An initial plugin foundation now exists. `src/ArrTags` targets `net10.0` against
the pinned `12.0.0` Jellyfin packages, `build.yaml` declares
`targetAbi: 12.0.0.0`, and `tests/ArrTags.Tests` checks the manifest ABI, plugin
identity, and the pinned dependency graph without a live host. The configuration
foundation adds connection and operational-limit validation, an immutable
replacement-snapshot service with last-valid retention, and secret-free
snapshots. The state boundary provides versioned, integrity-tagged records,
atomic writes, cache discard versus authoritative quarantine, traversal-safe
paths, and bounded retention. The dependency-injection foundation registers the
configuration snapshot, state repository, and library-event boundary, and an idle
hosted lifecycle service subscribes to library events only for its own lifetime.
All of the above are covered by foundation tests.

The final compatibility-set validation was performed by installing the generated
`artifacts/ArrTags_0.1.0.0.zip` package on the pinned Jellyfin `12.0.0` host (the
portable `v12.0` amd64 build, OS Ubuntu 24.04, .NET runtime `10.0.11` / host
`10.0.12`) and starting the host with both providers disabled. The host reported
`Loaded plugin: ArrTags 0.1.0.0`, wrote a plugin `meta.json` with
`targetAbi: 12.0.0.0` and `status: Active`, completed startup with no load
errors, unmanaged background work, or secret leakage, survived a restart, and
disposed the plugin cleanly on shutdown.

The exact OS and runtime packaging of a production host is an operator deployment
concern that is not repository-verifiable; plugin behavior is validated against
the pinned `net10.0` / Jellyfin `12.0.0` compatibility set. The configuration
snapshot is initialized from the persisted plugin configuration through the
service registrator, but it is not yet connected to Jellyfin's configuration save
path and no reconciliation worker consumes it; those land with the milestones
that introduce them.

## Implementation-Time Questions

- Validate `SaveImage` storage behavior and the selected source-artwork capture
  and restoration implementation on the target host configuration.
- Choose the renderer library, image format, fonts, and bounded artifact storage.
- Decide the remaining V1 image-surface details (indexed images, alternate
  versions, stacked parts). Item types and badge surfaces are resolved by
  ADR-006: V1 badge surfaces are Movie and Episode posters; Series/Season are
  structural only.
- Decide episode policies for specials, anime/absolute numbering, double
  episodes, multi-episode files, remote items, and path fallback.
- Define connection-to-library routing and ambiguity behavior for multiple
  Sonarr/Radarr instances.
- Define the Sonarr catalogue cache, provider inventory strategy, initial
  supported provider-version matrix, and cross-provider normalization rules.
- Decide whether stale metadata retains the active derived artwork, restores the
  source, or publishes an unbadged source image.
- Define webhook authentication, replay protection, rate limits, payload bounds,
  route exposure, and provider-record-to-Jellyfin resolution, or explicitly
  defer webhooks. Secret persistence/access is resolved by ADR-005.
- Define Jellyfin Enhanced duplicate-badge and Spoiler Guard behavior and test
  the selected policy through the standard image path.
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
- [x] Provider credentials remain in persisted plugin configuration and are
  obtained through the versioned, short-lived secret boundary in ADR-005.

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
