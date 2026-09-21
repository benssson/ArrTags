using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
using ArrTags.State;
using ArrTags.Updates;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ArrTags.Tests;

/// <summary>
/// Focused checks for the task 6.3 reconciliation processor: provider-neutral
/// matching and metadata publication, atomic publish only after the current item
/// and configuration are re-validated, and discard of stale long-running work.
/// No live Jellyfin or Arr instance is required.
/// </summary>
public sealed class MetadataReconciliationProcessorTests : IDisposable
{
    private readonly string _root;
    private readonly StateRepository _repository;
    private readonly MetadataStateStore _store;
    private readonly ReconciliationLibraryResolver _resolver = new();
    private readonly FakeMetadataReader _reader = new() { Kind = ArrProviderKind.Radarr };
    private ConfigurationSnapshotService _configuration;
    private MetadataReconciliationProcessor _processor;

    public MetadataReconciliationProcessorTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "arrtags-metadata-reconcile-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _repository = new StateRepository(_root);
        _store = new MetadataStateStore(_repository);
        _configuration = ReconciliationFixtures.Configuration();
        _processor = CreateProcessor();
        SeedEligibleMovie();
    }

    [Fact]
    public async Task PublishesAFreshStateForAMatchedEligibleItem()
    {
        _reader.Handler = MatchedMovie;

        var result = await _processor.ProcessAsync(WorkItem(_configuration.Current.ConfigurationVersion), default);

        Assert.True(result.IsSuccess);
        var stored = _store.Read(ReconciliationFixtures.ItemId, ArrProviderKind.Radarr);
        Assert.Equal(StateReadStatus.Found, stored.Status);
        Assert.Equal(MetadataStateKind.Fresh, stored.Value!.State);
        Assert.Equal(MediaMatchStatus.Matched, stored.Value.MatchStatus);
        Assert.False(string.IsNullOrEmpty(stored.Value.MetadataFingerprint));
        Assert.Equal("Bluray-1080p", stored.Value.Metadata!.QualityLabel);
        Assert.NotNull(stored.Value.FetchedAt);
    }

    [Fact]
    public async Task PublishesAnUnmatchedStateWhenNoRecordMatches()
    {
        _reader.Handler = static (identity, connection, _) => ArrMetadataReadResult.Success(new MediaMatch(
            identity,
            connection.Provider,
            connection.ConnectionId,
            MediaMatchStatus.NotFound,
            MediaMatchMethod.None,
            ambiguityReason: "No candidate matched."));

        var result = await _processor.ProcessAsync(WorkItem(_configuration.Current.ConfigurationVersion), default);

        Assert.True(result.IsSuccess);
        var stored = _store.Read(ReconciliationFixtures.ItemId, ArrProviderKind.Radarr);
        Assert.Equal(StateReadStatus.Found, stored.Status);
        Assert.Equal(MetadataStateKind.Unmatched, stored.Value!.State);
        Assert.Null(stored.Value.Metadata);
    }

    [Fact]
    public async Task DiscardsWhenTheConfigurationVersionAdvancedBeforeProcessing()
    {
        _reader.Handler = MatchedMovie;
        var stale = WorkItem(_configuration.Current.ConfigurationVersion);
        Assert.True(_configuration.TryReplace(ReplacementConfiguration(), out _));

        var result = await _processor.ProcessAsync(stale, default);

        Assert.True(result.IsSuccess);
        Assert.Equal(StateReadStatus.Missing, _store.Read(ReconciliationFixtures.ItemId, ArrProviderKind.Radarr).Status);
    }

    [Fact]
    public async Task DiscardsWhenTheConfigurationChangesWhileProcessing()
    {
        _reader.Handler = MatchedMovie;
        _reader.OnRead = () => _configuration.TryReplace(ReplacementConfiguration(), out _);

        var result = await _processor.ProcessAsync(WorkItem(_configuration.Current.ConfigurationVersion), default);

        Assert.True(result.IsSuccess);
        Assert.Equal(StateReadStatus.Missing, _store.Read(ReconciliationFixtures.ItemId, ArrProviderKind.Radarr).Status);
    }

    [Fact]
    public async Task DiscardsWhenTheConnectionIsDisabled()
    {
        _configuration = ReconciliationFixtures.Configuration(radarrEnabled: false);
        _processor = CreateProcessor();
        _reader.Handler = MatchedMovie;

        var result = await _processor.ProcessAsync(WorkItem(_configuration.Current.ConfigurationVersion), default);

        Assert.True(result.IsSuccess);
        Assert.Equal(StateReadStatus.Missing, _store.Read(ReconciliationFixtures.ItemId, ArrProviderKind.Radarr).Status);
    }

    [Fact]
    public async Task DiscardsWhenTheItemIsAbsent()
    {
        _resolver.Items.Clear();
        _reader.Handler = MatchedMovie;

        var result = await _processor.ProcessAsync(WorkItem(_configuration.Current.ConfigurationVersion), default);

        Assert.True(result.IsSuccess);
        Assert.Equal(StateReadStatus.Missing, _store.Read(ReconciliationFixtures.ItemId, ArrProviderKind.Radarr).Status);
    }

    [Fact]
    public async Task DiscardsWhenTheItemIsNoLongerEligible()
    {
        _configuration = ReconciliationFixtures.Configuration(badgeMoviePosters: false);
        _processor = CreateProcessor();
        _reader.Handler = MatchedMovie;

        var result = await _processor.ProcessAsync(WorkItem(_configuration.Current.ConfigurationVersion), default);

        Assert.True(result.IsSuccess);
        Assert.Equal(StateReadStatus.Missing, _store.Read(ReconciliationFixtures.ItemId, ArrProviderKind.Radarr).Status);
    }

    [Fact]
    public async Task DiscardsWhenTheItemChangesWhileProcessing()
    {
        _reader.Handler = MatchedMovie;
        _reader.OnRead = () => _resolver.Items.Remove(ReconciliationFixtures.ItemId);

        var result = await _processor.ProcessAsync(WorkItem(_configuration.Current.ConfigurationVersion), default);

        Assert.True(result.IsSuccess);
        Assert.Equal(StateReadStatus.Missing, _store.Read(ReconciliationFixtures.ItemId, ArrProviderKind.Radarr).Status);
    }

    [Fact]
    public async Task DiscardDoesNotOverwriteAnExistingPublishedState()
    {
        var existing = ReconciliationFixtures.MatchedEntry();
        _store.Write(existing);

        _reader.Handler = MatchedMovie;
        _reader.OnRead = () => _resolver.Items.Remove(ReconciliationFixtures.ItemId);

        await _processor.ProcessAsync(WorkItem(_configuration.Current.ConfigurationVersion), default);

        var stored = _store.Read(ReconciliationFixtures.ItemId, ArrProviderKind.Radarr);
        Assert.Equal(StateReadStatus.Found, stored.Status);
        Assert.Equal(existing.MetadataFingerprint, stored.Value!.MetadataFingerprint);
    }

    [Fact]
    public async Task TransientReaderFailureIsRetryableAndDoesNotPublish()
    {
        _reader.Result = ArrMetadataReadResult.Failure(new ArrProviderError(
            ArrProviderErrorCode.ProviderUnavailable,
            ArrErrorRetryability.Later,
            "The provider was unavailable."));

        var result = await _processor.ProcessAsync(WorkItem(_configuration.Current.ConfigurationVersion), default);

        Assert.False(result.IsSuccess);
        Assert.True(result.IsRetryable);
        Assert.Equal(StateReadStatus.Missing, _store.Read(ReconciliationFixtures.ItemId, ArrProviderKind.Radarr).Status);
    }

    [Fact]
    public async Task TerminalReaderFailureIsNotRetriedAndDoesNotPublish()
    {
        _reader.Result = ArrMetadataReadResult.Failure(new ArrProviderError(
            ArrProviderErrorCode.AuthenticationFailed,
            ArrErrorRetryability.Never,
            "The provider rejected the credentials."));

        var result = await _processor.ProcessAsync(WorkItem(_configuration.Current.ConfigurationVersion), default);

        Assert.False(result.IsSuccess);
        Assert.False(result.IsRetryable);
        Assert.Equal(StateReadStatus.Missing, _store.Read(ReconciliationFixtures.ItemId, ArrProviderKind.Radarr).Status);
    }

    [Fact]
    public void RegistratorWiresTheRealReconciliationProcessor()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ILibraryEventSource>(new FakeLibraryEventSource());

        new ArrTagsServiceRegistrator().RegisterServices(services, null!);

        Assert.Contains(
            services,
            descriptor => descriptor.ServiceType == typeof(IWorkItemProcessor)
                && descriptor.ImplementationType == typeof(MetadataReconciliationProcessor));
        Assert.DoesNotContain(
            services,
            descriptor => descriptor.ImplementationType is { } implementation
                && implementation.Name == "DeferredWorkItemProcessor");
        Assert.Equal(2, services.Count(descriptor => descriptor.ServiceType == typeof(IArrMetadataReader)));
    }

    [Fact]
    public void ProcessorDoesNotDependOnArtworkPublicationTypes()
    {
        var constructorTypes = typeof(MetadataReconciliationProcessor)
            .GetConstructors()
            .SelectMany(constructor => constructor.GetParameters())
            .Select(parameter => parameter.ParameterType)
            .ToArray();

        Assert.DoesNotContain(constructorTypes, DependsOnArtwork);
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

    private static bool DependsOnArtwork(Type type)
    {
        if (type.Namespace is { } space && space.StartsWith("ArrTags.Artwork", StringComparison.Ordinal))
        {
            return true;
        }

        return type.IsGenericType && type.GetGenericArguments().Any(DependsOnArtwork);
    }

    private static ArrMetadataReadResult MatchedMovie(
        MediaIdentity identity,
        ArrConnection connection,
        CancellationToken cancellationToken)
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
            quality: new ArrQualityDescriptor("Bluray-1080p", "bluray", 1080, "Remux", 7),
            videoCodec: "h264");
        return ArrMetadataReadResult.Success(match, metadata);
    }

    private MetadataReconciliationProcessor CreateProcessor()
    {
        return new MetadataReconciliationProcessor(
            _configuration,
            _resolver,
            new IArrMetadataReader[] { _reader },
            _store);
    }

    private void SeedEligibleMovie()
    {
        _resolver.LibraryIds[ReconciliationFixtures.ItemId] = ReconciliationFixtures.LibraryId;
        _resolver.Items[ReconciliationFixtures.ItemId] =
            ReconciliationFixtures.Movie(ReconciliationFixtures.ItemId, ReconciliationFixtures.LibraryId);
    }

    private static PluginConfiguration ReplacementConfiguration()
    {
        return new PluginConfiguration
        {
            BadgeMoviePosters = true,
            Radarr = new ArrConnectionConfiguration
            {
                Enabled = true,
                BaseUrl = "http://radarr.test",
                ApiKey = "replacement-api-key",
            },
        };
    }

    private static LibraryWorkItem WorkItem(long configurationVersion)
    {
        var key = new WorkItemKey(
            ReconciliationFixtures.ItemId,
            null,
            ArtworkImageSurface.Primary);
        return new LibraryWorkItem(key, LibraryWorkReason.Updated, configurationVersion);
    }
}
