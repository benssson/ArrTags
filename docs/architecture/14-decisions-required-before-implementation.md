# 14. Decisions required before implementation

The following are intentionally not guessed by this architecture:

1. Exact Jellyfin 12 patch, package versions, target framework, and plugin
   manifest `targetAbi`.
2. Initial supported item/image types and whether series/season posters use an
   explicit aggregate policy or remain disabled. Resolved by ADR-006: V1 badge
   surfaces are Movie and Episode posters; Series/Season are structural only and
   aggregation remains post-V1.
3. Exact badge fields, text truncation, placement, color/contrast rules, output
   format, and requested-size policy. Resolved by ADR-009: V1 renders bounded
   provider-neutral badges on Movie and Episode Primary posters, emits PNG at
   source dimensions, and passes through on unknown or failed input.
4. Whether episode matching permits number fallback for all libraries or only
   validated display-order cases. Resolved by ADR-007: number fallback is limited
   to regular single episodes after the series match; season zero specials,
   multi-episode spans, and absolute/scene numbering are excluded.
5. Whether configured path mappings are needed and how they are represented.
   Resolved by ADR-008: path fallback is deferred out of V1, so V1 has no path
   mapping configuration or path matching rule.
6. Default queue, concurrency, image-size, cache, timeout, retry, and stale
   state limits. Resolved for the foundation by ADR-004; the accepted values are
   in section 12.
7. Webhook endpoint exposure, replay policy, and shared-secret administration
   flow. Resolved by ADR-012: an anonymous plugin route authenticated by the
   `X-ArrTags-Webhook-Secret` header through the constant-time versioned webhook
   lease, bounded payload and tolerant parsing, bounded coalescing intake,
   idempotent replay handling, bounded provider-record-to-Jellyfin resolution
   into the existing work-hint path, and an administrator flow that reuses the
   ADR-005 `WebhookSecret` slot. Secret persistence and versioned access remain
   resolved by ADR-005.
8. Jellyfin Enhanced duplicate-badge defaults and Spoiler Guard behavior.
   Resolved by ADR-011: ArrTags adds no automatic duplicate/overlap suppression,
   no Enhanced-internals dependency, and no special spoiler/hidden handling; the
   existing poster and selector enable flags are the user's control surface.
9. Supported live Sonarr/Radarr release ranges and optional-field compatibility.
   Resolved by ADR-013: supported ranges are Sonarr 3.x-4.x and Radarr 3.x-6.x on
   `/api/v3`, absent optional fields map to explicit unknown values, and a
   malformed or missing required field fails closed as `ProviderIncompatible`
   with no version-number gate.

These decisions must be recorded in `docs/decisions.md` or
`docs/implementation-readiness.md` and reflected in a future architecture
revision before they become implementation assumptions. Item 1 (the pinned
Jellyfin `12.0.0` / `net10.0` / `targetAbi: 12.0.0.0` compatibility target) is
resolved in `docs/implementation-readiness.md`. Item 2 (V1 badge surfaces and
the library scope identifier) is resolved by ADR-006. Item 4 (episode numbering)
is resolved by ADR-007. Item 5 (path fallback) is resolved by ADR-008. Item 6
(foundation operational limits) is resolved by ADR-004, with the accepted values
recorded in section 12. Item 8 (Jellyfin Enhanced coexistence) is resolved by
ADR-011. Item 9 (supported provider release ranges and optional-field
compatibility) is resolved by ADR-013. The credential persistence and access boundary is resolved by ADR-005.
Item 7 (webhook exposure, authentication, payload limits, replay handling, rate
policy, and administration flow) is resolved by ADR-012, with the accepted
payload bound recorded in section 12.
The remaining items stay open and are tracked by the decision gates in
`PLANS.md`.
