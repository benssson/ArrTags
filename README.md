## Project Status

**Current milestone:** 🚧 Phase 2 — Sonarr & Radarr Integration (in progress)

Completed in Phase 2 so far:

- 2.1 Shared provider-client boundary and connection identity.
- 2.2 Dedicated `IHttpClientFactory` client registration.
- 2.3 Versioned credential boundary and Radarr v3 reads.
- 2.4 Sonarr v3 reads (series, episodes, and episode files) with the validated
  episode-file join.

The plugin:

- Targets Jellyfin 12.0.0 (`net10.0`).
- Builds successfully with 0 warnings.
- Loads successfully on Jellyfin 12.0.0.
- Passes 113 automated tests.

Next task:

- 2.5 — Map provider data into canonical `ArrProvider`, `ArrConnection`, and
  `BadgeMetadata` without leaking provider DTOs.
