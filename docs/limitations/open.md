# Open limitations

## Functional and operational limitations (deferred, not implemented)

### F3. No bounded, secret-free metrics/diagnostic-status surface

Queue depth, provider health, matching, cache, rendering, and stale-data
counters exist internally, but there is no bounded, secret-free user-facing or
diagnostic status surface.

- Evidence: `docs/architecture/12-performance-and-operational-limits.md`
  (documented as an open limitation here); `PLANS.md` Post-V1 Backlog.
- Consequence: operators have no supported in-product view of queue depth or
  provider health. Architecture section 12 states this as a recommendation, not
  a hard V1 gate.

### F4. Reconciliation coverage is bounded by `QueueCapacity`

The scheduled and post-scan scopes are enumerated from the start of a
deterministic order and the queue drops overflow, so a single run over a scope
larger than `QueueCapacity` (default 512) covers only a bounded prefix.
Successive runs re-cover the same prefix rather than advancing; there is no
persisted enumeration cursor, stale/unknown-only enqueue, or direct pipeline
drive.

- Evidence: Phase 6 review MEDIUM item; `docs/architecture/08-reconciliation-and-update-flow.md`;
  `PLANS.md` Post-V1 Backlog.
- Consequence: for a scope larger than `QueueCapacity`, some items are not
  reconciled by a single run and successive runs do not guarantee full
  coverage. Event, webhook, and per-item triggers are not affected.

### F5. The renderer depends on a host-supplied SkiaSharp with no bundled fallback

V1 compiles against the pinned `SkiaSharp`/`SkiaSharp.NativeAssets.Linux`
`3.119.4` but ships no renderer runtime and takes the managed assembly and
native library from the Jellyfin host (ADR-015, superseding the bundling parts
of ADR-010).

- Evidence: task 7.8 worker report and ADR-015; `docs/architecture/09-persisted-artwork-rendering.md`.
- Consequence: a host that does not provide a compatible SkiaSharp would make
  rendering fail closed (pass-through/preserve current artwork) rather than use
  a bundled copy. Validated only on the pinned Jellyfin `12.0.0`
  `linux-musl-x64` host; the plugin makes no RID-specific claim and does not
  distinguish musl from glibc.

### F6. Version-blind work coalescing can drop a post-save re-render

A configuration save activates the new snapshot (`Plugin.UpdateConfiguration` ->
`ConfigurationSnapshotService.TryReplace`) and then requests the bounded
post-save reconciliation, which enqueues a work hint carrying the new
configuration version for each eligible item. The queue key is the item,
connection, and image surface and deliberately excludes the configuration
version, and the queue coalesces any hint whose key is already pending or in
flight. A new-version hint for an item that was already pending or in flight
under the previous version is therefore dropped, and the outstanding item then
discards itself at processing time as a stale basis and is completed without
re-enqueueing. The item keeps its previously published artwork (rendered under
the old settings) until a later trigger (library event, webhook, post-scan,
post-save, or scheduled run) enqueues it with the current version.

- Evidence: code trace of `src/ArrTags/Plugin.cs` (activate then request
  reconciliation), `src/ArrTags/Updates/WorkItemKey.cs` (the key excludes the
  configuration version), `src/ArrTags/Updates/LibraryWorkQueue.cs` (coalesce a
  key already pending or in flight), `src/ArrTags/Reconciliation/MetadataReconciliationProcessor.cs`
  (version-mismatch discard) and `src/ArrTags/Updates/ArtworkPublishingWorkItemProcessor.cs`
  (artwork-stage version guard), and `src/ArrTags/Updates/WorkProcessingResult.cs`
  (a discard is a non-retryable completed outcome); operator analysis,
  2026-09-25.
- Consequence: immediately after a settings change, an item whose work happened
  to be pending or in flight at save time is not re-rendered by the post-save
  trigger and updates only on the next trigger that admits it. No work or
  artwork is lost and the current artwork is preserved. It is distinct from F4:
  F4 is bounded coverage of a scope larger than `QueueCapacity`, whereas F6 is a
  missed re-render for an item the trigger did reach. Candidate fixes (upgrade
  the pending item on coalesce, re-enqueue on a stale-basis discard, or make the
  key version-aware) change ADR-004 coalescing behavior and require an ADR update
  and coverage tests.

### F7. An empty resolved badge selection preserves the previous ArrTags badge instead of restoring the original

When the enabled selectors and their allowlists resolve no displayable value,
`BadgeDefinitionResolver.Resolve` returns `BadgeSelection.Empty` and
`SkiaBadgeRenderer` returns `PassThrough(NoDisplayableValue)`; the coordinator
then returns a pass-through result before the publisher is invoked and performs
no image mutation. ADR-009 defines this pass-through as preserving the current
usable artwork. For an item ArrTags previously published, that current artwork is
the old ArrTags badge (with its old selector values, position, size, and palette),
and there is no restore or removal path for an empty selection: restoration runs
only on item removal or the lifecycle drain. The stale badge therefore persists
indefinitely, and each later reconciliation resolves the same empty selection,
passes through, and preserves it again.

- Evidence: ADR-009 ("no field is displayable ... the result is `PassThrough` and
  the current usable artwork is unchanged"), `src/ArrTags/Rendering/BadgeDefinitionResolver.cs`
  (empty selection), `src/ArrTags/Rendering/SkiaBadgeRenderer.cs`
  (`NoDisplayableValue`), `src/ArrTags/Artwork/ArtworkGenerationCoordinator.cs`
  (pass-through returns before the publisher), and
  `src/ArrTags/Artwork/ArtworkLifecycleCoordinator.cs` (restore only on item
  removal or the lifecycle drain); operator analysis, 2026-09-25.
- Consequence: narrowing a selector allowlist (or otherwise making an item's
  values non-displayable) does not remove the badge previously published for that
  item, and an output-policy change (for example global position or size) never
  takes effect for it because no new image is produced. The pass-through itself
  is deliberate, but the operator-visible result is a poster that keeps a badge
  which no longer matches the configuration. A fix would treat an owned session
  that resolves an empty selection as a restore/removal (restore the retained
  source baseline, or remove the ArrTags image when the baseline was absent) while
  still preserving the current artwork for the other pass-through reasons (missing
  metadata, ineligible match, unavailable source); that changes ADR-009/ADR-003
  behaviour and needs an ADR update and coverage tests.

### F8. The specific render classification is not surfaced in the artwork log

The single artwork-boundary log line emits only the bounded outcome and the
generic reason (for example `Artwork generation for item <id> completed with
RenderPassThrough: The render passed through; the current artwork is preserved.`).
The specific bounded classification the result already carries - the
`PassThroughReason` (`NoMetadata`, `NoDisplayableValue`, `IneligibleSurface`,
`MatchNotEligible`, `SourceUnavailable`), the `FailureReason`, or the
`SourceFailureReason` - is not emitted, so two attempts with completely different
causes are indistinguishable in the host log.

- Evidence: `src/ArrTags/Artwork/ArtworkGenerationCoordinator.cs` `LogOutcome`
  emits only the outcome and reason, while `src/ArrTags/Artwork/ArtworkGenerationResult.cs`
  exposes `PassThroughReason`, `FailureReason`, and `SourceFailureReason`; operator
  analysis, 2026-09-25.
- Consequence: diagnosing why no badge was rendered requires eliminating causes
  from other log lines (or reproducing with tests) instead of reading the reason
  directly; the empty-allowlist case in F7 is one example. The classifications are
  bounded, non-secret enums, so including them is compatible with the ADR-020
  clause 4 redaction contract; the fix is a small logging change plus tests. It is
  narrower than F3 (no bounded metrics/diagnostic-status surface) and is not
  proposed as a replacement for it.
