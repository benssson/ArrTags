# 9. Persisted artwork rendering

### Publication flow

1. The reconciliation worker checks configuration, item type, image type,
   library scope, current `PublishedArtworkState`, and whether current badge
   input exists.
2. It observes the current Jellyfin image surface. If no ArrTags ownership
   session exists, it captures the exact current image bytes into an immutable
   plugin-owned source artifact, or records that the surface was absent. If the
   persisted session is still owned, it reuses that artifact instead.
3. It includes all output-affecting values in a publication fingerprint:
   source-artwork identity, metadata fingerprint, render configuration, and
   renderer/schema versions.
4. It renders from the retained original source artifact, never from an
   ArrTags-generated image without a successful ownership comparison.
5. It promotes the validated render output to a durable operation artifact. An
   evictable `ArtworkCacheEntry` is never the only copy needed for recovery.
6. It creates a durable `Prepared` operation containing the before identity,
   candidate after content identity, artifact references, generation, and
   ownership/publication tokens.
7. It re-observes the before identity immediately before mutation. A changed or
   unverifiable identity aborts the operation and leaves the active image
   unchanged.
8. It durably records `MutationStarted`, calls `SaveImage`, durably records the
   repository-update phase, calls the normal item update flow, and then reads
   back the effective image identity.
9. Only a verified after identity permits the durable `PublishedArtworkState`
   commit. The operation is marked `Committed` only after that final state is
   durable; source and derived artifacts remain until then.
10. Jellyfin's standard image routes then provide authorization, image tags,
   resizing, and response caching to clients.
11. Any unavailable source, cancellation, decode failure, size violation, or
   render exception leaves the current usable artwork unchanged.

ArrTags must not write media-folder artwork or Jellyfin's `resized-images`
cache directly. It may use the supported item-image APIs to publish a derived
active image. The original source must remain recoverable through plugin-owned
provenance state.

Task 5.5 implements this flow as a provider-neutral `ArtworkPublisher` driving a
host-neutral `IArtworkImageWriter` whose single Jellyfin implementation uses the
supported stream `SaveImage` overload with the durable derived bytes and then
`UpdateToRepositoryAsync(ItemUpdateType.ImageUpdate, ...)`. ArrTags selects the
overload and supplies plugin-owned bytes; it does not choose or write the
destination path itself. Jellyfin's own `ImageSaver` decides where the bytes are
stored (its internal metadata path, or the media folder when the library's
`SaveLocalMetadata` option is enabled), which is Jellyfin's supported API
behavior rather than a direct ArrTags write. The filesystem-path overload is
never used because it deletes its source file, and URL overloads are never used
for derived bytes.

The exact Jellyfin `12.0.0` publication/read ABI, the standard item-image route
variants, the read/write authorization split, and the confirmed cache/resize
ownership are pinned in
[`docs/research/jellyfin-12-architecture.md`](../research/jellyfin-12-architecture.md)
section 4.4 (task 5.1) and asserted by
`tests/ArrTags.Tests/JellyfinImageAbiTests.cs` and
`tests/ArrTags.Tests/JellyfinImageRouteTests.cs`. That section confirms the ABI;
it does not change the publication semantics defined here.

Task 6.6 implements steps 3 and 4 as production behavior.
`ArtworkGenerationCoordinator` selects the render source before rendering: for an
owned `Published` (or captured `NotPublished`) session it reads and
integrity-validates the retained original source artifact from the task 5.2 store
and renders from it, never from the observed active surface, so a repeat
publication can never stack a badge onto a previous ArrTags output. A missing,
corrupt, or dimension-less retained baseline fails closed with no mutation rather
than re-capturing the derived image as a new original. The exact retained-source
read is supplied to the publisher's session path, and the publisher still
revalidates the before identity before any mutation. The per-subject
serialization gate (`ArtworkSubjectGate`) is a process-local, lazy
`ConcurrentDictionary` keyed by item and surface whose entries are retained for
the process lifetime; the bound is the number of subjects ever processed and is
documented here as accepted for V1 rather than periodically pruned, because
pruning cannot be done safely without reference-counting an in-flight gate and
the entry is small and bounded by the processed-subject count.

### Provenance and ownership contract

Jellyfin 12's supported persisted-image surface does not include an artwork
owner, plugin token, or restoration pointer. `IProviderManager.SaveImage` accepts
image bytes and a type/index, and the normal item-image update flow persists the
result. Jellyfin's `ImageInfo` can expose an image tag, path, dimensions, and
size, but the image tag is a Jellyfin representation/cache validator, not an
actor identity or content digest. ArrTags therefore owns the following evidence
in `PublishedArtworkState`:

- An immutable retained source artifact containing the exact original bytes and
  integrity hash, or an explicit record that no source image existed.
- A source-capture identity for the image surface before the first publication.
- A stable random ownership token for one original-to-derived session.
- A new random publication token for each derived publication in that session.
- The expected active-image identity, including the image surface, content hash,
  and available Jellyfin image metadata.

The ownership token is a plugin correlation value, not something Jellyfin reads
or returns. Ownership is proven only when a fresh active-image observation
matches the persisted expected identity. The content hash is required; a path,
date, size, or Jellyfin image tag alone is insufficient. When a required
observation is unavailable, ownership is unknown and ArrTags must not mutate the
active image.

When ArrTags publishes v2 after v1, it must first prove that v1 is still active,
then render v2 from the retained original artifact. It updates the publication
token and expected active identity but keeps the original source artifact and
ownership token. An ArrTags output is never eligible to become a new original.

Any active-image mismatch is recorded as `OwnershipLost` without attempting to
identify the actor. A mismatch may be caused by a user, Jellyfin, a provider, or
another plugin; all are external for restoration purposes. `OwnershipLost` and
`OwnershipUnknown` both block automatic publication, restoration, and removal.

On disable or uninstall, ArrTags enters `RestorePending` and revalidates the
expected active identity immediately before mutation. If it still matches and
the source artifact passes integrity validation, ArrTags restores the retained
source through the supported item-image API. If the baseline was absent, it
removes the ArrTags image through the supported item-image API instead. It marks
the state `Restored` only after the resulting surface is verified. A changed,
unverifiable, missing, or corrupt baseline leaves the active image untouched and
results in `OwnershipLost`, `OwnershipUnknown`, or `RestoreBlocked`. If an
attempted restoration has an uncertain result, ArrTags does not perform another
automatic mutation; the associated operation enters `RecoveryBlocked` until the
postcondition can be reconciled.

The persisted artifact, expected identity, and tokens are sufficient to
re-evaluate ownership after a normal Jellyfin or plugin restart. This section
does not define a distributed transaction with Jellyfin; the operation protocol
below provides durable intent, postcondition reconciliation, and fail-closed
recovery instead.

### Crash-consistent publication and recovery

Jellyfin's `SaveImage`, item repository update, and ArrTags state persistence do
not share a transaction. ArrTags therefore uses a write-ahead operation protocol
rather than claiming atomicity it cannot obtain.

#### Durable publication protocol

1. Serialize work by Jellyfin item and image surface and assign a monotonically
   increasing generation. A stale generation cannot publish or finalize.
2. Capture the Blocker 1 source artifact, if needed, into a bounded temporary
   artifact. Validate its format, size, and hash, flush it to stable storage,
   and promote it to its immutable artifact ID before publication work proceeds.
   An absent baseline is recorded explicitly.
3. Render the derived image into a temporary artifact. Validate and hash it,
   flush it to stable storage, and promote it to the operation's durable
   `derivedArtifactId`. A crash before this promotion has no Jellyfin side
   effect and only requires temporary-artifact cleanup.
4. Write and durably replace an `ArtworkOperation` in `Prepared` phase. The
   record includes the exact `expectedBeforeIdentity`, candidate after content
   hash, source/derived artifact IDs, ownership token, publication token,
   generation, and target final state. This intent is the write-ahead record for
   all later external mutations.
5. Re-read the item and active image. If the before identity no longer matches,
   commit `OwnershipLost` or `OwnershipUnknown` and abort without calling a
   Jellyfin image mutation API.
6. Durably advance the operation to `MutationStarted`, then call the supported
   `SaveImage` API with the durable derived artifact. The phase is written before
   the call because a crash can occur before the call, during it, or after it.
7. Durably advance to `RepositoryUpdateStarted`, then call the normal Jellyfin
   item update flow. This call is replayable for the same item state, but its
   completion is considered uncertain until readback.
8. Durably advance to `VerificationPending` and re-read the item image through
   the supported item-image information and image representation paths. Record
   the observed after identity only after the effective active image is known.
9. If the after identity matches the durable candidate and image surface, write
   the final `PublishedArtworkState` with its new state revision and operation
   ID. Flush that state before marking the operation `Committed`.
10. Cleanup is a separate, replayable step after commit. It may remove only
    temporary or staged artifacts proven not to be the active image. Source
    artifacts referenced by the committed ownership state remain retained.

The candidate content hash is not treated as proof that Jellyfin stored the
same representation. The selected Jellyfin ABI and host configuration must be
validated so the active representation can be read back and compared. If it
cannot, the operation enters `RecoveryBlocked` rather than guessing.

#### Restart reconciliation

At startup, before new artwork work is accepted for an item/surface, ArrTags
loads its valid final state and any non-terminal operation record, validates
artifact integrity, and obtains the current item and active-image identity.

- If the item is absent, ArrTags writes an `ItemRemoved` tombstone and performs
  no image mutation. It does not replay an old operation if the item later
  reappears; that item requires a new baseline and operation.
- If the current identity matches the recorded after identity, or matches the
  validated candidate after representation, ArrTags ensures the normal item
  update is persisted, commits the intended final plugin state, and marks the
  operation `Committed`.
- If the current identity matches the expected before identity, ArrTags
  revalidates the generation and lifecycle fence and may retry the same
  deterministic operation only when that fence permits it. A disable or
  uninstall fence aborts a prepared publication; a mutation already in flight
  is reconciled before the guarded restoration operation is created. ArrTags
  never captures the current image as a new source.
- If the current identity matches neither before nor after, ArrTags records
  `OwnershipLost` when the image is observable or `OwnershipUnknown` when it is
  not. It aborts the operation and never restores, removes, or overwrites that
  image automatically.
- If state, the operation record, or an artifact cannot pass integrity checks,
  ArrTags quarantines the invalid record, enters `RecoveryBlocked`, and performs
  no automatic image mutation.

If the final state was durably written but the journal was not marked committed,
the final state and verified active identity take precedence; startup completes
the journal and performs safe cleanup. If the journal was durably committed but
the final state is absent or invalid, ArrTags reconstructs it only from the
operation's verified postcondition and artifacts; otherwise it remains blocked.

Task 5.7 implements this reconciliation as the provider-neutral
`ArtworkReconciler` entry point backed by the pure `ArtworkRecoveryDecisions`
table. The reconciler serializes with normal publication through the same
per-item/image-surface gate, re-observes the item and active image, and delegates
the deterministic mutation, readback, and final-state commit to the
`ArtworkPublisher`'s recoverable execution path so that execution is never
reimplemented. A resume or retry is permitted only when the generation and
lifecycle fence allow it and the operation can be re-executed from its retained
artifacts without recapturing the active image; a lifecycle fence aborts a
prepared publication. Reconciliation performs no artifact deletion, so an
artifact that is not proven non-active is retained or quarantined. Invocation is
an explicit boundary: the guarded restoration mutation remains the lifecycle
task 5.9, and the event/webhook/scheduled/post-scan wiring that feeds the work
queue is provided by tasks 6.1, 6.7, and 6.9.

Task 6.4 drives this recovery entry point from the Phase 6 pipeline as the
provider-neutral `ArtworkRecoveryGate` (`IArtworkRecoveryGate`). Before a queued
item is processed, the gate reads the durable `ArtworkOperation` record and, when
a non-terminal operation exists, reconciles it through the `ArtworkReconciler`
under the current durable lifecycle fence and re-reads the record as a
postcondition. New work proceeds only when the record is absent, already
terminal, or has reached a terminal outcome; a corrupt record, a recovery that
does not reach a terminal outcome, or an older durable generation fails closed.
The gate serializes through the same `ArtworkSubjectGate`, exposes the
authoritative durable generation so accepted work can only supersede it through
the store's monotonic generation fence, and never recaptures the current image as
a new source, mutates a changed or unverifiable image, or deletes an artifact
that is not proven non-active. `ArtworkRecoveringWorkItemProcessor` composes the
gate ahead of the artwork-free metadata reconciliation processor, and
`ArtworkStartupRecoveryService` runs one bounded, cancellation-aware startup scan
over the persisted operation records limited by
`OperationalLimits.ReconciliationBatchSize`. The scan is best-effort and never
blocks host startup; any record beyond the batch is recovered lazily by the
per-subject gate before that subject's next work item, so a non-terminal
operation is always reconciled before new work for its item/image surface.

The publication protocol additionally re-reads and enforces the durable lifecycle
fence at two checkpoints inside `ArtworkPublisher`: immediately before the first
image mutation (the operation is aborted without an external effect) and again
before the final `PublishedArtworkState` commit (the verified postcondition is
recorded but the final commit is left to reconciliation). An in-flight
publication therefore cannot cross a disable or uninstall drain that was raised
after the operation was prepared; the drain reconciles the operation before
creating its guarded restoration, so no untracked non-terminal publication
survives the fence.

Task 5.8 implements the generation step as the provider-neutral
`ArtworkGenerationCoordinator`, which composes the host source adapter, the
renderer, and the publisher for one item and V1 surface. It observes the current
source, builds the renderer input, and publishes only a `Rendered` result; an
absent source, a failed source read, a render pass-through (including missing
metadata or an ineligible match), and a failed render all leave the current
usable artwork unchanged and perform no image mutation. Missing metadata and an
ineligible match use the existing ADR-009 renderer pass-through convention rather
than a new badge policy. A pass-through also occurs when the resolved selection
is empty (for example a selector allowlist that excludes the item), and because
a previously published badge is the current usable artwork it is preserved
rather than restored; that operator-visible consequence is documented as an open
limitation (`docs/limitations.md` F7). The coordinator never calls Jellyfin
directly. For source consistency, the exact source observation used for the
render is supplied
to the publisher's new-session capture, so the retained provenance baseline and
the derived artifact describe the same bounded observation; the publisher's
before-mutation revalidation is unchanged, so a source that changes after that
observation still prevents the mutation. Selecting the retained original artifact
as the render source for a repeat publication while an ArrTags session is already
owned (publication-flow step 4) was an explicit boundary of task 5.8, which
observed the current surface only; task 6.6 implements the retained-source
selection in the coordinator (see the publication-flow note above) and the
Phase 6 pipeline that invokes it.

#### Disable, uninstall, and item removal

- Disable and uninstall first write a durable lifecycle fence that prevents new
  publication operations. Existing operations are reconciled to a terminal
  result before restoration or cleanup begins.
- A still-owned `Published` state then creates a separate `Restoration`
  operation with the derived image as its before identity and the retained
  source or explicit absence as its after target. It uses the same durable
  phases, readback, and postcondition commit rules.
- If restoration is blocked, externally changed, or uncertain, ArrTags leaves
  the active image and recovery records in place. Uninstall must not delete the
  source artifact or journal needed to recover that state; cleanup is deferred
  and the incomplete lifecycle result is reported.
- An `ItemRemoved` event is a hint until the item is re-read. Once absence is
  confirmed, ArrTags tombstones in-flight operations, performs no Jellyfin image
  calls, and cleans only plugin-owned artifacts under the retention policy.

Task 5.9 implements this lifecycle handling as the provider-neutral
`ArtworkLifecycleCoordinator` (`IArtworkLifecycleCoordinator`) backed by the
authoritative `ArtworkLifecycleFenceStore`, the `ArtworkReconciler`, and the
`ArtworkPublisher`. The publisher refuses new publication unless the durable
fence is a valid `Normal`; a disable or uninstall drain records the fence,
reconciles every non-terminal operation to a terminal result, and then creates
and executes one guarded `Restoration` operation per still-owned `Published`
surface through the same durable phases, readback, and postcondition commit as
publication. A present retained source is written through the supported stream
`SaveImage` API and an absent baseline is removed through the supported
`BaseItem.DeleteImageAsync` flow exposed as
`IArtworkImageWriter.RemoveImageAsync`; a blocked, externally changed, or
uncertain restoration leaves the active image and all recovery records in place
and reports an incomplete result, and no source artifact or journal record is
deleted eagerly. The drain classifies each reconciled operation by its durable
phase rather than by the transient reconciliation outcome, so an operation that
becomes `RecoveryBlocked` (including through a non-throwing source-read failure)
is never counted as resolved and the lifecycle result stays `Incomplete`. An
invalid or corrupt durable fence is preserved as fail-closed and is never
quarantined away or overwritten with `Normal`, so the publication read path
stays closed until an explicit recovery decision. A confirmed item removal
claims a tombstone only after the reconciler actually aborted the in-flight operation; when
the reconciler cannot reach the `ItemRemoved` decision the result is `Blocked`
and no tombstone is claimed.

The host trigger mapping is explicit because Jellyfin 12 exposes no plugin
disable hook:

- **Uninstall** is raised by the supported `Plugin.OnUninstalling()` hook. The
  hook records the durable `Uninstall` fence and performs a bounded synchronous
  drain (with cancellation) before returning; a blocked or uncertain restoration
  is left untouched and the host still completes the uninstall. The hook never
  throws into the host. The pinned host's uninstall removes only the versioned
  install folder, not the plugin's relocated `DataFolderPath`, so once the drain
  completes the plugin removes its own state root to preserve the previous
  cleanup semantics; an incomplete or cancelled drain retains the recovery
  records for later reconciliation (ADR-014).
- **Disable** is detected from the persisted plugin manifest status through the
  supported `IPluginManager` when the hosted `ArrTagsLifecycleService.StopAsync`
  runs. Disabling a plugin writes the manifest status and takes effect on the
  next restart; the still-loaded instance observes the `Disabled` status during
  its graceful shutdown and drains a `Disable` fence. A plain server shutdown
  leaves the status active and resolves `Normal`, so it never triggers
  restoration. The hosted lifecycle service also clears a stale fence on
  `StartAsync` when the host has loaded the plugin active, and per-subject state
  and operation records continue to guard unsafe work.
- **Item removal** is the `ILibraryEventSource.ItemRemoved` hint. It is acted on
  only after the item absence is confirmed by a fresh read; the handler runs on
  a tracked, bounded background task so synchronous library event delivery is
  never blocked. A confirmed removal tombstones the in-flight operation and
  marks the state `Removed` with no Jellyfin image call; a not-confirmed hint
  changes nothing.

A disable that is only observed after the plugin has already been unloaded
(for example a disable followed by a hard kill without a graceful shutdown)
cannot be detected from inside the plugin; that boundary is a documented host
limitation, and the durable fence still prevents new publication work while it
is present.

### Rendering constraints

- Cap source bytes, output bytes, decoded dimensions, and concurrent renders.
- Publish only completed, validated images through the supported item-image API;
  do not implement a parallel response validator or image route.
- Verify that the normal Jellyfin image route supplies the published image with
  correct authorization, image tags, conditional requests, and cache headers.
- Do not block library scans or synchronous library event delivery.

V1 source capture reads only the unindexed `Primary` surface and accepts only
PNG and JPEG source containers, failing closed for every other container so a
malformed color profile in an uninspected container can never be treated as
sRGB. Jellyfin reports the pre-orientation encoded dimensions, so the host source
adapter derives the post-orientation display dimensions from the exact bytes with
the pinned raster stack before building the `SourceImageInput` and the
`ActiveImageIdentity`; item-type eligibility remains a caller concern.

### V1 rendering contract

ADR-009 is authoritative for the complete V1 visual contract. The architectural
boundary is summarized here so implementation does not infer a second policy:

- Only the `Primary` poster surface without an image index is rendered, and only
  for Movie and Episode items. Series and Season remain structural entities.
- The renderer consumes `RenderRequest`, `BadgeMetadata`, and ordered
  provider-neutral `BadgeDefinition` values. It never branches on Sonarr,
  Radarr, provider DTOs, provider record IDs, or quality profiles.
- The default priority is actual quality, resolution, dynamic range/Dolby
  Vision, source, video codec, one composite audio value, then custom values.
  An explicitly true upgrade-pending value is a separate `UPGRADE` status pill.
- Technical pills use a configurable global anchor (ADR-019: four corners plus
  center, default bottom-left) with a two-row rail; the status pill is top-right
  except when the anchor is top-right, then top-left. The rail has no more than
  three pills per row. Rows stack away from the anchored edge and align to the
  anchored side. The global preset size (Small/Medium/Large, default Medium)
  multiplies the reference geometry. Reference geometry is based on a 1000 pixel
  width and scales by `clamp(width / 1000, 0.5, 4.0)` times the code-owned size
  factor, clamped so the badge still fits the safe area.
- Labels are single-line, bounded to 24 Unicode scalar values after
  normalization, and end-truncated with `...`. Unknown values are omitted, not
  rendered as claims or placeholders.
- Output is an 8-bit lossless PNG at the source dimensions. Opaque inputs remain
  RGB; meaningful source alpha is preserved as RGBA. Badge backing and text are
  opaque and use the ADR-009 contrast-validated palette.
- The renderer ignores client size and device pixel ratio. Decode, output,
  cancellation, and artifact limits use the accepted operational bounds. Any
  failure returns pass-through and leaves current artwork unchanged.

All output-affecting source, metadata, definition, style, font, geometry,
format, limit, schema, and renderer values belong in the render/publication
fingerprint. Timestamps and request correlation IDs do not. Publication,
provenance, caching, stale-artwork lifecycle, and Enhanced coexistence remain
separate concerns and are not redefined by this rendering contract.

### Renderer implementation contract

ADR-010 is authoritative for the V1 renderer implementation except where ADR-015
supersedes it. The renderer is a plugin-owned direct SkiaSharp service with an
exact compile-time SkiaSharp pin and no use of Jellyfin's global image services.
It loads the bundled DejaVu Sans Bold 2.37 font by resource bytes and has no
host-font fallback.

ADR-015 fixes the renderer runtime packaging. The plugin compiles against the
pinned `SkiaSharp` and `SkiaSharp.NativeAssets.Linux` `3.119.4` references with
their runtime assets excluded, so the package carries only `ArrTags.dll`,
`ArrTags.deps.json`, `build.yaml`, `THIRD-PARTY-NOTICES.md`, and the font/Skia
license notices. At runtime the plugin resolves the host's managed SkiaSharp
through the default load context and uses the host's own native `libSkiaSharp.so`
and its `libfontconfig.so.1` dependency. V1 is validated on the pinned Jellyfin
`12.0.0` `linux-musl-x64` host. Shipping a second managed or native SkiaSharp copy
causes a fatal host/plugin type-identity conflict (task 7.3 finding 7.3-F1),
which is why the renderer runtime is no longer bundled.

> **Resolved by ADR-015 (task 7.8, live-verified).** Task 7.3 found the
> previously bundled `SkiaSharp.dll`/`libSkiaSharp.so` fatal: Jellyfin's own
> `ProviderManager.SaveImage` image processing aborted the process with
> `System.InvalidCastException: [A]SkiaSharp.UserDataDelegate cannot be cast to
> [B]SkiaSharp.UserDataDelegate` because the host and the plugin loaded two
> managed SkiaSharp assemblies. ADR-015 removes the duplicate, and the task 7.8
> live re-verification on the pinned musl host confirms the package loads, a
> badge publishes and is served by `GET /Items/{id}/Images/Primary`, the source
> posters are preserved, and a provider outage does not affect Jellyfin.

The host boundary supplies a bounded, read-only `SourceImageInput` containing
the exact source bytes or artifact handle, content type, dimensions, and source
hash. It does not pass paths, Jellyfin entities, provider DTOs, credentials, or
mutable image objects into the renderer. The conceptual service contract is:

```text
RenderAsync(RenderRequest request, CancellationToken cancellationToken)
    -> RenderResult
```

The renderer has no external side effects. Successful output is bounded,
non-interlaced 8-bit sRGB PNG with RGB or RGBA channels, fixed encoder settings,
stripped nondeterministic metadata, and canonical transparent-pixel values.
Invalid input, missing runtime/font assets, cancellation, decode/layout/encode
errors, and resource-limit violations return a safe bounded result without
mutating source bytes. An input without an embedded color profile is treated as
sRGB, a supported embedded profile is converted to sRGB, and a malformed or
unsupported embedded profile fails closed with a bounded reason instead of being
guessed as sRGB.

Renderer configuration is part of the immutable versioned plugin configuration
snapshot and contains only enabled V1 selectors, bounded templates, bounded
per-selector value allowlists, the global badge position and size, and
contrast-validated style overrides. Format, alpha/color policy, reference
geometry/text limits, font identity, and renderer version remain code-owned and
fingerprinted inputs. Each allowlist is validated in
`RendererConfiguration.Validate` with bounded secret-free messages (at most 32
entries per selector, each at most 64 characters, trimmed, no blank or
control-character entry, no case-insensitive duplicate); a non-empty resolved
allowlist is included in the renderer configuration fingerprint, normalized for
case and entry order, while an empty allowlist means no restriction and adds
nothing to the fingerprint (identity-neutral relative to a non-empty allowlist).
The value filter is applied in the documented ADR-017 order
(value resolution → allowlist filter → template → normalization/truncation →
layout): `BadgeDefinitionResolver` filters each resolved pre-template value
against the selector definition's resolved allowlist before applying the
definition template. Matching is a case-insensitive ordinal exact comparison of
the trimmed value; no substring, wildcard, prefix, or regular-expression
matching is supported. The allowlist is applied to each retained `CustomBadge`
value independently, to the full `Audio` composite (features, then codec, then
channel count), and to the fixed `UpgradePending` status text. The filter only
removes an already-confirmed value and never widens an omission, and an
allowlisted value that cannot fit still follows the existing shorten/omit
behavior because the filter runs before layout.

The global badge position and size (ADR-019) are the only other
configuration-derived placement values. `RendererConfiguration.Position`
(`BottomLeft` default, plus `TopLeft`, `TopRight`, `BottomRight`, and `Center`)
positions the technical rail; `RendererConfiguration.Size` (`Medium` default,
plus `Small` and `Large`) multiplies the width-based scale by a code-owned
factor (`0.75`, `1.0`, and `1.5`). Rows stack away from the anchored edge
(downward for top anchors, upward for bottom anchors, vertically centered for
`Center`) and align to the anchored side. The status pill stays top-right except
when the anchor is `TopRight`, then top-left, so the two never overlap. Both
values are carried on the resolved `RenderOutputPolicy` and included in both the
renderer configuration fingerprint and the render fingerprint; the default
position and size are identity-neutral relative to other placement values (the
default reproduces the V1 output, so its PNG bytes stay byte-identical), but the
coordinated version advance changes the default configuration and output
fingerprints. The scaled 24-pixel inset and all ADR-009 safe-area, text-limit,
contrast, opacity, and determinism guarantees are unchanged, and a size or
position that cannot fit
falls back to the existing shorten/omit behavior rather than overflowing. The
undefined-value check is bounded and secret-free in
`RendererConfiguration.Validate`. The v1.1 allowlist and placement changes share
one coordinated output-affecting advance: `RendererConfiguration.CurrentSchemaVersion`
advanced from 1 to 2 and `RenderVersion.CurrentRendererVersion` advanced from 2 to
3, and the committed goldens were regenerated under ADR-010 with no writer,
auto-update, or auto-approval path. The nine default-configuration goldens keep
byte-identical PNGs because the default reproduces the V1 output, while their
output fingerprints advance with the renderer version, and anchor/size goldens
were added (`top-left`, `top-right-large`, `bottom-right-small`, and `center`).
Renderer validation uses
synthetic fixtures, decoded-pixel goldens, same-runtime byte determinism, and
explicit cross-runtime anti-aliasing tolerances as defined by ADR-010.
