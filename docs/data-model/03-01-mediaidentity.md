# 3.1 MediaIdentity

**Purpose:** The canonical identity and minimal media context for one Jellyfin
item. It is used for matching and cache scoping, not as a copy of the complete
Jellyfin entity.

| Field | Type | Required | Field Ownership | Notes |
| --- | --- | --- | --- | --- |
| `jellyfinItemId` | Jellyfin item identifier | Yes | Jellyfin | Stable only within the Jellyfin server; use as the local subject key. |
| `itemType` | `Movie`, `Series`, `Season`, `Episode`, or supported video type | Yes | Jellyfin | Determines which provider and matching policy applies. |
| `libraryId` | Jellyfin collection-folder/library identifier | Optional | Jellyfin | The owning collection-folder (library) identifier, not its display name; used for library eligibility and configuration scope (ADR-006). |
| `providerIds` | Map of provider name to external identifier | Optional | Jellyfin | Prefer normalized TMDb, IMDb, and TVDB IDs; absence is valid. |
| `title` | Display title | Optional | Jellyfin | Candidate or diagnostic data only; not sufficient identity. |
| `productionYear` | Integer year | Optional | Jellyfin | Tie-breaker or diagnostics; never sole proof of a match. |
| `seriesIdentity` | Parent series identity reference | Optional | Jellyfin | Used for seasons and episodes. |
| `seasonNumber` | Integer | Optional | Jellyfin | Relevant to seasons and episodes; season zero represents specials where applicable. |
| `episodeNumber` | Integer | Optional | Jellyfin | Primary episode number after Jellyfin numbering policy is applied. |
| `episodeNumberEnd` | Integer | Optional | Jellyfin | End of a multi-episode span. |
| `mediaLocation` | File/remote/virtual location summary | Optional | Jellyfin | Helps reject virtual or unsupported remote items without becoming an Arr match key. |
| `sourceFingerprint` | Opaque image/media identity fingerprint | Optional | Generated from Jellyfin | Changes when relevant Jellyfin source identity changes; not the provider metadata fingerprint. |
