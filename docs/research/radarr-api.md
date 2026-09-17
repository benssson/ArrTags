# Radarr API reference (ArrTags integration)

## Scope, evidence, and terminology

This is persistent technical context for ArrTags' Radarr integration. It is a
concise API reference for future development sessions, not a tutorial. It is
evidence and reference material for the accepted architecture in
[`../architecture.md`](../architecture.md) and
[`../decisions.md`](../decisions.md); it does not define ArrTags architecture or
phases. It covers only what ArrTags needs to match a Jellyfin movie to a Radarr
movie, read the *actual* file quality (not merely the configured quality
profile), and keep badges current.

**Evidence labels used throughout:**

- **Confirmed** — verified against the Radarr `develop` source tree and/or the
  generated OpenAPI 3.0.4 spec (`info.version` `3.0.0`) at the referenced
  permalink/path. API shape can still differ on an older installed release.
- **Inferred** — reasoned from source/spec but not directly exercised; validate
  against the target instance.
- **Assumption – validate** — not yet established; must be checked before
  relying on it.

**Authoritative sources (inspected):**

- OpenAPI spec: `src/Radarr.Api.V3/openapi.json` on `develop`
  (<https://raw.githubusercontent.com/Radarr/Radarr/develop/src/Radarr.Api.V3/openapi.json>,
  also published at <https://radarr.video/docs/api>).
- Auth: `src/Radarr.Http/Authentication/ApiKeyAuthenticationHandler.cs`,
  `AuthenticationBuilderExtensions.cs`, `NoAuthenticationHandler.cs`,
  `AuthenticationService.cs`, `UiAuthorizationPolicyProvider.cs`.
- Errors: `src/Radarr.Http/ErrorManagement/RadarrErrorPipeline.cs`.
- Movies/files: `src/Radarr.Api.V3/Movies/MovieController.cs`,
  `Movies/MovieResource.cs`, `MovieFiles/MovieFileController.cs`,
  `MovieFiles/MovieFileResource.cs`.
- Serialization: `src/NzbDrone.Common/Serializer/System.Text.Json/STJson.cs`,
  `.../Newtonsoft.Json/Json.cs`.
- Webhooks: `src/NzbDrone.Core/Notifications/Webhook/*` (`WebhookBase.cs`,
  `WebhookProxy.cs`, `WebhookEventType.cs`, `WebhookMovie.cs`,
  `WebhookMovieFile.cs`, `WebhookMovieFileMediaInfo.cs`,
  `WebhookImportPayload.cs`, `WebhookRemoteMovie.cs`,
  `WebhookCustomFormatInfo.cs`, `WebhookGrabbedRelease.cs`).
- Media info formatting: `src/NzbDrone.Core/MediaFiles/MediaInfo/MediaInfoFormatter.cs`.

**Baseline versions.** The v3 API surface is shared across Radarr v3, v4, v5 and
v6. At the time of writing the current stable release line is Radarr `6.4.x`
(Aug 2026) and the spec still reports `3.0.0`. Pin the exact Radarr version you
test against and treat unknown/missing JSON fields tolerantly (see §14).

---

## 1. Authentication and API key handling

**Confirmed** — the API-key handler (`ApiKeyAuthenticationHandler`) reads the key
from, in order:

1. query parameter `apikey`;
2. request header `X-Api-Key`;
3. `Authorization: Bearer <key>`.

A matching key yields an authenticated principal. A missing/non-matching key
produces `AuthenticateResult.NoResult()`; an authorization challenge returns
**HTTP 401** (`HandleChallengeAsync`) and a forbidden result returns **403**.

The key is shown in **Settings → General → Security → API Key** and persisted in
Radarr's `config.xml` as `<ApiKey>...</ApiKey>`. Instances may also be behind a
reverse proxy that injects authentication.

**Confirmed** — when the instance's `AuthenticationType` is `None`, the
registered default scheme is `NoAuthenticationHandler`, which always succeeds as
an anonymous principal. In that mode API requests are **not** gated by an API
key. When authentication is `Basic`, `Forms`, or `External`, API calls must
authenticate with the API key (the UI uses a cookie).

**Assumption – validate** — whether a specific deployment enforces the key
therefore depends on its `authentication` setting (reported by
`GET /api/v3/system/status` → `authentication` with values `none`, `basic`,
`forms`, `external`). Always send `X-Api-Key`; it is harmless when auth is
disabled.

**Practical guidance**

- Prefer the `X-Api-Key` header. The `apikey` query parameter and Bearer form
  work, but query strings are far more likely to be captured in proxy/access
  logs.
- Never log the key. Redact it in diagnostics.
- Jellyfin should store the key as configuration and avoid putting it in URLs.

---

## 2. Base URL and connection configuration

**Confirmed**

- Default listen address: `http://<host>:7878` (TCP port `7878`).
- API is versioned under `/api/v3`. The API-info endpoint is `GET /api` (no
  `/v3`).
- If the instance is configured with a **URL Base**, every request must include
  it, e.g. `http://host:7878/radarr/api/v3/movie`. `UrlBaseMiddleware` issues a
  `307` redirect to the URL-base-prefixed path when it is missing; following it
  on POST can be lossy, so configure the base explicitly.
- TLS: HTTPS is supported; certificate validation strictness is a Radarr
  setting. A plugin HTTP client should honour the configured exception policy if
  self-signed certificates are used, but not disable validation globally.

**Capability/version probes**

| Method | Endpoint | Purpose | Notes |
| --- | --- | --- | --- |
| GET | `/api` | API info | Returns `ApiInfoResource { current, deprecated[] }`. Requires a key when auth is enabled. |
| GET | `/api/v3/system/status` | Server + build info | Returns `SystemResource`; includes `version`, `branch`, `authentication`, `urlBase`, `osName`, `isDocker`. |
| GET | `/api/v3/health` | Health checks | Returns `HealthResource[]`. **There is no `/api/v3/system/health`.** |
| GET | `/ping` | Lightweight liveness | **Inferred** (observed in logs) — unauthenticated `200` used for reachability; do not depend on its body. |

Use `/api` (`deprecated[]`) plus `SystemResource.version` as the feature-detection
baseline rather than assuming a fixed release.

---

## 3. Identifying a Radarr movie from a Jellyfin item

**Confirmed identifiers exist on `MovieResource`:** `tmdbId` (int) and `imdbId`
(string). Radarr's internal `id` is its own database key and is not present in
Jellyfin.

**Confirmed endpoints for resolving a movie:**

| Method | Endpoint | Required params | Purpose |
| --- | --- | --- | --- |
| GET | `/api/v3/movie` | optional `tmdbId` | List all movies, or the one matching `tmdbId`. Each item embeds `movieFile`. |
| GET | `/api/v3/movie/{id}` | path `id` | Single movie by Radarr internal id. |
| GET | `/api/v3/movie/lookup/tmdb` | `tmdbId` | Look up a single movie on TMDb (network egress). |
| GET | `/api/v3/movie/lookup/imdb` | `imdbId` | Look up a single movie on IMDb. |
| GET | `/api/v3/movie/lookup` | `term` | Free-text search (network egress); returns an array. |

**Recommended matching strategy (for ArrTags):**

1. Read the Jellyfin `Movie` item's provider IDs (TMDb and IMDb).
2. Prefer `GET /api/v3/movie?tmdbId=<id>` because it returns the movie *only if
   it already exists in the Radarr library* and includes `movieFile`, `hasFile`,
   `qualityProfileId` and `statistics` in one call.
3. Fall back to `imdbId` matching against the `GET /api/v3/movie` list
   (`movie.imdbId`, case-sensitive string such as `tt0111161`).
4. Do **not** match on filesystem paths or `rootFolderPath` as the primary key:
   Radarr paths may be remote/container mappings that differ from Jellyfin's.
   Paths are a reasonable last-resort/validation signal only.
5. **Assumption – validate** — the exact Jellyfin provider-ID keys (`Tmdb`,
   `Imdb`, etc.) and whether Jellyfin 12 exposes them on all movie items. Treat a
   missing/ambiguous match as "no badge", never as an error affecting Jellyfin.
6. Radarr is movies-only. TV/anime are Sonarr's domain; ArrTags' Radarr worker
   should ignore `Series`/`Season`/`Episode` items.
7. A Jellyfin movie can exist in more than one Radarr instance/library
   configuration. Matching must be scoped to the configured Radarr connection
   and should surface ambiguity rather than guessing.

**Caveat (`Confirmed`):** `/movie/lookup*` endpoints require Radarr to reach
TMDb/IMDb over the internet. They are for discovery, not for routine badge
refreshes; prefer matching against the local library list.

---

## 4. API endpoints

All paths below are prefixed with `/api/v3` unless noted. All require the API key
when authentication is enabled. Responses are JSON, camelCase, with null
properties omitted (`STJson`: `DefaultIgnoreCondition = WhenWritingNull`) — treat
an absent field as null. String enums are camelCase in the REST API (`STJson`
`JsonStringEnumConverter(JsonNamingPolicy.CamelCase, true)`), so unknown enum
values deserialize as their numeric/string form.

### 4.1 Movies

#### `GET /api/v3/movie`

- **Query:** `tmdbId` (int, optional), `excludeLocalCovers` (bool, default
  `false`), `languageId` (int, optional).
- **Returns:** `MovieResource[]`.
- **Confirmed important behaviour:** each returned `MovieResource` embeds
  `movieFile` (mapped with `qualityCutoffNotMet`) and `statistics`, and sets
  `hasFile`/`sizeOnDisk` from statistics.
- **Confirmed caveat:** the list endpoint maps `movieFile` **without** a
  `ICustomFormatCalculationService`, so embedded `movieFile.customFormats` and
  `movieFile.customFormatScore` are null/missing. Call
  `GET /api/v3/moviefile?movieId=<id>` when custom formats are needed.
- **Caveat:** large libraries return everything in one response; this is still
  the cheapest way to reconcile all movies. Do not poll it aggressively.

#### `GET /api/v3/movie/{id}`

- **Path:** `id` (int, Radarr internal id), required.
- **Returns:** `MovieResource` (with embedded `movieFile`, same custom-format
  caveat as above).

#### `POST /api/v3/movie` / `PUT /api/v3/movie/{id}` / `DELETE /api/v3/movie/{id}`

Write operations. **Out of scope** for ArrTags (goal: read-only metadata). Listed
only to avoid accidental use.

### 4.2 Movie files

#### `GET /api/v3/moviefile`

- **Query:** `movieId` (int, repeatable) **or** `movieFileIds` (int, repeatable);
  at least one is required or Radarr returns `400 Bad Request` with
  `"movieId or movieFileIds must be provided"`.
- **Returns:** `MovieFileResource[]`.
- **Confirmed important behaviour:** this endpoint *does* pass
  `ICustomFormatCalculationService`, so `customFormats`, `customFormatScore`,
  and `qualityCutoffNotMet` are populated. This is the endpoint to use when
  badge content depends on custom formats or media info.

#### `GET /api/v3/moviefile/{id}`

- **Path:** `id` (int), required.
- **Returns:** `MovieFileResource` (fully populated, same as above).

#### `PUT /api/v3/moviefile/{id}`, `PUT/DELETE /api/v3/moviefile/bulk`, `PUT /api/v3/moviefile/editor`

Write operations. **Out of scope.** Note `/moviefile/editor` is deprecated in
favour of `/moviefile/bulk`.

### 4.3 Quality profiles and definitions

#### `GET /api/v3/qualityprofile`

- **Returns:** `QualityProfileResource[]`. Cache this; profiles change rarely.

#### `GET /api/v3/qualityprofile/{id}`

- **Path:** `id` (int), required.
- **Returns:** `QualityProfileResource`.

#### `GET /api/v3/qualityprofile/schema`

- **Returns:** a `QualityProfileResource` template describing all qualities and
  groups. Useful for understanding the profile tree; not needed for reads.

#### `GET /api/v3/qualitydefinition`

- **Returns:** `QualityDefinitionResource[]` (per-quality size limits and
  `title`/`weight` ordering). Not required for badges but useful context.

### 4.4 System

#### `GET /api/v3/system/status`

- **Returns:** `SystemResource` (see §10). Use `version`, `authentication`,
  `urlBase`, `branch`, `isDocker`.

#### `GET /api`

- **Returns:** `ApiInfoResource { current, deprecated[] }`. Use to detect the
  live API version and which versions are deprecated.

#### `GET /api/v3/health`

- **Returns:** `HealthResource[]` where `type` ∈ `ok | notice | warning | error`.
  Surface connectivity/health as plugin status, not as a Jellyfin failure.

### 4.5 Supporting reads

#### `GET /api/v3/parse`

- **Query:** `title` (string, optional).
- **Returns:** `ParseResource` — how Radarr would parse a release title. Useful
  for diagnostics only.

#### `GET /api/v3/history/movie`

- **Query:** `movieId` (int, optional), `eventType` (`MovieHistoryEventType`,
  optional), `includeMovie` (bool, default `false`).
- **Returns:** `HistoryResource[]`. Useful to explain *why* a file changed
  (import vs delete vs rename).

#### `GET /api/v3/mediacover/{movieId}/{filename}`

- **Path:** `movieId` (int), `filename` matching `(.+)\.(jpg|png|gif)`.
- **Returns:** binary image.
- `MovieResource.images[].url` is normally rewritten to a local
  `/api/v3/mediacover/...` URL unless `excludeLocalCovers=true`. ArrTags should
  **not** rewrite the poster from Radarr (badges are rendered Jellyfin-side), but
  the endpoint is useful if an original-poster backup is ever desired.

---

## 5. Quality representation

**Confirmed** — a movie file's quality is a `QualityModel`, not a scalar:

```json
"quality": {
  "quality": { "id": 7, "name": "Bluray-1080p", "source": "bluray", "resolution": 1080, "modifier": "none" },
  "revision": { "version": 1, "real": 0, "isRepack": false }
}
```

- `QualityModel` = `{ quality: Quality, revision: Revision }`.
- `Quality` = `{ id: int, name: string, source: QualitySource, resolution: int, modifier: Modifier }`.
  - `QualitySource` enum (camelCase in REST): `unknown`, `cam`, `telesync`,
    `telecine`, `workprint`, `dvd`, `tv`, `webdl`, `webrip`, `bluray`.
  - `Modifier` enum: `none`, `regional`, `screener`, `rawhd`, `brdisk`, `remux`.
  - `resolution` is an integer (e.g. `480`, `720`, `1080`, `2160`).
  - `name` is the human-facing quality label (e.g. `Bluray-1080p`,
    `Remux-2160p`, `WEBDL-1080p`, `WEBRip-1080p`, `HDTV-720p`). Badge text should
    generally use `name`; use `resolution`/`source`/`modifier` if a more compact
    custom label is desired.
- `Revision` = `{ version: int, real: int, isRepack: bool }` (`version` 1 =
  release, 2 = proper, etc.).

`Quality` is an **object descriptor**, not an enum. Do not assume the quality
`id` values are stable across Radarr versions; key off `name`/`resolution`/`source`
when rendering badges, and off `id` only for identity comparison within a single
instance.

---

## 6. Configured (requested) quality vs actual file quality

This is the section to get right.

### Actual file quality (confirmed)

- `movieFile.quality` (`QualityModel`) — the quality Radarr detected/assigned to
  the file on disk.
- `movieFile.mediaInfo` (`MediaInfoResource`) — ffprobe-derived technical detail
  (codec, resolution, HDR, audio), which can differ from the parsed quality tag.
- `movieFile.customFormats` (`CustomFormatResource[]`) and
  `movieFile.customFormatScore` (int) — matched custom formats and their summed
  score, from the file endpoint.
- `movieFile.qualityCutoffNotMet` (bool) — **Confirmed** computed as
  `IUpgradableSpecification.QualityCutoffNotMet(movie.QualityProfile, movieFile.Quality)`.
  `true` means the file has **not** yet reached the profile's cutoff (an upgrade
  is still desirable/possible); `false` means it is at or beyond cutoff.

### Configured/requested quality (confirmed)

- `movie.qualityProfileId` identifies the profile.
- `GET /api/v3/qualityprofile/{id}` returns `QualityProfileResource`:
  - `upgradeAllowed` (bool) — whether upgrades are permitted.
  - `cutoff` (int) — the **id** of the quality/group that ends upgrades.
  - `items` (`QualityProfileQualityItemResource[]`) — an **ordered tree**;
    `allowed` says whether a quality/group is enabled. Leaf items carry a
    `quality`; group items carry nested `items` and their own `id`.
  - `minFormatScore`, `cutoffFormatScore`, `minUpgradeFormatScore` (ints) —
    custom-format score gates.
  - `formatItems` (`ProfileFormatItemResource[]`) = `{ id, format, name, score }`
    — per-custom-format score within the profile.
  - `language` (Language).

**Resolving target vs actual:**

- To find the profile's cutoff/most-preferred quality name, walk `items`,
  filter `allowed == true`, and map the item whose `id == profile.cutoff` (or the
  top allowed leaf) back to its `quality.name`.
- Compare against `movieFile.quality.quality.name` only for display. **Prefer
  `qualityCutoffNotMet`** as the authoritative "is this file below the requested
  level / pending upgrade" signal — it already encodes cutoff, ordering, and
  upgrade rules, and avoids reimplementing Radarr's comparison logic.
- Custom-format *targets* are profile-side (`cutoffFormatScore`); the file's
  matched formats/score come from `movieFile`. Do not conflate the two.

### Badge policy implication

- "Actual quality" badges → `movieFile.quality.quality.name` plus `mediaInfo`
  (`videoDynamicRangeType`, `audioCodec`, etc.).
- "Upgrade pending" badges → `movieFile.qualityCutoffNotMet` (and optionally
  `profile.upgradeAllowed`).
- "Requested quality" badges → resolved from the quality profile.
- Never present the profile's target as if it were the file's quality.

---

## 7. Identifying the actual file and handling upgrades

**Confirmed**

- Radarr keeps at most **one** imported file per movie (unlike Sonarr episodes),
  so there is no multi-file aggregation to perform.
- Presence and linkage:
  - `movie.hasFile` (nullable bool; set from `statistics.movieFileCount > 0`).
  - `movie.movieFileId` (int; `0` when no file).
  - `movie.movieFile` (embedded `MovieFileResource` when present).
  - Or `GET /api/v3/moviefile?movieId=<id>` for the fully populated file.
- A file can be absent (`hasFile == false` / `movieFileId == 0`) even when the
  movie exists; render a "no file"/"missing" style badge, not an error.

**Upgrades and replacement files**

- When Radarr imports a better release it replaces or deletes the previous file.
  The movie's `movieFileId`, `movieFile.path`, `size`, `dateAdded`,
  `quality`, and `customFormat*` all change; `movie.lastSearchTime` or
  `statistics` may also change.
- `movieFile.qualityCutoffNotMet` flips to `false` once the cutoff is met.
- History (`GET /api/v3/history/movie?movieId=`) exposes
  `downloadFolderImported` / `movieFileDeleted` / `movieFileRenamed` events.
- **Confirmed webhook signal:** an import (initial or upgrade) fires eventType
  `Download`; upgrades additionally set `isUpgrade: true` and populate
  `deletedFiles[]` with the replaced file(s). There is **no distinct
  `onUpgrade` event type** — the notification setting labelled "On Upgrade" is
  the `Download` event with `isUpgrade == true`.
- ArrTags should treat any change to `movieFileId` or a file-quality fingerprint
  as "re-render", and re-read the movie/file before committing work (the file can
  change while a render is in flight).

---

## 8. Other metadata useful for poster badges

All confirmed present on the relevant resource unless noted.

**From `MovieResource`:** `title`, `year`, `status` (`MovieStatusType`),
`monitored`, `isAvailable`, `certification`, `genres[]`, `keywords[]`,
`ratings` (`imdb`, `tmdb`, `metacritic`, `rottenTomatoes`, `trakt`; each
`{ votes, value, type }`), `studio`, `runtime`, `collection`
(`MovieCollectionResource { title, tmdbId }`), `popularity`, `hasFile`,
`sizeOnDisk`, `runtime`, `originalLanguage`, `added`, `tags[]`.

**From `MovieFileResource`:** `releaseGroup`, `edition`, `languages[]`,
`size`, `dateAdded`, `sceneName`, `originalFilePath`, `customFormats[]`,
`customFormatScore`, `indexerFlags`, `qualityCutoffNotMet`.

**From `MediaInfoResource` (confirmed field names):** `audioBitrate`,
`audioChannels`, `audioCodec`, `audioLanguages`, `audioStreamCount`,
`videoBitDepth`, `videoBitrate`, `videoCodec`, `videoFps`, `videoDynamicRange`,
`videoDynamicRangeType`, `resolution`, `runTime`, `scanType`, `subtitles`.

**Confirmed formatting semantics** (`MediaInfoFormatter`):

- `videoCodec`: normalized strings such as `x264`, `x265`, `AVC`, `HEVC`, `AV1`,
  `VC1`, `MPEG2`, `VP9`.
- `audioCodec`: strings such as `TrueHD Atmos`, `TrueHD`, `DTS-HD MA`, `DTS-X`,
  `EAC3`, `AC3`, `AAC`, `HE-AAC`, `FLAC`, `Opus`, `PCM`.
- `videoDynamicRange`: `"HDR"` when the file has any HDR format, otherwise `""`.
- `videoDynamicRangeType`: `DV`, `DV HDR10`, `DV HDR10Plus`, `DV HLG`,
  `DV SDR`, `HDR10`, `HDR10Plus`, `HLG`, `PQ`, or `""`.

Example badge candidates: `Bluray-1080p`, `Remux-2160p`, `DV`, `HDR10`,
`TrueHD Atmos`, `x265`, `releaseGroup`.

---

## 9. Error responses and connection failures

**Confirmed** from `RadarrErrorPipeline`.

- **Missing/invalid API key (auth enabled):** `401 Unauthorized`. (Forbidden =
  `403`.)
- **Validation failure (`FluentValidation`):** `400 Bad Request` whose body is a
  JSON **array** of failures, camelCase fields:
  `[{ "propertyName", "errorMessage", "attemptedValue", "customState", "severity" }]`.
- **Model not found:** `404 Not Found`.
- **Conflict (e.g. SQLite constraint):** `409 Conflict`.
- **API exception:** the exception's status code with body
  `ErrorModel { "message", "description" }`.
- **Unhandled:** `500 Internal Server Error` with `ErrorModel`.

**Connection-level failures to handle gracefully (ArrTags must never surface
these to Jellyfin as failures):**

- DNS resolution failure, connection refused, TCP/TLS timeout.
- TLS certificate validation errors (self-signed/proxy).
- `307` redirect due to a missing URL Base — configure the base instead.
- A response that is **HTML** (login page) rather than JSON → almost always a
  wrong key or a reverse proxy intercepting the request. Treat non-JSON content
  as an auth/proxy condition, not a parse bug.
- Truncated/empty bodies due to network issues (observed in Radarr issues).

Recommended policy: bounded retries with backoff for transient transport errors
(not for 4xx), cancellation support, redacted logging, and a cached "last known
state" so a Radarr outage leaves existing badges intact.

---

## 10. Rate limiting and practical API considerations

- **No general API rate limiter was found** in the inspected source/spec: there
  are no `X-RateLimit-*`/`Retry-After` semantics documented for `/api/v3`, and no
  `429` in the API error pipeline. **Inferred.** Radarr's login lockout applies
  to form-based authentication, not API-key auth. **Assumption – validate** for a
  given reverse proxy, which may impose its own limits.
- Beware proportional cost, not rate limits: `GET /api/v3/movie` serializes the
  whole library and generally should not be polled on a short interval.
- **Minimise requests:**
  - One `GET /api/v3/movie` gives every movie plus embedded `movieFile`
    (`quality`, `mediaInfo`, `qualityCutoffNotMet`) — enough for most badges.
  - Only call `GET /api/v3/moviefile?movieId=` when `customFormats`,
    `customFormatScore`, or fully-populated media info are required.
  - Cache `GET /api/v3/qualityprofile` (small, rarely changes).
  - Cache `/api` and `/api/v3/system/status` for the process lifetime.
- Bound concurrency and honour `CancellationToken`. Do not block Jellyfin
  threads. Reuse a single `HttpClient`/factory-managed client.
- Set explicit, conservative timeouts; Radarr can be slow while importing.

---

## 11. Webhooks / events (keeping badges current)

**Confirmed** — Radarr has a first-class Webhook connection (Settings → Connect →
Webhook). It is configured via `GET/POST /api/v3/notification` (tag
`Notification`) with `implementation: "Webhook"`. Configurable fields include the
target URL, HTTP `POST`/`PUT`, optional basic-auth username/password, and custom
headers. The webhook sends `application/json` with **camelCase property names**
(Newtonsoft `CamelCasePropertyNamesContractResolver`, `Json.cs`).

**Confirmed event flags** (`NotificationResource`): `onGrab`, `onDownload`,
`onUpgrade`, `onRename`, `onMovieAdded`, `onMovieDelete`, `onMovieFileDelete`,
`onMovieFileDeleteForUpgrade`, `onHealthIssue`, `onHealthRestored`,
`onApplicationUpdate`, `onManualInteractionRequired` (each with a matching
`supportsOn*` flag).

**Confirmed `eventType` values.** `WebhookEventType` carries an explicit
Newtonsoft `StringEnumConverter` with `DefaultNamingStrategy`, so event type
values are **PascalCase even though other property names are camelCase**:

`Test`, `Grab`, `Download`, `Rename`, `MovieDelete`, `MovieFileDelete`, `Health`,
`ApplicationUpdate`, `MovieAdded`, `HealthRestored`, `ManualInteractionRequired`.

**Confirmed base payload:** `{ eventType, instanceName, applicationUrl }`.

**Confirmed `Download` (import/upgrade) payload** (`WebhookImportPayload`):

```json
{
  "eventType": "Download",
  "instanceName": "Radarr",
  "applicationUrl": "",
  "movie": {
    "id": 123,
    "title": "Example",
    "year": 2020,
    "filePath": "/movies/Example/Example.2020.mkv",
    "folderPath": "/movies/Example",
    "tmdbId": 603,
    "imdbId": "tt0133093",
    "overview": "...",
    "genres": ["Action"],
    "images": [{ "coverType": "poster", "url": "/api/v3/mediacover/123/poster.jpg", "remoteUrl": "..." }],
    "tags": ["4k"],
    "originalLanguage": { "id": 1, "name": "English" }
  },
  "remoteMovie": { "tmdbId": 603, "imdbId": "tt0133093", "title": "Example", "year": 2020 },
  "movieFile": {
    "id": 456,
    "relativePath": "Example.2020.mkv",
    "path": "/movies/Example/Example.2020.mkv",
    "quality": "Bluray-1080p",
    "qualityVersion": 1,
    "releaseGroup": "GROUP",
    "sceneName": "Example.2020.1080p.BluRay.x264-GROUP",
    "indexerFlags": "",
    "size": 1234567890,
    "dateAdded": "2026-01-01T00:00:00Z",
    "languages": [{ "id": 1, "name": "English" }],
    "mediaInfo": {
      "audioChannels": 6.0,
      "audioCodec": "DTS-HD MA",
      "audioLanguages": ["English"],
      "height": 1080,
      "width": 1920,
      "subtitles": [],
      "videoCodec": "x264",
      "videoDynamicRange": "HDR",
      "videoDynamicRangeType": "HDR10"
    }
  },
  "isUpgrade": false,
  "downloadClient": "qBittorrent",
  "downloadClientType": "Torrent",
  "downloadId": "...",
  "deletedFiles": [],
  "customFormatInfo": {
    "customFormats": [{ "id": 1, "name": "HDR" }],
    "customFormatScore": 100
  },
  "release": { "releaseTitle": "...", "indexer": "...", "size": 1234567890, "indexerFlags": [] }
}
```

**Confirmed webhook field notes:**

- `eventType` is the string above; `isUpgrade` distinguishes upgrades.
- `movieFile.quality` here is a **string** (`movieFile.Quality.Quality.Name`), not
  the `QualityModel` used by the REST API. `qualityVersion` is the revision.
- `movieFile.mediaInfo` uses `WebhookMovieFileMediaInfo`: `audioChannels`,
  `audioCodec`, `audioLanguages[]`, `height`, `width`, `subtitles[]`,
  `videoCodec`, `videoDynamicRange`, `videoDynamicRangeType`.
- `deletedFiles[]` contains the replaced file(s) for upgrades.
- `customFormatInfo` holds `{ customFormats: [{ id, name }], customFormatScore }`.

**ArrTags-specific caveats:**

- A webhook is **outbound from Radarr to ArrTags**, so Jellyfin must expose a
  reachable HTTP endpoint (a plugin controller). Confirm Jellyfin 12 controller
  conventions and authentication/authorisation for that endpoint.
- **Assumption – validate** — Radarr does not appear to sign webhook payloads
  (no HMAC in the inspected source). Treat the endpoint as untrusted: use a
  secret path/header or configured shared token, validate payload shape, and
  never let it drive unbounded work.
- Do not make badge correctness depend on webhooks: use a periodic/scheduled
  reconciliation against `GET /api/v3/movie` as the source of truth, and treat
  webhooks as a low-latency accelerator that enqueues the same deduplicated
  per-item work.
- `Health`/`HealthRestored` are useful for plugin health reporting. `Grab` fires
  before a file exists; ignore it for badge rendering.

---

## 12. Key response schemas (field lists)

All objects deserialize camelCase and omit nulls on write. The following are the
fields ArrTags may encounter.

### `MovieResource`

`id` (int), `title` (string), `originalTitle` (string), `originalLanguage`
(`Language`), `alternateTitles` (`AlternativeTitleResource[]`), `secondaryYear`
(int?), `secondaryYearSourceId` (int), `sortTitle`, `sizeOnDisk` (int64?),
`status` (`MovieStatusType`), `overview`, `inCinemas` (date-time?),
`physicalRelease` (date-time?), `digitalRelease` (date-time?), `releaseDate`
(date-time?), `physicalReleaseNote`, `images` (`MediaCover[]`), `website`,
`remotePoster`, `year` (int), `youTubeTrailerId`, `studio`, `path`,
`qualityProfileId` (int), `hasFile` (bool?), `movieFileId` (int), `monitored`
(bool), `minimumAvailability` (`MovieStatusType`), `isAvailable` (bool),
`folderName`, `runtime` (int), `cleanTitle`, `imdbId` (string), `tmdbId` (int),
`titleSlug` (string; **mapped from the TMDb id as a string**), `rootFolderPath`,
`folder`, `certification`, `genres` (string[]), `keywords` (string[]),
`tags` (int[]), `added` (date-time), `addOptions` (`AddMovieOptions`), `ratings`
(`Ratings`), `movieFile` (`MovieFileResource`), `collection`
(`MovieCollectionResource`), `popularity` (float), `lastSearchTime` (date-time?),
`statistics` (`MovieStatisticsResource`).

`MovieStatusType` enum: `tba`, `announced`, `inCinemas`, `released`, `deleted`.

`MovieStatisticsResource`: `movieFileCount` (int), `sizeOnDisk` (int64),
`releaseGroups` (string[]).

`Ratings`: `imdb`, `tmdb`, `metacritic`, `rottenTomatoes`, `trakt`; each
`RatingChild { votes: int, value: double, type: RatingType }` where `RatingType`
∈ `user | critic`.

`MovieCollectionResource`: `title`, `tmdbId` (int).

### `MovieFileResource`

`id` (int), `movieId` (int), `relativePath`, `path`, `size` (int64),
`dateAdded` (date-time), `sceneName`, `releaseGroup`, `edition`, `languages`
(`Language[]`), `quality` (`QualityModel`), `customFormats`
(`CustomFormatResource[]`), `customFormatScore` (int?), `indexerFlags` (int?),
`mediaInfo` (`MediaInfoResource`), `originalFilePath` (string),
`qualityCutoffNotMet` (bool).

`CustomFormatResource`: `id` (int), `name` (string),
`includeCustomFormatWhenRenaming` (bool?), `specifications` (array, rarely
needed).

### `QualityModel` / `Quality` / `Revision`

`QualityModel { quality: Quality, revision: Revision }`.
`Quality { id: int, name: string, source: QualitySource, resolution: int,
modifier: Modifier }`.
`QualitySource` ∈ `unknown | cam | telesync | telecine | workprint | dvd | tv |
webdl | webrip | bluray`.
`Modifier` ∈ `none | regional | screener | rawhd | brdisk | remux`.
`Revision { version: int, real: int, isRepack: bool }`.

### `QualityProfileResource`

`id` (int), `name`, `upgradeAllowed` (bool), `cutoff` (int), `items`
(`QualityProfileQualityItemResource[]`), `minFormatScore` (int),
`cutoffFormatScore` (int), `minUpgradeFormatScore` (int), `formatItems`
(`ProfileFormatItemResource[]`), `language` (`Language`).

`QualityProfileQualityItemResource`: `id` (int), `name`, `quality` (`Quality`),
`items` (recursive array), `allowed` (bool).

`ProfileFormatItemResource`: `id` (int), `format` (int), `name`, `score` (int).

### `MediaInfoResource`

`id` (int), `audioBitrate` (int64), `audioChannels` (double), `audioCodec`,
`audioLanguages` (string), `audioStreamCount` (int), `videoBitDepth` (int),
`videoBitrate` (int64), `videoCodec`, `videoFps` (double),
`videoDynamicRange` (string), `videoDynamicRangeType` (string),
`resolution` (string), `runTime` (string), `scanType` (string),
`subtitles` (string).

### `SystemResource`

`appName`, `instanceName`, `version`, `buildTime` (date-time), `isDebug`,
`isProduction`, `isAdmin`, `isUserInteractive`, `startupPath`, `appData`,
`osName`, `osVersion`, `isNetCore`, `isLinux`, `isOsx`, `isWindows`, `isDocker`,
`mode` (`console | service | tray`), `branch`, `databaseType` (`sqLite |
postgreSQL`), `databaseVersion`, `authentication` (`none | basic | forms |
external`), `migrationVersion` (int), `urlBase`, `runtimeVersion`,
`runtimeName`, `startTime`, `packageVersion`, `packageAuthor`,
`packageUpdateMechanism`, `packageUpdateMechanismMessage`.

### `ApiInfoResource`

`current` (string), `deprecated` (string[]).

### `HealthResource`

`id` (int), `source` (string), `type` (`ok | notice | warning | error`),
`message` (string), `wikiUrl` (string).

### `HistoryResource`

`id` (int), `movieId` (int), `sourceTitle`, `languages` (`Language[]`),
`quality` (`QualityModel`), `customFormats` (`CustomFormatResource[]`),
`customFormatScore` (int), `qualityCutoffNotMet` (bool), `date` (date-time),
`downloadId`, `eventType` (`MovieHistoryEventType`), `data` (object of string?),
`movie` (`MovieResource`).

`MovieHistoryEventType` ∈ `unknown | grabbed | downloadFolderImported |
downloadFailed | movieFileDeleted | movieFolderImported | movieFileRenamed |
downloadIgnored`.

### `ParseResource`

`id` (int), `title`, `parsedMovieInfo` (`ParsedMovieInfo`), `movie`
(`MovieResource`), `languages` (`Language[]`), `customFormats`
(`CustomFormatResource[]`), `customFormatScore` (int).

`ParsedMovieInfo`: `movieTitles[]`, `originalTitle`, `releaseTitle`,
`simpleReleaseTitle`, `quality` (`QualityModel`), `languages`, `releaseGroup`,
`releaseHash`, `edition`, `year`, `imdbId`, `tmdbId`, `hardcodedSubs`,
`movieTitle`, `primaryMovieTitle`.

### `Language`

`{ id: int, name: string }`.

---

## 13. Version-specific considerations

- **API surface:** `/api/v3` is used by Radarr v3–v6; the OpenAPI `info.version`
  is `3.0.0`. Current stable at time of writing is the `6.4.x` line. Do not
  assume a new `/api/v4` on upgrade.
- **Field drift:** fields such as `originalFilePath`, `edition`,
  `customFormatScore`, `indexerFlags`, `videoDynamicRangeType`, and
  `manualInteractionRequired` were added over time. Deserialize tolerantly
  (missing field ⇒ default) and use `/api` `deprecated[]` +
  `SystemResource.version` to gate optional features.
- **Spec provenance:** the published `openapi.json` is generated from `develop`
  and can include unreleased fields; the live instance UI/docs are generated from
  its own build. Verify against the actual instance.
- **Endpoint fixes:** `/api/v3/health` (not `/system/health`);
  `/api` (not `/api/v3/api`); `/api/v3/moviefile/editor` is deprecated in favour
  of `/api/v3/moviefile/bulk`.
- **Webhook enum casing** has historically been PascalCase values with camelCase
  properties; the source comment notes a possible future change to camelCase.
  Deserialize event types case-insensitively.
- **Database:** never access Radarr's SQLite/PostgreSQL directly. Use only the
  HTTP API.

---

## 14. Client libraries, schemas, and implementation recommendation

- **Preferred schema:** the machine-readable OpenAPI spec is the source of truth
  for models:
  <https://raw.githubusercontent.com/Radarr/Radarr/develop/src/Radarr.Api.V3/openapi.json>
  (or the instance's own generated docs). It reflects the instance build better
  than any snapshot here.
- **No official .NET client exists.** Third-party generated clients
  (`devopsarr/radarr-py`, `devopsarr/radarr-rs`, `radarr-api-rs`) are useful as
  cross-checks only; do not introduce them as dependencies into a Jellyfin
  plugin.
- **Recommended for ArrTags:** a small hand-written DTO set with
  `System.Text.Json`, configured to be tolerant:
  - `PropertyNameCaseInsensitive = true`;
  - ignore unknown properties;
  - `JsonStringEnumConverter` (or string-backed enums) so unknown enum values do
    not throw;
  - `[JsonPropertyName]` for exact camelCase mapping;
  - treat absent fields as null.
- Keep models scoped to what ArrTags reads (movies, movie files, quality
  profiles, media info, system/health). Do not attempt to model every endpoint.
- Respect the Jellyfin architecture guidance for `IHttpClientFactory`
  ([`../architecture.md`](../architecture.md) §7): register a dedicated client
  for Radarr (distinct base URL, timeout, headers) rather than creating raw
  `HttpClient` instances per call.

---

## 15. Assumptions to validate before implementation

1. Jellyfin 12 movie items reliably expose the TMDb and IMDb provider IDs needed
   for matching, and the exact provider-ID key names.
2. Whether an API key is enforced for a given deployment (`authentication` ∈
   `none` vs basic/forms/external), and behaviour behind the target reverse
   proxy.
3. `/ping` availability/body across versions (used only as a liveness hint).
4. Whether `/movie/lookup*` is acceptable for matching, or whether the plugin
   should match strictly against the local `GET /api/v3/movie` list (preferred,
   avoids TMDb egress from Radarr).
5. Exact webhook `eventType` casing on the target version (deserialize
   case-insensitively and log unknown values).
6. Whether webhook payloads can be authenticated (they appear unsigned) and how
   the Jellyfin-side endpoint will be protected.
7. Presence/absence of `customFormats`/`customFormatScore` on embedded
   `movieFile` in the target version (confirmed absent in current source; the
   dedicated moviefile endpoint is required).
8. Reverse-proxy URL Base behaviour and TLS certificate policy for the target
   deployment.

---

## 16. References

- Radarr OpenAPI spec (develop):
  <https://raw.githubusercontent.com/Radarr/Radarr/develop/src/Radarr.Api.V3/openapi.json>
- Hosted API docs: <https://radarr.video/docs/api>
- API key auth:
  <https://github.com/Radarr/Radarr/blob/develop/src/Radarr.Http/Authentication/ApiKeyAuthenticationHandler.cs>
- Auth scheme registration:
  <https://github.com/Radarr/Radarr/blob/develop/src/Radarr.Http/Authentication/AuthenticationBuilderExtensions.cs>
- Error pipeline:
  <https://github.com/Radarr/Radarr/blob/develop/src/Radarr.Http/ErrorManagement/RadarrErrorPipeline.cs>
- Movie controller/resource:
  <https://github.com/Radarr/Radarr/blob/develop/src/Radarr.Api.V3/Movies/MovieController.cs>,
  <https://github.com/Radarr/Radarr/blob/develop/src/Radarr.Api.V3/Movies/MovieResource.cs>
- Movie file controller/resource:
  <https://github.com/Radarr/Radarr/blob/develop/src/Radarr.Api.V3/MovieFiles/MovieFileController.cs>,
  <https://github.com/Radarr/Radarr/blob/develop/src/Radarr.Api.V3/MovieFiles/MovieFileResource.cs>
- Serialization:
  <https://github.com/Radarr/Radarr/blob/develop/src/NzbDrone.Common/Serializer/System.Text.Json/STJson.cs>,
  <https://github.com/Radarr/Radarr/blob/develop/src/NzbDrone.Common/Serializer/Newtonsoft.Json/Json.cs>
- Webhooks:
  <https://github.com/Radarr/Radarr/tree/develop/src/NzbDrone.Core/Notifications/Webhook>
- Media info formatting:
  <https://github.com/Radarr/Radarr/blob/develop/src/NzbDrone.Core/MediaFiles/MediaInfo/MediaInfoFormatter.cs>
- Servarr wiki (Connect/Webhook and custom scripts reference): <https://wiki.servarr.com/radarr/settings>
