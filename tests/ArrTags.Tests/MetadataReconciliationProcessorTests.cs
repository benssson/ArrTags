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
        Assert.NotNull(stored.Value.ExpiresAt);
        Assert.NotNull(stored.Value.StaleUntil);
        Assert.True(stored.Value.StaleUntil > stored.Value.ExpiresAt);
        Assert.True(stored.Value.IsUsableAsCurrent(DateTimeOffset.UtcNow));
        Assert.Equal(MetadataFreshness.Fresh, stored.Value.EvaluateFreshness(DateTimeOffset.UtcNow));
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
    public async Task TransientReaderFailureWithinTheWindowKeepsLastKnownGoodAsStale()
    {
        var seeded = SeedLastKnownGood(DateTimeOffset.UtcNow);
        _reader.Result = ArrMetadataReadResult.Failure(new ArrProviderError(
            ArrProviderErrorCode.ProviderUnavailable,
            ArrErrorRetryability.Later,
            "The provider was unavailable."));

        var result = await _processor.ProcessAsync(WorkItem(_configuration.Current.ConfigurationVersion), default);

        Assert.False(result.IsSuccess);
        Assert.True(result.IsRetryable);
        var stored = _store.Read(ReconciliationFixtures.ItemId, ArrProviderKind.Radarr);
        Assert.Equal(StateReadStatus.Found, stored.Status);
        Assert.Equal(MetadataStateKind.Stale, stored.Value!.State);
        Assert.Equal(seeded.MetadataFingerprint, stored.Value.MetadataFingerprint);
        Assert.Equal(seeded.ExpiresAt, stored.Value.ExpiresAt);
        Assert.Equal(seeded.StaleUntil, stored.Value.StaleUntil);
        Assert.Equal(MetadataFreshness.Stale, stored.Value.EvaluateFreshness(DateTimeOffset.UtcNow));
        Assert.True(stored.Value.IsUsableAsCurrent(DateTimeOffset.UtcNow));
    }

    [Fact]
    public async Task TransientReaderFailureAfterTheWindowDoesNotRefreshStaleMetadata()
    {
        // The seeded last-known-good window ended before the outage, so it must
        // never be kept or refreshed as current metadata.
        var seeded = SeedLastKnownGood(DateTimeOffset.UtcNow.AddDays(-3));
        Assert.False(seeded.IsUsableAsCurrent(DateTimeOffset.UtcNow));
        _reader.Result = ArrMetadataReadResult.Failure(new ArrProviderError(
            ArrProviderErrorCode.ProviderUnavailable,
            ArrErrorRetryability.Later,
            "The provider was unavailable."));

        var result = await _processor.ProcessAsync(WorkItem(_configuration.Current.ConfigurationVersion), default);

        Assert.False(result.IsSuccess);
        Assert.True(result.IsRetryable);
        var stored = _store.Read(ReconciliationFixtures.ItemId, ArrProviderKind.Radarr);
        Assert.Equal(StateReadStatus.Found, stored.Status);
        Assert.Equal(seeded.MetadataFingerprint, stored.Value!.MetadataFingerprint);
        Assert.False(stored.Value.IsUsableAsCurrent(DateTimeOffset.UtcNow));
        Assert.Equal(MetadataFreshness.Expired, stored.Value.EvaluateFreshness(DateTimeOffset.UtcNow));
    }

    [Fact]
    public async Task TransientReaderFailureWithoutLastKnownGoodKeepsNoMetadata()
    {
        _reader.Result = ArrMetadataReadResult.Failure(new ArrProviderError(
            ArrProviderErrorCode.ProviderUnavailable,
            ArrErrorRetryability.Later,
            "The provider was unavailable."));

        var result = await _processor.ProcessAsync(WorkItem(_configuration.Current.ConfigurationVersion), default);

        Assert.True(result.IsRetryable);
        Assert.Equal(StateReadStatus.Missing, _store.Read(ReconciliationFixtures.ItemId, ArrProviderKind.Radarr).Status);
    }

    [Fact]
    public void RegistratorWiresTheRealReconciliationProcessor()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ILibraryEventSource>(new FakeLibraryEventSource());

        new ArrTagsServiceRegistrator().RegisterServices(services, null!);

        // Task 6.4 composes the real metadata processor with the per-subject
        // artwork recovery gate behind the IWorkItemProcessor boundary; the
        // metadata processor remains artwork-free and is registered concretely.
        var processor = Assert.Single(services, descriptor => descriptor.ServiceType == typeof(IWorkItemProcessor));
        Assert.NotNull(processor.ImplementationFactory);
        Assert.Contains(
            services,
            descriptor => descriptor.ServiceType == typeof(MetadataReconciliationProcessor));
        Assert.Contains(
            services,
            descriptor => descriptor.ServiceType == typeof(IArtworkRecoveryGate)
                && descriptor.ImplementationFactory is not null);
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

    private MetadataStateEntry SeedLastKnownGood(DateTimeOffset fetchedAt)
    {
        var identity = ReconciliationFixtures.MovieIdentity();
        var match = ReconciliationFixtures.MatchedMovieMatch(identity);
        var entry = MetadataStateEntry.From(
            identity,
            match,
            ReconciliationFixtures.MovieMetadata(identity, match.RecordIdentity),
            fetchedAt,
            staleWindow: TimeSpan.FromMinutes(_configuration.Current.Limits.MetadataStaleWindowMinutes));
        _store.Write(entry);
        return entry;
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
