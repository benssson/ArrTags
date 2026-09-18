using System;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using ArrTags.Configuration;
using ArrTags.PluginLifecycle;
using ArrTags.Providers;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ArrTags.Tests;

/// <summary>
/// Milestone 2 boundary checks for the shared provider-client abstraction and
/// the connection identity rules. These tests require no live Jellyfin or Arr
/// instance.
/// </summary>
public class ProviderBoundaryTests
{
    [Fact]
    public void ConnectionIdIgnoresTrailingSlashAndSchemeCasing()
    {
        var canonical = ArrConnectionId.For(ArrProviderKind.Radarr, "http://radarr.local:7878/radarr");
        var trailingSlash = ArrConnectionId.For(ArrProviderKind.Radarr, "http://radarr.local:7878/radarr/");
        var upperCasing = ArrConnectionId.For(ArrProviderKind.Radarr, "HTTP://RADARR.LOCAL:7878/radarr");

        Assert.Equal(canonical, trailingSlash);
        Assert.Equal(canonical, upperCasing);
    }

    [Fact]
    public void ConnectionIdNormalizesDefaultPorts()
    {
        Assert.Equal(
            ArrConnectionId.For(ArrProviderKind.Sonarr, "http://sonarr.local"),
            ArrConnectionId.For(ArrProviderKind.Sonarr, "http://sonarr.local:80"));

        Assert.Equal(
            ArrConnectionId.For(ArrProviderKind.Sonarr, "https://sonarr.local"),
            ArrConnectionId.For(ArrProviderKind.Sonarr, "https://sonarr.local:443"));
    }

    [Fact]
    public void ConnectionIdDiffersByProviderKind()
    {
        var sonarr = ArrConnectionId.For(ArrProviderKind.Sonarr, "http://media.local:8080");
        var radarr = ArrConnectionId.For(ArrProviderKind.Radarr, "http://media.local:8080");

        Assert.NotEqual(sonarr, radarr);
    }

    [Fact]
    public void ConnectionIdExcludesUserInformation()
    {
        var id = ArrConnectionId.For(ArrProviderKind.Radarr, "http://operator:super-secret@radarr.local:7878/radarr");

        Assert.DoesNotContain("operator", id.Value, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("super-secret", id.Value, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("radarr.local", id.Value, StringComparison.Ordinal);
    }

    [Fact]
    public void ConnectionIdForUnconfiguredConnectionIsProviderNameOnly()
    {
        Assert.Equal("sonarr", ArrConnectionId.For(ArrProviderKind.Sonarr, string.Empty).Value);
        Assert.Equal("radarr", ArrConnectionId.For(ArrProviderKind.Radarr, null).Value);
    }

    [Fact]
    public void ConnectionIdRejectsUnknownProviderKind()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => ArrConnectionId.For((ArrProviderKind)99, "http://host:1234"));
    }

    [Fact]
    public void CatalogIncludesIndependentlyDisabledConnections()
    {
        var connections = ArrConnectionCatalog.FromSnapshot(new ConfigurationSnapshotService().Current);

        Assert.Equal(2, connections.Count);
        Assert.Contains(connections, connection => connection.Provider.Kind == ArrProviderKind.Sonarr && !connection.Enabled);
        Assert.Contains(connections, connection => connection.Provider.Kind == ArrProviderKind.Radarr && !connection.Enabled);
        Assert.All(connections, connection => Assert.Equal(ArrConnectionHealth.Unknown, connection.Health));
        Assert.All(connections, connection => Assert.False(connection.HasApiKey));
    }

    [Fact]
    public void CatalogMapsEnabledConnectionWithoutLeakingSecret()
    {
        var configuration = new PluginConfiguration();
        configuration.Radarr.Enabled = true;
        configuration.Radarr.BaseUrl = "https://radarr.local:7878/radarr/";
        configuration.Radarr.ApiKey = "super-secret-key";
        configuration.Radarr.RequestTimeoutSeconds = 30;

        var snapshot = PluginConfigurationSnapshot.From(configuration);
        var radarr = ArrConnectionCatalog.FromSnapshot(snapshot)
            .Single(connection => connection.Provider.Kind == ArrProviderKind.Radarr);

        Assert.True(radarr.Enabled);
        Assert.True(radarr.HasApiKey);
        Assert.Equal(30, radarr.RequestTimeoutSeconds);
        Assert.Equal(ArrTlsPolicy.Strict, radarr.TlsPolicy);
        Assert.Equal("radarr:https://radarr.local:7878/radarr", radarr.ConnectionId.Value);
        Assert.Equal(radarr.ConnectionId.Value, radarr.Provider.ProviderInstanceId);
        Assert.DoesNotContain("super-secret-key", radarr.ConnectionId.Value, StringComparison.Ordinal);
    }

    [Fact]
    public void CatalogMapsInsecureTlsPolicy()
    {
        var configuration = new PluginConfiguration();
        configuration.Sonarr.AllowInsecureTls = true;

        var sonarr = ArrConnectionCatalog.FromSnapshot(PluginConfigurationSnapshot.From(configuration))
            .Single(connection => connection.Provider.Kind == ArrProviderKind.Sonarr);

        Assert.Equal(ArrTlsPolicy.AllowInsecure, sonarr.TlsPolicy);
    }

    [Fact]
    public void CanonicalProviderTypesDoNotExposeSecretProperties()
    {
        Assert.Null(typeof(ArrConnection).GetProperty("ApiKey"));
        Assert.Null(typeof(ArrProvider).GetProperty("ApiKey"));

        var secretValuedProperties = typeof(ArrConnection).GetProperties()
            .Where(property => property.PropertyType == typeof(string))
            .Where(property => property.Name.Contains("Secret", StringComparison.OrdinalIgnoreCase)
                || property.Name.Contains("Key", StringComparison.OrdinalIgnoreCase));

        Assert.Empty(secretValuedProperties);
    }

    [Fact]
    public void ProviderRetainsDerivedCapabilities()
    {
        var provider = new ArrProvider(
            ArrProviderKind.Sonarr,
            "sonarr",
            capabilities: new[] { "series", "episodeFile", " " });

        Assert.True(provider.Capabilities.Contains("series"));
        Assert.True(provider.Capabilities.Contains("episodeFile"));
        Assert.DoesNotContain(provider.Capabilities, capability => string.IsNullOrWhiteSpace(capability));
        Assert.Equal(ArrProvider.V3ApiContract, provider.ApiContract);
    }

    [Fact]
    public void ProviderRequiresNonEmptyInstanceId()
    {
        Assert.Throws<ArgumentException>(() => new ArrProvider(ArrProviderKind.Radarr, " "));
    }

    [Fact]
    public void ProbeResultHealthyCarriesProvider()
    {
        var provider = new ArrProvider(ArrProviderKind.Radarr, "radarr", "Radarr", "6.4.0");

        var result = ArrConnectionProbeResult.Healthy(provider);

        Assert.True(result.IsHealthy);
        Assert.Equal(ArrConnectionHealth.Healthy, result.Health);
        Assert.Same(provider, result.Provider);
        Assert.Null(result.Error);
    }

    [Fact]
    public void ProbeResultFailureCarriesRedactedError()
    {
        var error = new ArrProviderError(
            ArrProviderErrorCode.AuthenticationFailed,
            ArrErrorRetryability.AfterConfiguration,
            "The configured API key was rejected.");

        var result = ArrConnectionProbeResult.Failed(ArrConnectionHealth.AuthenticationFailed, error);

        Assert.False(result.IsHealthy);
        Assert.Null(result.Provider);
        Assert.Same(error, result.Error);
    }

    [Fact]
    public void ProbeResultFailureCannotReportHealthy()
    {
        var error = new ArrProviderError(
            ArrProviderErrorCode.ProviderUnavailable,
            ArrErrorRetryability.Later,
            "unavailable");

        Assert.Throws<ArgumentException>(
            () => ArrConnectionProbeResult.Failed(ArrConnectionHealth.Healthy, error));
    }

    [Fact]
    public void ProviderErrorBoundsMessageLength()
    {
        var error = new ArrProviderError(
            ArrProviderErrorCode.InvalidResponse,
            ArrErrorRetryability.Never,
            new string('x', ArrProviderError.MaxMessageLength + 500));

        Assert.NotNull(error.Message);
        Assert.Equal(ArrProviderError.MaxMessageLength, error.Message!.Length);
    }

    [Fact]
    public async Task ClientBoundaryPreservesConnectionScope()
    {
        var connection = ArrConnectionCatalog.FromSnapshot(new ConfigurationSnapshotService().Current)
            .Single(candidate => candidate.Provider.Kind == ArrProviderKind.Radarr);
        var client = new FakeRadarrClient(connection);

        var result = await client.ProbeAsync(CancellationToken.None);

        Assert.Equal(ArrProviderKind.Radarr, client.Kind);
        Assert.Same(connection, client.Connection);
        Assert.True(result.IsHealthy);
        Assert.Same(connection.Provider, result.Provider);
    }

    [Fact]
    public void HttpClientNamesSeparateProviderAndTlsPolicy()
    {
        Assert.Equal("ArrTags.Sonarr", ArrHttpClientNames.For(ArrProviderKind.Sonarr, ArrTlsPolicy.Strict));
        Assert.Equal("ArrTags.Radarr", ArrHttpClientNames.For(ArrProviderKind.Radarr, ArrTlsPolicy.Strict));
        Assert.Equal("ArrTags.Sonarr.Insecure", ArrHttpClientNames.For(ArrProviderKind.Sonarr, ArrTlsPolicy.AllowInsecure));
        Assert.Equal("ArrTags.Radarr.Insecure", ArrHttpClientNames.For(ArrProviderKind.Radarr, ArrTlsPolicy.AllowInsecure));
    }

    [Fact]
    public void HttpClientNamesRejectUnknownValues()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => ArrHttpClientNames.For((ArrProviderKind)99, ArrTlsPolicy.Strict));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => ArrHttpClientNames.For(ArrProviderKind.Sonarr, (ArrTlsPolicy)99));
    }

    [Fact]
    public void ArrClientsAreRegisteredWithTheHttpClientFactory()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        new ArrTagsServiceRegistrator().RegisterServices(services, null!);

        using var provider = services.BuildServiceProvider();
        var httpClientFactory = provider.GetRequiredService<IHttpClientFactory>();

        Assert.IsType<ArrHttpClientFactory>(provider.GetRequiredService<IArrHttpClientFactory>());

        foreach (var kind in new[] { ArrProviderKind.Sonarr, ArrProviderKind.Radarr })
        {
            foreach (var policy in new[] { ArrTlsPolicy.Strict, ArrTlsPolicy.AllowInsecure })
            {
                using var client = httpClientFactory.CreateClient(ArrHttpClientNames.For(kind, policy));
                Assert.NotNull(client);
            }
        }
    }

    [Fact]
    public void ArrHttpClientFactoryConfiguresClientFromConnection()
    {
        var capturing = new CapturingHttpClientFactory();
        var factory = new ArrHttpClientFactory(capturing);
        var connection = BuildConnection(
            ArrProviderKind.Radarr,
            baseUrl: "https://radarr.local:7878/radarr",
            timeoutSeconds: 45,
            allowInsecureTls: true);

        using var client = factory.CreateClient(connection);

        Assert.Equal("ArrTags.Radarr.Insecure", capturing.RequestedName);
        Assert.Equal(new Uri("https://radarr.local:7878/radarr"), client.BaseAddress);
        Assert.Equal(TimeSpan.FromSeconds(45), client.Timeout);
        Assert.Contains(client.DefaultRequestHeaders.Accept, header => header.MediaType == "application/json");
    }

    [Fact]
    public void ArrHttpClientFactorySelectsStrictClientByDefault()
    {
        var capturing = new CapturingHttpClientFactory();
        var factory = new ArrHttpClientFactory(capturing);
        var connection = BuildConnection(ArrProviderKind.Sonarr, baseUrl: "http://sonarr.local:8989");

        using var client = factory.CreateClient(connection);

        Assert.Equal("ArrTags.Sonarr", capturing.RequestedName);
        Assert.Equal(TimeSpan.FromSeconds(OperationalLimits.DefaultRequestTimeoutSeconds), client.Timeout);
    }

    [Fact]
    public void ArrHttpClientFactoryRejectsDisabledConnection()
    {
        var factory = new ArrHttpClientFactory(new CapturingHttpClientFactory());
        var connection = BuildConnection(ArrProviderKind.Radarr, enabled: false);

        Assert.Throws<InvalidOperationException>(() => factory.CreateClient(connection));
    }

    [Fact]
    public void ArrHttpClientFactoryRejectsNonAbsoluteBaseUrl()
    {
        var factory = new ArrHttpClientFactory(new CapturingHttpClientFactory());
        var connection = BuildConnection(ArrProviderKind.Radarr, baseUrl: "not-a-url");

        Assert.Throws<InvalidOperationException>(() => factory.CreateClient(connection));
    }

    private static ArrConnection BuildConnection(
        ArrProviderKind kind,
        bool enabled = true,
        string baseUrl = "https://radarr.local:7878/radarr",
        int timeoutSeconds = OperationalLimits.DefaultRequestTimeoutSeconds,
        bool allowInsecureTls = false)
    {
        var configuration = new PluginConfiguration();
        var connectionConfiguration = kind == ArrProviderKind.Sonarr ? configuration.Sonarr : configuration.Radarr;
        connectionConfiguration.Enabled = enabled;
        connectionConfiguration.BaseUrl = baseUrl;
        connectionConfiguration.ApiKey = "test-key";
        connectionConfiguration.RequestTimeoutSeconds = timeoutSeconds;
        connectionConfiguration.AllowInsecureTls = allowInsecureTls;

        return ArrConnectionCatalog.FromSnapshot(PluginConfigurationSnapshot.From(configuration))
            .Single(connection => connection.Provider.Kind == kind);
    }

    private sealed class CapturingHttpClientFactory : IHttpClientFactory
    {
        public string? RequestedName { get; private set; }

        public HttpClient CreateClient(string name)
        {
            RequestedName = name;
            return new HttpClient();
        }
    }

    private sealed class FakeRadarrClient : IArrProviderClient
    {
        public FakeRadarrClient(ArrConnection connection)
        {
            Connection = connection;
        }

        public ArrProviderKind Kind => ArrProviderKind.Radarr;

        public ArrConnection Connection { get; }

        public Task<ArrConnectionProbeResult> ProbeAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(ArrConnectionProbeResult.Healthy(Connection.Provider));
        }
    }
}
