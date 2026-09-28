# Open limitations

## Functional and operational limitations (deferred, not implemented)

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
