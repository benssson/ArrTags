using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ArrTags.Artwork;
using ArrTags.Configuration;
using ArrTags.Matching;
using ArrTags.Media;
using ArrTags.Metadata;
using ArrTags.PluginLifecycle;
using ArrTags.Providers;
using ArrTags.Reconciliation;
using ArrTags.Rendering;
using ArrTags.State;
using ArrTags.Updates;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ArrTags.Tests;

/// <summary>
/// Task 6.6 focused checks for the Phase 6 artwork publication pipeline: after
/// metadata state is published, the publication-fingerprint gate decides whether
/// artwork must be regenerated, and only a changed fingerprint drives the real
/// generation coordinator and durable publisher. The host source/image boundary
/// and the renderer are injected doubles, so no live Jellyfin host and no native
/// Skia runtime are required.
/// </summary>
public sealed class ArtworkPublicationPipelineTests : IDisposable
{
    private static readonly ArtworkImageSurface Surface = ArtworkImageSurface.Primary;
    private static readonly Guid Item = ReconciliationFixtures.ItemId;

    private readonly string _root;
    private readonly StateRepository _repository;
    private readonly MetadataStateStore _metadataStore;
    private readonly SourceArtifactStore _artifacts;
    private readonly PublishedArtworkStateStore _states;
    private readonly ArtworkOperationStore _operations;
    private readonly ReconciliationLibraryResolver _resolver = new();
    private readonly FakeMetadataReader _reader = new() { Kind = ArrProviderKind.Radarr };
    private readonly PipelineArtworkHost _host = new();
    private readonly FingerprintingRenderer _renderer = new();
    private ConfigurationSnapshotService _configuration;
    private ArtworkPublishingWorkItemProcessor _processor;

    public ArtworkPublicationPipelineTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "arrtags-pipeline-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _repository = new StateRepository(_root);
        _metadataStore = new MetadataStateStore(_repository);
        _artifacts = new SourceArtifactStore(_repository);
        _states = new PublishedArtworkStateStore(_repository);
        _operations = new ArtworkOperationStore(_repository);
        _configuration = ReconciliationFixtures.Configuration();
        SeedEligibleMovie();
        _reader.Handler = (identity, connection, _) => Matched(identity, connection, "Bluray-1080p");
        _processor = CreateProcessor();
    }

    [Fact]
    public async Task UnchangedRepeatPerformsNoRenderAndNoImageMutation()
    {
        var original = Original();
        _host.CurrentBytes = original;

        var first = await _processor.ProcessAsync(WorkItem(_configuration.Current.ConfigurationVersion), default);
        Assert.True(first.IsSuccess);
        Assert.Equal(1, _renderer.Calls);
        Assert.Equal(1, _host.SaveCalls);

        var published = _states.Read(Item, Surface).Value!;
        Assert.Equal(ArtworkPublicationState.Published, published.State);

        var second = await _processor.ProcessAsync(WorkItem(_configuration.Current.ConfigurationVersion), default);

        Assert.True(second.IsSuccess);
        Assert.Equal(1, _renderer.Calls);
        Assert.Equal(1, _host.SaveCalls);

        var after = _states.Read(Item, Surface).Value!;
        Assert.Equal(published.PublishedFingerprint, after.PublishedFingerprint);
        Assert.Equal(published.StateRevision, after.StateRevision);
    }

    [Fact]
    public async Task ChangedMetadataFingerprintRegeneratesFromTheRetainedOriginalWithoutDoubleBadging()
    {
        var original = Original();
        _host.CurrentBytes = original;
        await _processor.ProcessAsync(WorkItem(_configuration.Current.ConfigurationVersion), default);

        var first = _states.Read(Item, Surface).Value!;
        var derived1 = _host.CurrentBytes!;
        var derived1Fingerprint = ArtworkHashes.ComputeSha256(derived1);
        Assert.NotEqual(ArtworkHashes.ComputeSha256(original), derived1Fingerprint);

        _reader.Handler = (identity, connection, _) => Matched(identity, connection, "Bluray-2160p");

        await _processor.ProcessAsync(WorkItem(_configuration.Current.ConfigurationVersion), default);

        Assert.Equal(2, _renderer.Calls);
        Assert.Equal(2, _host.SaveCalls);

        var second = _states.Read(Item, Surface).Value!;
        Assert.NotEqual(first.PublishedFingerprint, second.PublishedFingerprint);

        // The repeat publication rendered from the retained original artifact, not
        // from the previous ArrTags output.
        var originalSha = ArtworkHashes.ComputeSha256(original);
        Assert.Equal(originalSha, _renderer.LastRequest!.SourceImage!.SourceSha256);
        Assert.Equal(originalSha, second.SourceFingerprint);

        // Provenance is reused: the original artifact and ownership session are preserved.
        Assert.Equal(first.SourceArtifactId, second.SourceArtifactId);
        Assert.Equal(first.OwnershipToken, second.OwnershipToken);
        Assert.NotEqual(first.PublicationToken, second.PublicationToken);
        Assert.Equal(SourceArtifactReadStatus.Found, _artifacts.Read(second.SourceArtifactId!).Status);

        // No double badge: the published output is derived from the original source,
        // not from the first ArrTags output.
        var rendered = Encoding.UTF8.GetString(_host.CurrentBytes!);
        Assert.Contains(originalSha, rendered, StringComparison.Ordinal);
        Assert.DoesNotContain(derived1Fingerprint, rendered, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ChangedConfigurationFingerprintRegeneratesWithoutLosingProvenance()
    {
        _host.CurrentBytes = Original();
        await _processor.ProcessAsync(WorkItem(_configuration.Current.ConfigurationVersion), default);
        var first = _states.Read(Item, Surface).Value!;

        var replacement = new PluginConfiguration
        {
            BadgeMoviePosters = true,
            Radarr = new ArrConnectionConfiguration
            {
                Enabled = true,
                BaseUrl = "http://radarr.test",
                ApiKey = "replacement-api-key",
            },
        };
        replacement.Renderer.TechnicalBackground = "#000000";
        Assert.True(_configuration.TryReplace(replacement, out var result));
        Assert.True(result.IsValid);
        _processor = CreateProcessor();

        await _processor.ProcessAsync(WorkItem(_configuration.Current.ConfigurationVersion), default);

        Assert.Equal(2, _renderer.Calls);
        Assert.Equal(2, _host.SaveCalls);

        var second = _states.Read(Item, Surface).Value!;
        Assert.NotEqual(first.PublishedFingerprint, second.PublishedFingerprint);
        Assert.Equal(first.SourceArtifactId, second.SourceArtifactId);
        Assert.Equal(first.OwnershipToken, second.OwnershipToken);
        Assert.Equal("#000000", _renderer.LastRequest!.OutputPolicy.TechnicalBackground);
        Assert.Equal(_configuration.Current.RendererConfigurationFingerprint, _renderer.LastRequest.ConfigurationFingerprint);
        Assert.Equal(SourceArtifactReadStatus.Found, _artifacts.Read(first.SourceArtifactId!).Status);
    }

    [Fact]
    public async Task UnusableMetadataDoesNotRenderOrPublishAndRetainsCurrentArtwork()
    {
        _host.CurrentBytes = Original();
        await _processor.ProcessAsync(WorkItem(_configuration.Current.ConfigurationVersion), default);
        var first = _states.Read(Item, Surface).Value!;
        var renderCallsAfterFirst = _renderer.Calls;
        var saveCallsAfterFirst = _host.SaveCalls;

        // A matched record whose metadata is not usable as current (matched but no
        // observed metadata) must never drive a render or a mutation.
        _reader.Handler = (identity, connection, _) => ArrMetadataReadResult.Success(MatchedMatch(identity, connection));

        var second = await _processor.ProcessAsync(WorkItem(_configuration.Current.ConfigurationVersion), default);

        Assert.True(second.IsSuccess);
        Assert.Equal(renderCallsAfterFirst, _renderer.Calls);
        Assert.Equal(saveCallsAfterFirst, _host.SaveCalls);

        var after = _states.Read(Item, Surface).Value!;
        Assert.Equal(first.PublishedFingerprint, after.PublishedFingerprint);
        Assert.Equal(first.SourceArtifactId, after.SourceArtifactId);
    }

    [Fact]
    public async Task NonTerminalOperationBlocksPublicationAndIsNotDestroyed()
    {
        _host.CurrentBytes = Original();
        _operations.Write(ArtworkOperationFixtures.Publication(
            item: Item,
            surface: Surface,
            phase: ArtworkOperationPhase.Prepared));

        var result = await _processor.ProcessAsync(WorkItem(_configuration.Current.ConfigurationVersion), default);

        Assert.True(result.IsSuccess);
        Assert.Equal(0, _host.SaveCalls);

        var operation = _operations.Read(Item, Surface).Value!;
        Assert.Equal(ArtworkOperationPhase.Prepared, operation.Phase);
        Assert.Equal(StateReadStatus.Missing, _states.Read(Item, Surface).Status);
    }

    [Fact]
    public void RegistratorComposesRecoveryOverTheArtworkPublishingPipeline()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ILibraryEventSource>(new FakeLibraryEventSource());

        new ArrTags.PluginLifecycle.ArrTagsServiceRegistrator().RegisterServices(services, null!);

        Assert.Contains(
            services,
            descriptor => descriptor.ServiceType == typeof(ArtworkPublishingWorkItemProcessor)
                && descriptor.ImplementationFactory is not null);
        Assert.Contains(
            services,
            descriptor => descriptor.ServiceType == typeof(IWorkItemProcessor)
                && descriptor.ImplementationFactory is not null);
        Assert.Contains(
            services,
            descriptor => descriptor.ServiceType == typeof(MetadataReconciliationProcessor));
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private ArtworkPublishingWorkItemProcessor CreateProcessor()
    {
        var reconciliation = new MetadataReconciliationProcessor(
            _configuration,
            _resolver,
            new IArrMetadataReader[] { _reader },
            _metadataStore);
        var publisher = new ArtworkPublisher(_host, _host, _artifacts, _states, _operations, new OperationalLimits());
        var coordinator = new ArtworkGenerationCoordinator(_host, _renderer, publisher, _states, _artifacts);
        return new ArtworkPublishingWorkItemProcessor(reconciliation, coordinator, _states, _configuration);
    }

    private void SeedEligibleMovie()
    {
        _resolver.LibraryIds[Item] = ReconciliationFixtures.LibraryId;
        _resolver.Items[Item] = ReconciliationFixtures.Movie(Item, ReconciliationFixtures.LibraryId);
    }

    private static ArrMetadataReadResult Matched(
        MediaIdentity identity,
        ArrConnection connection,
        string qualityLabel)
    {
        var metadata = new BadgeMetadata(
            connection.Provider,
            MatchedRecord(connection),
            DateTimeOffset.UtcNow,
            quality: new ArrQualityDescriptor(qualityLabel, "bluray", 1080, "Remux", 7),
            videoCodec: "h264");
        return ArrMetadataReadResult.Success(MatchedMatch(identity, connection), metadata);
    }

    private static MediaMatch MatchedMatch(MediaIdentity identity, ArrConnection connection)
    {
        return new MediaMatch(
            identity,
            connection.Provider,
            connection.ConnectionId,
            MediaMatchStatus.Matched,
            MediaMatchMethod.ProviderId,
            MatchedRecord(connection),
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["tmdb"] = "603" });
    }

    private static RadarrIdentity MatchedRecord(ArrConnection connection)
    {
        return new RadarrIdentity(connection.ConnectionId, 42, ArrFileIdentity.Present(84));
    }

    private static LibraryWorkItem WorkItem(long configurationVersion)
    {
        var key = new WorkItemKey(Item, null, Surface);
        return new LibraryWorkItem(key, LibraryWorkReason.Updated, configurationVersion);
    }

    private static byte[] Original()
    {
        return Encoding.UTF8.GetBytes("original-source-poster-bytes");
    }
}
