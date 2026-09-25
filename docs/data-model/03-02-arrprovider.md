# 3.2 ArrProvider

**Purpose:** A provider type and provider-instance identity that can supply
metadata without exposing provider-specific API response types.

| Field | Type | Required | Field Ownership | Notes |
| --- | --- | --- | --- | --- |
| `kind` | `Sonarr` or `Radarr` | Yes | Plugin | The supported provider family. |
| `providerInstanceId` | Opaque instance identifier | Yes | Plugin/configuration | Identifies one configured server, not an Arr local record. Must not contain an API key. |
| `displayName` | String | Optional | Configuration | Human-readable administrative name. |
| `applicationVersion` | Version string | Optional | Provider, cached | Observed during connection probing; informational and feature-gating only. |
| `apiContract` | Contract identifier, such as `v3` | Yes | Provider/configuration | Both initial integrations use the Arr v3 API contract. |
| `capabilities` | Set of supported capability names | Optional | Derived from provider/version | Allows optional metadata fields without changing the core model. |
