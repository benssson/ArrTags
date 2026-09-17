# Canonical Data Model

**Status:** Draft V1 domain model

**Scope:** Internal domain objects exchanged between Jellyfin, Sonarr, Radarr,
the cache, and the badge-rendering pipeline.

This document defines conceptual models, not C# classes, API DTOs, or a storage
schema. Provider responses are translated into these models at the integration
boundary. The rest of ArrTags should not need to know whether a value came from
Sonarr or Radarr.

The model is intentionally compatible with the V1 architecture: ArrTags is
read-only against external services, original source artwork remains recoverable
through plugin-owned provenance, and a derived image may be published as
Jellyfin's active artwork through the supported item-image APIs.

## 1. Design Principles

### Provider-agnostic core

The matching, metadata, badge, render, and cache models use common concepts for
movies and television. Provider-specific identifiers and facts remain available
through typed provider references and extensions, but the renderer consumes
`BadgeMetadata`, not Sonarr or Radarr response objects.

### External models stop at the integration boundary

Sonarr and Radarr API resources, webhook payloads, and Jellyfin entities are
external models. They are parsed defensively and mapped to canonical models.
External field names, local database IDs, enum values, and optional response
shapes must not leak through the rest of the pipeline.

### Actual observations are separate from policy

Actual file quality comes from the current Arr file record. A quality profile is
requested policy and must not be represented as the actual quality badge.
`qualityCutoffNotMet` may be represented separately as an upgrade-pending
signal.

### Identity is explicit and scoped

Jellyfin IDs, provider IDs, Arr instance identity, and Arr local record IDs have
different scopes. A cache or match must retain enough scope to prevent a local
ID from being mistaken for an ID from another Arr connection.

### Immutable/value-style data

Canonical objects are conceptually immutable snapshots. A changed provider
response produces a new snapshot and fingerprint instead of mutating a snapshot
already being rendered. Small values such as quality, dimensions, fingerprints,
and colors should have value semantics.

### Missing is not false

Unavailable or unreported technical metadata is distinct from a confirmed
negative value. For example, absent media information means HDR is unknown, not
that the file is confirmed to be SDR.

### Deterministic rendering

All output-affecting inputs are represented in `RenderRequest` or its
fingerprints. The same source image, metadata, badge definition, renderer
version, and request parameters produce the same logical render result.

### Versioned evolution

Domain model, cache, and badge schema versions are explicit. Unknown future
metadata can be ignored by an older renderer, while a cache entry with an
incompatible schema can be discarded and rebuilt.

## 2. Model Relationships

```mermaid
classDiagram
    class MediaIdentity {
        +JellyfinItemId
        +ItemType
        +ProviderIds
    }
    class ArrProvider {
        +Kind
        +ProviderInstanceId
    }
    class ArrConnection {
        +ConnectionId
        +BaseUrl
        +Enabled
    }
    class MediaMatch {
        +Status
        +Method
        +RecordIdentity
    }
    class ArrRecordIdentity {
        +ConnectionId
        +ProviderKind
    }
    class SonarrIdentity {
        +SeriesId
        +EpisodeId
        +EpisodeFileIdentity
    }
    class RadarrIdentity {
        +MovieId
        +MovieFileIdentity
    }
    class ArrFileIdentity {
        +Presence
        +FileId
    }
    class BadgeMetadata {
        +BadgeSchemaVersion
        +Quality
        +Resolution
        +DynamicRange
        +Audio
        +Extensions
    }
    class BadgeDefinition {
        +DefinitionId
        +MetadataSelector
        +Style
        +Placement
    }
    class RenderRequest {
        +ImageSurface
        +SourceImageFingerprint
        +MetadataFingerprint
        +RendererVersion
    }
    class RenderResult {
        +Status
        +ContentType
        +BytesOrArtifact
        +ETag
    }
    class MetadataCacheEntry {
        +CacheKey
        +MetadataFingerprint
        +ExpiresAt
        +StaleUntil
    }
    class ArtworkCacheEntry {
        +RenderKey
        +ContentFingerprint
        +ExpiresAt
        +SizeBytes
    }
    class PublishedArtworkState {
        +ItemId
        +ImageSurface
        +State
        +SourcePresence
        +SourceArtifactId
        +SourceFingerprint
        +OwnershipToken
        +PublicationToken
        +ActiveImageIdentity
        +PublishedFingerprint
    }
    class ArtworkOperation {
        +OperationId
        +Kind
        +ImageSurface
        +Generation
        +ExpectedBeforeIdentity
        +Phase
        +LifecycleFence
    }
    class UpdateEvent {
        +EventType
        +Subject
        +Reason
        +InvalidationScope
    }
    class Configuration {
        +ConfigurationVersion
        +Connections
        +BadgeDefinitions
        +RenderingPolicy
    }

    MediaIdentity --> MediaMatch : matched item
    ArrConnection --> ArrProvider : uses
    ArrConnection --> MediaMatch : scoped match
    ArrConnection --> ArrRecordIdentity : scopes
    MediaMatch --> ArrRecordIdentity : record identity
    SonarrIdentity --> ArrRecordIdentity : is a
    RadarrIdentity --> ArrRecordIdentity : is a
    SonarrIdentity --> ArrFileIdentity : current episode file
    RadarrIdentity --> ArrFileIdentity : current movie file
    MediaMatch --> BadgeMetadata : supplies
    Configuration --> ArrConnection : configures
    Configuration --> BadgeDefinition : configures
    BadgeMetadata --> RenderRequest : input
    BadgeDefinition --> RenderRequest : selected definitions
    MediaIdentity --> RenderRequest : target
    RenderRequest --> RenderResult : produces
    MediaMatch --> MetadataCacheEntry : cached association
    BadgeMetadata --> MetadataCacheEntry : cached snapshot
    RenderRequest --> ArtworkCacheEntry : cache key
    RenderResult --> PublishedArtworkState : may publish
    RenderResult --> ArtworkOperation : stages
    ArtworkOperation --> PublishedArtworkState : commits
    UpdateEvent --> MediaMatch : invalidates
    UpdateEvent --> MetadataCacheEntry : invalidates
    UpdateEvent --> ArtworkCacheEntry : invalidates
    UpdateEvent --> PublishedArtworkState : restores or republishes
    UpdateEvent --> ArtworkOperation : recovers or cancels
```

`MediaIdentity` is the Jellyfin-side subject. `MediaMatch` connects it to one
scoped Arr record through a typed `ArrRecordIdentity` that distinguishes Sonarr
series/episode/file identity from Radarr movie/file identity.
`MetadataCacheEntry` retains the provider-derived snapshot;
`ArtworkCacheEntry` retains only bounded render work state. `PublishedArtworkState`
tracks the active derived image and the plugin-owned source-artwork provenance.
Jellyfin does not provide an artwork-owner field, so ownership is proven by the
combination of retained source bytes, a persisted expected active-image identity,
and ArrTags-generated publication/session tokens. A cache hit must never be
treated as a new source of truth when it is expired or incompatible.

## 3. Canonical Domain Models

### 3.1 MediaIdentity

**Purpose:** The canonical identity and minimal media context for one Jellyfin
item. It is used for matching and cache scoping, not as a copy of the complete
Jellyfin entity.

| Field | Type | Required | Field Ownership | Notes |
| --- | --- | --- | --- | --- |
| `jellyfinItemId` | Jellyfin item identifier | Yes | Jellyfin | Stable only within the Jellyfin server; use as the local subject key. |
| `itemType` | `Movie`, `Series`, `Season`, `Episode`, or supported video type | Yes | Jellyfin | Determines which provider and matching policy applies. |
| `libraryId` | Jellyfin library identifier | Optional | Jellyfin | Used for library eligibility and configuration scope. |
| `providerIds` | Map of provider name to external identifier | Optional | Jellyfin | Prefer normalized TMDb, IMDb, and TVDB IDs; absence is valid. |
| `title` | Display title | Optional | Jellyfin | Candidate or diagnostic data only; not sufficient identity. |
| `productionYear` | Integer year | Optional | Jellyfin | Tie-breaker or diagnostics; never sole proof of a match. |
| `seriesIdentity` | Parent series identity reference | Optional | Jellyfin | Used for seasons and episodes. |
| `seasonNumber` | Integer | Optional | Jellyfin | Relevant to seasons and episodes; season zero represents specials where applicable. |
| `episodeNumber` | Integer | Optional | Jellyfin | Primary episode number after Jellyfin numbering policy is applied. |
| `episodeNumberEnd` | Integer | Optional | Jellyfin | End of a multi-episode span. |
| `mediaLocation` | File/remote/virtual location summary | Optional | Jellyfin | Helps reject virtual or unsupported remote items without becoming an Arr match key. |
| `sourceFingerprint` | Opaque image/media identity fingerprint | Optional | Generated from Jellyfin | Changes when relevant Jellyfin source identity changes; not the provider metadata fingerprint. |

### 3.2 ArrProvider

**Purpose:** A provider type and provider-instance identity that can supply
metadata without exposing provider-specific API response types.

| Field | Type | Required | Field Ownership | Notes |
| --- | --- | --- | --- | --- |
| `kind` | `Sonarr` or `Radarr` | Yes | Plugin | The supported provider family. |
| `providerInstanceId` | Opaque instance identifier | Yes | Plugin/configuration | Identifies one configured server, not an Arr local record. Must not contain an API key. |
| `displayName` | String | Optional | Configuration | Human-readable administrative name. |
| `applicationVersion` | Version string | Optional | Provider, cached | Observed during connection probing; informational and feature-gating only. |
| `apiContract` | Contract identifier, such as `v3` | Yes | Provider/configuration | Both initial integrations use the Arr v3 API contract. |
| `capabilities` | Set of supported capability names | Optional | Derived from provider/version | Allows optional metadata fields without changing the core model. |

### 3.3 ArrConnection

**Purpose:** The configuration and identity of one Sonarr or Radarr server.
Secrets are configuration inputs but are deliberately not copied into match,
metadata, event, or cache identity values.

| Field | Type | Required | Field Ownership | Notes |
| --- | --- | --- | --- | --- |
| `connectionId` | Opaque stable identifier | Yes | Plugin/configuration | Used to scope provider records and cache keys. |
| `provider` | ArrProvider | Yes | Plugin/configuration | Identifies Sonarr versus Radarr and the configured instance. |
| `baseUrl` | Absolute URL | Yes when enabled | Configuration | Normalized URL base without an API path; safe to show only according to admin policy. |
| `enabled` | Boolean | Yes | Configuration | Disabled connections are not queried. |
| `requestTimeout` | Duration | Yes | Configuration | Finite request limit. |
| `tlsPolicy` | TLS validation policy | Yes | Configuration | Strict by default; exceptions are explicit and connection-scoped. |
| `secretReference` | Opaque secret reference | Optional | Configuration | References the protected API key; the secret value is never part of this domain snapshot. |
| `health` | Connection health state | Optional | Generated/cached | `Healthy`, `Unavailable`, `AuthenticationFailed`, `Incompatible`, or `Unknown`. |
| `lastProbedAt` | Timestamp | Optional | Generated/cached | Time of the latest connection/version probe. |

### 3.4 MediaMatch

**Purpose:** The validated relationship between a Jellyfin item and an Arr
record. A match is scoped to a connection and may be unresolved, ambiguous, or
invalid rather than silently guessed.

| Field | Type | Required | Field Ownership | Notes |
| --- | --- | --- | --- | --- |
| `mediaIdentity` | MediaIdentity reference | Yes | Jellyfin/plugin | The Jellyfin subject being matched. |
| `provider` | ArrProvider reference | Yes | Plugin | Provider family and instance scope. |
| `connectionId` | Opaque connection identifier | Yes | Plugin/configuration | Prevents cross-instance local-ID collisions. |
| `status` | `Matched`, `NotFound`, `Ambiguous`, `Unsupported`, or `Stale` | Yes | Generated | Only `Matched` permits provider metadata to be used for a badge. |
| `matchMethod` | Provider ID, number, configured path, manual, or none | Yes | Generated | Records how the result was established. |
| `recordIdentity` | Typed, connection-scoped Arr record/file identity | Required when `Matched` | Sonarr/Radarr, cached | Provider-neutral envelope holding one concrete `SonarrIdentity` or `RadarrIdentity`; see section 3.4.1. |
| `matchedProviderIds` | Map of IDs used for validation | Optional | Jellyfin + provider | Records the stable IDs that agreed; useful for diagnostics and invalidation. |
| `pathValidation` | Path comparison result | Optional | Generated | Only meaningful when an explicit path mapping is configured. |
| `matchedAt` | Timestamp | Optional | Generated | Last successful validation time. |
| `matchFingerprint` | Opaque fingerprint | Yes | Generated | Changes when the association or any scoped identity component (record ID, episode ID, or file identity presence/value) changes. |
| `ambiguityReason` | String/code | Optional | Generated | Safe explanation for skipped matches; never contains credentials. |

#### 3.4.1 Arr record and file identity

**Purpose:** A typed, connection-scoped representation of the Arr record and
current file a match resolves to. It replaces the former singular opaque record
identifier so a Sonarr episode carries its series, episode, and current-file
identity without ambiguity. These are canonical model types, never provider
DTOs.

Every `ArrRecordIdentity` is scoped to exactly one `ArrConnection`
(`connectionId`) and one provider kind. The same local record/file ID value on
two connections is never the same identity.

| Field | Type | Required | Field Ownership | Notes |
| --- | --- | --- | --- | --- |
| `connectionId` | Opaque connection identifier | Yes | Plugin/configuration | Scope for every contained local ID. |
| `providerKind` | `Sonarr` or `Radarr` | Yes | Plugin | Selects exactly one concrete identity shape. |

Concrete identity shapes are never mixed or substituted across providers.

**SonarrIdentity**

| Field | Type | Required | Field Ownership | Notes |
| --- | --- | --- | --- | --- |
| `seriesId` | Sonarr series local ID | Yes for a matched series or episode | Sonarr, cached | The series record the episode belongs to; distinct from an episode TVDB ID. |
| `episodeId` | Sonarr episode local ID | Yes for a matched episode | Sonarr, cached | The matched episode; distinct from the series ID and from the episode TVDB ID. |
| `episodeFileIdentity` | `ArrFileIdentity` | Yes for a matched episode | Sonarr, cached | Current file from the `episode.episodeFileId == episodeFile.id` join; explicit `Absent` when the episode has no current file. |

A series match carries `seriesId` only. An episode match always distinguishes
the series from the episode and from the current file; an episode cannot be
represented with only a series ID or only a file ID.

**RadarrIdentity**

| Field | Type | Required | Field Ownership | Notes |
| --- | --- | --- | --- | --- |
| `movieId` | Radarr movie local ID | Yes | Radarr, cached | The matched movie record. |
| `movieFileIdentity` | `ArrFileIdentity` | Yes | Radarr, cached | Current `movieFileId`; explicit `Absent` when the movie has no imported file. |

This preserves the existing Radarr movie/file identity shape: at most one
current file per movie.

**ArrFileIdentity**

| Field | Type | Required | Field Ownership | Notes |
| --- | --- | --- | --- | --- |
| `presence` | `Present` or `Absent` | Yes | Sonarr/Radarr, cached | Missing file identity is explicit; never encoded as `0`, `-1`, `null`, or an empty string. |
| `fileId` | Arr file local ID | Required when `presence` is `Present` | Sonarr/Radarr, cached | Sonarr `episodeFile.id` or Radarr `movieFile.id`; absent (never zero) when `presence` is `Absent`. |

**Shared Sonarr episode files.** A Sonarr episode-file identity is not owned by
one episode. Multiple episode identities may reference the same `fileId`, and a
`SonarrIdentity` must not imply a one-to-one episode-to-file relationship. Code
that reads `episodeFileIdentity` resolves the file by ID and must not assume the
file belongs exclusively to the matched episode.

**Fingerprints.** `matchFingerprint` and `metadataFingerprint` include the
connection ID, provider kind, every present identity component (`seriesId`,
`episodeId`, `RadarrIdentity.movieId`, and each `ArrFileIdentity.fileId`), and
each explicit `presence` value. A changed file ID, an `Absent`-to-`Present`
transition, or a matching change on another connection changes the dependent
fingerprint; no identity component is omitted.

**DTO boundary.** These are canonical identity values. Sonarr `SeriesResource`,
`EpisodeResource`, and `EpisodeFileResource` and Radarr `MovieResource` and
`MovieFileResource` remain integration-boundary DTOs and never appear in, or
replace, these types.

### 3.5 BadgeMetadata

**Purpose:** Provider-agnostic, badge-relevant observations for the current
matched media file. It is the renderer's metadata input. Each optional technical
value supports an explicit unknown state.

| Field | Type | Required | Field Ownership | Notes |
| --- | --- | --- | --- | --- |
| `badgeSchemaVersion` | Version identifier | Yes | Plugin | Version of the normalized metadata shape. |
| `provider` | ArrProvider reference | Yes | Plugin/provider | Identifies the source without exposing its DTO. |
| `recordIdentity` | Typed Arr record/file identity reference | Yes | Sonarr/Radarr + generated | Carries the connection-scoped `SonarrIdentity` or `RadarrIdentity`, including explicit file-identity presence; see section 3.4.1. |
| `observedAt` | Timestamp | Yes | Provider/generated | When the source observation was obtained. |
| `quality` | Quality descriptor | Optional | Sonarr/Radarr | Actual file quality label and structured source/resolution/modifier; never the quality profile target. |
| `resolution` | Resolution descriptor | Optional | Sonarr/Radarr or Derived | Prefer inspected media dimensions; retain whether the value is reported or derived. |
| `dynamicRange` | Dynamic-range descriptor | Optional | Sonarr/Radarr | HDR, HDR10, HDR10+, HLG, PQ, or unknown. |
| `dolbyVision` | Tri-state flag or descriptor | Optional | Sonarr/Radarr | Dolby Vision is distinct from generic HDR; unknown is not false. |
| `videoCodec` | String | Optional | Sonarr/Radarr | Normalized codec where the provider reports it. |
| `audioCodec` | String | Optional | Sonarr/Radarr | Normalized codec, such as TrueHD, DTS-HD MA, EAC3, AAC, or unknown. |
| `audioChannels` | Decimal number | Optional | Sonarr/Radarr | Channel count; do not infer Atmos solely from channel count. |
| `audioFeatures` | Set of `Atmos`, `DTS`, `DTS-HD`, `DTS-X`, or future feature names | Optional | Sonarr/Radarr or Derived | Feature detection must retain unknown when source data is incomplete. |
| `source` | Source descriptor | Optional | Sonarr/Radarr | Release source such as web, WEB-DL, WEBRip, HDTV, Blu-ray, or remux. |
| `upgradePending` | Tri-state flag | Optional | Sonarr/Radarr | Maps to `qualityCutoffNotMet`; it describes policy state, not observed quality. |
| `customBadges` | Ordered set of custom metadata values | Optional | Provider/configuration | Custom-format names or configured provider values, bounded and sanitized. |
| `extensions` | Namespaced extension map | Optional | Additional providers/generated | Future metadata that an older renderer may ignore. |
| `metadataFingerprint` | Opaque fingerprint | Yes | Generated | Includes every record/file identity component, badge-affecting values, and their schema version. |

#### Value conventions

`Quality descriptor` contains a display label, source classification, numeric
resolution when known, modifier/revision when relevant, and an optional provider
local quality identifier. The provider local ID is for equality within one
connection only.

`Resolution descriptor` contains width, height, display label, and an origin
such as provider quality, provider media info, Jellyfin media stream, or
derived. A quality parser's resolution and stream dimensions are related but
not interchangeable.

`Dynamic-range descriptor` contains a normalized family, optional profile name,
and a confidence/origin state. Missing media information is `Unknown`.

`Custom badges` are data values, not arbitrary executable markup. The renderer
decides whether a configured value is displayable.

### 3.6 BadgeDefinition

**Purpose:** Visual and selection rules for one badge, independent of the
metadata source. Definitions are configuration snapshots and do not contain
the current item's metadata.

| Field | Type | Required | Field Ownership | Notes |
| --- | --- | --- | --- | --- |
| `definitionId` | Opaque identifier | Yes | Configuration | Stable identity within configuration. |
| `definitionVersion` | Version identifier | Yes | Configuration | Changes when selection or visual behavior changes. |
| `enabled` | Boolean | Yes | Configuration | Disabled definitions produce no badge. |
| `metadataSelector` | Provider-neutral selector | Yes | Configuration | Selects quality, resolution, HDR, audio, source, custom value, or a future extension. |
| `fallbackPolicy` | Hide, placeholder, or alternate selector | Yes | Configuration | Must not turn unknown metadata into a false claim. |
| `textTemplate` | Bounded display template | Optional | Configuration | Formatting rule after values are normalized; no provider DTO paths. |
| `style` | Badge style value | Yes | Configuration | Color, opacity, border, text, font, and contrast policy. |
| `placement` | Placement value | Yes | Configuration | Anchor, order, margins, and scale. |
| `visibilityPolicy` | Image/item/client surface policy | Yes | Configuration | V1 is poster-oriented and not user-specific. |
| `customValueRules` | Optional bounded rules | Optional | Configuration | Maps approved custom metadata to a visual value. |

### 3.7 RenderRequest

**Purpose:** Complete logical input for generating one derived poster image for
publication through Jellyfin's item-image APIs. It represents the selected
source artwork plus all output-affecting metadata and configuration.

| Field | Type | Required | Field Ownership | Notes |
| --- | --- | --- | --- | --- |
| `requestId` | Opaque correlation identifier | Yes | Generated | Diagnostic only; not part of the output identity. |
| `mediaIdentity` | MediaIdentity reference | Yes | Jellyfin/plugin | Target item. |
| `imageSurface` | Image type and optional index | Yes | Jellyfin/request | V1 normally supports poster/primary surfaces; other surfaces require explicit policy. |
| `sourceImage` | Original source image handle/bytes | Yes at render time | Jellyfin/plugin | Source used to create derived artwork; the source is not overwritten by the renderer. |
| `sourceImageFingerprint` | Source image identity fingerprint | Yes | Jellyfin/generated | Changes when the retained or newly validated source artwork changes. |
| `renderParameters` | Format, quality, dimensions, and relevant output values | Yes | Configuration/generated | Included when they affect the persisted derived image. |
| `match` | MediaMatch | Yes | Generated/cache | Only a valid matched state supplies metadata. |
| `metadata` | BadgeMetadata | Optional | Sonarr/Radarr/cache | Absent metadata means no new publication or configured no-badge behavior. |
| `badgeDefinitions` | Ordered definitions | Yes | Configuration | Snapshot used for this render. |
| `configurationFingerprint` | Opaque fingerprint | Yes | Generated from configuration | Includes output-affecting settings, not secrets. |
| `rendererVersion` | Version identifier | Yes | Plugin | Changes when rendering behavior changes. |
| `outputPolicy` | Format, size, and limit policy | Yes | Configuration/generated | Includes bounded input/output and cancellation constraints. |

### 3.8 RenderResult

**Purpose:** The result of rendering or the safe decision to leave the current
active artwork unchanged.

| Field | Type | Required | Field Ownership | Notes |
| --- | --- | --- | --- | --- |
| `status` | `Rendered`, `PassThrough`, `NotEligible`, `CacheHit`, or `Failed` | Yes | Generated | Failure and ineligibility are normal domain outcomes, not Jellyfin failures. |
| `outputArtifact` | Image bytes or bounded artifact reference | Optional | Generated/cache | Present for completed render work; may be published as Jellyfin active artwork through the supported image API. |
| `contentType` | MIME type | Optional | Generated/Jellyfin | Must describe the returned artifact. |
| `width` / `height` | Integer dimensions | Optional | Generated/Jellyfin | Actual output dimensions. |
| `outputFingerprint` | Opaque fingerprint | Yes | Generated | Covers source, metadata, definitions, render values, and renderer version. |
| `etag` | Internal artifact identity, if useful | Optional | Generated | Jellyfin owns the validator for the published item image. |
| `createdAt` | Timestamp | Yes | Generated | Creation time of this result/artifact. |
| `passThroughReason` | Safe reason code | Optional | Generated | Used for diagnostics and metrics without exposing provider secrets. |
| `expiresAt` | Timestamp | Optional | Cache policy/generated | Applies when the result is cacheable. |

### 3.9 MetadataCacheEntry

**Purpose:** A versioned, restart-safe logical cache record for a match and its
last-known-good provider metadata. It is not a storage schema.

| Field | Type | Required | Field Ownership | Notes |
| --- | --- | --- | --- | --- |
| `cacheVersion` | Version identifier | Yes | Plugin | Incompatible entries can be rebuilt. |
| `cacheKey` | Opaque key | Yes | Generated | Includes Jellyfin item, connection, provider, and every typed record/file identity component; excludes secrets. |
| `mediaIdentity` | MediaIdentity reference | Yes | Cached from Jellyfin | The local subject. |
| `match` | MediaMatch | Yes | Cached/generated | Includes the scoped typed Arr record/file identity from section 3.4.1. |
| `metadata` | BadgeMetadata | Optional | Cached from Sonarr/Radarr | Last successful normalized snapshot. |
| `metadataFingerprint` | Opaque fingerprint | Optional | Generated | Determines whether artwork invalidation is required. |
| `providerVersion` | Version string | Optional | Cached from provider | Helps diagnose version drift and optional field availability. |
| `providerVersionToken` | ETag or provider revision token | Optional | Provider, cached | Use only when observed and validated; absence is normal. |
| `fetchedAt` | Timestamp | Optional | Generated | Last successful source fetch. |
| `expiresAt` | Timestamp | Optional | Cache policy/generated | Freshness boundary. |
| `staleUntil` | Timestamp | Optional | Cache policy/generated | Last-known-good may be used until this bounded time during outages. |
| `state` | Fresh, stale, unmatched, unavailable, or invalid | Yes | Generated | State is explicit so stale data is not mistaken for current data. |
| `lastError` | Domain error summary | Optional | Generated | Redacted, bounded, and non-secret. |

### 3.10 ArtworkCacheEntry

**Purpose:** A bounded, evictable work record for one rendered image before or
around publication. It has no authority over metadata or publication state.

| Field | Type | Required | Field Ownership | Notes |
| --- | --- | --- | --- | --- |
| `cacheVersion` | Version identifier | Yes | Plugin | Allows cache format changes without migration assumptions. |
| `renderKey` | Opaque key | Yes | Generated | Includes item, surface, source image fingerprint, metadata/config fingerprints, and renderer version. |
| `outputFingerprint` | Opaque fingerprint | Yes | Generated | Fingerprint of the output-affecting inputs. |
| `artifact` | Image bytes or artifact reference | Yes | Generated/cache | Bounded and evictable; storage mechanism is intentionally unspecified. |
| `contentType` | MIME type | Yes | Generated | Content type of the artifact. |
| `width` / `height` | Integer dimensions | Yes | Generated | Dimensions of the cached representation. |
| `sizeBytes` | Integer | Yes | Generated | Used for resource limits and eviction. |
| `etag` | Internal artifact identity, if useful | Optional | Generated | Jellyfin owns the validator for the published item image. |
| `createdAt` | Timestamp | Yes | Generated | Creation time. |
| `lastAccessedAt` | Timestamp | Optional | Generated | Eviction accounting. |
| `expiresAt` | Timestamp | Yes | Cache policy/generated | Expired entries are not served. |

### 3.10.1 PublishedArtworkState

**Purpose:** The conceptual state connecting plugin-owned source-artwork
provenance to a derived image currently published as Jellyfin item artwork. It
is not a Jellyfin storage schema and does not require the original source to be
held in the canonical metadata snapshot. It is the authority for publication
ownership and guarded restoration; Jellyfin image metadata alone is not enough.

| Field | Type | Required | Field Ownership | Notes |
| --- | --- | --- | --- | --- |
| `modelVersion` | Version identifier | Yes | Plugin | Changes to ownership semantics invalidate or migrate the record. |
| `jellyfinItemId` | Jellyfin item identifier | Yes | Jellyfin/plugin | Local subject whose active image may be derived. |
| `imageSurface` | Image type and optional index | Yes | Jellyfin/configuration | V1 surface selected by the image policy. |
| `state` | `NotPublished`, `Published`, `OwnershipLost`, `OwnershipUnknown`, `RestorePending`, `Restored`, `RestoreBlocked`, or `Removed` | Yes | Generated | Controls whether ArrTags may publish, restore, or remove the active image. |
| `sourcePresence` | `Present` or `Absent` | Yes when a publication session exists | Generated | `Absent` means the surface had no image before the session. |
| `sourceArtifactId` | Opaque retained-artifact identifier | Required when source is present | Generated | Content-addressed plugin-owned artifact; never a Jellyfin cache path or media path. |
| `sourceFingerprint` | SHA-256 of retained source bytes | Required when source is present | Generated | Hashes the exact bounded bytes used for rendering and restoration. |
| `sourceCaptureIdentity` | ActiveImageIdentity | Required for a publication session | Generated | Identity observed immediately before the first ArrTags publication, including an explicit absent baseline. |
| `ownershipToken` | Opaque random token | Required for an active session | Generated | Stable for one original-to-derived session; not a Jellyfin token. |
| `publicationToken` | Opaque random token | Required when published | Generated | New for each successful ArrTags-derived publication. |
| `activeImageIdentity` | ActiveImageIdentity | Required when published | Jellyfin/plugin | Expected identity of the currently active ArrTags image; re-observe before mutation. |
| `publishedFingerprint` | Opaque logical publication fingerprint | Required when published | Generated | Includes source, metadata, configuration, renderer, and schema inputs; not ownership proof alone. |
| `rendererVersion` | Version identifier | Yes when published | Plugin | Changes require regeneration. |
| `lastOwnershipObservation` | ActiveImageIdentity plus result | Yes | Generated | Latest `Owned`, `Changed`, or `Unknown` comparison for diagnostics and restart revalidation. |
| `stateRevision` | Monotonic integer | Yes | Generated | Increments for each committed state transition on the item/surface. |
| `lastOperationId` | Opaque operation identifier | Optional | Generated | Links the committed state to the durable artwork operation that produced it. |
| `updatedAt` | Timestamp | Yes | Generated | Last publication or restoration-state change. |

`sourceArtifactId` identifies an immutable artifact stored under the ArrTags data
folder. The artifact contains the exact source bytes, MIME type, byte length, and
integrity metadata. Its path is an implementation detail and is not an ownership
signal. The artifact remains retained while the session is `Published` or
`RestorePending`; after `OwnershipLost`, it may only be removed by bounded
retention cleanup and must never be used for automatic restoration.

`ActiveImageIdentity` is the observable identity of one Jellyfin image surface:

| Field | Type | Semantics |
| --- | --- | --- |
| `imageSurface` | Image type and index | Prevents an image on another surface or index from satisfying the comparison. |
| `presence` | `Present` or `Absent` | Absence is an identity value, not an error. |
| `contentSha256` | SHA-256 | Required for a `Present` ownership proof; hashes the bounded active representation used for comparison. |
| `byteLength` | Integer | Supporting evidence and integrity check for the content hash. |
| `width` / `height` | Integer | Supporting Jellyfin image metadata. |
| `dateModifiedUtc` | Timestamp | Supporting Jellyfin image metadata; not sufficient by itself. |
| `jellyfinImageTag` | Opaque string | Supporting Jellyfin cache/representation validator when available; not an ArrTags ownership token. |

The comparison is fail-closed. The surface and presence must match, the active
content hash must match, and every Jellyfin identity value recorded at
publication must still match when observable. A missing required hash or an
otherwise unavailable observation produces `OwnershipUnknown`, not ownership. A
path is never sufficient evidence because Jellyfin may reuse a path for a
different image. The Jellyfin image tag is useful for detecting replacement but
is not content- or actor-specific and cannot prove ownership on its own.

### 3.10.2 PublishedArtworkState transitions

These are ownership transitions. Crash-consistent ordering and restart
reconciliation are defined separately by `ArtworkOperation` in section 3.10.3.

| Current state | Condition | Next state | Required behavior |
| --- | --- | --- | --- |
| `NotPublished` or `Restored` | A new publication is eligible | `Published` | Observe the surface, retain its exact source or record `Absent`, create a new ownership token, and publish only if the baseline is recoverable. |
| `Published` | Current identity exactly matches `activeImageIdentity` | `Published` | Reuse the original source artifact; do not capture the ArrTags image as a new source. A changed publication gets a new publication token and active identity while retaining the same ownership token and source artifact. |
| `Published` | Current identity differs from the expected identity | `OwnershipLost` | Treat the change as external, regardless of whether it was a user, Jellyfin, provider, or another plugin. Do not publish, restore, or remove automatically. |
| `Published` | Current identity cannot be completely observed or compared | `OwnershipUnknown` | Leave the active image unchanged and perform no guarded mutation. |
| `Published` | Disable or uninstall requests restoration | `RestorePending` | Re-observe the active image immediately before any restoration mutation. |
| `RestorePending` | Expected identity matches and source artifact passes integrity validation | `Restored` | Restore the retained source, or remove the ArrTags image when `sourcePresence` is `Absent`, then verify the resulting surface. |
| `RestorePending` | Identity differs or cannot be proven | `OwnershipLost` or `OwnershipUnknown` | Do not mutate the active image. Retain the diagnostic state and artifact subject to bounded cleanup. |
| `RestorePending` | Source artifact is missing or corrupt | `RestoreBlocked` | Leave the active image unchanged and do not claim restoration completed. |
| `RestorePending` | The restoration result cannot be verified | `RestoreBlocked` | Do not claim completion or perform another automatic mutation; the associated operation enters `RecoveryBlocked` until reconciled. |
| Any state | Jellyfin item is removed | `Removed` | Perform no image operation against the missing item; clean plugin-owned records/artifacts only under the retention policy. |

An `OwnershipLost`, `OwnershipUnknown`, or `RestoreBlocked` record is never
automatically re-baselined. A future explicit administrative re-baseline, if
supported, starts a new ownership session and captures the then-current image;
it does not silently reclaim a later image.

### 3.10.3 ArtworkOperation

**Purpose:** A durable write-ahead operation record for one publication or
restoration attempt. It bridges the non-transactional Jellyfin image APIs and
the plugin-owned `PublishedArtworkState`. It is not an evictable render-cache
entry and must remain available until the operation reaches a terminal state and
its artifacts are safe to clean up.

Only one non-terminal operation may exist for an item/image surface at a time.
The operation generation fences stale queued work from publishing or finalizing
after a newer operation or lifecycle fence has been accepted.

| Field | Type | Required | Field Ownership | Notes |
| --- | --- | --- | --- | --- |
| `modelVersion` | Version identifier | Yes | Plugin | Invalid operation records are quarantined and never replayed blindly. |
| `operationId` | Opaque random identifier | Yes | Generated | Correlates the journal, staged artifacts, diagnostics, and final state. |
| `kind` | `Publication` or `Restoration` | Yes | Generated | Selects the target state and artifact use. |
| `jellyfinItemId` | Jellyfin item identifier | Yes | Jellyfin/plugin | Operation subject. |
| `imageSurface` | Image type and optional index | Yes | Jellyfin/configuration | Operation scope; must match both image identities. |
| `generation` | Monotonic per-item/surface value | Yes | Generated | Prevents stale work from becoming authoritative. |
| `ownershipToken` | Opaque token | Yes for an owned session | Generated | Carries the Blocker 1 ownership session through recovery. |
| `priorPublicationToken` | Opaque token | Optional | Generated | Expected prior ArrTags publication when replacing an owned image. |
| `publicationToken` | Opaque token | Required for publication | Generated | Candidate publication identity to commit if the postcondition matches. |
| `expectedBeforeIdentity` | ActiveImageIdentity | Yes | Generated/Jellyfin observation | Exact precondition observed before the external image mutation. |
| `candidateAfterContentSha256` | SHA-256 | Required when the after target is present | Generated | Hash of the durable artifact intended to become active; an absent after target records `presence = Absent`; the complete after identity is learned by readback. |
| `observedAfterIdentity` | ActiveImageIdentity | Optional | Jellyfin observation | Recorded only after the effective active image is re-observed. |
| `sourceArtifactId` | Opaque artifact identifier | Required when source is present | Generated | Retained original used by publication or restoration. |
| `derivedArtifactId` | Opaque artifact identifier | Required for publication | Generated | Durable, validated render output; never only an evictable cache entry. |
| `phase` | ArtworkOperationPhase | Yes | Generated | Durable lower-bound marker for external side effects. |
| `lifecycleFence` | Normal, Disable, Uninstall, or ItemRemoved | Yes | Generated | Prevents new publication work during lifecycle transitions. |
| `attempt` | Non-negative integer | Yes | Generated | Bounded recovery/retry accounting. |
| `lastError` | Redacted error summary | Optional | Generated | Diagnostics only; never a provider secret or raw payload. |
| `createdAt` / `updatedAt` | Timestamp | Yes | Generated | Journal lifecycle timestamps. |

`ArtworkOperationPhase` has these values:

| Phase | Meaning |
| --- | --- |
| `Prepared` | Source/derived artifacts and the complete operation intent are durable; no external image mutation has been started by this operation. |
| `MutationStarted` | The operation has durably recorded that `SaveImage` or the supported image-removal operation may have started. A crash before or during the call is treated as uncertain. |
| `RepositoryUpdateStarted` | The image mutation may have completed and the durable item update may have started. Both effects are re-observed during recovery. |
| `VerificationPending` | The supported item-image state and effective image representation must be read again before finalization. |
| `FinalizationPending` | The intended postcondition was observed; the final `PublishedArtworkState` or `Restored` state must be durably committed. |
| `Committed` | Final plugin state is durable and references the operation; cleanup may proceed under the artifact rules. |
| `Aborted` | The operation was safely stopped without adopting its candidate result, usually because the before identity no longer matched or the item was removed. |
| `RecoveryBlocked` | The operation or required observation is corrupt, unavailable, or ambiguous; no further automatic image mutation is permitted. |

The phase is deliberately a lower-bound marker. Recovery must assume that an
external call may have happened whenever the phase is `MutationStarted` or
later, even if the corresponding acknowledgment was not persisted. A durable
operation record is written before the first external image mutation. Operation
records, state records, and artifact manifests use versioned integrity metadata,
stable-storage flushing, and atomic replacement; a torn or invalid record is
quarantined rather than guessed.

Staged artifacts are promoted from temporary files only after bounded size,
format, and hash validation. They are retained until the operation is committed,
aborted, or tombstoned as removed, and cleanup must first prove that the
artifact is not the current active image. If that proof is unavailable, the
artifact is retained or quarantined.

### 3.10.4 ArtworkOperation transitions and recovery

| Phase or observation | Recovery action |
| --- | --- |
| `Prepared` and current identity equals `expectedBeforeIdentity` | Revalidate the generation and lifecycle fence, then resume the deterministic operation. |
| Any mutation-started phase and current identity equals `expectedBeforeIdentity` | The mutation did not become observable or the image was restored to the exact expected identity. Revalidate immediately and retry the same operation only under the current generation; never recapture a new source. |
| Any phase and current identity matches `observedAfterIdentity` or a validated candidate after identity | Ensure the normal Jellyfin item update is persisted, commit the target plugin state, then mark the operation `Committed`. |
| Any phase and current identity differs from both before and after identities | Mark the ownership state `OwnershipLost` when the image is observable, abort the operation, and never restore or remove the external image. |
| Any phase and the current identity cannot be observed or compared | Mark `OwnershipUnknown` or `RecoveryBlocked`, leave the image untouched, and require later reconciliation or explicit administration. |
| Any phase and the Jellyfin item is absent | Write an `ItemRemoved` tombstone, perform no image mutation, and retain cleanup records until the removal decision is durable. |
| Final state is durable but journal is non-terminal | Treat the final state and verified active identity as authoritative, mark the operation `Committed`, and perform only safe cleanup. |

Recovery runs under the same per-item/image-surface serialization as normal
publication and completes before new work for that subject is accepted. It is
postcondition-based rather than a distributed transaction: Jellyfin and the
plugin cannot be committed atomically, so uncertainty always resolves toward
preserving the currently observable artwork.

### 3.11 UpdateEvent

**Purpose:** A bounded invalidation or reconciliation hint that enters the
deduplicated update pipeline. Events carry enough subject information to enqueue
work but are not trusted as the provider source of truth.

| Field | Type | Required | Field Ownership | Notes |
| --- | --- | --- | --- | --- |
| `eventId` | Opaque identifier | Yes | Generated/external | Used for correlation and duplicate handling where available. |
| `eventType` | Metadata changed, artwork publication requested, artwork published, item removed, configuration changed, or cache invalidated | Yes | Generated | Canonical event vocabulary; external event names are mapped into it. |
| `source` | Jellyfin, Sonarr, Radarr, plugin, or scheduled task | Yes | Generated/external | Identifies the triggering boundary. |
| `occurredAt` | Timestamp | Yes | External/generated | Provider event time when trustworthy, otherwise receipt time. |
| `jellyfinItemId` | Jellyfin item identifier | Optional | Jellyfin/generated | Preferred local target when known. |
| `connectionId` | Arr connection identifier | Optional | Configuration/generated | Scopes provider events and invalidation. |
| `providerRecordId` | Arr record identifier | Optional | Sonarr/Radarr/generated | Hint only; re-read current state before publishing metadata. |
| `providerFileIds` | Arr file identifier set | Optional | Sonarr/Radarr/generated | Useful for file-change hints. |
| `reason` | Bounded reason code | Yes | Generated/external | Examples: import, upgrade, delete, library update, manual refresh. |
| `invalidationScope` | Match, metadata, artwork, all, or configuration | Yes | Generated | Determines the minimum cache state to invalidate. |
| `versionHint` | Optional provider/configuration/image token | Optional | External/generated | A hint, not proof of current state. |
| `correlationId` | Opaque identifier | Optional | Generated | Links webhook, reconciliation, cache, and render diagnostics. |

### 3.12 Configuration

**Purpose:** An immutable configuration snapshot controlling enabled providers,
badge selection, rendering, cache policy, and update behavior.

| Field | Type | Required | Field Ownership | Notes |
| --- | --- | --- | --- | --- |
| `configurationVersion` | Version identifier | Yes | Configuration/plugin | Increments when the effective configuration changes. |
| `connections` | ArrConnection set | Yes | Configuration | Sonarr and Radarr can be independently enabled. |
| `libraryScope` | Set of Jellyfin libraries/item types | Yes | Configuration | Limits matching and rendering eligibility. |
| `badgeDefinitions` | Ordered BadgeDefinition set | Yes | Configuration | Defines what metadata is displayed and how. |
| `renderingPolicy` | Render size, format, placement, and limits | Yes | Configuration | Output-affecting values belong in the configuration fingerprint. |
| `cachePolicy` | TTL, stale window, size, and eviction limits | Yes | Configuration | Separate metadata freshness from artwork retention. |
| `updatePolicy` | Schedule, webhook, retry, and queue policy | Yes | Configuration | Webhooks accelerate reconciliation; they do not replace it. |
| `enhancedCoexistencePolicy` | Duplicate/spoiler surface policy | Yes | Configuration | No dependency on Jellyfin Enhanced internals. |
| `pathMappings` | Optional connection-scoped mappings | Optional | Configuration | Required before path fallback is eligible. |
| `secretReferences` | Protected secret references | Optional | Configuration | API keys and webhook secrets are excluded from fingerprints and logs. |

## 4. Provider Mapping

The tables below describe conceptual mapping into canonical fields. They do not
repeat the complete Sonarr or Radarr API schemas. API endpoint and response
details remain in `sonarr-api.md`, `radarr-api.md`, and
`media-metadata-mapping.md`.

Mapping labels:

- **Direct:** copied after type and validity checks.
- **Derived:** calculated or normalized from one or more source values.
- **Generated:** created by ArrTags.
- **Cached:** retained from a prior successful observation.
- **Not applicable:** the provider does not supply this field.

### 4.1 MediaIdentity

| Canonical field | Jellyfin | Sonarr | Radarr | Mapping |
| --- | --- | --- | --- | --- |
| `jellyfinItemId` | `BaseItem.Id` | Not applicable | Not applicable | Direct from Jellyfin. |
| `itemType` | Jellyfin item type | Not applicable | Not applicable | Direct; controls provider selection. |
| `libraryId` | Collection-folder/library context | Not applicable | Not applicable | Direct from Jellyfin. |
| `providerIds` | `ProviderIds` such as `Tvdb`, `Tmdb`, `Imdb` | `series.tvdbId`, `series.tmdbId`, `series.imdbId`, `episode.tvdbId` | `movie.tmdbId`, `movie.imdbId` | Direct on each side, retained as normalized identity candidates. |
| `title` | `Name`/`OriginalTitle` | `series.title`/movie title or episode context | `movie.title`/`originalTitle` | Direct display/candidate data; never sole identity. |
| `productionYear` | `ProductionYear` | `series.year` | `movie.year` | Direct candidate/tie-breaker data. |
| `seriesIdentity` | Episode/season parent series | `series.id` | Not applicable | Derived after Sonarr series match. |
| `seasonNumber` | `ParentIndexNumber`/season `IndexNumber` | `episode.seasonNumber` | Not applicable | Direct values compared under an explicit numbering policy. |
| `episodeNumber` | `IndexNumber` | `episode.episodeNumber` | Not applicable | Direct values compared after series match. |
| `episodeNumberEnd` | `IndexNumberEnd` | Multiple episode records may share one file | Not applicable | Derived multi-episode span; requires explicit policy. |
| `mediaLocation` | `LocationType`, protocol, media source | `episodeFile.path`/`series.path` | `movieFile.path`/`movie.path` | Direct source facts; paths are only fallback/validation with configured mapping. |
| `sourceFingerprint` | Image/media source tag, date, or source facts | Not applicable | Not applicable | Generated from Jellyfin source state. |

### 4.2 ArrProvider

| Canonical field | Jellyfin | Sonarr | Radarr | Mapping |
| --- | --- | --- | --- | --- |
| `kind` | Not applicable | Application identity/status reports Sonarr | Application identity/status reports Radarr | Derived from the validated connection probe. |
| `providerInstanceId` | Not applicable | Not exposed as a stable ArrTags identity | Not exposed as a stable ArrTags identity | Generated per configured connection. |
| `displayName` | Not applicable | `instanceName` when available | `instanceName` when available | Direct/provider-assisted, then configuration may override. |
| `applicationVersion` | Not applicable | System status version | System status version | Direct and cached for diagnostics/capabilities. |
| `apiContract` | Not applicable | `/api/v3` | `/api/v3` | Configuration/provider contract. |
| `capabilities` | Not applicable | Derived from version/observed fields | Derived from version/observed fields | Generated capability set; unknown capabilities remain absent. |

### 4.3 ArrConnection

| Canonical field | Jellyfin | Sonarr | Radarr | Mapping |
| --- | --- | --- | --- | --- |
| `connectionId` | Not applicable | Not applicable | Not applicable | Generated by ArrTags configuration. |
| `provider` | Not applicable | Validated `appName`/status | Validated `appName`/status | Direct probe result mapped to ArrProvider. |
| `baseUrl` | Not applicable | Configured URL base and reported `urlBase` | Configured URL base and reported `urlBase` | Configuration is authoritative; provider status is validation/diagnostic data. |
| `enabled` | Not applicable | Not applicable | Not applicable | Direct from configuration. |
| `requestTimeout` | Not applicable | Not applicable | Not applicable | Plugin configuration, not provider metadata. |
| `tlsPolicy` | Not applicable | Not applicable | Not applicable | Plugin configuration. |
| `secretReference` | Not applicable | API key credential | API key credential | Stored as a protected reference; the key is never mapped into canonical metadata. |
| `health` | Not applicable | Status/health and request outcomes | Status/health and request outcomes | Derived and cached by ArrTags. |
| `lastProbedAt` | Not applicable | Not applicable | Not applicable | Generated by ArrTags. |

### 4.4 MediaMatch

| Canonical field | Jellyfin | Sonarr | Radarr | Mapping |
| --- | --- | --- | --- | --- |
| `mediaIdentity` | Current item identity | Candidate series/episode identity | Candidate movie identity | Direct Jellyfin subject plus provider comparison. |
| `provider` / `connectionId` | Applicable library configuration | Configured Sonarr instance | Configured Radarr instance | Generated/configuration scope. |
| `status` | Item eligibility/location | Zero, one, or multiple candidate records | Zero, one, or multiple candidate records | Derived; ambiguity and no match are valid results. |
| `matchMethod` | Provider IDs, numbers, path candidate | TVDB, other IDs, season/episode, mapped path | TMDb, IMDb, mapped path | Derived from the accepted matching strategy. |
| `recordIdentity` | Media source context only | `SonarrIdentity`: `series.id`, `episode.id`, and `episode.episodeFileId` | `RadarrIdentity`: `movie.id` and `movie.movieFileId` | Direct after validation and the current-file join; always connection-scoped; file identity records explicit present/absent. |
| `matchedProviderIds` | Provider IDs used | `tvdbId`/other matching IDs | `tmdbId`/`imdbId` | Direct evidence retained for diagnostics. |
| `pathValidation` | Item/media source path | Series/episode file path | Movie/movie file path | Derived only when configured path mapping permits comparison. |
| `matchedAt` | Not applicable | Not applicable | Not applicable | Generated/cached. |
| `matchFingerprint` | Item identity inputs | Series/episode/file identity inputs | Movie/file identity inputs | Generated from the Jellyfin subject and every scoped identity component. |
| `ambiguityReason` | Missing or conflicting identity | Candidate/mapping conflict | Candidate/mapping conflict | Generated safe reason. |

### 4.5 BadgeMetadata

| Canonical field | Jellyfin | Sonarr | Radarr | Mapping |
| --- | --- | --- | --- | --- |
| `badgeSchemaVersion` | Not applicable | Not applicable | Not applicable | Generated from plugin schema. |
| `provider` / `recordIdentity` | Matched Jellyfin subject | Typed `SonarrIdentity`: series, episode, and file identity | Typed `RadarrIdentity`: movie and file identity | Generated from the validated match and provider records; never a provider DTO. |
| `observedAt` | Not applicable | Response receipt/source observation time | Response receipt/source observation time | Generated, with provider time only when meaningful. |
| `quality` | No Arr quality concept | Current `episodeFile.quality` | Current `movieFile.quality` | Direct normalized actual file quality, not profile name. |
| `resolution` | Stream dimensions can be corroborating data | Quality resolution/media info | Quality resolution/media info | Derived normalized value with origin retained. |
| `dynamicRange` | Jellyfin stream analysis may corroborate | File `mediaInfo` where available | File `mediaInfo` where available | Direct/derived; unknown remains unknown. |
| `dolbyVision` | Stream analysis may indicate it | Media info dynamic-range fields | Media info dynamic-range fields | Derived from supported provider values; never inferred from absent data. |
| `videoCodec` | Jellyfin media streams | File media info | File media info | Direct normalized value, with Jellyfin only as optional corroboration. |
| `audioCodec` | Jellyfin media streams | File media info | File media info | Direct normalized value. |
| `audioChannels` | Jellyfin media streams | File media info | File media info | Direct normalized value. |
| `audioFeatures` | May provide technical hints | Media info/audio codec strings | Media info/audio codec strings | Derived feature set such as Atmos/DTS where supported. |
| `source` | Not applicable | `quality.quality.source` and quality name | `quality.quality.source` and quality name | Direct normalized release/source classification. |
| `upgradePending` | Not applicable | `qualityCutoffNotMet` | `qualityCutoffNotMet` | Direct policy signal; not actual quality. |
| `customBadges` | Not applicable | Custom formats/score and configured values | Custom formats/score and configured values | Direct provider values, bounded and normalized. |
| `extensions` | Optional stream extensions | Provider-specific future values | Provider-specific future values | Namespaced optional mappings. |
| `metadataFingerprint` | Jellyfin source may affect selected values | File ID and badge fields | File ID and badge fields | Generated over badge-affecting normalized state. |

### 4.6 BadgeDefinition

| Canonical field | Jellyfin | Sonarr | Radarr | Mapping |
| --- | --- | --- | --- | --- |
| `definitionId` | Not applicable | Not applicable | Not applicable | Generated/configured by the plugin. |
| `definitionVersion` | Not applicable | Not applicable | Not applicable | Configuration versioning. |
| `enabled` | Image/item scope may constrain it | Not applicable | Not applicable | Configuration with Jellyfin eligibility applied later. |
| `metadataSelector` | Not applicable | Provides candidate fields | Provides candidate fields | Provider-neutral selector resolves against BadgeMetadata. |
| `fallbackPolicy` | Not applicable | Missing fields remain unknown | Missing fields remain unknown | Configuration controls hide/alternate behavior. |
| `textTemplate` | Not applicable | Not applicable | Not applicable | Plugin configuration. |
| `style` | Image dimensions can constrain layout | Not applicable | Not applicable | Plugin configuration, evaluated against the request. |
| `placement` | Image surface and dimensions | Not applicable | Not applicable | Configuration plus Jellyfin request context. |
| `visibilityPolicy` | Item/image/library scope | Not applicable | Not applicable | Configuration and Jellyfin request eligibility. |
| `customValueRules` | Not applicable | Custom-format values may be input | Custom-format values may be input | Configuration maps approved values to badges. |

### 4.7 RenderRequest

| Canonical field | Jellyfin | Sonarr | Radarr | Mapping |
| --- | --- | --- | --- | --- |
| `requestId` | Request correlation context | Not applicable | Not applicable | Generated for one render attempt. |
| `mediaIdentity` | Item resolved by Jellyfin | Not applicable | Not applicable | Direct Jellyfin context. |
| `imageSurface` | Image route type/index | Not applicable | Not applicable | Direct request context and eligibility policy. |
| `sourceImage` | Retained source artwork | Not applicable | Not applicable | Direct source-artwork input. |
| `sourceImageFingerprint` | Source image identity | Not applicable | Not applicable | Derived from source-artwork state. |
| `renderParameters` | Persisted output parameters | Not applicable | Not applicable | Configuration/generated publication context. |
| `match` / `metadata` | Matched item context | Current normalized file observation | Current normalized file observation | Generated from provider mapping/cache. |
| `badgeDefinitions` | Surface eligibility | Not applicable | Not applicable | Configuration snapshot selected for the request. |
| `configurationFingerprint` | Not applicable | Not applicable | Not applicable | Generated from output-affecting configuration. |
| `rendererVersion` | Not applicable | Not applicable | Not applicable | Plugin-generated version. |
| `outputPolicy` | Request and image limits | Not applicable | Not applicable | Configuration plus Jellyfin request constraints. |

### 4.8 RenderResult

| Canonical field | Jellyfin | Sonarr | Radarr | Mapping |
| --- | --- | --- | --- | --- |
| `status` | Publication eligibility/result | Not applicable | Not applicable | Generated from render/publication outcome. |
| `outputArtifact` | Source/derived image bytes or representation | Not applicable | Not applicable | Generated render work that may be published through Jellyfin's item-image API. |
| `contentType` | Original/requested image content type | Not applicable | Not applicable | Preserved or deliberately selected by renderer policy. |
| `width` / `height` | Requested/source dimensions | Not applicable | Not applicable | Direct/derived from image representation. |
| `outputFingerprint` | Source image fingerprint | Metadata fingerprint input | Metadata fingerprint input | Generated composite fingerprint. |
| `etag` | Not applicable to native delivery | Not applicable | Not applicable | Native Jellyfin image validators are generated after publication. |
| `createdAt` | Not applicable | Not applicable | Not applicable | Generated. |
| `passThroughReason` | Publication constraints | Provider unavailable may contribute | Provider unavailable may contribute | Generated safe reason for leaving current artwork unchanged. |
| `expiresAt` | Not applicable | Metadata freshness may constrain | Metadata freshness may constrain | Generated by cache policy. |

### 4.9 MetadataCacheEntry

| Canonical field | Jellyfin | Sonarr | Radarr | Mapping |
| --- | --- | --- | --- | --- |
| `cacheVersion` | Not applicable | Not applicable | Not applicable | Generated plugin cache version. |
| `cacheKey` | Item ID/source scope | Connection/record/file scope | Connection/record/file scope | Generated composite key; never includes a secret. |
| `mediaIdentity` | Item snapshot | Not applicable | Not applicable | Cached Jellyfin identity. |
| `match` | Item and provider scope | Match evidence/current IDs | Match evidence/current IDs | Cached normalized match. |
| `metadata` | Optional Jellyfin corroboration | Normalized episode/file metadata | Normalized movie/file metadata | Cached last-known-good canonical snapshot. |
| `metadataFingerprint` | Source values that affect badge | Quality/file/media info fields | Quality/file/media info fields | Generated normalized fingerprint. |
| `providerVersion` | Not applicable | System status version | System status version | Cached probe observation. |
| `providerVersionToken` | Not applicable | Optional response validator | Optional response validator | Cached only if provider contract supports it. |
| `fetchedAt` / `expiresAt` / `staleUntil` | Not applicable | Request/cache policy | Request/cache policy | Generated cache lifecycle fields. |
| `state` / `lastError` | Jellyfin eligibility may affect state | HTTP/auth/parse/match outcome | HTTP/auth/parse/match outcome | Generated operational state, not provider DTO data. |

### 4.10 ArtworkCacheEntry

| Canonical field | Jellyfin | Sonarr | Radarr | Mapping |
| --- | --- | --- | --- | --- |
| `cacheVersion` | Not applicable | Not applicable | Not applicable | Generated plugin cache version. |
| `renderKey` | Item/image/source fingerprint | Metadata fingerprint | Metadata fingerprint | Generated composite render key. |
| `outputFingerprint` | Source image and publication values | Badge metadata input | Badge metadata input | Generated output identity. |
| `artifact` / `contentType` / dimensions | Source and image processing result | Not applicable | Not applicable | Generated render work before supported item-image publication. |
| `sizeBytes` / timestamps / `expiresAt` | Not applicable | Not applicable | Not applicable | Generated and controlled by cache policy. |
| `etag` | Not applicable to native delivery | Not applicable | Not applicable | Jellyfin generates validators after publication. |

### 4.11 UpdateEvent

| Canonical field | Jellyfin | Sonarr | Radarr | Mapping |
| --- | --- | --- | --- | --- |
| `eventType` | Item added/updated/removed or artwork publication | Webhook event | Webhook event | Mapped to the common event vocabulary. |
| `source` | Jellyfin event/task | Sonarr webhook/task | Radarr webhook/task | Direct source classification. |
| `occurredAt` | Event/receipt time | Webhook event time when present | Webhook event time when present | Direct where trustworthy, otherwise generated receipt time. |
| `jellyfinItemId` | Event item ID | Not normally supplied | Not normally supplied | Direct Jellyfin or resolved from provider identity. |
| `connectionId` | Configuration scope | Configured Sonarr connection | Configured Radarr connection | Generated from receiving endpoint/connection. |
| `providerRecordId` | Not applicable | Series/episode ID in webhook | Movie ID in webhook | Direct hint, revalidated through reads. |
| `providerFileIds` | Not applicable | Episode file ID/current file hints | Movie file ID/deleted-file hints | Direct hint where supplied. |
| `reason` | Library update/removal | Import, rename, file delete, health | Download/upgrade, rename, delete, health | Normalized event reason. |
| `invalidationScope` | Item/match/metadata/artwork | Provider record/file impact | Provider record/file impact | Generated from reason and policy. |
| `versionHint` | Image tag/date | Webhook/provider revision hints | Webhook/provider revision hints | Optional hint only. |
| `correlationId` | Task/request context | Webhook request context | Webhook request context | Generated or mapped for diagnostics. |

### 4.12 Configuration

| Canonical field | Jellyfin | Sonarr | Radarr | Mapping |
| --- | --- | --- | --- | --- |
| `configurationVersion` | Plugin configuration revision | Not applicable | Not applicable | Generated by plugin configuration updates. |
| `connections` | Jellyfin plugin configuration | Base URL/API key/timeout/TLS inputs | Base URL/API key/timeout/TLS inputs | Configuration maps to ArrConnection; credentials remain protected. |
| `libraryScope` | Libraries/item/image types | Not applicable | Not applicable | Jellyfin-side eligibility policy. |
| `badgeDefinitions` | Image surface/configuration | Provider fields selected by selector | Provider fields selected by selector | Plugin definitions consume normalized metadata from either provider. |
| `renderingPolicy` | Published image format, placement, and limits | Not applicable | Not applicable | Plugin configuration plus Jellyfin image context. |
| `cachePolicy` | Not applicable | Provider freshness/capability inputs | Provider freshness/capability inputs | Configuration controls retention; provider does not define plugin TTL. |
| `updatePolicy` | Library events/schedules | Webhook connection hints | Webhook connection hints | Common reconciliation policy. |
| `enhancedCoexistencePolicy` | Jellyfin Enhanced surface behavior | Not applicable | Not applicable | Configuration only; no Enhanced model is imported. |
| `pathMappings` | Jellyfin path namespace | Sonarr path namespace | Radarr path namespace | Explicit configuration required before path fallback. |
| `secretReferences` | Protected plugin settings | API key/webhook secret | API key/webhook secret | References only; values are not domain data. |

## 5. Badge Metadata Specification

### V1 planned fields

V1 should support the following fields when the selected provider supplies them:

| Field | V1 behavior | Semantics |
| --- | --- | --- |
| Quality | Planned | Actual Arr file quality label and structured descriptor. Never use the configured profile as actual quality. |
| Resolution | Planned | Normalized resolution from quality/media info, with origin retained. |
| HDR | Planned | Generic HDR/dynamic-range indication with unknown state. |
| Dolby Vision | Planned when reliably reported | Separate DV indication; DV may coexist with HDR10 or another base layer. |
| Video codec | Planned | Normalized codec value. |
| Audio codec | Planned | Normalized codec value. |
| Audio channels | Planned | Numeric channel count when known. |
| Atmos/DTS | Planned where derivable | Audio feature set, not a replacement for the base audio codec. |
| Source | Planned | Provider quality source such as web, WEB-DL, Blu-ray, or remux. |
| Upgrade pending | Optional V1 | `qualityCutoffNotMet`, clearly labelled as policy state. |
| Custom badges | Limited V1 | Configured, bounded custom-format or extension values. |

### Future fields

Future schema versions may add bit depth, frame rate, scan type, language,
subtitles, release group, edition, custom-format score, media certification,
stream count, and provider-specific extension values. These fields should be
added as optional normalized values or namespaced extensions rather than by
making existing fields provider-specific.

### Three-state technical values

Technical flags use `true`, `false`, or `unknown` where source absence is
meaningful. A provider may explicitly report that a feature is absent, but a
missing `mediaInfo` object must result in `unknown`. A renderer may hide unknown
badges, use a configured placeholder, or select a fallback definition; it must
not display a negative assertion based only on missing data.

### Quality profile separation

Quality profile name, cutoff, allowed qualities, and upgrade policy are not part
of the `quality` field. If a future badge exposes requested quality, it should be
a separately named policy field and should not share the actual-quality label.

## 6. Cache Model

### Metadata cache

`MetadataCacheEntry` is a last-known-good normalized snapshot. It may outlive a
temporary Arr outage, but only until its configured `staleUntil`. After that,
new artwork publication must stop or retain the current usable artwork according
to policy; it must not claim that stale metadata is current.

### Artwork cache

`ArtworkCacheEntry` is bounded, evictable render work. It is not the native
client response cache and has no authority to publish or restore artwork.
`PublishedArtworkState` is the source of truth for whether the active image is
an ArrTags publication and whether guarded restoration is possible.

### Cache keys and fingerprints

| Cache/object | Key or fingerprint inputs |
| --- | --- |
| Metadata entry | Jellyfin item ID, connection ID, provider kind, every typed record/file identity component, and metadata cache version. |
| Metadata fingerprint | Every typed record/file identity component (including explicit file-identity presence), badge-affecting normalized metadata, the match identity, and badge schema version. |
| Artwork entry | Jellyfin item/image surface/index, source image fingerprint, metadata fingerprint, configuration fingerprint, and renderer version. |
| Configuration fingerprint | Output-affecting badge definitions/rendering/coexistence settings; never API keys or webhook secrets. |

### ETags and provider versions

Provider ETags or revision tokens are optional observations, not assumed
correctness contracts. A provider token may reduce requests only after it is
validated for the deployed version. The canonical metadata fingerprint remains
the plugin's comparison value.

### Expiration and invalidation

Metadata expiration triggers refresh; it does not necessarily immediately delete
last-known-good data. Artwork entries are invalidated when any render-key input
changes, when the metadata fingerprint changes, when configuration or renderer
version changes, or when the source image changes. All entries are invalidated
when the relevant cache version changes.

## 7. Event Model

Events are internal work hints. They are bounded, deduplicated, cancellation-
aware, and safe to replay. Provider webhooks never directly publish metadata.

| Event | Required data | Effect |
| --- | --- | --- |
| Metadata changed | Subject, provider/connection, record/file hint, reason | Re-match or re-read current provider state, then replace the metadata snapshot if its fingerprint changed. |
| Artwork publication requested | Jellyfin item, image surface/index, source fingerprint, reason | Look up current metadata and construct bounded publication work. |
| Artwork published | Item, image surface, source fingerprint, published fingerprint, publication token, active-image identity, operation ID, state revision, status, correlation ID | Record publication state through the supported item-image flow; never write the image cache directly. |
| Item removed | Jellyfin item ID and optional provider scope | Invalidate metadata and artwork entries for the item; do not call provider write APIs. |
| Configuration changed | New configuration version and affected scopes | Replace the configuration snapshot and invalidate affected metadata/artwork state. |
| Cache invalidated | Scope, key/fingerprint, reason | Remove or mark stale only the affected cache entries. |
| Reconciliation requested | Scope, reason, schedule/manual source | Enqueue bounded work that re-reads current Jellyfin and Arr state. |
| Provider health changed | Connection ID, health state, safe error summary | Adjust retry/reconciliation behavior and preserve bounded last-known-good metadata where allowed. |

The common invalidation flow is:

```mermaid
sequenceDiagram
    participant S as Jellyfin/Arr/schedule
    participant Q as Update queue
    participant R as Reconciler
    participant A as Arr API
    participant M as Metadata cache
    participant C as Render work cache
    participant P as Published artwork

    S->>Q: UpdateEvent hint
    Q->>R: Coalesced subject work
    R->>A: Read current record/file
    A-->>R: Current provider response
    R->>R: Map and fingerprint
    R->>M: Publish new snapshot atomically
    R->>C: Invalidate changed render work
    R->>P: Publish validated derived artwork
```

## 8. Error Model

Failures are represented as status values and safe error summaries, not as
exceptions in the domain model and not as failures of Jellyfin library or image
serving operations.

### Domain error value

| Field | Type | Required | Field Ownership | Notes |
| --- | --- | --- | --- | --- |
| `code` | Stable error code | Yes | Generated | Examples below; consumers branch on code, not provider text. |
| `scope` | Connection, match, metadata, artwork, configuration, or cache | Yes | Generated | Identifies the affected boundary. |
| `retryability` | Never, later, or after configuration | Yes | Generated | Prevents rapid retry loops. |
| `message` | Redacted bounded message | Optional | Generated | Suitable for administrative diagnostics; no keys or sensitive URLs. |
| `providerStatus` | HTTP/status classification | Optional | Provider/generated | Useful for diagnostics, not required for rendering. |
| `occurredAt` | Timestamp | Yes | Generated | Error observation time. |
| `correlationId` | Opaque identifier | Optional | Generated | Links a failure to an event or request. |

### Error codes and behavior

| Error code | Meaning | Canonical behavior |
| --- | --- | --- |
| `MatchNotFound` | No provider record satisfies the matching policy | Keep match unresolved and leave current artwork unchanged. |
| `MatchAmbiguous` | Multiple candidates remain | Do not auto-badge; retain the reason for diagnostics. |
| `ProviderUnavailable` | DNS, connection, timeout, transient 5xx, or equivalent | Bounded retry; use last-known-good metadata only within stale policy. |
| `AuthenticationFailed` | Provider rejected credentials or authorization | Stop rapid retries and require configuration attention. |
| `ProviderIncompatible` | Unexpected version, contract, or required field shape | Mark capability/connection state and degrade without affecting Jellyfin. |
| `MetadataUnavailable` | Current metadata cannot be obtained or is invalid | Use valid bounded stale state or leave current artwork unchanged. |
| `ArtworkUnavailable` | Jellyfin source image is missing or unsupported | Keep the current usable artwork and do not publish a replacement. |
| `ArtworkOwnershipChanged` | The active image no longer matches the persisted ArrTags identity | Enter `OwnershipLost`; leave the active image unchanged and block automatic publication/restoration. |
| `ArtworkOwnershipUnknown` | The active image or required identity evidence cannot be observed or compared | Enter `OwnershipUnknown`; leave the active image unchanged and block automatic mutation. |
| `SourceArtifactInvalid` | The retained source artifact is missing or fails integrity validation | Block publication or enter `RestoreBlocked`; do not guess a source or overwrite the active image. |
| `ArtworkRecoveryBlocked` | A non-terminal artwork operation, artifact, or postcondition is corrupt, unavailable, or ambiguous | Quarantine the operation, preserve the current artwork, and require later reconciliation or explicit administration; never blind-replay or clean up. |
| `RenderingFailed` | Decode, layout, encode, size, or cancellation failure | Leave current artwork unchanged and record a bounded diagnostic. |
| `ConfigurationInvalid` | Configuration cannot produce a valid snapshot | Keep the last valid snapshot where supported; do not start affected work. |
| `CacheCorrupt` | Cache entry cannot be read or fails version/integrity checks | Discard/quarantine and rebuild; never block Jellyfin. |

## 9. Extension Strategy

### New metadata providers

A new source such as Bazarr, Tdarr, Plex, or a custom provider should implement
the integration boundary that maps its external identity and observations into
`ArrProvider`-like provider identity, `MediaMatch`, and `BadgeMetadata`. The
matching and rendering pipeline should consume the same canonical models.

Provider-specific facts belong in namespaced `extensions` or in a future
optional canonical field only after the fact has stable cross-provider meaning.
The renderer should not branch on provider kind for ordinary fields.

### New badge types

New badges should be added as metadata selectors and `BadgeDefinition` values.
They should not require a change to `RenderRequest` or `RenderResult`. A new
visual primitive may extend the style/placement vocabulary, while preserving
the same bounded rendering and fingerprint rules.

### New item or image surfaces

Support for season/series aggregation, thumbnails, or other surfaces requires
an explicit aggregation policy and eligibility rule. It must not silently reuse
an episode or movie file's quality as if it represented the whole aggregate.

## 10. Versioning

The version values have different responsibilities:

| Version | Responsibility | Cache consequence |
| --- | --- | --- |
| `modelVersion` / `cacheVersion` | Shape and meaning of persisted canonical/cache records | An incompatible value causes entries to be ignored or rebuilt. |
| `badgeSchemaVersion` | Meaning and availability of `BadgeMetadata` fields | Included in metadata fingerprints; changing semantics invalidates metadata/artwork. |
| `rendererVersion` | Layout, drawing, encoding, and output behavior | Included in render keys; changing it invalidates artwork entries but need not refetch metadata. |
| `configurationVersion` | Effective administrative configuration snapshot | Included in configuration fingerprints when output-affecting. |

### Compatibility rules

- Additive optional metadata fields should be readable by older versions as
  unknown and ignored by renderers that do not select them.
- A field whose meaning changes requires a badge schema version change, even if
  its name and type remain the same.
- A renderer-only change requires a renderer version change and artwork cache
  invalidation, not necessarily a provider refresh.
- A cache format or ownership change requires a cache version change; do not
  infer compatibility from a successful parse alone.
- Every fingerprint includes the version that defines its semantics.
- Cache entries must not contain secrets, raw provider credentials, or unbounded
  external payloads.

The canonical model is therefore evolvable without making the renderer depend
on external API versions or making old cache entries appear current by accident.
