# 3.12 Configuration

**Purpose:** An immutable configuration snapshot controlling enabled providers,
badge selection, rendering, cache policy, and update behavior.

| Field | Type | Required | Field Ownership | Notes |
| --- | --- | --- | --- | --- |
| `configurationVersion` | Version identifier | Yes | Configuration/plugin | Increments when the effective configuration changes. |
| `connections` | ArrConnection set | Yes | Configuration | Sonarr and Radarr can be independently enabled. |
| `libraryScope` | Set of Jellyfin collection-folder/library identifiers and eligible item types | Yes | Configuration | Entries are collection-folder/library identifiers, not display names; limits matching and rendering eligibility. V1 badge surfaces are Movie and Episode posters; Series/Season are structural only (ADR-006). An empty set means no library restriction. |
| `badgeDefinitions` | Ordered BadgeDefinition set | Yes | Configuration | Defines what metadata is displayed and how. |
| `renderingPolicy` | Render size, format, placement, and limits | Yes | Configuration | Output-affecting values belong in the configuration fingerprint. The global badge `Position` (four corners plus center, default bottom-left) and `Size` (Small/Medium/Large, default medium) are user-adjustable (ADR-019); format, reference geometry, text limits, and the renderer version remain code-owned. |
| `cachePolicy` | TTL, stale window, size, and eviction limits | Yes | Configuration | Separate metadata freshness from artwork retention. |
| `updatePolicy` | Schedule, webhook, retry, and queue policy | Yes | Configuration | Webhooks accelerate reconciliation; they do not replace it. |
| `enhancedCoexistencePolicy` | Existing poster and selector enable flags | Yes | Configuration | Realized by the existing `BadgeMoviePosters`/`BadgeEpisodePosters` flags and renderer selector enablement; ADR-011 adds no automatic duplicate/overlap suppression and no Enhanced-internals dependency. |
| `pathMappings` | Optional connection-scoped mappings | Optional | Configuration | Reserved post-V1; ADR-008 defers path fallback and V1 snapshots do not carry this field. |
| `secretReferences` | Protected, typed secret-slot references | Optional | Configuration | API keys and webhook secrets are represented only by safe references; values are excluded from fingerprints, logs, canonical snapshots, and state. |
| `logVerbosity` | Bounded level enum | Yes | Configuration | Per-plugin log verbosity: `Off`/`Error`/`Warning`/`Information`/`Debug`/`Trace`, default `Warning`. Validated at load and exposed through the settings page and the XML configuration. Not output-affecting: excluded from the renderer/configuration fingerprints and never changes `RenderVersion`. |

The conceptual `renderingPolicy`, `cachePolicy`, and `updatePolicy` objects above
are realized incrementally. At the foundation boundary, the persisted
`PluginConfiguration` holds the independently enabled Sonarr and Radarr
connections, library and image scope, and webhook secret, while an
`OperationalLimits` instance carries the queue, concurrency, timeout, retry,
artifact-size, decode, cache, quota, retention, and stale-window limits accepted
by ADR-004 and recorded in `docs/architecture/12-performance-and-operational-limits.md`.

**Collection persistence shape (task 9.1).** The two persisted collection
properties, `PluginConfiguration.EnabledLibraries` and
`RendererConfiguration.Selectors`, are **settable** (`get`/`set`) with a
null-coalescing setter that treats a null value as an empty collection. The
pinned Jellyfin 12.0.0 elevation-gated `PluginsController` POST deserializes the
request body with `Jellyfin.Extensions.Json.JsonDefaults.Options`, whose default
`System.Text.Json` object-creation handling (`Replace`) does not populate a
get-only collection property; a get-only shape silently dropped both collections
on a dashboard save. Making the properties settable lets the POST round-trip
populate them, and the setter preserves the non-null invariant the validator and
snapshot rely on. The persisted XML shape is unchanged
(`<EnabledLibraries><string>…</string></EnabledLibraries>` and
`<Renderer><Selectors><BadgeSelectorConfiguration>…`), so existing
`plugins/configurations/ArrTags.xml` files remain loadable.

`PluginConfigurationSnapshot` is the immutable, secret-free view validated by
`PluginConfigurationValidator` and supplied to workers; API keys and webhook
secrets remain only in the persisted configuration and appear in canonical state
as `secretReferences`, never as values. The configuration boundary publishes
that public snapshot together with a private, version-matched in-memory secret
snapshot. The private snapshot is not a canonical model, cache record, state
envelope, or serialization format. Workers acquire a short-lived secret lease
by reference and configuration version immediately before external
authentication. Badge definition persistence and rendering-style configuration
remain implementation work, but their V1 selector, layout, output, and failure
semantics are fixed by ADR-009. Path mappings are explicitly post-V1 under
ADR-008 and are not part of the V1 snapshot. The Jellyfin Enhanced coexistence
policy (ADR-011) is realized by the existing poster and renderer selector enable
flags; it adds no snapshot field and no automatic duplicate/overlap suppression.

**Logging verbosity (tasks 10.1-10.2).** `PluginConfiguration.LogVerbosity` is the
bounded per-plugin log verbosity (`Off`/`Error`/`Warning`/`Information`/`Debug`/
`Trace`, default `Warning`). It is validated at configuration load by
`PluginConfigurationValidator` (an undefined level rejects the candidate and the
last valid snapshot stays active), persisted in the XML configuration, exposed
through the settings page, and carried on the immutable
`PluginConfigurationSnapshot` as `LogVerbosity`. A plugin-owned
`ILogVerbosityGate` reads the snapshot's verbosity on every call and decides
whether a `Microsoft.Extensions.Logging.LogLevel` is enabled, so a replaced
configuration applies without a host restart; ArrTags registers no custom
`ILoggerProvider`/sink and does not replace the host `ILoggerFactory`. The value
is not output-affecting: it is excluded from the renderer/configuration
fingerprint and never changes `RenderVersion`. Task 10.2 instruments the
provider, matching, metadata, artwork, queue, reconciliation, webhook, and
lifecycle boundaries through the plugin-owned `IArrTagsLog<T>` facade, which
emits only bounded, already-redacted values under the ADR-020 clause 4 redaction
contract and bounds volume with the code-owned `LogThrottle`; no API key, webhook
secret, `SecretLease` value, secret header, raw request/response body, provider
payload, or mutable `PluginConfiguration` is logged (see
`docs/limitations/00-index.md` SEC-5).

**Badge value allowlist (v1.1 task 12.1).** Each `BadgeSelectorConfiguration`
entry gains a bounded `AllowedValues` string list (ADR-017). It is persisted in
the XML configuration as
`<Renderer><Selectors><BadgeSelectorConfiguration><AllowedValues><string>…` and
is exposed through the settings page per selector as comma-separated text. An
empty list means no restriction. `RendererConfiguration.Validate` rejects more
than 32 entries per selector, an entry longer than 64 characters, a blank entry,
a control-character entry, or a duplicate after case-insensitive comparison, with
bounded secret-free messages; the validator never includes a configured allowlist
value. The resolved `BadgeDefinition` carries the allowlist, and a non-empty
resolved allowlist is included in the renderer configuration fingerprint
(case- and order-normalized); an empty allowlist adds nothing to the fingerprint
(identity-neutral relative to a non-empty allowlist), though the coordinated v1.1
schema advance still changes the default configuration fingerprint. The value is
provider-neutral and never references a provider DTO path, record identifier,
quality profile, credential, or extension value.

**Badge position and size (v1.1 task 12.3).** `RendererConfiguration` gains the
global `Position` and `Size` enums (ADR-019). They are persisted in the XML
configuration as `<Renderer><Position>…</Position><Size>…</Size>` and exposed on
the settings page as two selects (anchor and preset size). `Position` is
`BottomLeft` (default), `TopLeft`, `TopRight`, `BottomRight`, or `Center`; `Size`
is `Medium` (default), `Small`, or `Large`. `RendererConfiguration.Validate`
rejects an undefined enum value with a bounded, secret-free message, and the
resolver carries the value onto the resolved `RenderOutputPolicy` (defaulting a
tolerantly read undefined value). The value is output-affecting: a non-default
position or size is included in both the renderer configuration fingerprint and
the render fingerprint, while the default position and size are identity-neutral
relative to other placement values (the default reproduces the V1 output, so its
PNG bytes are unchanged), but the coordinated v1.1 version advance changes the
default configuration and output fingerprints. Placement and size are global
renderer policy only; they are not per selector. The v1.1 allowlist and placement
changes share one coordinated advance: `RendererConfiguration.CurrentSchemaVersion`
advanced from 1 to 2 and `RenderVersion.CurrentRendererVersion` advanced from 2 to
3, and the committed goldens were regenerated with no writer or auto-approval
path (the nine default-configuration PNGs are byte-unchanged while their output
fingerprints advance with the renderer version, and anchor/size goldens were
added).

**Runtime activation (task 9.3).** The persisted `PluginConfiguration` is the
candidate supplied to `Plugin.UpdateConfiguration`. The override validates the
candidate before the base implementation persists anything (using the same
`PluginConfigurationValidator` the snapshot service uses); a valid candidate is
persisted by the host base implementation and then activated through
`ConfigurationSnapshotService.TryReplace` (ADR-016 clause 4), so a saved change is
observed without a host restart for the values resolved per operation (some
construction-captured limits still require a host restart; see
`docs/limitations/00-index.md` F2 and the ADR-016 implementation note in
`docs/decisions/00-index.md`). An invalid candidate is rejected before
persistence: the override does not call the base implementation, so a rejected
candidate is never written to `plugins/configurations/ArrTags.xml` and neither the
public snapshot nor the private secret map changes. The whole
validate/persist/activate sequence is serialized, so concurrent saves cannot leave
the running snapshot, the in-memory configuration, and the persisted file
divergent (security finding SEC-9.3-01). The validation messages are bounded and
secret-free; the validator never includes a secret value. The rejection is
surfaced to the administrator as exactly one bounded, secret-free activity-log
entry through the plugin-owned `IConfigurationRejectionNotifier` adapter
(ADR-021); the entry is not canonical configuration or state and carries no
secret or candidate value. Services that resolve
from the current snapshot per operation (work queue capacity and in-flight
bound, provider/render concurrency, metadata freshness, badge definitions, and
the renderer output policy) observe the replaced snapshot by subsequent work. A
successful activation also requests the bounded, non-blocking post-save
reconciliation (task 9.4, ADR-016 clause 5 second bullet), which enqueues the
same provider-neutral `LibraryWorkHint` work as every other trigger at the new
configuration version, so an existing poster re-renders promptly instead of
waiting for the next scheduled run; the trigger adds no configuration or state
field. Task 9.5's Goal A integration verification exercises the full save ->
activate -> bounded-reconcile flow without a live host, and limitation F2 is
recorded as resolved.

#### 3.12.1 Secret resolution semantics

The provider-neutral credential contract is a versioned resolver equivalent to:

```text
TryAcquire(secretReference, expectedConfigurationVersion) -> SecretLease or no result
```

`SecretLease` is short-lived, disposable, and non-serializable. It has no public
diagnostic/string representation. The provider transport boundary uses it only
to apply `X-Api-Key` to an authenticated request; a future webhook boundary uses
the distinct webhook lease for constant-time candidate comparison. A queue item
may carry a safe reference and configuration version, but never a lease or
secret.

The public snapshot and private secret snapshot are replaced as one active
configuration generation. Invalid replacement input changes neither. A rotated
key keeps its safe reference and connection identity when the provider and base
URL are unchanged, while new work receives a new configuration version and new
lease. An already acquired lease may finish its bounded request; retries must
acquire against the current version. On restart, the private snapshot is
reconstructed from persisted plugin configuration and no secret is recovered
from canonical state or cache.
