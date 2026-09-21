using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ArrTags.Concurrency;
using ArrTags.Configuration;
using ArrTags.Media;
using ArrTags.PluginLifecycle;
using ArrTags.Providers;
using ArrTags.Reconciliation;
using ArrTags.Rendering;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ArrTags.Tests;

/// <summary>
/// Focused checks that the ADR-004 provider and render concurrency limits are
/// enforced at their boundaries and are resolved from the current configuration
/// snapshot. The tests drive real concurrent acquisitions and assert that the
/// configured value actually caps concurrency. No live Jellyfin or Arr instance
/// is required.
/// </summary>
public sealed class ConcurrencyLimitTests
{
    [Fact]
    public async Task DynamicLimiterCapsConcurrencyAtTheConfiguredValue()
    {
        using var limiter = new DynamicConcurrencyLimiter(() => 2);

        var max = await MeasureConcurrency(8, (_, token) => limiter.AcquireAsync(token), expectedAtLimit: 2);

        Assert.Equal(2, max);
    }

    [Fact]
    public async Task DynamicLimiterReadsTheLimitOnEachAcquisition()
    {
        var limit = 1;
        using var limiter = new DynamicConcurrencyLimiter(() => limit);

        using var first = await limiter.AcquireAsync(CancellationToken.None);
        await AssertLimitedAsync(limiter);

        // Growing the limit takes effect on the next acquisition without
        // rebuilding the limiter.
        limit = 2;
        using var second = await limiter.AcquireAsync(CancellationToken.None);
        Assert.Equal(2, limiter.ActiveCount);
    }

    [Fact]
    public async Task DynamicLimiterHonorsAShrunkLimit()
    {
        var limit = 2;
        using var limiter = new DynamicConcurrencyLimiter(() => limit);

        using var first = await limiter.AcquireAsync(CancellationToken.None);
        using var second = await limiter.AcquireAsync(CancellationToken.None);
        limit = 1;

        await AssertLimitedAsync(limiter);
        Assert.Equal(2, limiter.ActiveCount);
    }

    [Fact]
    public async Task ProviderLimiterCapsPerConnectionConcurrency()
    {
        var configuration = Configuration(new OperationalLimits
        {
            ProviderConcurrencyPerConnection = 2,
            ProviderConcurrencyGlobal = 16,
        });

        using var limiter = new ProviderConcurrencyLimiter(configuration);
        var connection = ArrConnectionId.For(ArrProviderKind.Radarr, "http://radarr.test");

        var max = await MeasureConcurrency(
            6,
            (_, token) => limiter.AcquireAsync(connection, token),
            expectedAtLimit: 2);

        Assert.Equal(2, max);
    }

    [Fact]
    public async Task ProviderLimiterCapsGlobalConcurrencyAcrossConnections()
    {
        var configuration = Configuration(new OperationalLimits
        {
            ProviderConcurrencyPerConnection = 16,
            ProviderConcurrencyGlobal = 3,
        });

        using var limiter = new ProviderConcurrencyLimiter(configuration);

        var max = await MeasureConcurrency(
            6,
            (index, token) => limiter.AcquireAsync(
                ArrConnectionId.For(ArrProviderKind.Radarr, "http://radarr-" + index + ".test"),
                token),
            expectedAtLimit: 3);

        Assert.Equal(3, max);
    }

    [Fact]
    public async Task ProviderLimiterReadsTheGlobalLimitFromTheCurrentSnapshot()
    {
        var configuration = Configuration(new OperationalLimits
        {
            ProviderConcurrencyPerConnection = 16,
            ProviderConcurrencyGlobal = 1,
        });

        using var limiter = new ProviderConcurrencyLimiter(configuration);
        var connection = ArrConnectionId.For(ArrProviderKind.Radarr, "http://radarr.test");

        using var first = await limiter.AcquireAsync(connection, CancellationToken.None);
        await AssertLimitedAsync(limiter, connection);

        Assert.True(configuration.TryReplace(ValidConfiguration(globalLimit: 2), out _));

        using var second = await limiter.AcquireAsync(connection, CancellationToken.None);
        Assert.Equal(2, limiter.GlobalActiveCount);
    }

    [Fact]
    public async Task ReaderDecoratorCapsConcurrentProviderReads()
    {
        var configuration = Configuration(new OperationalLimits
        {
            ProviderConcurrencyPerConnection = 2,
            ProviderConcurrencyGlobal = 8,
        });

        using var limiter = new ProviderConcurrencyLimiter(configuration);
        var inner = new BlockingMetadataReader();
        var reader = new ConcurrencyLimitedArrMetadataReader<BlockingMetadataReader>(inner, limiter);

        Assert.Equal(ArrProviderKind.Radarr, reader.Kind);

        var connection = ArrConnectionCatalog
            .FromSnapshot(configuration.Current)
            .Single(candidate => candidate.Provider.Kind == ArrProviderKind.Radarr);

        var tasks = Enumerable
            .Range(0, 6)
            .Select(_ => Task.Run(() => reader.ReadAsync(Identity(), connection, CancellationToken.None)))
            .ToArray();

        await WaitUntilAsync(() => inner.Active == 2);
        Assert.Equal(2, inner.Active);
        Assert.Equal(2, inner.Max);

        inner.Release.SetResult();
        await Task.WhenAll(tasks).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(2, inner.Max);
    }

    [Fact]
    public async Task RenderDecoratorCapsConcurrentRenders()
    {
        var configuration = Configuration(new OperationalLimits { RenderConcurrency = 2 });
        using var limiter = new DynamicConcurrencyLimiter(
            () => configuration.Current.Limits.RenderConcurrency);

        var inner = new BlockingRenderer();
        var renderer = new ConcurrencyLimitedRenderer(inner, limiter);

        var tasks = Enumerable
            .Range(0, 6)
            .Select(_ => Task.Run(() => renderer.RenderAsync(null!, CancellationToken.None)))
            .ToArray();

        await WaitUntilAsync(() => inner.Active == 2);
        Assert.Equal(2, inner.Active);
        Assert.Equal(2, inner.Max);

        inner.Release.SetResult();
        await Task.WhenAll(tasks).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(2, inner.Max);
    }

    [Fact]
    public void RegistratorWiresConcurrencyLimitedRendererAndReaders()
    {
        var services = new ServiceCollection();
        new ArrTagsServiceRegistrator().RegisterServices(services, null!);

        Assert.Single(services, descriptor => descriptor.ServiceType == typeof(IRenderer));
        Assert.Equal(2, services.Count(descriptor => descriptor.ServiceType == typeof(IArrMetadataReader)));

        using var provider = services.BuildServiceProvider();

        Assert.IsType<ConcurrencyLimitedRenderer>(provider.GetRequiredService<IRenderer>());

        var readers = provider.GetServices<IArrMetadataReader>().ToArray();
        Assert.Equal(2, readers.Length);
        Assert.All(
            readers,
            reader => Assert.StartsWith(
                "ConcurrencyLimitedArrMetadataReader",
                reader.GetType().Name,
                StringComparison.Ordinal));
    }

    private static ConfigurationSnapshotService Configuration(OperationalLimits limits)
    {
        return new ConfigurationSnapshotService(ValidConfiguration(limits));
    }

    private static PluginConfiguration ValidConfiguration(OperationalLimits limits)
    {
        return new PluginConfiguration
        {
            Radarr = new ArrConnectionConfiguration
            {
                Enabled = true,
                BaseUrl = "http://radarr.test",
                ApiKey = "test-radarr-api-key",
            },
            Limits = limits,
        };
    }

    private static PluginConfiguration ValidConfiguration(int globalLimit)
    {
        return ValidConfiguration(new OperationalLimits
        {
            ProviderConcurrencyPerConnection = 16,
            ProviderConcurrencyGlobal = globalLimit,
        });
    }

    private static MediaIdentity Identity()
    {
        return ReconciliationFixtures.MovieIdentity();
    }

    private static async Task AssertLimitedAsync(DynamicConcurrencyLimiter limiter)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => limiter.AcquireAsync(cts.Token));
    }

    private static async Task AssertLimitedAsync(
        ProviderConcurrencyLimiter limiter,
        ArrConnectionId connection)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => limiter.AcquireAsync(connection, cts.Token));
    }

    private static async Task<int> MeasureConcurrency(
        int count,
        Func<int, CancellationToken, Task<IDisposable>> acquire,
        int expectedAtLimit)
    {
        var active = 0;
        var max = 0;
        var started = 0;
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var allStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var tasks = Enumerable
            .Range(0, count)
            .Select(index => Task.Run(async () =>
            {
                using var lease = await acquire(index, CancellationToken.None).ConfigureAwait(false);
                var current = Interlocked.Increment(ref active);
                UpdateMax(ref max, current);

                if (Interlocked.Increment(ref started) == expectedAtLimit)
                {
                    allStarted.TrySetResult();
                }

                await release.Task.ConfigureAwait(false);
                Interlocked.Decrement(ref active);
            }))
            .ToArray();

        await allStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(expectedAtLimit, Volatile.Read(ref active));

        release.SetResult();
        await Task.WhenAll(tasks).WaitAsync(TimeSpan.FromSeconds(5));
        return Volatile.Read(ref max);
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException("The concurrency condition was not reached in time.");
            }

            await Task.Delay(5);
        }
    }

    private static void UpdateMax(ref int target, int value)
    {
        int current;
        while (value > (current = Volatile.Read(ref target)))
        {
            if (Interlocked.CompareExchange(ref target, value, current) == current)
            {
                break;
            }
        }
    }

    private sealed class BlockingMetadataReader : IArrMetadataReader
    {
        private int _active;
        private int _max;

        public ArrProviderKind Kind => ArrProviderKind.Radarr;

        public int Active => Volatile.Read(ref _active);

        public int Max => Volatile.Read(ref _max);

        public TaskCompletionSource Release { get; } =
            new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<ArrMetadataReadResult> ReadAsync(
            MediaIdentity identity,
            ArrConnection connection,
            CancellationToken cancellationToken)
        {
            var current = Interlocked.Increment(ref _active);
            UpdateMax(ref _max, current);

            await Release.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
            Interlocked.Decrement(ref _active);

            return ArrMetadataReadResult.Failure(new ArrProviderError(
                ArrProviderErrorCode.ProviderUnavailable,
                ArrErrorRetryability.Later,
                "The reader was released by the test."));
        }
    }

    private sealed class BlockingRenderer : IRenderer
    {
        private int _active;
        private int _max;

        public int Active => Volatile.Read(ref _active);

        public int Max => Volatile.Read(ref _max);

        public TaskCompletionSource Release { get; } =
            new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<RenderResult> RenderAsync(RenderRequest request, CancellationToken cancellationToken)
        {
            var current = Interlocked.Increment(ref _active);
            UpdateMax(ref _max, current);

            await Release.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
            Interlocked.Decrement(ref _active);

            return RenderResult.Failed(RenderFailureReason.RenderError);
        }
    }
}
