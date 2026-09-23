# Implementation Readiness

## Status

**Status:** Phase 1 complete; Milestone 2 complete (Gate 2 met); Phase 3 media matching complete (tasks 3.1 through 3.8, Milestone 3 acceptance criteria satisfied and Gate 3 met); DG-3 accepted by ADR-009; renderer implementation contract accepted by ADR-010; SkiaSharp/HarfBuzzSharp host compatibility confirmed by the task 4.8 spike; task 4.11's ADR-010 test oracle complete, including the F2 color-profile fail-closed change and the EXIF orientation correctness fix (renderer version 2); DG-8 Jellyfin Enhanced coexistence resolved by ADR-011; Phase 5 Jellyfin artwork integration complete (tasks 5.1 through 5.11, Gate 5 met at the integration-test level); Phase 6 caching, updates, and performance complete (tasks 6.1 through 6.9, Gate 6 met at the integration-test level, tag `v0.1.0-phase6`; the provider inventory/catalogue cache, runtime configuration replacement, the reconciliation coverage bound, and a metrics/status surface were deferred at Phase 6 and are consolidated in `docs/limitations.md`); Phase 7 complete - tasks 7.1-7.8 complete and all five Phase 7 acceptance criteria are met; task 7.2's live verification found a release-blocking versioned-install/data-folder collision that task 7.7 resolves by relocating the plugin state root to `ProgramDataPath/ArrTags` outside `PluginsPath` (ADR-014) with regression tests and a re-run live install/upgrade/reload/uninstall verification that passes, so Phase 7 acceptance criterion 2 is met; task 7.3 complete - the live GOALS success-criteria verification on the pinned host met criteria 1-4 and 9, covered criterion 7 at the contract level, and found release blocker 7.3-F1 (the bundled `SkiaSharp.dll`/`libSkiaSharp.so` conflict fatally with the host's own SkiaSharp and abort Jellyfin on the first badge publication); task 7.8 complete - the plugin no longer bundles the duplicate renderer runtime and shares the host's SkiaSharp through the default load context (ADR-015, superseding the bundling parts of ADR-010), with the packaged-runtime contract updated in `ArrTags.csproj`, `build.yaml`, and `PluginPackagingTests`, and a re-run live end-to-end verification on the pinned Jellyfin `12.0.0` musl host that passes (the package loads with no error, a badge publishes with no host crash, `GET /Items/{id}/Images/Primary` serves the published bytes matching the persisted `ActiveImageIdentity`, the original source posters are byte-unchanged, changed mock metadata republishes and unchanged metadata does not, and a provider outage leaves the host up with the current artwork unchanged), so `GOALS.md` criteria 5 and 8 are met as shipped, with criterion 6 met for render and publication but only partial for provider fetches (`docs/limitations.md` F1), and Phase 7 acceptance criteria 3 and 4 are met; task 7.4 complete - the live review of logs, diagnostics, HTTP behavior, and persisted state found no credential leakage and no unbounded path (negative result), so no production fix was required; task 7.5 complete - the release package now builds byte-reproducibly from a clean checkout (deterministic packer, `PathMap`, and suppressed git-derived SDK inputs) and the commands, pinned inputs, artifact identity, supported version ranges, and release checklist are recorded in `docs/release/build-and-release.md`, so Phase 7 acceptance criterion 5 is met; task 7.6 complete - the known limitations and deferred decisions are consolidated in `docs/limitations.md`, which states explicitly that all five Phase 7 acceptance criteria are met; `GOALS.md` criteria 1-4, 8, and 9 are met as shipped; criterion 5 is met as shipped but requires the host to supply a compatible SkiaSharp; criterion 6 is met for render and publication but only partial for provider fetches; and criterion 7 is met only at the contract level, so no deferred or unverified capability is presented as available. All Phase 7 tasks are complete; Gate 7 is met (Phase 7 review approved; tag `v0.1.0-phase7`). The 0.1.0 release security review's SEC-1 finding (MVC model binding read form/multipart webhook bodies before authentication) is fixed by a pre-binding authorization filter that authenticates before any body read for every content type and rejects a non-JSON body with a bounded `400`, live-re-verified on the pinned host; see `docs/changelog.md`. Phase 8 release distribution is complete (tasks 8.1-8.6): the `1.0.1.0` release is prepared with the annotated tag `v1.0.1` at `8cba85b` and the committed repository `manifest.json`; the GitHub release publication, the asset upload, and the manifest push remain the user's manual step with `scripts/publish-release.sh`, so the plugin-catalog install is prepared but not yet live. Phase 9 (v1.1) dashboard settings UI tasks are complete: task 9.1 (configuration round-trip spike, blocking prerequisite) is complete - the get-only `Collection<T>` round-trip was proven to fail under the pinned `Jellyfin.Extensions.Json.JsonDefaults.Options`, so `PluginConfiguration.EnabledLibraries` and `RendererConfiguration.Selectors` are now settable (null-coalescing) and are populated by the supported elevation-gated `PluginsController` POST, with the persisted XML shape unchanged (see `docs/data-model.md` 3.12 and `PLANS.md` task 9.1); task 9.2 (dashboard settings page and embedded page resource) is complete - `Plugin` implements `IHasWebPages` with one secret-free embedded `Configuration/config.html` page (logical name `ArrTags.Configuration.config.html`) that reads and writes the user-adjustable configuration through the administrator-gated API, ADR-016 clause 6's acceptance of the anonymous static page-resource endpoint is recorded in `docs/architecture.md` section 6, and the pinned `DashboardController` serving and authorization behavior are confirmed by host-guarded tests skipped without `ARRTAGS_JELLYFIN_HOST_DIR`; task 9.3 (elevation-gated save path and runtime activation) is complete - `Plugin` overrides `UpdateConfiguration` to validate the candidate before persistence and then, for a valid candidate, persist it with the host base implementation and activate it through `ConfigurationSnapshotService.TryReplace` without a host restart; an invalid candidate is rejected before persistence, the last valid public snapshot and private secret generation are retained, the save sequence is serialized so concurrent saves cannot diverge, the rejection is surfaced as one bounded, secret-free activity-log entry through the plugin-owned `IConfigurationRejectionNotifier` adapter (ADR-021), the override never throws into the host, and it adds no custom configuration-save route (see `docs/architecture.md` section 6 and `docs/data-model.md` 3.12); task 9.4 (bounded post-save reconciliation trigger) is complete - a successful replacement requests a bounded, non-blocking reconciliation through the plugin-owned `IConfigurationReconciliationTrigger` boundary, whose hosted `ConfigurationReconciliationTrigger` runs the existing bounded `LibraryReconciliationService` off the save thread and coalesces redundant requests to at most one bounded rerun, so existing posters re-render promptly without a synchronous full-library scan or a blocked save response; task 9.5 (Goal A documentation and integration verification) is complete - the canonical documents (`docs/architecture.md` section 6, `docs/data-model.md` 3.12, `docs/limitations.md` F2, and `README.md`) describe the shipped behaviour, limitation F2 is recorded as resolved, and `GoalAIntegrationTests` composes the full save -> activate -> bounded-reconcile flow without a live host (the pinned POST deserialization, the real `Plugin.UpdateConfiguration` override, the real snapshot service, the real post-save trigger over the bounded reconciliation service, and the real artwork publishing pipeline), so all six Phase 9 acceptance criteria are met at the integration-test level. Phase 9 is complete: Gate 9 is met (the Phase 9 review is approved in `docs/implementation/phase-9/phase-review.json`) and the annotated tag `v1.1.0-phase9` is created; the live Goal A confirmation is owned by task 14.3.

**Basis:** ADR-002, ADR-003, ADR-004, ADR-005, ADR-008, ADR-009, ADR-010, and ADR-011; findings in
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

## Resolved renderer orientation defect (task 4.11)

- [x] Fix the renderer's EXIF dimension-swapping orientation transforms. Task
  4.11's orientation golden exposed a genuine pre-existing defect:
  `src/ArrTags/Rendering/SkiaOrientation.cs` used the oriented `height`/`width`
  as the translation origin for orientations 5-8 instead of the source
  dimensions, so an opaque 500x750 JPEG with EXIF orientation 6 rendered to
  750x500 with 125,000 fully transparent pixels and clipped content, orientation
  7 left 250,000 transparent and lost the corner marker, and orientation 8 left
  187,500 transparent. The transforms now translate about
  `source.Height`/`source.Width`; `RenderOrientationTests` proves all eight
  orientations are fully opaque and place their corner markers correctly. Because
  the fix is output-affecting, `RenderVersion.CurrentRendererVersion` advanced
  from 1 to 2 and the committed golden manifest was regenerated under ADR-010.

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

## Resolved DG-8

ADR-011 resolves the Jellyfin Enhanced coexistence policy. ArrTags does not
implement automatic duplicate-badge detection, overlap suppression, or a
dependency on Enhanced internals; Jellyfin Enhanced chooses its own overlay
placement, so overlap handling is deferred to the user. ArrTags badge output is
controlled only by the existing configuration (the
`BadgeMoviePosters`/`BadgeEpisodePosters` poster enable flags and the renderer
selector enablement), with no new suppression knob or coexistence field.
Enhanced's Spoiler Guard has no material effect on ArrTags badge display, so
ArrTags renders its derived badge normally and adds no special spoiler/hidden
handling or Enhanced filter-ordering dependency.

The coexistence tests in `tests/ArrTags.Tests/EnhancedCoexistenceTests.cs`
assert the absence of an Enhanced reference and of an Enhanced/spoiler/
suppression type in the production assembly, the absence of a spoiler, hidden,
or duplicate/overlap suppression branch in the policy surface and the renderer/
publication reason enums, and that badge eligibility and output vary only with
the existing ArrTags poster and selector flags.

## Resolved SkiaSharp host compatibility (task 4.8)

Task 4.8 confirmed the pinned Jellyfin `12.0.0` host versions from its
`jellyfin.deps.json` and native files: `SkiaSharp` / `SkiaSharp.HarfBuzz` /
`SkiaSharp.NativeAssets.Linux` `3.119.4`, and `HarfBuzzSharp` /
`HarfBuzzSharp.NativeAssets.Linux` `8.3.1.5`, with native ELF64 x86-64
`libSkiaSharp.so` and `libHarfBuzzSharp.so`. The host's managed `SkiaSharp.dll`
is byte-identical to the local NuGet `3.119.4` asset. The native
`libSkiaSharp.so` byte-identity holds for the matching RID, not across RIDs: the
task 4.8 glibc spike host matched the NuGet linux-x64 asset, while the pinned musl
host's native library is 18,453,464 bytes / SHA-256 `59039b25...`, byte-identical
to the NuGet `3.119.4` linux-musl-x64 asset (and to the file inside the pinned
Jellyfin distribution) rather than the linux-x64 `11,170,296` / `66c856ea...`
asset (task 7.3 reviewer finding F2).

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

The evidence and exact commands are recorded in
`docs/research/skia-host-compatibility.md`. The task 4.8 "ship the managed and
root-level native SkiaSharp assets in the plugin folder" packaging constraint is
superseded by ADR-015 (task 7.8): shipping the duplicate managed SkiaSharp was
found fatal live (task 7.3 finding 7.3-F1), and the plugin now shares the host's
SkiaSharp through the default load context. Task 7.8 re-validated the full
render/publication path on the live host.

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
| 10 | Define Jellyfin Enhanced duplicate-badge and Spoiler Guard behavior. | Resolved for V1 | Resolved by ADR-011: ArrTags adds no automatic duplicate/overlap detection or suppression and no Enhanced-internals dependency, the existing poster/selector enable flags remain the user's control surface, and Spoiler Guard has no material effect on ArrTags badge display. Covered by `EnhancedCoexistenceTests` in task 5.10. |
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
  and restoration implementation on the target host configuration. The exact
  Jellyfin 12.0.0 publication/read ABI, the standard item-image route variants,
  the read/write authorization split, and Jellyfin's ownership of image tags,
  caching, and resizing are now pinned by task 5.1 in
  `docs/research/jellyfin-12-architecture.md` section 4.4; the remaining
  host-behavior items are the exact on-disk representation, post-publication
  read-back, and standard-route delivery.
- Pin and validate the exact SkiaSharp managed package, Linux native asset
  package, and supported Linux RIDs against the declared Jellyfin 12 /
  `net10.0` target. The renderer library (SkiaSharp) is resolved by ADR-010; task
  4.8 confirmed the pinned `3.119.4` host version, native library names, plugin
  load-context resolution, and one decode/draw/encode round trip (see
  `docs/research/skia-host-compatibility.md`). Task 7.8/ADR-015 resolves the
  packaging: the plugin compiles against the pinned packages with runtime assets
  excluded and takes the managed assembly and native library from the host,
  superseding the ADR-010 bundling requirement. V1 remains validated on the
  pinned `linux-musl-x64` host only.
- Validate that the bundled DejaVu Sans Bold 2.37 font renders ADR-009's
  typography metrics, record its SHA-256, and confirm where the font and Skia
  license notices are packaged. The font choice is resolved by ADR-010.
- Decide whether V1 exposes contrast-validated palette overrides or keeps the
  fixed ADR-009 palette; ADR-010 permits overrides only as a bounded
  configuration value.
- Resolved by task 4.11: the renderer treats an input without an embedded
  profile as sRGB, converts a PNG `iCCP`/JPEG `APP2` profile that
  `SKColorSpace.CreateIcc` parses, and fails closed with
  `RenderFailureReason.UnsupportedColorProfile` for a malformed or unsupported
  profile. The PNG-contract tests assert both rejection and conversion.
- The canonical RGB value for fully transparent output pixels is fixed as
  `#00000000` and verified by the task 4.6/4.11 canonical-transparent tests.
  The accepted non-canonical Linux runtime for cross-runtime tolerance tests
  remains unselected: this environment has only the pinned canonical runtime and
  the identical Jellyfin host runtime, so the task 4.11 tolerant half is a
  recorded environment limitation, not a fabricated result. The comparison is
  data-driven against an optional `Goldens/non-canonical/` set and is skipped by
  `NonCanonicalRuntimeFactAttribute` while absent. Recommended resolution: in the
  testing/release milestone, select and record a second explicitly supported
  non-canonical Linux runtime that shares the pinned SkiaSharp 3.119.4
  managed/native assets, produce its golden set with the same manifest shape, and
  run the ADR-010 anti-aliased-text tolerance against it.
- Resolved by task 4.11's F2 supporting change: the exact pass-through/failure
  mapping for invalid or unsupported embedded color profiles is now implemented
  and tested (see the first item above).
- Wire renderer configuration to the administrative save/config surface;
  Gate 4 tests can construct configuration snapshots directly, so this is not a
  Phase 4 prerequisite.
- Validate how the publication pipeline supplies the unindexed `Primary` source
  image for Movie and Episode items. Item types and badge surfaces are resolved
  by ADR-006 and ADR-009; indexed or alternate poster surfaces are not V1
  render targets. The host source adapter is Phase 5; the renderer-side
  `SourceImageInput` contract is resolved by ADR-010. Task 5.1 pinned the read
  surface it will use (`BaseItem.GetImageInfo`, `ItemImageInfo`, `ImageInfo`,
  `IImageProcessor.GetImageCacheTag`/`GetImageDimensions`, and the standard
  item-image route); task 5.3 implemented the adapter over that read surface with
  an explicit PNG/JPEG container confinement and byte/dimension bounds, and the
  exact publication read-back remains host validation.
- Episode policies for specials, anime/absolute numbering, double episodes, and
  multi-episode files are resolved by ADR-007 and task 3.5. ADR-008 resolves
  DG-5 by deferring path fallback out of V1; virtual, missing, remote, offline,
  and `.strm` items receive no path-based match.
- Define connection-to-library routing and ambiguity behavior for multiple
  Sonarr/Radarr instances.
- Define the Sonarr catalogue cache, provider inventory strategy, initial
  supported provider-version matrix, and cross-provider normalization rules. The
  initial supported provider-version matrix is resolved by ADR-013: Sonarr
  3.x-4.x and Radarr 3.x-6.x on `/api/v3`, with absent optional fields mapped to
  explicit unknowns and a malformed required field failing closed as
  `ProviderIncompatible`. The provider catalogue/inventory cache is promoted to
  v1.1 (ADR-018) and tracked in `docs/planning/v1.1.md`, and the cross-provider
  normalization strategy remains a post-V1 consideration.
- Decide whether stale metadata retains the active derived artwork, restores the
  source, or publishes an unbadged source image.
- Define webhook authentication, replay protection, rate limits, payload bounds,
  route exposure, and provider-record-to-Jellyfin resolution, or explicitly
  defer webhooks. Secret persistence/access is resolved by ADR-005. Resolved by
  ADR-012 (task 6.7): the anonymous `/ArrTags/Webhook/Sonarr` and
  `/ArrTags/Webhook/Radarr` plugin routes authenticate the
  `X-ArrTags-Webhook-Secret` header through the constant-time versioned webhook
  lease, bound and tolerantly parse the payload, coalesce duplicate/replayed
  deliveries in a bounded intake, resolve only already-known provider-record
  associations bounded by the reconciliation batch size, and enqueue the same
  deduplicated `LibraryWorkHint` work as every other trigger; a webhook never
  publishes metadata, mutates artwork, or calls an Arr endpoint.
  `WebhookBoundaryTests` and `WebhookResolutionTests` cover the boundary without
  a live host.
- Resolved by ADR-011: ArrTags adds no automatic duplicate/overlap detection or
  suppression and no Enhanced-internals dependency; the existing poster and
  renderer selector enable flags are the user's control surface, and Spoiler
  Guard has no material effect on ArrTags badge display. `EnhancedCoexistenceTests`
  (task 5.10) covers the policy without a live Enhanced install; confirming the
  standard image route is completed by task 5.11 (`JellyfinImageResponseTests`
  invokes the real pinned host `ImageController` actions and response pipeline
  in-process; no live HTTP round-trip was performed).
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
- [x] The Jellyfin Enhanced coexistence policy is resolved by ADR-011: no
  automatic duplicate/overlap suppression, no Enhanced-internals dependency, and
  no special spoiler/hidden handling; the existing poster and selector enable
  flags remain the user's control surface.
- [x] Provider credentials remain in persisted plugin configuration and are
  obtained through the versioned, short-lived secret boundary in ADR-005.
- [x] DG-3 badge fields, provider-neutral selectors, layout, typography,
  contrast, text bounds, PNG output, scaling, and pass-through behavior are
  defined by ADR-009.
- [x] The V1 renderer implementation contract is defined by ADR-010 (its
  renderer-bundling parts superseded by ADR-015): SkiaSharp compiled against an
  exact pin with the host providing the managed assembly and native library at
  runtime, the bundled DejaVu Sans Bold 2.37 font, the bounded
  `SourceImageInput`/`RenderAsync`/`RenderResult` boundary, sRGB PNG and
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

**Open items carried out of Phase 7 (from the Phase 6 review and the Phase 7
reviews; consolidated in `docs/limitations.md` and not presented as solved):**

**v1.1 promotion:** the provider inventory cache and the runtime configuration
replacement wiring are promoted into v1.1 and tracked in
[`docs/planning/v1.1.md`](planning/v1.1.md) (Goals C and A, resolved by ADR-018
and ADR-016). They are no longer deferred V1 items.

- **Promoted to v1.1 (ADR-018):** provider catalogue/inventory cache. Every
  reconciliation work item still re-reads the whole provider library and the
  per-record file resource, so the "avoid unnecessary API requests" goal is only
  partially met for provider fetches; v1.1 adds a bounded, short-TTL
  inventory/catalogue cache with ArrTags-side invalidation.
- **Promoted to v1.1 (ADR-016):** runtime configuration replacement wiring.
  `ConfigurationSnapshotService.TryReplace` is implemented and validated, and
  task 9.3 wires it to Jellyfin's configuration-update mechanism
  (`Plugin.UpdateConfiguration`), so a saved webhook secret, provider
  enable/disable, badge/selector change, or DG-6 limit change is observed
  without a restart for the values resolved per operation; construction-captured
  limits (the artifact-size/decode limits and the render work-cache TTL/quota and
  terminal-provenance retention) still require a restart
  (`docs/limitations.md` F2). Task 9.4 adds the bounded, non-blocking post-save
  reconciliation trigger, so a successful save also re-renders existing posters
  promptly instead of waiting for the next library event, webhook, post-scan, or
  scheduled run. Task 9.5 records limitation F2 as resolved, so this promotion is
  complete.
- Safe metrics/diagnostic-status surface: bounded queue, provider-health,
  matching, cache, render, and stale-data counters exist internally but no
  bounded, secret-free user-facing or diagnostic status surface exists yet.
- Reconciliation coverage bound: successive scheduled/post-scan runs re-cover the
  same bounded `QueueCapacity` prefix when the scope is larger, so full coverage
  is not guaranteed by a single run (`docs/architecture.md` lines 472-478).
- Verification-coverage gaps and packaging/release limitations (no live
  Sonarr/Radarr instance; unselected/unrun ADR-010 non-canonical comparison;
  contract-level-only Enhanced coexistence; manual-only live `SaveImage`
  read-back; unexercised live uninstall drain; pinned-toolchain-dependent
  byte-reproducibility; reduced debug metadata; stage-only `PackagePlugin=true`;
  retained SkiaSharp license notices; pre-release orphaned state) are itemized in
  `docs/limitations.md`.
- Optional hardening: `ArtworkSubjectGate` has no idle eviction (task 7.4
  finding 7.4-R1); the webhook `401` framework ProblemDetails body is a minor
  wording gap for a future ADR-012 clarification (task 7.4 finding 7.4-R2).

**Resolved by task 7.7 (Phase 7 release blocker from task 7.2):**

- [x] Versioned-install-layout data-folder collision: the plugin's
  Jellyfin-derived `DataFolderPath` was `PluginsPath/<assembly name>` and the
  plugin persisted state there, so a standard versioned install
  (`PluginsPath/ArrTags_<version>`) was deleted by `PluginManager.DiscoverPlugins`
  on the next host restart. Resolved by ADR-014: the `Plugin` constructor calls
  the public `BasePlugin.SetAttributes` contract to relocate the state root to
  `ProgramDataPath/ArrTags` outside `PluginsPath`, the uninstall hook removes its
  own relocated state root after a completed drain, regression tests cover the
  versioned-install-plus-state case, and the live install/upgrade/reload/uninstall
  verification passes with state present and the install folder preserved. No
  released installs exist, so no migration is required; an unreleased developer
  install may have an orphaned `PluginsPath/ArrTags` folder that must be deleted
  once.
