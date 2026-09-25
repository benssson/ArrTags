# 8. Reconciliation and update flow

Library event handlers are deliberately short:

1. Check whether the event is relevant to an enabled library/item type.
2. Ignore ArrTags-generated internal state changes where applicable.
3. Enqueue the Jellyfin item ID and a reason/version hint.
4. Return without external I/O, rendering, or image writes.

Work is coalesced by item ID and connection. A worker re-reads the item and
current configuration before processing. It then:

1. Resolves the applicable Sonarr/Radarr connection.
2. Matches the item using the policy above.
3. Fetches or reuses the current Arr record and file metadata.
4. Produces a normalized badge input and fingerprint.
5. Atomically publishes the metadata state.
6. Re-observes the configured image surface and evaluates the persisted
   `PublishedArtworkState` ownership contract.
7. Enqueues artwork generation when the publication fingerprint changes or when
   a new recoverable source baseline is required.
8. Creates or resumes the durable `ArtworkOperation`; source capture and
   rendering produce durable staged artifacts before any Jellyfin image API is
   called.
9. Delegates publication, item persistence, postcondition verification, and
   final `PublishedArtworkState` commit to the crash-recoverable operation
   protocol in section 9.

The worker must re-check the item and relevant metadata before publishing a
result if the operation was long-running. A changed fingerprint causes stale
work to be discarded rather than published. A non-terminal artwork operation
also fences new work for its item/image surface until recovery reaches a
terminal outcome.

Work items contain only item, connection, reason, and safe configuration-version
hints. They never capture API keys or secret leases. A worker must resolve the
current public snapshot and acquire a matching lease at the external-I/O
boundary; configuration rotation or disablement therefore fences new provider
requests without requiring queue contents to be scrubbed for credentials.

The installed implementation (`src/ArrTags/Updates`) realizes this boundary as a
bounded in-memory work queue with a fixed, bounded pool of cancellation-aware
hosted workers. Work is single-flight per item and image surface; the pending
bound and the per-item in-flight bound are resolved from the current
configuration snapshot. A worker classifies each outcome with the provider retry
vocabulary (`ArrErrorRetryability`) and retries only a transient (`Later`)
outcome, within `TransientRetryCount` and the bounded exponential backoff; a
terminal or unclassified failure is not retried. A redundant hint for work that
is already pending or in flight is coalesced, and overflow coalesces or drops
without ever blocking or throwing into the library-event publisher. On shutdown
the worker stops accepting, cancels queued and in-flight work, and awaits the
workers within a bounded host-shutdown timeout. The reconciliation work
dispatched by the worker is installed in `src/ArrTags/Reconciliation`
(`MetadataReconciliationProcessor`): it re-reads the current item and
configuration, resolves the applicable connection, matches and maps the current
provider metadata through the provider readers, revalidates the work's basis
immediately before publishing, and atomically publishes the metadata state
through the versioned cache boundary; a changed basis (advanced configuration
version, disabled or changed connection, removed/changed/ineligible item) or a
provider read failure discards the work rather than publishing partial or stale
state. A published metadata state carries computed freshness boundaries derived
from the configured last-known-good window. A transient provider outage within
that window keeps the last-known-good snapshot as explicit stale state without
extending the bounded window; after the window the snapshot is expired and is
not usable as current, so new artwork publication must stop or retain the
current usable artwork.

The artwork stage of step 7 is installed in `src/ArrTags/Updates`
(`ArtworkPublishingWorkItemProcessor`) behind the pure
`src/ArrTags/Artwork/ArtworkRegenerationPlanner`. For a published metadata state
it compares the logical publication fingerprint against the persisted
`PublishedArtworkState` and drives the coordinator only when the fingerprint or
the renderer/schema identity changed or a new recoverable source baseline is
required; metadata that is not usable as current under the freshness policy is
never rendered or published, and artwork generation is best-effort after the
successful metadata publication so a render or publication failure preserves the
current usable artwork. The task 6.4 recovery gate still runs before any new work
for a subject, and every generation enters the publisher through its normal
durable write-ahead protocol and per-subject serialization.

Refresh triggers are:

- Jellyfin `ItemAdded`, relevant `ItemUpdated`, and `ItemRemoved` events.
- Post-scan reconciliation.
- Manual and periodic scheduled reconciliation.
- Authenticated Sonarr/Radarr webhooks as low-latency hints.

Webhooks are accelerators, not the source of truth. Payloads are validated,
bounded, and converted into the same deduplicated work as other triggers. A
periodic reconciliation repairs missed or forged notifications.

The same trigger set is the ArrTags-side provider inventory cache invalidation
set (ADR-018 clause 3): an accepted webhook invalidates its connection, and a
reconciliation (post-scan, manual/periodic scheduled, or post-save) invalidates
the retained observation sets, with the inventory TTL as the bounded fallback.

All reconciliation triggers (events, webhooks, post-scan, and manual/periodic
scheduled) enqueue the same bounded work hints through the bounded, coalescing
queue, so reconciliation is bounded by `QueueCapacity` (ADR-004). The scheduled
and post-scan scopes are enumerated from the start of a deterministic order and
the queue drops overflow, so a single run over a scope larger than
`QueueCapacity` covers a bounded prefix; successive runs overlap rather than
advancing. Making successive runs cover the whole scope (a persisted enumeration
cursor, stale/unknown-only enqueue, or direct pipeline drive) is not implemented
and is documented as an open limitation in `docs/limitations/00-index.md` rather than
presented as solved.

Coalescing is version-blind: the queue key is the item, connection, and image
surface, so a redundant hint is coalesced regardless of the configuration version
it carries. A post-save hint carrying the new configuration version is therefore
coalesced away when the item already has pending or in-flight work under the
previous version, and that outstanding item then discards itself as a stale basis
and is completed without re-enqueueing; the item is re-rendered on the next
trigger rather than by the save. This is a known open limitation
(`docs/limitations/00-index.md` F6) and is not presented as solved.

The installed webhook boundary (`src/ArrTags/Webhooks`, ADR-012) realizes this
contract. `ArrTagsWebhookController` is an anonymous plugin route
(`POST /ArrTags/Webhook/Sonarr` and `POST /ArrTags/Webhook/Radarr`) discovered
by Jellyfin's plugin controller registration. A pre-binding authorization filter
authenticates the `X-ArrTags-Webhook-Secret` header through the ADR-005 webhook
lease with a constant-time comparison before MVC model binding can read the
request body, so the uniform fail-closed `401` holds for every content type; the
filter also rejects a non-JSON content type with a bounded `400` before the read,
so the configured payload bound rather than the framework form limits governs
the route. The action then enforces the configured bounded payload size, parses
only the event type, upgrade flag, and provider record/file hints with a bounded
tolerant parser, and performs a non-blocking submit; it returns only bounded
safe status codes and never logs, returns, or retains the secret or body. A
bounded, coalescing `WebhookIntake` plus the hosted `WebhookIntakeService`
resolve an accepted event off the request path through
`WebhookReconciliationResolver`, which looks up only the Jellyfin items ArrTags
has already associated with the advertised provider record in its persisted
metadata-state mapping, bounded by the configured reconciliation batch size. The
resolved items are enqueued through `IWorkHintSink` as the same bounded
`LibraryWorkHint` work as every other trigger, so the worker re-reads current
Jellyfin and Arr state and discards a changed or ineligible basis. Duplicate,
out-of-order, and replayed deliveries coalesce in the short intake window and in
the work queue; an unmatched event is a bounded no-op that periodic
reconciliation repairs.

The installed scheduled and post-scan triggers (`src/ArrTags/Reconciliation` and
`src/ArrTags/PluginLifecycle`) realize the remaining refresh-trigger set. The
provider-neutral `LibraryReconciliationService` enumerates the candidate movie
and episode items in pages bounded by `OperationalLimits.ReconciliationBatchSize`
through the `IMediaLibraryEnumerator` boundary, filters each page through the
existing `MediaIdentityFactory` and `MediaEligibility` boundaries against the
current configuration snapshot, checks cancellation between and inside pages,
yields between batches, and enqueues only the same bounded `LibraryWorkHint` work
through `IWorkHintSink`; it never calls a provider, renderer, publisher, or image
API. It stops as soon as the durable lifecycle fence refuses new publication
work, so a disable or uninstall fence is never crossed. `ArrTagsReconciliationTask`
is the Jellyfin 12 `IScheduledTask` with a default periodic interval trigger and
manual execution through Jellyfin's scheduled-task surface;
`ArrTagsPostScanTask` is the Jellyfin 12 `ILibraryPostScanTask` invoked by
`LibraryManager` after a media-library scan. Both are public concrete plugin
types discovered by Jellyfin's assembly scanning and are also registered in DI,
and neither performs provider, rendering, or library work during registration or
construction. The ADR-004 provider and render concurrency limits are enforced at
their boundaries: a `ProviderConcurrencyLimiter` acquires the global and
per-connection permits around each reconciliation read, and a
`ConcurrencyLimitedRenderer` acquires the render permit around each render; both
resolve the current limit from the configuration snapshot on every acquisition
through the bounded, cancellation-aware `DynamicConcurrencyLimiter`, so a
replaced snapshot takes effect without rebuilding a singleton.

The bounded provider inventory cache boundary is defined in
`src/ArrTags/Providers` (`ArrInventoryCache`/`ArrInventoryCacheEntry`). It holds
one canonical, secret-free observation set per `ArrConnection` — the provider
library list and the per-record file observations needed for badge metadata — so
one library read can serve a reconciliation window instead of one read per work
item (ADR-018 clause 1). The set is provider-neutral: it carries only the
canonical `MatchCandidate` and `BadgeMetadata`, never a provider DTO, credential,
request URL, or Jellyfin item state. The cache is non-authoritative and
in-memory: it is never persisted, is rebuilt empty on restart, and cannot evict
authoritative provenance or a non-terminal artwork operation. The configured
inventory TTL is the total bounded lifetime of one observation set: a stored set
is fresh for the first half and is served as explicit bounded last-known-good for
the remaining half, and the TTL is never extended. A library read failure during
a population caches nothing and returns the bounded failure, so the inventory
cache never serves a failed read as current; the existing per-item bounded
last-known-good metadata state is what retains last-known-good metadata. An
observation set that exceeds the configured record or byte bound, or a failed
bulk sub-read, is not cached and the caller keeps the direct provider read
unchanged. No provider `ETag`, `If-None-Match`, revision token, or
`history/since` watermark is assumed (ADR-018 clauses 3 and 7).

The provider-client integration is installed (task 11.2). `ArrInventoryCacheProvider`
resolves the cache from the current configuration snapshot and rebuilds it with
the new validated bounds when the snapshot is replaced, so a changed inventory
limit takes effect without rebuilding the singleton (the cache is
non-authoritative, so the discard is safe). It also owns the per-connection
single-flight population gate, so concurrent cold readers for one connection
serialize and one library read (and one set of bulk file reads) populates the
cache for all of them; a reader that waited re-checks the cache rather than
starting a second population, and a failed population releases the gate for a
later attempt. The `RadarrMetadataReader` and `SonarrMetadataReader` populate the
cache on a miss with one library read (`api/v3/movie` / `api/v3/series`) plus the
bulk file reads, store the canonical observations, and serve every work item in
the cache window from the retained set without another library read; an absent or
expired set is re-read, and an over-bound set or a failed bulk/sub-read keeps the
existing direct read unchanged. A library read failure during a population
(`/api/v3/movie` or `/api/v3/series`) caches nothing and fails the work items in
that window: the reader returns the bounded failure, and each work item that
waited on the per-connection single-flight gate re-checks the still-empty cache
and re-attempts the population. A sub-read failure (the bulk
`/moviefile?movieId=`, the per-series `/episode?seriesId=`, or the bulk
`/episodeFile?episodeFileIds=`) falls back to the unchanged direct read instead
of failing the window. The per-item bounded last-known-good metadata state is what retains
the existing artwork through that outage (the published state is kept as explicit
stale with an unchanged fingerprint and window). On the cached population
path the file resources are read with the bulk selection endpoints (ADR-018
clause 4): the Radarr client issues bounded `moviefile?movieId=` requests with a
repeatable `movieId` for the movies with a file, and the Sonarr client resolves
episode files from the per-series episode read (which embeds the current file)
and issues bounded `episodeFile?episodeFileIds=` requests with a repeatable
`episodeFileId` for the remaining files, so the cached path does not issue
per-item file reads. The direct-read fallback (over-bound sets and failed
bulk/sub-reads) remains and still performs the per-record file read unchanged. The
observation set's `ObservedAt` is the population time; the published per-item
`MetadataStateEntry` freshness remains derived from the publish time
(`DateTimeOffset.UtcNow`), and the cache entry's bounded reuse window
(`StaleUntil`) is what limits how long an observation set may be presented as
current inventory. The ArrTags-side invalidation sources are wired (task 11.3,
ADR-018 clause 3): an accepted provider webhook invalidates the event's
provider/connection from the hosted intake loop before any hint is enqueued (a
bounded invalidate-all when the connection cannot be resolved), and Jellyfin
library refresh/post-scan, manual and periodic scheduled, and post-save
reconciliation invalidate the retained observation sets at the start of
`LibraryReconciliationService.ReconcileAsync`, so their work begins a fresh
provider-read window. The invalidation surface (`ArrInventoryCache.Invalidate`/
`InvalidateAll` and the `ArrInventoryCacheProvider` equivalents) is bounded,
thread-safe, and secret-free: it removes retained sets under the same gate as
`TryStore`/`TryGet`, never holds a lock across provider I/O, and carries only the
non-secret connection identifier; a population already in flight when the
invalidation runs may still store the read it took before it, which is bounded by
the configured TTL and repaired by the next invalidation source. The bounded
inventory TTL remains the fallback, and the periodic scheduled reconciliation
still runs on its interval, so v1.1 is not refresh-only.
