# Architecture

**Status:** Draft v1 (pre-implementation)

**Last reviewed against:**
- Jellyfin 12.x
- Sonarr v4.x
- Radarr v5.x

**Purpose:** Canonical architectural design before implementation begins.

This document is the authoritative design for ArrTags. The companion research
documents contain API details, source evidence, and alternatives; they do not
override the decisions recorded here.

## 1. Scope and principles

ArrTags is a Jellyfin 12 plugin that reads Sonarr and Radarr metadata and
renders badges onto image responses. The plugin is read-only with respect to
Sonarr, Radarr, and Jellyfin media metadata.

The architecture follows these principles:

- Preserve the underlying poster and Jellyfin-managed artwork.
- Deliver badges through Jellyfin's normal image endpoints so all clients can
  receive the same result.
- Keep external I/O, image processing, and cache maintenance outside library
  event handlers and request paths where practical.
- Prefer stable provider IDs and documented Jellyfin APIs over names, paths,
  database access, or third-party plugin internals.
- Treat missing, stale, ambiguous, or unavailable external metadata as a
  recoverable condition and pass through the original image when necessary.
- Make every derived result fingerprinted, bounded, cancellable, and safe to
  regenerate after a restart.

## 2. Goals and non-goals

### Goals

- Support Jellyfin 12 only for the initial implementation.
- Support independently configured Sonarr and Radarr connections.
- Match Jellyfin movies to Radarr and TV content to Sonarr.
- Display actual file metadata, especially actual quality, rather than treating
  a configured quality profile as the file's quality.
- Update badges after Jellyfin changes, Arr changes, webhooks, and scheduled
  reconciliation.
- Preserve Jellyfin image files and avoid a permanent generated-poster copy.
- Remain safe alongside Jellyfin Enhanced.
- Fail without degrading Jellyfin library operations or image serving.

### Non-goals

- Writing to Sonarr or Radarr.
- Modifying media files, media-folder posters, or Jellyfin's image cache.
- Replacing Jellyfin's metadata refresh or image-provider pipeline.
- Depending on Jellyfin Enhanced's DOM, JavaScript, configuration, or private
  endpoints.
- Supporting arbitrary Arr applications or Jellyfin versions before 12.

## 3. System architecture

```mermaid
flowchart TD
    JF[Jellyfin host] --> PL[ArrTags plugin]
    PL --> CFG[Configuration]
    PL --> API[Sonarr/Radarr API clients]
    PL --> STATE[Plugin state and metadata cache]

    EVT[Library events] --> Q[Bounded deduplicating queue]
    TASK[Scheduled/post-scan reconciliation] --> Q
    WH[Authenticated Arr webhook] --> Q
    Q --> REC[Reconciliation coordinator]
    REC --> MATCH[Provider-ID matching]
    MATCH --> API
    MATCH --> STATE

    REQ[Client image request] --> FILTER[MVC image action filter]
    FILTER --> ELIG[Eligibility and metadata lookup]
    ELIG --> STATE
    FILTER --> ORIG[Original Jellyfin image action]
    ORIG --> RENDER[Decode, draw, encode]
    ELIG --> RENDER
    RENDER --> CACHE[Bounded ephemeral rendered-image cache]
    CACHE --> RESP[Original or badged response]
```

The plugin has two related but separate paths:

1. **Reconciliation path:** obtains and fingerprints Arr metadata ahead of image
   requests. It is asynchronous, bounded, and restart-safe.
2. **Image path:** observes a Jellyfin image action, looks up the current
   fingerprint, and replaces only the HTTP response when a valid badge can be
   rendered. It never persists a generated image as Jellyfin artwork.

The image path is the canonical rendering strategy. An MVC action filter is the
preferred implementation because it operates on typed controller actions and
can replace the executed result. A version-pinned spike must confirm every
required image route variant, including indexed images and size/format query
parameters. If a `PhysicalFileResult` cannot be reliably replaced for a needed
route, the implementation may use an ASP.NET Core middleware fallback for that
route. This does not authorize direct modification of Jellyfin image storage.

## 4. Plugin components and boundaries

| Component | Responsibility | Boundary |
| --- | --- | --- |
| `Plugin` | Identity, configuration, data-folder ownership, uninstall hook | Thin `BasePlugin<PluginConfiguration>` entry point |
| Service registrator | Register services, hosted services, filters, controllers, and tasks | Parameterless `IPluginServiceRegistrator` |
| Configuration service | Validate and publish immutable configuration snapshots | Uses plugin configuration persistence; never exposes secrets |
| Sonarr client | Read Sonarr v3 resources and probe connections | `IHttpClientFactory`; no write endpoints |
| Radarr client | Read Radarr v3 resources and probe connections | `IHttpClientFactory`; no write endpoints |
| Matching service | Match a Jellyfin item to one configured Arr record | Provider IDs first; ambiguity is not auto-accepted |
| Metadata cache | Store current Arr snapshot and badge-relevant fingerprint | Versioned plugin-owned state under `DataFolderPath` |
| Reconciliation coordinator | Fetch, match, fingerprint, and enqueue affected items | Runs outside event handlers and image requests where possible |
| Work queue | Coalesce item work and bound memory/concurrency | Hosted service with cancellation-aware workers |
| Image action filter | Decide eligibility and replace image results | Only the image response is mutable |
| Badge renderer | Draw configured labels onto an already processed image | Bounded input/output and render concurrency |
| Rendered-image cache | Avoid repeated decode/draw/encode work | Ephemeral, bounded, fingerprint-keyed cache |
| Webhook controller | Accept authenticated low-latency Arr hints | Validates token and payload; never trusts payload as source of truth |
| Scheduled task | Manual and periodic full reconciliation | Cancellable, progress-reporting, retry-safe |

No component accesses Jellyfin database tables, image-cache directories,
`ImageSaver`, or concrete Jellyfin implementation types when a plugin-facing
API is available.

## 5. Plugin lifecycle and registration

### Startup

1. Jellyfin discovers the `BasePlugin<PluginConfiguration>` implementation.
2. Jellyfin constructs the parameterless service registrator before building
   the service provider.
3. The registrator registers configuration, API clients, caches, matching and
   rendering services, the hosted worker, scheduled task, webhook controller,
   and image filter registration.
4. The hosted service subscribes to library events and starts the bounded queue
   consumer after the host is ready.
5. Startup must not perform unbounded Arr requests or a full-library render
   synchronously.

### Shutdown and uninstall

- Unsubscribe library events during hosted-service shutdown.
- Stop accepting new work, cancel queued work, and await workers within host
  shutdown limits.
- Do not leave unmanaged threads or fire-and-forget tasks running.
- Plugin-owned cache/state may be removed on uninstall according to the plugin
  uninstall policy; Jellyfin artwork must not be altered because ArrTags never
  owns it.

## 6. Configuration and persisted state

### Configuration

`PluginConfiguration` is persisted through Jellyfin's plugin configuration
mechanism. Updates are treated as replacement snapshots, not as mutable objects
shared with background workers.

Configuration includes:

- Independently enabled Sonarr and Radarr connections.
- Base URL, API key, timeout, and explicit TLS exception policy per connection.
- Enabled libraries and eligible image/item types.
- Badge fields, placement, colors, scale, margins, output limits, and renderer
  version settings.
- Refresh interval, queue limits, cache limits, concurrency, and retry policy.
- Webhook token or equivalent secret for inbound Arr notifications.
- Jellyfin Enhanced coexistence and duplicate-badge policy.

API keys and webhook secrets must not appear in logs, status responses, cache
keys, fingerprints, or exception messages. TLS certificate validation is strict
by default; any exception is explicit and scoped to one connection.

### Plugin state

State is stored under `DataFolderPath`, not in Jellyfin's database or image
cache. The format is versioned and written atomically. Corrupt state is ignored
or quarantined so the plugin can rebuild it.

Each item record may contain:

- Jellyfin item ID and provider IDs.
- Arr connection identity and external record IDs.
- Last successful Arr metadata snapshot or normalized badge input.
- Badge-relevant metadata fingerprint.
- Jellyfin image tag/date fingerprint used for rendered-image invalidation.
- Renderer/configuration version.
- Last refresh, error, retry, and reconciliation status.

The metadata record and rendered-image cache have different purposes. A
metadata snapshot may be retained as last-known-good state during a temporary
Arr outage. Rendered image bytes are ephemeral and must be evictable; they are
not a replacement for original artwork.

## 7. Sonarr and Radarr integration

### HTTP clients

Use Jellyfin's `IHttpClientFactory` and a dedicated named or typed client per
Arr service. Requests use the configured base URL, `/api/v3`, `Accept:
application/json`, and `X-Api-Key`. The client must support cancellation,
finite timeouts, bounded retries for transient transport failures, redacted
logging, and tolerant JSON deserialization.

The integration is read-only. It must not call write endpoints, access Arr
databases, or infer success from an HTML login page or redirect.

### Matching policy

The initial matching order is:

- **Movie to Radarr:** Jellyfin TMDb ID, then IMDb ID. Use Radarr's local movie
  library endpoints; do not use lookup endpoints for routine refreshes.
- **Series to Sonarr:** Jellyfin TVDB ID, then a cached catalogue comparison of
  other stable provider IDs when available.
- **Episode to Sonarr:** episode TVDB ID, then exact season and episode numbers
  after the series match. Anime, specials, absolute numbering, double episodes,
  and multi-episode files require explicit policy and validation.
- **Path:** only a configured, normalized path mapping may use paths as a
  fallback. Container or host paths must never be assumed equivalent.
- **Title and year:** candidate or manual-disambiguation data only; never an
  automatic badge match when zero or multiple candidates remain.

Missing IDs, ambiguous matches, missing files, virtual items, and remote items
produce no new badge rather than a guessed badge.

### Metadata semantics

Badge inputs come from the current Arr file resource:

- Actual quality comes from the file's quality model.
- Technical values such as codec, dynamic range, audio, language, release
  group, and custom-format score come from the file resource where available.
- `qualityCutoffNotMet` is the authoritative upgrade-pending signal.
- Quality profiles describe requested policy and must not be presented as actual
  file quality.

Radarr's embedded movie file may omit custom-format fields; request the
  dedicated movie-file endpoint when those fields are enabled. Sonarr episode
  files must be joined using `episodeFileId == episodeFile.id`.

## 8. Reconciliation and update flow

Library event handlers are deliberately short:

1. Check whether the event is relevant to an enabled library/item type.
2. Ignore ArrTags-generated internal state changes where applicable.
3. Enqueue the Jellyfin item ID and a reason/version hint.
4. Return without external I/O, rendering, or image writes.

Work is coalesced by item ID and connection. A worker re-reads the item and
current configuration before processing. It then:

1. Resolves the applicable Sonarr/Radarr connection.
2. Matches the item using the policy above.
3. Fetches or reuses the current Arr record and file metadata.
4. Produces a normalized badge input and fingerprint.
5. Atomically publishes the metadata state.
6. Invalidates only affected rendered-cache entries.

The worker must re-check the item and relevant metadata before publishing a
result if the operation was long-running. A changed fingerprint causes stale
work to be discarded rather than published.

Refresh triggers are:

- Jellyfin `ItemAdded`, relevant `ItemUpdated`, and `ItemRemoved` events.
- Post-scan reconciliation.
- Manual and periodic scheduled reconciliation.
- Authenticated Sonarr/Radarr webhooks as low-latency hints.

Webhooks are accelerators, not the source of truth. Payloads are validated,
bounded, and converted into the same deduplicated work as other triggers. A
periodic reconciliation repairs missed or forged notifications.

## 9. Image response rendering

### Request flow

1. The image action filter identifies supported Jellyfin image actions and typed
   item/image arguments.
2. It checks configuration, item type, image type, library scope, and whether
   a current badge input exists.
3. It includes all output-affecting values in a render key:
   item ID, image type/index, request sizing/format parameters, Jellyfin image
   tag or modification fingerprint, metadata fingerprint, render configuration,
   and renderer version.
4. A cache hit returns the cached response with correct content type and cache
   validation behavior.
5. On a miss, the original image action runs. The filter reads the resulting
   image bytes or file, decodes the already resized image, draws badges, and
   returns the encoded result.
6. Any unsupported result, size limit, cancellation, decode failure, or render
   exception falls through to the original response.

The output is a response artifact only. ArrTags must never call
`IProviderManager.SaveImage`, `SetImage`, `UpdateToRepositoryAsync`, or
`IDynamicImageProvider` for badges. It must not write media-folder artwork or
Jellyfin's `resized-images` cache.

### Rendering constraints

- Preserve the requested dimensions and format policy; do not upscale a small
  source beyond Jellyfin 12 behavior.
- Cap input bytes, output bytes, decoded dimensions, and concurrent renders.
- Use a bounded cache under the plugin data folder or an explicitly bounded
  memory cache; clean up expired disk entries.
- Preserve or deliberately replace `ETag`, `Last-Modified`, `Cache-Control`,
  `Range`, and conditional-request behavior based on the final response. This
  requires integration tests through the exact supported Jellyfin 12 ABI.
- Do not block library scans or synchronous library event delivery.

## 10. Jellyfin Enhanced coexistence

ArrTags has no source-level dependency on Jellyfin Enhanced. Enhanced's quality
tags are client-side Web overlays; ArrTags' badges are server-side image
responses. Therefore:

- Native clients receive ArrTags badges without requiring the Web UI.
- Jellyfin Web may show both systems and duplicate information.
- ArrTags exposes a policy to disable or limit its server badges where needed.
- Documentation may recommend disabling overlapping Enhanced quality tags, but
  ArrTags does not alter Enhanced configuration.
- Spoiler Guard ordering and hidden/blurred image behavior must be tested; an
  ArrTags badge must not reveal information that a spoiler policy hides.

## 11. Failure, consistency, and security policy

Failures are isolated by layer:

- Arr connection failures preserve last-known-good metadata for a bounded
  period, then pass through the original image when no usable state remains.
- Authentication, malformed JSON, unsupported fields, and version drift are
  reported as connection/item status, not Jellyfin failures.
- No match or ambiguous match produces no badge.
- Renderer failures return the original image and record a rate-limited error.
- Queue overflow coalesces or drops redundant work; it never blocks a library
  event indefinitely.
- Cancellation prevents publication of partial state or partial image output.
- State writes are atomic and recoverable after crashes.

Inbound webhook endpoints require a configured shared secret or equivalent
boundary authentication, validate content size and payload shape, and rate-limit
or coalesce requests. They must not accept arbitrary item IDs as permission to
perform unbounded work.

## 12. Performance and operational limits

Implementation must define and test concrete defaults for:

- Maximum queue entries and per-item single-flight work.
- Maximum concurrent Sonarr/Radarr requests.
- Maximum concurrent image renders.
- HTTP timeout, retry count, and exponential backoff.
- Maximum input/output image bytes and decoded pixel dimensions.
- Metadata and rendered-cache size/TTL/eviction.
- Full-reconciliation page/batch size and cancellation behavior.
- Maximum stale-last-known-good duration.

Metrics or diagnostic status should distinguish queue depth, API health,
matching failures, cache hits/misses, render failures, and stale metadata
without exposing credentials or full external payloads.

## 13. Testing and validation gates

Before implementation is considered complete, validate against the exact
Jellyfin 12.x ABI and supported Arr versions.

### Unit tests

- Configuration validation and secret redaction.
- Tolerant Sonarr/Radarr DTO deserialization.
- Provider-ID matching, ambiguity, missing-file, and path-mapping behavior.
- Actual-versus-requested quality semantics.
- Fingerprint stability and invalidation.
- Queue coalescing, cancellation, retry, and state recovery.
- Badge layout, size limits, output format, and renderer failures.

### Integration tests

- Plugin discovery, DI registration, startup, shutdown, and scheduled task.
- Jellyfin item event delivery without blocking the event publisher.
- All supported image route variants, indexed images, requested sizes/formats,
  conditional requests, ranges, and non-200 pass-through responses.
- MVC filter replacement of `PhysicalFileResult`; middleware fallback only if
  the spike proves it is required.
- Arr authentication, URL bases, timeouts, outages, upgrades, missing files,
  and webhook authentication.
- Jellyfin Enhanced Quality Tags and Spoiler Guard enabled and disabled.
- Restart during reconciliation and rendering, corrupted state, and cache
  eviction.

### Acceptance checks

- Original Jellyfin artwork remains byte-for-byte untouched.
- Disabling ArrTags immediately restores the unmodified image response.
- A changed Arr file or badge configuration produces a new render key.
- Repeated unchanged requests do not repeatedly decode and encode the image.
- A failed external service or render never produces a broken Jellyfin image.
- Web, mobile, TV, Kodi, and other image-consuming clients receive the same
  server-rendered result where their image request is supported.

## 14. Decisions required before implementation

The following are intentionally not guessed by this architecture:

1. Exact Jellyfin 12 patch, package versions, target framework, and plugin
   manifest `targetAbi`.
2. Initial supported item/image types and whether series/season posters use an
   explicit aggregate policy or remain disabled.
3. Exact badge fields, text truncation, placement, color/contrast rules, output
   format, and requested-size policy.
4. Whether episode matching permits number fallback for all libraries or only
   validated display-order cases.
5. Whether configured path mappings are needed and how they are represented.
6. Default queue, concurrency, image-size, cache, timeout, retry, and stale
   state limits.
7. Webhook endpoint exposure and shared-secret administration flow.
8. Jellyfin Enhanced duplicate-badge defaults and Spoiler Guard behavior.
9. Supported live Sonarr/Radarr release ranges and optional-field compatibility.

These decisions must be recorded in a future architecture revision before they
become implementation assumptions.

## 15. Supporting research

- [Poster rendering strategies](poster-rendering-strategies.md) documents the
  inspected plugin approaches and the MVC-filter versus middleware tradeoff.
- [Media metadata mapping](media-metadata-mapping.md) documents Jellyfin item
  identity, provider IDs, file joins, versions, and matching caveats.
- [Sonarr API reference](sonarr-api.md) documents the read-only v3 integration,
  matching endpoints, episode/file joins, and webhook considerations.
- [Radarr API reference](radarr-api.md) documents the read-only v3 integration,
  movie/file endpoints, quality semantics, and webhook considerations.
- [Project goals](../GOALS.md) defines the product requirements and success
  criteria that this architecture must satisfy.
