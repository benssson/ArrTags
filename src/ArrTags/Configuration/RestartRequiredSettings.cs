using System;
using System.Collections.Generic;
using ArrTags.Rendering;

namespace ArrTags.Configuration;

/// <summary>
/// The ADR-028 clause 2 authoritative, code-owned classification of every
/// user-adjustable setting: the settings page must mark every
/// <see cref="SettingApplicationScope.RestartRequired"/> setting with note text,
/// and <see cref="RestartRequired"/> is the complete evidenced set. A setting is
/// restart-required when <em>any</em> consumer resolves its value at singleton
/// construction; a setting with both a construction-captured and a
/// per-operation consumer is restart-required and <see cref="SettingClassification.IsMixed"/>
/// (some paths apply the change immediately while others require a restart).
/// Every entry carries per-consumer code evidence; the code-evidenced audit is
/// recorded under <c>docs/implementation/16.2/</c>.
/// </summary>
/// <remarks>
/// The classified domain is every writable property of the five configuration
/// model types the settings page round-trips (<see cref="PluginConfiguration"/>,
/// <see cref="ArrConnectionConfiguration"/>, <see cref="OperationalLimits"/>,
/// <see cref="RendererConfiguration"/>, and <see cref="BadgeSelectorConfiguration"/>;
/// <see cref="BadgeSelectorConfiguration.Selector"/> is a code-owned fixed enum
/// and is not user-adjustable). The structural tests reflect over those types so
/// a newly added user-adjustable property cannot be silently unclassified.
/// </remarks>
public static class RestartRequiredSettings
{
    private static readonly IReadOnlyList<SettingClassification> AllValue = BuildAll();

    /// <summary>
    /// Gets every user-adjustable setting with its classification and evidence.
    /// </summary>
    public static IReadOnlyList<SettingClassification> All => AllValue;

    /// <summary>
    /// Gets the authoritative restart-required set, in a stable order.
    /// </summary>
    public static IReadOnlyList<SettingClassification> RestartRequired { get; } = FilterRestartRequired(AllValue);

    /// <summary>
    /// Gets the classification for one setting name.
    /// </summary>
    /// <param name="name">The stable setting name (<c>Type.Property</c>).</param>
    /// <returns>The classification entry.</returns>
    /// <exception cref="ArgumentException">The name is empty or is not classified.</exception>
    public static SettingClassification Get(string name)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);

        foreach (var entry in AllValue)
        {
            if (string.Equals(entry.Name, name, StringComparison.Ordinal))
            {
                return entry;
            }
        }

        throw new ArgumentException($"'{name}' is not a classified user-adjustable setting.", nameof(name));
    }

    /// <summary>
    /// Determines whether one setting requires a host restart.
    /// </summary>
    /// <param name="name">The stable setting name (<c>Type.Property</c>).</param>
    /// <returns><see langword="true"/> when the setting is in the restart-required set.</returns>
    /// <exception cref="ArgumentException">The name is empty or is not classified.</exception>
    public static bool IsRestartRequired(string name)
    {
        return Get(name).RestartRequired;
    }

    private static IReadOnlyList<SettingClassification> BuildAll()
    {
        var entries = new List<SettingClassification>();

        // ---- OperationalLimits -------------------------------------------------
        entries.Add(PerOperation(
            "OperationalLimits.QueueCapacity",
            new[] { "queueCapacity" },
            "src/ArrTags/Updates/LibraryWorkQueue.cs: Capacity (74-81) and TryEnqueue (166) read the Func<OperationalLimits> supplied by ArrTagsServiceRegistrator.CreateLibraryWorkQueue (src/ArrTags/PluginLifecycle/ArrTagsServiceRegistrator.cs 266-274, configuration.Current.Limits) on every enqueue."));
        entries.Add(PerOperation(
            "OperationalLimits.PerItemInFlightWork",
            new[] { "perItemInFlightWork" },
            "src/ArrTags/Updates/LibraryWorkQueue.cs: TryEnqueue (151) calls ResolvePerItemInFlight (260-264), which reads _limits().PerItemInFlightWork from the current snapshot provider on every enqueue."));
        entries.Add(PerOperation(
            "OperationalLimits.ProviderConcurrencyPerConnection",
            new[] { "providerConcurrencyPerConnection" },
            "src/ArrTags/Concurrency/ProviderConcurrencyLimiter.cs: CreateConnectionLimiter (90-94) supplies a DynamicConcurrencyLimiter lambda reading _configuration.Current.Limits.ProviderConcurrencyPerConnection, resolved on every permit acquisition (DynamicConcurrencyLimiter.cs 77, 121-125)."));
        entries.Add(PerOperation(
            "OperationalLimits.ProviderConcurrencyGlobal",
            new[] { "providerConcurrencyGlobal" },
            "src/ArrTags/Concurrency/ProviderConcurrencyLimiter.cs: the global DynamicConcurrencyLimiter lambda (36-37) reads _configuration.Current.Limits.ProviderConcurrencyGlobal on every permit acquisition."));
        entries.Add(PerOperation(
            "OperationalLimits.RenderConcurrency",
            new[] { "renderConcurrency" },
            "src/ArrTags/PluginLifecycle/ArrTagsServiceRegistrator.cs: CreateRenderer (225-235) supplies configuration.Current.Limits.RenderConcurrency to a DynamicConcurrencyLimiter, which resolves it on every render acquisition (DynamicConcurrencyLimiter.cs 77, 121-125)."));
        entries.Add(PerOperationNoConsumer(
            "OperationalLimits.RequestTimeoutSeconds",
            new[] { "requestTimeoutSeconds" },
            "No runtime consumer: the global OperationalLimits.RequestTimeoutSeconds is validated, persisted, and round-tripped by the settings page but is never read at runtime. The effective request timeout is the per-connection ArrConnectionConfiguration.RequestTimeoutSeconds: PluginConfigurationSnapshot.From (196-206) copies it per snapshot, ArrConnectionCatalog.Create (src/ArrTags/Providers/ArrConnectionCatalog.cs 46-71) carries it per connection, and ArrHttpClientFactory.CreateClient (src/ArrTags/Providers/ArrHttpClientFactory.cs 50) applies it per client creation. ADR-028 clause 2 is binary; with zero consumers no consumer resolves the value at singleton construction, so the setting is not restart-required, and a restart would not apply it either. This pre-existing inert global field is recorded so the audit is not silent; changing it is outside task 16.2."));
        entries.Add(PerOperation(
            "OperationalLimits.TransientRetryCount",
            new[] { "transientRetryCount" },
            "src/ArrTags/Updates/LibraryWorkWorker.cs: ProcessWithRetryAsync (188-189) reads _configuration.Current.Limits per work item for the queue retry policy; src/ArrTags/Reconciliation/ArrReadClientFactory.cs (40-51) builds each provider client from _configuration.Current.Limits, and the client reads it per read (SonarrClient.cs 275, RadarrClient.cs 249)."));
        entries.Add(PerOperation(
            "OperationalLimits.RetryBackoffInitialSeconds",
            new[] { "retryBackoffInitialSeconds" },
            "src/ArrTags/Updates/LibraryWorkWorker.cs: ComputeBackoff (221) uses the per-work-item current limits; src/ArrTags/Providers/Sonarr/SonarrClient.cs (400-401) and RadarrClient.cs (374-375) read it from the per-read client limits resolved by ArrReadClientFactory (40-51)."));
        entries.Add(PerOperation(
            "OperationalLimits.RetryBackoffFactor",
            new[] { "retryBackoffFactor" },
            "src/ArrTags/Providers/Sonarr/SonarrClient.cs (400-401) and RadarrClient.cs (374-375) compute the backoff from the per-read client limits resolved by ArrReadClientFactory (40-51); the queue-level policy reads the current snapshot per item (LibraryWorkWorker.cs 188, 221)."));
        entries.Add(PerOperation(
            "OperationalLimits.RetryBackoffMaxSeconds",
            new[] { "retryBackoffMaxSeconds" },
            "src/ArrTags/Providers/Sonarr/SonarrClient.cs (401) and RadarrClient.cs (375) cap the backoff from the per-read client limits resolved by ArrReadClientFactory (40-51); the queue-level policy reads the current snapshot per item (LibraryWorkWorker.cs 188, 221)."));
        entries.Add(PerOperation(
            "OperationalLimits.ProviderResponseLimitBytes",
            new[] { "providerResponseLimitBytes" },
            "src/ArrTags/Reconciliation/ArrReadClientFactory.cs: CreateSonarr/CreateRadarr (40-51) pass _configuration.Current.Limits into a client created per read; src/ArrTags/Providers/Sonarr/SonarrClient.cs (375) and RadarrClient.cs (349) bound the response with it."));
        entries.Add(PerOperation(
            "OperationalLimits.WebhookMaxPayloadBytes",
            new[] { "webhookMaxPayloadBytes" },
            "src/ArrTags/Webhooks/ArrTagsWebhookController.cs: ReceiveAsync (112-125) reads _configuration.Current per request and bounds the body read with snapshot.Limits.WebhookMaxPayloadBytes."));
        entries.Add(MixedRestartRequired(
            "OperationalLimits.SourceArtifactLimitBytes",
            "sourceArtifactLimitBytes",
            "Construction-captured: src/ArrTags/Artwork/SourceArtifactStore.cs ctor (41-48) copies repository.Limits.SourceArtifactLimitBytes; the repository's limits are captured at singleton construction by ArrTagsServiceRegistrator.CreateStateRepository (src/ArrTags/PluginLifecycle/ArrTagsServiceRegistrator.cs 255-264). src/ArrTags/Artwork/JellyfinArtworkImageAccess.cs ctor (45-54) and src/ArrTags/Artwork/ArtworkSourceReader.cs ctor (31-37) also copy it from the snapshot at singleton construction (ArrTagsServiceRegistrator 281-295).",
            "Per-operation: src/ArrTags/Rendering/SkiaBadgeRenderer.cs: RenderAsync (94-99) validates the source with request.Limits.SourceArtifactLimitBytes (RenderLimitGuard.cs 41); request.Limits is the snapshot.Limits of the work item (ArtworkPublishingWorkItemProcessor.cs 105-114, PluginConfigurationSnapshot per activation)."));
        entries.Add(MixedRestartRequired(
            "OperationalLimits.DerivedArtifactLimitBytes",
            "derivedArtifactLimitBytes",
            "Construction-captured: src/ArrTags/Artwork/ArtworkPublisher.cs ctor (52-68) copies limits.DerivedArtifactLimitBytes and enforces it at PromoteDerived (1030); the publisher is a singleton constructed from the then-current snapshot limits by ArrTagsServiceRegistrator.CreateArtworkPublisher (319-330).",
            "Per-operation: src/ArrTags/Rendering/SkiaBadgeRenderer.cs (278) and RenderLimitGuard.ValidateDerivedOutput (src/ArrTags/Rendering/RenderLimitGuard.cs 66-89) enforce request.Limits.DerivedArtifactLimitBytes, which is the work item's snapshot.Limits (ArtworkPublishingWorkItemProcessor.cs 105-114)."));
        entries.Add(MixedRestartRequired(
            "OperationalLimits.MaxImageDimensionPixels",
            "maxImageDimensionPixels",
            "Construction-captured: src/ArrTags/Artwork/ArtworkSourceReader.cs ctor (31-37) copies limits.MaxImageDimensionPixels and enforces it on every read (143-149); the reader is a singleton constructed from the then-current snapshot limits by ArrTagsServiceRegistrator.CreateArtworkSourceReader (290-295).",
            "Per-operation: src/ArrTags/Rendering/RenderLimitGuard.cs (46, 78) enforces limits.MaxImageDimensionPixels for the source and derived surfaces, supplied as request.Limits from the work item's snapshot.Limits (ArtworkPublishingWorkItemProcessor.cs 105-114, SkiaBadgeRenderer.cs 94-108)."));
        entries.Add(RestartRequiredOnly(
            "OperationalLimits.RenderCacheTtlMinutes",
            "renderCacheTtlMinutes",
            "Construction-captured: src/ArrTags/State/StateRepository.cs ctor (34-38) stores the limits instance supplied by ArrTagsServiceRegistrator.CreateStateRepository (255-264) at singleton construction; ApplyRetention (190-195) reads _limits.RenderCacheTtlMinutes for the render work-cache TTL."));
        entries.Add(RestartRequiredOnly(
            "OperationalLimits.RenderCacheQuotaBytes",
            "renderCacheQuotaBytes",
            "Construction-captured: src/ArrTags/State/StateRepository.cs ctor (34-38) stores the limits instance supplied by ArrTagsServiceRegistrator.CreateStateRepository (255-264) at singleton construction; ApplyRetention (190-195) reads _limits.RenderCacheQuotaBytes for the render work-cache quota."));
        entries.Add(RestartRequiredOnly(
            "OperationalLimits.ArtifactStorageQuotaBytes",
            "artifactStorageQuotaBytes",
            "Construction-captured: src/ArrTags/Artwork/SourceArtifactStore.cs ctor (41-48) copies repository.Limits.ArtifactStorageQuotaBytes and enforces it at Promote (120-125); the repository's limits are captured at singleton construction by ArrTagsServiceRegistrator.CreateStateRepository (255-264), so the authoritative artifact storage quota changes only after a restart."));
        entries.Add(RestartRequiredOnly(
            "OperationalLimits.TerminalProvenanceRetentionDays",
            "terminalProvenanceRetentionDays",
            "Construction-captured: src/ArrTags/State/StateRepository.cs ctor (34-38) stores the limits instance supplied by ArrTagsServiceRegistrator.CreateStateRepository (255-264) at singleton construction; ApplyRetention (197-200) and src/ArrTags/Artwork/ArtifactRetention.cs (104-106) read _repository.Limits.TerminalProvenanceRetentionDays from that captured instance."));
        entries.Add(PerOperation(
            "OperationalLimits.ReconciliationBatchSize",
            new[] { "reconciliationBatchSize" },
            "src/ArrTags/Reconciliation/LibraryReconciliationService.cs (120) reads snapshot.Limits.ReconciliationBatchSize per run; src/ArrTags/Webhooks/WebhookReconciliationResolver.cs (59) per webhook event; src/ArrTags/PluginLifecycle/ArtworkStartupRecoveryService.cs (123) per startup scan; src/ArrTags/Providers/Sonarr/SonarrClient.cs (186) and RadarrClient.cs (168) per read client created from _configuration.Current.Limits (ArrReadClientFactory 40-51)."));
        entries.Add(PerOperation(
            "OperationalLimits.MetadataStaleWindowMinutes",
            new[] { "metadataStaleWindowMinutes" },
            "src/ArrTags/Reconciliation/MetadataReconciliationProcessor.cs: ReconcileAsync reads _configuration.Current (104) and the publish-time snapshot (204), and derives the metadata freshness window from publishSnapshot.Limits.MetadataStaleWindowMinutes (247) per work item."));
        entries.Add(PerOperation(
            "OperationalLimits.InventoryCacheTtlMinutes",
            new[] { "inventoryCacheTtlMinutes" },
            "src/ArrTags/Providers/ArrInventoryCacheProvider.cs: Current (52-68) rebuilds ArrInventoryCache.FromLimits(snapshot.Limits) when the configuration version changes; FromLimits reads InventoryCacheTtlMinutes (src/ArrTags/Providers/ArrInventoryCache.cs 109-117)."));
        entries.Add(PerOperation(
            "OperationalLimits.InventoryCacheMaxRecords",
            new[] { "inventoryCacheMaxRecords" },
            "src/ArrTags/Providers/ArrInventoryCacheProvider.cs: Current (52-68) rebuilds ArrInventoryCache.FromLimits(snapshot.Limits) when the configuration version changes; FromLimits reads InventoryCacheMaxRecords (src/ArrTags/Providers/ArrInventoryCache.cs 109-117)."));
        entries.Add(PerOperation(
            "OperationalLimits.InventoryCacheMaxBytes",
            new[] { "inventoryCacheMaxBytes" },
            "src/ArrTags/Providers/ArrInventoryCacheProvider.cs: Current (52-68) rebuilds ArrInventoryCache.FromLimits(snapshot.Limits) when the configuration version changes; FromLimits reads InventoryCacheMaxBytes (src/ArrTags/Providers/ArrInventoryCache.cs 109-117)."));

        // ---- PluginConfiguration ----------------------------------------------
        entries.Add(PerOperation(
            "PluginConfiguration.LogVerbosity",
            new[] { "logVerbosity" },
            "src/ArrTags/PluginLifecycle/LogVerbosityGate.cs: EffectiveLevel (31) reads _configuration.Current.LogVerbosity on every call (registered in ArrTagsServiceRegistrator 51), so a replacement applies without a restart."));
        entries.Add(PerOperation(
            "PluginConfiguration.WebhookSecret",
            new[] { "webhookSecret" },
            "src/ArrTags/Webhooks/WebhookAuthenticationFilter.cs: OnAuthorizationAsync (56-63) reads _configuration.Current per request and authenticates through IPluginSecretResolver.TryAcquire with the snapshot version; src/ArrTags/Webhooks/ArrTagsWebhookController.cs ReceiveAsync (112-114) does the same, and ConfigurationSnapshotService.TryAcquire (93-110) fences the secret to the current version."));
        entries.Add(PerOperation(
            "PluginConfiguration.BadgeMoviePosters",
            new[] { "badgeMoviePosters" },
            "src/ArrTags/Media/MediaEligibility.cs: IsBadgeSurface (61-72) reads configuration.BadgeMoviePosters from the snapshot supplied per eligibility check; MetadataReconciliationProcessor.ReconcileAsync (104, 122, 227) passes _configuration.Current."));
        entries.Add(PerOperation(
            "PluginConfiguration.BadgeEpisodePosters",
            new[] { "badgeEpisodePosters" },
            "src/ArrTags/Media/MediaEligibility.cs: IsBadgeSurface (61-72) reads configuration.BadgeEpisodePosters from the snapshot supplied per eligibility check; MetadataReconciliationProcessor.ReconcileAsync (104, 122, 227) passes _configuration.Current."));
        entries.Add(PerOperation(
            "PluginConfiguration.EnabledLibraries",
            new[] { "enabledLibraries" },
            "src/ArrTags/Media/MediaEligibility.cs: IsInLibraryScope (29) reads configuration.EnabledLibraries from the snapshot supplied per eligibility check (MetadataReconciliationProcessor.ReconcileAsync 104, 122, 227)."));

        // ---- ArrConnectionConfiguration ---------------------------------------
        entries.Add(PerOperation(
            "ArrConnectionConfiguration.Enabled",
            new[] { "sonarrEnabled", "radarrEnabled" },
            "src/ArrTags/Reconciliation/MetadataReconciliationProcessor.cs: ReconcileAsync (104, 133-137) resolves the connection from the current snapshot via ArrConnectionCatalog.FromSnapshot (279-290) and discards disabled connections; LibraryReconciliationService.cs (85) and WebhookReconciliationResolver.cs (52-57) resolve the same way per operation."));
        entries.Add(PerOperation(
            "ArrConnectionConfiguration.BaseUrl",
            new[] { "sonarrBaseUrl", "radarrBaseUrl" },
            "src/ArrTags/Providers/ArrConnectionCatalog.cs: FromSnapshot/Create (21-71) build the connection identity and base URL from the current snapshot per operation (MetadataReconciliationProcessor 104/133; LibraryReconciliationService 85)."));
        entries.Add(PerOperation(
            "ArrConnectionConfiguration.ApiKey",
            new[] { "sonarrApiKey", "radarrApiKey" },
            "src/ArrTags/Configuration/ConfigurationSnapshotService.cs: BuildSecrets (112-119) and TryAcquire (93-110) issue a version-matched lease for the current snapshot; src/ArrTags/Providers/ArrConnection.cs carries only the safe reference, so a rotated key applies to new work without a restart."));
        entries.Add(PerOperation(
            "ArrConnectionConfiguration.RequestTimeoutSeconds",
            new[] { "sonarrRequestTimeoutSeconds", "radarrRequestTimeoutSeconds" },
            "src/ArrTags/Providers/ArrConnectionCatalog.cs: Create (46-71) carries the snapshot's timeout per connection; src/ArrTags/Providers/ArrHttpClientFactory.cs CreateClient (32-53) applies it when the per-read client is created (ArrReadClientFactory 40-51)."));
        entries.Add(PerOperation(
            "ArrConnectionConfiguration.AllowInsecureTls",
            new[] { "sonarrAllowInsecureTls", "radarrAllowInsecureTls" },
            "src/ArrTags/Providers/ArrConnectionCatalog.cs: Create (46-71) maps the snapshot's flag to the ArrTlsPolicy; src/ArrTags/Providers/ArrHttpClientFactory.cs CreateClient (47-50) selects the TLS-policy named client per read (ArrReadClientFactory 40-51)."));

        // ---- RendererConfiguration --------------------------------------------
        entries.Add(PerOperation(
            "RendererConfiguration.Position",
            new[] { "rendererPosition" },
            "src/ArrTags/Updates/ArtworkPublishingWorkItemProcessor.cs: ProcessAsync reads _configuration.Current (79) and passes snapshot.RendererOutputPolicy into the regeneration decision and the generation request per work item (89-114); PluginConfigurationSnapshot.From (189-194) derives the policy through RendererConfigurationResolver per activation."));
        entries.Add(PerOperation(
            "RendererConfiguration.Size",
            new[] { "rendererSize" },
            "src/ArrTags/Updates/ArtworkPublishingWorkItemProcessor.cs: ProcessAsync reads _configuration.Current (79) and passes snapshot.RendererOutputPolicy per work item (89-114); PluginConfigurationSnapshot.From (189-194) derives the policy through RendererConfigurationResolver per activation."));
        entries.Add(PerOperation(
            "RendererConfiguration.TechnicalBackground",
            new[] { "technicalBackground" },
            "src/ArrTags/Configuration/RendererConfigurationResolver.cs: ResolveOutputPolicy (66-115) resolves the palette into the snapshot policy; ArtworkPublishingWorkItemProcessor.cs (79, 89-114) reads it from the current snapshot per work item, and the fingerprint change republishes affected items."));
        entries.Add(PerOperation(
            "RendererConfiguration.TechnicalText",
            new[] { "technicalText" },
            "src/ArrTags/Configuration/RendererConfigurationResolver.cs: ResolveOutputPolicy (66-115) resolves the palette into the snapshot policy; ArtworkPublishingWorkItemProcessor.cs (79, 89-114) reads it from the current snapshot per work item."));
        entries.Add(PerOperation(
            "RendererConfiguration.StatusBackground",
            new[] { "statusBackground" },
            "src/ArrTags/Configuration/RendererConfigurationResolver.cs: ResolveOutputPolicy (66-115) resolves the palette into the snapshot policy; ArtworkPublishingWorkItemProcessor.cs (79, 89-114) reads it from the current snapshot per work item."));
        entries.Add(PerOperation(
            "RendererConfiguration.StatusText",
            new[] { "statusText" },
            "src/ArrTags/Configuration/RendererConfigurationResolver.cs: ResolveOutputPolicy (66-115) resolves the palette into the snapshot policy; ArtworkPublishingWorkItemProcessor.cs (79, 89-114) reads it from the current snapshot per work item."));

        // ---- BadgeSelectorConfiguration ---------------------------------------
        entries.Add(PerOperation(
            "BadgeSelectorConfiguration.Enabled",
            SelectorPageElementIds("enabled"),
            "src/ArrTags/Configuration/RendererConfigurationResolver.cs: ResolveDefinitions (27-64) resolves the enabled selectors into the snapshot BadgeDefinitions; src/ArrTags/Updates/ArtworkPublishingWorkItemProcessor.cs (79, 89-114) passes them per work item, and the fingerprint change republishes affected items."));
        entries.Add(PerOperation(
            "BadgeSelectorConfiguration.Template",
            SelectorPageElementIds("template"),
            "src/ArrTags/Configuration/RendererConfigurationResolver.cs: ResolveDefinitions (27-64) resolves each selector template; src/ArrTags/Rendering/BadgeDefinitionResolver.cs applies it per render from the work item's BadgeDefinitions (ArtworkPublishingWorkItemProcessor.cs 105-114)."));
        entries.Add(PerOperation(
            "BadgeSelectorConfiguration.AllowedValues",
            SelectorPageElementIds("allowedValues"),
            "src/ArrTags/Configuration/RendererConfigurationResolver.cs: ResolveDefinitions (27-64) carries each selector allowlist into the snapshot BadgeDefinitions and the configuration fingerprint; src/ArrTags/Updates/ArtworkPublishingWorkItemProcessor.cs (89-114) reads them from the current snapshot per work item."));

        return entries.AsReadOnly();
    }

    private static IReadOnlyList<SettingClassification> FilterRestartRequired(IReadOnlyList<SettingClassification> entries)
    {
        var restartRequired = new List<SettingClassification>();
        foreach (var entry in entries)
        {
            if (entry.RestartRequired)
            {
                restartRequired.Add(entry);
            }
        }

        return restartRequired.AsReadOnly();
    }

    private static SettingClassification RestartRequiredOnly(string name, string pageElementId, string constructionEvidence)
    {
        return new SettingClassification(
            name,
            SettingApplicationScope.RestartRequired,
            isMixed: false,
            new[] { pageElementId },
            new[] { constructionEvidence },
            Array.Empty<string>());
    }

    private static SettingClassification MixedRestartRequired(
        string name,
        string pageElementId,
        string constructionEvidence,
        string perOperationEvidence)
    {
        return new SettingClassification(
            name,
            SettingApplicationScope.RestartRequired,
            isMixed: true,
            new[] { pageElementId },
            new[] { constructionEvidence },
            new[] { perOperationEvidence });
    }

    private static SettingClassification PerOperation(string name, IReadOnlyList<string> pageElementIds, string evidence)
    {
        return new SettingClassification(
            name,
            SettingApplicationScope.PerOperation,
            isMixed: false,
            pageElementIds,
            Array.Empty<string>(),
            new[] { evidence });
    }

    private static SettingClassification PerOperationNoConsumer(
        string name,
        IReadOnlyList<string> pageElementIds,
        string note)
    {
        return new SettingClassification(
            name,
            SettingApplicationScope.PerOperation,
            isMixed: false,
            pageElementIds,
            Array.Empty<string>(),
            Array.Empty<string>(),
            note);
    }

    private static IReadOnlyList<string> SelectorPageElementIds(string suffix)
    {
        var selectorNames = Enum.GetNames<BadgeSelector>();
        var ids = new string[selectorNames.Length];
        for (var index = 0; index < selectorNames.Length; index++)
        {
            ids[index] = "selector-" + selectorNames[index] + "-" + suffix;
        }

        return ids;
    }
}
