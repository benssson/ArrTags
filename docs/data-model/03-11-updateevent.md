# 3.11 UpdateEvent

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
