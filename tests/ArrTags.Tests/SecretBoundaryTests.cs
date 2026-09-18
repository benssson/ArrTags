using System;
using System.Linq;
using System.Net.Http;
using ArrTags.Configuration;
using ArrTags.PluginLifecycle;
using ArrTags.Providers;
using ArrTags.Secrets;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ArrTags.Tests;

/// <summary>
/// Task 2.3 checks for the versioned secret-access boundary: atomic
/// configuration/secret generations, lease acquisition, rotation fencing,
/// purpose separation, disposal, and secret exclusion from diagnostics.
/// </summary>
public class SecretBoundaryTests
{
    [Fact]
    public void ConnectionCarriesSafeReferenceAndConfigurationVersion()
    {
        var service = new ConfigurationSnapshotService(EnabledRadarr("radarr-secret"));
        var connection = RadarrConnection(service);

        Assert.Same(SecretReference.RadarrApiKey, connection.ApiKeyReference);
        Assert.Equal(service.Current.ConfigurationVersion, connection.ConfigurationVersion);
        Assert.True(connection.HasApiKey);
        Assert.DoesNotContain("radarr-secret", connection.ApiKeyReference.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void ResolverAcquiresLeaseForConfiguredKey()
    {
        var service = new ConfigurationSnapshotService(EnabledRadarr("radarr-secret"));

        Assert.True(service.TryAcquire(SecretReference.RadarrApiKey, service.Current.ConfigurationVersion, out var lease));
        Assert.NotNull(lease);
        Assert.Same(SecretReference.RadarrApiKey, lease!.Reference);
        Assert.False(lease.IsDisposed);
        lease.Dispose();
    }

    [Fact]
    public void ResolverRejectsVersionMismatch()
    {
        var service = new ConfigurationSnapshotService(EnabledRadarr("radarr-secret"));

        Assert.False(service.TryAcquire(
            SecretReference.RadarrApiKey,
            service.Current.ConfigurationVersion + 1,
            out var lease));
        Assert.Null(lease);
    }

    [Fact]
    public void ResolverRejectsUnconfiguredSlot()
    {
        var service = new ConfigurationSnapshotService(new PluginConfiguration());

        Assert.False(service.TryAcquire(SecretReference.SonarrApiKey, service.Current.ConfigurationVersion, out var lease));
        Assert.Null(lease);
    }

    [Fact]
    public void RotationIncrementsVersionAndFencesOldLeases()
    {
        var service = new ConfigurationSnapshotService(EnabledRadarr("first-secret"));
        var firstVersion = service.Current.ConfigurationVersion;

        Assert.True(service.TryReplace(EnabledRadarr("second-secret"), out _));
        Assert.Equal(firstVersion + 1, service.Current.ConfigurationVersion);

        Assert.False(service.TryAcquire(SecretReference.RadarrApiKey, firstVersion, out _));
        Assert.True(service.TryAcquire(SecretReference.RadarrApiKey, service.Current.ConfigurationVersion, out var lease));

        using var request = new HttpRequestMessage();
        lease!.ApplyTo(request);
        Assert.Equal("second-secret", request.Headers.GetValues("X-Api-Key").Single());
        lease.Dispose();
    }

    [Fact]
    public void InvalidReplacementRetainsSnapshotAndSecret()
    {
        var service = new ConfigurationSnapshotService(EnabledRadarr("stable-secret"));
        var version = service.Current.ConfigurationVersion;

        var invalid = EnabledRadarr("ignored-secret");
        invalid.Radarr.BaseUrl = "not-a-url";

        Assert.False(service.TryReplace(invalid, out _));
        Assert.Equal(version, service.Current.ConfigurationVersion);

        Assert.True(service.TryAcquire(SecretReference.RadarrApiKey, version, out var lease));
        using var request = new HttpRequestMessage();
        lease!.ApplyTo(request);
        Assert.Equal("stable-secret", request.Headers.GetValues("X-Api-Key").Single());
        lease.Dispose();
    }

    [Fact]
    public void LeaseAppliesApiKeyHeaderAndNotUrl()
    {
        var service = new ConfigurationSnapshotService(EnabledRadarr("header-secret"));
        Assert.True(service.TryAcquire(SecretReference.RadarrApiKey, service.Current.ConfigurationVersion, out var lease));

        using var request = new HttpRequestMessage(HttpMethod.Get, "https://radarr.local/radarr/api/v3/movie");
        lease!.ApplyTo(request);

        Assert.Equal("header-secret", request.Headers.GetValues("X-Api-Key").Single());
        Assert.DoesNotContain("header-secret", request.RequestUri!.ToString(), StringComparison.Ordinal);

        lease.Dispose();
    }

    [Fact]
    public void WebhookLeaseMatchesCandidateAndRejectsOthers()
    {
        var configuration = new PluginConfiguration { WebhookSecret = "webhook-secret" };
        var service = new ConfigurationSnapshotService(configuration);

        Assert.True(service.TryAcquire(SecretReference.WebhookAuthentication, service.Current.ConfigurationVersion, out var lease));
        Assert.True(lease!.Matches("webhook-secret"));
        Assert.False(lease.Matches("wrong-secret"));
        Assert.False(lease.Matches(null));
        lease.Dispose();
    }

    [Fact]
    public void LeasePurposesAreNotInterchangeable()
    {
        var service = new ConfigurationSnapshotService(EnabledRadarr("radarr-secret"));
        Assert.True(service.TryAcquire(SecretReference.RadarrApiKey, service.Current.ConfigurationVersion, out var lease));

        Assert.Throws<InvalidOperationException>(() => lease!.Matches("anything"));
        lease!.Dispose();
    }

    [Fact]
    public void DisposedLeaseCannotBeUsed()
    {
        var service = new ConfigurationSnapshotService(EnabledRadarr("radarr-secret"));
        Assert.True(service.TryAcquire(SecretReference.RadarrApiKey, service.Current.ConfigurationVersion, out var lease));
        lease!.Dispose();

        Assert.True(lease.IsDisposed);
        Assert.Throws<InvalidOperationException>(() => lease.ApplyTo(new HttpRequestMessage()));
    }

    [Fact]
    public void LeaseDiagnosticsDoNotContainSecret()
    {
        var service = new ConfigurationSnapshotService(EnabledRadarr("radarr-secret"));
        Assert.True(service.TryAcquire(SecretReference.RadarrApiKey, service.Current.ConfigurationVersion, out var lease));

        Assert.DoesNotContain("radarr-secret", lease!.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("radarr-secret", lease.Reference.ToString(), StringComparison.Ordinal);
        lease.Dispose();
    }

    [Fact]
    public void ResolverIsRegisteredAsTheConfigurationSingleton()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        new ArrTagsServiceRegistrator().RegisterServices(services, null!);

        using var provider = services.BuildServiceProvider();
        var resolver = provider.GetRequiredService<IPluginSecretResolver>();

        Assert.Same(provider.GetRequiredService<ConfigurationSnapshotService>(), resolver);
    }

    private static PluginConfiguration EnabledRadarr(string apiKey)
    {
        var configuration = new PluginConfiguration();
        configuration.Radarr.Enabled = true;
        configuration.Radarr.BaseUrl = "https://radarr.local:7878";
        configuration.Radarr.ApiKey = apiKey;
        return configuration;
    }

    private static ArrConnection RadarrConnection(ConfigurationSnapshotService service)
    {
        return ArrConnectionCatalog.FromSnapshot(service.Current)
            .Single(connection => connection.Provider.Kind == ArrProviderKind.Radarr);
    }
}
