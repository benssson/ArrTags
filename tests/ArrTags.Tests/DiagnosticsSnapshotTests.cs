using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using ArrTags.Artwork;
using ArrTags.Diagnostics;
using ArrTags.Matching;
using ArrTags.PluginLifecycle;
using ArrTags.Providers;
using ArrTags.Rendering;
using ArrTags.Updates;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ArrTags.Tests;

/// <summary>
/// Task 17.1 focused checks for the bounded diagnostics snapshot model: the
/// fixed counter set, the bounded and secret-free member graph, the queue
/// observation, the thread-safe counters, and the DI registration. No live
/// Jellyfin or Arr instance is required.
/// </summary>
public sealed class DiagnosticsSnapshotTests
{
    private static readonly Type[] SnapshotModelTypes =
    {
        typeof(DiagnosticsSnapshot),
        typeof(MatchingFailureCounts),
        typeof(RenderFailureCounts),
    };

    [Fact]
    public void SnapshotExposesExactlyTheFixedCounterSet()
    {
        var names = typeof(DiagnosticsSnapshot)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(property => property.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(
            new[]
            {
                "CacheHits",
                "CacheMisses",
                "MatchingFailures",
                "QueueDepth",
                "QueueInFlight",
                "RadarrHealth",
                "RenderFailures",
                "SonarrHealth",
                "StaleMetadataTransitions",
            },
            names);
    }

    [Fact]
    public void MatchingFailureCountsExposeTheBoundedClassificationSet()
    {
        var names = typeof(MatchingFailureCounts)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(property => property.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(new[] { "Ambiguous", "NotFound", "Unsupported" }, names);
    }

    [Fact]
    public void RenderFailureCountsExposeOneCountPerDeclaredReason()
    {
        var reasons = Enum.GetValues<RenderFailureReason>();

        // The counters are stored and captured by the declared numeric value, so
        // the bounded classification set must stay contiguous; a gap would shift
        // every later classification silently.
        for (var index = 0; index < reasons.Length; index++)
        {
            Assert.Equal(index, (int)reasons[index]);
        }

        var names = typeof(RenderFailureCounts)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(property => property.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(
            reasons.Select(reason => reason.ToString()).OrderBy(name => name, StringComparer.Ordinal),
            names);
    }

    [Fact]
    public void SnapshotGraphCarriesOnlyCountsAndBoundedEnums()
    {
        // The snapshot is bounded by construction: every member type in the
        // closed model graph is an integer count, a bounded connection-health
        // enum, or one of the fixed-shape count records. A string, path, item
        // identity, collection, or array type cannot be added without failing
        // this assertion.
        var inspected = 0;
        foreach (var type in EnumeratePropertyGraph(typeof(DiagnosticsSnapshot)))
        {
            Assert.True(
                type == typeof(int)
                || type == typeof(long)
                || type == typeof(ArrConnectionHealth)
                || SnapshotModelTypes.Contains(type),
                $"The diagnostics snapshot graph contains the non-bounded type {type}.");

            inspected++;
        }

        Assert.True(inspected >= 4, "The snapshot graph must be inspectable.");
    }

    [Fact]
    public void SnapshotGraphExposesNoStringPathArrayOrCollectionCarrier()
    {
        var forbidden = new[]
        {
            typeof(string),
            typeof(Guid),
            typeof(Uri),
            typeof(DateTimeOffset),
        };

        var inspected = 0;
        foreach (var type in EnumeratePropertyGraph(typeof(DiagnosticsSnapshot)))
        {
            Assert.DoesNotContain(type, forbidden);
            Assert.False(type.IsArray, $"{type} exposes an array.");
            Assert.False(
                typeof(System.Collections.IEnumerable).IsAssignableFrom(type),
                $"{type} exposes a collection.");

            foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                inspected++;
                Assert.DoesNotContain(property.PropertyType, forbidden);
                Assert.False(property.PropertyType.IsArray, $"{type.Name}.{property.Name} is an array.");
                Assert.False(
                    typeof(System.Collections.IEnumerable).IsAssignableFrom(property.PropertyType),
                    $"{type.Name}.{property.Name} is a collection.");
            }
        }

        Assert.True(inspected > 0, "The snapshot model types must expose inspectable members.");
    }

    [Fact]
    public async Task QueueDepthAndInFlightComeFromTheBoundedWorkQueue()
    {
        var metrics = new DiagnosticsMetrics();
        using var queue = new LibraryWorkQueue(capacity: 8);
        var provider = new DiagnosticsSnapshotProvider(metrics, queue);
        var first = WorkItem(Guid.NewGuid(), LibraryWorkReason.Updated, 1);
        var second = WorkItem(Guid.NewGuid(), LibraryWorkReason.Added, 1);

        var empty = provider.GetSnapshot();
        Assert.Equal(0, empty.QueueDepth);
        Assert.Equal(0, empty.QueueInFlight);

        Assert.True(queue.TryEnqueue(first));
        Assert.True(queue.TryEnqueue(second));

        var queued = provider.GetSnapshot();
        Assert.Equal(2, queued.QueueDepth);
        Assert.Equal(0, queued.QueueInFlight);

        var dequeued = await queue.DequeueAsync(CancellationToken.None);

        var processing = provider.GetSnapshot();
        Assert.Equal(1, processing.QueueDepth);
        Assert.Equal(1, processing.QueueInFlight);

        queue.CompleteProcessing(dequeued.Key);
        Assert.Equal(0, provider.GetSnapshot().QueueInFlight);
    }

    [Fact]
    public void CountersAreThreadSafeAndReportExactTotals()
    {
        var metrics = new DiagnosticsMetrics();
        const int Iterations = 4096;

        Parallel.For(0, Iterations, _ =>
        {
            metrics.RecordCacheHit();
            metrics.RecordCacheMiss();
            metrics.RecordMatchingFailure(MediaMatchStatus.NotFound);
            metrics.RecordMatchingFailure(MediaMatchStatus.Ambiguous);
            metrics.RecordMatchingFailure(MediaMatchStatus.Unsupported);
            metrics.RecordRenderFailure(RenderFailureReason.DecodeFailed);
            metrics.RecordRenderFailure(RenderFailureReason.EncodeFailed);
            metrics.RecordStaleMetadata();
            metrics.RecordProviderHealth(ArrProviderKind.Sonarr, ArrConnectionHealth.Healthy);
            metrics.RecordProviderHealth(ArrProviderKind.Radarr, ArrConnectionHealth.Unavailable);
        });

        var snapshot = metrics.Capture(queueDepth: 3, queueInFlight: 4);

        Assert.Equal(3, snapshot.QueueDepth);
        Assert.Equal(4, snapshot.QueueInFlight);
        Assert.Equal(Iterations, snapshot.CacheHits);
        Assert.Equal(Iterations, snapshot.CacheMisses);
        Assert.Equal(Iterations, snapshot.MatchingFailures.NotFound);
        Assert.Equal(Iterations, snapshot.MatchingFailures.Ambiguous);
        Assert.Equal(Iterations, snapshot.MatchingFailures.Unsupported);
        Assert.Equal(Iterations, snapshot.RenderFailures.DecodeFailed);
        Assert.Equal(Iterations, snapshot.RenderFailures.EncodeFailed);
        Assert.Equal(0, snapshot.RenderFailures.LayoutFailed);
        Assert.Equal(Iterations, snapshot.StaleMetadataTransitions);
        Assert.Equal(ArrConnectionHealth.Healthy, snapshot.SonarrHealth);
        Assert.Equal(ArrConnectionHealth.Unavailable, snapshot.RadarrHealth);
    }

    [Fact]
    public void SnapshotProviderIsResolvableFromTheRegistratorWithoutProviderWork()
    {
        var services = new ServiceCollection();
        new ArrTagsServiceRegistrator().RegisterServices(services, null!);

        var descriptor = Assert.Single(
            services,
            candidate => candidate.ServiceType == typeof(DiagnosticsSnapshotProvider));
        Assert.NotNull(descriptor.ImplementationFactory);
        Assert.Single(services, candidate => candidate.ServiceType == typeof(DiagnosticsMetrics));

        using var serviceProvider = services.BuildServiceProvider();
        var snapshotProvider = serviceProvider.GetRequiredService<DiagnosticsSnapshotProvider>();

        var snapshot = snapshotProvider.GetSnapshot();

        Assert.Equal(0, snapshot.QueueDepth);
        Assert.Equal(0, snapshot.QueueInFlight);
        Assert.Equal(ArrConnectionHealth.Unknown, snapshot.SonarrHealth);
        Assert.Equal(ArrConnectionHealth.Unknown, snapshot.RadarrHealth);
        Assert.Equal(0, snapshot.CacheHits);
        Assert.Equal(0, snapshot.CacheMisses);
        Assert.Equal(0, snapshot.MatchingFailures.NotFound);
        Assert.Equal(0, snapshot.RenderFailures.RenderError);
        Assert.Equal(0, snapshot.StaleMetadataTransitions);
    }

    private static LibraryWorkItem WorkItem(Guid itemId, LibraryWorkReason reason, long configurationVersion)
    {
        return new LibraryWorkItem(
            new WorkItemKey(itemId, null, ArtworkImageSurface.Primary),
            reason,
            configurationVersion);
    }

    private static IEnumerable<Type> EnumeratePropertyGraph(Type root)
    {
        var visited = new HashSet<Type>();
        var pending = new Stack<Type>();
        pending.Push(root);

        while (pending.Count > 0)
        {
            var type = pending.Pop();
            if (!visited.Add(type))
            {
                continue;
            }

            yield return type;

            foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                pending.Push(property.PropertyType);
            }
        }
    }
}
