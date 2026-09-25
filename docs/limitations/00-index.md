# ArrTags known limitations and deferred decisions

Canonical register of what ArrTags does not yet do, or has not yet verified.
Each item has a stable ID; open items are in [`open.md`](open.md),
still-current accepted items and verification/packaging limits are in
[`accepted.md`](accepted.md), deliberate scope exclusions are in
[`exclusions.md`](exclusions.md), and resolved items are retained in
[`archive.md`](archive.md). The former `docs/limitations.md` path remains as
a pointer stub.

## V1 success criteria status

`GOALS.md` lists nine success criteria. The live verification task 7.3 and its
superseding task 7.8 exercise them on the pinned Jellyfin `12.0.0` musl host
against the committed mock Sonarr/Radarr fixture.

| # | `GOALS.md` success criterion | Status | Limitation |
| --- | --- | --- | --- |
| 1 | Installs and loads correctly on Jellyfin 12 | Met | — |
| 2 | Sonarr and Radarr configured independently | Met | — |
| 3 | Jellyfin movie/TV item matched to its Arr item | Met | — |
| 4 | Metadata (e.g. quality) retrieved through the API | Met | — |
| 5 | Metadata rendered as a badge on the poster | Met as shipped | Requires the host to supply a compatible SkiaSharp (item F5); no bundled fallback. |
| 6 | Poster updates occur without unnecessary repeated processing | Met for render/publication and for the provider library read within a window | One provider library read per connection serves every work item in the reconciliation window, and the ArrTags-side invalidation trigger set is wired (item F1 resolved, task 11.4); render and publication are fingerprint-gated. Remaining bounds: Sonarr per-series episode reads are O(series) per window and a sparse invalidation window repopulates the whole library (item F1). |
| 7 | Operates correctly alongside Jellyfin Enhanced | Met at the contract level only | Jellyfin Enhanced is not installed on the pinned host (item V3). |
| 8 | Failures do not adversely affect Jellyfin | Met as shipped | — |
| 9 | Buildable and testable reproducibly | Met | Reproducibility is guaranteed only for the pinned SDK (item P1). |

Phase 7 acceptance criteria as recorded in `PLANS.md`:

1. The full test suite passes from a clean checkout — **met** by task 7.1.
2. The packaged plugin loads and operates on the declared Jellyfin 12 ABI —
   **met** by task 7.7.
3. Sonarr and Radarr movie/television scenarios pass with unchanged and changed
   metadata — **met** by task 7.8.
4. Provider, matching, rendering, artwork, cache, and lifecycle failures do not
   adversely affect Jellyfin — **met** by task 7.8.
5. The release artifact and build process are reproducible and documented —
   **met** by task 7.5.

All five Phase 7 acceptance criteria are met. Gate 7 and the Phase 7 phase
review are separate and are not declared by this document.

## Limitation index

| ID | Title | Status | Section |
| --- | --- | --- | --- |
| `F3` | No bounded, secret-free metrics/diagnostic-status surface | Open | [`open.md`](open.md) |
| `F4` | Reconciliation coverage is bounded by `QueueCapacity` | Open | [`open.md`](open.md) |
| `F5` | The renderer depends on a host-supplied SkiaSharp with no bundled fallback | Open | [`open.md`](open.md) |
| `F6` | Version-blind work coalescing can drop a post-save re-render | Open | [`open.md`](open.md) |
| `F7` | An empty resolved badge selection preserves the previous ArrTags badge instead of restoring the original | Open | [`open.md`](open.md) |
| `F8` | The specific render classification is not surfaced in the artwork log | Open | [`open.md`](open.md) |
| `F1` | Provider inventory/catalogue cache | Resolved | [`archive.md`](archive.md) |
| `F2` | A saved configuration change is activated at runtime and re-renders existing posters via the bounded post-save trigger | Resolved | [`archive.md`](archive.md) |
| `V1` | No live Sonarr or Radarr instance | Accepted | [`accepted.md`](accepted.md) |
| `V2` | ADR-010 non-canonical cross-runtime comparison is unselected and unrun | Accepted | [`accepted.md`](accepted.md) |
| `V3` | Jellyfin Enhanced coexistence is contract-level only | Accepted | [`accepted.md`](accepted.md) |
| `V4` | Live `IProviderManager.SaveImage` read-back is manual, not an automated live test | Accepted | [`accepted.md`](accepted.md) |
| `V5` | Live `Plugin.OnUninstalling` drain is not covered by an automated live test | Accepted | [`accepted.md`](accepted.md) |
| `V6` | Host-guarded facts are skipped without the pinned host | Accepted | [`accepted.md`](accepted.md) |
| `P1` | Byte-reproducibility depends on the pinned toolchain | Accepted | [`accepted.md`](accepted.md) |
| `P2` | The shipped assembly has reduced debug metadata for reproducibility | Accepted | [`accepted.md`](accepted.md) |
| `P3` | `PackagePlugin=true` stages but does not write the archive | Accepted | [`accepted.md`](accepted.md) |
| `P4` | SkiaSharp license notices ship although SkiaSharp is no longer redistributed | Accepted | [`accepted.md`](accepted.md) |
| `P5` | Pre-release orphaned state and uninstall drain behavior | Accepted | [`accepted.md`](accepted.md) |
| `P6` | The `v1.1.0` release publication, manifest push, and asset upload are pending | Accepted | [`accepted.md`](accepted.md) |
| `D1` | Webhook `401` response body versus ADR-012 wording | Accepted | [`accepted.md`](accepted.md) |
| `D2` | Optional hardening not done: `ArtworkSubjectGate` has no idle eviction | Accepted | [`accepted.md`](accepted.md) |
| `RR-1` | Phases 1-3 have no independent phase-review records | Accepted | [`accepted.md`](accepted.md) |
| `RR-15` | Mock fixture API keys appear in tracked documentation | Accepted | [`accepted.md`](accepted.md) |
| `SEC-2` | State-root resolution falls back to a shared temp path (LOW, open) | Accepted | [`accepted.md`](accepted.md) |
| `SEC-3` | The state-envelope integrity hash does not cover envelope metadata (INFORMATIONAL, noted) | Accepted | [`accepted.md`](accepted.md) |
| `SEC-4` | Authentication error bodies carry framework `application/problem+json` detail (INFORMATIONAL, noted) | Accepted | [`accepted.md`](accepted.md) |
| `SEC-5` | Secrets persist at rest only in Jellyfin's plugin configuration XML, and ArrTags logging is bounded and redacted (INFORMATIONAL, noted) | Accepted | [`accepted.md`](accepted.md) |
| `SEC-6` | The `ArtworkSubjectGate` process-lifetime bound (INFORMATIONAL, noted) | Accepted | [`accepted.md`](accepted.md) |
| `SEC-7` | A declared non-JSON content type is now rejected with a bounded 400 (INFORMATIONAL, noted) | Accepted | [`accepted.md`](accepted.md) |
| `SEC-8` | No per-client authentication throttling on the anonymous webhook route (INFORMATIONAL, noted) | Accepted | [`accepted.md`](accepted.md) |
| `SEC-9` | Configuration-rejection activity-log surfacing is a new bounded, secret-free administrator-visible surface (INFORMATIONAL, noted) | Accepted | [`accepted.md`](accepted.md) |
| `SEC-10` | Configured provider API keys and the webhook shared secret are not length-bounded (LOW, accepted) | Accepted | [`accepted.md`](accepted.md) |
| `SEC-11` | Library-scope entries are not count/length-bounded or validated as library identifiers (LOW, accepted) | Accepted | [`accepted.md`](accepted.md) |
| `SEC-12` | Configured base-URL user information is not rejected (INFORMATIONAL, accepted) | Accepted | [`accepted.md`](accepted.md) |
| `P12R-F1` | The absolute default output-fingerprint pin lives only in the forced-native golden path (MEDIUM, accepted) | Accepted | [`accepted.md`](accepted.md) |
| `V11-G1` | The badge value allowlist (Goal B) was not exercised live (LOW, accepted) | Accepted | [`accepted.md`](accepted.md) |
| `V11-G2` | Phase-review LOW residuals (accepted, tracked) | Accepted | [`accepted.md`](accepted.md) |
| `V11-G3` | Carried release LOWs from the `1.0.1.0` audit (accepted) | Accepted | [`accepted.md`](accepted.md) |
| `V11-G4` | Stale "owned by task 14.3" pending phrasing in a few current-state surfaces (LOW, accepted) | Accepted | [`accepted.md`](accepted.md) |
