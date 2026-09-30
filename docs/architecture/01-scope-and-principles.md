# 1. Scope and principles

ArrTags is a Jellyfin 12 plugin that reads Sonarr and Radarr metadata and
publishes derived badge artwork through Jellyfin's item-image APIs. The plugin
is read-only with respect to Sonarr and Radarr; its only Jellyfin artwork
mutation is the guarded publication of its own derived image.

The architecture follows these principles:

- Preserve the original poster source through plugin-owned provenance and
  restoration state; a validated derived poster may become Jellyfin's active
  artwork through the supported image APIs.
- Deliver badges through Jellyfin's normal image endpoints so all clients can
  receive the same result.
- Keep external I/O, image processing, artwork publication, and cache
  maintenance outside library event handlers and image request paths.
- Prefer stable provider IDs and documented Jellyfin APIs over names, paths,
  database access, or third-party plugin internals.
- Treat missing, stale, ambiguous, or unavailable external metadata as a
  recoverable condition and leave the current usable artwork unchanged when
  necessary.
- Make every derived result fingerprinted, bounded, cancellable, and safe to
  regenerate after a restart.
