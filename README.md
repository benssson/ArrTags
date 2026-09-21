## Project Status

**Current milestone:** Phase 6 — Caching, updates & performance is complete
(tasks 6.1 through 6.9; Gate 6 met at the integration-test level, tag
`v0.1.0-phase6`). Library-event, authenticated webhook, scheduled/periodic,
post-scan, and manual reconciliation feed a bounded, coalescing, single-flight
work queue; metadata state is published atomically after current
item/configuration re-validation, with an explicit freshness and bounded
stale-last-known-good policy separated from artwork retention and bounded
artifact GC; non-terminal artwork operations are recovered before new work for
the same item/image surface; artwork is regenerated only when a publication
fingerprint changes, and repeat publication renders from the retained original
source rather than the previous ArrTags output; and the authenticated, bounded
webhook boundary is pinned by ADR-012. The provider and render concurrency limits
from ADR-004 are enforced at their boundaries. No live Jellyfin host or live Arr
instance was exercised, so host-guarded and native-Skia facts skip in this
environment. The provider inventory/catalogue cache, runtime configuration
replacement wiring, and a safe metrics/diagnostic-status surface remain tracked
for Phase 7.

Phase 5 — Jellyfin artwork integration is complete (tasks 5.1 through 5.11; Gate
5 met for the pinned 12.0.0 ABI at the integration-test level). Phase 5 derived
poster artwork is published through Jellyfin's supported item-image APIs with
retained-source provenance and guarded restoration; the standard image response
path is validated in-process against the real pinned host `ImageController` plus
the real ArrTags publication boundary, and a live HTTP round-trip was not
performed. Phase 7 wires the Phase 6 pipeline to that publication path on a
host.
Phase 4 — Badge rendering is complete (tasks 4.1 through 4.11; all Milestone 4
acceptance criteria satisfied and Gate 4 met). The renderer is provider-neutral
and deterministic within the configured limits with safe pass-through on
failure; the only deferred validation is the ADR-010 non-canonical cross-runtime
comparison, tracked for the testing/release milestone. Milestone 3 is complete
(tasks 3.1 through 3.8; acceptance criteria satisfied and Gate 3 met).

Completed in Phase 2 (Sonarr & Radarr integration):

- 2.1 Shared provider-client boundary and connection identity.
- 2.2 Dedicated `IHttpClientFactory` client registration.
- 2.3 Versioned credential boundary and Radarr v3 reads.
- 2.4 Sonarr v3 reads (series, episodes, and episode files) with the validated
  episode-file join.
- 2.5 Canonical `ArrProvider`/`ArrConnection`/`BadgeMetadata` mapping with
  connection-scoped Sonarr and Radarr record/file identity, actual-file quality
  semantics, and no provider DTO leakage.
- 2.6 Explicit unknown audio-feature state and bounded, sanitized custom badge
  values at the canonical metadata boundary.
- 2.7 Provider failure-matrix tests for authentication failures, unavailable
  services, malformed responses, optional fields, version drift, cancellation,
  and retries.

Completed in Phase 3:

- 3.1 Canonical `MediaIdentity` snapshots for Movie, Series, Season, and Episode
  with deterministic library scope and V1 badge-surface eligibility.
- 3.2 Provider-neutral candidate selection, evidence recording, and the
  deterministic `MediaMatch` fingerprint.
- 3.3 Match status policy: zero candidates resolve to `NotFound`, multiple
  candidates resolve to `Ambiguous`, and only a single candidate is accepted,
  with no title/year guessing.
- 3.4 Documented movie, series, and episode matching order, with provider-neutral
  orchestration, the series-before-episode rule, bounded unsupported outcomes,
  and provider-specific candidate assembly.
- 3.5 Explicit V1 episode-numbering policy (ADR-007): number fallback is enabled
  only for regular single episodes after the series match, excluding season zero
  specials, multi-episode spans, and absolute/scene numbering.
- 3.6 DG-5 path decision (ADR-008): configured path mappings and path fallback
  are deferred out of V1; V1 never assumes Jellyfin and Arr path namespaces are
  equivalent.
- 3.7 Connection-scoped identity verification: Arr-local record and file IDs are
  always bounded by their originating `ArrConnection`, so identical numeric IDs
  on different Sonarr/Radarr connections never collide.
- 3.8 Fail-closed location eligibility: remote, virtual, offline, `.strm`, and
  other non-local/fileless items are rejected with a safe no-badge status before
  provider matching, while eligible local Movie and Episode files match
  unchanged. Paths remain non-identity context only under ADR-008.

Completed in Phase 4 (Badge rendering):

- 4.1 Provider-neutral `BadgeSelector` vocabulary and `BadgeSelectorResolver`
  reading only canonical `BadgeMetadata`, in ADR-009 priority order.
- 4.2 V1 rendering specification (ADR-009): fields, templates, layout, geometry,
  contrast, text limits, PNG output, scaling, and pass-through policy. No
  rendering code is included in this documentation closure.
- 4.3 Unknown-versus-confirmed-negative semantics: tri-state flags resolve only
  on a confirmed `true`, confirmed generic dynamic range is displayed while an
  unknown range is omitted, and the canonical fingerprint retains the
  distinction.
- 4.4 Render request and result fingerprints: `RenderVersion`,
  `RenderOutputPolicy`, and the validated `RenderFingerprintInput` snapshot feed
  deterministic SHA-256 result/output and request (render key) fingerprints over
  every output-affecting value, including the renderer and badge schema versions,
  excluding correlation identifiers and timestamps.
- 4.5 Image and text limit enforcement: `RenderLimitGuard` validates the bounded
  source descriptor and planned derived output surface against the accepted
  operational limits before decode/draw/encode work, and `BadgeTextNormalizer`
  applies the ADR-009 control/whitespace normalization and 24-scalar end
  truncation from `RenderOutputPolicy` without splitting a surrogate pair.
  `RenderLimitResult` carries only a bounded, non-secret reason code.
- 4.7 Pinned renderer assets and shipped notices (ADR-010): `SkiaSharp` and
  `SkiaSharp.NativeAssets.Linux` are pinned to the exact Jellyfin 12.0.0 host
  version `3.119.4` (no `HarfBuzzSharp` until task 4.8 decides), and both
  `packages.lock.json` files are regenerated. The DejaVu Sans Bold 2.37 font
  (708,920 bytes, SHA-256 `5c1247ac...252ce895`) is embedded as the stable
  resource `ArrTags.Resources.DejaVuSans-Bold.ttf`. `RenderFontIdentity` records
  family, style, version, byte length, SHA-256, and the resource logical name and
  opens a read-only stream over the exact bytes; `RenderOutputPolicy.Default` uses
  that identity and the render fingerprint records its descriptor, so a changed
  font asset changes the fingerprint. `THIRD-PARTY-NOTICES.md` and the
  `licenses/` files (`DejaVu-Fonts-License.txt`, `SkiaSharp-LICENSE.txt`, and
  `SkiaSharp-THIRD-PARTY-NOTICES.txt`) are copied into the plugin package.
- 4.8 SkiaSharp host-compatibility spike: the pinned Jellyfin 12.0.0 host stack
  (`SkiaSharp` / `SkiaSharp.HarfBuzz` / `SkiaSharp.NativeAssets.Linux` `3.119.4`
  and `HarfBuzzSharp` / `HarfBuzzSharp.NativeAssets.Linux` `8.3.1.5`, native
  ELF64 x86-64 `libSkiaSharp.so` / `libHarfBuzzSharp.so`) is confirmed; plugin
  load-context resolution was measured; and a decode/draw/encode round trip
  using the pinned `3.119.4` native library and the embedded DejaVu Sans Bold
  font is an environment-guarded test. The Phase 5 packaging constraint is to
  place the managed and root-level native SkiaSharp assets in the plugin folder.
  `HarfBuzzSharp` is confirmed unnecessary for ADR-009's labels. Evidence is in
  `docs/research/skia-host-compatibility.md`.
- 4.9 Provider-neutral renderer service and drawing engine (ADR-009/ADR-010):
  `SourceImageInput`, the minimal `BadgeDefinition` snapshot with its code-owned
  V1 default, `RenderRequest`, the bounded three-variant `RenderResult`,
  `IRenderer`/`SkiaBadgeRenderer`, the pure `BadgeLayoutEngine`/`BadgeGeometry`/
  `BadgeContrast` helpers, and `BadgeTextNormalizer.Shorten`. The renderer
  resolves the ordered `BadgeSelection` through the existing selector resolver,
  enforces the source/output limits and contrast before decode, applies EXIF
  orientation, packs the two-row/three-pill bottom-left rail and the top-right
  `UPGRADE` status pill, and encodes a deterministic fixed-settings
  non-interlaced 8-bit sRGB PNG (RGB for opaque output, straight-alpha RGBA
  otherwise with canonical transparent-pixel RGB). Cancellation is checked at
  each documented checkpoint and never yields a partial artifact or mutates the
  source. The renderer is a pure library and is not yet wired to plugin
  configuration, DI, providers, or Jellyfin (tasks 4.10 and 4.11 remain).
- 4.6 Renderer behavior matrix: seven test-only files add 51 cases covering the
  named behaviors with no production change. Unguarded cases cover the
  `clamp(width / 1000, 0.5, 4.0)` geometry scale, resolved-value truncation and
  width fitting, two-row/three-pill rail packing, lower-priority omission,
  safe-area bounds on narrow and short posters, independent top-right `UPGRADE`
  placement, cancellation precedence and bounded cancellation, and every
  pre-decode pass-through/failure decision (limits, contrast, invalid color,
  missing font resource, no metadata/source). Environment-guarded cases cover
  real decode/encode dimensions for opaque, alpha, and EXIF-oriented sources,
  structural PNG format behavior (8-bit non-interlaced RGB/RGBA, straight
  semi-transparent alpha, canonical transparent pixels, `sRGB` and stripped
  source metadata), and the malformed/unsupported decode failures. The
  renderer's later cancellation checkpoints are not independently reachable
  through the public synchronous contract without production-only test hooks;
  that limitation is recorded in the task status. Default `./build.sh test`
  passes 554 tests with 21 guarded Skia skips (575 total); the forced native run
  passes all 575.
- 4.10 Renderer configuration model, snapshot, and fingerprint (ADR-010):
  `RendererConfiguration`/`BadgeSelectorConfiguration` persist only enabled V1
  selectors, one bounded provider-neutral `{value}` template per selector, and
  four optional contrast-validated palette overrides; output format, color
  space, alpha, font, geometry, text limits, and renderer version stay
  code-owned. `PluginConfigurationValidator` rejects unknown/duplicate
  selectors, unusable templates, malformed colors, and any style below 4.5:1
  contrast. `RendererConfigurationResolver` maps the configuration to the
  ordered `BadgeDefinition` snapshot and effective `RenderOutputPolicy`, and
  `PluginConfigurationSnapshot` exposes both plus a deterministic secret-free
  `RendererConfigurationFingerprint` (SHA-256 over selector enablement/templates
  and palette, excluding credentials, the webhook secret, timestamps, and
  correlation identifiers). Defaults reproduce ADR-009 exactly and
  `ConfigurationSnapshotService.TryReplace` retains the last valid snapshot and
  private secrets on an invalid candidate. The configuration is not yet wired to
  the Jellyfin admin save surface, DI, providers, or artwork pipeline.

- 4.11 Golden-image, byte-determinism, PNG-contract, and cross-runtime tolerance
  tests (ADR-010) — complete. The test oracle is in place: committed
  repository-owned synthetic decoded-pixel goldens (`tests/ArrTags.Tests/Goldens/`)
  for the ADR-010 fixture list compared as decoded planes, encoded bytes,
  dimensions, alpha/channel behavior, output hash, and output fingerprint with no
  auto-approval or golden-writer path; byte-determinism across repeated renders,
  item identity, observation timestamp, stream chunking, and a separate fresh
  process; PNG-contract tests for non-interlaced 8-bit RGB/RGBA, fixed `sRGB`,
  stripped metadata, canonical transparent-pixel RGB, straight source-alpha
  preservation, and malformed/unsupported embedded-profile rejection; and the
  ADR-010 cross-runtime comparator (exact canonical rule plus the 0.1 percent
  anti-aliased-text tolerance) with unguarded boundary tests. The F2 supporting
  production change deferred from 4.9 is included: an invalid or unsupported
  embedded PNG `iCCP`/JPEG `APP2` profile now fails closed with
  `RenderFailureReason.UnsupportedColorProfile`, while a supported profile is
  converted to sRGB and an unprofiled input is treated as sRGB. The authorized
  task 4.11 correctness fix corrects the pre-existing EXIF dimension-swapping
  orientation defect in `SkiaOrientation.Apply` (the transforms now translate
  about the source dimensions), and `RenderOrientationTests` proves all eight
  orientations are fully opaque and place their corner markers correctly; because
  that is an output-affecting drawing change `RenderVersion` advanced to 2 and
  the committed golden manifest was regenerated. Default `./build.sh test` passes
  593 tests with 40 guarded skips (633 total); the forced native run passes 655
  tests with one non-canonical-golden skip (656 total).

Completed in Phase 5 (Jellyfin artwork integration):

- 5.1 Confirmed the exact supported Jellyfin 12.0.0 item-image publication ABI
  and route variants. The publication surface
  (`IProviderManager.SaveImage` stream/URL/path overloads), the read surface
  (`BaseItem.GetImageInfo`/`ImageInfos`, `ItemImageInfo`, `ImageInfo`,
  `IImageProcessor.GetImageCacheTag`/`GetImageDimensions`,
  `ILibraryManager.UpdateImagesAsync`/`ConvertImageToLocal`), and the item
  update (`BaseItem.UpdateToRepositoryAsync(ItemUpdateType.ImageUpdate, ...)`)
  are pinned against the `12.0.0` NuGet assemblies. The standard
  `ImageController` route templates and the read/write authorization split are
  pinned against the pinned host `Jellyfin.Api.dll`, and the route/authorization
  status observations are live-confirmed on the pinned host and its OpenAPI
  document. Jellyfin's image-tag/`ETag`/`Last-Modified` and cache-header
  behavior and its never-upscale resize path are confirmed from the pinned
  source/artifact. Evidence is in
  `docs/research/jellyfin-12-architecture.md` section 4.4 (routes in section
  3.1, response/authorization behavior in section 3.3); the confirmation is
  asserted by `JellyfinImageAbiTests` (11 unguarded cases) and
  `JellyfinImageRouteTests` (5 host-guarded cases). V1 remains the unindexed
  `Primary` poster for Movie and Episode only (ADR-006/ADR-009); indexed or
  alternate poster surfaces are out of V1.
- 5.2 Implemented the provider-neutral source-artwork provenance and guarded
  restoration state in `src/ArrTags/Artwork`. `ActiveImageIdentity` is the
  observable surface/presence/content-hash identity and `ArtworkOwnershipComparer`
  applies the fail-closed ADR-002 ownership rule (surface and presence must
  match, a present identity needs a matching content hash, recorded Jellyfin
  values must still match when observable, and a missing hash or unavailable
  observation is `Unknown`). `PublishedArtworkState` carries the data-model
  3.10.1 fields and states with validated invariants, and
  `PublishedArtworkStateTransitions` implements every 3.10.2 guarded transition
  as pure decisions and commits without performing any image mutation.
  `SourceArtifactStore` is the content-addressed, immutable, traversal-safe
  authoritative source-artifact store with bounded size/format/hash validation,
  atomic promotion, read-time integrity validation, and quota-safe rejection of
  new work. `PublishedArtworkStateStore` persists the state through the versioned
  authoritative boundary and quarantines invalid records rather than replaying
  them. The durable `ArtworkOperation` journal and publication remain later
  tasks. New tests (`ArtworkProvenanceTests`, `SourceArtifactStoreTests`,
  `PublishedArtworkStateStoreTests`, 75 cases) cover the comparison rules, state
  invariants, every transition, artifact round-trip/integrity/traversal/atomic
  promotion/quota behavior, and authoritative persistence and quarantine.
- 5.3 Implemented the plugin-owned Jellyfin host source adapter in
  `src/ArrTags/Artwork`. `IArtworkSourceReader`/`ArtworkSourceReader` is the
  host-neutral core that reads the current active representation through the
  injectable `IArtworkImageAccess` seam, enforces the V1 unindexed `Primary`
  surface and the `OperationalLimits` source byte and decoded dimension bounds,
  confines accepted containers to PNG and JPEG so uninspected containers fail
  closed, and returns an `ArtworkSourceReadResult` that builds both the
  renderer's `SourceImageInput` and the task 5.2 `ActiveImageIdentity` from one
  bounded read. `JellyfinArtworkImageAccess` resolves the item through
  `ILibraryManager`, reads `BaseItem.GetImageInfo(ImageType.Primary, 0)`,
  converts a non-local image to a local file through
  `ILibraryManager.ConvertImageToLocal` when required, reads a bounded byte copy,
  and observes the image tag and modification time; all `MediaBrowser.*`
  references are confined to that implementation and `src/ArrTags/Rendering`
  stays Jellyfin-free. Jellyfin reports the pre-EXIF-orientation encoded
  dimensions, so `SourceImageDescriptor` derives the true display dimensions from
  the exact bytes with the pinned SkiaSharp codec, matching the renderer's
  `EncodedOrigin` validation. The V1 container confinement closes the
  carried-forward Phase 4 MEDIUM finding without changing the renderer's
  `SourceColorProfile`. New tests (`ArtworkSourceReaderTests`,
  `ArtworkSourceContentTypeTests`, `JellyfinArtworkImageAccessTests`,
  `SourceImageDescriptorTests`, 65 cases with four native-guarded) cover present,
  absent, unsupported-surface, unsupported-container, oversized-byte,
  oversized-dimension, unreadable, hash/length/surface correctness, oriented
  dimensions, supported non-local conversion, and boundary-neutrality checks.
- 5.4 Extended the plugin package so the renderer's managed binding, matching
  Linux native asset, dependency manifest, `build.yaml`, and Skia/font notices
  ship at the plugin folder root. The `PackagePlugin` target now derives
  `SkiaSharp.dll` and the `linux-x64` `libSkiaSharp.so` from the project's
  MSBuild-resolved runtime assets and fails if either is missing;
  `build.yaml` `artifacts` lists `ArrTags.dll`, `SkiaSharp.dll`,
  `libSkiaSharp.so`, and `ArrTags.deps.json` for the unchanged identity and
  `targetAbi: 12.0.0.0`. V1 claims only `linux-x64` (multi-RID packaging is out
  of V1), and no arbitrary system Skia library is loaded. The package was
  installed on the pinned Jellyfin 12.0.0 host, which loaded `SkiaSharp
  3.119.0.0` from the plugin folder and started cleanly, and a replicated
  Jellyfin `PluginLoadContext` over the exact package mapped the plugin-local
  `libSkiaSharp.so` next to `SkiaSharp.dll`. A full image render is not wired
  until tasks 5.5/5.11. New packaging tests live in `PluginPackagingTests`.
- 5.6 Implemented the durable `ArtworkOperation` write-ahead record and its
  authoritative store in `src/ArrTags/Artwork` (ADR-003; data-model sections
  3.10.3 and 3.10.4; architecture section 9). `ArtworkOperation` carries the
  operation id, kind, item/surface subject, generation, ownership/prior/next
  publication tokens, the exact `expectedBeforeIdentity`, the explicit
  source and candidate-after presences, the candidate after content hash, the
  optional observed after identity, the source/derived artifact references, the
  phase, the lifecycle fence, the bounded attempt counter, the redacted
  `lastError`, and the journal timestamps, with `Validate` enforcing every
  documented conditional rule and excluding paths/credentials. The
  `ArtworkOperationPhase` enum matches the data-model table and is documented as
  a durable lower-bound marker; `ArtworkOperationPhases` is the pure phase
  advance helper and `ArtworkOperationFencing` provides the pure generation and
  lifecycle-fence decisions (a disable/uninstall/item-removal fence refuses new
  publication work). `ArtworkOperationStore` persists through the versioned
  authoritative boundary, keyed per item/image surface, validates on write,
  quarantines a valid-envelope-but-invalid payload, fences writes by the
  monotonic generation so stale work cannot overwrite a newer durable record,
  and keeps non-terminal operations out of terminal-provenance retention. The
  store performs no image mutation; task 5.5 drives the ordering. New tests
  (`ArtworkOperationTests`, `ArtworkOperationStoreTests`, 101 cases) cover the
  model invariants, absent/present after target, token bounds and `lastError`
  redaction, every legal/illegal phase transition, the fence decisions,
  restart durability, generation fencing, one-non-terminal-per-subject,
  quarantine, and retention.
- 5.5 Implemented the single-subject durable publication orchestration in
  `src/ArrTags/Artwork` (ADR-002/ADR-003; `docs/architecture.md` section 9 steps
  4-10). `IArtworkImageWriter` is the host-neutral mutation boundary and
  `JellyfinArtworkImageWriter` is its only implementation: it resolves the item
  through `ILibraryManager`, uses the supported `IProviderManager.SaveImage`
  stream overload with the durable derived bytes and `image/png`, and then calls
  `BaseItem.UpdateToRepositoryAsync(ItemUpdateType.ImageUpdate, ...)` exactly like
  the standard item-image controller. It never writes a media-folder poster,
  Jellyfin's image cache, or an item-image path directly, never uses the
  filesystem-path overload that deletes its source, and never uses a URL
  overload; all `MediaBrowser.*` references for publication stay in that one file
  and `src/ArrTags/Rendering` remains Jellyfin-free. `ArtworkPublisher` reads the
  authoritative `PublishedArtworkState`, captures and retains the exact source
  baseline (or an explicit absent baseline) through `IArtworkSourceReader` and
  `SourceArtifactStore`, promotes the validated render output to a durable derived
  artifact, persists a `Prepared` `ArtworkOperation` before any mutation,
  revalidates the before identity immediately before mutation (a mismatch or
  unobservable identity never calls `SaveImage` and marks the operation `Aborted`;
  a previously published session also persists the ownership outcome
  `OwnershipLost`/`OwnershipUnknown`, while an initial capture persists no
  `PublishedArtworkState`), advances durably through `MutationStarted`,
  `RepositoryUpdateStarted`, `VerificationPending`, and `FinalizationPending`,
  and commits the final `PublishedArtworkState` with a new publication token,
  active identity, state revision, and operation id (retaining the source artifact
  and ownership token) before marking the operation `Committed`. Failures and
  uncertainties leave the current artwork unchanged, record a bounded
  non-secret diagnostic, and never mark the operation committed; a pre-existing
  non-terminal or recovery-blocked operation blocks new work. Repeat publication
  verifies the prior publication is still active, reuses the first source artifact
  and ownership token, and issues a new publication token, so an ArrTags output
  is never captured as a new source. The publisher is registered with the existing
  lazy DI pattern and adds no event, queue, or library-scan wiring (Phase 6 drives
  it).
  New tests (`ArtworkPublisherTests`, `JellyfinArtworkImageWriterTests`, 23 cases)
  cover the happy path and durable phase ordering, absent baseline, mismatch and
  unobservable aborts, readback mismatch and update failure, repeat publication,
  ownership-lost and non-terminal blocking, bounded cancellation,
  derived-artifact rejection, state hygiene, the stream overload (not the
  deleting path or URL overload), the normal image update flow, and
  boundary-neutrality.
- 5.7 Implemented the provider-neutral, postcondition-based reconciliation
  service in `src/ArrTags/Artwork` (ADR-003; data-model section 3.10.4;
  architecture section 9). `ArtworkRecoveryDecisions` is the pure 3.10.4
  decision table and `ArtworkReconciler` is the registered invocable boundary:
  it serializes with normal publication through the shared subject gate, reads
  the authoritative state and durable operation (quarantining an invalid
  record), re-observes the item and active image, and delegates execution to the
  publisher. A before-identity match resumes or retries the same deterministic
  operation only under the current generation and lifecycle fence and reads the
  retained derived artifact instead of recapturing the active image; a lifecycle
  fence aborts a prepared publication without mutation. An after-identity match
  ensures the normal item update is persisted and commits the intended final
  state. An observable mismatch records `OwnershipLost` and aborts; an
  unobservable image records `OwnershipUnknown` and leaves the image untouched; a
  confirmed missing item writes an `ItemRemoved` tombstone with no image
  mutation; a durable final state completes the journal without further
  mutation; and invalid state or a missing/corrupt required artifact enters
  `RecoveryBlocked` with no replay or cleanup. Reconciliation deletes no
  artifacts, so an artifact not proven non-active is retained, and the publisher
  now records the logical publication fingerprint and renderer version on the
  operation so an after-match recovery commits without re-rendering. The
  reconciler adds no startup work; Phase 6 owns the event/queue/startup-scan
  wiring. Task 5.9 now executes the guarded restoration in the publisher, so a
  restoration resume is no longer reported as `Deferred`. New tests
  (`ArtworkReconcilerTests`) cover every decision branch, the fence
  abort, resume revalidation, source retention, artifact corruption, the
  item-absent tombstone, the durable-final-state completion, resolution of a
  previously `RecoveryBlocked` operation, guarded restoration execution,
  cancellation, exception containment, DI registration, and boundary-neutrality.
- 5.8 Implemented the provider-neutral single-subject artwork generation
  coordination in `src/ArrTags/Artwork` (architecture section 9; data-model
  sections 3.7-3.8). `ArtworkGenerationCoordinator` composes the host source
  adapter, the renderer, and the durable publisher: it observes the current
  source, builds the `SourceImageInput` and `RenderRequest` from canonical
  inputs, and publishes only a `Rendered` result. An absent source is a no-op; a
  failed source read (unavailable, unsupported, oversized, or oversized
  dimension), a render pass-through (including missing metadata and an
  ineligible match), and a failed render preserve the current usable artwork and
  perform no image mutation, and the renderer or publisher is skipped
  accordingly. Missing metadata and an ineligible match rely on the existing
  ADR-009/ADR-010 renderer pass-through convention. The bounded
  `ArtworkGenerationResult` distinguishes published, no-source/absent,
  source-unavailable, render pass-through, render failed,
  publication-not-completed, blocked, and cancelled with no secret, path, entity,
  source bytes, or artifact. Cancellation is honored and no exception escapes.
  The exact observed source is supplied to the publisher's new-session capture
  through an additive internal overload so the render and the retained provenance
  baseline share one observation, while the public publisher behavior and the
  before-mutation revalidation are unchanged. `IRenderer`
  (`SkiaBadgeRenderer`) and the coordinator are registered with the existing lazy
  DI pattern and no startup work, with no event, queue, or library-scan wiring.
  New tests (`ArtworkGenerationCoordinatorTests`, 23 cases) cover every outcome
  and preservation path with injectable doubles and assert zero image mutation.
- 5.9 Implemented the durable lifecycle fence, the disable/uninstall drain and
  guarded restoration, the supported image-removal primitive, and confirmed
  item-removal tombstoning in `src/ArrTags/Artwork` and
  `src/ArrTags/PluginLifecycle`. `ArtworkLifecycleFenceStore` records the active
  `Normal`/`Disable`/`Uninstall`/`ItemRemoved` fence as authoritative,
  integrity-tagged, atomically written state, and the publisher now refuses new
  publication unless the durable fence is a valid `Normal`. `ArtworkPublisher`
  gained a guarded restoration path that reuses the same durable phases,
  readback, and postcondition commit rules as publication: a present baseline
  restores the retained source through the supported stream `SaveImage` API and
  an absent baseline removes the ArrTags image through the new
  `IArtworkImageWriter.RemoveImageAsync` primitive, which is implemented in
  `JellyfinArtworkImageWriter` with the supported `BaseItem.DeleteImageAsync`
  flow (never a direct media-file or image-cache delete). `ArtworkReconciler`
  now executes a restoration resume instead of deferring it, and its
  `ArtworkRecoveryDecisions` evaluation accepts the currently active fence so a
  disable/uninstall drain aborts an in-flight prepared publication before
  restoration. `ArtworkLifecycleCoordinator` records the fence, reconciles every
  non-terminal operation to a terminal result before restoring, leaves the image
  and its recovery records in place and reports an incomplete result when a
  restoration is blocked, externally changed, or uncertain, and tombstones a
  confirmed item removal with no Jellyfin image call. The drain classifies each
  reconciled operation by its durable phase, so an operation that becomes
  `RecoveryBlocked` (including through a non-throwing source-read failure) is
  reported `Incomplete` rather than resolved; the item-removal result claims a
  tombstone only when the reconciler actually aborted the in-flight operation; and an
  invalid/corrupt fence is preserved as fail-closed rather than overwritten with
  `Normal`. The hosted
  `ArrTagsLifecycleService` resolves the fence at graceful shutdown and performs
  a bounded drain (a plain shutdown is a no-op), and wires the previously no-op
  `ItemRemoved` hint to a tracked, bounded confirmation task. The
  `Plugin.OnUninstalling` hook performs a bounded synchronous drain and never
  throws into the host. The pinned host removes only the versioned install
  folder, not the plugin's relocated `DataFolderPath`, so after a completed drain
  the hook also removes the plugin's own state root (ADR-014). All new services use the existing lazy DI pattern
  with no startup work, and no source artifact or journal record is deleted
  eagerly. New tests (`ArtworkLifecycleTests`, 28 cases, plus focused additions
  to `ArtworkReconcilerTests`, `ArtworkPublisherTests`,
  `LifecycleFoundationTests`, and `StateBoundaryTests`) cover fence durability,
  invalid-fence fail-closed and not-overwritten-by-reset, publication refusal
  under every non-normal fence, present/absent-baseline restoration,
  reconcile-before-restore ordering, blocked/changed/uncertain retention
  (including a recovery-blocked source-read failure reported `Incomplete`),
  bounded cancellation, item-removal confirmation and the invalid-state
  no-tombstone path, the hosted lifecycle wiring, the `Plugin.OnUninstalling`
  lazy resolution and failure containment, bounded deterministic traversal-safe
  `StateRepository.Enumerate`, DI registration, and boundary-neutrality. See
  `docs/architecture.md` section 9 and `docs/data-model.md` section 3.10.
- 5.10 Resolved decision gate DG-8 with `docs/decisions.md` ADR-011 and recorded
  the Jellyfin Enhanced coexistence policy in `docs/architecture.md` section 10.
  ArrTags does not implement automatic duplicate-badge detection, overlap
  suppression, or a dependency on Enhanced internals; Jellyfin Enhanced chooses
  its own overlay placement, so overlap handling is deferred to the user, and
  ArrTags badge output is controlled only by the existing `BadgeMoviePosters`/
  `BadgeEpisodePosters` poster enable flags and the renderer selector
  enablement. Enhanced's Spoiler Guard has no material effect on ArrTags badge
  display, so ArrTags renders its derived badge normally and adds no special
  spoiler/hidden handling. No new configuration knob, production behavior,
  `RenderVersion`, or renderer behavior was added. New tests
  (`EnhancedCoexistenceTests`, 7 cases) assert that the production assembly has
  no Enhanced reference or Enhanced/spoiler/suppression type, that the policy
  surface and the renderer/publication reason enums have no
  spoiler/hidden/duplicate/overlap suppression branch, and that badge
  eligibility and output vary only with the existing ArrTags configuration.
- 5.11 Exercised the supported standard Jellyfin server image response path with
  the pinned 12.0.0 host and no parallel route or response interception
  (ADR-001). New tests in `tests/ArrTags.Tests/JellyfinImageResponseTests.cs`
  load the pinned host `Jellyfin.Api.dll`, construct the real
  `Jellyfin.Api.Controllers.ImageController` with host doubles and a real
  `DefaultHttpContext`, and invoke the actual `GetItemImage`,
  `GetItemImageByIndex`, and `GetItemImage2` actions. They cover the
  unindexed/indexed/path-form `Primary` routes and the server-rendered
  `PhysicalFileResult` (path and content type), the quoted image-tag `ETag`
  and `304` conditional response (quoted/bare `If-None-Match` and
  `If-Modified-Since`), `Cache-Control: public, max-age=31536000, immutable`,
  `Vary`, `Content-Disposition`, DLNA headers, `no-cache` revalidation, the
  size/format plumbing, the pinned never-upscale clamp, and the `404`
  pass-through. A publish-then-read-back case uses the real
  `JellyfinArtworkImageWriter` and serves the result through the real standard
  route, asserting the derived bytes (not the stale source) and an untouched
  original. The task 5.1 `JellyfinImageRouteTests` pin the same host's route
  templates and authorization attributes. A live HTTP round-trip against a
  running Jellyfin server was not performed, and the pipeline was not driven from
  ArrTags generation on a live host because the Phase 6 queue/event wiring does
  not exist yet; the in-process case uses the real ArrTags writer and the real
  pinned `ImageController`.

The plugin:

- Targets Jellyfin 12.0.0 (`net10.0`).
- Builds successfully with 0 warnings.
- Loads successfully on Jellyfin 12.0.0.
- Passes 973 automated tests; 58 additional environment-guarded tests (the task
  4.8 round trip, the task 4.6/4.9 render cases, the task 4.11 golden,
  PNG-contract, cross-runtime, determinism, orientation, and profile cases
  including the non-canonical-golden placeholder, the task 5.1/5.11 host route
  cases, the task 5.3 native source-decode cases, and the task 5.4
  package-content cases) are skipped unless their environment guard is provided.
  With `ARRTAGS_SKIA_COMPAT=1` and the pinned native runtime the full suite
  additionally passes the guarded render cases; adding `ARRTAGS_JELLYFIN_HOST_DIR`
  pointing at the pinned host unskips the fourteen image route/response cases
  (987 passed, 44 skipped, 1031 total), and running `./build.sh package` first
  also unskips the three task 5.4 package-content cases.

Next tasks:

- Phase 6 — Caching, updates & performance (Milestone 6). Phase 5 is complete;
  Phase 6 task 6.1 (keep library event handlers short) is the next task in the
  authoritative execution order.
- Deferred to the testing/release milestone: select and record the second
  explicitly supported non-canonical Linux runtime, produce its golden set under
  `tests/ArrTags.Tests/Goldens/non-canonical/`, and run the ADR-010 tolerant
  cross-runtime comparison.
