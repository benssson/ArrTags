# 6. Cache Model

### Metadata cache

`MetadataCacheEntry` is a last-known-good normalized snapshot. It may outlive a
temporary Arr outage, but only until its configured `staleUntil`. After that,
new artwork publication must stop or retain the current usable artwork according
to policy; it must not claim that stale metadata is current. The entry's
`expiresAt`/`staleUntil` boundaries are derived from the single configured
last-known-good window, which is the total bounded last-known-good lifetime: an
observation is `Fresh` for the first half of the window and may be retained as
bounded `Stale` for the remaining half, so the total never exceeds the
configured window. A transient outage within the window keeps the snapshot as
explicit `Stale` without extending the window. The entry's usability and
retention are governed by freshness, not by the artwork render-cache or
provenance eviction policy.

### Artwork cache

`ArtworkCacheEntry` is bounded, evictable render work. It is not the native
client response cache and has no authority to publish or restore artwork.
`PublishedArtworkState` is the source of truth for whether the active image is
an ArrTags publication and whether guarded restoration is possible.

### Inventory cache

The provider inventory cache (ADR-018) is the bounded, in-memory,
per-connection observation set that lets one provider library read serve a
reconciliation window. It holds the provider library list (Sonarr `/series`,
Radarr `/movie`) and the per-record file observations needed for badge metadata
as canonical, secret-free observations, and it is **distinct from
`MetadataCacheEntry` (3.9)**: the inventory cache holds raw canonical provider
observations for reuse, while `MetadataCacheEntry` remains the per-item match
and metadata freshness record.

It is non-authoritative: it is never persisted as authoritative state, it is
rebuilt empty on restart, and it never contains a credential, secret lease, or
provider DTO. One `ArrInventoryCacheEntry` per connection carries
`ArrInventoryRecordObservation` values (a canonical `MatchCandidate` plus the
canonical `BadgeMetadata` mapped from the record's current file). The configured
inventory TTL (`OperationalLimits.InventoryCacheTtlMinutes`) is the total bounded
lifetime of one observation set: the set is fresh for the first half and may be
served as explicit bounded last-known-good for the remaining half, and the TTL is
never extended; a library read failure during a population caches nothing and
returns the bounded failure, while the existing per-item bounded last-known-good
metadata state retains last-known-good metadata. The per-connection record and
byte bounds
(`OperationalLimits.InventoryCacheMaxRecords` and
`InventoryCacheMaxBytes`) reject an over-bound observation set and the caller
keeps the direct provider read unchanged. The entry's only free-text field is a
bounded `ArrProviderError` failure summary; value-level redaction of that message
is the producer contract (`ArrProviderError` is documented as bounded and
redacted, ADR-020 clause 4), consistent with the per-item metadata record's
last-error summary, and the cache itself stores no credential. The cache assumes
no provider `ETag` or revision token; such a token remains an optional
observation only (see "ETags and provider versions").

The cache is populated and consumed at the provider-client boundary (task 11.2).
Concurrent cold readers for one connection serialize through the provider's
per-connection single-flight gate, so one library read (`/movie` or `/series`)
plus the bulk file reads populates the cache for all of them. On a miss the
reader reads the per-record file resources through the provider's bulk selection
endpoints — Radarr `moviefile?movieId=` with a repeatable `movieId` and Sonarr
`episodeFile?episodeFileIds=` with a repeatable `episodeFileId` (the embedded
per-series episode file is preferred) — in bounded chunks, then maps them to
canonical observations and stores them. Every work item in the cache window is
served from the retained observations without another library read; an absent or
expired set is re-read, an over-bound set or a failed bulk/sub-read keeps the
existing direct read unchanged, and a library read failure during a population
caches nothing and returns the bounded failure (the per-item bounded
last-known-good metadata state is what retains last-known-good metadata).

The observation set's `ObservedAt` is the population time, so on a cache hit the
canonical file observation's `ObservedAt` is the population time rather than the
work item's time. This does not change per-item freshness: the published
`MetadataStateEntry` freshness is derived from the publish time
(`DateTimeOffset.UtcNow`), `MetadataSnapshot` does not carry the canonical
observation timestamp, and the metadata fingerprint excludes it, so the published
per-item state exposes only publish-time freshness. The bound that prevents a
cached read from being presented as current beyond its lifetime is the cache
entry's bounded reuse window (`ExpiresAt`/`StaleUntil`), not the fingerprint.

The inventory cache is invalidated ArrTags-side (ADR-018 clause 3, task 11.3): an
accepted provider webhook invalidates the event's provider/connection, and a
reconciliation (Jellyfin library refresh/post-scan, scheduled, manual, or
post-save) invalidates the retained sets so its work begins a fresh provider-read
window; the inventory TTL remains the bounded fallback. The invalidation surface
(`ArrInventoryCache.Invalidate`/`InvalidateAll` and the `ArrInventoryCacheProvider`
equivalents) is bounded, thread-safe, and secret-free, removes only retained sets
under the cache gate, and never holds a lock across provider I/O. A population
already in flight when an invalidation runs may still store the read it took
before it, which is bounded by the configured TTL and repaired by the next
invalidation source. No provider `ETag`/`If-None-Match`, revision token,
`history/since` watermark, or SignalR channel participates.

### Cache keys and fingerprints

| Cache/object | Key or fingerprint inputs |
| --- | --- |
| Metadata entry | Jellyfin item ID, connection ID, provider kind, every typed record/file identity component, and metadata cache version. |
| Metadata fingerprint | Every typed record/file identity component (including explicit file-identity presence), badge-affecting normalized metadata, the match identity, and badge schema version. |
| Artwork entry | Jellyfin item/image surface/index, source image fingerprint, metadata fingerprint, configuration fingerprint, and renderer version. |
| Inventory entry | Connection ID, provider kind, and inventory cache version. |
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
when the relevant cache version changes. The inventory cache is invalidated per
connection by the provider webhook and in full by a reconciliation
(refresh/post-scan, scheduled, manual, or post-save), with the inventory TTL as
the bounded fallback (ADR-018 clause 3); see "Inventory cache".
