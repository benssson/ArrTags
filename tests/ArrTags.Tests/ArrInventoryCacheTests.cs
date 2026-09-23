using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using ArrTags.Configuration;
using ArrTags.Matching;
using ArrTags.Metadata;
using ArrTags.Providers;
using ArrTags.Reconciliation;
using ArrTags.Secrets;
using Xunit;

namespace ArrTags.Tests;

/// <summary>
/// Boundary checks for the v1.1 Phase 11 provider inventory cache model and its
/// operational limits (ADR-018 clauses 1, 5, 6, and 7). These tests require no
/// live Jellyfin or Arr instance.
/// </summary>
public class ArrInventoryCacheTests
{
    private static readonly DateTimeOffset ObservedAt = new(2026, 9, 23, 12, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Ttl = TimeSpan.FromMinutes(20);

    private static readonly Type[] InventoryCacheModelTypes =
    {
        typeof(ArrInventoryCache),
        typeof(ArrInventoryCacheEntry),
        typeof(ArrInventoryRecordObservation),
        typeof(ArrInventoryCacheState),
    };

    [Theory]
    [InlineData(0, 10000, 32L * 1024 * 1024)]
    [InlineData(1441, 10000, 32L * 1024 * 1024)]
    [InlineData(15, 0, 32L * 1024 * 1024)]
    [InlineData(15, 100001, 32L * 1024 * 1024)]
    [InlineData(15, 10000, 1024L)]
    [InlineData(15, 10000, (256L * 1024 * 1024) + 1)]
    public void ValidatorRejectsOutOfRangeInventoryLimits(int ttlMinutes, int maxRecords, long maxBytes)
    {
        var configuration = new PluginConfiguration();
        configuration.Limits.InventoryCacheTtlMinutes = ttlMinutes;
        configuration.Limits.InventoryCacheMaxRecords = maxRecords;
        configuration.Limits.InventoryCacheMaxBytes = maxBytes;

        Assert.False(PluginConfigurationValidator.Validate(configuration).IsValid);
    }

    [Theory]
    [InlineData(1, 1, 1L * 1024 * 1024)]
    [InlineData(1440, 100000, 256L * 1024 * 1024)]
    public void ValidatorAcceptsInventoryLimitBoundaries(int ttlMinutes, int maxRecords, long maxBytes)
    {
        var configuration = new PluginConfiguration();
        configuration.Limits.InventoryCacheTtlMinutes = ttlMinutes;
        configuration.Limits.InventoryCacheMaxRecords = maxRecords;
        configuration.Limits.InventoryCacheMaxBytes = maxBytes;

        Assert.True(PluginConfigurationValidator.Validate(configuration).IsValid);
    }

    [Fact]
    public void InventoryLimitsDefaultToValidValuesAndCloneIndependently()
    {
        var limits = new OperationalLimits();

        Assert.Equal(OperationalLimits.DefaultInventoryCacheTtlMinutes, limits.InventoryCacheTtlMinutes);
        Assert.Equal(15, limits.InventoryCacheTtlMinutes);
        Assert.Equal(10000, limits.InventoryCacheMaxRecords);
        Assert.Equal(32L * 1024 * 1024, limits.InventoryCacheMaxBytes);
        Assert.True(PluginConfigurationValidator.Validate(new PluginConfiguration()).IsValid);

        var clone = limits.Clone();
        clone.InventoryCacheTtlMinutes = 7;
        clone.InventoryCacheMaxRecords = 5;
        clone.InventoryCacheMaxBytes = 2L * 1024 * 1024;

        Assert.Equal(OperationalLimits.DefaultInventoryCacheTtlMinutes, limits.InventoryCacheTtlMinutes);
        Assert.Equal(10000, limits.InventoryCacheMaxRecords);
        Assert.Equal(32L * 1024 * 1024, limits.InventoryCacheMaxBytes);
        Assert.Equal(7, clone.InventoryCacheTtlMinutes);
        Assert.Equal(5, clone.InventoryCacheMaxRecords);
        Assert.Equal(2L * 1024 * 1024, clone.InventoryCacheMaxBytes);
    }

    [Fact]
    public void FromLimitsBindsTheValidatedLimitsToTheCache()
    {
        // Distinctive non-default values so a wrong field mapping or a wrong time
        // unit fails: 90 minutes is 1h30m, which differs from 90 seconds and 90
        // hours, and 7 records / 3 MiB differ from each other and from the
        // defaults.
        var limits = new OperationalLimits
        {
            InventoryCacheTtlMinutes = 90,
            InventoryCacheMaxRecords = 7,
            InventoryCacheMaxBytes = 3L * 1024 * 1024,
        };

        var cache = ArrInventoryCache.FromLimits(limits);

        Assert.Equal(TimeSpan.FromMinutes(90), cache.Ttl);
        Assert.Equal(7, cache.MaxRecordsPerConnection);
        Assert.Equal(3L * 1024 * 1024, cache.MaxBytesPerConnection);

        // The defaults map too, so FromLimits is not relying on constructor
        // defaults or a constant.
        var defaultCache = ArrInventoryCache.FromLimits(new OperationalLimits());
        Assert.Equal(TimeSpan.FromMinutes(OperationalLimits.DefaultInventoryCacheTtlMinutes), defaultCache.Ttl);
        Assert.Equal(10000, defaultCache.MaxRecordsPerConnection);
        Assert.Equal(32L * 1024 * 1024, defaultCache.MaxBytesPerConnection);
    }

    [Fact]
    public void InventoryCacheTypesHaveNoSecretNamedMembers()
    {
        // Secondary guard only: a member-name heuristic cannot detect a secret
        // stored under a neutral name. The structural type guard below carries
        // the type-level assurance.
        var types = new[]
        {
            typeof(ArrInventoryCache),
            typeof(ArrInventoryCacheEntry),
            typeof(ArrInventoryRecordObservation),
        };

        foreach (var type in types)
        {
            var secretProperties = type.GetProperties()
                .Where(property => IsSecretName(property.Name));
            Assert.Empty(secretProperties);

            var secretFields = type
                .GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static)
                .Where(field => IsSecretName(field.Name));
            Assert.Empty(secretFields);
        }
    }

    [Fact]
    public void InventoryCacheTypesExposeNoCredentialBearingMembers()
    {
        // A member-name heuristic alone cannot detect a secret stored under a
        // neutral name, so this guard checks the member *types* of the cache
        // object graph. It fails if an ArrConnection, its API-key reference, a
        // secret lease, the mutable plugin configuration, or a per-item metadata
        // state member is added to any cache type.
        var forbiddenTypes = new[]
        {
            typeof(ArrConnection),
            typeof(ArrConnectionConfiguration),
            typeof(SecretReference),
            typeof(SecretLease),
            typeof(PluginConfiguration),
            typeof(MetadataStateEntry),
            typeof(MetadataStateStore),
        };

        var inspected = 0;
        foreach (var type in InventoryCacheModelTypes)
        {
            foreach (var memberType in EnumerateMemberTypes(type))
            {
                inspected++;
                Assert.DoesNotContain(memberType, forbiddenTypes);
            }
        }

        Assert.True(inspected > 0, "The cache model types must expose inspectable members.");
    }

    [Fact]
    public void LastErrorSatisfiesTheBoundedRedactedProviderErrorContract()
    {
        // ArrProviderError is the documented bounded/redacted producer-contract
        // carrier (ADR-020 clause 4). The cache stores it unchanged and does not
        // add value-level redaction, so this asserts the actual guarantee rather
        // than overstating it: the only free-text surface is a bounded message,
        // and the error type exposes exactly the stable code, the retryability
        // classification, and the bounded message. Value-level redaction of the
        // message is the producer's responsibility, exactly as for the per-item
        // metadata record's last-error summary.
        var error = new ArrProviderError(
            ArrProviderErrorCode.ProviderUnavailable,
            ArrErrorRetryability.Later,
            new string('x', ArrProviderError.MaxMessageLength + 500));

        Assert.Equal(ArrProviderError.MaxMessageLength, error.Message!.Length);

        var propertyNames = typeof(ArrProviderError).GetProperties()
            .Select(property => property.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(new[] { "Code", "Message", "Retryability" }, propertyNames);
    }

    [Fact]
    public void InventoryObservationCarriesOnlyCanonicalProviderNeutralTypes()
    {
        var propertyTypes = typeof(ArrInventoryRecordObservation)
            .GetProperties()
            .Select(property => property.PropertyType)
            .ToArray();

        Assert.Contains(typeof(MatchCandidate), propertyTypes);
        Assert.Contains(typeof(BadgeMetadata), propertyTypes);
        Assert.DoesNotContain(propertyTypes, IsProviderDtoType);

        var recordProperty = typeof(ArrInventoryCacheEntry).GetProperty(nameof(ArrInventoryCacheEntry.Records));
        Assert.NotNull(recordProperty);
        Assert.Equal(
            typeof(System.Collections.Generic.IReadOnlyList<ArrInventoryRecordObservation>),
            recordProperty!.PropertyType);
    }

    [Fact]
    public void CacheIsEmptyOnConstructionAndRebuiltAfterRestart()
    {
        var connection = BuildConnection(ArrProviderKind.Radarr, "https://radarr.local:7878");
        var cache = new ArrInventoryCache(1000, 8L * 1024 * 1024, Ttl);
        Assert.Equal(0, cache.Count);

        Assert.True(cache.TryStore(connection, ObservedAt, new[] { BuildRecord(connection, 1, 10) }));
        Assert.Equal(1, cache.Count);

        // A new instance models a host restart: the in-memory cache is rebuilt
        // empty and carries nothing forward.
        var restarted = new ArrInventoryCache(1000, 8L * 1024 * 1024, Ttl);
        Assert.Equal(0, restarted.Count);
        Assert.False(restarted.TryGet(connection.ConnectionId, ObservedAt, out _));
    }

    [Fact]
    public void CacheEntryIsFreshThenStaleThenExpiredWithinTheTtlBound()
    {
        var connection = BuildConnection(ArrProviderKind.Radarr, "https://radarr.local:7878");
        var entry = ArrInventoryCacheEntry.From(
            connection,
            ObservedAt,
            Ttl,
            new[] { BuildRecord(connection, 1, 10) });

        Assert.Equal(ObservedAt + TimeSpan.FromMinutes(10), entry.ExpiresAt);
        Assert.Equal(ObservedAt + TimeSpan.FromMinutes(20), entry.StaleUntil);
        Assert.Equal(ArrInventoryCacheState.Fresh, entry.EvaluateState(ObservedAt));
        Assert.Equal(ArrInventoryCacheState.Fresh, entry.EvaluateState(ObservedAt + TimeSpan.FromMinutes(9)));
        Assert.Equal(ArrInventoryCacheState.Stale, entry.EvaluateState(ObservedAt + TimeSpan.FromMinutes(10)));
        Assert.Equal(ArrInventoryCacheState.Stale, entry.EvaluateState(ObservedAt + TimeSpan.FromMinutes(19)));
        Assert.Equal(ArrInventoryCacheState.Expired, entry.EvaluateState(ObservedAt + TimeSpan.FromMinutes(20)));

        Assert.True(entry.IsUsableAsCurrent(ObservedAt + TimeSpan.FromMinutes(19)));
        Assert.False(entry.IsUsableAsCurrent(ObservedAt + TimeSpan.FromMinutes(20)));
    }

    [Fact]
    public void ProviderFailureKeepsBoundedLastKnownGoodAndNeverExtendsIt()
    {
        var connection = BuildConnection(ArrProviderKind.Radarr, "https://radarr.local:7878");
        var cache = new ArrInventoryCache(1000, 8L * 1024 * 1024, Ttl);
        Assert.True(cache.TryStore(connection, ObservedAt, new[] { BuildRecord(connection, 1, 10) }));

        Assert.True(cache.TryGet(connection.ConnectionId, ObservedAt + TimeSpan.FromMinutes(15), out var retained));
        Assert.NotNull(retained);

        var failure = new ArrProviderError(
            ArrProviderErrorCode.ProviderUnavailable,
            ArrErrorRetryability.Later,
            "The provider is unavailable.");
        var failed = retained!.WithFailure(failure);

        Assert.Same(failure, failed.LastError);
        Assert.Equal(retained.ObservedAt, failed.ObservedAt);
        Assert.Equal(retained.ExpiresAt, failed.ExpiresAt);
        Assert.Equal(retained.StaleUntil, failed.StaleUntil);
        Assert.Equal(retained.Records.Count, failed.Records.Count);
        Assert.Equal(ArrInventoryCacheState.Stale, failed.EvaluateState(ObservedAt + TimeSpan.FromMinutes(15)));
        Assert.Equal(ArrInventoryCacheState.Expired, failed.EvaluateState(ObservedAt + TimeSpan.FromMinutes(20)));
        Assert.False(failed.IsUsableAsCurrent(ObservedAt + TimeSpan.FromMinutes(20)));

        // Past the bounded window the last-known-good set is expired and evicted;
        // the cache never extends it on failure.
        Assert.False(cache.TryGet(connection.ConnectionId, ObservedAt + TimeSpan.FromMinutes(20), out _));
        Assert.Equal(0, cache.Count);
    }

    [Fact]
    public void ProviderFailureRetainedThroughTheCacheBoundaryNeverExtendsTheWindow()
    {
        var connection = BuildConnection(ArrProviderKind.Radarr, "https://radarr.local:7878");
        var records = new[] { BuildRecord(connection, 1, 10) };

        var withoutFailure = new ArrInventoryCache(1000, 8L * 1024 * 1024, Ttl);
        Assert.True(withoutFailure.TryStore(connection, ObservedAt, records));
        Assert.True(withoutFailure.TryGet(connection.ConnectionId, ObservedAt + TimeSpan.FromMinutes(15), out var clean));
        Assert.NotNull(clean);

        var failure = new ArrProviderError(
            ArrProviderErrorCode.ProviderUnavailable,
            ArrErrorRetryability.Later,
            "The provider is unavailable.");

        var withFailure = new ArrInventoryCache(1000, 8L * 1024 * 1024, Ttl);
        Assert.True(withFailure.TryStore(connection, ObservedAt, records, lastError: failure));

        // The retained failure-annotated set is retrievable through the cache
        // boundary, and its window is identical to a non-failure store at the same
        // observedAt/TTL: the failure annotation never extends the bounded
        // last-known-good window.
        Assert.True(withFailure.TryGet(connection.ConnectionId, ObservedAt + TimeSpan.FromMinutes(15), out var annotated));
        Assert.NotNull(annotated);
        Assert.Same(failure, annotated!.LastError);
        Assert.Equal(ObservedAt, annotated.ObservedAt);
        Assert.Equal(ObservedAt + TimeSpan.FromMinutes(10), annotated.ExpiresAt);
        Assert.Equal(ObservedAt + TimeSpan.FromMinutes(20), annotated.StaleUntil);
        Assert.Equal(clean!.ObservedAt, annotated.ObservedAt);
        Assert.Equal(clean.ExpiresAt, annotated.ExpiresAt);
        Assert.Equal(clean.StaleUntil, annotated.StaleUntil);
        Assert.Single(annotated.Records);
        Assert.Equal(ArrInventoryCacheState.Stale, annotated.EvaluateState(ObservedAt + TimeSpan.FromMinutes(15)));

        // At and after StaleUntil the set is evicted and not served.
        Assert.False(withFailure.TryGet(connection.ConnectionId, ObservedAt + TimeSpan.FromMinutes(20), out _));
        Assert.Equal(0, withFailure.Count);
    }

    [Fact]
    public void OverBoundObservationSetIsRejectedWithoutDisplacingTheRetainedSet()
    {
        var connection = BuildConnection(ArrProviderKind.Radarr, "https://radarr.local:7878");
        var recordCountBounded = new ArrInventoryCache(1, 8L * 1024 * 1024, Ttl);

        Assert.True(recordCountBounded.TryStore(connection, ObservedAt, new[] { BuildRecord(connection, 1, 10) }));
        Assert.Equal(1, recordCountBounded.Count);

        // An over-record set is rejected and does not displace the retained set.
        Assert.False(recordCountBounded.TryStore(
            connection,
            ObservedAt,
            new[] { BuildRecord(connection, 2, 20), BuildRecord(connection, 3, 30) }));
        Assert.Equal(1, recordCountBounded.Count);
        Assert.True(recordCountBounded.TryGet(connection.ConnectionId, ObservedAt, out var retained));
        Assert.Single(retained!.Records);

        // An over-byte set is rejected and stores nothing.
        var byteBounded = new ArrInventoryCache(1000, 1, Ttl);
        Assert.False(byteBounded.TryStore(connection, ObservedAt, new[] { BuildRecord(connection, 1, 10) }));
        Assert.Equal(0, byteBounded.Count);
    }

    [Fact]
    public void ByteBoundIsEnforcedAtTheActualEntrySize()
    {
        var connection = BuildConnection(ArrProviderKind.Radarr, "https://radarr.local:7878");
        var records = new[] { BuildRecord(connection, 1, 10) };
        var size = ArrInventoryCacheEntry.From(connection, ObservedAt, Ttl, records).SizeBytes;
        Assert.True(size > 1);

        // Just at the real accounting boundary is accepted; one byte under
        // rejects. Both stores recompute the same deterministic size from the same
        // observation set.
        var atBound = new ArrInventoryCache(1000, size, Ttl);
        Assert.True(atBound.TryStore(connection, ObservedAt, records));
        Assert.Equal(1, atBound.Count);

        var overBound = new ArrInventoryCache(1000, size - 1, Ttl);
        Assert.False(overBound.TryStore(connection, ObservedAt, records));
        Assert.Equal(0, overBound.Count);
    }

    [Fact]
    public void CacheRejectsObservationOutsideItsConnectionScope()
    {
        var connection = BuildConnection(ArrProviderKind.Radarr, "https://radarr.local:7878");
        var other = BuildConnection(ArrProviderKind.Radarr, "https://other.local:7878");
        var cache = new ArrInventoryCache(1000, 8L * 1024 * 1024, Ttl);

        Assert.False(cache.TryStore(connection, ObservedAt, new[] { BuildRecord(other, 1, 10) }));
        Assert.Equal(0, cache.Count);
    }

    [Fact]
    public void RecordObservationRejectsMismatchedFileObservation()
    {
        var connection = BuildConnection(ArrProviderKind.Radarr, "https://radarr.local:7878");
        var candidateIdentity = new RadarrIdentity(connection.ConnectionId, 1, ArrFileIdentity.Present(10));
        var otherIdentity = new RadarrIdentity(connection.ConnectionId, 2, ArrFileIdentity.Present(20));
        var candidate = new MatchCandidate(connection.ConnectionId, ArrProviderKind.Radarr, candidateIdentity);
        var fileObservation = new BadgeMetadata(connection.Provider, otherIdentity, ObservedAt);

        Assert.Throws<ArgumentException>(() => new ArrInventoryRecordObservation(candidate, fileObservation));
    }

    [Fact]
    public void CacheEntrySizeEstimateGrowsWithBoundedContent()
    {
        var connection = BuildConnection(ArrProviderKind.Radarr, "https://radarr.local:7878");
        var shortEntry = ArrInventoryCacheEntry.From(
            connection,
            ObservedAt,
            Ttl,
            new[] { BuildRecord(connection, 1, 10, "A") });
        var longEntry = ArrInventoryCacheEntry.From(
            connection,
            ObservedAt,
            Ttl,
            new[] { BuildRecord(connection, 2, 20, new string('x', 500)) });

        Assert.True(shortEntry.SizeBytes > 0);
        Assert.True(longEntry.SizeBytes > shortEntry.SizeBytes);
    }

    [Fact]
    public void InventoryCacheEntryIsDistinctFromThePerItemMetadataStateRecord()
    {
        // The inventory entry carries no per-item match/metadata state fields.
        Assert.Null(typeof(ArrInventoryCacheEntry).GetProperty("JellyfinItemId"));
        Assert.Null(typeof(ArrInventoryCacheEntry).GetProperty("MatchStatus"));
        Assert.Null(typeof(ArrInventoryCacheEntry).GetProperty("Metadata"));

        // No member of the inventory entry or cache references the per-item
        // metadata state record or its authoritative store, so the two caches
        // cannot be conflated.
        var memberTypes = EnumerateMemberTypes(typeof(ArrInventoryCacheEntry))
            .Concat(EnumerateMemberTypes(typeof(ArrInventoryCache)))
            .ToArray();
        Assert.NotEmpty(memberTypes);
        Assert.DoesNotContain(typeof(MetadataStateEntry), memberTypes);
        Assert.DoesNotContain(typeof(MetadataStateStore), memberTypes);
    }

    private static IEnumerable<Type> EnumerateMemberTypes(Type type)
    {
        foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static))
        {
            foreach (var memberType in Unwrap(property.PropertyType))
            {
                yield return memberType;
            }
        }

        foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static))
        {
            foreach (var memberType in Unwrap(field.FieldType))
            {
                yield return memberType;
            }
        }
    }

    private static IEnumerable<Type> Unwrap(Type type)
    {
        yield return type;

        if (type.IsGenericType)
        {
            foreach (var argument in type.GetGenericArguments())
            {
                foreach (var nested in Unwrap(argument))
                {
                    yield return nested;
                }
            }
        }

        if (type.IsArray)
        {
            foreach (var nested in Unwrap(type.GetElementType()!))
            {
                yield return nested;
            }
        }
    }

    private static bool IsSecretName(string name)
    {
        return name.Contains("Secret", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Key", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Token", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Credential", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsProviderDtoType(Type type)
    {
        var candidate = type;
        if (type.IsGenericType)
        {
            candidate = type.GetGenericArguments()[0];
        }

        return candidate.Namespace is not null
            && (candidate.Namespace.EndsWith(".Sonarr", StringComparison.Ordinal)
                || candidate.Namespace.EndsWith(".Radarr", StringComparison.Ordinal));
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

    private static ArrInventoryRecordObservation BuildRecord(
        ArrConnection connection,
        int movieId,
        int? movieFileId,
        string? title = null)
    {
        var identity = new RadarrIdentity(
            connection.ConnectionId,
            movieId,
            movieFileId is int fileId ? ArrFileIdentity.Present(fileId) : ArrFileIdentity.Absent);
        var candidate = new MatchCandidate(connection.ConnectionId, ArrProviderKind.Radarr, identity, title: title);

        BadgeMetadata? fileObservation = null;
        if (movieFileId is not null)
        {
            fileObservation = new BadgeMetadata(
                connection.Provider,
                identity,
                ObservedAt,
                quality: new ArrQualityDescriptor("Bluray-1080p", "bluray", 1080, null, 7));
        }

        return new ArrInventoryRecordObservation(candidate, fileObservation);
    }
}
