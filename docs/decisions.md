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

- `docs/jellyfin-12-architecture.md`
- `docs/poster-rendering-strategies.md`
- `docs/architecture.md`
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
