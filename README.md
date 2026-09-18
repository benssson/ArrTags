## Project Status

**Current milestone:** 🚧 Phase 2 — Sonarr & Radarr Integration (in progress)

Completed in Phase 2 so far:

- 2.1 Shared provider-client boundary and connection identity.
- 2.2 Dedicated `IHttpClientFactory` client registration.
- 2.3 Versioned credential boundary and Radarr v3 reads.
- 2.4 Sonarr v3 reads (series, episodes, and episode files) with the validated
  episode-file join.
- 2.5 Canonical `ArrProvider`/`ArrConnection`/`BadgeMetadata` mapping with
  connection-scoped Sonarr and Radarr record/file identity, actual-file quality
  semantics, and no provider DTO leakage.
- 2.6 Explicit unknown audio-feature state and bounded, sanitized custom badge
  values at the canonical metadata boundary.

The plugin:

- Targets Jellyfin 12.0.0 (`net10.0`).
- Builds successfully with 0 warnings.
- Loads successfully on Jellyfin 12.0.0.
- Passes 142 automated tests.

Next task:

- 2.7 — Provider failure-matrix tests for authentication failures, unavailable
  services, malformed responses, optional fields, version drift, cancellation,
  and retries.
