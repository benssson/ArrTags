# 7. Sonarr and Radarr integration

### HTTP clients

Use Jellyfin's `IHttpClientFactory` and a dedicated named or typed client per
Arr service. Requests use the configured base URL, `/api/v3`, `Accept:
application/json`, and `X-Api-Key`. The connection-scoped provider client
acquires the current version-matched API-key lease immediately before creating
the authenticated request; the key is never put in a URL or query string. The
client must support cancellation,
finite timeouts, bounded retries for transient transport failures, redacted
logging, and tolerant JSON deserialization.

The integration is read-only. It must not call write endpoints, access Arr
databases, or infer success from an HTML login page or redirect.

### Matching policy

The initial matching order is:

- **Movie to Radarr:** Jellyfin TMDb ID, then IMDb ID. Use Radarr's local movie
  library endpoints; do not use lookup endpoints for routine refreshes.
- **Series to Sonarr:** Jellyfin TVDB ID, then a cached catalogue comparison of
  other stable provider IDs when available.
- **Episode to Sonarr:** episode TVDB ID, then exact season and episode numbers
  after the series match, under the explicit V1 numbering policy (ADR-007).
  Number fallback applies only to regular, single episodes: season zero
  specials and multi-episode spans are excluded, and absolute/scene numbering is
  never an identity key. Specials, spans, and absolute-numbered anime match by
  the episode TVDB id only when number fallback is ineligible.
- **Path:** path matching is not an automatic V1 rule. Raw Jellyfin and Arr
  paths are location context only; V1 never normalizes or compares them and must
  not assume container and host paths are equivalent. Configured path fallback
  is deferred out of V1 by ADR-008.
- **Title and year:** candidate or manual-disambiguation data only; never an
  automatic badge match when zero or multiple candidates remain.

Missing IDs, ambiguous matches, missing files, virtual items, and remote items
produce no new badge rather than a guessed badge.

### Metadata semantics

Badge inputs come from the current Arr file resource:

- Actual quality comes from the file's quality model.
- Technical values such as codec, dynamic range, audio, language, release
  group, and custom-format score come from the file resource where available.
- Unreported technical values remain explicitly unknown rather than being
  presented as `false` or as an empty confirmed value; provider custom values
  are bounded before they enter canonical metadata.
- `qualityCutoffNotMet` is the authoritative upgrade-pending signal.
- Quality profiles describe requested policy and must not be presented as actual
  file quality.

Radarr's embedded movie file may omit custom-format fields; request the
  dedicated movie-file endpoint when those fields are enabled. Sonarr episode
  files must be joined using `episodeFileId == episodeFile.id`.
