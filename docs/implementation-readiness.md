# Implementation Readiness

## Status

**Status:** Phase 1 complete; Milestone 2 complete (Gate 2 met); Phase 3 media matching complete (tasks 3.1 through 3.8, Milestone 3 acceptance criteria satisfied and Gate 3 met); DG-3 accepted by ADR-009; renderer implementation contract accepted by ADR-010; SkiaSharp/HarfBuzzSharp host compatibility confirmed by the task 4.8 spike

**Basis:** ADR-002, ADR-003, ADR-004, ADR-005, ADR-008, ADR-009, and ADR-010; findings in
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

## Resolved DG-3

ADR-009 defines the complete V1 badge rendering contract. The renderer targets
only unindexed Movie and Episode `Primary` posters and consumes provider-neutral
`BadgeMetadata` selectors. It displays actual quality, resolution, confirmed
dynamic range, source, video codec, one composite audio value, bounded custom
values, and an explicitly true upgrade-pending status, in the fixed priority and
layout defined by the ADR.

The contract fixes the two-row bottom-left technical rail, top-right `UPGRADE`
status pill, reference geometry and scale, bold/semibold single-line
typography, 24-scalar end truncation, contrast-validated opaque colors, lossless
8-bit PNG output at source dimensions, alpha preservation, no client-size or
device-pixel-ratio variants, and pass-through on unknown, unavailable,
unsupported, cancelled, malformed, or failed input. The renderer never reads
provider DTOs or turns missing values into claims.

DG-3 is a documentation gate, not a rendering implementation gate. Phase 4 can
begin immediately. The renderer library and bundled font are now resolved by
ADR-010; the implementation code, pinned package/font versions, and tests remain
outstanding.

## Resolved ADR-010

ADR-010 defines the V1 renderer implementation contract without changing the
ADR-009 visual specification. V1 uses a plugin-owned, provider-neutral
SkiaSharp renderer with exact managed and Linux native asset pins, and it does
not use Jellyfin's global image services. Text uses the bundled DejaVu Sans Bold
2.37 font loaded by resource bytes, with no host-font fallback, and its hash and
license notice are part of the renderer identity.

The host boundary supplies a bounded, read-only `SourceImageInput` containing
the exact source bytes or artifact handle, content type, oriented dimensions,
and source hash. The conceptual service contract is
`RenderAsync(RenderRequest, CancellationToken) -> RenderResult`; the renderer
has no Jellyfin, provider, or filesystem side effects and never mutates the
source bytes. Successful output is bounded, non-interlaced 8-bit sRGB PNG with
RGB or RGBA channels depending on source alpha, fixed encoder settings, stripped
nondeterministic metadata, and canonical transparent-pixel values. Invalid
input, missing runtime/font assets, cancellation, decode/layout/encode errors,
and resource-limit violations return a safe bounded result.

Renderer configuration persists only enabled V1 selectors, bounded templates,
and contrast-validated style overrides inside the immutable versioned
configuration snapshot. Format, color-space, alpha, font, geometry/text limits,
and renderer version stay code-owned and fingerprinted. Validation uses
synthetic fixtures, decoded-pixel goldens, same-runtime byte determinism, and
explicit cross-runtime anti-aliasing tolerances.

The renderer-side `SourceImageInput` and `RenderAsync` contract is Phase 4. The
Jellyfin host source adapter that reads the unindexed `Primary` image and
supplies those bytes, plus publication, provenance, restoration, caching,
stale-artwork lifecycle, and Enhanced coexistence, stays in Phase 5 and later.

## Resolved SkiaSharp host compatibility (task 4.8)

Task 4.8 confirmed the pinned Jellyfin `12.0.0` host versions from its
`jellyfin.deps.json` and native files: `SkiaSharp` / `SkiaSharp.HarfBuzz` /
`SkiaSharp.NativeAssets.Linux` `3.119.4`, and `HarfBuzzSharp` /
`HarfBuzzSharp.NativeAssets.Linux` `8.3.1.5`, with native ELF64 x86-64
`libSkiaSharp.so` and `libHarfBuzzSharp.so`. The host's SkiaSharp managed and
native files are byte-identical to the local NuGet `3.119.4` assets.

The plugin load behavior was measured, not assumed. Jellyfin 12 constructs
`PluginLoadContext(pluginFolder)`, whose `AssemblyDependencyResolver` does not
discover a deps.json inside that folder, and then loads every DLL in the folder
into the plugin context. A plugin that ships no SkiaSharp therefore resolves the
host's shared managed SkiaSharp and uses the host's native library; a plugin that
ships `SkiaSharp.dll` gets its own managed copy, and its native
`libSkiaSharp.so` is found only when it sits next to `SkiaSharp.dll` in the
plugin folder root. The round trip decoded, drew with the bundled DejaVu Sans
Bold font from embedded bytes, and encoded a non-interlaced 8-bit PNG on the
pinned runtime. `HarfBuzzSharp` is not needed for ADR-009's single-line bounded
labels, so the 4.7 decision to omit it stands.

The evidence, exact commands, and the Phase 5 packaging constraint (ship the
managed and root-level native SkiaSharp assets in the plugin folder) are recorded
in `docs/research/skia-host-compatibility.md`. Phase 5 must still validate the
packaged assets on the live host.

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
| 3 | Decide V1 item/image scope and aggregate quality behavior. | Resolved for V1 | Resolved by ADR-006 and ADR-009: V1 badge surfaces are unindexed Movie and Episode `Primary` posters; Series/Season are structural only, aggregate quality remains post-V1, and indexed or alternate poster surfaces are not render targets. |
| 4 | Decide episode policies for specials, anime/absolute numbering, double episodes, multi-episode files, remote items, and path fallback. | Resolved for V1 | ADR-007 defines numbering and ADR-008 defers path fallback out of V1. Virtual, missing, remote, offline, and `.strm` items do not gain a path-based badge match; task 3.8 enforces the no-badge outcome fail-closed in the matching pipeline. |
| 5 | Define connection-to-library routing and ambiguity behavior for multiple Arr instances. | Implementation-time decision | Finalize before multi-connection matching and reconciliation work. |
| 6 | Define catalogue caching, provider inventory, provider-version support, and cross-provider normalization. | Implementation-time decision | Define during provider integration and cover the result with bounded inventory and contract tests. |
| 7 | Decide the policy for active derived artwork after metadata becomes stale. | Implementation-time decision | Finalize before stale-state artwork handling and publication invalidation are implemented. |
| 8 | Define webhook security, bounds, replay handling, and provider-record resolution, or defer webhooks. | Implementation-time decision | Decide before webhook work; deferral remains an available V1 scope decision. |
| 9 | Record concrete queue, concurrency, retry, timeout, response-size, retention, storage, and stale-state limits. | Should become a Phase 1 implementation task | Complete for the foundation defaults; recorded in ADR-004 and `docs/architecture.md` section 12, with runtime enforcement and tuning in later milestones. |
| 10 | Define Jellyfin Enhanced duplicate-badge and Spoiler Guard behavior. | Implementation-time decision | Finalize and test before the Jellyfin artwork integration milestone. |
| 11 | Define secret persistence, safe references, credential access, rotation, and webhook-secret reuse. | Should become a Milestone 2 implementation task | Resolved by ADR-005 and implemented in task 2.3; the credential-boundary tests pass before authenticated provider reads. |
| 12 | Define the V1 badge rendering contract. | Resolved for V1 | Resolved by ADR-009; implementation must use its provider-neutral fields, layout, bounds, output, scaling, contrast, and pass-through rules. |
| 13 | Define the V1 renderer implementation contract. | Resolved for V1 | Resolved by ADR-010; implementation must use the pinned SkiaSharp stack, bundled DejaVu Sans Bold 2.37 font, bounded source/result contract, sRGB PNG policy, immutable renderer configuration, and golden/determinism tests. See the Phase 4 tasks in `PLANS.md`. |

## Already Satisfied

- [x] The Jellyfin `12.0.0` / `net10.0` compatibility target, host package
  versions, and plugin `targetAbi` are pinned and documented above.
- [x] API-key and webhook-secret persistence and versioned access are defined
  without exposing values to canonical state or workers' mutable configuration.
- [x] V1 badge surfaces are Movie and Episode posters, and library scope uses
  Jellyfin collection-folder/library identifiers (ADR-006).
- [x] Episode numbering for specials, anime/absolute numbering, double episodes,
  and multi-episode files is defined and tested (ADR-007, task 3.5).
- [x] Ineligible item locations (remote, virtual, offline, `.strm`, fileless,
  and otherwise non-local) fail closed with a safe no-badge status before
  provider matching, while eligible local files match unchanged (ADR-008, task
  3.8). Paths remain non-identity context only.
- [x] The pinned Jellyfin 12.0.0 SkiaSharp/HarfBuzzSharp versions and native
  library names, the plugin load-context resolution behavior, and one
  decode/draw/encode round trip on the pinned Linux runtime are confirmed (task
  4.8; see `docs/research/skia-host-compatibility.md`).

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
- Pin and validate the exact SkiaSharp managed package, Linux native asset
  package, and supported Linux RIDs against the declared Jellyfin 12 /
  `net10.0` target. The renderer library (SkiaSharp) is resolved by ADR-010, and
  the pinned `3.119.4` host version, native library names, plugin load-context
  resolution, and one decode/draw/encode round trip were confirmed by task 4.8
  (see `docs/research/skia-host-compatibility.md`). Multi-RID packaging and the
  live-host behavior of the packaged assets remain Phase 5 validation.
- Validate that the bundled DejaVu Sans Bold 2.37 font renders ADR-009's
  typography metrics, record its SHA-256, and confirm where the font and Skia
  license notices are packaged. The font choice is resolved by ADR-010.
- Decide whether V1 exposes contrast-validated palette overrides or keeps the
  fixed ADR-009 palette; ADR-010 permits overrides only as a bounded
  configuration value.
- Define which embedded ICC profiles the renderer treats as supported and the
  exact pass-through/failure mapping for unsupported or invalid profiles.
- Fix the canonical RGB value used for fully transparent output pixels and the
  accepted non-canonical Linux runtime used for cross-runtime tolerance tests.
- Wire renderer configuration to the administrative save/config surface;
  Gate 4 tests can construct configuration snapshots directly, so this is not a
  Phase 4 prerequisite.
- Validate how the publication pipeline supplies the unindexed `Primary` source
  image for Movie and Episode items. Item types and badge surfaces are resolved
  by ADR-006 and ADR-009; indexed or alternate poster surfaces are not V1
  render targets. The host source adapter is Phase 5; the renderer-side
  `SourceImageInput` contract is resolved by ADR-010.
- Episode policies for specials, anime/absolute numbering, double episodes, and
  multi-episode files are resolved by ADR-007 and task 3.5. ADR-008 resolves
  DG-5 by deferring path fallback out of V1; virtual, missing, remote, offline,
  and `.strm` items receive no path-based match.
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
- [x] DG-3 badge fields, provider-neutral selectors, layout, typography,
  contrast, text bounds, PNG output, scaling, and pass-through behavior are
  defined by ADR-009.
- [x] The V1 renderer implementation contract is defined by ADR-010: SkiaSharp
  with pinned native assets, the bundled DejaVu Sans Bold 2.37 font, the
  bounded `SourceImageInput`/`RenderAsync`/`RenderResult` boundary, sRGB PNG and
  alpha/metadata policy, renderer configuration persistence, and the
  golden/determinism test strategy.

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
- Configured Jellyfin-to-Arr path fallback and path normalization, if later
  evidence justifies the additional namespace and ambiguity contract (ADR-008).
