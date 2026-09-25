# 6. Configuration and persisted state

### Configuration

`PluginConfiguration` is persisted through Jellyfin's plugin configuration
mechanism. Updates are treated as replacement snapshots, not as mutable objects
shared with background workers. A candidate configuration is validated before it
becomes active; an invalid replacement is rejected and the last valid snapshot
stays active so invalid configuration cannot bring down Jellyfin.

The persisted configuration includes:

- Independently enabled Sonarr and Radarr connections.
- Base URL, API key, timeout, and explicit TLS exception policy per connection.
- Enabled libraries (collection-folder/library identifiers, not display names),
  eligible image/item types, and V1 badge surfaces. V1 badge-bearing item types
  are Movie and Episode posters; Series and Season are structural only and
  aggregation remains post-V1 (ADR-006).
- Badge fields, placement, colors, scale, margins, output limits, and renderer
  version settings. DG-3 is resolved by ADR-009: V1 uses the bounded,
  provider-neutral Movie/Episode Primary-poster specification in that ADR.
- Operational limits supplied by `OperationalLimits`: queue capacity, provider
  and render concurrency, request timeout, retry count/backoff, provider
  response and artifact sizes, decoded image dimensions, reconciliation batch
  size, cache TTL/quota, provenance retention, and the stale-data window (see
  section 12 and ADR-004).
- V1 does not persist or publish path mappings. Configured path fallback is
  deferred out of V1 by ADR-008.
- Webhook token or equivalent secret for inbound Arr notifications. V1 uses the
  `WebhookSecret` slot as the inbound shared secret; the anonymous
  `POST /ArrTags/Webhook/Sonarr` and `POST /ArrTags/Webhook/Radarr` endpoints
  authenticate it through the versioned secret boundary and never place it in a
  URL (ADR-012).
- No separate Jellyfin Enhanced coexistence field is persisted. The coexistence
  policy (ADR-011) is realized entirely through the existing poster enable flags
  and renderer selector enablement, with no automatic duplicate/overlap
  suppression and no Enhanced-internals dependency.
- Bounded per-plugin log verbosity (`LogVerbosity`, default `Warning`). It is
  validated at configuration load and applied without a restart from the current
  snapshot. It is not output-affecting: it is excluded from the renderer and
  configuration output fingerprints and never changes `RenderVersion` (ADR-020
  clause 5).

API keys and webhook secrets must not appear in logs, status responses, cache
keys, fingerprints, or exception messages. TLS certificate validation is strict
by default; any exception is explicit and scoped to one connection.

### Dashboard settings page

`Plugin` implements `MediaBrowser.Model.Plugins.IHasWebPages` and returns one
`PluginPageInfo` (`Name` `ArrTags`, `EnableInMainMenu = false`) whose
`EmbeddedResourcePath` is the embedded `Configuration/config.html` with the
explicit assembly manifest logical name `ArrTags.Configuration.config.html`
(ADR-016 clause 1). The pinned `Jellyfin.Api.Controllers.DashboardController`
serves the page from the plugin's own assembly resource at
`GET web/ConfigurationPage?name=ArrTags`; the server injects nothing into the
page.

The page is read/write for the user-adjustable settings only: the Sonarr and
Radarr connections and their API keys, the webhook secret, the Movie/Episode
poster flags, the enabled-library scope, the renderer selectors/templates and
per-selector value allowlists, palette overrides, the operational limits, and
the bounded log verbosity. It reads and writes them through
the supported elevation-gated `PluginsController` `GET`/`POST
{pluginId}/Configuration` path and adds no custom save route (ADR-016 clause 3).
The three secret inputs are password fields with no embedded value; the page
embeds no secret literal and only ever surfaces a secret value through the same
administrator-gated configuration API that already returns it (ADR-016
clause 2).

The byte-denominated operational limits are presented in a fixed per-field
binary unit (MB, or KB for the webhook payload limit) with the field's fixed
step and display precision; the input declares the unit-labelled range in the
same unit and its native `step` plus the explicit conversion check reject a
value that is not an exact step multiple, while the persisted and internal
values stay bytes (ADR-028 clause 1; task 16.1).

The pinned static page-resource action carries no `[Authorize]`, the controller
has no class-level `[Authorize]`, and there is no fallback authorization policy,
so the page resource itself is reachable without authentication. ADR-016
clause 6 explicitly accepts this anonymous static page-resource endpoint as a
non-data surface: the page is static HTML/JS, reflects no data, and every
configuration data endpoint remains administrator-gated. This acceptance is the
task 9.2 confirmation of the page-resource authorization behavior; the
host-guarded tests additionally confirm the pinned route and authorization
attributes where a pinned host directory is available.

### Runtime configuration activation

`Plugin` overrides `BasePlugin<T>.UpdateConfiguration`. The host's
elevation-gated `PluginsController` `POST {pluginId}/Configuration` deserializes
the candidate and calls the override. The override validates the candidate before
the base implementation persists anything (using the same
`PluginConfigurationValidator` the snapshot service uses); a valid candidate is
persisted by `base.UpdateConfiguration(configuration)` and then activated by
`ConfigurationSnapshotService.TryReplace(...)`, so a saved change takes effect
without a host restart (ADR-016 clause 4). The whole validate/persist/activate
sequence is serialized, so concurrent elevation-gated saves cannot leave the
running snapshot, `Plugin.Configuration`, and the persisted file divergent
(security finding SEC-9.3-01).

An invalid candidate is rejected before persistence: the override does not call
`base.UpdateConfiguration`, so a rejected candidate is never written to
`plugins/configurations/ArrTags.xml` and the last valid public snapshot and
private secret map remain active. The rejection is surfaced to the administrator
as exactly one bounded, secret-free activity-log entry written through the
plugin-owned `IConfigurationRejectionNotifier` adapter, whose Jellyfin
implementation resolves the host `IActivityManager` and never throws into the
host (ADR-021); a valid save writes no entry. The bounded, secret-free validation
outcome is also retained on the plugin instance for diagnostics and is never
persisted or returned by the configuration API. The override never throws into
the host (service resolution, validation, and notification failures are
contained) and adds no custom configuration-save route (ADR-016 clause 3).

Services that resolve from the current snapshot on each operation — the work
queue capacity and in-flight bound, provider/render concurrency, metadata
freshness, badge definitions, and the renderer output policy — observe a
replaced snapshot without rebuilding the singletons (ADR-016 clause 5 first
bullet). Some singletons capture the artifact-size/decode limits and the
`StateRepository`-backed render work-cache TTL/quota and terminal-provenance
retention values from `OperationalLimits` at construction, so those particular
values change only after a host restart; this pre-existing state/artwork-layer
behaviour is outside the save-path change.

The complete, code-evidenced classification of every user-adjustable setting is
owned by `src/ArrTags/Configuration/RestartRequiredSettings.cs` (ADR-028
clause 2; task 16.2): a setting is restart-required when any consumer resolves
its value at singleton construction, and per-operation only when every consumer
resolves it from the current snapshot on each operation. The restart-required
set is `SourceArtifactLimitBytes`, `DerivedArtifactLimitBytes`, and
`MaxImageDimensionPixels` (each mixed: a construction-captured consumer and a
per-operation render consumer both exist), plus the construction-only
`RenderCacheTtlMinutes`, `RenderCacheQuotaBytes`, `ArtifactStorageQuotaBytes`,
and `TerminalProvenanceRetentionDays`; every other setting, including
`LogVerbosity`, the connection fields, the badge/library scope, and the renderer
configuration, is per-operation. The per-consumer evidence is recorded in
`docs/implementation/16.2/consumer-evidence.md`; the settings page mirrors the
set in its `restartRequiredFields` table and marks every restart-required setting
with always-present static note text stating that a Jellyfin server restart is
required for a change to take effect, with the mixed settings also stating that
some paths apply the change immediately (ADR-028 clause 3; task 16.3).

The modal restart reminder is implemented as a best-effort web-only behavior
(ADR-028 clause 4; task 16.5). On save, the page snapshots the persisted
restart-required values from the freshly fetched configuration before the submit
handler mutates it, and after writing the submitted values it compares each
classified setting against that snapshot; only a real change schedules a
reminder. The reminder runs inside the successful
`ApiClient.updatePluginConfiguration(...)` callback after the unchanged
`Dashboard.processPluginConfigurationUpdateResult` call, so a rejected POST shows
no reminder. It resolves `window.Dashboard` lazily and calls
`Dashboard.alert({ title, message })` with a plain-text body under `message`,
inside a presence/type guard
(`if (!window.Dashboard || typeof window.Dashboard.alert !== 'function') { return; }`)
and a swallowing `try`/`catch`, so an absent or failing modal API shows nothing
and changes nothing. The task 16.4 `jellyfin-expert` spike recorded that the
pinned Jellyfin 12.0 bundled web client exposes `window.Dashboard` as a legacy
compatibility object whose `Dashboard.alert({ title, message })` is the only
single-button modal (`Dashboard.alert('text')` is a transient non-modal toast,
`Dashboard.confirm(...)` is a two-button modal, and `Dashboard.showMessage` does
not exist). The object is an intentionally retained web-client compatibility shim
with an in-source TODO to remove it, so it is public but potentially unstable and
is not a server plugin ABI; it exists only in jellyfin-web, and the
pinned-asset/source confirmation is not a live-browser probe. Dismissing the
modal with Escape or the backdrop is accepted as a benign unhandled promise
rejection in the shim (`Dashboard.alert(object)` returns no promise and attaches
no `.catch`); it does not affect the save or the page. The repository has no
JavaScript runtime, so the reminder cannot be executed by the test suite; its
contract is pinned by structural tests over the embedded page
(`tests/ArrTags.Tests/RestartRequiredReminderTests.cs`). ADR-028 clause 4's
fail-open condition applies: the always-present note text above is the
authoritative behavior and the modal is best-effort. The full finding and
recommendation are recorded in
`docs/research/jellyfin-expert/modal-notification-api.json`.

### Logging and verbosity

ArrTags logs through the host's `Microsoft.Extensions.Logging`
`ILogger<T>`/`ILoggerFactory`, resolved through plugin DI (ADR-020 clause 1).
The host's Serilog pipeline and sinks receive the records with the logger
category (the calling type's full name) as `{SourceContext}`; because every
ArrTags type lives under the `ArrTags` namespace, every category is prefixed
`ArrTags.*` (for example `ArrTags.Providers.Radarr`), so an administrator's
host-level per-category override is predictable. ArrTags registers no custom
`ILoggerProvider`/sink and does not replace the host `ILoggerFactory` (ADR-020
clause 3); a plugin-registered provider would be ineffective on the pinned host.

Per-plugin verbosity is the bounded `LogVerbosity` enum (`Off`, `Error`,
`Warning`, `Information`, `Debug`, `Trace`; default `Warning`), persisted in
`PluginConfiguration`, validated at configuration load, and exposed through the
settings page and the XML configuration. A plugin-owned `ILogVerbosityGate`
reads the effective verbosity from the current configuration snapshot on every
call, so a saved change applies without a restart; it is provider-neutral and
decides whether a `Microsoft.Extensions.Logging.LogLevel` is enabled for
ArrTags. An undefined level is rejected by validation and defensively treated as
`Warning`, so an invalid candidate can never raise verbosity.

Verbosity is not output-affecting: it is excluded from the renderer and
configuration output fingerprints and never changes `RenderVersion`, so
changing it never republishes artwork (ADR-020 clause 5). The log call sites are
now added at the provider, matching, metadata, artwork, queue, reconciliation,
webhook, and lifecycle boundaries through the plugin-owned `IArrTagsLog<T>`
facade, which emits only bounded, already-redacted values under the ADR-020
clause 4 redaction contract and applies the shared repetition suppressor; the
bounded log-volume limit row is recorded in section 12 (ADR-020 clause 6). The
allowed value set includes the bounded log subject added by the ADR-026
amendment to ADR-020 clause 4: the media file-name component only
(`Path.GetFileName`), with control scalars removed and bounded to 128 Unicode
scalar values, and the Jellyfin item identifier in `D` format as the fallback
when no usable name is available. An explicit directory, drive, share, or
full-path exclusion applies; the exclusion is host-native-path scoped, because
`Path.GetFileName` uses host-platform separator semantics (on the pinned Linux
host a Windows-style backslash string is a single file name rather than a
path). The artwork-boundary line also carries the
specific bounded render classification (`PassThroughReason`, `FailureReason`,
or `SourceFailureReason`) when the result has one. The `Warning` default keeps
normal operation quiet.

### Bounded post-save reconciliation

A successful replacement also requests the bounded, non-blocking post-save
reconciliation (ADR-016 clause 5 second bullet). `Plugin.UpdateConfiguration`
calls the plugin-owned `IConfigurationReconciliationTrigger` boundary after the
running snapshot is replaced. The production
`ConfigurationReconciliationTrigger` is a hosted singleton whose loop runs the
existing bounded `LibraryReconciliationService` off the save thread and enqueues
the same provider-neutral `LibraryWorkHint` work as the scheduled, manual, and
post-scan triggers (source `PostSave`). The request slot is bounded: at most one
reconciliation is pending, a redundant request is coalesced, and a request that
arrives while a reconciliation is running schedules exactly one bounded rerun so
the latest replaced snapshot is observed (a run reads the current snapshot once
when it starts). The trigger is never a synchronous full-library scan, never
blocks the save response, and never throws into the host; the save path contains
a trigger-resolution or trigger failure. The trigger is required because a work
item whose `ConfigurationVersion` is older than the current snapshot is skipped
by `ArtworkPublishingWorkItemProcessor`, so without it existing posters would
re-render only on the next library event, webhook, post-scan, or scheduled run.
Task 9.5's Goal A integration verification composes the full save -> activate ->
bounded-reconcile flow without a live host, so limitation F2 is resolved.

### Secret access boundary

Jellyfin's persisted `PluginConfiguration` is the only V1 source of truth for
the Sonarr API key, Radarr API key, and inbound webhook shared secret. ArrTags
does not add a second secret file, state record, database table, environment
variable, or external secret manager. Protection at rest therefore follows the
Jellyfin configuration/data-folder permissions and administrative boundary.

The configuration service creates a safe `SecretReference` for each configured
credential slot and an in-memory private secret snapshot when it creates the
public `PluginConfigurationSnapshot`. The active configuration is one atomic
versioned pair: a public snapshot containing no secret values and a private map
from typed references to secret material. V1 has one stable API-key slot for
each provider and one separate webhook-secret slot. References are generated by
the configuration boundary, contain no secret, and are safe to carry in an
`ArrConnection`, queue hint, or diagnostic status.

The DI boundary is a singleton `IPluginSecretResolver`. Its semantic contract is
`TryAcquire(reference, expectedConfigurationVersion)`, returning a short-lived,
disposable, non-serializable `SecretLease` or no result. The lease has no public
diagnostic/string representation. The provider transport boundary uses an
API-key lease only to add `X-Api-Key` to the current request; a future webhook
boundary uses its distinct lease for constant-time candidate comparison. The
HTTP client factory remains secret-free.

Workers capture the public configuration version before resolving a connection
and acquire the matching lease before sending a request. A version mismatch,
disabled connection, unknown reference, or missing secret produces no lease and
causes the bounded operation to discard or restart against the current snapshot.
Queue items, caches, canonical models, state envelopes, and fingerprints carry
only the safe reference or configuration version, never a lease or secret.
The resolver publishes immutable maps for concurrent workers; each worker gets
an independent lease, no lock is held across external I/O, and a lease is
released when the bounded request or authentication comparison completes,
cancels, or times out.

Configuration replacement validates the candidate first, then creates both
snapshot components and swaps them together. Invalid input leaves both the
public and private active state unchanged. A key rotation retains the same
safe reference and connection identity when the provider and base URL are
unchanged, but increments the configuration version. New work uses the new
lease; an already acquired lease may finish its bounded cancellable request with
the old value. A retry must reacquire against the current version rather than
reuse an old authentication failure. Restart reconstructs the private snapshot
from persisted plugin configuration; no plugin state or metadata cache contains
credentials.

### Plugin state

State is stored under `DataFolderPath`, not in Jellyfin's database or image
cache. `DataFolderPath` is relocated by the `Plugin` constructor to
`ProgramDataPath/ArrTags`, a sibling of the plugins directory and therefore
outside `PluginsPath` (ADR-014); Jellyfin's derived `PluginsPath/<assembly name>`
is never used because it collides with the supported versioned install folder.
Each record is written as a versioned envelope carrying a schema version
and a SHA-256 integrity hash over its payload. Writes go through a flushed
temporary file and an atomic replacement in the same directory, and record kinds
and identifiers are validated as single path segments so state cannot escape its
root. Corrupt non-authoritative cache state may be ignored or rebuilt;
authoritative artwork state and operation records are quarantined and retained
for recovery rather than treated as `NotPublished`. Cache records are bounded by
age and quota, while authoritative records are never pruned as ordinary cache
entries; only explicitly terminal provenance records are eligible for retention
cleanup. Metadata last-known-good records are governed by freshness rather than
by the render work-cache TTL/quota and are explicitly exempt from that policy. A
bounded, scheduled retention pass applies cache/quota retention, terminal
provenance retention, metadata freshness retention, and authoritative artifact
garbage collection, so retention is enforced in production and not only in
tests. Artifact garbage collection deletes an artifact only after proving it is
not the active image, not the retained source of a live ownership session, and
not referenced by a non-terminal or recovery-blocked operation, and it fails
closed when an authoritative record cannot be validated.

Publication and restoration use a durable write-ahead operation journal under
the same plugin data boundary. `ArtworkOperation` records, operation manifests,
and staged artifacts are not evictable render-cache entries. They remain until
the operation is committed, safely aborted, or durably tombstoned as removed.
An invalid or torn operation record is quarantined and causes recovery to fail
closed rather than replaying an unknown mutation.

The retained source artwork is stored as a content-addressed, immutable artifact
under the plugin data folder (the `artifacts/source` area, sharded by hash). The
exact source bytes are accompanied by an authoritative manifest containing the
MIME type, byte length, and SHA-256, persisted through the versioned state
boundary so it is never treated as ordinary cache and a corrupt manifest is
quarantined. Promotion is atomic and bounded: the bytes pass size, MIME/magic
byte, and hash validation before an identical artifact is reused (content
addressing makes promotion idempotent). When the authoritative artifact storage
quota would be exceeded, new derived work is rejected and the current artwork is
preserved rather than evicting provenance. `PublishedArtworkState` is persisted
as authoritative state under the same boundary; a valid envelope whose payload
violates the documented ownership invariants is quarantined and never replayed.

Each item record may contain:

- Jellyfin item ID and provider IDs.
- Arr connection identity and the typed Arr record/file identity.
- Last successful Arr metadata snapshot or normalized badge input.
- `PublishedArtworkState`, including the retained source artifact, expected
  active-image identity, ownership/publication tokens, and restoration state,
  when ArrTags has published an image.
- Badge-relevant metadata fingerprint.
- Jellyfin image tag/date observations used as supporting published-artwork
  evidence and invalidation; these are not ownership proof by themselves.
- Renderer/configuration version.
- Last refresh, error, retry, and reconciliation status.
- Non-terminal artwork operations and lifecycle fences, when publication,
  restoration, disable, uninstall, or removal is in progress.

The metadata record and artwork publication state have different purposes. A
metadata snapshot may be retained as last-known-good state during a temporary
Arr outage. A derived image published through Jellyfin is active artwork and is
not an ephemeral response artifact. The original source and restoration
provenance remain plugin-owned and must not be confused with the active image.
The operation journal is the recovery authority until a final artwork state is
durably committed; cache entries never serve that role.
