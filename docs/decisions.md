# Architecture Decisions

## ADR-001: Persisted Derived Poster Artwork

**Status:** Accepted

**Date:** 2026-09-16

### Context

ArrTags must display Sonarr/Radarr-derived badges to native and web Jellyfin
clients while using Jellyfin's normal image authorization and caching behavior.
It must not modify original media files or write directly to Jellyfin's image
cache.

Jellyfin 12 provides supported APIs for adding or replacing item artwork, but it
does not provide a supported non-persistent post-processing hook for existing
image responses. MVC filters, ASP.NET middleware, and replacement of the global
image processor are public or technically usable mechanisms, but depend on
Jellyfin implementation details and do not establish a stable image-overlay
contract.

### Decision

ArrTags will generate derived poster artwork asynchronously and publish it as a
normal Jellyfin item image through the supported item-image APIs, centered on
`IProviderManager.SaveImage` and the normal item update flow.

The published image will be served by Jellyfin's standard image routes. ArrTags
will not intercept or replace native image HTTP responses at request time.

Before replacing an active image, ArrTags must follow the ownership contract in
ADR-002. Jellyfin ownership metadata is not assumed; the plugin retains an exact
source artifact and compares a persisted expected active-image identity before
any subsequent publication or restoration.

ArrTags must not write original media files or Jellyfin's `resized-images` cache
directly. The exact storage and restoration policy remains subject to the
implementation-time validation for the selected Jellyfin 12 ABI.

### Consequences

- Native clients and web clients use Jellyfin's normal authorization, image
  tags, resizing, and HTTP cache behavior.
- The active Jellyfin poster is a persisted derived image while ArrTags owns
  the publication; the original source is preserved through plugin-owned state,
  not left as the active image.
- Badge updates are asynchronous publication work rather than per-request
  rendering.
- Jellyfin's normal image tag changes provide the representation invalidation
  boundary after a successful image update.
- ArrTags must track source identity, generated-artwork identity, ownership and
  publication tokens, configuration, renderer, and metadata fingerprints.
- Disabling or uninstalling ArrTags requires a guarded restoration path.
- Jellyfin Web may still display duplicate client-side Enhanced quality tags;
  ArrTags must not depend on Enhanced internals.
- A failed or cancelled render must leave the current usable artwork unchanged.

### Rejected alternatives

#### MVC action filter

Rejected as the V1 delivery mechanism. It is a public ASP.NET Core mechanism,
but not a supported Jellyfin image-overlay contract. It depends on internal
controller actions, result types, filter ordering, authorization timing, and
response-validator behavior.

#### ASP.NET Core middleware

Rejected as the V1 delivery mechanism. It can technically rewrite image
responses, but it must reproduce route matching, authorization, buffering,
conditional requests, ranges, cache headers, and interaction with other
middleware and filters.

#### `IImageProcessor` or `IImageEncoder` replacement

Rejected. These are global image services, not badge-specific extension points;
the Jellyfin image processor is a core singleton and replacement would affect
unrelated image processing.

#### `IDynamicImageProvider` as a request-time overlay

Rejected for that purpose. It is a supported refresh-time provider and its
generated output is persisted or attached as artwork. It does not receive an
existing poster for transparent per-request compositing. It may be considered
as part of refresh orchestration, but it is not the response-delivery contract.

#### Custom plugin image route

Rejected as the primary V1 delivery mechanism. A plugin controller route is
supported, but native clients will not automatically request it and ArrTags
would own separate authorization and HTTP cache semantics.

#### Jellyfin Enhanced internals or client overlay

Rejected. Enhanced internals are not a supported integration surface, and a
client overlay does not provide native-client parity.

### References

- [`docs/research/jellyfin-12-architecture.md`](research/jellyfin-12-architecture.md)
- [`docs/research/poster-rendering-strategies.md`](research/poster-rendering-strategies.md)
- [`docs/architecture.md`](architecture.md)
- ADR-002 below
- Jellyfin `IProviderManager.SaveImage` and `IDynamicImageProvider` APIs

## ADR-002: Guarded Artwork Ownership and Restoration

**Status:** Accepted

**Date:** 2026-09-17

### Context

Persisted derived artwork changes the active Jellyfin image. Jellyfin 12's
supported item-image APIs do not attach plugin ownership metadata to that image.
The item-image information includes an image tag, path, dimensions, size, and
other observations, but none identifies which actor last changed the image. The
image tag is a Jellyfin cache/representation validator and is not sufficient to
prove ArrTags ownership.

ArrTags must preserve the original image across repeated publications and must
never restore or remove its image after a user or another component has replaced
it.

### Decision

ArrTags will persist one `PublishedArtworkState` per Jellyfin item and image
surface. The state contains:

- An immutable, content-addressed source artifact containing the exact original
  bytes, MIME type, length, and SHA-256 integrity hash, or an explicit absent
  baseline.
- The source-capture identity observed before the first ArrTags publication.
- A random `ownershipToken` that remains stable for the original-to-derived
  session.
- A new random `publicationToken` for every successful derived publication.
- An expected active-image identity containing the surface, active content hash,
  and all available Jellyfin image identity observations.
- A logical publication fingerprint and explicit ownership/restoration state.

The tokens are ArrTags state identifiers. They are not embedded in the image and
are not treated as Jellyfin guarantees. Ownership is proven only by comparing a
fresh active-image observation with the persisted expected identity. Content
hash equality is required; a path, date, size, or Jellyfin image tag alone does
not prove ownership. If the required observation cannot be made, ownership is
unknown and ArrTags fails closed.

When publishing v2 after v1, ArrTags must first verify that v1 is still active,
reuse the first source artifact, retain the same ownership token, and create a
new publication token and expected active identity. An ArrTags image is never
captured as a new original.

### State transitions

- A new session captures the current source image or records absence before
  entering `Published`.
- A matching active identity permits a repeated publication from the retained
  source artifact.
- A changed active identity enters `OwnershipLost`; an unverifiable identity
  enters `OwnershipUnknown`. Both states prohibit automatic publication,
  restoration, and removal.
- Disable or uninstall enters `RestorePending`. Restoration is allowed only
  after a fresh identity match and source-artifact integrity check.
- A present baseline is restored through the supported item-image API. An absent
  baseline is restored by removing the ArrTags image through that API.
- Successful restoration is verified before entering `Restored`; a missing or
  corrupt source enters `RestoreBlocked` without changing the active image. An
  uncertain result after an attempted restoration also enters `RestoreBlocked`
  without another automatic mutation.
- Item removal enters `Removed` and performs no image mutation against the
  removed item.

### Consequences

- ArrTags can distinguish its active derived image from the retained original
  across restarts and repeated publications, provided its persisted state and
  source artifact remain intact.
- A later user/provider/plugin change is preserved because any mismatch or
  uncertainty blocks mutation rather than attempting to infer the actor.
- Jellyfin's image tag remains useful as supporting replacement evidence, but
  ArrTags does not depend on it as an ownership token.
- Source artifacts require bounded retention, integrity validation, safe file
  permissions, and cleanup policy.
- A byte-identical replacement that preserves every observable identity value is
  indistinguishable from the expected image through Jellyfin's supported APIs;
  the contract treats observable equality as ownership because no stronger actor
  attribution exists.

### Deferred from this ADR

This ADR defines ownership evidence and guarded logical transitions. Crash-
consistent ordering between source capture, rendering, `SaveImage`, item
persistence, provenance persistence, restart reconciliation, disable, uninstall,
and item removal is defined by ADR-003. Exact ABI and host-storage validation
remains an implementation-time requirement.

### References

- `docs/data-model.md`, sections 3.10.1 and 3.10.2
- `docs/architecture.md`, section 9
- `docs/research/jellyfin-12-architecture.md`, sections 4.3 and 7
- Jellyfin 12 `IProviderManager.SaveImage`, `ImageInfo`, `ItemImageInfo`, and
  standard item-image APIs

## ADR-003: Crash-Recoverable Artwork Publication

**Status:** Accepted

**Date:** 2026-09-17

### Context

Jellyfin's supported `SaveImage` API, normal item repository update, and
ArrTags' plugin-state persistence do not participate in one transaction. A
failure between them can leave an image mutation without provenance, provenance
for an image that was not published, or staged artifacts that are unsafe to
delete. Disable, uninstall, and item removal add lifecycle races to the same
problem.

ADR-002 already defines the source artifact, active-image identity, ownership
token, publication token, and fail-closed ownership states. Blocker 2 requires a
durable protocol that preserves those semantics across interruption and restart.

### Decision

ArrTags will use one durable `ArtworkOperation` write-ahead record per item and
image surface. It will use immutable, integrity-checked staged artifacts and
postcondition reconciliation instead of claiming a distributed transaction with
Jellyfin.

The operation protocol is:

1. Capture the original source artifact or explicit absent baseline and promote
   it to durable storage before any image mutation.
2. Render the derived artifact, validate its bounded output and hash, and
   promote it to durable storage. Evictable render-cache entries are never the
   only recovery copy.
3. Persist a `Prepared` operation containing the exact before identity,
   candidate after content hash, source/derived artifact references, generation,
   ownership token, publication token, and target final state.
4. Revalidate the before identity, persist `MutationStarted`, then call
   `SaveImage` or the supported image-removal operation.
5. Persist the repository-update phase, call the normal Jellyfin item update,
   and persist `VerificationPending` before reading back the effective image
   identity.
6. If the after identity is verified, persist the final `PublishedArtworkState`
   or restoration state with the operation ID and new state revision, then mark
   the operation `Committed`.
7. Cleanup is separate from commit and may only remove artifacts proven not to
   be the active image.

Operation phases are lower-bound markers. If interruption occurs after
`MutationStarted`, recovery assumes the external call may have happened even if
its acknowledgment was not persisted. Recovery compares the current identity to
the durable before and after identities:

- Before match: revalidate the generation and lifecycle fence, then retry the
  same deterministic operation without recapturing a source.
- After match: ensure the normal item update is persisted, commit the intended
  final plugin state, and mark the operation committed.
- Neither match: record `OwnershipLost` when observable or `OwnershipUnknown`
  when not observable; never restore, remove, or overwrite the current image.
- Invalid operation, state, or artifact: quarantine it and enter
  `RecoveryBlocked`; never blindly replay or clean up.

Recovery runs before new work for the item/surface is accepted. A lifecycle
fence prevents new publication during disable or uninstall. Existing operations
are resolved first, then a still-owned publication creates a separate guarded
restoration operation. An unresolved restoration keeps its journal and source
artifact. Confirmed item removal creates a tombstone and performs no image
mutation against the missing item.

Durable records use versioned integrity metadata, stable-storage flushing, and
atomic replacement. A torn record is quarantined. The final plugin state is
durable before the journal is marked `Committed`; if a crash occurs between
those writes, a verified final state allows startup to complete the journal.

### Consequences

- A crash before image mutation leaves no published artwork and only staged
  artifacts to clean up.
- A crash during or after `SaveImage` is resolved by observing the effective
  before/after image, not by assuming the API call succeeded or failed.
- Source provenance remains retained until the final state is committed and
  cleanup is proven safe.
- Replays are bounded, serialized, deterministic, and generation-fenced; they
  do not recapture an ArrTags image as a new original.
- External changes and incomplete observations fail closed, preserving the
  currently observable artwork rather than attempting rollback.
- Disable and uninstall cannot claim complete cleanup while unresolved journal
  records or restoration obligations remain.
- Exact `SaveImage` representation, readback, stable-storage, and host-specific
  behavior still require implementation-time validation and integration tests.

### References

- `docs/data-model.md`, sections 3.10.3 and 3.10.4
- `docs/architecture.md`, sections 5, 6, 8, 9, and 11
- `PLANS.md`, sections 5 and 6
- ADR-002: Guarded Artwork Ownership and Restoration

## ADR-004: Foundation Operational Limits and Defaults

**Status:** Accepted

**Date:** 2026-09-17

### Context

The V1 architecture requires bounded queue, provider, rendering, HTTP, retry,
artifact, cache, retention, and stale-state behavior, but deliberately deferred
concrete values to implementation. The persisted-artwork and crash-recovery
protocols add authoritative state that must never be treated as evictable cache.
Without explicit values and validation rules, the foundation could allow
unbounded work or discard recovery state, and later milestones would have no
agreed baseline to build and tune against.

### Decision

ArrTags accepts the concrete foundation defaults recorded in
`docs/architecture.md`, section 12. They cover update queue capacity,
per-item/surface single-flight work, provider and render concurrency, HTTP
timeout, transient retry count and exponential backoff, maximum provider
response size, source and derived artifact sizes, decoded image dimensions,
full-reconciliation batch size, metadata last-known-good and render work-cache
windows, render work-cache and authoritative storage quotas, terminal provenance
retention, and the stale last-known-good duration.

The limits are validated at configuration load time. A value outside its
documented range, or a non-finite value where a finite value is required,
rejects the new configuration and retains the last valid snapshot rather than
partially applying it.

Authoritative state has priority over cache storage:

- Active provenance and non-terminal artwork operations are never evicted as
  ordinary cache entries, regardless of cache or quota pressure.
- The render work cache is bounded independently from authoritative provenance.
- When the authoritative storage quota is exhausted, ArrTags rejects new derived
  work and preserves the current artwork instead of evicting recovery state.

This decision selects initial foundation values. It does not reopen ADR-001,
ADR-002, or ADR-003, and it does not decide the provider, matching, rendering,
artwork, or webhook questions that remain gated by later milestones.

### Consequences

- Every required operational limit has an explicit value, unit, validation
  rule, and safe failure behavior.
- Configuration cannot introduce unbounded queue, network, render, or storage
  work.
- Cache eviction cannot destroy ownership or recovery state.
- Later milestones may tune values within the documented validation ranges
  without reopening this decision. Changing a range or the authoritative-state
  priority requires a new decision.
- Runtime enforcement, boundary tests, and representative-load validation land
  with the configuration and state foundation tasks (1.5 and 1.7) and the
  performance milestone.

### References

- `docs/architecture.md`, sections 6, 9, 11, and 12
- `docs/data-model.md`, sections 3.9, 3.10, and 3.12
- `PLANS.md`, tasks 1.4, 1.5, and 1.7, and decision gate DG-6

## ADR-005: Versioned Secret Access Boundary

**Status:** Accepted

**Date:** 2026-09-18

### Context

ArrTags must send Sonarr and Radarr API keys in authenticated requests, but the
canonical configuration snapshot is intentionally secret-free. The persisted
`PluginConfiguration` currently contains the API-key fields because Jellyfin's
plugin configuration mechanism is the existing configuration owner. The current
provider connection model exposes only `HasApiKey`; it must not make workers read
the mutable plugin configuration directly.

The same boundary will eventually be needed for the inbound webhook secret, but
webhook exposure, authorization, replay handling, and payload policy remain
separate implementation decisions.

### Decision

Jellyfin's persisted plugin configuration remains the only V1 source of truth
for API keys and the webhook shared secret. ArrTags will not add a second secret
file, cache record, database table, environment-variable convention, or external
secret-management dependency.

The configuration boundary will create one immutable, private in-memory secret
snapshot at the same time as each public `PluginConfigurationSnapshot`. The
active state is one atomically replaced pair:

- the public, secret-free configuration snapshot and its monotonic
  `configurationVersion`; and
- a private map from typed `SecretReference` values to secret material.

The public snapshot may carry the safe reference, but never the referenced
value. A secret reference is an ArrTags-generated opaque slot identifier, not a
user-provided vault path and not a hash of the secret. V1 uses separate slots for
the Sonarr API key, Radarr API key, and webhook shared secret. The current V1
one-connection-per-provider constraint makes these slots unambiguous; any future
multiple-connection support must define additional connection-scoped slots first.

The DI boundary is a singleton `IPluginSecretResolver` with the following
semantic contract:

```text
TryAcquire(reference, expectedConfigurationVersion) -> SecretLease or no result
```

`SecretLease` is short-lived, disposable, and non-serializable. It has no public
diagnostic/string representation. The provider transport boundary may use an
API-key lease to add `X-Api-Key` to the current request only. A webhook boundary
may use a webhook lease for constant-time candidate comparison. A lease is never
placed in a queue item, canonical object, cache entry, exception, log, or
persisted record.

Provider workers obtain the public snapshot first, resolve the connection and
its safe reference, then acquire a lease for that snapshot's configuration
version. A version mismatch returns no lease and causes the worker to discard or
restart the bounded operation using the current snapshot. The HTTP client
factory remains secret-free; concrete provider clients apply the lease to the
request header and never put the key in a URL or query string.

The resolver publishes immutable maps and permits concurrent read-only lease
acquisition. Each worker receives an independent lease; no lock is held across
external I/O, and no provider client stores a lease beyond the bounded request
or authentication comparison that acquired it. Cancellation, timeout, shutdown,
or request completion releases the lease reference. The resolver does not
create unbounded copies proportional to queue size.

### Activation and replacement

1. Jellyfin loads the persisted `PluginConfiguration` at startup. ArrTags
   validates it without including secret values in errors.
2. For a valid candidate, the configuration boundary copies the API keys and
   webhook secret into the private secret snapshot, creates the secret-free
   public snapshot and safe references, increments the configuration version,
   and atomically publishes the pair.
3. An invalid candidate publishes neither component. The previous public
   snapshot and private secret snapshot remain active.
4. Configuration activation must use the same replacement path for startup and
   later saves. Workers never retain or read the mutable `PluginConfiguration`
   object.

Secret rotation keeps the same safe reference and connection identity when the
provider kind and base URL are unchanged. It increments the configuration
version but does not put the new or old key into a configuration fingerprint or
cache key. New operations acquire only the new lease. An already acquired lease
may complete its bounded, cancellable request with the old value; after that
lease is released, the old value is no longer retained by the active resolver.
Retries must reacquire against the current configuration version and must not
blindly repeat an authentication failure with an old lease.

Disabling a provider publishes a new version that refuses new lease acquisition
for that connection. Lifecycle cancellation fences new work and drains or
cancels existing requests within the configured host shutdown limits. A plugin
restart reconstructs the private snapshot from the persisted plugin
configuration; no secret is recovered from plugin state, metadata cache, or
artwork state.

### Security requirements

- API keys and the webhook secret may exist only in Jellyfin's persisted plugin
  configuration, the private in-memory snapshot, a short-lived lease, and the
  outbound authenticated request while it is being sent.
- Safe references, provider kind, connection ID, and configuration version may
  appear in diagnostics. Secret values may not appear in logs, exception
  messages, status responses, telemetry, fingerprints, URLs, headers recorded
  by diagnostics, cache/state records, or serialized snapshots.
- Provider errors expose only bounded safe codes and messages. Authentication
  failures do not include request objects, response bodies, or header values.
- The API key is sent only as `X-Api-Key`; query-string authentication is not
  permitted because URLs are more likely to be persisted or logged.
- The resolver does not promise memory zeroization for managed strings. Its
  protection boundary is short lifetime, no duplication into domain/state
  objects, atomic replacement, and controlled use by the transport/auth
  boundary. Host filesystem permissions remain responsible for protecting
  Jellyfin's persisted plugin configuration at rest.
- The webhook shared secret uses a distinct typed reference and purpose. This
  ADR provides its storage/access foundation but does not authorize exposing a
  webhook route or define webhook replay/rate/payload policy.

### Rejected alternatives

#### Read `Plugin.Configuration` from each provider client

Rejected. It shares mutable host-owned configuration with workers, can combine
an old public snapshot with a new secret, makes rotation races implicit, and
violates the existing immutable-snapshot boundary.

#### Put API keys in `PluginConfigurationSnapshot`, `ArrConnection`, or state

Rejected. Those values are used in canonical data, cache identity, diagnostics,
and persistence boundaries. Adding the actual secret would violate the explicit
secret-free model and expand the credential exposure surface.

#### Persist a separate ArrTags secret file or state record

Rejected. It duplicates Jellyfin's supported plugin configuration persistence,
creates recovery and rotation ordering problems, and would put credentials near
state that is deliberately designed for cache/artwork recovery rather than
secret storage.

#### Use an environment variable, OS vault, or general-purpose secret manager

Rejected for V1. No such host-independent Jellyfin plugin contract is currently
required, and adding one would make configuration administration and restart
behavior deployment-specific. It can be reconsidered only if Jellyfin provides
an approved secret facility or the product scope changes.

#### Put the key in a URL, query parameter, connection ID, or fingerprint

Rejected. These values are routinely logged, cached, compared, and persisted;
the Sonarr and Radarr research explicitly recommends the `X-Api-Key` header.

### Consequences

- Task 2.3 and task 2.4 have one provider-neutral credential contract for both
  providers and a defined path for the future webhook controller.
- The current secret-free configuration snapshot remains safe for queues,
  canonical state, cache keys, and diagnostics.
- Configuration replacement and key rotation are generation-fenced and do not
  invalidate provider identity merely because a key changed.
- The provider implementation must add the safe reference/version plumbing and
  resolver registration before making authenticated requests, but it does not
  need another architectural decision for credential access.
- Credential boundary tests must cover startup hydration, invalid replacement,
  secret rotation, version mismatch, disabled connections, restart hydration,
  lease disposal, and secret exclusion from diagnostics and serialization.

### References

- `docs/architecture.md`, sections 4, 6, 7, 8, and 11
- `docs/data-model.md`, sections 3.3 and 3.12
- `docs/reviews/pre-implementation-review-01.md`, section C
- `docs/research/sonarr-api.md`, "Connection and authentication" and "Errors, resilience, and request discipline"
- `docs/research/radarr-api.md`, "Authentication and API key handling"
