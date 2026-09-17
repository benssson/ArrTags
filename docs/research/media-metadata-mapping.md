# Media metadata mapping (Jellyfin ↔ Sonarr/Radarr)

## Scope, evidence, and terminology

This document records how a Jellyfin media item can be matched to its
corresponding Sonarr or Radarr record, and how the *actual* file metadata (in
particular quality) is obtained. It is a design/research reference, not an
implementation plan, and it deliberately does **not** mandate a matching order:
it states what the APIs confirm, what follows from source, what is proposed, and
what remains unresolved.

**Direction of interest:** Jellyfin → Sonarr/Radarr only. The reverse direction
(finding Jellyfin items from an Arr record, e.g. for webhook handling or
reconciliation) is out of scope here, although some Jellyfin query facilities
that would support it are noted where they clarify the model.

**Central premise.** Jellyfin models identity, location and its own stream
analysis. It has **no concept of Sonarr/Radarr quality, quality profile, release
group, or custom-format score**. Those values exist only in Sonarr/Radarr and
must be read from their APIs (see `docs/sonarr-api.md` and `docs/radarr-api.md`).
Any mapping design therefore has two independent halves:

1. **Identity/location** from Jellyfin, used to find the Arr record;
2. **Actual file metadata** (quality and related fields) from the Arr record.

**Evidence labels:**

| Label | Meaning |
| --- | --- |
| **Confirmed** | Directly established by the Jellyfin `v12.0` source tag or by the Sonarr/Radarr API docs in this repository. |
| **Inferred** | A conclusion drawn from confirmed source/spec, not a published stability promise. |
| **Proposed** | A design option presented for consideration; not API-establishable. |
| **Unresolved** | Needs a version-pinned validation or a product decision. |

**Sources inspected (Jellyfin `v12.0`, tag commit
`6c073e19ddf604b2369c638716164fdab4c952dc`):**

- `MediaBrowser.Controller/Entities/BaseItem.cs`
- `MediaBrowser.Controller/Entities/Video.cs`
- `MediaBrowser.Controller/Entities/Movies/Movie.cs`
- `MediaBrowser.Controller/Entities/TV/Series.cs`, `TV/Season.cs`, `TV/Episode.cs`
- `MediaBrowser.Controller/Entities/IHasMediaSources.cs`, `IHasSeries.cs`
- `MediaBrowser.Controller/Entities/InternalItemsQuery.cs`
- `MediaBrowser.Controller/Library/ILibraryManager.cs`
- `MediaBrowser.Model/Entities/MetadataProvider.cs`
- `MediaBrowser.Model/Entities/ProviderIdsExtensions.cs`
- `MediaBrowser.Model/Dto/MediaSourceInfo.cs`
- `MediaBrowser.Model/MediaInfo/MediaProtocol.cs`
- `MediaBrowser.Model/Entities/LocationType.cs`

Cross-references: `docs/jellyfin-12-architecture.md` (plugin/serving/threading
constraints), `docs/sonarr-api.md` (Sonarr endpoints and quality), and
`docs/radarr-api.md` (Radarr endpoints and quality).

---

## 1. How Jellyfin identifies items

### 1.1 Common `BaseItem` fields (Confirmed)

Every item derives from `BaseItem`. The fields relevant to matching are:

| Member | Type | Notes for matching |
| --- | --- | --- |
| `Id` | `Guid` | Jellyfin-local identity. Not portable to Sonarr/Radarr and can change if an item is deleted/re-added. Use as a cache/state key, never as the external key. |
| `ProviderIds` | `Dictionary<string,string>` | External provider identifiers. Constructed with `StringComparer.OrdinalIgnoreCase`. |
| `Name` / `OriginalTitle` | `string` | Display metadata, not identity. |
| `ProductionYear` | `int?` | Useful only as a tie-breaker candidate. |
| `Path` | `string` | Item file/folder path (see §5). |
| `PathProtocol` / `IsFileProtocol` | `MediaProtocol?` / `bool` | `IsFileProtocol` is `PathProtocol == MediaProtocol.File`; distinguishes local files from `.strm`/remote paths. |
| `LocationType` | `LocationType` | `FileSystem`, `Remote`, `Virtual`, `Offline`. Virtual items (e.g. missing episodes) have no file. |
| `IndexNumber` | `int?` | Item number within a parent (episode number, track number, …). |
| `ParentIndexNumber` | `int?` | Parent number (season number for episodes, disc for songs). |
| `RunTimeTicks` | `long?` | Duration; a possible sanity check or multi-part signal, not identity. |
| `PremiereDate` | `DateTime?` | Airing/release date; tie-breaker only. |
| `MediaType` | `MediaType` | Coarse type (`Video`, …). |
| `PresentationUniqueKey` | `string` | Stable grouping/presentation key; for series it can span library folders (see §1.3). |

`BaseItem` also exposes `GetMediaSources(bool enablePathSubstitution)` and
`GetMediaStreams()` (see §6).

### 1.2 Provider IDs (Confirmed)

`MetadataProvider` (`MediaBrowser.Model/Entities/MetadataProvider.cs`) enumerates
the provider names Jellyfin knows:

| Enum | `ProviderIds` key | Notes |
| --- | --- | --- |
| `Imdb` | `"Imdb"` | e.g. `tt0111161`. |
| `Tmdb` | `"Tmdb"` | numeric string, e.g. `"603"`. |
| `Tvdb` | `"Tvdb"` | series/episode TVDB id. |
| `TmdbCollection` | `"TmdbCollection"` | collection grouping, not the movie itself. |
| `Tvcom`, `TvRage`, `TvMaze`, `Zap2It` | matching enum name | Legacy/auxiliary TV providers. |
| `Custom` | `"Custom"` | Jellyfin's own override merging key. |
| `MusicBrainz*`, `AudioDb*` | matching enum name | Audio only; not relevant here. |

`ProviderIdsExtensions` establishes the following behaviour (Confirmed):

- `TrySetProviderId`/`SetProviderIds` normalise known provider names to the
  enum's `ToString()` casing before insertion, so keys read back as `"Tmdb"`,
  `"Imdb"`, `"Tvdb"`, etc.
- Lookups (`GetProviderId`/`TryGetProviderId`) are case-insensitive and treat
  empty values as absent.
- `IsValidProviderId` rejects implausible values (e.g. a TMDb id must be a
  positive number; an IMDb id must match the title/person/company prefix plus
  digits). This means an id present under a key is usually well-formed, but it
  **does not** prove it belongs to the matching Arr record.

Read provider IDs through `BaseItem.GetProviderId(MetadataProvider.Tmdb)` etc.
rather than indexing the dictionary directly, to inherit the normalisation and
empty-value handling.

**Inferred:** Jellyfin only fills the provider IDs that its configured metadata
providers successfully retrieved. A library that never ran the IMDb/TMDb/TVDB
providers, or a manually added item, may have no IDs at all.

### 1.3 Movies

`Movie : Video` (`MediaBrowser.Controller/Entities/Movies/Movie.cs`) — Confirmed
relevant members:

- `ProviderIds`: typically `Tmdb` and `Imdb`.
- `TmdbCollectionName` / `CollectionName` (and Jellyfin's box-set grouping) —
  relates to a collection, not the movie identity.
- Inherits all `Video` members, including alternate-version handling (§8.3).

### 1.4 Series

`Series : Folder` (Confirmed):

- `ProviderIds`: `Tvdb` is the primary TV identity; `Tmdb`, `Imdb` may also be
  present. `GetUserDataKeys()` in source consults `Imdb`, `Tvdb` and `Custom`.
- `DisplayOrder` (`string`): `airdate`, `dvd`, or `absolute`. This governs how
  Sonarr-style numbering should be interpreted and is a mapping hazard (§4).
- `Status` (`SeriesStatus?`), `AirDays`, `AirTime`.
- `PresentationUniqueKey` / `CreatePresentationUniqueKey()` can incorporate
  automatic series grouping and the metadata language, so it is a stable
  *grouping* key across duplicated folders — prefer it over `Name` when a
  series-level key is needed, but still treat it as Jellyfin-local, not an
  external id.

### 1.5 Seasons

`Season : Folder, IHasSeries` (Confirmed):

- `SeriesId` (Guid), `SeriesPresentationUniqueKey`, `SeriesName`.
- `IndexNumber` (from `BaseItem`) is the season number; `BeforeMetadataRefresh`
  can fill it from the path via `ILibraryManager.GetSeasonNumberFromPath`.
- `IHasSeries` provides `FindSeriesId()`, `FindSeriesName()`,
  `FindSeriesPresentationUniqueKey()`.
- `Season` carries the `[RequiresSourceSerialisation]` attribute in 12.0.
- **Inferred:** seasons rarely carry a TVDB season id in `ProviderIds`; season
  identity is normally derived from the matched series plus the season number.

### 1.6 Episodes

`Episode : Video, IHasSeries` (Confirmed members):

| Member | Meaning |
| --- | --- |
| `SeriesId`, `SeasonId` | Parent linkage (Guid). |
| `SeriesName`, `SeasonName` | Display only. |
| `ParentIndexNumber` | Season number (via `AiredSeasonNumber` when set). |
| `IndexNumber` | Episode number. |
| `IndexNumberEnd` | End of a double/multi-episode span; `ContainsEpisodeNumber(int)` checks a range. |
| `AirsBeforeSeasonNumber`, `AirsAfterSeasonNumber`, `AirsBeforeEpisodeNumber` | Special airing placement. |
| `AiredSeasonNumber` | `AirsAfterSeasonNumber ?? AirsBeforeSeasonNumber ?? ParentIndexNumber`. |
| `IsMissingEpisode` / `LocationType == Virtual` | No file present. |
| `ProviderIds` | Can include `Tvdb` (and others), but its presence is provider-dependent. |

Before refresh, `Episode.BeforeMetadataRefresh` may run
`ILibraryManager.FillMissingEpisodeNumbersFromPath` to populate season/episode
numbers from the filename, and can read embedded MP4 metadata when the library
option is enabled. **Inferred:** those values (and therefore number-based
matching) can change on a metadata refresh.

### 1.7 `Video` alternate versions and parts

`Video : BaseItem, … , IHasMediaSources` (Confirmed) models multiple files/parts
in ways that matter for "multiple files":

- `PrimaryVersionId` (Guid?): an alternate version points at its primary.
- `LocalAlternateVersions` (`string[]`): additional files at distinct paths.
- `LinkedAlternateVersions` (`LinkedChild[]`): separately-scanned items linked as
  versions (e.g. editions).
- `AdditionalParts` (`string[]`) and `IsStacked`: a logical item split across
  files (stacked/multi-part).
- `MediaSourceCount` and `GetAllVersions()`: enumerate this version and all
  alternates.
- `ILibraryManager.GetLocalAlternateVersionIds(Video)` and
  `GetLinkedAlternateVersions(Video)` expose the same relationships (Confirmed).
- `InternalItemsQuery.IncludeOwnedItems` (default `false`) controls whether
  alternate versions/additional parts are returned by general queries
  (Confirmed).

---

## 2. Confirmed Jellyfin lookup APIs

### 2.1 `ILibraryManager` (read-only subset)

Confirmed members relevant to mapping:

| Member | Use |
| --- | --- |
| `GetItemById(Guid)` / `GetItemById<T>(Guid)` | Resolve an item from a cached Jellyfin id. |
| `GetItemById<T>(Guid, User)` | Same with user-access validation (user-facing paths only). |
| `GetItemIds(InternalItemsQuery)` | Return matching item ids without materialising items. |
| `GetItemList(InternalItemsQuery)` | Return matching items. |
| `QueryItems(InternalItemsQuery)` / `GetItemsResult(...)` | Paged/counted variants. |
| `FindByPath(string path, bool? isFolder)` | Path-based lookup (see §5). |
| `GetNewItemId(string key, Type type)` | Compute an item id deterministically from a path/type. |
| `GetLocalAlternateVersionIds(Video)` / `GetLinkedAlternateVersions(Video)` | Version enumeration. |
| `GetSeasonNumberFromPath(string, Guid?)` | Season-number inference. |
| `GetCollectionFolders(BaseItem)` | Library scope for an item. |
| `ItemAdded` / `ItemUpdated` / `ItemRemoved` events | Re-evaluation triggers (see architecture doc §6). |

### 2.2 `InternalItemsQuery`

Confirmed fields useful for locating candidates:

| Field | Notes |
| --- | --- |
| `IncludeItemTypes` (`BaseItemKind[]`) | Constrain to `Movie`, `Series`, `Season`, `Episode`. |
| `HasAnyProviderId` (`Dictionary<string,string>?`) | Filter by provider id(s). |
| `HasImdbId`, `HasTmdbId`, `HasTvdbId` (`bool?`) | Presence filters. |
| `Path` (`string?`) | Exact path filter. |
| `ParentIndexNumber`, `IndexNumber` (`int?`) | Episode/season numbering filters. |
| `ParentId`, `SeriesPresentationUniqueKey`, `AncestorWithPresentationUniqueKey` | Scoping/grouping (used internally for series grouping). |
| `IsMissing`, `IsVirtualItem` | Exclude missing/virtual items. |
| `IncludeOwnedItems` (`bool`) | Include alternate versions/additional parts (default excluded). |
| `DtoOptions` | Controls DTO enrichment; can be minimised for background work. |

Note: `HasAnyProviderId` also enables the reverse direction (Arr → Jellyfin
items). That direction is out of scope here but is the same confirmed facility.

### 2.3 Media sources

- `IHasMediaSources.GetMediaSources(bool enablePathSubstitution)` returns
  `IReadOnlyList<MediaSourceInfo>`; `Path` and `GetMediaStreams()` are also on
  the interface (Confirmed).
- `BaseItem.GetMediaSources(bool)` lifts `Video.GetAllItemsForMediaSources()`
  into a `MediaSourceInfo` per version/part and orders the queried item's own
  source first (Confirmed).

---

## 3. Matching a Jellyfin movie to a Radarr record

Radarr identity and file fields are documented in `docs/radarr-api.md`. Radarr
`MovieResource` exposes `tmdbId` (int) and `imdbId` (string); lookup/listing
endpoints include `GET /api/v3/movie?tmdbId=`, `/movie/lookup/tmdb`,
`/movie/lookup/imdb`, and `/movie/lookup?term=`.

Candidate keys, with their confirmability:

| Key | Jellyfin source | Radarr target | Status | Caveats |
| --- | --- | --- | --- | --- |
| TMDb id | `ProviderIds["Tmdb"]` | `movie.tmdbId` | **Confirmed** on both sides | Jellyfin stores it only if the TMDb provider populated it; `TMDB` id is an exact external key. |
| IMDb id | `ProviderIds["Imdb"]` | `movie.imdbId` | **Confirmed** on both sides | Same provider-dependence; Radarr may lack an IMDb id on some records. |
| Title + `ProductionYear` | `Name`/`OriginalTitle`, `ProductionYear` | `movie.title`/`originalTitle`, `movie.year` | **Proposed** | Non-unique and normalization-sensitive; only a candidate, not proof. |
| Filesystem path | `Path` / `MediaSourceInfo.Path` | `movieFile.path` / `movie.path` | **Proposed** | Valid only when both applications see the same namespace (§5). |
| TMDB collection id | `ProviderIds["TmdbCollection"]` | `movie.collection.tmdbId` | **Inferred** | Groups movies into a collection; does not identify a single movie. |

**Confirmed mechanics:** Jellyfin exposes the ids; Radarr exposes the ids. The
association between them (that a Jellyfin `Tmdb` id *is* Radarr's `tmdbId`) is
the external-services contract implied by both using TMDb, not a Jellyfin API
promise.

**Inferred:** a single Jellyfin movie may correspond to multiple Radarr records
across multiple Radarr instances, or to none. Whether a given Radarr instance
"owns" a movie is a configuration/product question, not something the APIs
answer.

**Unresolved:** how to behave when TMDb and IMDb disagree (e.g. a remade title
or an id correction), and whether a title+year candidate should ever be
auto-accepted.

---

## 4. Matching a Jellyfin series/episode to a Sonarr record

Sonarr identity, endpoints and the `episode`/`episodeFile` join are documented in
`docs/sonarr-api.md`.

### 4.1 Series keys

| Key | Jellyfin source | Sonarr target | Status | Caveats |
| --- | --- | --- | --- | --- |
| TVDB id | `ProviderIds["Tvdb"]` | `series.tvdbId` | **Confirmed** (preferred query path in `sonarr-api.md`) | Requires the TVDB provider to have run. |
| TMDb / IMDb / TVMaze | `ProviderIds["Tmdb"|"Imdb"|"TvMaze"]` | `series.tmdbId` / `imdbId` / `tvMazeId` | **Inferred** | Sonarr exposes the fields but there is no documented v3 filter for all of them; would require comparing against a cached Sonarr catalogue. |
| Title + year | `Name`, `ProductionYear` | `series.title`, `series.year` | **Proposed** | Non-unique; manual disambiguation only. |
| Path | `Series.Path` | `series.path` | **Proposed** | Namespace-dependent (§5). |

### 4.2 Episode keys (after a series match)

| Key | Jellyfin source | Sonarr target | Status | Caveats |
| --- | --- | --- | --- | --- |
| Episode TVDB id | `Episode.ProviderIds["Tvdb"]` | `episode.tvdbId` | **Inferred** | Worth validating: episode-level TVDB ids are provider-dependent and may be absent. |
| Season + episode number | `ParentIndexNumber`, `IndexNumber` | `episode.seasonNumber`, `episodeNumber` | **Confirmed** fields on both sides | Numbering scheme must agree; see `DisplayOrder`. |
| Double-episode span | `IndexNumber` + `IndexNumberEnd` | multiple `episode` records | **Inferred** | A Jellyfin multi-episode span may map to more than one Sonarr episode. |
| Absolute / scene numbering | (no dedicated field) | `episode.absoluteEpisodeNumber`, scene fields | **Unresolved** | Anime/special numbering; explicitly flagged as needing validation in `sonarr-api.md`. |
| File path | `Episode.Path` / `MediaSourceInfo.Path` | `episodeFile.path` | **Proposed** | Useful for multi-episode files only when paths are shared (§5). |

**Confirmed join rule (from `sonarr-api.md`):** a Sonarr episode's current file is
the record where `episode.episodeFileId == episodeFile.id`; `hasFile` is only a
convenience flag. An embedded `episodeFile` should be trusted only when its `id`
equals `episodeFileId`. Several episodes may reference the same `episodeFileId`
(multi-episode files).

**Caveats:**

- **`DisplayOrder`**: Jellyfin's `Series.DisplayOrder` (`airdate`/`dvd`/
  `absolute`) affects how season/episode numbers are *interpreted*, and Sonarr
  applies its own numbering. Number-based matching can disagree when display
  orders differ. **Inferred/proposed.**
- **Specials** use season `0`; `AirsBefore/AfterSeasonNumber` can relocate an
  episode relative to `ParentIndexNumber`. **Confirmed fields; mapping
  consequence inferred.**
- **Missing episodes** (`LocationType.Virtual`) have no file and must not produce
  a file/quality badge. **Confirmed.**

---

## 5. Filesystem paths as a fallback

### 5.1 Confirmed Jellyfin path facilities

- `BaseItem.Path`, `MediaSourceInfo.Path`.
- `ILibraryManager.FindByPath(string path, bool? isFolder)`.
- `InternalItemsQuery.Path` (exact path filter).
- `PathProtocol`/`MediaProtocol` (`File`, `Http`, `Rtmp`, `Rtsp`, `Udp`, `Rtp`,
  `Ftp`); `IsFileProtocol` and `LocationType` distinguish local files from remote
  and virtual items.
- `GetMediaSources(enablePathSubstitution)` can return a path after Jellyfin's
  path substitution is applied; `ILibraryManager.GetPathAfterNetworkSubstitution`
  and `GetMappedPath` (internal to `BaseItem`) are the related mechanisms.

### 5.2 Why paths are only a candidate

- **Namespace mismatch (Confirmed as a real hazard, see `sonarr-api.md`):**
  Jellyfin, Sonarr and Radarr are frequently separate containers/hosts with
  different mount roots for the same files. A path that matches literally on one
  host will not match on another.
- **Path substitution:** Jellyfin can rewrite paths per library; the value a
  plugin observes depends on `enablePathSubstitution` and the owner item.
- **`.strm` / remote items:** `LocationType.Remote`/non-`File` protocol paths are
  URLs or shortcut contents, not comparable to Arr file paths.
- **Virtual/missing items:** no path at all.
- **Symlinks, case, separators:** exact string comparison is fragile;
  normalisation (separators, casing, symlinks, trailing slashes) is
  **Proposed**, not confirmed by any single API.
- **Multi-part/version items:** one Jellyfin item can have several
  `MediaSourceInfo.Path` values (§8.3), so "the path" is ambiguous.

**Proposed but unvalidated:** requiring an explicit, user-configured path mapping
between Jellyfin and each Arr instance before using paths at all. No confirmed
Jellyfin or Arr API encodes those mappings for a third party.

---

## 6. Retrieving the actual media file for a Jellyfin item

### 6.1 The Jellyfin side (Confirmed)

For a `Movie` or `Episode` (both `Video`/`IHasMediaSources`):

1. Resolve the item (`ILibraryManager.GetItemById<T>` or via events).
2. Call `GetMediaSources(enablePathSubstitution)` to obtain one
   `MediaSourceInfo` per version/part.

`MediaSourceInfo` fields relevant to identifying a file:

| Field | Notes |
| --- | --- |
| `Id` | The version item's id (string form of a Guid). |
| `Path` | File path or URL. May be empty for placeholders. |
| `Protocol` | `MediaProtocol`; `File` for local files. |
| `Container` | Container/extension, e.g. `mkv`, `mp4`. |
| `Size` | Byte size, if known. |
| `RunTimeTicks` | Duration. |
| `MediaStreams` | Jellyfin's own stream analysis (video/audio/subtitle). |
| `VideoType`, `IsoType`, `Video3DFormat` | Video shape (`VideoFile`, `BluRay`, `Dvd`, `Iso`, …). |
| `Type` | `MediaSourceType` (`Default`, `Grouping`, `Placeholder`); placeholders have no path. |
| `IsRemote`, `ETag` | Remote flag and (for local files) a hash of `DateModified`. |

**Important:** Jellyfin's `MediaStreams` are produced by Jellyfin's own probing.
They describe the file's technical properties but are **not** Sonarr/Radarr
quality classifications, release groups, or custom-format scores.

### 6.2 The Arr side (Confirmed, cross-reference)

- Radarr: one imported file per movie (`movie.hasFile`, `movie.movieFileId`,
  `movie.movieFile`, or `GET /api/v3/moviefile?movieId=`).
- Sonarr: per-episode `episodeFileId` and the `episodeFile` join described in
  `docs/sonarr-api.md`.

### 6.3 Joining Jellyfin sources to Arr files

**Proposed options:**

- Primary candidate: use the provider-id match to obtain the Arr record, then
  read the Arr file resource directly (quality/media info come from Arr). The
  Jellyfin media source is then only needed to confirm the item is a real local
  file (`LocationType`, `IsFileProtocol`).
- Secondary candidate: compare `MediaSourceInfo.Path` against
  `movieFile.path` / `episodeFile.path` after configured remapping (§5).

**Unresolved:** whether a Jellyfin media source should be individually matched to
an Arr file, or whether matching happens only at the item level.

---

## 7. Actual quality versus requested quality

This is documented in full in the Arr references; the mapping-relevant summary:

- **Actual file quality** comes only from the Arr file resource:
  Radarr `movieFile.quality` (`QualityModel`) / Sonarr `episodeFile.quality`.
  Compare with `docs/radarr-api.md` §5–6 and `docs/sonarr-api.md` §"Actual file
  quality versus quality profile".
- **Requested/configured quality** is policy: Radarr
  `movie.qualityProfileId` → `GET /api/v3/qualityprofile/{id}`; Sonarr
  `series.qualityProfileId`/`profileName`. It is not the file's quality.
- **Upgrade-pending signal:** `qualityCutoffNotMet` on the Arr file resource
  (Radarr computed against the movie profile; Sonarr likewise).
- **Custom formats/score:** `customFormats`/`customFormatScore` on the Arr file
  resource; Radarr only populates these on `GET /api/v3/moviefile?movieId=`, not
  on the embedded `movieFile` in the movie list (`docs/radarr-api.md` §4.1).

**Confirmed separation:** Jellyfin exposes no equivalent of any of these. A badge
labelled with quality must derive its text from the Arr record, not from
Jellyfin's `MediaStreams` or `Container` alone.

---

## 8. Upgrades, replacements, and multiple files

### 8.1 Radarr

**Confirmed (from `radarr-api.md`):** Radarr keeps at most one imported file per
movie; an upgrade replaces or deletes the previous file, changing `movieFileId`,
`movieFile.path`, `size`, `dateAdded`, `quality`, and `customFormat*`.
`GET /history/movie` records `downloadFolderImported` / `movieFileDeleted` /
`movieFileRenamed`. The webhook `Download` event carries `isUpgrade` and
`deletedFiles[]`; there is no distinct `onUpgrade` event type.

### 8.2 Sonarr

**Confirmed (from `sonarr-api.md`):** `episode.episodeFileId` is the authoritative
current-file association; several episodes may share one file; a changed file id
is a definite refresh trigger; `dateAdded`/`size` are not sufficient upgrade
detectors on their own.

### 8.3 Jellyfin multiple files/versions

Jellyfin 12 models multiple files in three ways (Confirmed members in `Video`):

| Mechanism | Members | Consequence for mapping |
| --- | --- | --- |
| Local alternate versions | `LocalAlternateVersions`, `PrimaryVersionId`, `ILibraryManager.GetLocalAlternateVersionIds` | One primary item plus sibling items sharing provider ids; `GetMediaSources` returns one source per version. |
| Linked alternate versions | `LinkedAlternateVersions`, `ILibraryManager.GetLinkedAlternateVersions` | Separately scanned items (e.g. editions, cuts) linked as versions. |
| Additional parts (stacked) | `AdditionalParts`, `IsStacked` | One logical item spanning multiple files. |
| General query visibility | `InternalItemsQuery.IncludeOwnedItems` | Non-primary versions/parts are excluded unless explicitly included. |

**Proposed options when a Jellyfin item maps to more than one file:**

- Represent every `MediaSourceInfo` and match each to an Arr file; or
- Select one representative source and document which; or
- Refuse to badge and report ambiguity.

**Confirmed impossibility:** Sonarr/Radarr do not provide a single canonical
"quality of all versions" aggregate; that is a product decision.

### 8.4 Fingerprint inputs (Proposed)

A stored fingerprint sufficient to decide "has the badge-relevant state changed"
should include at least: Jellyfin item id, Jellyfin provider id(s), Arr connection
identity, Arr record id, Arr file id, Arr `quality`, Arr `customFormatScore`,
`qualityCutoffNotMet`, languages, selected `MediaInfo` values, and the badge
renderer version. This mirrors the fingerprint guidance in `sonarr-api.md` and
the caching guidance in `docs/jellyfin-12-architecture.md` §8.

### 8.5 Refresh triggers (Confirmed facilities)

- Jellyfin `ILibraryManager.ItemAdded`/`ItemUpdated`/`ItemRemoved` events.
- Jellyfin scheduled task / post-scan reconciliation (architecture doc §6–7).
- Sonarr/Radarr outbound webhooks as low-latency hints (see the Arr docs; webhook
  authentication is not provided by the inspected Arr source and must be handled
  at the Jellyfin boundary).

---

## 9. Failure handling

The architecture doc ("Unclear items") and GOALS ("fail gracefully") set the
expectations; this table maps the matching-specific cases.

| Situation | Confirmed/Inferred behaviour available | Notes |
| --- | --- | --- |
| No Arr record found | No matching record returned | Render no badge rather than a guess. |
| Ambiguous match (multiple candidates) | APIs return multiple records | **Proposed:** do not auto-accept; require manual disambiguation or skip. |
| Missing provider IDs on the Jellyfin item | `GetProviderId` returns null | Fall back to other candidate keys, or skip. **Inferred** that this is common without providers configured. |
| Arr unavailable / timeout / 401 | See Arr error sections | Preserve last known state; bounded backoff; never block Jellyfin. |
| Partial Arr metadata | Fields are nullable/optional | Degrade the badge; e.g. absent Radarr `customFormats`. |
| Item has no file | Radarr `hasFile=false`; Sonarr `hasFile=false`; Jellyfin virtual/missing | No file-quality badge. |
| Non-file item (`.strm`, remote) | `LocationType.Remote` / non-`File` protocol | Independent of Arr file matching; requires an explicit policy. |
| Multiple Jellyfin versions | `MediaSourceCount > 1` | Ambiguous file choice (§8.3). |
| Multiple Arr instances | Not expressed by any API | Configuration/product concern. |

---

## 10. Confirmed / inferred / proposed / unresolved matrix

| Topic | Status |
| --- | --- |
| Jellyfin `ProviderIds` keys and normalisation | **Confirmed** |
| `MetadataProvider` enum names | **Confirmed** |
| Jellyfin `ILibraryManager` item/query APIs | **Confirmed** |
| `InternalItemsQuery` filters (`HasAnyProviderId`, `Path`, numbering, `IncludeOwnedItems`) | **Confirmed** |
| `IHasMediaSources.GetMediaSources` / `MediaSourceInfo` shape | **Confirmed** |
| Jellyfin models alternate versions and additional parts | **Confirmed** |
| Radarr exposes `tmdbId`/`imdbId` and `movieFile.quality` | **Confirmed** (Arr docs) |
| Sonarr exposes `tvdbId`, `episodeFileId`, `episodeFile.quality` | **Confirmed** (Arr docs) |
| Jellyfin has no quality/quality-profile concept | **Confirmed** |
| Jellyfin `Tmdb` id is the same key Radarr calls `tmdbId` | **Inferred** (external contract) |
| Jellyfin episode-level `Tvdb` ids are reliably present | **Inferred / unresolved** |
| Paths are comparable across Jellyfin and Arr | **Proposed** (namespace-dependent) |
| Title+year matching | **Proposed** (tie-breaker at most) |
| Season/series poster aggregation policy | **Unresolved** |
| Anime/absolute/scene numbering | **Unresolved** |
| Multi-episode and multi-version file selection | **Unresolved** |
| Webhook authentication at the Jellyfin boundary | **Unresolved** |

---

## 11. Unresolved questions before implementation

1. Which Jellyfin provider IDs are actually populated across real libraries
   (movies: TMDb/IMDb; series: TVDB/TMDb/IMDb; episodes: TVDB), and what fraction
   of items have none?
2. Are episode-level TVDB ids reliably present and stable across metadata
   refreshes, or must number-based matching be the norm?
3. How should `Series.DisplayOrder` (airdate/dvd/absolute) interact with Sonarr's
   numbering, and what happens when they disagree?
4. How should double-episodes (`IndexNumberEnd`) and multi-episode files map to
   Sonarr `episodeFileId`?
5. What is the policy for a Jellyfin item with multiple media sources/versions
   (`MediaSourceCount > 1`, stacked parts) — per-source badges, one chosen source,
   or no badge?
6. What are the badge semantics for Season/Series posters when no single file
   exists: none, "mixed", lowest, highest, or an explicit summary?
7. Can a single Jellyfin library reliably correspond to one Arr instance, or
   must each connection be independently scoped/disambiguated?
8. Should a path fallback be offered at all, and if so, how are Jellyfin↔Arr path
   mappings configured and secured?
9. How are `.strm`, remote, virtual/missing and placeholder items handled?
10. What is the retry/backoff and "Arr unavailable" policy, and when is a
    previously rendered badge reverted versus preserved?
11. How is the Arr outbound webhook endpoint authenticated (the inspected Arr
    source does not sign payloads)?
12. What exact Jellyfin 12.x patch and package versions will be pinned and used
    for validation fixtures (see architecture doc, "Unclear items")?

---

## 12. References

**Jellyfin `v12.0` source:**

- `BaseItem`: <https://github.com/jellyfin/jellyfin/blob/v12.0/MediaBrowser.Controller/Entities/BaseItem.cs>
- `Video`: <https://github.com/jellyfin/jellyfin/blob/v12.0/MediaBrowser.Controller/Entities/Video.cs>
- `Movie`: <https://github.com/jellyfin/jellyfin/blob/v12.0/MediaBrowser.Controller/Entities/Movies/Movie.cs>
- `Series`: <https://github.com/jellyfin/jellyfin/blob/v12.0/MediaBrowser.Controller/Entities/TV/Series.cs>
- `Season`: <https://github.com/jellyfin/jellyfin/blob/v12.0/MediaBrowser.Controller/Entities/TV/Season.cs>
- `Episode`: <https://github.com/jellyfin/jellyfin/blob/v12.0/MediaBrowser.Controller/Entities/TV/Episode.cs>
- `IHasMediaSources`: <https://github.com/jellyfin/jellyfin/blob/v12.0/MediaBrowser.Controller/Entities/IHasMediaSources.cs>
- `InternalItemsQuery`: <https://github.com/jellyfin/jellyfin/blob/v12.0/MediaBrowser.Controller/Entities/InternalItemsQuery.cs>
- `ILibraryManager`: <https://github.com/jellyfin/jellyfin/blob/v12.0/MediaBrowser.Controller/Library/ILibraryManager.cs>
- `MetadataProvider`: <https://github.com/jellyfin/jellyfin/blob/v12.0/MediaBrowser.Model/Entities/MetadataProvider.cs>
- `ProviderIdsExtensions`: <https://github.com/jellyfin/jellyfin/blob/v12.0/MediaBrowser.Model/Entities/ProviderIdsExtensions.cs>
- `MediaSourceInfo`: <https://github.com/jellyfin/jellyfin/blob/v12.0/MediaBrowser.Model/Dto/MediaSourceInfo.cs>
- `MediaProtocol`: <https://github.com/jellyfin/jellyfin/blob/v12.0/MediaBrowser.Model/MediaInfo/MediaProtocol.cs>

**Project references:**

- `docs/jellyfin-12-architecture.md`
- `docs/sonarr-api.md`
- `docs/radarr-api.md`
- `GOALS.md`
