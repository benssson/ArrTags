# Canonical Data Model (index)

The data model is split by section; section and subsection numbers are
preserved in each file heading. This index is the section map.

**Scope:** Internal domain objects exchanged between Jellyfin, Sonarr, Radarr,
the cache, and the badge-rendering pipeline.

This document defines conceptual models, not C# classes, API DTOs, or a storage
schema. Provider responses are translated into these models at the integration
boundary. The rest of ArrTags should not need to know whether a value came from
Sonarr or Radarr.

The model is intentionally compatible with the V1 architecture: ArrTags is
read-only against external services, original source artwork remains recoverable
through plugin-owned provenance, and a derived image may be published as
Jellyfin's active artwork through the supported item-image APIs.


## Sections

| § | Section | File |
| --- | --- | --- |
| 1 | Design Principles | [`01-design-principles.md`](01-design-principles.md) |
| 2 | Model Relationships | [`02-model-relationships.md`](02-model-relationships.md) |
| 3 | Canonical Domain Models | split into [`3.1`-`3.12`](#sections) below |
| 3.1 | MediaIdentity | [`03-01-mediaidentity.md`](03-01-mediaidentity.md) |
| 3.2 | ArrProvider | [`03-02-arrprovider.md`](03-02-arrprovider.md) |
| 3.3 | ArrConnection | [`03-03-arrconnection.md`](03-03-arrconnection.md) |
| 3.4 | MediaMatch | [`03-04-mediamatch.md`](03-04-mediamatch.md) |
| 3.5 | BadgeMetadata | [`03-05-badgemetadata.md`](03-05-badgemetadata.md) |
| 3.6 | BadgeDefinition | [`03-06-badgedefinition.md`](03-06-badgedefinition.md) |
| 3.7 | RenderRequest | [`03-07-renderrequest.md`](03-07-renderrequest.md) |
| 3.8 | RenderResult | [`03-08-renderresult.md`](03-08-renderresult.md) |
| 3.9 | MetadataCacheEntry | [`03-09-metadatacacheentry.md`](03-09-metadatacacheentry.md) |
| 3.10 | ArtworkCacheEntry | [`03-10-artworkcacheentry.md`](03-10-artworkcacheentry.md) |
| 3.11 | UpdateEvent | [`03-11-updateevent.md`](03-11-updateevent.md) |
| 3.12 | Configuration | [`03-12-configuration.md`](03-12-configuration.md) |
| 4 | Provider Mapping | [`04-provider-mapping.md`](04-provider-mapping.md) |
| 5 | Badge Metadata Specification | [`05-badge-metadata-specification.md`](05-badge-metadata-specification.md) |
| 6 | Cache Model | [`06-cache-model.md`](06-cache-model.md) |
| 7 | Event Model | [`07-event-model.md`](07-event-model.md) |
| 8 | Error Model | [`08-error-model.md`](08-error-model.md) |
| 9 | Extension Strategy | [`09-extension-strategy.md`](09-extension-strategy.md) |
| 10 | Versioning | [`10-versioning.md`](10-versioning.md) |
