# Deliberate scope exclusions

## Deliberate V1 scope exclusions

These are recorded in `GOALS.md` (Initially out of scope), ADR-006, ADR-008, and
ADR-009, and remain excluded unless `GOALS.md` is deliberately changed: Jellyfin
versions before 12; modifying original media files; writing or managing
Sonarr/Radarr metadata; a general poster-management system; user-specific
badges; non-poster artwork; and unapproved external services. Configured
Jellyfin-to-Arr path fallback is deferred out of V1 by ADR-008; V1 badge
surfaces are unindexed Movie and Episode `Primary` posters only (ADR-006 /
ADR-009); Series/Season aggregation remains post-V1.
