# Architecture (index)

The architecture is split by section; section numbers are preserved in each
file heading. This index is the section map.

**Last reviewed against:**
- Jellyfin 12.x
- Sonarr v4.x
- Radarr v5.x

**Purpose:** Canonical architectural design before implementation begins.

This document is the authoritative design for ArrTags. The companion research
documents contain API details, source evidence, and alternatives; they do not
override the decisions recorded here.


## Sections

| § | Section | File |
| --- | --- | --- |
| 1 | Scope and principles | [`01-scope-and-principles.md`](01-scope-and-principles.md) |
| 2 | Goals and non-goals | [`02-goals-and-non-goals.md`](02-goals-and-non-goals.md) |
| 3 | System architecture | [`03-system-architecture.md`](03-system-architecture.md) |
| 4 | Plugin components and boundaries | [`04-plugin-components-and-boundaries.md`](04-plugin-components-and-boundaries.md) |
| 5 | Plugin lifecycle and registration | [`05-plugin-lifecycle-and-registration.md`](05-plugin-lifecycle-and-registration.md) |
| 6 | Configuration and persisted state | [`06-configuration-and-persisted-state.md`](06-configuration-and-persisted-state.md) |
| 7 | Sonarr and Radarr integration | [`07-sonarr-and-radarr-integration.md`](07-sonarr-and-radarr-integration.md) |
| 8 | Reconciliation and update flow | [`08-reconciliation-and-update-flow.md`](08-reconciliation-and-update-flow.md) |
| 9 | Persisted artwork rendering | [`09-persisted-artwork-rendering.md`](09-persisted-artwork-rendering.md) |
| 10 | Jellyfin Enhanced coexistence | [`10-jellyfin-enhanced-coexistence.md`](10-jellyfin-enhanced-coexistence.md) |
| 11 | Failure, consistency, and security policy | [`11-failure-consistency-and-security-policy.md`](11-failure-consistency-and-security-policy.md) |
| 12 | Performance and operational limits | [`12-performance-and-operational-limits.md`](12-performance-and-operational-limits.md) |
| 13 | Testing and validation gates | [`13-testing-and-validation-gates.md`](13-testing-and-validation-gates.md) |
| 14 | Decisions required before implementation | [`14-decisions-required-before-implementation.md`](14-decisions-required-before-implementation.md) |
| 15 | Supporting research | [`15-supporting-research.md`](15-supporting-research.md) |
| 16 | Implementation sequencing | [`16-implementation-sequencing.md`](16-implementation-sequencing.md) |
