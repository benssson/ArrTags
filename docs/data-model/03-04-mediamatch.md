# 3.4 MediaMatch

**Purpose:** The validated relationship between a Jellyfin item and an Arr
record. A match is scoped to a connection and may be unresolved, ambiguous, or
invalid rather than silently guessed.

| Field | Type | Required | Field Ownership | Notes |
| --- | --- | --- | --- | --- |
| `mediaIdentity` | MediaIdentity reference | Yes | Jellyfin/plugin | The Jellyfin subject being matched. |
| `provider` | ArrProvider reference | Yes | Plugin | Provider family and instance scope. |
| `connectionId` | Opaque connection identifier | Yes | Plugin/configuration | Prevents cross-instance local-ID collisions. |
| `status` | `Matched`, `NotFound`, `Ambiguous`, `Unsupported`, or `Stale` | Yes | Generated | Only `Matched` permits provider metadata to be used for a badge. |
| `matchMethod` | Provider ID, number, configured path (post-V1), manual, or none | Yes | Generated | Records how the result was established; V1 never emits `ConfiguredPath` (ADR-008). |
| `recordIdentity` | Typed, connection-scoped Arr record/file identity | Required when `Matched` | Sonarr/Radarr, cached | Provider-neutral envelope holding one concrete `SonarrIdentity` or `RadarrIdentity`; see section 3.4.1. |
| `matchedProviderIds` | Map of IDs used for validation | Optional | Jellyfin + provider | Records the stable IDs that agreed; useful for diagnostics and invalidation. |
| `pathValidation` | Path comparison result | Optional | Generated | Reserved for a future path-mapping decision; V1 never produces a path match or path validation result. |
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
