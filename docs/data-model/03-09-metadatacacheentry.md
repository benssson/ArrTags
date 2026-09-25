# 3.9 MetadataCacheEntry

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
