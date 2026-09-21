using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using ArrTags.Artwork;
using ArrTags.Configuration;
using ArrTags.Matching;
using ArrTags.Media;
using ArrTags.Metadata;
using ArrTags.Providers;
using ArrTags.Reconciliation;
using ArrTags.Rendering;
using ArrTags.State;
using ArrTags.Updates;

namespace ArrTags.Tests;

/// <summary>
/// The injectable host boundary for the task 6.8 Phase 6 end-to-end tests. It is
/// both the source reader and the image writer, so a single double observes every
/// read/mutation the pipeline performs. The hooks let a test pause a read, save,
/// or item update, throw a simulated crash, raise a lifecycle fence, or block
/// until cancellation without a live Jellyfin host and without touching a real
/// library.
/// </summary>
internal sealed class Phase6Host : IArtworkSourceReader, IArtworkImageWriter
{
    /// <summary>
    /// Gets or sets the current active surface bytes, or <see langword="null"/> for an absent surface.
    /// </summary>
    public byte[]? CurrentBytes { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether a save replaces the active surface bytes.
    /// </summary>
    public bool SaveAppliesBytes { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether a save reports a bounded failure.
    /// </summary>
    public bool SaveReturnsFailure { get; set; }

    /// <summary>
    /// Gets or sets an optional hook awaited before a source read completes. Use it
    /// to pause, block, or fail a read. A thrown exception propagates to the caller.
    /// </summary>
    public Func<CancellationToken, Task>? ReadHook { get; set; }

    /// <summary>
    /// Gets or sets an optional hook awaited before a save applies.
    /// </summary>
    public Func<CancellationToken, Task>? SaveHook { get; set; }

    /// <summary>
    /// Gets or sets an optional hook awaited before an item update completes.
    /// </summary>
    public Func<CancellationToken, Task>? UpdateHook { get; set; }

    /// <summary>
    /// Gets or sets an optional side effect raised while an item update is in flight.
    /// A thrown exception propagates and simulates a process crash mid-publication.
    /// </summary>
    public Action? OnPersistItemUpdate { get; set; }

    /// <summary>Gets a signal set when a read enters its hook.</summary>
    public TaskCompletionSource ReadEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>Gets a signal set when a save enters its hook.</summary>
    public TaskCompletionSource SaveEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>Gets a signal set when an item update enters its hook.</summary>
    public TaskCompletionSource UpdateEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>Gets the number of source reads.</summary>
    public int ReadCalls { get; private set; }

    /// <summary>Gets the number of image saves.</summary>
    public int SaveCalls { get; private set; }

    /// <summary>Gets the number of item-update persists.</summary>
    public int UpdateCalls { get; private set; }

    /// <summary>Gets the number of image removals.</summary>
    public int RemoveCalls { get; private set; }

    /// <summary>Gets the exact bytes written by the last save.</summary>
    public byte[]? SavedBytes { get; private set; }

    /// <summary>Gets the content type written by the last save.</summary>
    public string? SavedContentType { get; private set; }

    /// <inheritdoc />
    public async Task<ArtworkSourceReadResult> ReadAsync(
        Guid itemId,
        ArtworkImageSurface surface,
        CancellationToken cancellationToken)
    {
        ReadCalls++;
        if (ReadHook is { } hook)
        {
            ReadEntered.TrySetResult();
            await hook(cancellationToken).ConfigureAwait(false);
        }

        cancellationToken.ThrowIfCancellationRequested();
        return CurrentBytes is null ? ArtworkSourceReadResult.Absent(surface) : Present(surface, CurrentBytes);
    }

    /// <inheritdoc />
    public async Task<ArtworkImageMutationResult> SaveImageAsync(
        Guid itemId,
        ArtworkImageSurface surface,
        ReadOnlyMemory<byte> content,
        string contentType,
        CancellationToken cancellationToken)
    {
        SaveCalls++;
        if (SaveHook is { } hook)
        {
            SaveEntered.TrySetResult();
            await hook(cancellationToken).ConfigureAwait(false);
        }

        cancellationToken.ThrowIfCancellationRequested();
        if (SaveReturnsFailure)
        {
            return ArtworkImageMutationResult.Failure(
                ArtworkImageMutationStatus.Failed,
                "The fake image save failed.");
        }

        SavedBytes = content.ToArray();
        SavedContentType = contentType;
        if (SaveAppliesBytes)
        {
            CurrentBytes = SavedBytes;
        }

        return ArtworkImageMutationResult.Success();
    }

    /// <inheritdoc />
    public async Task<ArtworkImageMutationResult> PersistItemUpdateAsync(
        Guid itemId,
        ArtworkImageSurface surface,
        CancellationToken cancellationToken)
    {
        UpdateCalls++;
        if (UpdateHook is { } hook)
        {
            UpdateEntered.TrySetResult();
            await hook(cancellationToken).ConfigureAwait(false);
        }

        cancellationToken.ThrowIfCancellationRequested();
        OnPersistItemUpdate?.Invoke();
        return ArtworkImageMutationResult.Success();
    }

    /// <inheritdoc />
    public Task<ArtworkImageMutationResult> RemoveImageAsync(
        Guid itemId,
        ArtworkImageSurface surface,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        RemoveCalls++;
        CurrentBytes = null;
        return Task.FromResult(ArtworkImageMutationResult.Success());
    }

    /// <summary>
    /// Creates a present source read result for the supplied bytes.
    /// </summary>
    /// <param name="surface">The surface.</param>
    /// <param name="bytes">The exact bytes.</param>
    /// <returns>A present read result.</returns>
    public static ArtworkSourceReadResult Present(ArtworkImageSurface surface, byte[] bytes)
    {
        return ArtworkSourceReadResult.Present(
            surface,
            "image/png",
            bytes,
            ArtworkHashes.ComputeSha256(bytes),
            100,
            150,
            DateTimeOffset.UnixEpoch,
            "host-tag");
    }
}

/// <summary>
/// A renderer that delegates to the production fingerprinting renderer but exposes
/// entry/cancellation hooks, so a test can pause or cancel a render while still
/// producing the exact production output fingerprint the publication gate relies
/// on.
/// </summary>
internal sealed class Phase6Renderer : IRenderer
{
    private readonly FingerprintingRenderer _inner = new();

    /// <summary>
    /// Gets or sets an optional hook awaited before the render executes.
    /// </summary>
    public Func<CancellationToken, Task>? RenderHook { get; set; }

    /// <summary>Gets a signal set when a render enters its hook.</summary>
    public TaskCompletionSource RenderEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>Gets the number of render calls.</summary>
    public int Calls => _inner.Calls;

    /// <summary>Gets the last render request.</summary>
    public RenderRequest? LastRequest => _inner.LastRequest;

    /// <inheritdoc />
    public async Task<RenderResult> RenderAsync(RenderRequest request, CancellationToken cancellationToken)
    {
        if (RenderHook is { } hook)
        {
            RenderEntered.TrySetResult();
            await hook(cancellationToken).ConfigureAwait(false);
        }

        return await _inner.RenderAsync(request, cancellationToken).ConfigureAwait(false);
    }
}

/// <summary>
/// The host-neutral plugin lifecycle fence provider double used by the task 6.8
/// drain tests.
/// </summary>
internal sealed class Phase6FenceProvider : IPluginLifecycleFenceProvider
{
    /// <summary>
    /// Gets or sets the fence the provider implies.
    /// </summary>
    public ArtworkLifecycleFence Fence { get; set; } = ArtworkLifecycleFence.Normal;

    /// <inheritdoc />
    public ArtworkLifecycleFence GetLifecycleFence() => Fence;
}

/// <summary>
/// The task 6.8 Phase 6 harness. It composes the complete production Phase 6
/// graph - the durable stores, the reconciliation processor, the artwork
/// publication pipeline, the per-subject recovery gate, the lifecycle drain
/// coordinator, and the bounded work queue - over one plugin data directory using
/// only injectable host/renderer doubles.
/// </summary>
/// <remarks>
/// <see cref="Restart"/> rebuilds every in-memory service over the same durable
/// directory, so a test can prove that the queue/worker, metadata state,
/// freshness, artwork recovery, and retention make the same guarded decisions
/// without relying on in-memory state. The harness owns the temporary directory
/// and a restarted harness deliberately shares ownership with its parent.
/// </remarks>
internal sealed class Phase6Harness : IDisposable
{
    /// <summary>The V1 unindexed poster surface.</summary>
    public static readonly ArtworkImageSurface Surface = ArtworkImageSurface.Primary;

    private readonly PluginConfiguration _configuration;
    private readonly bool _ownsRoot;

    /// <summary>
    /// Initializes a new instance of the <see cref="Phase6Harness"/> class.
    /// </summary>
    /// <param name="root">The plugin data root; a unique temporary directory is created when omitted.</param>
    /// <param name="configuration">The plugin configuration; the reconciliation fixture default when omitted.</param>
    /// <param name="itemId">The seeded Jellyfin movie identifier.</param>
    /// <param name="libraryId">The seeded library identifier.</param>
    /// <param name="ownsRoot">Whether this harness deletes the root on dispose.</param>
    /// <param name="copyBytesFrom">An existing host whose active bytes seed the fresh host.</param>
    public Phase6Harness(
        string? root = null,
        PluginConfiguration? configuration = null,
        Guid? itemId = null,
        Guid? libraryId = null,
        bool ownsRoot = true,
        Phase6Host? copyBytesFrom = null)
    {
        _ownsRoot = ownsRoot;
        Root = root ?? Path.Combine(Path.GetTempPath(), "arrtags-phase6-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Root);
        _configuration = configuration ?? new PluginConfiguration
        {
            BadgeMoviePosters = true,
            Radarr = new ArrConnectionConfiguration
            {
                Enabled = true,
                BaseUrl = "http://radarr.test",
                ApiKey = "test-radarr-api-key",
            },
        };

        ItemId = itemId ?? Guid.NewGuid();
        LibraryId = libraryId ?? Guid.NewGuid();

        Configuration = new ConfigurationSnapshotService(_configuration);
        Repository = new StateRepository(Root, _configuration.Limits);
        Metadata = new MetadataStateStore(Repository);
        States = new PublishedArtworkStateStore(Repository);
        Operations = new ArtworkOperationStore(Repository);
        Artifacts = new SourceArtifactStore(Repository);
        Fences = new ArtworkLifecycleFenceStore(Repository);

        Library = new ReconciliationLibraryResolver();
        Reader = new FakeMetadataReader { Kind = ArrProviderKind.Radarr };
        Host = new Phase6Host { CurrentBytes = copyBytesFrom?.CurrentBytes };
        Renderer = new Phase6Renderer();

        Publisher = new ArtworkPublisher(Host, Host, Artifacts, States, Operations, _configuration.Limits, Fences);
        Reconciler = new ArtworkReconciler(Host, States, Operations, Publisher);
        Coordinator = new ArtworkGenerationCoordinator(Host, Renderer, Publisher, States, Artifacts);
        Gate = new ArtworkRecoveryGate(Operations, Reconciler, Fences);
        Lifecycle = new ArtworkLifecycleCoordinator(
            Host,
            States,
            Operations,
            Reconciler,
            Publisher,
            Fences,
            new Phase6FenceProvider());

        Reconciliation = new MetadataReconciliationProcessor(
            Configuration,
            Library,
            new IArrMetadataReader[] { Reader },
            Metadata);
        Publishing = new ArtworkPublishingWorkItemProcessor(Reconciliation, Coordinator, States, Configuration);
        Recovering = new ArtworkRecoveringWorkItemProcessor(Publishing, Gate);

        Queue = new LibraryWorkQueue(() => Configuration.Current.Limits);

        SeedMovie(ItemId);
    }

    /// <summary>Gets the plugin data root.</summary>
    public string Root { get; }

    /// <summary>Gets the seeded Jellyfin movie identifier.</summary>
    public Guid ItemId { get; }

    /// <summary>Gets the seeded library identifier.</summary>
    public Guid LibraryId { get; }

    /// <summary>Gets the configuration snapshot service.</summary>
    public ConfigurationSnapshotService Configuration { get; }

    /// <summary>Gets the versioned state repository.</summary>
    public StateRepository Repository { get; }

    /// <summary>Gets the metadata state store.</summary>
    public MetadataStateStore Metadata { get; }

    /// <summary>Gets the published artwork state store.</summary>
    public PublishedArtworkStateStore States { get; }

    /// <summary>Gets the durable artwork operation store.</summary>
    public ArtworkOperationStore Operations { get; }

    /// <summary>Gets the retained artifact store.</summary>
    public SourceArtifactStore Artifacts { get; }

    /// <summary>Gets the durable lifecycle fence store.</summary>
    public ArtworkLifecycleFenceStore Fences { get; }

    /// <summary>Gets the injectable Jellyfin library resolver.</summary>
    public ReconciliationLibraryResolver Library { get; }

    /// <summary>Gets the provider read double.</summary>
    public FakeMetadataReader Reader { get; }

    /// <summary>Gets the injectable host source/image boundary.</summary>
    public Phase6Host Host { get; }

    /// <summary>Gets the injectable renderer.</summary>
    public Phase6Renderer Renderer { get; }

    /// <summary>Gets the durable publisher.</summary>
    public ArtworkPublisher Publisher { get; }

    /// <summary>Gets the restart reconciler.</summary>
    public ArtworkReconciler Reconciler { get; }

    /// <summary>Gets the artwork generation coordinator.</summary>
    public ArtworkGenerationCoordinator Coordinator { get; }

    /// <summary>Gets the per-subject recovery gate.</summary>
    public ArtworkRecoveryGate Gate { get; }

    /// <summary>Gets the lifecycle drain coordinator.</summary>
    public ArtworkLifecycleCoordinator Lifecycle { get; }

    /// <summary>Gets the metadata reconciliation processor.</summary>
    public MetadataReconciliationProcessor Reconciliation { get; }

    /// <summary>Gets the composed metadata+artwork processor.</summary>
    public ArtworkPublishingWorkItemProcessor Publishing { get; }

    /// <summary>Gets the composed recovery+metadata+artwork processor.</summary>
    public ArtworkRecoveringWorkItemProcessor Recovering { get; }

    /// <summary>Gets the bounded work queue.</summary>
    public LibraryWorkQueue Queue { get; }

    /// <summary>
    /// Builds a fresh Phase 6 graph over the same durable directory. Metadata
    /// state, freshness, published artwork state, retained artifacts, the
    /// lifecycle fence, and recovery records all survive; only in-memory queue,
    /// worker, and service state is discarded. The active host bytes are copied so
    /// the simulated host restart observes the same active image.
    /// </summary>
    /// <returns>The restarted harness.</returns>
    public Phase6Harness Restart()
    {
        var restarted = new Phase6Harness(
            Root,
            _configuration,
            ItemId,
            LibraryId,
            ownsRoot: false,
            copyBytesFrom: Host);

        restarted.Reader.Handler = Reader.Handler;
        restarted.Reader.Result = Reader.Result;
        return restarted;
    }

    /// <summary>
    /// Seeds a Jellyfin movie and its library mapping so the item is eligible for
    /// reconciliation.
    /// </summary>
    /// <param name="itemId">The item identifier.</param>
    public void SeedMovie(Guid itemId)
    {
        Library.LibraryIds[itemId] = LibraryId;
        Library.Items[itemId] = ReconciliationFixtures.Movie(itemId, LibraryId);
    }

    /// <summary>
    /// Creates the queued work item for the seeded item at the current
    /// configuration generation.
    /// </summary>
    /// <param name="itemId">The item identifier, or the seeded item when omitted.</param>
    /// <returns>The bounded work item.</returns>
    public LibraryWorkItem WorkItem(Guid? itemId = null)
    {
        var key = new WorkItemKey(itemId ?? ItemId, null, Surface);
        return new LibraryWorkItem(key, LibraryWorkReason.Updated, Configuration.Current.ConfigurationVersion);
    }

    /// <summary>
    /// Creates the bounded work hint for the seeded item.
    /// </summary>
    /// <param name="itemId">The item identifier, or the seeded item when omitted.</param>
    /// <param name="reason">The work reason.</param>
    /// <returns>The bounded hint.</returns>
    public LibraryWorkHint Hint(Guid? itemId = null, LibraryWorkReason reason = LibraryWorkReason.Updated)
    {
        return new LibraryWorkHint(itemId ?? ItemId, reason, Configuration.Current.ConfigurationVersion);
    }

    /// <summary>
    /// Creates a lifecycle worker over this harness queue with an optional bounded
    /// shutdown timeout and worker count.
    /// </summary>
    /// <param name="processor">The processor to drive, or the composed recovery pipeline.</param>
    /// <param name="workerCount">The bounded worker count.</param>
    /// <param name="shutdownTimeout">The bounded shutdown timeout.</param>
    /// <returns>The hosted worker.</returns>
    public LibraryWorkWorker CreateWorker(
        IWorkItemProcessor? processor = null,
        int workerCount = 2,
        TimeSpan? shutdownTimeout = null)
    {
        return new LibraryWorkWorker(
            Queue,
            processor ?? Recovering,
            Configuration,
            workerCount,
            shutdownTimeout ?? TimeSpan.FromSeconds(2));
    }

    /// <summary>
    /// Creates the default production read handler that returns a matched Radarr
    /// movie with the supplied quality label. The provider version is constant, so
    /// the canonical metadata fingerprint is deterministic across a restart.
    /// </summary>
    /// <param name="qualityLabel">The quality label to report.</param>
    /// <returns>The reader handler.</returns>
    public static Func<MediaIdentity, ArrConnection, CancellationToken, ArrMetadataReadResult> Matched(
        string qualityLabel = "Bluray-1080p")
    {
        return (identity, connection, _) =>
        {
            var record = new RadarrIdentity(connection.ConnectionId, 42, ArrFileIdentity.Present(84));
            var match = new MediaMatch(
                identity,
                connection.Provider,
                connection.ConnectionId,
                MediaMatchStatus.Matched,
                MediaMatchMethod.ProviderId,
                record,
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["tmdb"] = "603" });
            var metadata = new BadgeMetadata(
                connection.Provider,
                record,
                DateTimeOffset.UtcNow,
                quality: new ArrQualityDescriptor(qualityLabel, "bluray", 1080, "Remux", 7),
                videoCodec: "h264");
            return ArrMetadataReadResult.Success(match, metadata);
        };
    }

    /// <summary>
    /// Creates a transient provider-unavailable read failure.
    /// </summary>
    /// <returns>The failed read result.</returns>
    public static ArrMetadataReadResult ProviderUnavailable()
    {
        return ArrMetadataReadResult.Failure(new ArrProviderError(
            ArrProviderErrorCode.ProviderUnavailable,
            ArrErrorRetryability.Later,
            "The provider was unavailable."));
    }

    /// <summary>
    /// Creates deterministic original source bytes.
    /// </summary>
    /// <returns>The bytes.</returns>
    public static byte[] Original()
    {
        return System.Text.Encoding.UTF8.GetBytes("phase6-original-source-poster-bytes");
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (!_ownsRoot)
        {
            return;
        }

        try
        {
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, recursive: true);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}

/// <summary>
/// Shared bounded polling helpers for the task 6.8 tests.
/// </summary>
internal static class Phase6Wait
{
    /// <summary>
    /// Polls a condition until it holds or a bounded timeout elapses.
    /// </summary>
    /// <param name="condition">The condition.</param>
    /// <param name="timeoutSeconds">The bounded timeout in seconds.</param>
    /// <returns>A task that completes when the condition holds.</returns>
    public static async Task UntilAsync(Func<bool> condition, int timeoutSeconds = 10)
    {
        var deadline = DateTime.UtcNow.AddSeconds(timeoutSeconds);
        while (!condition() && DateTime.UtcNow < deadline)
        {
            await Task.Delay(10).ConfigureAwait(false);
        }

        Xunit.Assert.True(condition(), "The expected condition was not observed within the bounded test timeout.");
    }
}
