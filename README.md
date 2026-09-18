## Project Status

**Current milestone:** Phase 4 — Badge rendering is in progress. Tasks 4.1
(provider-neutral metadata selectors), 4.2 (V1 rendering specification, ADR-009),
4.3 (unknown-versus-confirmed-negative semantics), 4.4 (render request and
result fingerprints), 4.5 (image and text limit enforcement), and 4.7 (pinned
SkiaSharp stack, embedded DejaVu Sans Bold 2.37 font, and shipped license
notices) are complete.
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

The plugin:

- Targets Jellyfin 12.0.0 (`net10.0`).
- Builds successfully with 0 warnings.
- Loads successfully on Jellyfin 12.0.0.
- Passes 438 automated tests.

Next tasks:

- Phase 4 — Badge rendering. Continue with the ADR-010 renderer implementation
  tasks in the authoritative execution order 4.8, 4.9, 4.6, 4.10, 4.11,
  beginning with task 4.8 (the SkiaSharp host-compatibility spike). Task 4.6
  (renderer behavior tests) follows the renderer implementation and assets it
  exercises; it was reordered after task 4.9.
