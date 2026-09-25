# 5. Badge Metadata Specification

### V1 fields

V1 includes the following fields when the canonical metadata confirms them:

| Field | V1 behavior | Semantics |
| --- | --- | --- |
| Quality | Included | Actual Arr file quality label and structured descriptor. Never use the configured profile as actual quality. |
| Resolution | Included | Normalized resolution from quality/media info, with origin retained. |
| Dynamic range and Dolby Vision | Included when confirmed | Dolby Vision is one prioritized range label; unknown range is omitted. |
| Video codec | Included when confirmed | Normalized codec value. |
| Audio | Included when any component is confirmed | One composite value ordered as audio feature, codec, then channel count. Unknown features are not inferred. |
| Source | Included when confirmed | Provider quality source such as web, WEB-DL, Blu-ray, or remux. |
| Upgrade pending | Included only when explicitly true | `qualityCutoffNotMet`, rendered as the separate `UPGRADE` status state. |
| Custom badges | Limited V1 | Bounded custom values, in canonical order, subject to the 24-scalar display limit and available rail space. |

### Future fields

Future schema versions may add bit depth, frame rate, scan type, language,
subtitles, release group, edition, custom-format score, media certification,
stream count, and provider-specific extension values. These fields should be
added as optional normalized values or namespaced extensions rather than by
making existing fields provider-specific.

### Three-state technical values

Technical flags use `true`, `false`, or `unknown` where source absence is
meaningful. A provider may explicitly report that a feature is absent, but a
missing `mediaInfo` object must result in `unknown`. The optional audio feature
set follows the same rule: an absent (`null`) set is unknown, while an empty set
means the provider reported a codec with no known feature. Under ADR-009, the
renderer hides unknown, absent, and unreported values; it does not use a
placeholder or select a fallback value and must not display a negative
assertion based only on missing data.

### Quality profile separation

Quality profile name, cutoff, allowed qualities, and upgrade policy are not part
of the `quality` field. If a future badge exposes requested quality, it should be
a separately named policy field and should not share the actual-quality label.
