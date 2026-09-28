# Architecture decisions

One ADR per file. ADR text is a historical decision record: do not rewrite
it; record a divergence or a superseding ADR instead. The former
`docs/decisions.md` path remains as a pointer stub.

| ADR | Title | Status | File |
| --- | --- | --- | --- |
| ADR-001 | Persisted Derived Poster Artwork | Accepted | [`ADR-001.md`](ADR-001.md) |
| ADR-002 | Guarded Artwork Ownership and Restoration | Accepted (restoration trigger set extended by ADR-024 (v1.2)) | [`ADR-002.md`](ADR-002.md) |
| ADR-003 | Crash-Recoverable Artwork Publication | Accepted (restoration trigger set extended by ADR-024 (v1.2)) | [`ADR-003.md`](ADR-003.md) |
| ADR-004 | Foundation Operational Limits and Defaults | Accepted | [`ADR-004.md`](ADR-004.md) |
| ADR-005 | Versioned Secret Access Boundary | Accepted | [`ADR-005.md`](ADR-005.md) |
| ADR-006 | V1 Badge Surfaces and Library Scope Identifier | Accepted | [`ADR-006.md`](ADR-006.md) |
| ADR-007 | V1 Episode Numbering Policy for Number Fallback | Accepted | [`ADR-007.md`](ADR-007.md) |
| ADR-008 | V1 Path Fallback Deferred | Accepted | [`ADR-008.md`](ADR-008.md) |
| ADR-009 | V1 Badge Rendering Specification | Accepted (the geometry, placement, and scaling-model clauses are superseded by ADR-019 (v1.1); the empty-selection pass-through clause is superseded by ADR-024 (v1.2) for the owned-session case) | [`ADR-009.md`](ADR-009.md) |
| ADR-010 | V1 Renderer Implementation Contract | Accepted (partially superseded by ADR-015: the SkiaSharp library | [`ADR-010.md`](ADR-010.md) |
| ADR-011 | Jellyfin Enhanced Coexistence Policy | Accepted | [`ADR-011.md`](ADR-011.md) |
| ADR-012 | Inbound Arr Webhook Boundary | Accepted | [`ADR-012.md`](ADR-012.md) |
| ADR-013 | Supported Sonarr/Radarr Release Ranges and Optional-Field Compatibility Policy | Accepted | [`ADR-013.md`](ADR-013.md) |
| ADR-014 | Plugin State Root Outside Jellyfin's Plugins Directory | Accepted | [`ADR-014.md`](ADR-014.md) |
| ADR-015 | Host-Provided SkiaSharp Runtime for the Renderer | Accepted (supersedes the renderer-bundling parts of ADR-010) | [`ADR-015.md`](ADR-015.md) |
| ADR-016 | Dashboard Settings UI and Runtime Configuration Activation | Accepted (v1.1; settings-page contract amended by ADR-028 (v1.2)) | [`ADR-016.md`](ADR-016.md) |
| ADR-017 | Badge Value Allowlist | Accepted (v1.1) | [`ADR-017.md`](ADR-017.md) |
| ADR-018 | Provider Inventory Cache and Library-Refresh-Driven Reconciliation | Accepted (v1.1) | [`ADR-018.md`](ADR-018.md) |
| ADR-019 | Configurable Badge Size and Placement | Accepted (v1.1; clause 3 extended by ADR-027 (v1.2); supersedes the code-owned geometry and placement clauses of ADR-009 and the corresponding renderer-configuration clauses of ADR-010) | [`ADR-019.md`](ADR-019.md) |
| ADR-020 | Plugin Logging and Configurable Verbosity | Accepted (v1.1; clause 4 amended by ADR-026 (v1.2)) | [`ADR-020.md`](ADR-020.md) |
| ADR-021 | Administrator-Visible Configuration-Rejection Surfacing | Accepted (v1.1) | [`ADR-021.md`](ADR-021.md) |
| ADR-022 | Successive-Run Reconciliation Coverage | Accepted (v1.2; clause 3 amended by ADR-029 (v1.2)) | [`ADR-022.md`](ADR-022.md) |
| ADR-023 | Version-Aware Post-Save Re-Render | Accepted (v1.2) | [`ADR-023.md`](ADR-023.md) |
| ADR-024 | Empty-Selection Artwork Restoration | Accepted (v1.2) | [`ADR-024.md`](ADR-024.md) |
| ADR-025 | Read-Only Diagnostics Status Surface | Accepted (v1.2) | [`ADR-025.md`](ADR-025.md) |
| ADR-026 | Human-Readable Log Subject Identifier | Accepted (v1.2) | [`ADR-026.md`](ADR-026.md) |
| ADR-027 | Extra Large Badge Size | Accepted (v1.2) | [`ADR-027.md`](ADR-027.md) |
| ADR-028 | Limit Display Units and Restart-Required Settings Surfacing | Accepted (v1.2) | [`ADR-028.md`](ADR-028.md) |
| ADR-029 | Identity-Anchored Reconciliation Cursor (Amendment to ADR-022 Clause 3) | Accepted (v1.2) | [`ADR-029.md`](ADR-029.md) |
