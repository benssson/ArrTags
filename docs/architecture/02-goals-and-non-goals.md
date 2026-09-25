# 2. Goals and non-goals

### Goals

- Support Jellyfin 12 only for the initial implementation.
- Support independently configured Sonarr and Radarr connections.
- Match Jellyfin movies to Radarr and TV content to Sonarr.
- Display actual file metadata, especially actual quality, rather than treating
  a configured quality profile as the file's quality.
- Update badges after Jellyfin changes, Arr changes, webhooks, and scheduled
  reconciliation.
- Preserve the original source artwork through plugin-owned provenance and
  restoration state; avoid modifying media files or Jellyfin's image cache.
- Remain safe alongside Jellyfin Enhanced.
- Fail without degrading Jellyfin library operations or image serving.

### Non-goals

- Writing to Sonarr or Radarr.
- Modifying media files, media-folder posters, or Jellyfin's image cache.
- Replacing Jellyfin's metadata refresh or image-provider pipeline.
- Depending on Jellyfin Enhanced's DOM, JavaScript, configuration, or private
  endpoints.
- Supporting arbitrary Arr applications or Jellyfin versions before 12.
