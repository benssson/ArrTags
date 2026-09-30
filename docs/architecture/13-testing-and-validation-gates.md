# 13. Testing and validation gates

Before implementation is considered complete, validate against the exact
Jellyfin 12.x ABI and supported Arr versions.

### Unit tests

- Configuration validation and secret redaction.
- Secret-boundary startup hydration, invalid replacement, rotation, version
  mismatch, disablement, restart hydration, lease disposal, and secret
  exclusion from diagnostics and serialization.
- Tolerant Sonarr/Radarr DTO deserialization.
- Provider-ID matching, ambiguity, missing-file, and rejection of path-only
  matches.
- Actual-versus-requested quality semantics.
- Fingerprint stability and invalidation.
- Queue coalescing, cancellation, retry, and state recovery.
- Badge layout, size limits, output format, and renderer failures.

### Integration tests

- Plugin discovery, DI registration, startup, shutdown, the scheduled
  reconciliation task (periodic and manual), the post-scan reconciliation hook,
  and the provider/render concurrency limits.
- Jellyfin item event delivery without blocking the event publisher.
- All supported image route variants, indexed images, requested sizes/formats,
  conditional requests, ranges, and non-200 pass-through responses.
- Publication through Jellyfin's supported item-image APIs, standard image
  routes, image tags, authorization, and failure behavior.
- Crash interruption before, during, and after source capture, rendering,
  `SaveImage`, item persistence, final state persistence, and cleanup; journal
  corruption; restart reconciliation; lifecycle fences; and item tombstones.
- Arr authentication, URL bases, timeouts, outages, upgrades, missing files,
  and webhook authentication.
- Jellyfin Enhanced coexistence: ArrTags output and eligibility are independent
  of Enhanced Quality Tags and Spoiler Guard state, and no Enhanced internals are
  referenced (ADR-011).
- Restart during reconciliation and rendering, corrupted state, and cache
  eviction.

### Acceptance checks

- Original source artwork is retained according to the provenance/restoration
  policy, and media files remain byte-for-byte untouched.
- Disabling ArrTags restores the original source only when the persisted active
  identity still matches; it never overwrites an externally changed image.
- Repeated ArrTags publications preserve the first retained source and do not
  treat an earlier ArrTags image as a new original.
- A clean restart can reload the ownership state and make the same guarded
  decision without relying on Jellyfin ownership metadata.
- A changed Arr file or badge configuration produces a new publication
  fingerprint.
- Repeated unchanged state does not repeatedly render or publish the image;
  Jellyfin handles normal response caching afterward.
- A failed external service or render never produces a broken Jellyfin image.
- Web, mobile, TV, Kodi, and other image-consuming clients receive the same
  server-rendered result where their image request is supported.
