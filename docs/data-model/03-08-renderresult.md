# 3.8 RenderResult

**Purpose:** The result of rendering or the safe decision to leave the current
active artwork unchanged.

| Field | Type | Required | Field Ownership | Notes |
| --- | --- | --- | --- | --- |
| `status` | `Rendered`, `PassThrough`, `NotEligible`, `CacheHit`, or `Failed` | Yes | Generated | Failure and ineligibility are normal domain outcomes, not Jellyfin failures. |
| `outputArtifact` | Image bytes or bounded artifact reference | Optional | Generated/cache | Present for completed render work; may be published as Jellyfin active artwork through the supported image API. |
| `contentType` | MIME type | Optional | Generated/Jellyfin | Must describe the returned artifact. |
| `width` / `height` | Integer dimensions | Optional | Generated/Jellyfin | Actual output dimensions. |
| `outputFingerprint` | Opaque fingerprint | Yes | Generated | Covers source, metadata, definitions, render values, and renderer version. |
| `etag` | Internal artifact identity, if useful | Optional | Generated | Jellyfin owns the validator for the published item image. |
| `createdAt` | Timestamp | Yes | Generated | Creation time of this result/artifact. |
| `passThroughReason` | Safe reason code | Optional | Generated | Used for diagnostics and metrics without exposing provider secrets. |
| `expiresAt` | Timestamp | Optional | Cache policy/generated | Applies when the result is cacheable. |

**Phase 4 implementation note:** The provider-neutral renderer service (task
4.9, ADR-010) implements the 3.6-3.8 contract in `src/ArrTags/Rendering`. The V1
`BadgeDefinition` snapshot is deliberately minimal (selector, enabled flag, and
one bounded `{value}` template); task 4.10 persists exactly those two
user-adjustable dimensions per selector, plus contrast-validated palette
overrides, into `RendererConfiguration` and maps them back through
`RendererConfigurationResolver`. The remaining definition, style, placement, and
visibility fields above stay code-owned per ADR-009/ADR-010: V1 does not expose
user-selectable format, color space, alpha, font, geometry, text-limit, or
version values, so they are not part of the persisted configuration.
`BadgeDefinitionResolver` applies the bounded templates on top of the existing
`BadgeSelectorResolver`, so semantic priority remains code-owned. `RenderResult`
has three bounded variants: `Rendered` (complete PNG artifact plus content type,
oriented dimensions, output hash, and output fingerprint), `PassThrough`, and
`Failed` with exactly one safe reason code; `NotEligible` and `CacheHit` remain
conceptual policy states handled outside the renderer. `SourceImageInput` is
the bounded, read-only bytes descriptor defined by ADR-010; the Jellyfin host
adapter that supplies it remains Phase 5 work. `PluginConfigurationSnapshot`
exposes the validated definitions, the effective `RenderOutputPolicy`, and the
secret-free renderer configuration fingerprint that feeds
`RenderRequest.configurationFingerprint`.

**v1.1 task 12.1 implementation note:** `BadgeSelectorConfiguration` and the
resolved `BadgeDefinition` carry the bounded `AllowedValues` allowlist (ADR-017).
`RendererConfigurationResolver.ResolveDefinitions` maps the persisted
`BadgeSelectorConfiguration.AllowedValues` into `BadgeDefinition.AllowedValues`,
which is an immutable, trimmed copy. The value is bounded and validated in
`RendererConfiguration.Validate` with secret-free messages. A non-empty resolved
allowlist is included in `RendererConfigurationFingerprint`, which normalizes
case and entry order so that case-only or order-only allowlist changes are
identity-neutral; an empty allowlist means no restriction and adds nothing to
the fingerprint (identity-neutral relative to a non-empty allowlist), though the
coordinated v1.1 schema advance still changes the default configuration
fingerprint. Task 12.1 does not yet apply the filter to rendering; the renderer
filtering order is task 12.2.

**v1.1 task 12.3 implementation note:** `RendererConfiguration` gains the global
`Position` (`BadgePosition`: `BottomLeft` default, `TopLeft`, `TopRight`,
`BottomRight`, `Center`) and `Size` (`BadgeSize`: `Medium` default, `Small`,
`Large`) settings (ADR-019 clauses 1-5 and 7). `RendererConfiguration.Validate`
rejects an undefined enum value with a bounded, secret-free message, and
`RendererConfigurationResolver.ResolveOutputPolicy` carries both onto the
resolved `RenderOutputPolicy` (falling back to the code-owned default for a
tolerantly read undefined value). `BadgeGeometry.ComputeEffectiveScale` computes
`clamp(width / 1000, 0.5, 4.0) * sizeFactor` (`Small` 0.75, `Medium` 1.0,
`Large` 1.5), clamped so the scaled outer inset leaves a positive safe area; the
layout engine then shortens or omits rather than overflowing. `BadgeLayoutEngine`
positions the rail per anchor (rows stack away from the anchored edge and align
to the anchored side; `Center` centers both axes) and places the status pill
top-right except when the anchor is `TopRight`, then top-left. The 24-pixel
scaled inset and all ADR-009 safe-area, text-limit, contrast, opacity, and
determinism guarantees are unchanged. A non-default position or size is included
in both `RendererConfigurationFingerprint` and
`RenderFingerprint.ComputeOutputFingerprint`; the default position and size are
identity-neutral relative to other placement values (the default reproduces the
V1 output, so the default PNG bytes are unchanged), but the coordinated v1.1
version advance changes the default configuration and output fingerprints.
Placement and size are global only; there is no per-selector placement. The v1.1
allowlist and placement changes share one
coordinated advance: `RendererConfiguration.CurrentSchemaVersion` advanced from 1
to 2 and `RenderVersion.CurrentRendererVersion` advanced from 2 to 3, and the
committed goldens were regenerated with no writer or auto-approval path (the nine
default-configuration PNGs are byte-unchanged while their output fingerprints
advance with the renderer version, and anchor/size goldens were added).

**v1.2 task 18.3 implementation note (ADR-027; extends the v1.1 task 12.3 note above).** The v1.1 text above remains the historical record of the three-preset v1.1 state. ADR-027 adds `BadgeSize.ExtraLarge = 3` with the code-owned factor `2.0` relative to `Medium`, so the resolved `RenderOutputPolicy.Size` can carry the fourth preset. `BadgeGeometry.ComputeEffectiveScale` still computes `clamp(width / 1000, 0.5, 4.0) * sizeFactor` and still clamps the scaled inset so the badge fits the safe area, so an Extra Large badge that cannot fit falls back to the existing shorten/omit behavior rather than overflowing; `BadgeLayoutEngine`, the text-limit and contrast rules, and the default `Medium` output are unchanged. The `RendererConfiguration.Validate` `Enum.IsDefined` check accepts the new member, and a non-default size is included in both `RendererConfigurationFingerprint` and `RenderFingerprint.ComputeOutputFingerprint`, so selecting Extra Large republishes only the items whose policy selects it. The version decision stands (ADR-027 clause 4): `RendererConfiguration.CurrentSchemaVersion` stays `2` and `RenderVersion.CurrentRendererVersion` stays `3`; the existing committed Small/Medium/Large goldens remain byte-identical, and two deterministic Extra Large goldens (`bottom-left-extra-large`, `full-rail-extra-large`) were added (task 18.2).

**Phase 5 implementation note:** The task 5.8
`ArtworkGenerationCoordinator` composes the 3.7-3.8 request/result with the
source adapter and publisher. It maps the renderer's bounded outcome into an
`ArtworkGenerationOutcome` that distinguishes published, absent source,
source-unavailable, render pass-through, render failure, publication not
completed, blocked, and cancelled; only a published outcome changes the active
artwork. Missing metadata and an ineligible match remain renderer pass-through
states and produce no badge and no mutation. The coordinator supplies the exact
observed source to the publisher so the retained provenance baseline matches the
render source.

**v1.2 task 20.1 implementation note (ADR-024; extends the Phase 5 note above).**
The Phase 5 text above remains the historical task 5.8 record. An owned
`Published` session whose resolved selection is empty is a restore obligation,
not a preserve: the `ArtworkGenerationCoordinator` drives the internal
`ArtworkPublisher.RestoreAsync` under the current durable lifecycle fence through
the guarded, crash-recoverable `ArtworkOperation` protocol, so the retained
source baseline is restored or the ArrTags image is removed when the baseline was
absent. `ArtworkGenerationOutcome` adds `Restored` for the completed
restoration/removal — besides `Published`, the only outcome that changes the
active artwork — and `ArtworkGenerationResult` carries the bounded
`ArtworkReconciliationOutcome` (`Completed` for a verified restore/removal and
otherwise the bounded refusal or uncertainty classification) plus the
`NoDisplayableValue` render pass-through reason that drove the obligation. The
renderer adds the distinct `RenderPassThroughReason.NoFittingBadge` for a
non-empty selection whose layout is empty; that reason and every other
pass-through reason preserve the current artwork, and a non-owned item is never
mutated. The change is not a render output change: `RenderVersion` and the
renderer-configuration schema do not advance, and no fingerprint input changes.
