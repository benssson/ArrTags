# Open limitations

Open items are those ArrTags does not yet do, or has not yet verified, and that a
later release may resolve. Resolved items are retained in
[`archive.md`](archive.md) with their resolving change, accepted items and
verification bounds are in [`accepted.md`](accepted.md), deliberate scope
exclusions are in [`exclusions.md`](exclusions.md), and the canonical register is
[`00-index.md`](00-index.md).

### F9. A cancelled artwork mutation seals the subject into a permanently blocked state (HIGH, open)

An `OperationCanceledException` raised by the host image mutation, the item
update, or the post-mutation verification is handled by writing two **terminal**
records: the durable operation is advanced to `RecoveryBlocked` and the published
state to `RestoreBlocked`. Because `RecoveryBlocked` is terminal, every existing
recovery path is then unreachable — the recovery gate returns `AlreadyTerminal`
and the restoration resume requires the state to be `RestorePending`, which the
same handler overwrote. Cancellation is a routine condition (host restart,
bounded drain timeout, per-request timeout), not a subject defect, so the most
common cause of interruption produces the only unrecoverable outcome. This
violates the `GOALS.md` reliability goals that Jellyfin restarts and plugin
reloads are handled safely and that partially completed poster processing does
not leave media in an unusable state.

- Precondition: a subject with an owned published session and an intact retained
  source baseline, whose ADR-024 restoration is attempted (an empty resolved
  selection) or whose publication is interrupted, while the host call is
  cancelled.
- Behaviour: the cancellation is converted into a terminal outcome rather than a
  resumable one. The plugin then never retries the mutation and never re-observes
  the subject. The poster keeps whatever image was active when the mutation was
  cancelled.
- Observed: on the origin host, item `23f05d15d184fa94d6ddf5f56dde6f5c`, surface
  `Primary`, 2026-09-24T21:58:55Z. The operation record is `Kind: Restoration`,
  `Phase: 7` (`RecoveryBlocked`), `Attempt: 0`,
  `LastError: "The restoration mutation was cancelled."`, `ObservedAfter: null`;
  the state record is `State: 6` (`RestoreBlocked`) with `SourcePresence: Present`
  and a retained source artifact that passes integrity validation. The 11,578,522-byte
  badge published earlier that day remained the active image and no trigger could
  remove it. Recovery required hand-deleting the state record after manually
  restoring the clean poster.
- Consequence: the subject is permanently out of automatic scope. The only v1.2
  remedy is an out-of-band edit of the authoritative state tree, which is
  undocumented and, done in the wrong order, captures the badged image as the new
  baseline and makes the badge unrecoverable.
- Evidence: `src/ArrTags/Artwork/ArtworkPublisher.cs:735-743` (restoration),
  `:380` and `:414` (publication analogues);
  `src/ArrTags/Artwork/ArtworkOperationPhases.cs:50-55`;
  `src/ArrTags/Artwork/ArtworkRecoveryGate.cs:84-91`;
  `src/ArrTags/Artwork/ArtworkPublisher.cs:849` and `:892-894`; the
  `docs/implementation/planning/v1.3.json` origin investigation; ADR-030.
- Related: `V12-F7-1` records the same structural blocking for the
  host-re-adoption path and is **accepted**; this item covers the distinct
  cancellation path, which is not accepted and is not covered by `V12-F7-1`.
- Promoted to v1.3 by `docs/planning/v1.3.md` (G10, DG-23). Resolved by Phase 22.

### F10. Artwork skip, block, and suppressed-selection outcomes are not surfaced in any log or counter (HIGH, open)

Two bounded, non-secret reasons are computed at the artwork boundary and then
discarded. `ArtworkRegenerationPlanner.Decide` returns one of six bounded skip
reasons and emits nothing; because a skip performs no generation attempt it also
produces no artwork log line. `ArtworkPublisher.WriteRestoreBlocked` writes the
terminal blocked state without emitting its bounded reason. The ADR-025
diagnostics snapshot counts render **failures** only, so pass-throughs, planner
skips, and blocked states are not represented.

- Precondition: any subject that is skipped by the regeneration planner, sealed in
  a blocked ownership or restore state, or resolves an empty badge selection
  because a configured per-selector allowlist suppressed every value.
- Behaviour: the subject produces no log record and no counter movement. A blocked
  subject is indistinguishable from a correct no-op, and an allowlist-suppressed
  subject is indistinguishable from a provider read failure.
- Observed: on the origin host, three independent subject classes were silent —
  one `OwnershipLost`, one `RestoreBlocked` (F9), and roughly 645 subjects whose
  selection was suppressed by the configured `Quality` allowlist. Diagnosing one
  affected subject required reading `cache/metadata-state/*.json` and
  `authoritative/artwork-state/*.json` and hand-correlating them; two intermediate
  hypotheses during that investigation were wrong before the state records were
  read. The ADR-020 clause 6 throttle (five records per category and event per
  minute) additionally means a whole-library run cannot answer the question from
  logs even when the reasons were emitted.
- Consequence: an operator cannot determine, from supported surfaces, why an item
  has no badge or why a poster was not corrected. This is the gap that made the
  origin session require manual state edits.
- Evidence: `src/ArrTags/Artwork/ArtworkRegenerationPlanner.cs:69`, `:74`, `:80`,
  `:90`, `:110`, `:128`; `src/ArrTags/Artwork/ArtworkPublisher.cs:900-912`;
  `src/ArrTags/Diagnostics/DiagnosticsMetrics.cs:168-189`;
  `src/ArrTags/Logging/LogThrottle.cs:23`; the
  `docs/implementation/planning/v1.3.json` origin investigation; ADR-031.
- Relationship to F8: F8 (resolved in v1.2) made the bounded render classification
  appear on the artwork log line, so two different *generation-attempt* outcomes
  became distinguishable. It did not cover a decision not to attempt generation,
  and it added no counter. This item is the remaining, wider gap.
- Promoted to v1.3 by `docs/planning/v1.3.md` (G11, DG-24). Resolved by Phase 23.
