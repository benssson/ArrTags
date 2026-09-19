## Project Status

**Current milestone:** Phase 4 — Badge rendering is in progress. Tasks 4.1
(provider-neutral metadata selectors), 4.2 (V1 rendering specification, ADR-009),
4.3 (unknown-versus-confirmed-negative semantics), 4.4 (render request and
result fingerprints), 4.5 (image and text limit enforcement), 4.7 (pinned
SkiaSharp stack, embedded DejaVu Sans Bold 2.37 font, and shipped license
notices), 4.8 (SkiaSharp host-compatibility spike), 4.9 (provider-neutral
renderer service and drawing engine), 4.6 (renderer behavior matrix), and 4.10
(renderer configuration model, snapshot, and fingerprint) are complete.
Milestone 3 is complete (tasks 3.1 through
3.8; acceptance criteria satisfied and Gate 3 met).

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

The plugin:

- Targets Jellyfin 12.0.0 (`net10.0`).
- Builds successfully with 0 warnings.
- Loads successfully on Jellyfin 12.0.0.
- Passes 575 automated tests; 21 additional environment-guarded SkiaSharp
  tests (the task 4.8 round trip, the nine task 4.9 render cases, and the eleven
  task 4.6 behavior-matrix render cases) are skipped unless
  `ARRTAGS_SKIA_COMPAT=1` and the pinned native runtime are provided. With that
  environment the full 596-test suite passes.

Next tasks:

- Phase 4 — Badge rendering. Continue with task 4.11, the ADR-010 golden-image,
  byte-determinism, PNG-contract, and cross-runtime tolerance tests.
