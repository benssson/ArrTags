using System;
using System.IO;
using System.Linq;
using System.Xml.Serialization;
using ArrTags.Configuration;
using Xunit;

namespace ArrTags.Tests;

/// <summary>
/// Foundation-level checks for the plugin configuration model, its validator,
/// the immutable replacement snapshot, and secret redaction. These tests
/// require no live Jellyfin or Arr instance.
/// </summary>
public class ConfigurationFoundationTests
{
    [Fact]
    public void DefaultsDisableBothProviders()
    {
        var configuration = new PluginConfiguration();

        Assert.False(configuration.Sonarr.Enabled);
        Assert.False(configuration.Radarr.Enabled);
        Assert.True(PluginConfigurationValidator.Validate(configuration).IsValid);

        var snapshot = new ConfigurationSnapshotService().Current;
        Assert.False(snapshot.SonarrEnabled);
        Assert.False(snapshot.RadarrEnabled);
    }

    [Fact]
    public void ProvidersAreIndependentlyEnableable()
    {
        var configuration = new PluginConfiguration();
        configuration.Sonarr.Enabled = true;
        configuration.Sonarr.BaseUrl = "https://sonarr.local:8989";
        configuration.Sonarr.ApiKey = "sonarr-key";

        var result = PluginConfigurationValidator.Validate(configuration);
        Assert.True(result.IsValid);

        var snapshot = PluginConfigurationSnapshot.From(configuration);
        Assert.True(snapshot.SonarrEnabled);
        Assert.False(snapshot.RadarrEnabled);
        Assert.True(snapshot.SonarrHasApiKey);
        Assert.False(snapshot.RadarrHasApiKey);
    }

    [Fact]
    public void ValidatorAcceptsValidEnabledConnection()
    {
        var configuration = new PluginConfiguration();
        configuration.Radarr.Enabled = true;
        configuration.Radarr.BaseUrl = "http://radarr.local:7878";
        configuration.Radarr.ApiKey = "radarr-key";
        configuration.Radarr.RequestTimeoutSeconds = 30;

        Assert.True(PluginConfigurationValidator.Validate(configuration).IsValid);
    }

    [Theory]
    [InlineData("radarr.local:7878")]
    [InlineData("ftp://radarr.local")]
    [InlineData("")]
    public void ValidatorRejectsInvalidBaseUrl(string baseUrl)
    {
        var configuration = new PluginConfiguration();
        configuration.Radarr.Enabled = true;
        configuration.Radarr.BaseUrl = baseUrl;
        configuration.Radarr.ApiKey = "radarr-key";

        var result = PluginConfigurationValidator.Validate(configuration);

        Assert.False(result.IsValid);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(121)]
    public void ValidatorRejectsOutOfRangeTimeout(int timeoutSeconds)
    {
        var configuration = new PluginConfiguration();
        configuration.Sonarr.RequestTimeoutSeconds = timeoutSeconds;

        Assert.False(PluginConfigurationValidator.Validate(configuration).IsValid);
    }

    [Fact]
    public void ValidatorRejectsOutOfRangeLimits()
    {
        var configuration = new PluginConfiguration();
        configuration.Limits.QueueCapacity = 0;
        configuration.Limits.MaxImageDimensionPixels = 100000;

        var result = PluginConfigurationValidator.Validate(configuration);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.Contains("QueueCapacity", StringComparison.Ordinal));
        Assert.Contains(result.Errors, error => error.Contains("MaxImageDimensionPixels", StringComparison.Ordinal));
    }

    [Fact]
    public void ValidatorRejectsInvalidLibraryScope()
    {
        var configuration = new PluginConfiguration();
        configuration.EnabledLibraries.Add("library-a");
        configuration.EnabledLibraries.Add("library-a");
        configuration.EnabledLibraries.Add(string.Empty);

        var result = PluginConfigurationValidator.Validate(configuration);

        Assert.False(result.IsValid);
    }

    [Fact]
    public void ValidatorDoesNotLeakSecrets()
    {
        var configuration = new PluginConfiguration();
        configuration.Sonarr.Enabled = true;
        configuration.Sonarr.BaseUrl = "not-a-url";
        configuration.Sonarr.ApiKey = "sonarr-super-secret";
        configuration.WebhookSecret = "webhook-super-secret";

        var result = PluginConfigurationValidator.Validate(configuration);
        var combined = string.Join(" ", result.Errors);

        Assert.False(result.IsValid);
        Assert.DoesNotContain("sonarr-super-secret", combined, StringComparison.Ordinal);
        Assert.DoesNotContain("webhook-super-secret", combined, StringComparison.Ordinal);
    }

    [Fact]
    public void SnapshotExcludesSecretValues()
    {
        var configuration = new PluginConfiguration();
        configuration.Sonarr.Enabled = true;
        configuration.Sonarr.BaseUrl = "https://sonarr.local:8989";
        configuration.Sonarr.ApiKey = "sonarr-super-secret";
        configuration.WebhookSecret = "webhook-super-secret";

        var snapshot = PluginConfigurationSnapshot.From(configuration);

        Assert.True(snapshot.SonarrHasApiKey);
        Assert.True(snapshot.WebhookConfigured);
        Assert.DoesNotContain("sonarr-super-secret", snapshot.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("webhook-super-secret", snapshot.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void SnapshotServiceSwapsValidReplacement()
    {
        var initial = new PluginConfiguration();
        initial.Sonarr.Enabled = true;
        initial.Sonarr.BaseUrl = "https://first.local";
        initial.Sonarr.ApiKey = "key";

        var service = new ConfigurationSnapshotService(initial);

        var replacement = new PluginConfiguration();
        replacement.Sonarr.Enabled = true;
        replacement.Sonarr.BaseUrl = "https://second.local";
        replacement.Sonarr.ApiKey = "key";

        Assert.True(service.TryReplace(replacement, out var result));
        Assert.True(result.IsValid);
        Assert.Equal("https://second.local", service.Current.SonarrBaseUrl);
    }

    [Fact]
    public void SnapshotServiceRetainsLastValidSnapshotOnInvalidReplacement()
    {
        var initial = new PluginConfiguration();
        initial.Sonarr.Enabled = true;
        initial.Sonarr.BaseUrl = "https://first.local";
        initial.Sonarr.ApiKey = "key";

        var service = new ConfigurationSnapshotService(initial);

        var invalid = new PluginConfiguration();
        invalid.Sonarr.Enabled = true;
        invalid.Sonarr.BaseUrl = "not-a-url";
        invalid.Sonarr.ApiKey = "key";

        Assert.False(service.TryReplace(invalid, out var result));
        Assert.False(result.IsValid);
        Assert.Equal("https://first.local", service.Current.SonarrBaseUrl);
    }

    [Fact]
    public void SnapshotServiceFallsBackToDefaultsForInvalidInitialConfiguration()
    {
        var invalid = new PluginConfiguration();
        invalid.Radarr.Enabled = true;
        invalid.Radarr.BaseUrl = "not-a-url";

        var service = new ConfigurationSnapshotService(invalid);

        Assert.False(service.Current.RadarrEnabled);
    }

    [Fact]
    public void LibraryScopeRoundTripsThroughXmlSerializer()
    {
        var configuration = new PluginConfiguration();
        configuration.EnabledLibraries.Add("library-a");
        configuration.EnabledLibraries.Add("library-b");

        var serializer = new XmlSerializer(typeof(PluginConfiguration));
        using var writer = new StringWriter();
        serializer.Serialize(writer, configuration);

        using var reader = new StringReader(writer.ToString());
        var restored = (PluginConfiguration)serializer.Deserialize(reader)!;

        Assert.Equal(new[] { "library-a", "library-b" }, restored.EnabledLibraries.ToArray());
    }
}
