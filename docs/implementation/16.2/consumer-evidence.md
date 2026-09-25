# Task 16.2 - Authoritative restart-required set: per-consumer code evidence

**Task:** 16.2 (Phase 16, goals G6/G8, ADR-028 clause 2). **Status:** complete.
This is the code-evidenced audit required by `docs/planning/v1.2.md` section 8
("the code-evidenced restart-required audit (task 16.2), recorded as evidence
under `docs/implementation/16.2/`"). The code-owned classification it evidences
is `src/ArrTags/Configuration/RestartRequiredSettings.cs` (types
`SettingApplicationScope`, `SettingClassification`, `RestartRequiredSettings`);
the settings-page mirror is `restartRequiredFields` in
`src/ArrTags/Configuration/config.html`; the tests are
`tests/ArrTags.Tests/RestartRequiredSettingsTests.cs` and
`tests/ArrTags.Tests/RestartRequiredConsumerEvidenceTests.cs`.

Relative paths below are from the repository root. Line numbers are from the
tree that carries this audit (base commit `8b29dfa` plus this task's changes).

## 1. Rule and classification domain

ADR-028 clause 2 fixes a binary classification:

- a setting is **restart-required** when **any** consumer resolves the value at
  singleton construction;
- a setting is **per-operation** only when **every** consumer resolves it from
  the current configuration snapshot on each operation;
- a setting with both kinds of consumer is **restart-required and mixed**, and
  its note text states that some paths apply the change immediately while others
  require a restart.

The classified domain is every writable property of the five configuration model
types the settings page round-trips
(`PluginConfiguration`, `ArrConnectionConfiguration`, `OperationalLimits`,
`RendererConfiguration`, `BadgeSelectorConfiguration`). That is the same type set
`DashboardSettingsPageTests` uses to prove the page covers every user-adjustable
property, so a newly added property cannot be left unclassified.
`BadgeSelectorConfiguration.Selector` is the fixed code-owned selector enum, not
a user-adjustable field, and is the one property deliberately not classified.
There are 43 classified settings: 24 `OperationalLimits`, and 19 other
user-adjustable settings.

## 2. Singleton-construction capture sites

The production DI graph constructs these singletons once per host process
(`src/ArrTags/PluginLifecycle/ArrTagsServiceRegistrator.cs`). Each site below
resolves the configuration snapshot at construction time, so a later
replacement snapshot cannot change the captured consumer.

| Singleton | DI factory (ArrTagsServiceRegistrator.cs) | Captures |
| --- | --- | --- |
| `StateRepository` | `CreateStateRepository` (255-264) passes `configuration.Current.Limits` and the constructor stores that instance (`src/ArrTags/State/StateRepository.cs` 34-38) | the whole limits instance; `ApplyRetention` reads `_limits.RenderCacheTtlMinutes` (192), `_limits.RenderCacheQuotaBytes` (193), and `_limits.TerminalProvenanceRetentionDays` (199) |
| `SourceArtifactStore` | `CreateSourceArtifactStore` (304-307) over the `StateRepository` | `repository.Limits.SourceArtifactLimitBytes` and `repository.Limits.ArtifactStorageQuotaBytes` (`src/ArrTags/Artwork/SourceArtifactStore.cs` 46-47), enforced at `Promote` (81-86, 120-125) |
| `JellyfinArtworkImageAccess` | `CreateArtworkImageAccess` (281-288) passes `configuration.Current.Limits` | `limits.SourceArtifactLimitBytes` (`src/ArrTags/Artwork/JellyfinArtworkImageAccess.cs` 53), enforced at `TryReadBounded` (183-187) |
| `ArtworkSourceReader` | `CreateArtworkSourceReader` (290-295) passes `configuration.Current.Limits` | `limits.SourceArtifactLimitBytes` and `limits.MaxImageDimensionPixels` (`src/ArrTags/Artwork/ArtworkSourceReader.cs` 35-36), enforced at `ProcessPresent` (110-116, 143-149) |
| `ArtworkPublisher` | `CreateArtworkPublisher` (319-330) passes `configuration.Current.Limits` | `limits.DerivedArtifactLimitBytes` (`src/ArrTags/Artwork/ArtworkPublisher.cs` 67), enforced at `PromoteDerived` (1030-1035) |
| `ArtifactRetention` | `CreateArtifactRetention` (398-405) over the `StateRepository` | reads `_repository.Limits.TerminalProvenanceRetentionDays` per pass (`src/ArrTags/Artwork/ArtifactRetention.cs` 106) from the instance captured at `StateRepository` construction |

`PluginConfigurationSnapshot.From` clones the validated limits
(`src/ArrTags/Configuration/PluginConfigurationSnapshot.cs` 182, 213), so each
activated snapshot owns an independent `OperationalLimits`; a replacement never
mutates the instance a singleton captured. Every other singleton in the graph
resolves from the current snapshot per operation, as recorded below.

## 3. The authoritative restart-required set (7 settings)

| Setting | Mixed | Singleton-construction capture | Per-operation resolution |
| --- | --- | --- | --- |
| `OperationalLimits.SourceArtifactLimitBytes` | yes | `SourceArtifactStore` ctor (46) via `CreateStateRepository`; `JellyfinArtworkImageAccess` ctor (53); `ArtworkSourceReader` ctor (35) | `RenderLimitGuard.ValidateSourceImage` (`src/ArrTags/Rendering/RenderLimitGuard.cs` 41) called from `SkiaBadgeRenderer.RenderAsync` (`src/ArrTags/Rendering/SkiaBadgeRenderer.cs` 94-99) with `request.Limits`, which is the work item's `snapshot.Limits` (`src/ArrTags/Updates/ArtworkPublishingWorkItemProcessor.cs` 105-114) |
| `OperationalLimits.DerivedArtifactLimitBytes` | yes | `ArtworkPublisher` ctor (67) via `CreateArtworkPublisher` | `SkiaBadgeRenderer.cs` 278 and `RenderLimitGuard.ValidateDerivedOutput` (84) with the work item's `request.Limits` |
| `OperationalLimits.MaxImageDimensionPixels` | yes | `ArtworkSourceReader` ctor (36) via `CreateArtworkSourceReader` | `RenderLimitGuard` 46 (source) and 78 (derived output) with the work item's `request.Limits` |
| `OperationalLimits.RenderCacheTtlMinutes` | no | `StateRepository` ctor (34-38) via `CreateStateRepository`; consumed by `ApplyRetention` (192) | none |
| `OperationalLimits.RenderCacheQuotaBytes` | no | `StateRepository` ctor (34-38); consumed by `ApplyRetention` (193) | none |
| `OperationalLimits.ArtifactStorageQuotaBytes` | no | `SourceArtifactStore` ctor (47) over the captured `StateRepository.Limits`; enforced at `Promote` (120-125) | none |
| `OperationalLimits.TerminalProvenanceRetentionDays` | no | `StateRepository` ctor (34-38); consumed by `ApplyRetention` (199) and `ArtifactRetention.Apply` (106) through the captured `_repository.Limits` | none |

The three mixed entries are exactly the ADR-028 examples and the ADR-028
`ArtifactStorageQuotaBytes` restart-required example is present. This task adds
the complete evidence for the two remaining restart-required limits the ADR
names in its context (`RenderCacheTtlMinutes`, `RenderCacheQuotaBytes`,
`TerminalProvenanceRetentionDays` are the "`StateRepository`-backed render cache
TTL/quota and terminal-provenance retention" group).

## 4. Per-operation settings (all 36 remaining classified settings)

### 4.1 `OperationalLimits` (17)

| Setting | Consumer and per-operation resolution site |
| --- | --- |
| `QueueCapacity` | `LibraryWorkQueue.Capacity` (74-81) and `TryEnqueue` (166) call the `Func<OperationalLimits>` supplied by `CreateLibraryWorkQueue` (266-274) on every enqueue |
| `PerItemInFlightWork` | `LibraryWorkQueue.TryEnqueue` (151) -> `ResolvePerItemInFlight` (260-264) per enqueue |
| `ProviderConcurrencyGlobal` | `ProviderConcurrencyLimiter` global limiter lambda (36-37) per permit acquisition |
| `ProviderConcurrencyPerConnection` | `ProviderConcurrencyLimiter.CreateConnectionLimiter` (90-94) per permit acquisition |
| `RenderConcurrency` | `CreateRenderer` (225-235) supplies `configuration.Current.Limits.RenderConcurrency` to a `DynamicConcurrencyLimiter`, which resolves it on every acquisition (`DynamicConcurrencyLimiter.cs` 77, 121-125) |
| `RequestTimeoutSeconds` | **no runtime consumer** - see section 5 |
| `TransientRetryCount` | `LibraryWorkWorker.ProcessWithRetryAsync` (188-189) per work item; `SonarrClient` (275) / `RadarrClient` (249) from the per-read client limits (`ArrReadClientFactory` 40-51) |
| `RetryBackoffInitialSeconds` | `LibraryWorkWorker` (221) per item; `SonarrClient` (400) / `RadarrClient` (374) per read |
| `RetryBackoffFactor` | `SonarrClient` (400-401) / `RadarrClient` (374-375) per read; `LibraryWorkWorker` (188, 221) |
| `RetryBackoffMaxSeconds` | `SonarrClient` (401) / `RadarrClient` (375) per read; `LibraryWorkWorker` (221) |
| `ProviderResponseLimitBytes` | `SonarrClient` (375) / `RadarrClient` (349) from the client created per read by `ArrReadClientFactory` (40-51) |
| `WebhookMaxPayloadBytes` | `ArrTagsWebhookController.ReceiveAsync` (112-125) reads `_configuration.Current` and bounds the body read per request |
| `ReconciliationBatchSize` | `LibraryReconciliationService` (120) per run; `WebhookReconciliationResolver` (59) per event; `ArtworkStartupRecoveryService` (123) per startup scan; `SonarrClient` (186) / `RadarrClient` (168) per read |
| `MetadataStaleWindowMinutes` | `MetadataReconciliationProcessor.ReconcileAsync` (104, 204, 247) from the publish-time snapshot per work item |
| `InventoryCacheTtlMinutes` | `ArrInventoryCacheProvider.Current` (52-68) rebuilds `ArrInventoryCache.FromLimits(snapshot.Limits)` per configuration version (`ArrInventoryCache.cs` 109-117) |
| `InventoryCacheMaxRecords` | same rebuild path (`ArrInventoryCache.cs` 114) |
| `InventoryCacheMaxBytes` | same rebuild path (`ArrInventoryCache.cs` 115) |

### 4.2 Other user-adjustable settings (19)

| Setting | Consumer and per-operation resolution site |
| --- | --- |
| `PluginConfiguration.LogVerbosity` | `LogVerbosityGate.EffectiveLevel` (31) reads `_configuration.Current.LogVerbosity` on every call |
| `PluginConfiguration.WebhookSecret` | `WebhookAuthenticationFilter.OnAuthorizationAsync` (61-63) and `ArrTagsWebhookController.ReceiveAsync` (112-114) read the current snapshot and authenticate through `ConfigurationSnapshotService.TryAcquire` (93-110) with the snapshot version |
| `PluginConfiguration.BadgeMoviePosters` | `MediaEligibility.IsBadgeSurface` (`src/ArrTags/Media/MediaEligibility.cs` 61-72) from the snapshot supplied per eligibility check (`MetadataReconciliationProcessor` 104, 122, 227) |
| `PluginConfiguration.BadgeEpisodePosters` | same `IsBadgeSurface` path |
| `PluginConfiguration.EnabledLibraries` | `MediaEligibility.IsInLibraryScope` (29) from the snapshot supplied per eligibility check |
| `ArrConnectionConfiguration.Enabled` | `MetadataReconciliationProcessor` (104, 133-137) resolves via `ArrConnectionCatalog.FromSnapshot` (279-290); `LibraryReconciliationService` (85) and `WebhookReconciliationResolver` (52-57) resolve per operation |
| `ArrConnectionConfiguration.BaseUrl` | `ArrConnectionCatalog.FromSnapshot/Create` (`src/ArrTags/Providers/ArrConnectionCatalog.cs` 21-71) builds the identity from the current snapshot per operation |
| `ArrConnectionConfiguration.ApiKey` | `ConfigurationSnapshotService.BuildSecrets` (112-119) and `TryAcquire` (93-110) issue a version-matched lease per request; only a safe reference is carried |
| `ArrConnectionConfiguration.RequestTimeoutSeconds` | `ArrConnectionCatalog.Create` (46-71) carries the snapshot value; `ArrHttpClientFactory.CreateClient` (50) applies it per client creation (`ArrReadClientFactory` 40-51) |
| `ArrConnectionConfiguration.AllowInsecureTls` | `ArrConnectionCatalog.Create` (46-71) maps it to `ArrTlsPolicy`; `ArrHttpClientFactory.CreateClient` (47-50) selects the TLS-policy client per read |
| `RendererConfiguration.Position` | `RendererConfigurationResolver.ResolveOutputPolicy` (66-115) resolves it per activation; `ArtworkPublishingWorkItemProcessor` (79, 89-114) reads `snapshot.RendererOutputPolicy` per work item |
| `RendererConfiguration.Size` | same policy path |
| `RendererConfiguration.TechnicalBackground` | same policy path (palette) |
| `RendererConfiguration.TechnicalText` | same policy path (palette) |
| `RendererConfiguration.StatusBackground` | same policy path (palette) |
| `RendererConfiguration.StatusText` | same policy path (palette) |
| `BadgeSelectorConfiguration.Enabled` | `RendererConfigurationResolver.ResolveDefinitions` (27-64) per activation; `ArtworkPublishingWorkItemProcessor` (79, 105-114) passes the definitions per work item |
| `BadgeSelectorConfiguration.Template` | same definitions path; `BadgeDefinitionResolver` applies the template per render |
| `BadgeSelectorConfiguration.AllowedValues` | same definitions path; a non-empty allowlist is also part of the renderer configuration fingerprint, so a change republishes affected items |

No connection, badge-scope, library-scope, renderer, or verbosity setting is
captured at singleton construction anywhere in the graph, so none of the 19 is
restart-required.

## 5. Audited finding: the global `OperationalLimits.RequestTimeoutSeconds` has no runtime consumer

An exhaustive search of `src/ArrTags` shows `OperationalLimits.RequestTimeoutSeconds`
is read only by `OperationalLimits.Validate` (range check), `Clone`, the XML
serializer, and the settings page. The request timeout that actually applies is
the per-connection `ArrConnectionConfiguration.RequestTimeoutSeconds`:
`PluginConfigurationSnapshot.From` copies it (196-206), `ArrConnectionCatalog`
carries it per connection (46-71), and `ArrHttpClientFactory.CreateClient`
applies it per client creation (50). The global field defaults to 15 seconds and
the connection default uses `OperationalLimits.DefaultRequestTimeoutSeconds`, so
a changed global value has no runtime effect at all.

ADR-028 clause 2 is binary and is defined in terms of consumers; with zero
consumers no consumer resolves the value at singleton construction, so the
setting is not restart-required (a restart would not apply it either). It is
therefore classified per-operation with no consumer evidence and an explicit
audit note, and `RestartRequiredSettingsTests` pins it as the only classified
setting without a runtime consumer. Changing or removing the inert global field
is outside task 16.2's scope and is recorded for the orchestrator; it is not a
blocker for the classification.

## 6. Settings-page mirror

`config.html` declares `restartRequiredFields` (seven entries, each with
`setting`, `elementId`, and `mixed`) immediately after the byte-limit mapping.
Task 16.3 renders the note text from it; task 16.5 adds the modal reminder. The
mirror carries no note text itself, and this task adds no page behavior.
`RestartRequiredSettingsTests.ThePageRestartRequiredMirrorMatchesTheCodeOwnedClassification`
compares the mirror to `RestartRequiredSettings.RestartRequired` exactly
(setting name, element id, mixed flag, order), and
`EveryRestartRequiredSettingHasAStaticSettingsPageElement` proves each
restart-required element id exists in the page.

## 7. Test evidence and sensitivity

`RestartRequiredSettingsTests` (10 cases): unique/consistent entries, the exact
restart-required set, the exact mixed set, the named real construction-capture
types, lookup agreement, the audited inert setting, no-silent-member reflection
over every writable leaf property of the five configuration model types
(including `PluginConfiguration`, whose five leaf settings are derived by the
same `WritableLeafProperties` filter as the other four types), the static page
element per restart-required setting, the page mirror equality, and the
constructor guard (mixed must be restart-required; per-operation must have no
construction evidence; restart-required needs evidence or a note).

`RestartRequiredConsumerEvidenceTests` (7 cases): behavioral probes that the
`StateRepository` uses the snapshot limits captured at construction (cache TTL
and terminal retention), that its captured limits do not track a replacement
snapshot (TTL, quota, and terminal retention), that `SourceArtifactStore` uses
its captured source limit and quota, that `ArtworkSourceReader` uses its
captured byte and dimension limits, that `ArtworkPublisher` uses its captured
derived limit, and that `LibraryWorkQueue.Capacity` and `LogVerbosityGate`
resolve per operation.

Sensitivity probes (temporary edit, focused run, revert):

| Probe | Result |
| --- | --- |
| Mixed flags flipped off in the classification | 2 failures: `TheMixedSetIsExactlyTheEvidencedDualConsumerSettings`, `ThePageRestartRequiredMirrorMatchesTheCodeOwnedClassification` |
| Restart-required-only entries flipped to per-operation | 3 failures: `TheRestartRequiredSetIsExactlyTheEvidencedConstructionCapturedSettings`, `EveryRestartRequiredEntryNamesItsRealConstructionCaptureSite`, `ThePageRestartRequiredMirrorMatchesTheCodeOwnedClassification` |
| One `OperationalLimits` entry renamed away | 1 failure: `EveryUserAdjustableConfigurationPropertyIsClassified` |
| A new unclassified writable leaf property added to `PluginConfiguration` | 1 failure: `EveryUserAdjustableConfigurationPropertyIsClassified` (missing `PluginConfiguration.ProbeClassificationGuard`) |
| One page mirror entry changed (`mixed` and `elementId`) | 1 failure: `ThePageRestartRequiredMirrorMatchesTheCodeOwnedClassification` |
| A restart-required page input id renamed | 1 failure: `EveryRestartRequiredSettingHasAStaticSettingsPageElement` |
| `SourceArtifactStore` changed to re-read the live limits per promotion | 1 failure: `SourceArtifactStoreUsesTheSourceLimitAndQuotaCapturedAtConstruction` |
| `ArtworkPublisher` derived-limit enforcement removed | 1 failure: `ArtworkPublisherUsesTheDerivedLimitCapturedAtConstruction` |

All probes were reverted; the final default suite is Failed 0, Passed 1744,
Skipped 63, Total 1807 (baseline after 16.1: Failed 0, Passed 1727, Skipped 63,
Total 1790; +17 passed / +17 total, 0 new skips). `./build.sh build` reports 0
warnings / 0 errors.
