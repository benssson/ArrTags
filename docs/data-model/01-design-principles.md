# 1. Design Principles

### Provider-agnostic core

The matching, metadata, badge, render, and cache models use common concepts for
movies and television. Provider-specific identifiers and facts remain available
through typed provider references and extensions, but the renderer consumes
`BadgeMetadata`, not Sonarr or Radarr response objects.

### External models stop at the integration boundary

Sonarr and Radarr API resources, webhook payloads, and Jellyfin entities are
external models. They are parsed defensively and mapped to canonical models.
External field names, local database IDs, enum values, and optional response
shapes must not leak through the rest of the pipeline.

### Actual observations are separate from policy

Actual file quality comes from the current Arr file record. A quality profile is
requested policy and must not be represented as the actual quality badge.
`qualityCutoffNotMet` may be represented separately as an upgrade-pending
signal.

### Identity is explicit and scoped

Jellyfin IDs, provider IDs, Arr instance identity, and Arr local record IDs have
different scopes. A cache or match must retain enough scope to prevent a local
ID from being mistaken for an ID from another Arr connection.

### Immutable/value-style data

Canonical objects are conceptually immutable snapshots. A changed provider
response produces a new snapshot and fingerprint instead of mutating a snapshot
already being rendered. Small values such as quality, dimensions, fingerprints,
and colors should have value semantics.

### Missing is not false

Unavailable or unreported technical metadata is distinct from a confirmed
negative value. For example, absent media information means HDR is unknown, not
that the file is confirmed to be SDR.

### Deterministic rendering

All output-affecting inputs are represented in `RenderRequest` or its
fingerprints. The same source image, metadata, badge definition, renderer
version, and request parameters produce the same logical render result.

### Versioned evolution

Domain model, cache, and badge schema versions are explicit. Unknown future
metadata can be ignored by an older renderer, while a cache entry with an
incompatible schema can be discarded and rebuilt.
