using System;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using ArrTags.Configuration;
using ArrTags.Providers;
using Xunit;

namespace ArrTags.Tests;

/// <summary>
/// Task 11.3 focused checks for the ArrTags-side inventory cache invalidation
/// surface (ADR-018 clause 3). They prove the bounded per-connection and
/// invalidate-all operations remove only the intended observation sets, are safe
/// to call concurrently with reads and populations, are secret-free, keep the
/// bounded TTL fallback, and that the production assembly takes no SignalR
/// dependency. No live Jellyfin or Arr instance is required.
/// </summary>
public sealed class InventoryCacheInvalidationTests
{
    private static readonly TimeSpan Ttl = TimeSpan.FromMinutes(20);

    private static readonly Type[] InvalidationSurfaceTypes =
    {
        typeof(ArrInventoryCache),
        typeof(ArrInventoryCacheProvider),
    };

    [Fact]
    public void InvalidateRemovesOnlyTheTargetedConnection()
    {
        var radarr = BuildConnection(ArrProviderKind.Radarr, "http://radarr.test");
        var sonarr = BuildConnection(ArrProviderKind.Sonarr, "http://sonarr.test");
        var cache = new ArrInventoryCache(1000, 8L * 1024 * 1024, Ttl);
        var now = DateTimeOffset.UtcNow;

        Assert.True(cache.TryStore(radarr, now, Array.Empty<ArrInventoryRecordObservation>()));
        Assert.True(cache.TryStore(sonarr, now, Array.Empty<ArrInventoryRecordObservation>()));
        Assert.Equal(2, cache.Count);

        Assert.True(cache.Invalidate(radarr.ConnectionId));
        Assert.Equal(1, cache.Count);
        Assert.False(cache.TryGet(radarr.ConnectionId, now, out _));
        Assert.True(cache.TryGet(sonarr.ConnectionId, now, out var retained));
        Assert.Equal(sonarr.ConnectionId, retained!.ConnectionId);

        // Invalidating an absent connection is a bounded no-op.
        Assert.False(cache.Invalidate(radarr.ConnectionId));
        Assert.Equal(1, cache.Count);
    }

    [Fact]
    public void InvalidateAllClearsEveryConnectionAndIsIdempotent()
    {
        var radarr = BuildConnection(ArrProviderKind.Radarr, "http://radarr.test");
        var sonarr = BuildConnection(ArrProviderKind.Sonarr, "http://sonarr.test");
        var cache = new ArrInventoryCache(1000, 8L * 1024 * 1024, Ttl);
        var now = DateTimeOffset.UtcNow;

        Assert.True(cache.TryStore(radarr, now, Array.Empty<ArrInventoryRecordObservation>()));
        Assert.True(cache.TryStore(sonarr, now, Array.Empty<ArrInventoryRecordObservation>()));
        Assert.Equal(2, cache.Count);

        cache.InvalidateAll();

        Assert.Equal(0, cache.Count);
        Assert.False(cache.TryGet(radarr.ConnectionId, now, out _));
        Assert.False(cache.TryGet(sonarr.ConnectionId, now, out _));

        // Idempotent on an already-empty cache.
        cache.InvalidateAll();
        Assert.Equal(0, cache.Count);
    }

    [Fact]
    public void ExpiredSetIsEvictedByTheBoundedTtlFallback()
    {
        // A one-minute TTL: the set is unusable at or after StaleUntil and the
        // TTL fallback evicts it on the next read, independent of any event.
        var radarr = BuildConnection(ArrProviderKind.Radarr, "http://radarr.test");
        var cache = new ArrInventoryCache(1000, 8L * 1024 * 1024, TimeSpan.FromMinutes(1));
        var observedAt = DateTimeOffset.UtcNow;

        Assert.True(cache.TryStore(radarr, observedAt, Array.Empty<ArrInventoryRecordObservation>()));
        Assert.Equal(1, cache.Count);

        Assert.False(cache.TryGet(radarr.ConnectionId, observedAt + TimeSpan.FromMinutes(1), out _));
        Assert.Equal(0, cache.Count);
    }

    [Fact]
    public void InvalidationIsSafeConcurrentlyWithReadsPopulationsAndInvalidations()
    {
        var radarr = BuildConnection(ArrProviderKind.Radarr, "http://radarr.test");
        var cache = new ArrInventoryCache(1000, 8L * 1024 * 1024, Ttl);
        var failures = 0;

        Parallel.For(0, 8, worker =>
        {
            for (var iteration = 0; iteration < 500; iteration++)
            {
                try
                {
                    cache.TryStore(radarr, DateTimeOffset.UtcNow, Array.Empty<ArrInventoryRecordObservation>());
                    cache.TryGet(radarr.ConnectionId, DateTimeOffset.UtcNow, out _);
                    if ((worker + iteration) % 3 == 0)
                    {
                        cache.Invalidate(radarr.ConnectionId);
                    }

                    if ((worker + iteration) % 7 == 0)
                    {
                        cache.InvalidateAll();
                    }
                }
                catch (Exception)
                {
                    Interlocked.Increment(ref failures);
                }
            }
        });

        Assert.Equal(0, failures);
    }

    [Fact]
    public void InvalidationApiTakesOnlyTheNonSecretConnectionIdentifier()
    {
        // The invalidation surface is bounded and secret-free: every declared
        // invalidation method takes only the non-secret connection identifier
        // (never an ArrConnection, secret lease, or credential), and returns a
        // bounded bool or void.
        var inspected = 0;
        foreach (var type in InvalidationSurfaceTypes)
        {
            foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            {
                if (method.Name is not ("Invalidate" or "InvalidateAll"))
                {
                    continue;
                }

                inspected++;
                foreach (var parameter in method.GetParameters())
                {
                    Assert.Equal(typeof(ArrConnectionId), parameter.ParameterType);
                }

                Assert.True(
                    method.ReturnType == typeof(bool) || method.ReturnType == typeof(void),
                    $"{type.Name}.{method.Name} must return a bounded result.");
            }
        }

        Assert.Equal(4, inspected);
    }

    [Fact]
    public void ProductionAssemblyReferencesNoSignalRAndDeclaresNoSignalRType()
    {
        var assembly = typeof(ArrInventoryCache).Assembly;

        Assert.DoesNotContain(
            assembly.GetReferencedAssemblies(),
            reference => (reference.Name ?? string.Empty).Contains("SignalR", StringComparison.OrdinalIgnoreCase));

        foreach (var type in assembly.GetTypes())
        {
            Assert.False(
                (type.FullName ?? string.Empty).Contains("SignalR", StringComparison.OrdinalIgnoreCase),
                $"Unexpected SignalR type '{type.FullName}'.");
        }
    }

    private static ArrConnection BuildConnection(ArrProviderKind kind, string baseUrl)
    {
        var configuration = new PluginConfiguration();
        var connectionConfiguration = kind == ArrProviderKind.Sonarr ? configuration.Sonarr : configuration.Radarr;
        connectionConfiguration.Enabled = true;
        connectionConfiguration.BaseUrl = baseUrl;
        connectionConfiguration.ApiKey = "test-key";

        return ArrConnectionCatalog.FromSnapshot(PluginConfigurationSnapshot.From(configuration))
            .Single(connection => connection.Provider.Kind == kind);
    }
}
