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

**Status:** Accepted

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
