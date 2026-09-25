# 3.7 RenderRequest

**Purpose:** Complete logical input for generating one derived poster image for
publication through Jellyfin's item-image APIs. It represents the selected
source artwork plus all output-affecting metadata and configuration.

| Field | Type | Required | Field Ownership | Notes |
| --- | --- | --- | --- | --- |
| `requestId` | Opaque correlation identifier | Yes | Generated | Diagnostic only; not part of the output identity. |
| `mediaIdentity` | MediaIdentity reference | Yes | Jellyfin/plugin | Target item. |
| `imageSurface` | Image type and optional index | Yes | Jellyfin/request | V1 supports only the unindexed `Primary` poster surface for Movie and Episode items. |
| `sourceImage` | Original source image handle/bytes | Yes at render time | Jellyfin/plugin | Source used to create derived artwork; the source is not overwritten by the renderer. |
| `sourceImageFingerprint` | Source image identity fingerprint | Yes | Jellyfin/generated | Changes when the retained or newly validated source artwork changes. |
| `renderParameters` | Format, quality, dimensions, and relevant output values | Yes | Configuration/generated | V1 is lossless 8-bit PNG at source dimensions; client-requested size and device pixel ratio are not render inputs. |
| `match` | MediaMatch | Yes | Generated/cache | Only a valid matched state supplies metadata. |
| `metadata` | BadgeMetadata | Optional | Sonarr/Radarr/cache | Absent metadata means no new publication or configured no-badge behavior. |
| `badgeDefinitions` | Ordered definitions | Yes | Configuration | Snapshot used for this render. |
| `configurationFingerprint` | Opaque fingerprint | Yes | Generated from configuration | Includes output-affecting settings, not secrets. |
| `rendererVersion` | Version identifier | Yes | Plugin | Changes when rendering behavior changes. |
| `outputPolicy` | Format, size, and limit policy | Yes | Configuration/generated | Includes PNG/RGB-or-RGBA output, source-dimension preservation, 24-scalar text, two-row/three-pill layout, operational bounds, and cancellation constraints. |
