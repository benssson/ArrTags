# 3.6 BadgeDefinition

**Purpose:** Visual and selection rules for one badge, independent of the
metadata source. Definitions are configuration snapshots and do not contain
the current item's metadata.

| Field | Type | Required | Field Ownership | Notes |
| --- | --- | --- | --- | --- |
| `definitionId` | Opaque identifier | Yes | Configuration | Stable identity within configuration. |
| `definitionVersion` | Version identifier | Yes | Configuration | Changes when selection or visual behavior changes. |
| `enabled` | Boolean | Yes | Configuration | Disabled definitions produce no badge. |
| `metadataSelector` | Provider-neutral selector | Yes | Configuration | Selects one ADR-009 V1 field: quality, resolution, dynamic range, audio, source, video codec, custom value, or upgrade-pending. |
| `fallbackPolicy` | Hide in V1 | Yes | Configuration | Unknown and unavailable values are omitted; no placeholder or alternate selector is used in V1. |
| `textTemplate` | Bounded display template | Optional | Configuration | Formatting rule after values are normalized; no provider DTO paths. |
| `allowedValues` | Bounded string list | Optional | Configuration | Per-selector value allowlist (ADR-017); empty means no restriction. At most 32 entries, each at most 64 characters; entries are trimmed, blank entries and control characters are rejected, and case-insensitive duplicates are rejected. Matching is a case-insensitive ordinal exact match against the resolved pre-template value; no substring, wildcard, prefix, or regular-expression matching. |
| `style` | Badge style value | Yes | Configuration | Opaque palette, text, font, and contrast policy; V1 geometry is defined by ADR-009. |
| `placement` | Placement value | Yes | Configuration | V1 poster anchor, bounded rail packing, margins, and scale. The global position and size (ADR-019) are configurable; packing and the safe area remain bounded and code-owned. |
| `visibilityPolicy` | Image/item/client surface policy | Yes | Configuration | V1 is poster-oriented and not user-specific. |
| `customValueRules` | Optional bounded rules | Optional | Configuration | Maps approved custom metadata to a visual value. |

For V1, `metadataSelector` uses the selector vocabulary in section 3.5. A
definition may disable a selector, provide one bounded provider-neutral
`{value}` template, and carry a bounded per-selector value allowlist. Definition
order cannot override the ADR-009 priority; configuration controls visibility
and bounded presentation, not semantic precedence. An empty `allowedValues` list
means no restriction; a non-empty list restricts rendering to the confirmed
values it matches and never widens an unknown/absent omission (the filter itself
is applied by the renderer per ADR-017 clause 3). V1 definitions target the
unindexed `Primary` poster surface of Movie and Episode items only.

The V1 style and placement values are bounded domain values rather than
arbitrary markup:

- Technical pills use `#111827` with `#FFFFFF` text; the upgrade status pill
  uses `#B45309` with `#FFFFFF` text.
- Text is a single bold or semibold sans-serif line at a 28 pixel reference
  size. Geometry uses the 1000 pixel reference values and
  `clamp(width / 1000, 0.5, 4.0)` scale in ADR-009.
- Technical pills use a configurable global anchor (ADR-019: four corners plus
  center, default bottom-left) with at most two rows and three pills per row;
  rows stack away from the anchored edge and align to the anchored side. Upgrade
  status is an independent pill that is top-right except when the anchor is
  top-right, then top-left. The global preset size (Small/Medium/Large, default
  Medium) multiplies the reference geometry.
- The final label is limited to 24 Unicode scalar values after whitespace and
  control-character normalization; end truncation uses `...`.
- A configured color must pass the 4.5:1 text/background contrast check. Badge
  backing is opaque, and source alpha is preserved only in the output image
  outside the badge pixels.
