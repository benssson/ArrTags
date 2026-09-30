# 3.5 BadgeMetadata

**Purpose:** Provider-agnostic, badge-relevant observations for the current
matched media file. It is the renderer's metadata input. Each optional technical
value supports an explicit unknown state.

| Field | Type | Required | Field Ownership | Notes |
| --- | --- | --- | --- | --- |
| `badgeSchemaVersion` | Version identifier | Yes | Plugin | Version of the normalized metadata shape. |
| `provider` | ArrProvider reference | Yes | Plugin/provider | Identifies the source without exposing its DTO. |
| `recordIdentity` | Typed Arr record/file identity reference | Yes | Sonarr/Radarr + generated | Carries the connection-scoped `SonarrIdentity` or `RadarrIdentity`, including explicit file-identity presence; see section 3.4.1. |
| `observedAt` | Timestamp | Yes | Provider/generated | When the source observation was obtained. |
| `quality` | Quality descriptor | Optional | Sonarr/Radarr | Actual file quality label and structured source/resolution/modifier; never the quality profile target. |
| `resolution` | Resolution descriptor | Optional | Sonarr/Radarr or Derived | Prefer inspected media dimensions; retain whether the value is reported or derived. |
| `dynamicRange` | Dynamic-range descriptor | Optional | Sonarr/Radarr | HDR, HDR10, HDR10+, HLG, PQ, or unknown. |
| `dolbyVision` | Tri-state flag or descriptor | Optional | Sonarr/Radarr | Dolby Vision is distinct from generic HDR; unknown is not false. |
| `videoCodec` | String | Optional | Sonarr/Radarr | Normalized codec where the provider reports it. |
| `audioCodec` | String | Optional | Sonarr/Radarr | Normalized codec, such as TrueHD, DTS-HD MA, EAC3, AAC, or unknown. |
| `audioChannels` | Decimal number | Optional | Sonarr/Radarr | Channel count; do not infer Atmos solely from channel count. |
| `audioFeatures` | Set of `Atmos`, `DTS`, `DTS-HD`, `DTS-X`, or future feature names | Optional | Sonarr/Radarr or Derived | Feature detection must retain unknown when source data is incomplete; an absent set is unknown and an empty set is a reported codec with no known feature. |
| `source` | Source descriptor | Optional | Sonarr/Radarr | Release source such as web, WEB-DL, WEBRip, HDTV, Blu-ray, or remux. |
| `upgradePending` | Tri-state flag | Optional | Sonarr/Radarr | Maps to `qualityCutoffNotMet`; it describes policy state, not observed quality. |
| `customBadges` | Ordered set of custom metadata values | Optional | Provider/configuration | Custom-format names or configured provider values. Bounded to 32 values of at most 128 characters; blank values and control characters are removed. |
| `extensions` | Namespaced extension map | Optional | Additional providers/generated | Future metadata that an older renderer may ignore. |
| `metadataFingerprint` | Opaque fingerprint | Yes | Generated | Includes every record/file identity component, badge-affecting values, and their schema version. |

#### Value conventions

`Quality descriptor` contains a display label, source classification, numeric
resolution when known, modifier/revision when relevant, and an optional provider
local quality identifier. The provider local ID is for equality within one
connection only.

`Resolution descriptor` contains width, height, display label, and an origin
such as provider quality, provider media info, Jellyfin media stream, or
derived. A quality parser's resolution and stream dimensions are related but
not interchangeable.

`Dynamic-range descriptor` contains a normalized family, optional profile name,
and a confidence/origin state. Missing media information is `Unknown`.

`Custom badges` are data values, not arbitrary executable markup. The renderer
decides whether a configured value is displayable.

#### V1 renderable selector vocabulary

ADR-009 defines the V1 selector vocabulary consumed by the renderer. Selectors
are canonical field selectors, never provider DTO paths:

| Selector | Value rule | V1 display behavior |
| --- | --- | --- |
| `Quality` | Confirmed actual quality display label | First technical pill; quality profile is never eligible. |
| `Resolution` | Confirmed normalized resolution display label | Second technical pill. |
| `DynamicRange` | Confirmed dynamic range or Dolby Vision state | Dolby Vision replaces the generic range label. |
| `Source` | Confirmed source display label | Technical pill after range. |
| `VideoCodec` | Confirmed normalized video codec | Technical pill after source. |
| `Audio` | Confirmed feature, codec, and channel values | One composite pill with feature before codec before channels. |
| `CustomBadge` | One bounded `customBadges` value | One candidate per value, in canonical order, after standard fields. |
| `UpgradePending` | Explicitly true tri-state value | Separate `UPGRADE` status pill; false and unknown are omitted. |

`Extensions`, provider identity, Arr record/file IDs, quality profiles, custom-
format scores, titles, and episode numbering are not V1 selectors. An older
renderer may ignore future selectors without treating them as unknown negative
claims.
