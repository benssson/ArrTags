# 9. Extension Strategy

### New metadata providers

A new source such as Bazarr, Tdarr, Plex, or a custom provider should implement
the integration boundary that maps its external identity and observations into
`ArrProvider`-like provider identity, `MediaMatch`, and `BadgeMetadata`. The
matching and rendering pipeline should consume the same canonical models.

Provider-specific facts belong in namespaced `extensions` or in a future
optional canonical field only after the fact has stable cross-provider meaning.
The renderer should not branch on provider kind for ordinary fields.

### New badge types

New badges should be added as metadata selectors and `BadgeDefinition` values.
They should not require a change to `RenderRequest` or `RenderResult`. A new
visual primitive may extend the style/placement vocabulary, while preserving
the same bounded rendering and fingerprint rules.

### New item or image surfaces

Support for season/series aggregation, thumbnails, or other surfaces requires
an explicit aggregation policy and eligibility rule. It must not silently reuse
an episode or movie file's quality as if it represented the whole aggregate.
