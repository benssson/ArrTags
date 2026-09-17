# Sonarr API reference

Persistent integration reference for ArrTags.  This is deliberately a read-only
Sonarr integration: identify a Jellyfin TV item, obtain the current Sonarr file
record, and derive poster-badge state from that record.

## Scope, versions, and evidence

**Supported target:** Sonarr's `/api/v3` API, for Sonarr 3 and 4.  The upstream
application currently states that the v3 documentation applies to both Sonarr
v3 and v4.  Upstream also has a separate v5 API under development; do not
silently switch URL prefixes or models to `/api/v5`.

**Evidence labels used here:**

| Label | Meaning |
| --- | --- |
| **Confirmed** | Current official Sonarr API documentation or current upstream source directly establishes it. |
| **Source-inferred** | A conclusion from official source, not a public compatibility promise. |
| **Validate** | A reasonable implementation assumption that needs a live Sonarr v3/v4 integration test. |

The canonical API explorer is [Sonarr API Docs](https://sonarr.tv/docs/api/?api=v3).
The authoritative implementation is the [Sonarr source repository](https://github.com/Sonarr/Sonarr).
This document was researched against the current `develop` source on 2026-09-15.

## Connection and authentication

### Configuration contract

Store one independently configurable Sonarr connection containing:

| Setting | Requirement / handling |
| --- | --- |
| Base URL | Absolute `http` or `https` URL. It may include Sonarr's configured URL base, for example `https://media.example/sonarr`; do not append `/api/v3` to the persisted setting. Trim only a terminal `/`. |
| API key | Secret from Sonarr **Settings > General > Security > API Key**. Never log it, expose it in a status response, or put it in a cache/fingerprint. |
| Timeout | A finite per-request timeout plus Jellyfin cancellation token. **Validate** the final values; start conservatively (for example 10–30 seconds), since Sonarr may be local but a reverse proxy may not be. |
| TLS | Validate certificates by default. An insecure-certificate option, if ever offered, must be explicit and per connection. |

**Confirmed:** Sonarr uses the configured URL base as ASP.NET's path base before
API routing, and `/api/v3` is then routed below it. `GET /api/v3/system/status`
returns `urlBase`, which is useful for diagnostic display. A normal standalone
installation commonly listens on port 8989, but that is a deployment default,
not an ArrTags assumption.

Construct every request as:

```text
{baseUrl}/api/v3/{resource}[?query]
X-Api-Key: {apiKey}
Accept: application/json
```

**Confirmed:** both `X-Api-Key` HTTP-header authentication and `apikey` query
authentication are declared by Sonarr. Prefer the header: query keys leak into
URLs, access logs, browser history, proxies, and diagnostic messages. All
endpoints below require the API key unless a future per-instance probe proves
otherwise; Sonarr's API fallback authorization policy requires authentication.

Do not attempt browser/UI-cookie authentication. A reverse proxy may add its
own authentication in front of Sonarr; that is outside Sonarr's API contract.

### Connection probe and version gate

| Method / endpoint | Purpose and parameters | Key response / caveats |
| --- | --- | --- |
| `GET {base}/api/v3/system/status` | Test DNS/TLS/routing/API key and discover the application. No parameters. | `appName`, `version`, `branch`, `instanceName`, `urlBase`, `startTime`, OS/runtime fields. Require `appName == "Sonarr"` and record `version`; show, but do not expose, the configured URL/key. **Confirmed.** |

Example:

```http
GET /sonarr/api/v3/system/status HTTP/1.1
Host: media.example
X-Api-Key: <redacted>
Accept: application/json
```

```json
{
  "appName": "Sonarr",
  "instanceName": "Sonarr",
  "version": "4.x.y.z",
  "branch": "main",
  "urlBase": "/sonarr",
  "startTime": "2026-09-15T12:00:00Z"
}
```

**Validate:** whether a user-entered base URL that already includes `/api/v3`
should be rejected in the configuration UI (recommended) rather than guessed.
Avoid redirects that might drop `X-Api-Key`; normalize configuration first.

## Matching Jellyfin items to Sonarr

### Series identity

Use immutable external IDs first; display title is not identity.

1. **TVDB ID — preferred and confirmed query path.** Read Jellyfin's series
   provider ID for TVDB, parse it as an integer, then call
   `GET /api/v3/series?tvdbId={id}`. Sonarr's `SeriesResource` exposes
   `id` (Sonarr-local ID), `tvdbId`, `tmdbId`, `tvMazeId`, `imdbId`, title,
   year, `path`, and `qualityProfileId`.
2. **Other provider IDs — source-confirmed fields, no equivalent documented
   v3 filter.** A returned Sonarr series exposes TMDB, TVMaze, IMDb, MAL and
   AniList IDs. If Jellyfin lacks TVDB, fetch/cache the Sonarr series catalogue
   and compare normalized provider IDs exactly. Do not use a search endpoint
   for an already-added local series.
3. **File path — fallback.** Compare a normalized Jellyfin media-source path
   with `episodeFile.path` (best) or series `path` (weaker). This only works
   when both applications see the same filesystem naming/mapping; do not
   assume Docker/container paths match host paths.
4. **Title + year — last resort/manual disambiguation only.** Match exact
   normalized title/alternate title plus year, and reject zero or multiple
   candidates. Never auto-badge a merely plausible title match.

Persist the verified Sonarr-local `seriesId` together with the external ID and
the connection identity as a cache, not as permanent truth. Series may be
deleted/re-added and local IDs then change. Revalidate on a 404, matching
metadata change, or reconciliation run.

### Episode identity

After the series match, obtain its episodes (below) and resolve in this order:

1. Exact Jellyfin episode TVDB provider ID == Sonarr `episode.tvdbId`.
2. Exact `seasonNumber` + `episodeNumber` from Jellyfin's season/index number.
3. For anime/special numbering only, an explicitly designed match using
   Sonarr `absoluteEpisodeNumber` or scene-number fields. **Validate** this
   policy with real libraries; scene numbering is not a general identity key.
4. Exact normalized actual file path == `episodeFile.path`, if shared paths
   are known. This is especially useful for multi-episode files.

Sonarr's episode resource has local `id`, `seriesId`, `tvdbId`,
`seasonNumber`, `episodeNumber`, optional absolute/scene numbers,
`episodeFileId`, `hasFile`, and optional embedded `episodeFile`. Its local
episode ID is not a TVDB ID.

**Important:** special episodes use season `0`; files can represent more than
one episode; and a series-level poster cannot honestly claim one file quality
unless ArrTags defines an aggregation policy (for example, per-season summary
or “mixed”). A title/year fallback needs explicit user confirmation.

## Read endpoints used by ArrTags

All paths in this table are relative to the configured base URL and require
`X-Api-Key`. Responses are JSON in camelCase. Only the read operations are in
scope; do not use the writable resource endpoints to “correct” Sonarr.

| Method / endpoint | Required / useful parameters | Purpose and important response fields | Caveats |
| --- | --- | --- | --- |
| `GET /api/v3/system/status` | none | Connection/version probe; see above. | Required during connection test and useful after unexpected API changes. |
| `GET /api/v3/series` | `tvdbId` optional; `includeSeasonImages` optional | Returns an array. Series fields include `id`, `title`, `alternateTitles`, `year`, `path`, `tvdbId`, `tmdbId`, `tvMazeId`, `imdbId`, `qualityProfileId`, `profileName`, `monitored`, `status`, `network`, `genres`, `certification`, `ratings`, `statistics`, `tags`, `seasons`. | With `tvdbId`, source returns a zero-or-one element array. Do not assume title uniqueness. |
| `GET /api/v3/series/{id}` | path `id`; `includeSeasonImages` optional | Revalidate/cache one known series. Same identity, profile and descriptive fields. | `404` when local Sonarr ID no longer exists. |
| `GET /api/v3/episode` | **one of:** `seriesId`, repeated `episodeIds`, or `episodeFileId`. Optional `seasonNumber` only with `seriesId`; booleans `includeSeries`, `includeEpisodeFile`, `includeImages`. | Returns an array of `EpisodeResource`. Essential: `id`, `seriesId`, `tvdbId`, `seasonNumber`, `episodeNumber`, `episodeFileId`, `hasFile`, absolute/scene numbering. With `includeEpisodeFile=true`, embeds the current file resource. | **Confirmed from controller source:** missing all selectors is `400`; `seriesId` wins if more than one selector is sent. Fetch a series once, map locally, and cache/deduplicate. |
| `GET /api/v3/episodeFile` | **one of:** `seriesId` or repeated `episodeFileIds`. | Returns `EpisodeFileResource` records. Essential: `id`, `seriesId`, `relativePath`, `path`, `size`, `dateAdded`, `quality`, `qualityCutoffNotMet`, `languages`, `releaseGroup`, `sceneName`, `releaseType`, `customFormats`, `customFormatScore`, `mediaInfo`. | **Confirmed from controller source:** no selector is `400`; `seriesId` wins. A series query is an inventory, not proof that each file is current for an episode. |
| `GET /api/v3/episodeFile/{id}` | path `id` | Fetch/revalidate a particular file, including actual quality. | **Source-inferred** through Sonarr's standard REST-by-ID base controller; validate against supported v3/v4 instances before relying on it for the first implementation. The episode endpoint with `episodeFileId` is a safe reverse lookup. |
| `GET /api/v3/qualityprofile/{id}` | path `id` | Obtain requested/allowed quality policy for explanatory UI only: profile `id`, `name`, cutoff and ordered allowed quality items/custom-format policy. | It is **not** the file's actual quality and is not necessary to render a file-quality badge. Treat exact nested schema as version-sensitive and use the live API docs when/if this feature is exposed. |
| `GET /api/v3/qualityprofile` | none | List profiles to resolve `series.qualityProfileId`. | Same policy-only caveat. Cache by connection/version; profile changes can make a file upgradeable without changing the file. |

### Minimal normal request flow

```text
Jellyfin Series TVDB ID
  -> GET /series?tvdbId=...
  -> Sonarr series.id
  -> GET /episode?seriesId=...&includeEpisodeFile=true
  -> match one episode; its episodeFileId / episodeFile.id
  -> use episodeFile.quality for the badge
```

For a cache warm/reconciliation path, `GET /episodeFile?seriesId=...` is an
efficient complementary file inventory. Join it to episode data by
`episode.episodeFileId == episodeFile.id`, not by series ID alone.

Representative fields (not a full schema):

```json
{
  "id": 73,
  "seriesId": 12,
  "tvdbId": 1234567,
  "seasonNumber": 2,
  "episodeNumber": 4,
  "episodeFileId": 418,
  "hasFile": true,
  "episodeFile": {
    "id": 418,
    "path": "/tv/Example/Season 02/Example - S02E04.mkv",
    "size": 4294967296,
    "quality": {
      "quality": { "id": 3, "name": "WEBDL-1080p", "source": "web", "resolution": 1080 },
      "revision": { "version": 1, "real": 0, "isRepack": false }
    },
    "qualityCutoffNotMet": false,
    "releaseGroup": "ExampleGroup",
    "languages": [],
    "mediaInfo": { "videoCodec": "h264", "audioCodec": "ac3", "width": 1920, "height": 1080 }
  }
}
```

Field presence/nullability in the example is illustrative; deserialize
defensively. In particular, MediaInfo can be absent/incomplete.

## Actual file quality versus quality profile

### The rule

**Use `EpisodeFileResource.quality`, never `SeriesResource.qualityProfileId`
or `profileName`, to label the quality of the media file currently associated
with a Jellyfin episode.** This is confirmed by Sonarr's resource mappers:
the episode file owns `QualityModel`, while the series owns only the chosen
quality profile ID/name.

`quality` is a `QualityModel`:

| Field | Meaning for badge use |
| --- | --- |
| `quality.id` | Sonarr quality identifier; useful for internal equality, not user-facing text. |
| `quality.name` | Best primary label, such as `WEBDL-1080p` or `Bluray-2160p Remux`. Do not reconstruct it from a fixed enum; Sonarr can evolve. |
| `quality.source` | Sonarr's parsed source classification (`web`, `webRip`, `bluray`, `blurayRaw`, `television`, etc.). |
| `quality.resolution` | Parsed quality resolution in pixels (for example `1080`/`2160`), distinct from MediaInfo dimensions. |
| `revision.version`, `revision.real`, `revision.isRepack` | Release revision/proper/repack characteristics. Include only if badge design needs it. |
| `qualityCutoffNotMet` | Whether Sonarr considers this file below the series profile cutoff. It says “still upgradeable by cutoff,” not “bad file.” |

**Source-inferred:** Sonarr's quality parser aggregates source, resolution and
revision from release naming, MediaInfo and/or filename extension; detection
origin is intentionally JSON-ignored in `QualityModel`. Thus `quality.name`
is Sonarr's recorded release-quality classification, whereas `mediaInfo.width`
and `height` describe inspected stream data. Do not overwrite the former with
the latter; display both only under a deliberate “reported vs detected” policy.

### Quality profile is policy, not observation

The series `qualityProfileId` selects a configurable desired/allowed quality
set, ordering/cutoff, and (in modern Sonarr) custom-format scoring rules. It
can be changed without replacing any files. Conversely, a file can be upgraded
while the profile ID stays unchanged. `profileName` is therefore unsuitable for
a “currently 1080p WEB-DL” badge.

Useful secondary quality-related file fields:

| Field | Suitable interpretation |
| --- | --- |
| `customFormats`, `customFormatScore` | Sonarr's custom-format evaluation for the stored file; optional advanced badge/diagnostic, not a universal quality measure. |
| `qualityCutoffNotMet` | A possible “upgrade pending” indicator, if that UX is wanted. It is a live policy evaluation, so include it in the badge fingerprint. |
| `releaseType`, `releaseGroup`, `sceneName` | Release provenance; optional, user-configurable and potentially absent. |
| `languages` | File language objects; potentially valuable for a language badge. |
| `mediaInfo` | Stream attributes such as codecs, dimensions, HDR/dynamic-range/audio data where Sonarr extracted it; absent data must not be treated as “no HDR/Atmos.” |

## Current file, multiple episodes, upgrades, and state changes

For a matched Sonarr episode, `episodeFileId` is the authoritative current-file
association; `hasFile` is only a convenience flag. The exact join is:

```text
episode.hasFile == true && episode.episodeFileId > 0
    => current file = episodeFile where episodeFile.id == episode.episodeFileId
```

Use the embedded `episodeFile` only when it is included and its `id` equals
`episodeFileId`; otherwise look it up in the series file inventory. If there is
no current association, render no actual-file-quality badge (or an explicitly
configured “missing” state), never the profile's desired quality.

**Confirmed/source-inferred implications from the resources and controllers:**

- A file resource is owned by a series, and several episode resources may
  reference the same `episodeFileId` (multi-episode files).
- `GET /episodeFile?seriesId=...` can retain old/unreferenced records only as
  long as Sonarr has them; do not choose “newest file in series” as an episode's
  quality. Join through `episodeFileId` every time.
- On import/upgrade/replacement, persist a fingerprint that includes at least
  connection ID, series ID, episode ID, `episodeFileId`, file `quality`,
  `customFormatScore`, `qualityCutoffNotMet`, `languages`, relevant MediaInfo,
  and renderer version. A changed file ID is a definite refresh trigger.
- A file can be deleted or reassigned during the read/render window. Re-read
  the episode/file immediately before image publication; treat a `404` or
  mismatch as a benign stale-work cancellation and enqueue reconciliation.
- Do not use `dateAdded` or `size` as the sole upgrade detector. They are useful
  inputs to a fingerprint/diagnostic only.

**Validate:** the desired badge scope. For an Episode poster use the matching
episode file. For Season/Series posters choose and document one of: no
file-quality badge, “mixed”, lowest/highest available quality, or a calculated
summary. It is an ArrTags product rule, not a Sonarr-provided aggregate.

## Errors, resilience, and request discipline

### Responses and connection failures

**Confirmed from Sonarr's error pipeline:** invalid/failed API requests produce
normal HTTP status codes and JSON errors; validation failures can be a JSON
array, while other API exceptions use an error object with message/description.
Models and error payloads are not a stable user-facing localization contract.

| Result | ArrTags action |
| --- | --- |
| DNS failure, refused connection, TLS failure, timeout | Mark connection unavailable; do not block Jellyfin events; retry with bounded exponential backoff and jitter. Preserve previous badge unless product policy says otherwise. |
| `401` / `403` | Configuration/authentication failure: stop automatic rapid retries and surface a redacted action message. Check reverse-proxy auth separately. |
| `404` | A cached Sonarr series/file was removed or base URL/path is wrong. Re-resolve external ID once; otherwise mark unmatched/stale without error storms. |
| `400` / `422` | Caller/query/model incompatibility. Do not blindly retry; log endpoint/status and a redacted/truncated response. The episode/file selector rules above are common causes. |
| `409` | Conflict is possible for Sonarr writes, which ArrTags does not perform; unexpected on a GET should be logged and retried only as transient after inspection. |
| `5xx`, gateway `502`/`503`/`504` | Treat as transient; retry idempotent GETs with bounded backoff/jitter. Do not retry indefinitely or concurrently stampede the service. |
| `429` or `Retry-After` | Sonarr source does not expose an application-wide documented API quota. Nevertheless, a reverse proxy/gateway can rate-limit. Honor `Retry-After` if present and reduce per-connection concurrency. |

Use a dedicated `HttpClient` configured through Jellyfin DI, cancellation tokens,
small bounded concurrency, response-size limits where practical, and
per-connection single-flight/cache coalescing. Do not poll every item during a
Jellyfin scan. Cache series/episode inventories briefly and reconcile in a
bounded background task. API keys must be redacted from all URLs, headers,
exceptions and telemetry.

There is no confirmed Sonarr server-side ETag/conditional-GET contract to rely
on for these resources. **Validate** any observed cache headers before making
them part of state correctness.

## Keeping badges current: webhook and SignalR options

### Recommended: Sonarr outbound Webhook connection

**Confirmed:** Sonarr's built-in Webhook notification connection sends outbound
events for grab, download, import complete, rename, episode-file delete,
series add/delete, health issue/restored, application update and manual
interaction required. Configure it manually in Sonarr's Connections settings;
ArrTags does not need to create/modify Sonarr notification configuration.

Use at least download/import, rename, episode-file-delete and series-delete
events as *hints* to enqueue a targeted refresh. A webhook is not a transaction
or source of truth: acknowledge quickly, validate/authorize the incoming
request at the plugin boundary, coalesce duplicate/out-of-order events, then
use the read endpoints above to get current state. Test an upgrade and deletion
against the installed Sonarr version before relying on exact payload shape.

**Validate before implementation:** webhook authentication/secret strategy,
whether Jellyfin can safely expose the receiving route to Sonarr, source-IP
allowlisting/reverse-proxy behavior, the exact payload/schema version, retry
semantics, timeout expectations, and how user configuration discovers the URL.
Do not assume Sonarr signs webhook payloads unless verified on the exact
version; protect the endpoint independently.

### Optional / not first implementation: SignalR

**Source-inferred:** Sonarr hosts an authenticated SignalR hub at
`/signalr/messages`; its resource controllers broadcast changes for episode
files and series. This could reduce reconciliation latency, but hub protocol,
method names, message schemas and reconnect guarantees are not documented as a
stable external integration API. Prefer webhooks plus periodic reconciliation;
only adopt SignalR after a version-pinned prototype and failure-mode tests.

## Models and schemas

No official, supported Sonarr .NET client library was identified. Sonarr's own
C# resources are application implementation types, not a client package for
plugins to reference. The public API explorer is generated from the upstream
OpenAPI configuration, but production source only enables the generated
`docs/{documentName}/openapi.json` endpoint in debug builds; do not depend on a
live production instance exposing it.

Preferred order:

1. Use the official API explorer and version-pinned official source as the
   contract.
2. Keep narrow, tolerant DTOs for the read fields ArrTags actually uses (or
   generate them from a checked-in, reviewed official OpenAPI snapshot).
3. Treat unknown JSON fields as forward-compatible; treat fields needed for a
   badge as nullable/optional and degrade gracefully.
4. Do not add a third-party client library merely for these five GET calls.
   If a generator/client is considered later, verify its generated schema
   against the target Sonarr major/minor release and retain the schema source.

## Pre-implementation validation checklist

- Test a Sonarr v4 main-branch instance with a URL base and `X-Api-Key`.
- Verify the precise Jellyfin provider-ID keys/types and obtain real TVDB,
  TMDB/IMDb-only, special, anime and multi-episode examples.
- Capture sanitized responses for the six read endpoints above, including an
  episode without a file and one upgraded/replaced file.
- Confirm a `qualityProfile` response separately and keep it out of the
  actual-file badge path.
- Test Sonarr's outbound webhook payloads for import, upgrade, rename and
  delete, including delivery failure/retry behavior.
- Test container path mismatch and ensure no path fallback creates a false
  match.
- Pin the supported Sonarr version range in the plugin README/config UI and
  add contract tests using retained sanitized fixtures.

## Primary sources

- [Sonarr API Docs (v3)](https://sonarr.tv/docs/api/?api=v3)
- [Startup/API security, version docs, URL-base middleware and SignalR hub](https://github.com/Sonarr/Sonarr/blob/develop/src/NzbDrone.Host/Startup.cs)
- [v3 series controller/resource](https://github.com/Sonarr/Sonarr/tree/develop/src/Sonarr.Api.V3/Series)
- [v3 episode controller/resource](https://github.com/Sonarr/Sonarr/tree/develop/src/Sonarr.Api.V3/Episodes)
- [v3 episode-file controller/resource](https://github.com/Sonarr/Sonarr/tree/develop/src/Sonarr.Api.V3/EpisodeFiles)
- [v3 system-status controller](https://github.com/Sonarr/Sonarr/blob/develop/src/Sonarr.Api.V3/System/SystemController.cs)
- [Quality and QualityModel implementation](https://github.com/Sonarr/Sonarr/tree/develop/src/NzbDrone.Core/Qualities)
- [Webhook notification implementation](https://github.com/Sonarr/Sonarr/blob/develop/src/NzbDrone.Core/Notifications/Webhook/Webhook.cs)
- [API error pipeline](https://github.com/Sonarr/Sonarr/blob/develop/src/Sonarr.Http/ErrorManagement/SonarrErrorPipeline.cs)
