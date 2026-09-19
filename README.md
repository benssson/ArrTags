## Project Status

**Current milestone:** Phase 5 — Jellyfin artwork integration is in progress.
Task 5.1 (confirm the exact supported Jellyfin 12.0.0 item-image publication ABI
and route variants) is complete; the remaining Phase 5 tasks are not started.
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

The plugin:

- Targets Jellyfin 12.0.0 (`net10.0`).
- Builds successfully with 0 warnings.
- Loads successfully on Jellyfin 12.0.0.
- Passes 604 automated tests; 45 additional environment-guarded tests (the task
  4.8 round trip, the task 4.6/4.9 render cases, the task 4.11 golden,
  PNG-contract, cross-runtime, determinism, orientation, and profile cases
  including the non-canonical-golden placeholder, and the task 5.1 host route
  cases) are skipped unless their environment guard is provided. With
  `ARRTAGS_SKIA_COMPAT=1` and the pinned native runtime the full 672-test suite
  passes 666 with 6 skips (the five task 5.1 route cases and the non-canonical
  golden placeholder); adding `ARRTAGS_JELLYFIN_HOST_DIR` pointing at the pinned
  host passes 671 with only the non-canonical golden skip.

Next tasks:

- Phase 5 — Jellyfin artwork integration (Milestone 5). Task 5.1 is complete;
  the next task in the authoritative Phase 5 execution order is 5.2
  (source-artwork provenance and guarded restoration state).
- Deferred to the testing/release milestone: select and record the second
  explicitly supported non-canonical Linux runtime, produce its golden set under
  `tests/ArrTags.Tests/Goldens/non-canonical/`, and run the ADR-010 tolerant
  cross-runtime comparison.
