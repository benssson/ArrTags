# 2. Model Relationships

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
    class SecretReference {
        +Purpose
        +SlotId
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
    ArrConnection --> SecretReference : references
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
