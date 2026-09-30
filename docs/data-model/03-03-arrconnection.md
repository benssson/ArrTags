# 3.3 ArrConnection

**Purpose:** The configuration and identity of one Sonarr or Radarr server.
Secrets are configuration inputs but are deliberately not copied into match,
metadata, event, or cache identity values.

| Field | Type | Required | Field Ownership | Notes |
| --- | --- | --- | --- | --- |
| `connectionId` | Opaque stable identifier | Yes | Plugin/configuration | Used to scope provider records and cache keys. |
| `provider` | ArrProvider | Yes | Plugin/configuration | Identifies Sonarr versus Radarr and the configured instance. |
| `baseUrl` | Absolute URL | Yes when enabled | Configuration | Normalized URL base without an API path; safe to show only according to admin policy. |
| `enabled` | Boolean | Yes | Configuration | Disabled connections are not queried. |
| `requestTimeout` | Duration | Yes | Configuration | Finite request limit. |
| `tlsPolicy` | TLS validation policy | Yes | Configuration | Strict by default; exceptions are explicit and connection-scoped. |
| `secretReference` | Opaque, typed secret-slot reference | Yes | Configuration | References the protected API-key slot; it is safe metadata, remains stable during key rotation, and never contains the secret value. The value may be absent while the reference still exists. |
| `configurationVersion` | Monotonic integer | Yes | Configuration | The configuration generation this connection was derived from; scopes credential leases to the generation that produced the connection. |
| `health` | Connection health state | Optional | Generated/cached | `Healthy`, `Unavailable`, `AuthenticationFailed`, `Incompatible`, or `Unknown`. |
| `lastProbedAt` | Timestamp | Optional | Generated/cached | Time of the latest connection/version probe. |

`SecretReference` is generated at the configuration boundary, not entered as a
provider URL or looked up in a general-purpose vault. V1 has one slot for the
Sonarr API key and one for the Radarr API key because V1 has at most one
connection of each provider kind. The webhook shared secret has a separate
typed slot and must never be interchangeable with an Arr API-key reference.
ADR-012 uses that slot only for constant-time inbound webhook authentication.
Future multi-connection support must add connection-scoped slots before it is
enabled.

The actual secret remains in Jellyfin's persisted `PluginConfiguration` and in
the private in-memory secret snapshot owned by the configuration boundary. It
is not a canonical domain value. `ArrConnection` and its containing snapshots
may carry the safe reference and a `hasApiKey` diagnostic flag, but never the
referenced string.

Conceptually, a `SecretReference` contains only a purpose (`ArrApiKey` or
`WebhookAuthentication`) and a stable opaque slot identifier. It contains no
provider URL, API key, webhook value, or external vault address.
