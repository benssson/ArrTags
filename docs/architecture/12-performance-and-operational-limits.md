# 12. Performance and operational limits

The decision that accepts these foundation defaults and its rationale are
recorded in [ADR-004](../decisions/00-index.md); the inbound webhook payload bound is
recorded in [ADR-012](../decisions/00-index.md). The accepted values, units, validation
ranges, and safe failure behavior are listed below. Limits are validated at
configuration load time; a value outside its documented range, or a non-finite
value where a finite value is required, rejects the new configuration and
retains the last valid snapshot rather than partially applying it.

| Limit | Default | Unit | Validation range | Safe failure behavior |
| --- | --- | --- | --- | --- |
| Update queue capacity | 512 | entries | integer `1`–`100000` | Overflow coalesces or drops redundant work; library events never block. |
| Per-item/surface in-flight work | 1 | operations | integer `1`–`1` | Additional hints coalesce into one pending work item. |
| Provider concurrency per connection | 4 | requests | integer `1`–`16` | Excess requests wait; no request is dropped silently. |
| Provider concurrency, all connections | 8 | requests | integer `1`–`32` | Bounds total concurrent Sonarr/Radarr requests. |
| Render concurrency | 2 | renders | integer `1`–`8` | Excess render work remains queued under the queue cap. |
| HTTP request timeout | 15 | seconds | `1`–`120` | Request is cancelled and classified as transient. |
| Transient retries | 2 | attempts | integer `0`–`5` | After exhaustion the connection is unhealthy and bounded last-known-good state applies. |
| Retry backoff | 1 initial, factor 2, 15 cap | seconds | base `1`–`60`, cap at least base and at most `120` | Retries are bounded; no unbounded retry loop. |
| Provider JSON response size | 8 | MiB | `64 KiB`–`64 MiB` | Response is rejected, classified invalid, and produces no badge. |
| Inbound webhook payload size | 256 | KiB | `4 KiB`–`4 MiB` | Request is rejected with `413` before the body is buffered and produces no work. |
| Source artifact size | 32 | MiB | `64 KiB`–`128 MiB` | Capture is rejected and the current artwork is preserved. |
| Derived artifact size | 32 | MiB | `64 KiB`–`128 MiB` | Render output is discarded and the current artwork is preserved. |
| Decoded image dimensions | 8192 per side | pixels | `512`–`16384` per side | Oversized input is rejected before decode. |
| Full-reconciliation batch size | 100 | records | integer `1`–`1000` | Cancellation is checked between pages and work yields between batches. |
| Metadata last-known-good window | 24 | hours | `5 min`–`7 days` | After expiry, stale metadata is not used and the current artwork is left unchanged. |
| Render work-cache TTL | 24 | hours | `1 min`–`30 days` | Expired entries are evicted. |
| Render work-cache quota | 1 | GiB | `64 MiB`–`64 GiB` | LRU eviction bounded by the quota; never evicts authoritative state. |
| Authoritative artifact/provenance storage quota | 4 | GiB | `256 MiB`–`256 GiB` | On exhaustion, reject new derived work and preserve the current artwork. |
| Terminal provenance retention | 30 | days | `1`–`365 days` | Cleanup only after the operation is terminal and cleanup is proven safe. |
| Plugin log verbosity | Warning | level | `Off`, `Error`, `Warning`, `Information`, `Debug`, or `Trace` | An undefined level rejects the configuration and retains the last valid snapshot; the default keeps normal operation quiet. |
| Plugin log volume | 5 per minute per category/event | messages | code-owned (not user-configurable) | Repetition-suppressed: at most the bound per category/event within the window, then one bounded suppression summary per window; the tracking set is capped at 256 keys. |
| Inventory cache TTL | 15 | minutes | integer `1`–`1440` | At expiry the cached observation set is not usable as current and the next reconciliation re-reads the provider library. |
| Inventory cache records per connection | 10000 | records | integer `1`–`100000` | An observation set larger than the bound is not cached; the direct provider read path is used unchanged. |
| Inventory cache bytes per connection | 32 | MiB | `1 MiB`–`256 MiB` | An observation set larger than the bound is not cached; the direct provider read path is used unchanged. |

The plugin log verbosity is a bounded, validated level (`LogVerbosity`) that
selects how much ArrTags writes through the host logging pipeline. It is applied
without a restart from the current configuration snapshot, is excluded from the
renderer and configuration output fingerprints, and never changes
`RenderVersion`, so it is not output-affecting. Every ArrTags boundary logs
through the plugin-owned `IArrTagsLog<T>` facade under the ADR-020 clause 4
redaction contract: only bounded, already-redacted values are emitted
(`ArrProviderError` code/retryability/message, the non-secret connection
identity, configuration version, bounded reason codes and enums, item/record
identifiers that are not secret, and counts), and no API key, webhook
secret, `SecretLease` value, `X-Api-Key`/`X-ArrTags-Webhook-Secret` header, raw
request/response body, full provider payload, or mutable `PluginConfiguration` is
ever logged. The emitted data shape is identical at every level, so raising
verbosity cannot expand a redacted value into a secret-bearing one. The
artwork-generation boundary currently logs only the bounded outcome and the
generic reason; the specific bounded `PassThroughReason`, `FailureReason`, and
`SourceFailureReason` are retained on the generation result but are not emitted,
which is documented as an open diagnostic limitation (`docs/limitations.md` F8).

Log volume is bounded (ADR-020 clause 6) by the plugin-owned, provider-neutral,
thread-safe `LogThrottle`: it admits at most 5 records per category and event
within a one-minute window, writes one bounded suppression summary when the
window rolls over, and caps its tracking set at 256 keys. The bound is code-owned
and not user-configurable; ADR-020 authorizes a new configuration field only for
the verbosity itself.

The provider inventory cache (ADR-018) is bounded by the three rows above and
validated at configuration load. It is in-memory, non-authoritative, rebuilt
empty on restart, and secret-free; it holds only canonical `MatchCandidate` and
`BadgeMetadata` observations and never a credential, secret lease, or provider
DTO. The entry's only free-text carrier is the bounded `ArrProviderError`
failure summary; value-level redaction of that message is the producer contract
(`ArrProviderError` is documented as bounded and redacted, ADR-020 clause 4),
consistent with the per-item metadata record's last-error summary. The configured
inventory TTL is the total bounded lifetime of one cached
observation set: fresh for the first half, explicit bounded last-known-good for
the remaining half, and expired and unusable as current at or after the TTL; the
TTL is never extended. A library read failure during a population caches nothing
and returns the bounded failure (so the cache never serves a failed read as
current, and the per-item bounded last-known-good metadata state is what retains
last-known-good metadata), while a failed bulk/sub-read keeps the direct read.
The per-connection record and byte bounds reject an over-bound
observation set and the caller keeps the direct provider read unchanged. The
default TTL is deliberately far shorter than the 24-hour metadata last-known-good
window because the inventory holds raw observations that are cheap to re-read
and must not present a stale library structure as current; event-based
invalidation is the primary freshness source and the TTL is only the bounded
fallback (ADR-018 clause 3). The provider metadata readers populate and consume
the cache at the provider-client boundary (task 11.2): concurrent cold readers
for one connection serialize through the per-connection single-flight gate so one
library read serves all of them, a cache hit serves a work item without a
provider library read, the cached population path reads the per-record file
resources through the bulk selection endpoints (in bounded chunks) rather than
per item, and the cache bounds are resolved from the current configuration
snapshot so a replaced snapshot rebuilds the non-authoritative cache with the new
bounds. The cache is invalidated ArrTags-side (task 11.3): an accepted provider
webhook invalidates the event's connection, and a reconciliation (Jellyfin
library refresh/post-scan, scheduled, manual, or post-save) invalidates the
retained sets so its work begins a fresh provider-read window; the inventory TTL
remains the bounded fallback. The invalidation surface is bounded, thread-safe,
and secret-free, and the periodic scheduled reconciliation continues on its
interval, so v1.1 is not refresh-only. The observation set's `ObservedAt` is the
population time, and the cache
entry's bounded reuse window (`StaleUntil`) limits how long it may be reused; the
published per-item metadata freshness remains derived from the publish time, not
the observation time. No provider `ETag` or revision token is assumed (ADR-018
clause 7).

Active provenance and any non-terminal artwork operation are never evicted as
ordinary cache entries. They remain until the operation is committed, safely
aborted, or tombstoned, regardless of cache or quota pressure. The render
work cache is bounded independently from authoritative provenance. When the
authoritative storage quota is exhausted, ArrTags rejects new derived work and
preserves the current artwork instead of evicting recovery state.

The configured metadata last-known-good window is the total bound on
last-known-good use. A metadata state record is fresh for the first half of the
window (`expiresAt = fetchedAt + window / 2`) and may be retained as explicit
bounded stale last-known-good for the remaining half until the end of the
configured window (`staleUntil = fetchedAt + window`); at or after `staleUntil`
it is expired and unusable as current, consistent with the section-12 safe
failure above. A transient provider outage within the window keeps the snapshot
as explicit stale state; it never extends the bounded window. Bounded artifact
retention reclaims superseded derived render output (proven non-active,
non-source, and not referenced by a non-terminal or recovery-blocked operation)
so the authoritative quota can be reused, while the retained source baseline of
a live session and the active image are never reclaimed.

Metrics or diagnostic status should distinguish queue depth, API health,
matching failures, cache hits/misses, render failures, and stale metadata
without exposing credentials or full external payloads. A bounded, secret-free
status/diagnostic surface is not yet implemented and is documented as an open
limitation in `docs/limitations.md`.

The provider concurrency (per connection and global) and render concurrency
limits are enforced at their boundaries, not merely validated. Each provider
reconciliation read acquires the global then the per-connection permit, and each
render acquires a render permit; both limits are read from the current
configuration snapshot on every acquisition, so a replaced snapshot takes effect
without rebuilding a singleton. The bounded, cancellation-aware limiter suspends
waiters without blocking a thread, is cancelled by the operation's token, and
does not grow an unbounded queue of its own. Excess provider requests wait rather
than being dropped silently, and excess render work remains queued under the
work-queue cap.
