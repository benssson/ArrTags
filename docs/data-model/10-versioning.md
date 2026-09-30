# 10. Versioning

The version values have different responsibilities:

| Version | Responsibility | Cache consequence |
| --- | --- | --- |
| `modelVersion` / `cacheVersion` | Shape and meaning of persisted canonical/cache records | An incompatible value causes entries to be ignored or rebuilt. |
| `badgeSchemaVersion` | Meaning and availability of `BadgeMetadata` fields | Included in metadata fingerprints; changing semantics invalidates metadata/artwork. |
| `rendererVersion` | Layout, drawing, encoding, and output behavior | Included in render keys; changing it invalidates artwork entries but need not refetch metadata. |
| `configurationVersion` | Effective administrative configuration snapshot | Included in configuration fingerprints when output-affecting. |

Persisted plugin state is additionally wrapped in a storage envelope that carries
its own envelope schema version and a SHA-256 hash over the payload, separate
from the payload's `modelVersion`/`cacheVersion`. The envelope versions the
storage format while the versions above version the record meaning. An envelope
with an incompatible schema version or failed integrity is discarded for cache
state and quarantined for authoritative state, so it can never be read as valid
current state.

### Compatibility rules

- Additive optional metadata fields should be readable by older versions as
  unknown and ignored by renderers that do not select them.
- A field whose meaning changes requires a badge schema version change, even if
  its name and type remain the same.
- A renderer-only change requires a renderer version change and artwork cache
  invalidation, not necessarily a provider refresh.
- A cache format or ownership change requires a cache version change; do not
  infer compatibility from a successful parse alone.
- Every fingerprint includes the version that defines its semantics.
- Cache entries must not contain secrets, raw provider credentials, or unbounded
  external payloads.

The canonical model is therefore evolvable without making the renderer depend
on external API versions or making old cache entries appear current by accident.
