## A. Overall Assessment

**The project is not ready for full V1 implementation.** The domain architecture, provider separation, canonical metadata model, failure policy, and milestone structure are mostly coherent. However, three blockers must be resolved first.

Intended flow is understood as:

`Jellyfin item/image request -> identity and authorization -> cached canonical match/metadata -> Sonarr or Radarr read -> deterministic badge rendering -> derived image response -> Jellyfin client`

Important missing responsibilities are:

- Provider-record-to-Jellyfin-item resolution for webhook events.
- Authorization-aware rendered-image cache access.
- Ownership of transformed ETags, conditional requests, ranges, and `HEAD`.
- Provider inventory/index caching for large-library reconciliation.

### Requirements Traceability

| Goal | Architecture | Data Model | Plan | Implementation/Test Path | Gap |
|---|---|---|---|---|---|
| Jellyfin 12 compatibility | Plugin registration/lifecycle and Jellyfin research | `Configuration`, `MediaIdentity` | Milestones 1, 5, 7 | ABI smoke test, plugin discovery, lifecycle tests | **BLOCKER:** exact patch, .NET target, packages, and `targetAbi` are unset |
| Sonarr/Radarr integration | `architecture.md:195-240` | `ArrProvider`, `ArrConnection`, `BadgeMetadata` | Milestone 2 | DTO fixtures, auth/outage/version tests | **PRE-IMPLEMENTATION:** supported release matrix and normalization rules are incomplete |
| Movie/episode matching | Matching policy in `architecture.md:210-225` | `MediaIdentity`, `MediaMatch` | Milestone 3 | Identity, ambiguity, numbering, file-join tests | **PRE-IMPLEMENTATION:** episode/provider-record representation and numbering policy are incomplete |
| Actual file metadata | Provider metadata rules | `BadgeMetadata.quality`, technical fields, tri-state values | Milestones 2 and 4 | Mapping and unknown-value tests | **IMPLEMENTATION-TIME:** cross-provider normalization rules need specification |
| Configurable badges | Rendering architecture | `BadgeDefinition`, `RenderRequest` | Milestone 4 | Selector, layout, fingerprint, renderer tests | **PRE-IMPLEMENTATION:** initial field set and visual/output policy are undecided |
| Preserve artwork and avoid repeat work | Response rendering and ephemeral cache | `ArtworkCacheEntry`, fingerprints | Milestones 4-6 | Source-artwork invariant, cache-key and invalidation tests | **BLOCKER:** no confirmed Jellyfin overlay extension point |
| Automatic updates | Event, schedule, webhook reconciliation | `UpdateEvent`, cache states | Milestone 6 | Event, restart, deletion, outage tests | **PRE-IMPLEMENTATION:** webhook authentication and reverse lookup are undefined |
| Enhanced compatibility | `architecture.md:313-325` | `enhancedCoexistencePolicy` | Milestones 5 and 7 | Filter-order, Spoiler Guard, duplicate-badge tests | **PRE-IMPLEMENTATION:** behavior is still an acceptance assumption |
| Reliability/performance/release | Failure, cache, lifecycle sections | Error and versioned cache models | Milestones 1, 6, 7 | Load, corruption, clean-build, package tests | **PRE-IMPLEMENTATION:** concrete limits and reproducible toolchain are missing |

The Jellyfin mechanisms are correctly differentiated in the research:

| Mechanism | Classification |
|---|---|
| `IPluginServiceRegistrator` | Supported |
| Plugin controllers for new routes | Supported, but cannot replace native image routes |
| `IHttpClientFactory` through DI | Supported |
| `ILibraryManager`, provider IDs, media sources, library events | Public/supported with normal API caveats |
| `IDynamicImageProvider` | Supported, but refresh-time and persistent, so unsuitable for this V1 overlay |
| `IImageProcessor` replacement/decorator | Public but unstable and global |
| MVC action filter or ASP.NET middleware for native image rewriting | Public framework APIs, but unsupported Jellyfin image extension contracts |
| `ImageController` route/result/header behavior and filter ordering | Internal implementation details |
| Jellyfin Enhanced internals | Unsupported dependency |

This is explicitly documented in `docs/jellyfin-12-architecture.md:244-305`, but the selected architecture and plan still describe the filter as the preferred image integration.

## B. Blockers

1. **The required image delivery mechanism is not a supported Jellyfin plugin extension point.**

   **Evidence:** `docs/jellyfin-12-architecture.md:244-260` and `:275-305` state that no confirmed supported, non-persistent, per-request overlay hook exists. MVC filters and middleware are classified as workarounds. Nevertheless, `docs/architecture.md:95-101` and `PLANS.md:254-300` make the filter/middleware path the V1 delivery mechanism.

   **Affected documents:** `GOALS.md`, `docs/architecture.md`, `docs/jellyfin-12-architecture.md`, `docs/poster-rendering-strategies.md`, `PLANS.md`.

   **Required action:** Explicitly choose one:
   - Accept a version-pinned, unsupported Jellyfin interception workaround and narrow the compatibility claim.
   - Use supported persisted artwork APIs, accepting that original artwork is changed.
   - Use a supported custom image route, accepting that clients must request it.
   - Use a client-side overlay, accepting that native clients do not receive badges.

2. **Authorization and transformed HTTP semantics are not safely specified.**

   The native image action resolves the item using the current user inside the action. The architecture says a cache hit may return directly before the original action (`docs/architecture.md:280-295`). That can bypass item-level authorization unless the cache path performs an equivalent user-scoped authorization check.

   The same design must handle the fact that Jellyfin may return `304` before image bytes exist, while badge metadata can change independently of the Jellyfin image tag (`docs/jellyfin-12-architecture.md:134-152`). `ETag`, `Last-Modified`, `Cache-Control`, `Range`, `HEAD`, and conditional requests are not yet given an implementable contract.

   **Required action:** Define and test an authorization-first response flow and a complete validator policy before selecting filter versus middleware.

3. **The Jellyfin build target and plugin ABI are not pinned.**

   `PLANS.md:406-409` and `docs/architecture.md:403-423` explicitly leave the exact Jellyfin patch, .NET target, package versions, and manifest `targetAbi` unresolved. There is also no project file or reproducible build definition yet.

   **Required action:** Select the exact host build and toolchain, record the package/ABI matrix, and validate plugin discovery before application implementation.

## C. Pre-Implementation Actions

- Resolve the three blockers above.
- Remove or clearly mark the competing architecture appended at `docs/architecture.md:438-479`; it conflicts with the current plan’s combined-provider milestone order.
- Define V1 image/item scope. The safest initial scope is movie and episode primary/poster images only. Explicitly exclude or define series, season, alternate-version, stacked, and indexed-image behavior.
- Define the episode policy for specials, anime/absolute numbering, display-order differences, double episodes, and multi-episode files.
- Extend the canonical match identity to represent both Sonarr series identity and episode identity. `MediaMatch.externalRecordId` at `docs/data-model.md:231-244` is currently insufficiently precise.
- Decide whether V1 supports one Sonarr and one Radarr connection or requires per-library/per-connection routing. `libraryScope` is not currently connection-scoped.
- Establish the supported Sonarr/Radarr release matrix. Current documents describe Sonarr v3/v4 and Radarr v3-v6, while `architecture.md:3-8` names narrower baselines.
- Define the minimal V1 badge field set, output format, truncation, contrast, dimensions, and size limits before renderer work.
- Define concrete queue, HTTP timeout, retry, concurrency, metadata TTL, stale window, rendered-cache size, and eviction defaults.
- Decide whether webhooks are V1 functionality or an optional accelerator. If retained, define shared-secret handling, replay/rate limits, endpoint exposure, and provider-record-to-Jellyfin resolution. The mapping document currently excludes that reverse direction at `docs/media-metadata-mapping.md:12-15`.
- Define how API keys and webhook secrets are stored and administered. `secretReference` is described, but its secure owner and persistence mechanism are not.
- Define redirect, URL validation, TLS exception, path-mapping, and arbitrary-file-access rules for external HTTP and filesystem inputs.
- Run the Jellyfin route spike across GET, HEAD, indexed images, requested sizes/formats, 200/304/non-200 responses, ranges, authorization, and Enhanced/Spoiler Guard combinations.
- Add sanitized provider fixtures for missing files, upgrades, malformed fields, version drift, multi-episode files, and custom-format omissions.
- Establish a large-library test scenario. At the stated scale, a safe design should use provider inventories/catalogues and bounded batches rather than one provider request per Jellyfin item.
- Treat the current cache design as conceptually sound but incomplete: single-flight rendering, atomic rendered-artifact publication, authorization scope, provider indexing, and startup reconciliation need explicit implementation contracts.
- The plan’s milestones are directionally correct, but Milestones 1, 2, 5, 6, and 7 are too broad for individual implementation tasks. Split them at service boundaries.
- The agent definitions are coherent overall. The main issue is that `implementation-planner.md` requires architectural decisions to be recorded in `docs/decisions.md`, but that file does not exist.

### Failure Coverage

| Failure | Defined? | Remaining issue |
|---|---|---|
| Jellyfin restart | Mostly | Exact startup reconciliation policy is unspecified |
| Sonarr unavailable | Yes | Bounded stale-state behavior needs concrete defaults |
| Radarr unavailable | Yes | Bounded stale-state behavior needs concrete defaults |
| Authentication failure | Yes | Secret storage and diagnostic policy need detail |
| Timeout | Yes | Numeric timeout/retry limits are unset |
| No match | Yes | Correctly produces no badge |
| Missing media file | Yes | Aggregate series/season behavior remains open |
| Source poster missing | Partial | Exact pass-through behavior depends on route interception |
| Render failure | Yes | Must preserve original response semantics |
| Cache corruption | Yes | Rendered-artifact atomicity is not specified |
| Item deleted | Partial | Jellyfin deletion is defined; Arr webhook-to-item resolution is not |
| Configuration changed | Yes | Invalidation scope needs tests |
| Plugin disabled/reloaded | Partial | Exact filter/cache teardown behavior is unverified |

## D. Implementation-Time Questions

These can be resolved during implementation after the pre-implementation decisions are recorded:

- Exact DTO nullability and optional-field handling for the declared provider versions.
- Concrete Sonarr/Radarr request batching and provider inventory cache implementation.
- Renderer library, font source, and image encoding details.
- Disk versus memory implementation for the bounded rendered cache.
- Atomic file-write mechanics if disk caching is selected.
- Exact hosted-service lifetime, cancellation, and scheduled-task registration details against the pinned ABI.
- Metrics/status endpoint shape, provided it cannot expose secrets or unbounded payloads.
- Specific fingerprint serialization and hashing implementation.
- Result conversion details for `PhysicalFileResult`, `FileStreamResult`, and equivalent route results after the route spike.
- Lowest-cost integration test harness for the selected Jellyfin host build.

## E. Post-V1 Items

These should not delay V1:

- Additional Arr applications or pre-Jellyfin-12 support.
- Non-poster artwork and additional image surfaces.
- User-specific badges.
- Series/season aggregate quality badges unless a deliberate aggregation policy is added.
- SignalR-based Arr updates.
- Broad technical metadata such as bit depth, frame rate, scan type, subtitles, release group, certification, and stream count.
- Advanced anime/scene-numbering and complex alternate-version aggregation if conservative no-badge behavior is accepted for V1.
- Expanded provider release ranges beyond the tested matrix.
- Webhooks, if periodic reconciliation satisfies the required update latency.
- Client-specific integration with Jellyfin Enhanced internals.

## F. Recommended Documentation Changes

- Make `docs/architecture.md` a single authoritative document by removing or explicitly demoting the duplicated section.
- Add `docs/decisions.md` for the image-delivery choice, ABI pin, V1 scope, webhook policy, and Enhanced policy.
- Add a provider support matrix covering Sonarr/Radarr versions, API contract, optional fields, and test fixtures.
- Update the architecture to distinguish “supported Jellyfin API” from “version-pinned framework workaround.”
- Add an image HTTP contract covering authorization, GET/HEAD, ETags, `304`, ranges, cache headers, and filter ordering.
- Correct the canonical model to represent Sonarr series plus episode IDs and the selected Jellyfin media source.
- Document the provider-record-to-Jellyfin reverse-resolution strategy or explicitly defer webhooks.
- Make connection-to-library routing explicit.
- Add a short traceability section to `PLANS.md`.
- Populate `README.md` with supported versions, installation/configuration, limitations, and the chosen compatibility claim.

## G. Recommended First Coding Task

After the blockers are resolved, start with **a minimal Jellyfin 12 plugin foundation smoke test**:

- Pin the selected Jellyfin package/ABI and .NET target.
- Create the thin plugin entry point and manifest.
- Add the parameterless service registrator.
- Add configuration validation with both providers disabled by default.
- Add an idle hosted service with deterministic shutdown/cancellation.
- Add versioned plugin-state read/write boundaries with corrupt-state recovery.
- Add tests for plugin discovery, DI registration, configuration redaction, startup, shutdown, reload, and cancellation.

The acceptance criteria should be:

- The plugin loads on the exact declared Jellyfin build.
- Both providers can remain disabled without provider I/O.
- Invalid configuration does not take down Jellyfin.
- No background work survives shutdown.
- Corrupt or incompatible state is discarded or rebuilt.
- No secret appears in logs, diagnostics, fingerprints, or canonical state.

The image-route spike should precede production image integration, but it is a compatibility decision spike rather than the first feature implementation.
