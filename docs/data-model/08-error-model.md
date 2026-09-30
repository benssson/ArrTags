# 8. Error Model

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
