## Project Status

**Current milestone:** Phase 3 — Media matching (tasks 3.1 through 3.7 complete; Milestone 3 acceptance and Gate 3 verification remain)

Completed in Phase 2 (Sonarr & Radarr integration):

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
- 2.7 Provider failure-matrix tests for authentication failures, unavailable
  services, malformed responses, optional fields, version drift, cancellation,
  and retries.

Completed in Phase 3 so far:

- 3.1 Canonical `MediaIdentity` snapshots for Movie, Series, Season, and Episode
  with deterministic library scope and V1 badge-surface eligibility.
- 3.2 Provider-neutral candidate selection, evidence recording, and the
  deterministic `MediaMatch` fingerprint.
- 3.3 Match status policy: zero candidates resolve to `NotFound`, multiple
  candidates resolve to `Ambiguous`, and only a single candidate is accepted,
  with no title/year guessing.
- 3.4 Documented movie, series, and episode matching order, with provider-neutral
  orchestration, the series-before-episode rule, bounded unsupported outcomes,
  and provider-specific candidate assembly.
- 3.5 Explicit V1 episode-numbering policy (ADR-007): number fallback is enabled
  only for regular single episodes after the series match, excluding season zero
  specials, multi-episode spans, and absolute/scene numbering.
- 3.6 DG-5 path decision (ADR-008): configured path mappings and path fallback
  are deferred out of V1; V1 never assumes Jellyfin and Arr path namespaces are
  equivalent.
- 3.7 Connection-scoped identity verification: Arr-local record and file IDs are
  always bounded by their originating `ArrConnection`, so identical numeric IDs
  on different Sonarr/Radarr connections never collide.

The plugin:

- Targets Jellyfin 12.0.0 (`net10.0`).
- Builds successfully with 0 warnings.
- Loads successfully on Jellyfin 12.0.0.
- Passes 327 automated tests.

Next tasks:

- All Phase 3 tasks are complete. Milestone 3 acceptance criteria and Gate 3
  verification remain before Milestone 4 (badge rendering).
