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

## ADR-006: V1 Badge Surfaces and Library Scope Identifier

**Status:** Accepted

**Date:** 2026-09-18

### Context

The media matching milestone needs an unambiguous answer to decision gate DG-2:
which Jellyfin item and image types V1 badges are attached to, and whether
series and season surfaces use an explicit aggregate policy. The canonical
`MediaIdentity` model also records a `libraryId` for eligibility and
configuration scope, and `PluginConfiguration.EnabledLibraries` stores that
scope as strings, but the identifier semantics (collection-folder/library ID
versus display name) were not pinned.

### Decision

V1 badge-bearing item types are **Movie** and **Episode** posters. They are the
only surfaces for which ArrTags creates derived badge artwork.

Series and Season remain supported canonical and matching entities: they are
required as structural and contextual hierarchy for series/episode matching and
for resolving `MediaIdentity.seriesIdentity` and episode numbering. They are
**not** V1 badge surfaces. No aggregate series or season quality badge is
produced, and deriving an aggregate quality from one child file remains
prohibited. Series and season aggregate badges remain in the Post-V1 backlog
and require an explicit aggregation and eligibility policy before being enabled.

`PluginConfiguration.EnabledLibraries` entries are pinned to Jellyfin
**collection-folder/library identifiers**. A scope entry is the identity of the
collection folder (library) that owns an item, not its display name. This is
the canonical scope identifier used when mapping a Jellyfin item to its
library for eligibility; an empty set means no library restriction. No
migration machinery is introduced because the configuration is not yet
connected to Jellyfin's configuration-save path and `EnabledLibraries` defaults
to empty.

### Consequences

- Task 3.1 builds `MediaIdentity` snapshots for Movie, Series, Season, and
  Episode as canonical entities, while V1 badge eligibility is limited to Movie
  and Episode surfaces.
- Series and Season data may be captured and matched but must not be published
  as V1 badge artwork.
- Library eligibility compares an item's collection-folder/library identifier
  against the configured scope entries; display names are never a scope key.
- This ADR resolves DG-2. It does not decide the exact badge fields and image
  output policy (DG-3), episode numbering rules (DG-4), or path mappings (DG-5),
  and it does not reopen ADR-001 through ADR-005.

### References

- `PLANS.md`, decision gate DG-2 and Milestone 3
- `docs/architecture.md`, sections 6 and 14
- `docs/data-model.md`, sections 3.1 and 3.12
- `docs/implementation-readiness.md`, Former Action Disposition item 3

## ADR-007: V1 Episode Numbering Policy for Number Fallback

**Status:** Accepted

**Date:** 2026-09-18

### Context

Milestone 3 matches Jellyfin episodes to Sonarr episode records. The documented
order (`docs/architecture.md` section 7) is the episode TVDB identifier first,
then exact season and episode numbers after the parent series match. Jellyfin
and Sonarr can disagree on episode numbering: specials are relocated relative to
their airing season, anime and alternate orderings use absolute or scene
numbering, and a Jellyfin multi-episode span (`IndexNumberEnd` greater than
`IndexNumber`) can correspond to several Sonarr episode records that share one
episode file. Number fallback therefore could not be enabled until decision gate
DG-4 defined and tested an explicit policy.

### Decision

V1 enables season/episode number fallback only for regular, single episodes and
only after the parent series has matched. The policy is implemented by
`EpisodeNumberingPolicy` and applied by `SeasonEpisodeMatchRule` after the
episode TVDB rule.

- The Jellyfin identity must be an `Episode` with a positive season number and a
  positive episode number, and no multi-episode span.
- The Sonarr candidate must be an episode with a positive season number and a
  positive episode number, and no multi-episode span.
- Season zero represents specials and is excluded from number fallback. Specials
  require the episode TVDB identifier in V1.
- A Jellyfin multi-episode span (`EpisodeNumberEnd > EpisodeNumber`) is excluded
  from number fallback because one Jellyfin item can map to multiple Sonarr
  episode records. An end number equal to the start is treated as a single
  episode.
- Absolute, scene, and other alternate numbering is **not** an identity key in
  V1. Anime and other absolute-numbered series match by the episode TVDB
  identifier or, when season and episode numbers happen to agree, by normal
  number fallback; an absolute-number agreement alone never matches.
- The comparison is exact equality of `(seasonNumber, episodeNumber)` with no
  tolerance. It never uses title, year, path, or air date.
- An ineligible or non-equivalent candidate simply does not satisfy the rule, so
  zero survivors produce `NotFound` and multiple survivors produce `Ambiguous`.
  No badge is produced for either outcome.

### Consequences

- Regular episodes with an agreeing season and episode number can match without
  an episode-level TVDB identifier, which is commonly absent.
- Specials, multi-episode spans, and absolute-numbered anime keep the
  conservative no-badge behavior unless an episode TVDB identifier resolves
  them. Configured path fallback is deferred out of V1 by ADR-008.
- Number matches record `MediaMatchMethod.Number` and carry no matched provider
  identifiers in the match fingerprint, so a change between an identifier match
  and a number match invalidates dependent state.
- This ADR resolves DG-4. DG-5 is resolved separately by ADR-008, and this ADR
  does not reopen ADR-001 through ADR-006.

### References

- `PLANS.md`, decision gate DG-4 and Milestone 3 task 3.5
- `docs/architecture.md`, section 7 "Matching policy"
- `docs/data-model.md`, section 4.4 `MediaMatch` and the `MediaIdentity`
  numbering fields
- `docs/research/sonarr-api.md`, "Episode identity"
- `docs/research/media-metadata-mapping.md`, section 4.2

## ADR-008: V1 Path Fallback Deferred

**Status:** Accepted

**Date:** 2026-09-18

### Context

Decision gate DG-5 asks whether ArrTags needs configured, connection-scoped
Jellyfin-to-Sonarr/Radarr path mappings. The original architecture reserved a
path fallback, but no V1 configuration schema, normalization contract, or
matching rule was approved.

The implemented V1 matching boundary already has the following identity paths:

- Radarr movies use Jellyfin TMDb, then IMDb, identifiers.
- Sonarr series use TVDB, then the other stable provider identifiers available
  in the local Sonarr catalogue.
- Sonarr episodes are scoped to the matched series and use the episode TVDB
  identifier, then the exact regular single-episode season and episode numbers
  approved by ADR-007.

After a record is matched, the Arr-local record and file identifiers retrieve
the current metadata. Sonarr's `episodeFileId == episodeFile.id` join and
Radarr's movie/file relationship are sufficient for that metadata lookup; a
Jellyfin path is not needed to select the current Arr file.

The remaining cases where a path could add coverage are items with missing
provider identifiers, specials without an episode TVDB identifier, and
absolute-numbered or otherwise incompatible episode numbering. Those are
already defined V1 no-badge outcomes when identity evidence is insufficient.

### Decision

Configured, connection-scoped path mappings and path normalization are deferred
out of V1. DG-5 is resolved negatively for the current V1 scope.

V1 therefore has this explicit contract:

- No `pathMappings` field is persisted or published in the V1 configuration
  snapshot.
- No Jellyfin path is compared with a Sonarr or Radarr path for identity.
- No V1 matching rule or `MatchEvidence` may use `ConfiguredPath`.
- `MediaIdentity` location data and `MatchCandidate.PrimaryPath` remain raw
  descriptive context only. They are not normalized identity keys.
- Provider-ID and approved episode-number rules remain the only automatic V1
  identity fallbacks. Title, year, local Arr IDs, and paths are not substitutes
  for cross-system identity.
- Missing, virtual, remote, offline, `.strm`, fileless, ambiguous, or otherwise
  unsupported items continue to produce no new badge when the approved identity
  rules cannot establish exactly one match.
- Existing conceptual `ConfiguredPath` and `pathValidation` model values are
  reserved for a future post-V1 decision. V1 must not emit a matched result
  using them.

### Evaluation

| V1 case | Finding | V1 result |
| --- | --- | --- |
| Normal movies | Radarr exposes TMDb and IMDb identifiers that correspond to the Jellyfin provider IDs. | Match by TMDb, then IMDb. |
| Normal episodes | Sonarr exposes episode TVDB IDs and exact season/episode fields; the parent series is matched first. | Match by episode TVDB, then ADR-007 number fallback. |
| Specials | Season zero is intentionally excluded from number fallback. | Match only by episode TVDB; otherwise no badge. |
| Absolute-numbered/anime episodes | Absolute and scene numbers are not a stable V1 identity key. | Match by episode TVDB or approved regular numbering only; otherwise no badge. |
| Missing or incomplete provider IDs | IDs may be absent when Jellyfin metadata providers have not populated them. | Use the remaining approved identity rules; otherwise `NotFound` or `Ambiguous`, with no badge. |
| Container/host path differences | Jellyfin and Arr commonly expose different namespaces; literal equality can be false or unsafe. | Never assume equivalence and never use paths in V1 matching. |
| Remote, virtual, missing, and `.strm` items | They do not provide a comparable local media file and cannot safely receive file-derived metadata through path matching. | No new badge when the item is not an eligible local file. |
| Multiple sources or versions | A path could select one source without an approved aggregate or representative-source policy. | Do not add path-based disambiguation; preserve the existing fail-closed behavior. |
| Arr record/file IDs | They are connection-scoped local identities and validated file joins after a record match, not Jellyfin-to-Arr identity keys. | Use them for current metadata and invalidation, not as a path-fallback replacement. |
| Operational risk | Normalization would need namespace mapping, boundary-safe comparison, case/separator policy, symlink behavior, and ambiguity handling. | Avoid this V1 false-match and attack surface. |

Deferring path fallback does not materially break the stated V1 goals. V1
requires validated matches and safe degradation, not a match for every item with
missing or conflicting identity data. The conservative no-badge behavior is
already required for ambiguous, missing, virtual, remote, and unsupported cases.

### Consequences

- Task 3.6 is a documentation-only decision closure. It adds no configuration,
  normalization code, matching rule, or runtime behavior.
- The existing absence of a `ConfiguredPath` rule is the correct V1 behavior;
  tests should continue to assert that it is absent.
- V1 does not expose path namespaces, host topology, or path-derived diagnostics
  through configuration or match state. No secret-boundary change is required.
- Items that could only be resolved by a path fallback remain unmatched in V1,
  which is safer than accepting a namespace-dependent or ambiguous match.
- A future path decision must be a new ADR. It must define the connection-scoped
  schema, both path namespaces, normalization and boundary rules, location
  eligibility, multi-source behavior, rule precedence, configuration snapshot
  replacement, and deterministic tests before any path identity is enabled.

### References

- `PLANS.md`, decision gate DG-5 and Milestone 3 task 3.6
- `docs/architecture.md`, sections 6, 7, 11, and 14
- `docs/data-model.md`, sections 3.1, 3.4, and 3.12
- `docs/research/media-metadata-mapping.md`, sections 3-5 and 8-11
- `docs/research/sonarr-api.md`, "Matching Jellyfin items to Sonarr"
- `docs/research/radarr-api.md`, "Identifying a Radarr movie from a Jellyfin item"

## ADR-009: V1 Badge Rendering Specification

**Status:** Accepted (the geometry and placement clauses are superseded by
ADR-019: badge position and size are now configurable. Fields, selector order,
colors, text limits, output format, scaling model, and failure behavior remain in
force.)

**Date:** 2026-09-18

### Context

ADR-006 limits V1 badge artwork to Movie and Episode posters. ADR-001 through
ADR-003 select persisted derived artwork, guarded source ownership, and
crash-recoverable publication, but do not define the visual contract for the
derived image. `BadgeMetadata` now contains provider-neutral observations with
explicit unknown states, while the renderer still needs a bounded and
deterministic field, layout, color, text, image, and failure policy.

The renderer must be useful on posters of different sizes, must not turn
missing provider data into a claim, and must not contain Sonarr- or Radarr-
specific branches. It must also produce one stable artwork representation for
the persisted-artwork path rather than a separate response-time variant for
each client request.

### Decision

ArrTags accepts the following complete V1 rendering specification.

#### Surface and source

- V1 rendering applies only to the `Primary` poster surface of eligible Movie
  and Episode items, with no image index. Series and Season are not rendered.
- The renderer uses the retained original source artifact selected by the
  publication pipeline. It never reads an Arr response, Jellyfin provider DTO,
  or an earlier ArrTags-derived image as its source.
- The output preserves the source pixel dimensions and aspect ratio. It never
  upscales a source image and ignores client-requested image sizes; Jellyfin's
  normal image pipeline may resize the persisted result after publication.
- A source that cannot be decoded, exceeds the accepted byte or decoded-
  dimension limits, or has unsupported image semantics produces `PassThrough`.

#### V1 fields and templates

The default field order is the priority order when space is limited:

1. Actual quality: the observed file quality label from `BadgeMetadata.quality`.
   A quality profile, cutoff target, or requested quality is never substituted.
2. Resolution: the normalized `BadgeMetadata.resolution.displayLabel`.
3. Dynamic range: Dolby Vision is displayed as `DV` when confirmed and replaces
   the generic dynamic-range value; otherwise the confirmed dynamic-range
   label is used. Unknown range is omitted.
4. Source: the normalized source label such as `WEB-DL`, `Blu-ray`, or
   `Remux`.
5. Video codec: the normalized `BadgeMetadata.videoCodec` value.
6. Audio: one composite badge built from confirmed audio feature names, then
   audio codec, then channel count. Features use the fixed order `Atmos`,
   `DTS-X`, `DTS-HD`, `DTS`; unknown audio features are omitted rather than
   inferred from codec or channel count.
7. Custom values: each retained `BadgeMetadata.customBadges` value is a
   separate candidate, in its canonical provider order.

`upgradePending` is a separate status badge, shown only when it is explicitly
`true`. It is pinned to the upper-right corner and does not displace the
technical badge rail. The status text is `UPGRADE`. A false or unknown value
produces no status badge.

The default templates display the normalized value without a field-name prefix;
the fixed status template is `UPGRADE`. A V1 `BadgeDefinition` may disable a
field or provide a bounded provider-neutral value template containing one
`{value}` placeholder. It may not reference provider DTO paths, record IDs,
quality profiles, credentials, or extension data. Template output is still
subject to the same text and layout limits. Extensions are ignored in V1.

Within the audio badge, known feature tokens take precedence over codec, and
codec takes precedence over channels. Multiple confirmed feature tokens are
ordered as above. Tokens are included as whole tokens, separated by `/`, until
the display budget is reached; a token that cannot fit is omitted. If no
feature, codec, or channel value is known, the audio badge is omitted.

The same fields, templates, and order apply to Movies and Episodes. V1 does
not add title, season, episode number, language, release group, custom-format
score, provider name, Arr IDs, or a Series/Season aggregate badge.

#### Layout and placement

- Technical badges form a bottom-left rail inside the poster safe area. They
  are single-line rounded pills, packed left-to-right in the priority order
  above.
- The rail has at most two rows and at most three pills per row. A pill that
  does not fit the remaining row is moved to the next row. Once both rows are
  full, lower-priority candidates are omitted. No pill is split across rows.
- The `UPGRADE` status pill is a single-line top-right pill in the same safe
  area. It is rendered independently of the rail and is never duplicated in
  the technical order.
- The default reference geometry is defined at a 1000 pixel poster width:
  24 pixel outer inset, 8 pixel pill gap, 8 pixel row gap, 48 pixel pill
  height, 8 pixel corner radius, 12 pixel horizontal padding, and 7 pixel
  vertical padding. Geometry scales uniformly with output width using
  `scale = clamp(width / 1000, 0.5, 4.0)`.
- The font is a bold or semibold sans-serif, single line, with no italics. The
  reference font size is 28 pixels and the status text is uppercase. The
  implementation must use one deterministic bundled font asset or an
  equivalently pinned host font; its identity is an output-affecting input.
- Badge geometry is measured after text normalization. If a candidate is too
  wide for the available rail, its text is shortened before the candidate is
  omitted. The renderer may reduce geometry only through the defined scale;
  it must not overlap, crop, or paint outside the safe area.

#### Colors and accessibility

The default palette is intentionally small and does not encode provider kind:

| Use | Background | Text |
| --- | --- | --- |
| Technical badge | `#111827` | `#FFFFFF` |
| Upgrade status | `#B45309` | `#FFFFFF` |

Badge backgrounds are fully opaque, so contrast does not depend on the poster
behind the pill. The text/background contrast must be at least 4.5:1 for every
configured style. Configurable colors are accepted only after this validation;
color alone must not communicate upgrade state because the status also says
`UPGRADE`. A 1 pixel solid border or shadow may be used only if it is included
in the renderer version and fingerprint; it is not required for contrast.

#### Text limits and truncation

- The final visible text of every pill, including template literals and the
  three characters in `...`, is at most 24 Unicode scalar values.
- Control characters are removed and runs of whitespace are collapsed to one
  space. Markup, line breaks, and provider-specific formatting are not
  accepted.
- Text longer than the limit is truncated at the end and receives `...`; the
  first 21 scalar values are retained. Truncation happens before width fitting.
- If the bounded text still cannot fit its pill at the scaled geometry, it is
  shortened further using the same end-truncation rule. A value that cannot
  produce a visible label is omitted. Lower-priority fields are never expanded
  to recover omitted text.
- The canonical custom-value limits remain 32 values and 128 characters per
  value. The renderer applies the stricter 24-scalar display limit and never
  displays more custom values than fit in the two-row rail.

#### Image format, transparency, and scaling

- V1 output is an 8-bit lossless PNG. An opaque source is emitted as RGB; a
  source with meaningful alpha is emitted as RGBA and its alpha is preserved.
- The badge pill backgrounds and text are opaque. Existing source
  transparency may remain transparent; the renderer does not add a transparent
  canvas or use semi-transparent badge backing for readability.
- Source metadata such as EXIF, embedded thumbnails, and nondeterministic
  timestamps is not copied to the output. Orientation is applied to pixels
  before layout. Encoder settings are fixed by the renderer version.
- All geometry is calculated in output pixels. There is no device-pixel-ratio,
  `@2x`, or DPI-metadata branch. A higher-resolution source receives the same
  layout scaled by its pixel width, and Jellyfin performs any later client-size
  resizing through its standard image path.
- The existing source, decoded-dimension, and derived-artifact operational
  limits apply. A render that would exceed the derived-artifact limit is
  rejected before publication.

#### Incomplete metadata and failure behavior

- A field is rendered only from a confirmed value in `BadgeMetadata`.
  `null`, unknown, absent, or unreported values are omitted. The renderer does
  not display `Unknown`, `N/A`, `False`, or an inferred negative.
- If `BadgeMetadata` is absent, the match is not eligible, no field is
  displayable, or the source is unavailable, the result is `PassThrough` and
  the current usable artwork is unchanged.
- If an allowed last-known-good metadata snapshot is supplied by the
  reconciliation pipeline, it is rendered as supplied; freshness and stale
  retention are not decided by the renderer. After that snapshot is no longer
  allowed, no replacement is rendered or published by this contract.
- Decode, layout, font, cancellation, resource-limit, and encode failures
  produce a bounded non-secret failure result and leave current artwork
  unchanged. Partial output is never published.

#### Provider-neutral consumption and determinism

The renderer accepts `RenderRequest` containing `MediaIdentity`, `MediaMatch`,
`BadgeMetadata`, an ordered `BadgeDefinition` snapshot, the retained source,
and the output policy. Field selection is through the provider-neutral selector
vocabulary in `BadgeDefinition`; provider kind is not a rendering branch.

The render/output fingerprint includes the source fingerprint, metadata
fingerprint, selected definition versions and values, palette, templates,
font asset identity, output format, source dimensions, scale policy, text
limits, renderer version, and badge schema version. Request correlation IDs,
timestamps, and diagnostics are excluded. Equal inputs produce the same
logical output; any output-affecting change requires a new fingerprint.

### Consequences

- V1 has a small, readable, provider-neutral badge vocabulary that emphasizes
  actual file observations and safely omits incomplete data.
- The two-row rail and fixed text bounds prevent a long custom value or a large
  technical label from covering the poster.
- PNG preserves source quality and optional alpha but can be larger than JPEG;
  the existing artifact limit and pass-through behavior bound that cost.
- Rendering is independent of client request size, device pixel ratio, and
  Jellyfin Web or Enhanced internals. The resulting persisted image can be
  consumed by all normal Jellyfin image clients.
- This ADR defines rendering only. Publication, caching, source capture,
  restoration, stale-artwork lifecycle, and Enhanced coexistence remain governed
  by ADR-001 through ADR-003 and their later gates.

### Rejected alternatives

#### JPEG as the V1 output

Rejected as the default because it is lossy, cannot preserve source alpha, and
adds quality-dependent encoder behavior to a persisted derived artifact.

#### WebP, SVG, or client-selected output formats

Rejected for V1. They would expand client and Jellyfin ABI validation and would
make one persisted representation depend on the requesting client. A later
format decision requires a new ADR.

#### Rendering `Unknown`, `N/A`, or inferred negatives

Rejected because missing provider data is explicitly distinct from a confirmed
negative in the canonical model.

#### Quality profile, custom-format score, or provider-specific labels as the
quality badge

Rejected because they describe requested policy or provider implementation
details rather than the actual observed file quality, and would violate the
provider-neutral renderer boundary.

#### Unlimited badges, unconstrained templates, or a single dense text block

Rejected because they reduce poster legibility, allow provider values to dominate
the image, and make bounded rendering and accessibility unreliable.

#### Request-time or device-pixel-ratio-specific rendering

Rejected because V1 publishes one persisted image through Jellyfin's supported
image APIs. Client-size variants belong to Jellyfin's normal image processing,
not to ArrTags rendering identity.

### References

- `PLANS.md`, DG-3 and Milestone 4
- `docs/architecture.md`, sections 4, 6, 9, 11, and 12
- `docs/data-model.md`, sections 3.5 through 3.8
- ADR-001: Persisted Derived Poster Artwork
- ADR-002: Guarded Artwork Ownership and Restoration
- ADR-003: Crash-Recoverable Artwork Publication
- ADR-006: V1 Badge Surfaces and Library Scope Identifier
- `docs/research/poster-rendering-strategies.md`, sections 4 and 6
- `docs/research/media-metadata-mapping.md`, sections 7 and 9

## ADR-010: V1 Renderer Implementation Contract

**Status:** Accepted (partially superseded by ADR-015: the SkiaSharp library
choice, bundled font, PNG/color handling, host/source boundary, configuration,
and testing strategy remain in force; the decisions that the plugin package must
carry the managed `SkiaSharp.dll` and native `libSkiaSharp.so` and must not load
a system Skia library are superseded by ADR-015; the renderer-configuration
clauses that keep geometry and placement code-owned are superseded by ADR-019)

**Date:** 2026-09-18

### Context

ADR-009 fixes the V1 visual behavior but intentionally leaves the renderer
implementation choices open. The implementation must run inside a Jellyfin 12
plugin on Linux Docker hosts, produce repeatable bounded output, avoid host-font
or global-Jellyfin-service coupling, and keep the provider-neutral
`BadgeMetadata` boundary intact.

The remaining choices are the raster library, bundled font, PNG/color handling,
the host-to-renderer boundary, configuration persistence, and test oracle. They
must be resolved without changing ADR-009's fields, layout, or output policy.

### Decision

ArrTags accepts the following V1 implementation contract.

#### Rendering library

V1 will use **SkiaSharp** directly through a plugin-owned renderer service. The
exact `SkiaSharp` managed package and matching Linux native asset package must
be pinned to exact versions in the dependency lock file and validated against
the declared Jellyfin 12 / `net10.0` target. The renderer uses SkiaSharp as a
private implementation detail; it does not replace or decorate Jellyfin's
global `IImageProcessor` or `IImageEncoder`.

SkiaSharp is selected because:

- Jellyfin 12's inspected drawing path and the inspected Jellyfin poster
  plugins already use Skia-based raster operations, making the rendering model
  and Linux behavior relevant to the target host.
- It provides direct raster, alpha, text, and PNG operations without spawning a
  process or depending on ImageMagick binaries.
- Its native implementation is suitable for bounded decode/draw/encode work
  and avoids introducing a second managed font-rasterization stack beside the
  one already represented in the Jellyfin ecosystem.
- The package and native runtime versions can be treated as explicit renderer
  identity inputs and tested as one compatibility unit.

The plugin package must carry the native assets required by every Linux
architecture it claims to support. It must not silently load an arbitrary
system Skia library. A missing or incompatible native asset is a bounded
renderer failure, not a reason to fall back to another raster path.

> **Partially superseded by [ADR-015](#adr-015-host-provided-skiasharp-runtime-for-the-renderer).**
> Task 7.3 found live that shipping the managed `SkiaSharp.dll` and native
> `libSkiaSharp.so` in the plugin package creates a fatal host/plugin type
> identity conflict, so the plugin no longer bundles the renderer runtime and
> instead resolves the host's SkiaSharp through the default load context. This
> paragraph's bundling requirement and the "must not load a system Skia library"
> reading no longer apply; the renderer library choice and the compile-time pin
> remain in force.

SkiaSharp's managed binding and Skia's applicable third-party notices must be
reviewed and shipped according to their licenses. This is an explicit release
input, not an assumption that the host's copy supplies the plugin's license
obligations.

#### Bundled font

V1 will bundle **DejaVu Sans Bold, version 2.37**, as the single text asset for
all technical and status pills. The font file is loaded from plugin-owned
resources by bytes, not resolved through the host's font directories. Its
SHA-256, file version, and license notice are part of the renderer identity.

DejaVu Sans Bold is selected because it is a mature Linux-compatible sans-serif
with broad Unicode coverage, a permissive DejaVu font license, and predictable
bold metrics suitable for the short single-line V1 labels. One asset avoids
fallback-font differences between Docker images and between development and
production hosts.

The renderer has no font fallback. A missing, corrupt, or unsupported bundled
font returns a bounded failure/pass-through result. The DejaVu font license and
notice must be included with the plugin distribution.

#### PNG, alpha, and color handling

The implementation shall produce the ADR-009 output as:

- Non-interlaced, 8-bit-per-channel PNG with RGB for fully opaque output and
  RGBA when the source has meaningful alpha.
- sRGB pixel values. An input without a color profile is treated as sRGB. A
  supported embedded profile is converted to sRGB before drawing; an invalid or
  unsupported profile fails closed rather than being guessed.
- Straight, unpremultiplied alpha in RGBA output. Source alpha is preserved
  outside badge pixels, while badge backing, text, and any configured border are
  fully opaque.
- A fixed sRGB declaration and no other retained source metadata. EXIF, ICC
  payloads, thumbnails, text chunks, timestamps, comments, and other ancillary
  metadata are stripped from the result.
- Fixed encoder settings, including compression/filter policy and no interlace,
  selected as part of the renderer version. No random, host-time, or process-
  specific bytes may enter the PNG.

Fully transparent pixels must use a canonical RGB value so hidden source bytes
cannot make equal visible images produce different output hashes. Orientation
must be resolved before layout, and the output dimensions are the oriented
source dimensions required by ADR-009.

The renderer validates source and derived byte limits and decoded dimensions
before accepting unbounded allocations. A size, color, alpha, decode, or encode
violation returns pass-through/failure and never returns partial PNG data.

#### Host/source boundary and renderer service contract

Jellyfin interaction belongs to a host-side source adapter, not to the renderer.
The adapter supplies a conceptual immutable `SourceImageInput` containing:

- A bounded read-only stream or artifact handle for the exact source bytes.
- The source content type, byte length, dimensions after orientation, and
  SHA-256 identity observed by the host boundary.
- No filesystem path, Jellyfin entity, provider DTO, network handle, credential,
  or mutable image object.

The adapter is responsible for obtaining the source through the supported host
image boundary and for preserving the exact bytes needed by the caller. The
renderer does not open paths, query Jellyfin, read provider services, or mutate
the input source.

The renderer exposes the following conceptual asynchronous contract:

```text
RenderAsync(RenderRequest request, CancellationToken cancellationToken)
    -> RenderResult
```

`RenderRequest` contains the validated `SourceImageInput`, `MediaIdentity`,
`MediaMatch`, optional `BadgeMetadata`, the immutable `BadgeDefinition` and
rendering-policy snapshot, and renderer/schema identities. It contains no
provider DTOs or secrets. The renderer revalidates bounded input, surface,
metadata, and configuration invariants before decode.

For a successful result, `RenderResult` contains a bounded PNG artifact or
read-only output handle, `image/png`, oriented output dimensions, output hash,
and the deterministic output fingerprint. For no usable badge input it returns
`PassThrough`; for malformed, cancelled, unsupported, or resource-exhausted
work it returns a bounded failure/pass-through result with a safe reason code.
The renderer has no external side effects and never modifies source bytes.

Cancellation is checked before decode, after decode, between layout stages, and
before output finalization. A cancellation or exception discards the partial
output and does not alter the caller's source artifact.

#### Renderer configuration persistence

User-adjustable renderer configuration is persisted as part of the immutable,
versioned plugin configuration snapshot:

- Enabled/disabled V1 selectors and their bounded provider-neutral `{value}`
  templates.
- Contrast-validated palette overrides, if exposed by the configuration
  surface, and the bounded V1 style/placement values allowed by ADR-009.
- Configuration schema/version and the resulting secret-free configuration
  fingerprint.

The V1 output format, color-space conversion, alpha model, font asset, font
identity, geometry limits, text limits, and renderer implementation version are
not user-selectable. They are code-owned renderer policy values and are still
included in the render fingerprint. Font bytes, source bytes, provider values,
credentials, and rendered images are never stored in plugin configuration.

Configuration replacement uses the existing immutable snapshot boundary:
invalid candidates are rejected, the last valid snapshot remains active, and
workers receive one captured configuration snapshot for a render attempt.

#### Renderer testing strategy

V1 uses layered tests with synthetic, repository-owned image fixtures so tests
do not depend on copyrighted poster artwork:

- Selector and layout unit tests cover field priority, audio token ordering,
  unknown-versus-empty values, two-row packing, omission, safe margins,
  truncation, status placement, and configuration validation.
- Golden-image tests cover an opaque JPEG-like source, an RGB PNG source, an
  RGBA source, orientation, every V1 field, long custom values, missing fields,
  full rail capacity, and the upgrade status. Goldens are decoded pixel planes,
  not only encoded PNG files, and include expected dimensions, alpha, sRGB
  values, and output fingerprints.
- Determinism tests render the same request repeatedly and after process
  restart. On the canonical pinned Linux runtime, decoded pixels, output hash,
  and PNG bytes must be identical. Request IDs, timestamps, and stream read
  chunking must not affect the result.
- PNG tests assert non-interlaced 8-bit RGB/RGBA output, stripped metadata,
  canonical transparent-pixel RGB values, fixed sRGB declaration, source-alpha
  preservation, and rejection of malformed/unsupported profiles.
- Failure and bounds tests cover oversized input/output, invalid dimensions,
  corrupt bytes, missing font/native assets, cancellation at each checkpoint,
  no displayable metadata, and draw/encode exceptions. Each case must leave
  the source unchanged and return a bounded safe result.
- Cross-runtime validation is pixel-based. Geometry, dimensions, alpha, solid
  badge colors, and text placement must match exactly. On an explicitly
  supported non-canonical Linux runtime, only anti-aliased text pixels may use
  a maximum per-channel difference of 1 and at most 0.1 percent differing
  pixels; any larger difference fails compatibility validation.
- Performance tests use representative poster dimensions and verify the
  configured render concurrency, cancellation responsiveness, and memory
  bounds without invoking Jellyfin or external providers.

Golden baselines are regenerated only when the renderer version, SkiaSharp
runtime, native asset, font hash, or an accepted visual decision changes. A
test must not auto-approve a changed golden image.

### Rejected alternatives

#### ImageSharp

Rejected for V1 despite its pure-managed deployment advantage. It would add a
separate image and font stack, its rendering behavior would not match the
Skia-based Jellyfin ecosystem, and its current Six Labors licensing model
requires project-specific commercial/use review rather than providing the
simple permissive dependency posture desired for this plugin. It can be
reconsidered only with a deliberate license and cross-runtime rendering review.

#### `System.Drawing.Common`

Rejected because it is not a supported cross-platform server-rendering choice
for Linux Docker deployment and can depend on unavailable native system
components.

#### ImageMagick or an external raster process

Rejected because it expands the container dependency and security surface,
requires process management, and makes bounded cancellation and deterministic
versioning harder than an in-process library.

#### Jellyfin's global image services

Rejected because using `IImageProcessor` or `IImageEncoder` as the renderer
would couple ArrTags to a global image hot path and affect unrelated Jellyfin
artwork. ADR-001 already rejects that extension strategy.

#### Host font discovery or a font fallback chain

Rejected because Docker image contents and operating-system font versions would
change glyph metrics and rasterization. The single bundled font is the stable
V1 input.

### Consequences

- V1 has one explicit in-process raster stack and one font asset across supported
  deployments, with native asset validation as part of compatibility testing.
- Linux Docker deployment is reproducible when the managed package, native
  asset, font hash, and runtime are pinned; unsupported runtime combinations
  fail safely rather than selecting an untested fallback.
- SkiaSharp's native dependency and applicable notices are a deliberate tradeoff
  for Jellyfin alignment, raster performance, and a smaller conceptual renderer
  surface than a second managed graphics stack.
- Pixel-level goldens and strict failure tests make visual changes reviewable
  without coupling renderer tests to Jellyfin or Arr services.
- No separate `docs/rendering-spec.md` is justified at this stage. ADR-009 is the
  product rendering specification and ADR-010 is the implementation contract;
  a separate document would duplicate both before Phase 4 code exists.

### References

- ADR-001: Persisted Derived Poster Artwork
- ADR-009: V1 Badge Rendering Specification
- `docs/architecture.md`, sections 4 and 9
- `docs/data-model.md`, sections 3.5 through 3.8
- `docs/research/poster-rendering-strategies.md`, sections 1 through 6
- Jellyfin 12 `SkiaEncoder`, `IImageProcessor`, and `IImageEncoder` findings
  cited by the rendering research

## ADR-011: Jellyfin Enhanced Coexistence Policy

**Status:** Accepted

**Date:** 2026-09-20

### Context

Decision gate DG-8 asks how ArrTags should behave when its server-rendered
badges coexist with Jellyfin Enhanced, which provides client-side quality tags
and a server-side Spoiler Guard. ArrTags already rejects any dependency on
Enhanced internals (ADR-001) and publishes its badges as persisted artwork
through Jellyfin's supported item-image APIs. ADR-009 fixes the badge content
and layout, and ADR-010 fixes the renderer implementation.

Two questions remained open: whether ArrTags should automatically detect,
suppress, or limit duplicate or overlapping badges, and whether a spoiler or
hidden image state should change ArrTags badge output. The repository owner
resolved DG-8 explicitly.

### Decision

ArrTags does **not** implement automatic duplicate-badge detection, overlap
suppression, or a dependency on Jellyfin Enhanced internals.

- Jellyfin Enhanced can choose where it draws its own client-side tags, so
  overlap handling is deferred to the user. ArrTags does not attempt to detect
  or avoid Enhanced's overlays.
- ArrTags badge output is controlled only by the existing ArrTags
  configuration: the poster enable flags (`BadgeMoviePosters` and
  `BadgeEpisodePosters`) and the renderer selector enablement
  (`RendererConfiguration.Selectors`). No new suppression knob, duplicate
  policy field, or Enhanced-internals coupling is introduced. A user who does
  not want overlapping presentation disables the relevant ArrTags poster
  surface or selector, or configures Enhanced.
- Jellyfin Enhanced's Spoiler Guard has no material effect on ArrTags badge
  display. ArrTags renders its derived badge normally and adds no special
  handling for a spoiler or hidden state. ArrTags never reads or reproduces
  Enhanced's filter ordering.
- ArrTags continues to publish one persisted badge image through Jellyfin's
  standard item-image route; Enhanced's client overlays and server filters
  remain independent of that route.

### Consequences

- No new configuration, runtime branch, or dependency is added for coexistence;
  the existing enable flags remain the user's control surface.
- ArrTags can never interfere with Enhanced by attempting to suppress or
  coordinate with it, and Enhanced cannot silently change ArrTags output.
- Jellyfin Web may still show both ArrTags server badges and Enhanced
  client-side quality tags; that duplicate presentation is a documented,
  user-managed outcome rather than an ArrTags detection problem.
- A hidden, blurred, or spoiler-protected client presentation is outside
  ArrTags' rendering contract; ArrTags output is the same regardless of that
  client state.
- Coexistence tests assert the absence of an Enhanced reference in the
  production assembly, the absence of a spoiler, hidden, or
  duplicate/overlap suppression branch in the renderer and publication surface,
  and that badge output varies only with the existing ArrTags configuration.
- This ADR resolves DG-8. It does not reopen ADR-001 through ADR-010 and does
  not decide a future explicit Enhanced integration, which remains post-V1.

### Rejected alternatives

#### Automatic duplicate/overlap detection or suppression

Rejected. ArrTags cannot reliably observe Enhanced's client-side overlay
placement or configuration, and guessing would risk suppressing a legitimate
badge. Overlap handling is the user's choice through existing ArrTags and
Enhanced configuration.

#### Depend on Jellyfin Enhanced internals

Rejected. Enhanced internals are not a supported integration surface (ADR-001);
coupling to them would break on Enhanced changes and would violate the plugin's
supported-API boundary.

#### Special spoiler or hidden handling

Rejected. Enhanced's Spoiler Guard is a server-side image filter with no
supported contract for ArrTags, and its ordering with the persisted-image route
is not an ArrTags concern. Adding spoiler-specific suppression would create an
unsupported behavioral coupling without improving badge correctness.

### References

- `PLANS.md`, decision gate DG-8, Milestone 5 task 5.10, and the Milestone 5
  acceptance criterion for Jellyfin Enhanced coexistence
- `docs/architecture.md`, section 10
- `GOALS.md`, "Jellyfin Enhanced compatibility" and success criterion 7
- ADR-001: Persisted Derived Poster Artwork
- ADR-009: V1 Badge Rendering Specification
- `docs/implementation-readiness.md`, Former Action Disposition item 10

## ADR-012: Inbound Arr Webhook Boundary

**Status:** Accepted

**Date:** 2026-09-21

### Context

Decision gate DG-7 requires a decision for webhook exposure, authentication,
payload limits, replay handling, and the route administration flow. Secret
persistence and versioned access are already resolved by ADR-005, which provides
the `SecretReference.WebhookAuthentication` slot, the `IPluginSecretResolver`
boundary, and the constant-time `SecretLease.Matches` comparison, but
deliberately does not authorize exposing a route or define request policy.

The provider research confirms the available transport. Sonarr and Radarr each
expose a first-class outbound Webhook connection that issues an HTTP `POST` or
`PUT` with a JSON body and configurable custom headers; Sonarr also supports
optional basic-auth credentials. Neither provider signs webhook payloads, so the
receiving endpoint must protect itself independently. The event names and
payload shapes are provider-specific and version-sensitive, and the research
explicitly requires treating webhooks as hints that accelerate a read-based
reconciliation rather than as a source of truth.

Jellyfin 12 discovers exported `ControllerBase` types in plugin assemblies as
independently routed API controllers, so a plugin can add a supported route
without replacing any host controller. ArrTags already has a bounded,
coalescing, single-flight `LibraryWorkHint`/`LibraryWorkQueue` path (tasks 6.1
and 6.2) that every trigger feeds, and a persisted metadata-state mapping (task
6.3) from a Jellyfin item to a connection-scoped provider record identity.

### Decision

ArrTags accepts the following V1 inbound webhook contract.

#### Route exposure and controller registration

ArrTags exposes one plugin MVC controller, `ArrTagsWebhookController`, derived
from `ControllerBase` and exported from the plugin assembly. Jellyfin's plugin
controller registration discovers and routes it; ArrTags adds no route outside
that supported mechanism and does not replace or intercept any host route.

- Sonarr deliveries: `POST /ArrTags/Webhook/Sonarr`.
- Radarr deliveries: `POST /ArrTags/Webhook/Radarr`.
- The controller is `[AllowAnonymous]` because the caller is a Sonarr or Radarr
  instance, not a Jellyfin user. It is not protected by Jellyfin's user
  authorization and must not rely on it; the shared secret header is the
  authentication boundary.
- The route identifies the provider family. A delivery for a provider whose
  connection is absent or disabled resolves to no work.

#### Authentication and constant-time comparison

- The request presents the shared secret in the `X-ArrTags-Webhook-Secret`
  header. The secret is never accepted in the URL or a query string, and it is
  never logged, echoed, or returned.
- The candidate is compared with the configured secret through the ADR-005
  webhook lease: the boundary reads the current public snapshot, checks
  `WebhookConfigured`, acquires
  `TryAcquire(SecretReference.WebhookAuthentication, snapshot.ConfigurationVersion)`,
  and calls `SecretLease.Matches`, which uses a constant-time comparison.
- The comparison is bounded. An empty candidate, a candidate longer than 1024
  characters, a missing or rotated configuration generation, or an absent
  secret all fail closed before any comparison proportional to the candidate.
- Every authentication failure returns the same bounded `401 Unauthorized` with
  no body. The response never reveals whether a secret is configured, whether
  the slot resolved, or why the candidate was rejected.
- Authentication runs before MVC model binding. A pre-binding authorization
  filter authenticates the request and fails closed before any request body is
  read or parsed, so the uniform `401` holds for every content type and no
  request body is processed before the shared secret is verified.

#### Payload bounds and tolerant parsing

- The request body is bounded by `OperationalLimits.WebhookMaxPayloadBytes`
  (default 256 KiB, validation range 4 KiB to 4 MiB), resolved from the current
  snapshot per request. A declared or streamed body above the bound is rejected
  with `413 Payload Too Large` before the whole body is buffered.
- The route accepts a JSON body: `application/json`, `text/json`, or a `+json`
  structured syntax suffix, with an absent content type tolerated for
  compatibility. A non-JSON content type is rejected with `400 Bad Request`
  before the body is read, so the configured bound remains the effective bound
  for every content type and the framework form/model-binding limits never
  govern the route.
- JSON parsing is tolerant of unknown fields and property-name casing and
  bounded by a maximum nesting depth and a maximum number of episode entries.
  Only the event type, the upgrade flag, and the provider-local record/file
  identifiers are read; the raw payload is never retained.
- An empty, malformed, truncated, or wrong-shaped payload, or a relevant event
  that advertises no usable provider record identifier, is rejected with
  `400 Bad Request`. No body or parser exception detail is returned.
- An unsupported or non-relevant event type (for example `Test`, `Grab`,
  `Health`, `ApplicationUpdate`, or `ManualInteractionRequired`) is
  acknowledged with `202 Accepted` and produces no work and no retained state.

#### Provider-record-to-Jellyfin resolution

- A webhook is a hint. The advertised provider record/file identifiers are used
  only to find the Jellyfin items ArrTags has already associated with that
  provider record in its persisted metadata-state mapping for the resolved
  connection. The payload is never trusted as current state and never grants
  permission to work on an arbitrary item.
- Resolution is bounded by `OperationalLimits.ReconciliationBatchSize`. It
  scans at most that many persisted metadata-state records and returns at most
  that many distinct Jellyfin item ids. A record that does not exist, an absent
  association, a provider-kind or connection mismatch, or a disabled connection
  produces no hint.
- Each resolved item is enqueued through the existing `IWorkHintSink` as a
  bounded `LibraryWorkHint` with the current safe configuration generation, so a
  webhook enters exactly the same deduplicated work path as library events,
  post-scan, and scheduled reconciliation. The worker re-reads the current
  Jellyfin item and Arr state and discards a changed or ineligible basis; the
  webhook never publishes metadata, mutates artwork, or calls an Arr endpoint.
- This bounded lookup is best-effort by design. A webhook for a record that
  ArrTags has not yet associated with a Jellyfin item produces no work; the
  authoritative library-event and periodic reconciliation remain responsible
  for new or missed items. A webhook can never widen the set of items beyond
  those already known and eligible.

#### Replay handling

- Duplicate, out-of-order, and replayed deliveries are handled by idempotent
  coalescing rather than a persistent nonce store: the intake suppresses a
  repeated provider record/file event within a short bounded window, and the
  `LibraryWorkQueue` coalesces the resolved work by item, connection, and
  surface. The worker re-reads current state, so repeating or reordering a
  delivery cannot duplicate or extend work.
- A replay after the short window is admitted and re-resolves, but still
  coalesces into the same deduplicated work and cannot produce a second
  publication because publication is fingerprint-gated and single-flight.

#### Rate limiting and coalescing

- The controller authenticates, bounds, parses, and performs a non-blocking
  bounded submit, then returns. It performs no resolution, provider read, or
  disk scan on the request thread and never blocks a Jellyfin request.
- A hosted `WebhookIntakeService` consumes a bounded in-memory intake queue off
  the request path, resolves at most one delivery at a time, and enqueues the
  bounded hints. The intake is bounded; overflow drops the new delivery rather
  than growing without bound or blocking. A burst therefore cannot create
  unbounded work, and any dropped delivery is repaired by the authoritative
  periodic reconciliation.
- A stopped intake and a stopped work queue reject new work so host shutdown
  stops accepting before the drain.

#### Work-scope guarantees

- Webhooks never publish metadata, never mutate or restore artwork, never call
  an Arr write endpoint, and never use a payload item identifier as permission.
- Webhooks do not change the queue, worker, publication, recovery, freshness, or
  regeneration mechanics; they are one more bounded producer on the existing
  hint boundary.

#### Route administration flow

- The administrator configures the shared secret in the existing
  `PluginConfiguration.WebhookSecret` slot. It is persisted by Jellyfin's
  supported plugin configuration mechanism and accessed only through the
  ADR-005 versioned secret boundary; ArrTags adds no second secret store and
  never displays the value in diagnostics.
- The administrator configures the Sonarr and/or Radarr Webhook connection with
  the fixed endpoint URL and adds the `X-ArrTags-Webhook-Secret` custom header
  carrying the same secret. The endpoint is a fixed, documented plugin route;
  no secret appears in the URL.
- If no secret is configured, every request fails closed with `401`. There is no
  unauthenticated mode, no default secret, and no auto-generated secret.

### Consequences

- DG-7 is resolved: route exposure, authentication, payload bounds, replay
  handling, rate limiting, and the administration flow are fixed for V1.
- The webhook boundary reuses the ADR-005 secret lease and the task 6.1/6.2
  bounded hint path, so no queue, worker, publication, or state model changes
  are required and no credential enters a queue item, cache, fingerprint, or
  diagnostic.
- A webhook can only accelerate work for items ArrTags already tracks. New or
  missed items continue to rely on library events and periodic reconciliation,
  which stays authoritative.
- The periodic/post-scan repair guarantee is bounded by the ADR-004 queue
  capacity: reconciliation enqueues the same bounded hints through the bounded,
  coalescing queue, and a run over a scope larger than `QueueCapacity` drops the
  tail and re-enumerates from the start of a deterministic order on the next run.
  Successive runs therefore overlap rather than advancing across a larger scope;
  making successive runs cover the whole scope remains an open limitation
  documented in `docs/limitations.md` (see `docs/architecture.md` section 8).
- The controller registration depends on Jellyfin's documented plugin
  controller discovery; the exact host routing behavior remains an
  implementation-time validation item.
- A future provider that signs payloads, or a change to the auth transport,
  requires a new decision; this ADR does not add HMAC or source-IP allowlisting.

### Rejected alternatives

#### Secret in the URL or query string

Rejected. URLs are routinely logged, cached, and persisted, and ADR-005 already
rejects query-string credentials for the provider API keys. The shared secret is
carried only in a request header.

#### HTTP basic auth as the only mechanism

Rejected as the primary contract. It is supported by both providers, but it
couples the secret to a username/password form, is more likely to be captured by
intermediaries, and does not add protection over a dedicated header. The header
is the single documented mechanism.

#### HMAC/signature verification

Rejected for V1. The inspected Sonarr and Radarr webhook implementations do not
sign payloads, so requiring a signature would reject every real delivery. A
future signed transport requires a new decision.

#### Source-IP allowlisting

Rejected as a V1 requirement. Reverse-proxy and container networking make the
observed source address unreliable, and the shared-secret boundary already
authenticates the request. It remains a deployment-level option.

#### Treating the payload as the source of truth

Rejected. Provider event names and payload shapes are version-sensitive and
unsigned; publishing or mutating from a payload would let an untrusted request
or a provider bug drive incorrect artwork. The worker always re-reads current
Jellyfin and Arr state.

#### Direct per-request reconciliation or full-library scan

Rejected. Resolving and scanning on the request thread, or scanning the whole
library per delivery, would let a burst block or amplify work. The boundary uses
a bounded intake, a bounded resolution scan, and the existing coalescing queue.

#### A persistent replay/nonce store

Rejected for V1. Work is idempotent because the worker re-reads current state
and the queue coalesces, so a durable nonce store would add authoritative state
and failure modes without changing the outcome. A short bounded in-memory
coalescing window plus the queue provides the required replay behavior.

#### No webhook route

Rejected. The architecture and Milestone 6 deliverable include authenticated Arr
webhook hints as a low-latency accelerator, and the provider research confirms
both providers support the transport.

### References

- `PLANS.md`, decision gate DG-7, Milestone 6 task 6.7, and the Milestone 6
  deliverable for authenticated Arr webhook hints
- `docs/architecture.md`, sections 4, 5, 6, 8, 11, 12, and 14
- `docs/data-model.md`, sections 3.3, 3.11, 4.11, and 7
- ADR-004: Foundation Operational Limits and Defaults
- ADR-005: Versioned Secret Access Boundary
- `docs/research/sonarr-api.md`, "Keeping badges current: webhook and SignalR options"
- `docs/research/radarr-api.md`, "Webhooks / events (keeping badges current)"
- `docs/research/jellyfin-12-architecture.md`, section 2
- `docs/implementation-readiness.md`, webhook readiness item

## ADR-013: Supported Sonarr/Radarr Release Ranges and Optional-Field Compatibility Policy

**Status:** Accepted

**Date:** 2026-09-21

### Context

Decision gate DG-9 requires the supported live Sonarr/Radarr release ranges and
the optional-field compatibility policy to be decided and recorded before the
testing/release milestone (`PLANS.md` gate table, "Required before Milestones 2
and 7"). Milestone 2 implemented the read-only `/api/v3` provider boundary and
its failure matrix, but the release ranges were never accepted as an explicit
support claim, and `docs/architecture.md` item 9 and
`docs/implementation-readiness.md` still list the initial provider-version matrix
as open.

The provider research pins the available surface. Sonarr's `/api/v3` API applies
to Sonarr v3 and v4, while a separate v5 API is in development and must not be
depended on (`docs/research/sonarr-api.md`). Radarr's `/api/v3` surface is shared
across Radarr v3, v4, v5 and v6, with `6.4.x` the current stable line at the time
of writing and the OpenAPI `info.version` still reporting `3.0.0`
(`docs/research/radarr-api.md`). Both integrations call `/api/v3` exclusively and
probe `appName` and record `version`, but the implementation applies no
version-number gate.

### Decision

ArrTags V1 supports the following live provider releases through the pinned
`/api/v3` API contract.

- Sonarr: 3.x and 4.x.
- Radarr: 3.x, 4.x, 5.x and 6.x.
- API contract: `/api/v3` only, using the `ArrProvider.V3ApiContract` constant.
- Excluded: Sonarr 2.x and earlier, the in-development Sonarr v5 API surface, and
  any provider that does not identify itself as `Sonarr` or `Radarr` through its
  `system/status` probe.

Compatibility is behavioural, not a version-number gate. The connection probe
requires the exact application identity (`appName`) and the `/api/v3` contract;
the observed `version` is recorded in the canonical `ArrProvider` identity for
diagnostics and capability reporting, and no minimum or maximum version number is
enforced. A provider that identifies correctly but cannot satisfy the required
field shape fails closed with `ArrProviderErrorCode.ProviderIncompatible` and an
`Incompatible` connection health, and ArrTags leaves the current usable artwork
unchanged.

Only identity and quality-critical fields are mandatory. Every absent, null, or
unrecognized optional technical field maps to ArrTags' explicit unknown-value
state instead of a guessed or default value; no badge, fingerprint, or
publication decision depends on an optional field being present. A malformed or
missing required field is an incompatibility, never a silent default.

The declared ranges and the optional-field policy are documented in the release
artifact, the README/config UI, and `docs/architecture.md`, and the supported
ranges are exercised by provider contract fixtures across the declared lines in
the testing/release milestone.

### Consequences

- V1 makes a bounded, explicit support claim instead of an implicit "any v3"
  claim, so an operator on Sonarr v5 or Radarr v2 receives an incompatibility
  rather than undefined behavior.
- Because there is no numeric gate, a newer provider release that keeps the
  `/api/v3` contract and required field shapes keeps working; when it does not,
  the failure is classified as `ProviderIncompatible` and degrades safely.
- Missing optional fields remain explicit unknowns; the badge set does not change
  on an optional-field difference alone, preserving fingerprint stability.
- Provider contract fixtures become the evidence for the declared lines and must
  cover both a fully populated and a sparse/optional-missing payload.

### Rejected alternatives

- A numeric minimum/maximum version gate was rejected: the API contract, not the
  marketing version, is the compatibility boundary, and a numeric gate would
  reject compatible newer builds.
- Claiming Sonarr v5 support was rejected: its API is a separate in-development
  surface that the provider research explicitly excludes.
- Assuming a default for an absent optional field was rejected: it would fabricate
  technical metadata and destabilize fingerprints.

### References

- `PLANS.md`, decision gate DG-9, Phase 7 tasks 7.1, 7.5 and 7.6
- `docs/research/sonarr-api.md`, "Scope, versions, and evidence" and "Connection
  probe and version gate"
- `docs/research/radarr-api.md`, "Baseline versions" and "Capability/version
  probes"
- `docs/architecture.md`, section 4 and the remaining-decision item 9
- `docs/implementation-readiness.md`, provider-version-matrix item
- ADR-004: Foundation Operational Limits and Defaults

## ADR-014: Plugin State Root Outside Jellyfin's Plugins Directory

**Status:** Accepted

**Date:** 2026-09-21

### Context

Task 7.2 reproduced a release-blocking defect live on the pinned Jellyfin
`12.0.0` host. Jellyfin's `BasePlugin<T>` constructor derives a plugin's data
folder as
`Path.Combine(ApplicationPaths.PluginsPath, Path.GetFileNameWithoutExtension(assembly.Location))`
(`MediaBrowser.Common/Plugins/BasePluginOfT.cs`). Its `"_<version>"` branch is
dead because `Version` is still `null` at that point, so an ArrTags plugin
always derives `PluginsPath/ArrTags`. The supported `InstallationManager`
installs a repository package to `PluginsPath/ArrTags_<version>`.

ArrTags persists its state at `plugin.DataFolderPath`
(`ArrTagsServiceRegistrator.CreateStateRepository`), so the first real state
write creates `PluginsPath/ArrTags`. On the next host restart
`PluginManager.DiscoverPlugins` enumerates every directory under `PluginsPath`,
groups entries by manifest name, and deletes all but one of the same-named
entries. The data folder has no `meta.json`, so it receives an auto-manifest
named `ArrTags` with the `MD5("ArrTags")` GUID, which sorts after the real plugin
GUID; the versioned install folder is therefore deleted and no ArrTags plugin
loads. The unversioned layout (`PluginsPath/ArrTags` as both install and data
folder) and the no-state case are unaffected, so the defect is specific to the
standard versioned install layout once state exists.

The Phase 5 uninstall design assumed the host deletes the plugin data folder
after `Plugin.OnUninstalling()` returns. Inspection of the pinned host source
shows that `InstallationManager.UninstallPlugin` calls `OnUninstalling()` and
then `PluginManager.RemovePlugin`, which deletes only `plugin.Path` (the install
folder), never `DataFolderPath`. The assumption held only for the unversioned
layout where the install folder and data folder were the same directory.

### Decision

The `Plugin` constructor re-points its data folder using the public
`BasePlugin.SetAttributes(assemblyFilePath, dataFolderPath, version)` method
(the `IPluginAssembly` contract the host loader itself uses). The state root is:

```
Path.Combine(IApplicationPaths.ProgramDataPath, "ArrTags")
```

- It is directly under the host program data directory and is a sibling of the
  `plugins/` directory, never inside `PluginsPath`. It therefore cannot be
  enumerated by `PluginManager` discovery as a plugin folder and cannot collide
  with the versioned install folder.
- `ProgramDataPath` was chosen over `DataPath`: in Jellyfin 12 `DataPath` is
  `ProgramDataPath/data`, a server-managed area that already holds host-owned
  `trickplay` and `backups` data, while a plugin-namespaced sibling of `plugins/`
  is unambiguously plugin-owned and stable across versioned install-folder
  replacement.
- Only supported `MediaBrowser.Common` contracts are used
  (`BasePlugin`/`IPluginAssembly`/`IApplicationPaths`); no Jellyfin internals or
  concrete implementation types are referenced.
- `AssemblyFilePath` and `Version` are passed through unchanged, so
  `CanUninstall`, configuration-file naming, and version reporting are preserved.

Because the pinned host does not delete a relocated `DataFolderPath` on
uninstall, the plugin preserves the previous cleanup semantics itself:
`Plugin.OnUninstalling` runs the bounded synchronous uninstall drain and, only
when the drain result is complete (every operation terminal and every owned
surface restored), deletes its own state root. Cleanup is bounded and never
throws into the host. When the drain is incomplete or cancelled, the state root
and its recovery records are retained so the unresolved artwork can still be
reconciled, matching the Phase 5 fail-closed rule.

### Consequences

- V1 has no released installs, so no state migration is required. A pre-release
  developer install may still have an empty or stale `PluginsPath/ArrTags` data
  folder; that folder is orphaned by this change and, while present, would still
  trigger the discovery collision. Operators of unreleased builds must delete
  `PluginsPath/ArrTags` once. Released V1 installs never create it.
- Plugin state now survives plugin upgrades, because it is no longer inside the
  versioned install folder that the host replaces on upgrade.
- A completed uninstall removes the plugin state root explicitly; an incomplete
  uninstall leaves orphaned state on disk (the plugin is gone and cannot
  reconcile it) until an operator removes it or a reinstall reconciles it. This
  is the same fail-closed retention the design already accepted for unresolved
  restorations.
- Manual removal of the install folder (as opposed to a host uninstall API call)
  does not invoke `OnUninstalling`, so it leaves the state root in place; this is
  a test-fixture detail, not a host uninstall path.
- The state root is outside `PluginsPath` and therefore also outside the folder
  the host deletes when a plugin is uninstalled or upgraded, so all persisted
  authoritative artwork recovery records, retained source artifacts, and
  lifecycle fences remain under plugin control across restarts.

### Rejected alternatives

- A data folder under `PluginsPath` such as `PluginsPath/ArrTags-data` was
  rejected: it is still enumerated by `PluginManager` discovery, so it would
  become an auto-manifested plugin folder (with a `*.*`-matching name) and be
  subject to same-name grouping and deletion.
- The unversioned install layout (`PluginsPath/ArrTags` as both install and data
  folder) was rejected: it removes the supported versioned upgrade behavior and
  loses all provenance when the install folder is replaced or deleted.
- Relying on an upstream Jellyfin fix to the dead versioned-data-folder branch
  was rejected for V1: it is outside the plugin's control and would not protect
  released installs on the pinned `12.0.0` host.
- Keeping the Jellyfin-derived folder and simply documenting the collision was
  rejected: it makes the standard repository install flow delete the plugin.

### References

- `PLANS.md`, Phase 7 task 7.7 and the task 7.2 release blocker
- `docs/implementation/7.2/worker-report.json` and
  `docs/implementation/7.2/reviewer-report.json` (finding 7.2-F1)
- Pinned Jellyfin source revision `6c073e19ddf604b2369c638716164fdab4c952dc`:
  `MediaBrowser.Common/Plugins/BasePlugin.cs` (`SetAttributes`),
  `MediaBrowser.Common/Plugins/BasePluginOfT.cs`,
  `Emby.Server.Implementations/Plugins/PluginManager.cs`
  (`DiscoverPlugins`, `DeletePlugin`),
  `Emby.Server.Implementations/Updates/InstallationManager.cs`
  (`UninstallPlugin`), `Emby.Server.Implementations/AppBase/BaseApplicationPaths.cs`
- `src/ArrTags/Plugin.cs`, `src/ArrTags/PluginLifecycle/ArrTagsServiceRegistrator.cs`
- `docs/architecture.md`, sections 4, 5, and 6
- ADR-003: Crash-Consistent Publication Recovery

## ADR-015: Host-Provided SkiaSharp Runtime for the Renderer

**Status:** Accepted (supersedes the renderer-bundling parts of ADR-010)

**Date:** 2026-09-22

### Context

ADR-010 fixed the V1 renderer as a plugin-owned direct SkiaSharp service and
required the plugin package to carry the managed `SkiaSharp.dll` and the matching
Linux native `libSkiaSharp.so` at the plugin folder root, and to avoid loading an
arbitrary system Skia library. That requirement rested on the task 4.8 spike
(`docs/research/skia-host-compatibility.md`), which measured that a plugin that
ships its own managed SkiaSharp gets that copy loaded into the plugin load
context, that its native library is found only when it sits next to the managed
assembly in the plugin folder root, and that the host's native asset was
byte-for-byte identical to the pinned NuGet asset on the spike host.

Task 7.3 exercised the committed package live on the pinned Jellyfin `12.0.0`
musl host and found release blocker 7.3-F1: publishing the first badge through
Jellyfin's supported `IProviderManager.SaveImage` path aborts the whole Jellyfin
process with

```text
System.InvalidCastException: [A]SkiaSharp.UserDataDelegate cannot be cast to
[B]SkiaSharp.UserDataDelegate
```

where A is the host default-context `SkiaSharp.dll` and B is the plugin-context
copy loaded from the plugin folder. Jellyfin's own image processor and the
plugin both hold SkiaSharp types, and two managed SkiaSharp assemblies in
different `AssemblyLoadContext`s give those types two distinct identities, so a
delegate created by one copy cannot be consumed by the other. The failure is
deterministic, occurs about 8 ms after the artwork-operation record is written,
does not occur with both providers disabled or with the plugin uninstalled, and
disappears when the two bundled files are removed from the installed plugin
folder so the plugin shares the host's SkiaSharp. The managed host and plugin
`SkiaSharp.dll` are byte-identical, so this is purely a duplicate-management
problem, not a renderer or publication defect.

Task 7.3's independent review also corrected a task 4.8 research claim (reviewer
finding F2): on the pinned musl host the host's native `libSkiaSharp.so` is
18,453,464 bytes / SHA-256 `59039b25...`, which is byte-identical to the NuGet
`SkiaSharp.NativeAssets.Linux` `3.119.4` **`runtimes/linux-musl-x64/native/`**
asset (and to the file shipped inside the pinned Jellyfin distribution), and
differs from the **`runtimes/linux-x64/native/`** asset (11,170,296 bytes /
SHA-256 `66c856ea...`) that the spike measured as byte-identical on its glibc
host. Native byte-identity therefore holds for the matching RID, not across
RIDs. The managed `SkiaSharp.dll` is byte-identical
(SHA-256 `aaaaa18c68ba1f3a3408b00dff28b11d5705198e17ba9d3aa59222bfd35407c8`) on
both hosts. Any fix must therefore use the host's own native library rather than
assume it equals the packaged one.

### Decision

ArrTags stops bundling the renderer runtime and shares the host's SkiaSharp.

- `src/ArrTags/ArrTags.csproj` keeps exact `SkiaSharp` and
  `SkiaSharp.NativeAssets.Linux` `3.119.4` references for compile-time identity
  only, both with `<ExcludeAssets>runtime</ExcludeAssets>`, matching the existing
  `Jellyfin.Controller`/`Jellyfin.Model` pattern. The plugin compiles against the
  same managed SkiaSharp the Jellyfin `12.0.0` host uses, and
  `SkiaSharp.NativeAssets.Linux` pins the expected native ABI in the dependency
  graph without shipping a native library. Precisely, `ExcludeAssets=runtime`
  keeps the runtime assets out of the build output and the package, but it does
  not remove the `SkiaSharp.NativeAssets.Linux` `runtimeTargets` entries for the
  Linux RIDs that remain in `ArrTags.deps.json`; those native files are not
  staged into the package, and at runtime the plugin uses the host's own native
  library (task 7.8 reviewer finding 7.8-R3).
- The plugin package no longer carries `SkiaSharp.dll` or `libSkiaSharp.so`.
  The `PackagePlugin` target ships only `ArrTags.dll`, `ArrTags.deps.json`,
  `build.yaml`, `THIRD-PARTY-NOTICES.md`, and the `licenses/` notices, no longer
  resolves or copies the renderer runtime assets, and no longer fails when they
  are absent. `build.yaml` `artifacts` lists only `ArrTags.dll` and
  `ArrTags.deps.json`.
- At runtime the plugin resolves the host's managed SkiaSharp through the default
  load context: Jellyfin's `PluginLoadContext` (constructed from the plugin
  folder) resolves nothing from the folder, so the runtime falls back to the
  host's `jellyfin.deps.json` assembly. The host's native `libSkiaSharp.so` and
  its `libfontconfig.so.1` dependency are the ones used.
- V1 remains validated only on the pinned Jellyfin `12.0.0` `linux-musl-x64`
  host (the plugin does not separately distinguish musl from glibc; the pinned
  host's `jellyfin.deps.json` runtime target is
  `.NETCoreApp,Version=v10.0/linux-musl-x64` - task 7.8 reviewer finding
  7.8-R6). The plugin makes no RID-specific runtime claim; the host it runs on
  must provide SkiaSharp and its native dependencies.
- The test project (`tests/ArrTags.Tests/ArrTags.Tests.csproj`) references
  `SkiaSharp` and `SkiaSharp.NativeAssets.Linux` `3.119.4` directly so the
  golden, native, and load-context tests still exercise the real pinned renderer
  runtime in the test environment. That reference is never part of the plugin
  package.

### Consequences

- No plugin-context copy of SkiaSharp exists, so the host and plugin share one
  managed type identity and the task 7.3-F1 `InvalidCastException` cannot occur.
- The plugin no longer redistributes SkiaSharp, so it also no longer owns the
  native library's system dependency (`libfontconfig.so.1`) provisioning; the
  host image must provide it, as the pinned host already does.
- The plugin's SkiaSharp version must stay compatible with the host's. The V1
  contract is the pin to the exact `3.119.4` host version; a host on a different
  incompatible SkiaSharp is a bounded renderer failure, not a bundling fallback.
- `THIRD-PARTY-NOTICES.md` and the `licenses/` notices continue to ship for the
  bundled DejaVu font and for the compile-time SkiaSharp reference; the plugin
  does not redistribute the SkiaSharp binaries.
- The `PackagePlugin` target can no longer fail on a missing renderer asset, and
  a regression test asserts the produced package contains neither
  `SkiaSharp.dll` nor `libSkiaSharp.so`.
- `docs/research/skia-host-compatibility.md` section 3 is evidence for the
  superseded bundling constraint and is retained as historical research with a
  superseded marker; its section 2 native byte-identity claim is corrected for
  the pinned musl host.

### Rejected alternatives

#### Keep bundling the managed SkiaSharp and native library

Rejected. It is the confirmed fatal Jellyfin-affecting defect (7.3-F1); the two
managed copies cannot share type identity.

#### Bundle the managed assembly only

Rejected. A plugin-context managed SkiaSharp with no same-directory native
library produces a `DllNotFoundException` (task 4.8 scenario B-x64/B-runtimes),
and leaving the host's native library mixed with a plugin-context managed copy
does not restore shared type identity.

#### Bundle the native library only, resolve the managed assembly from the host

Rejected. The managed assembly identity would be shared, but loading a second
native `libSkiaSharp.so` with the same SONAME in one process risks native symbol
and lifecycle conflicts and does not follow the host's own library; the task 7.3
diagnostic that removed both files is the behavior that works.

#### Load the host's SkiaSharp through an explicit path or custom load context

Rejected for V1. It would depend on host-private paths and duplicate the host's
dependency resolution instead of using the documented default-context fallback
that a plugin with no bundled copy already gets.

#### Drop `SkiaSharp.NativeAssets.Linux` entirely

Rejected. Keeping it compile-time-only records the exact native ABI the plugin
expects and keeps the dependency graph stable without shipping an asset; dropping
it removes that pin without changing runtime behavior.

### References

- `PLANS.md`, Phase 7 task 7.8 (task 7.3 finding 7.3-F1)
- `docs/implementation/7.3/worker-report.json` and
  `docs/implementation/7.3/reviewer-report.json` (findings 7.3-F1 and F2)
- `docs/research/skia-host-compatibility.md`, sections 2 and 3 (superseded
  packaging constraint; corrected native byte-identity claim)
- `src/ArrTags/ArrTags.csproj`, `build.yaml`,
  `tests/ArrTags.Tests/PluginPackagingTests.cs`
- `docs/architecture.md`, section 9
- ADR-010: V1 Renderer Implementation Contract

## ADR-016: Dashboard Settings UI and Runtime Configuration Activation

**Status:** Accepted (v1.1)

**Date:** 2026-09-23

### Context

V1 has no web configuration UI. `README.md` states this explicitly, and
`docs/limitations.md` F2 records the consequence: the plugin reads
`plugins/configurations/ArrTags.xml` only at startup, so a saved webhook secret,
provider enable/disable, badge/selector change, or limit change is not observed
until the process restarts. `ConfigurationSnapshotService.TryReplace` already
implements validated, last-valid-retaining replacement, but it is not connected
to Jellyfin's configuration-save path.

The supported Jellyfin 12 contracts are confirmed in
`docs/research/jellyfin-12-architecture.md` section 9 and
`docs/research/jellyfin-expert/jellyfin-12-config-pages-and-logging.json`:

- A plugin exposes a dashboard page by implementing
  `MediaBrowser.Model.Plugins.IHasWebPages.GetPages()` and returning
  `PluginPageInfo` values. `Jellyfin.Api.Controllers.DashboardController` serves
  `web/ConfigurationPages` and `web/ConfigurationPage?name=` from the page's
  embedded assembly resource; the server injects nothing.
- Configuration is read and written through
  `Jellyfin.Api.Controllers.PluginsController` `GET`/`POST {pluginId}/Configuration`,
  which is class-level `[Authorize(Policy = Policies.RequiresElevation)]` (an
  authenticated administrator). The POST deserializes to the plugin's
  `ConfigurationType` using `JsonDefaults.Options` (PascalCase) and calls
  `IHasPluginConfiguration.UpdateConfiguration`.
- `BasePlugin<TConfigurationType>.UpdateConfiguration` is `virtual`: it assigns
  `Configuration`, calls `SaveConfiguration` (XML to the existing
  `plugins/configurations/ArrTags.xml`), and raises `ConfigurationChanged`. It
  does **not** refresh any plugin runtime state.

In the pinned source the static page-resource action carries no `[Authorize]`
and there is no fallback authorization policy, so the page resource itself is
reachable without authentication. It is static HTML/JS only and reflects no data;
all configuration data endpoints are administrator-gated. The jellyfin-web client
contract used by in-tree pages (`ApiClient`, `Dashboard`,
`Dashboard.processPluginConfigurationUpdateResult`, `data-require`, `pageshow`)
is public but potentially unstable and is not pinned by the server repository.

One behavior is not yet verified in this repository: whether `System.Text.Json`
populates ArrTags' get-only `Collection<T>` configuration properties
(`PluginConfiguration.EnabledLibraries`, `RendererConfiguration.Selectors`) on
the POST round-trip.

### Decision

ArrTags adds a dashboard settings page and wires runtime configuration
activation.

1. The `Plugin` entry point implements `IHasWebPages` and returns one
   `PluginPageInfo` (name `ArrTags`, `EnableInMainMenu = false`) whose
   `EmbeddedResourcePath` is an embedded `Configuration/config.html` with the
   explicit logical name `ArrTags.Configuration.config.html`, following the
   in-tree Jellyfin page pattern.
2. The page is read/write for the user-adjustable settings only: provider
   connections and their secrets, the webhook secret, poster/library scope,
   renderer selectors/templates/palette, the v1.1 allowlist/placement/verbosity
   settings, and the operational limits. The page embeds no secret and exposes no
   secret value beyond what Jellyfin's existing administrator configuration API
   already returns.
3. Configuration is saved only through the supported elevation-gated
   `PluginsController` path. ArrTags adds no custom configuration-save route.
4. `Plugin` overrides `BasePlugin<T>.UpdateConfiguration`. The override validates
   the candidate **before** the base implementation persists anything, using the
   same `PluginConfigurationValidator` the snapshot service uses. An invalid
   candidate is rejected without calling `base.UpdateConfiguration`, so it is
   never persisted and the last valid snapshot and private secrets remain active;
   a valid candidate is then persisted by `base.UpdateConfiguration(configuration)`
   and activated by `ConfigurationSnapshotService.TryReplace(...)`. The whole
   validate/persist/activate sequence is serialized, so concurrent
   elevation-gated saves cannot leave the running snapshot, `Plugin.Configuration`,
   and the persisted file divergent (security finding SEC-9.3-01). The override
   never throws into the host. Surfacing is
   defined by ADR-021: a rejected candidate writes a bounded, secret-free
   administrator-visible activity-log entry. Clause 4 does not require an inline
   page message or an HTTP error, and adds no custom save route.
5. Runtime activation has two parts:
   - **Live snapshot replacement.** Services resolve limits, provider/render
     concurrency, freshness, retention, badge definitions, and the renderer
     output policy from the current snapshot per operation, so a replaced
     snapshot takes effect without a host restart.
   - **Bounded re-render trigger.** A successful replacement also enqueues a
     bounded, non-blocking reconciliation through the existing work-hint /
     reconciliation boundary, so saved settings re-render existing posters
     promptly. The trigger is a bounded enqueue, never a synchronous
     full-library scan, and it never throws into the host or blocks the save
     response. It is required because a work item whose `ConfigurationVersion`
     is older than the current snapshot is skipped
     (`ArtworkPublishingWorkItemProcessor`), so without a trigger existing
     posters would not update until the next library event, webhook, post-scan,
     or scheduled run.
   Together these resolve limitation F2.
6. The anonymous static page-resource endpoint is accepted as a non-data surface;
   no secret or item data is placed in the page.
7. The following are required before this ADR's implementation is complete:
   - **Blocking prerequisite:** a test proving the POST round-trip populates the
     get-only `Collection<T>` configuration properties. If it does not, the
     configuration shape must change (for example settable collection
     properties) before the settings page is built, so a save cannot silently
     drop `EnabledLibraries` or `Renderer.Selectors`.
   - An explicit decision or live-host confirmation of the page-resource
     authorization behavior on the pinned host.

### Consequences

- Limitation F2 is resolved: a saved configuration change is observed without a
  restart, and a successful save triggers a bounded reconciliation so existing
  posters re-render with the new settings rather than waiting for the next
  scheduled run.
- Operators configure ArrTags from the dashboard instead of editing XML.
- The plugin now depends on a public-but-unstable web-client contract; a future
  jellyfin-web change may require the page to be updated.
- The page and the `UpdateConfiguration` override become a new security-relevant
  surface: the page must remain secret-free and the save path must remain the
  administrator-gated supported one.
- Configuration validation failures must be surfaced safely without exposing
  secret values.

### Rejected alternatives

- A plugin-owned custom configuration-save route was rejected: it would bypass
  the elevation boundary and is an unsupported workaround.
- A read-only display page was rejected: it would not resolve F2.
- Relying on `ConfigurationChanged` alone (without overriding
  `UpdateConfiguration`) was rejected as the primary mechanism because the
  override makes activation explicit and testable; the event remains an
  acceptable alternative.
- Keeping the XML-only, restart-required workflow was rejected: it is the
  documented limitation this ADR exists to remove.

### References

- `docs/research/jellyfin-12-architecture.md`, section 9
- `docs/research/jellyfin-expert/jellyfin-12-config-pages-and-logging.json`
- `docs/limitations.md` F2
- `docs/planning/v1.1.md` (Goal A, DG-10)
- `src/ArrTags/Plugin.cs`,
  `src/ArrTags/Configuration/ConfigurationSnapshotService.cs`
- `docs/architecture.md`, section 6
- ADR-004 (operational limits), ADR-005 (secret boundary)
- ADR-021 (administrator-visible configuration-rejection surfacing)

## ADR-017: Badge Value Allowlist

**Status:** Accepted (v1.1)

**Date:** 2026-09-23

### Context

ADR-009 fixes the V1 selector vocabulary and resolution order, and ADR-010 /
task 4.10 expose per-selector enablement and one bounded `{value}` template. A
selector can be enabled or disabled as a whole, but there is no way to suppress
specific values — for example, to render a dynamic-range badge only for
HDR-family values and not `SDR`, or to show only selected quality labels.
Operators must currently disable the whole selector to suppress one value.

Canonical `BadgeMetadata` already distinguishes confirmed values from
unknown/absent values (task 2.6), and `BadgeSelectorResolver` /
`BadgeDefinitionResolver` omit unknown values. The allowlist is a policy filter
over confirmed values, not a change to unknown-value semantics.

Decision gate DG-11 required the allowlist scope, matching/normalization, bound,
and unknown/custom-value interaction.

### Decision

ArrTags adds a per-selector value allowlist.

1. **Scope: per selector.** `BadgeSelectorConfiguration` gains a bounded
   `AllowedValues` string list. Each selector filters independently. An empty
   list means no restriction (every confirmed value passes); the setting is a
   whitelist, not a blocklist. The resolved `BadgeDefinition` snapshot gains the
   same resolved allowlist (for example an `AllowedValues` property), and
   `RendererConfigurationResolver.ResolveDefinitions` maps the persisted value
   into it, because the renderer only ever receives
   `IReadOnlyList<BadgeDefinition>`.
2. **Match: case-insensitive ordinal exact match against the resolved
   pre-template value.** The comparison value is the canonical display text
   produced by `BadgeSelectorResolver` before the definition template is applied,
   with surrounding whitespace trimmed. No substring, wildcard, prefix, or
   regular-expression matching is supported in v1.1.
   - For `CustomBadge`, the allowlist is applied to each retained custom value
     independently.
   - For `Audio`, the resolved value is the composite (features, then codec, then
     channel count); the allowlist matches the full composite string.
   - For `UpgradePending`, the only possible value is the fixed status text;
     allowlisting it is equivalent to enabling the selector and is permitted for
     uniformity.
3. **Order.** Value resolution → allowlist filter → definition template → text
   normalization/truncation → layout. An allowlisted value that cannot fit still
   follows the existing shorten/omit behavior.
4. **Unknown and absent values.** Unchanged: they are omitted and never
   inferred. The allowlist only removes already-confirmed values and never widens
   an omission.
5. **Bounds and validation.** At most 32 entries per selector; each entry at most
   64 characters; entries are trimmed, control characters are rejected, blank
   entries are rejected, and duplicates (after case-insensitive comparison) are
   rejected. Validation runs in `RendererConfiguration.Validate` with secret-free
   messages.
6. **Output-affecting identity.** The resolved allowlists are included in
   `RendererConfigurationFingerprint`. Because the renderer-configuration schema
   and rendering behavior change, `RendererConfiguration.CurrentSchemaVersion`
   advances from 1 to 2 and `RenderVersion.CurrentRendererVersion` advances from
   2 to 3; committed goldens are regenerated.
7. **No provider coupling.** The allowlist is provider-neutral and matches only
   canonical resolved values; it never references a provider DTO path, record
   identifier, quality profile, credential, or extension value.

### Consequences

- Operators can suppress individual values (for example `SDR`) without disabling
  the whole selector.
- The allowlist is bounded, validated, and secret-free, and it cannot widen an
  omission.
- Changing the allowlist is output-affecting and republishes affected artwork,
  exactly like a selector or palette change.
- Whole-composite matching for the audio selector is coarse and is documented;
  token-level audio filtering is a possible future refinement.

### Rejected alternatives

- A global single allowlist was rejected: the same string means different things
  across selectors and could suppress unrelated badges.
- Substring, wildcard, prefix, or regular-expression matching was rejected: it is
  less predictable and can re-include values unexpectedly.
- A blocklist/denylist was rejected: it was not requested, and a whitelist with
  an empty default is simpler and safer.
- Per-item or per-library allowlists were rejected as out of v1.1 scope; v1.1 is
  one global renderer policy.

### References

- `docs/planning/v1.1.md` (Goal B, DG-11)
- `docs/data-model.md`, sections 3.6 and 3.12
- `src/ArrTags/Configuration/BadgeSelectorConfiguration.cs`,
  `RendererConfiguration.cs`, `RendererConfigurationFingerprint.cs`
- `src/ArrTags/Rendering/BadgeSelectorResolver.cs`, `BadgeDefinitionResolver.cs`
- ADR-009 (selector vocabulary and order), ADR-010 (renderer configuration)

## ADR-018: Provider Inventory Cache and Library-Refresh-Driven Reconciliation

**Status:** Accepted (v1.1)

**Date:** 2026-09-23

### Context

`docs/limitations.md` F1 records that every reconciliation work item still
re-reads the whole provider library (`/api/v3/movie`, `/api/v3/series`) and then
the per-record file resource (`/api/v3/moviefile?movieId=`,
`/api/v3/episode?…&includeEpisodeFile=true`, `/api/v3/episodeFile?seriesId=`).
Each scheduled, post-scan, and manual reconciliation enqueues up to
`QueueCapacity` work hints, and each hint performs its own provider read, so a
large library performs one full provider-library read per work item. The
`GOALS.md` Reliability/Performance goal "avoid unnecessary API requests to Sonarr
and Radarr" is therefore only partially met for provider fetches.

This is a reconciliation fetch-volume problem, not a request-time problem. Per
ADR-001, badges are rendered asynchronously and published as persisted Jellyfin
item images; Jellyfin's normal image routes serve them, so no Arr call occurs per
poster request.

Research in `docs/research/sonarr-api.md` and `docs/research/radarr-api.md`
("Conditional requests and inventory caching"), with the structured report
`docs/research/arr-api-researcher/conditional-requests-and-inventory-caching.json`,
confirms:

- Neither provider generates `ETag`, reads `If-None-Match`, or emits
  `Last-Modified` for `/api/**`; `/api/**` is explicitly non-cacheable and
  `If-Modified-Since` is ignored. The undocumented `?h=` query bypass makes a
  path cacheable and must never be used.
- There is no bulk library revision token. `GET /api/v3/history/since?date=` is a
  durable but incomplete file-mutation log (grab/import/failed/delete/rename
  only) and itself requires a provider poll. `/system/status`, `/command`,
  `/system/task`, and `/queue` are not library revision tokens.
- Neither provider applies an inbound API rate limiter or documents a polling
  interval.
- Bulk selection is available: Radarr `/moviefile?movieId=` accepts a repeatable
  `movieId`, and Sonarr accepts repeated `episodeIds`/`episodeFileIds`.

### Decision

ArrTags adds a bounded provider inventory/catalogue cache at the provider-client
boundary.

1. **Scope.** The cache holds, per `ArrConnection`, the provider library list
   (Sonarr `/series`, Radarr `/movie`) and the per-record file resources needed
   for badge metadata, as canonical, secret-free observations.
2. **Reuse.** One library read per connection serves all work items in a
   reconciliation window instead of one read per item.
3. **Invalidation is ArrTags-side.** Invalidation sources are provider webhook
   events (ADR-012), Jellyfin library refresh/post-scan, scheduled/manual
   reconciliation, and a bounded TTL fallback. No provider conditional request,
   revision token, `history/since` watermark, or SignalR dependency is used. The
   complete trigger set is retained: v1.1 is **not** refresh-only, and periodic
   scheduled reconciliation continues alongside the webhook, post-scan, manual,
   and TTL sources.
4. **Bulk reads.** Where multiple records are needed, ArrTags uses the bulk
   selection endpoints (Radarr `moviefile?movieId=` repeated ids; Sonarr
   episode/episodeFile id selection) to avoid per-item reads.
5. **Bounds.** The inventory TTL and cache entry/size bounds are added to
   `OperationalLimits` and `docs/architecture.md` section 12, validated at
   configuration load. The cache is non-authoritative: on provider failure the
   existing bounded last-known-good semantics apply, and the cache never holds a
   credential or other secret. The inventory cache is in-memory and rebuilt on
   restart; it is not persisted authoritative state.
6. **Relationship to existing cache model.** The inventory cache is distinct from
   the per-item `MetadataCacheEntry` (`docs/data-model.md` 3.9): the inventory
   cache holds raw canonical provider observations for reuse, while
   `MetadataCacheEntry` remains the per-item match/metadata freshness record. The
   data model documents it as its own subsection under section 6, not as a
   `MetadataCacheEntry` variant.
7. Provider `ETag`/revision tokens remain optional observations only if a future
   provider contract supplies them; they are never assumed.

### Consequences

- Provider reads drop from O(work items) to O(connections) per TTL/refresh
  window, directly addressing F1.
- Staleness is bounded by the configured TTL and by event-based invalidation.
- New configuration and section 12 limit rows are required, and the
  `docs/data-model.md` cache model gains an inventory-cache description.
- The cache is bounded and secret-free and cannot evict authoritative provenance
  or non-terminal artwork operations.
- No dependency is introduced on provider caching internals or on an unstable
  push channel.

### Rejected alternatives

- Conditional `GET`/`ETag`/`If-None-Match` was rejected: neither provider
  supports it for `/api/**`.
- `history/since` as the primary invalidation signal was rejected: it is
  incomplete and still requires a provider poll.
- SignalR was rejected: it is an internal UI contract, not a stable external API.
- Per-request render caching was rejected: ADR-001 already publishes persisted
  images, so there is no per-request Arr call to cache.
- An unbounded or long-lived cache was rejected: it would present stale metadata
  as current.
- The `?h=` cacheable bypass was rejected: undocumented, unintended for API
  reads, and capable of returning an unconditional `304`.

### References

- `docs/research/sonarr-api.md`, "Conditional requests and inventory caching"
- `docs/research/radarr-api.md`, section 16
- `docs/research/arr-api-researcher/conditional-requests-and-inventory-caching.json`
- `docs/limitations.md` F1
- `docs/planning/v1.1.md` (Goal C, DG-12)
- `docs/architecture.md` sections 8 and 12
- ADR-001 (persisted derived artwork), ADR-004 (limits), ADR-012 (webhooks)

## ADR-019: Configurable Badge Size and Placement

**Status:** Accepted (v1.1; supersedes the code-owned geometry and placement
clauses of ADR-009 and the corresponding renderer-configuration clauses of
ADR-010)

**Date:** 2026-09-23

### Context

ADR-009 fixes the badge layout as a bottom-left technical rail (at most two
rows, three pills per row) with an independent top-right `UPGRADE` status pill,
at a reference geometry defined for a 1000-pixel poster width and scaled by
`clamp(width / 1000, 0.5, 4.0)`. ADR-010 and task 4.10 deliberately kept
geometry, placement, scale, and font code-owned and not representable in
configuration; `RendererConfiguration` exposes only selector enablement,
templates, and the four palette colors.

Operators want to choose the badge size and place the badge in a corner or the
center. This requires superseding the ADR-009/ADR-010 clauses that make placement
and geometry code-owned, while preserving the safe-area, text, contrast, and
determinism guarantees.

Decision gate DG-13 required the size semantics, the corner/center anchors, rail
packing, and status-pill placement.

### Decision

ArrTags adds a global badge size and position setting.

1. **Position.** `RendererConfiguration` gains `BadgePosition`, an enum with
   `TopLeft`, `TopRight`, `BottomLeft`, `BottomRight`, and `Center`. It positions
   the technical rail. The default is `BottomLeft`, which reproduces the V1
   output.
2. **Status pill.** The `UPGRADE` status pill remains top-right, except when the
   rail anchor is `TopRight`, in which case the status pill is placed top-left so
   the two never overlap. No separate status-position setting is added in v1.1.
3. **Size.** `RendererConfiguration` gains `BadgeSize`, an enum with `Small`,
   `Medium`, and `Large`; the default is `Medium`, which reproduces the V1
   geometry. The size multiplies the existing width-based scale:
   `effectiveScale = clamp(width / 1000, 0.5, 4.0) * sizeFactor`, where the
   code-owned factors are `Small = 0.75`, `Medium = 1.0`, and `Large = 1.5`. The
   effective scale is clamped so the badge still fits the safe area.
4. **Rail packing.** Rows stack away from the anchored edge: downward for top
   anchors, upward for bottom anchors (current behavior), and vertically centered
   for `Center`. Rows align to the anchored side: left-aligned for left anchors,
   right-aligned for right anchors, and centered for `Center`. Pills pack in the
   ADR-009 priority order within the available rows; the existing at-most-two-
   rows / three-pills-per-row limit, shortening, and omission behavior is
   unchanged.
5. **Safe area.** The 24-pixel scaled inset and all ADR-009 safe-area, text-limit,
   contrast, opacity, and determinism guarantees are unchanged. No pill may paint
   outside the safe area; a size or position that cannot fit falls back to the
   existing shorten/omit behavior rather than overflowing.
6. **Output-affecting identity and resolved snapshot.** The configured position
   and size are carried on the resolved `RenderOutputPolicy` (the object passed
   into `BadgeLayoutEngine.Build`), so the layout engine and renderer read them
   from the same immutable snapshot as the rest of the output policy. They are
   included in both `RendererConfigurationFingerprint` and
   `RenderFingerprint.ComputeOutputFingerprint` (which enumerates the policy
   fields explicitly). The renderer-configuration schema version and
   `RenderVersion.CurrentRendererVersion` advance (shared with ADR-017), and
   committed goldens are regenerated.
7. **No per-selector placement.** Placement and size are global renderer policy
   in v1.1; per-selector placement is not representable.

### Consequences

- Operators can place the badge in any corner or the center and choose a preset
  size.
- The V1 default (`BottomLeft`, `Medium`) reproduces the existing output for
  unchanged configuration.
- The change is output-affecting and republishes affected artwork.
- The derived status-pill rule keeps the two badge groups from overlapping
  without a second setting.
- Center and right anchors add new layout cases and goldens, covered by tests.

### Rejected alternatives

- A numeric size multiplier was rejected: presets are friendlier for a simple
  settings UI and are sufficient for v1.1.
- An independent status-pill position was rejected: more UI and more overlap
  combinations to validate.
- Keeping the status pill fixed at top-right with the rail reserving space
  (current behavior) was rejected: it is awkward when the rail is anchored
  top-right and does not express a true top-right rail.
- Always-left-aligned rows were rejected: they misalign at right anchors.
- Per-selector placement or size was rejected as out of v1.1 scope.

### References

- `docs/planning/v1.1.md` (Goal E, DG-13)
- `docs/data-model.md`, sections 3.6 and 3.12
- `src/ArrTags/Rendering/BadgeGeometry.cs`, `BadgeLayoutEngine.cs`,
  `SkiaBadgeRenderer.cs`
- `src/ArrTags/Configuration/RendererConfiguration.cs`,
  `RendererConfigurationFingerprint.cs`
- ADR-009 (superseded geometry/placement clauses), ADR-010 (renderer
  configuration)

## ADR-020: Plugin Logging and Configurable Verbosity

**Status:** Accepted (v1.1)

**Date:** 2026-09-23

### Context

The plugin currently has zero logging call sites. `docs/limitations.md` SEC-5
documents that fact as the reason no plugin log path can leak a secret:
"the plugin has no logging call sites, so no plugin log path can leak a secret."

The supported Jellyfin 12 logging contract is confirmed in
`docs/research/jellyfin-12-architecture.md` section 10 and
`docs/research/jellyfin-expert/jellyfin-12-config-pages-and-logging.json`:

- `ILogger<T>`/`ILoggerFactory` resolve through plugin DI and write to the host
  Serilog pipeline with `{SourceContext}` set to the logger category.
- Per-plugin verbosity is supported only as **plugin-owned gating** from the
  plugin's own configuration. The host's Serilog `MinimumLevel.Override` is host
  configuration, not a plugin API, and `ServerConfiguration` has no log-level
  field; the dashboard controls log viewing, not level.
- A plugin-registered `ILoggerProvider`/sink is ineffective on the pinned host:
  Jellyfin calls the no-argument `UseSerilog()` with no `LoggerProviderCollection`,
  so `SerilogLoggerFactory.AddProvider` ignores providers and `CreateLogger`
  always returns a Serilog-backed logger. Replacing the host `ILoggerFactory` is
  not a supported plugin contract.

Adding logging creates a new secret-exposure path. `PluginConfiguration` holds
`Sonarr.ApiKey`, `Radarr.ApiKey`, and `WebhookSecret`; provider requests carry the
`X-Api-Key` header and the webhook route authenticates the
`X-ArrTags-Webhook-Secret` header. The current SEC-5 negative result does not
cover any log path.

### Decision

1. ArrTags logs through `Microsoft.Extensions.Logging`
   `ILogger<T>`/`ILoggerFactory` resolved via DI. Logger categories are prefixed
   `ArrTags.*` (for example `ArrTags.Providers.Radarr`) so host per-category
   overrides are predictable.
2. A bounded, secret-free verbosity setting is added to `PluginConfiguration` as
   an explicit level enum (`Off`, `Error`, `Warning`, `Information`, `Debug`,
   `Trace`), validated at configuration load and exposed through the ADR-016
   settings UI and the XML configuration. The default is `Warning`.
3. Log calls are gated by the plugin's own verbosity, read from the current
   configuration snapshot. ArrTags does **not** register a custom
   `ILoggerProvider`/sink and does **not** replace the host `ILoggerFactory`.
4. **Redaction contract.** No log call may emit an API key, the webhook secret, a
   `SecretLease` value, an `X-Api-Key`/`X-ArrTags-Webhook-Secret` header, a raw
   request/response body, a full provider payload, or the mutable
   `PluginConfiguration`. Logging uses only types already proven bounded and
   redacted (`ArrProviderError`, safe `SecretReference`, connection identity,
   configuration version, and bounded reason codes). Raising verbosity must not
   expand a redacted value into a secret-bearing one.
5. Verbosity is **not** output-affecting: it is excluded from the renderer and
   configuration output fingerprints and does not change `RenderVersion`, so
   changing it never republishes artwork.
6. Log volume is bounded: high-frequency messages are rate-limited or
   repetition-suppressed, and a `docs/architecture.md` section 12 limit row
   records the bound.
7. Logging is a diagnostic mechanism, not a metrics/status surface; limitation
   F3 remains open.
8. `docs/limitations.md` SEC-5 is rewritten from "no logging call sites" to the
   redaction contract above, and the new logging path is covered by a security
   review.

### Consequences

- Operators can diagnose ArrTags behavior and raise or lower its verbosity
  without changing the global host log level or restarting (the setting applies
  through ADR-016's runtime activation).
- A new secret-exposure path exists and is mitigated by the redaction contract
  and by redaction tests at every verbosity level.
- SEC-5 changes from a negative result to a positive, reviewed contract.
- The `Warning` default keeps normal operation quiet while surfacing errors and
  warnings.
- No custom sink means log output goes only to the host's existing sinks; a
  future need for a plugin-owned sink would require a new decision.

### Rejected alternatives

- Registering a custom `ILoggerProvider`/sink was rejected: it is ineffective on
  the pinned host and not a supported contract.
- Relying solely on the host `MinimumLevel.Override` was rejected: it is host
  configuration, not a plugin-controlled setting.
- Logging everything at `Information` by default was rejected: unnecessary noise
  and a larger secret-exposure surface.
- Adding no logging was rejected: it leaves operators blind and does not address
  the diagnostics gap.

### References

- `docs/research/jellyfin-12-architecture.md`, section 10
- `docs/research/jellyfin-expert/jellyfin-12-config-pages-and-logging.json`
- `docs/limitations.md` SEC-5 and F3
- `docs/planning/v1.1.md` (Goal F, DG-14)
- ADR-005 (secret boundary), ADR-016 (settings UI activation)

## ADR-021: Administrator-Visible Configuration-Rejection Surfacing

**Status:** Accepted (v1.1)

**Date:** 2026-09-23

### Context

ADR-016 clause 4 requires `Plugin.UpdateConfiguration` to reject an invalid
candidate, retain the last valid snapshot and private secrets, surface the
bounded validation failure to the administrator, and never throw into the host.
The supported save path is `PluginsController` `POST {pluginId}/Configuration`,
which calls the void `UpdateConfiguration` and unconditionally returns
`204 NoContent`; the jellyfin-web client's
`Dashboard.processPluginConfigurationUpdateResult` reports only the HTTP
outcome, not plugin validation detail. A non-throwing override therefore cannot
surface a rejection inline or as an HTTP error.

Task 9.3 attempt 1 only retained the bounded, secret-free validation result on
the plugin instance (a diagnostic property). That is not administrator-visible
and does not satisfy clause 4's surfacing requirement.

The pinned Jellyfin `12.0.0` host exposes
`MediaBrowser.Model.Activity.IActivityManager` (resolvable through plugin DI)
with `Task CreateAsync(Jellyfin.Database.Implementations.Entities.ActivityLog
entry)`. Activity entries are visible to administrators in the dashboard
Activity log (`GET /System/ActivityLog/Entries`, elevation-gated) and pushed
over the ActivityLog websocket. The `IActivityManager` and `ActivityLog` types
live in `Jellyfin.Database.Implementations`, a lower stability tier than the
pinned `MediaBrowser.Model`/`MediaBrowser.Common` contracts (architecture
review risk AR-05).

### Decision

1. On a rejected configuration save, write **exactly one** administrator-visible
   activity-log entry through `IActivityManager.CreateAsync`. A valid save writes
   no entry.
2. Isolate the coupling to `Jellyfin.Database.Implementations` behind the
   plugin-owned `IConfigurationRejectionNotifier` interface with a Jellyfin-free
   contract (`NotifyRejected(IReadOnlyList<string> reasons)`). The only
   implementation that resolves `IActivityManager` is
   `JellyfinConfigurationRejectionNotifier`.
3. The entry is fixed and bounded. `Name` and `Type` are fixed constants
   (`Type` is `ArrTagsConfigurationRejected`) with no candidate values and no
   user input. `UserId` is `Guid.Empty` and `LogSeverity` is `Warning`.
   `Overview`/`ShortOverview` are built only from bounded, secret-free validation
   reasons; at most eight reasons are surfaced, control characters are stripped
   and whitespace runs are collapsed, and every reason and every field is
   truncated to the `ActivityLog` column bounds (`Name`/`Overview`/
   `ShortOverview` 512, `Type` 256). The interface documents that its only
   production caller passes the validator's secret-free messages
   (SEC-9.3-03).
4. The write is a bounded synchronous wait (consistent with the existing
   `OnUninstalling` drain) and is fully contained: it never throws into the host,
   and a notifier failure does not affect the rejection, the retained
   snapshot/secrets, or the last valid configuration (which remains active and
   persisted; the rejected candidate is never persisted).
5. No custom configuration-save route, inline page message, or HTTP error is
   added. The rejection is enforced before persistence: the override validates the
   candidate first and does not call the base implementation for an invalid
   candidate, so a rejected candidate is never persisted and the last valid
   configuration remains active. The whole validate/persist/activate sequence is
   serialized so concurrent saves cannot diverge (security finding SEC-9.3-01).
6. Duplicate consecutive identical entries are not suppressed in v1.1;
   admin-initiated saves are infrequent. A later refinement may add suppression.

### Consequences

- An administrator sees a bounded, secret-free "ArrTags configuration rejected"
  entry in the dashboard Activity log after a rejected save. This resolves the
  ADR-016 clause 4 surfacing requirement without changing the save path's HTTP
  outcome.
- The plugin depends on `Jellyfin.Database.Implementations` (transitively via
  `Jellyfin.Model`, so no extra package reference is required); the dependency is
  isolated in one adapter implementation, so a future host change is contained.
- The activity-log entry is a new plugin-initiated outbound administrator-visible
  surface. It carries no secret or candidate value and is recorded in
  `docs/limitations.md` SEC-9.
- The HTTP response of a rejected save remains `204 NoContent` and the dashboard
  reports success; the rejection is visible in the Activity log and, on reload,
  because the last valid configuration remains active and persisted (the rejected
  candidate was never written). An inline page message would require an HTTP
  error or a custom route, both forbidden by ADR-016 clause 4.
- The rejection is atomic with respect to persistence: validating before the base
  implementation means an invalid candidate is never written, so there is no
  crash window and no restore step for a concurrent valid save to overwrite. The
  save sequence is serialized, so the running snapshot, `Plugin.Configuration`,
  and the persisted file cannot diverge (security finding SEC-9.3-01).
- The write is best-effort: if `IActivityManager` is unavailable or the write
  fails, the rejection and the last-valid retention still hold and nothing is
  thrown.

### Rejected alternatives

- **Inline page message.** Rejected. The page's `updatePluginConfiguration`
  result reports only the HTTP outcome, so an inline message would require
  changing the HTTP outcome (an error) or adding a custom data route, both
  forbidden by ADR-016 clause 4.
- **Throw / HTTP error.** Rejected. ADR-016 clause 4 requires the override to
  never throw into the host, and a `500` would be an unbounded failure surface.
- **Host `ILogger`.** Rejected as the primary mechanism. Plugin logging and its
  redaction contract are Goal F (ADR-020), and a log line is not an
  administrator-visible dashboard surface; the activity log is the supported,
  elevation-gated administrator surface.
- **Property-only diagnostic.** Rejected as insufficient. A plugin-instance
  property is not administrator-visible and does not satisfy clause 4's
  surfacing requirement (the attempt-1 finding).
- **Duplicate-entry suppression in v1.1.** Deferred. Not required, and it adds
  cross-save state to a bounded best-effort path.

### References

- ADR-016 clause 4 (the `UpdateConfiguration` override and the surfacing
  requirement)
- ADR-005 (secret boundary), ADR-020 (logging and the SEC-5 rewrite)
- `docs/limitations.md` SEC-5 and SEC-9
- `docs/research/jellyfin-expert/configuration-save-failure-surfacing.json`
- `docs/research/jellyfin-12-architecture.md`, section 9
- `src/ArrTags/Configuration/IConfigurationRejectionNotifier.cs`,
  `src/ArrTags/PluginLifecycle/JellyfinConfigurationRejectionNotifier.cs`


