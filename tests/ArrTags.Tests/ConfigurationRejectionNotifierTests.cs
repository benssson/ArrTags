using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using ArrTags.Configuration;
using ArrTags.PluginLifecycle;
using MediaBrowser.Model.Activity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace ArrTags.Tests;

/// <summary>
/// Phase 9 task 9.3 rework coverage for the administrator-visible
/// configuration-rejection surfacing (ADR-021). These tests drive the real
/// <see cref="JellyfinConfigurationRejectionNotifier"/> against a recording
/// <see cref="IActivityManager"/> double: a rejection writes exactly one fixed
/// name/type, bounded, secret-free entry with an empty user id and Warning
/// severity; the reasons are capped and truncated to the ActivityLog column
/// bounds; and the notifier never throws when the activity manager fails or is
/// unavailable. No live Jellyfin host is required.
/// </summary>
public sealed class ConfigurationRejectionNotifierTests
{
    [Fact]
    public void NotifyRejectedWritesExactlyOneFixedTypeEntry()
    {
        var (manager, recorder) = RecordingActivityManager.Create();
        var notifier = new JellyfinConfigurationRejectionNotifier(ProviderWith(manager));

        notifier.NotifyRejected(new[] { "Sonarr base URL must be an absolute http or https URL." });

        var entry = Assert.Single(recorder.Entries);
        Assert.Equal(JellyfinConfigurationRejectionNotifier.EntryName, entry.Name);
        Assert.Equal(JellyfinConfigurationRejectionNotifier.EntryType, entry.Type);
        Assert.Equal(Guid.Empty, entry.UserId);
        Assert.Equal(LogLevel.Warning, entry.LogSeverity);
        Assert.NotNull(entry.Overview);
        Assert.Contains("absolute http or https URL", entry.Overview!, StringComparison.Ordinal);
        Assert.False(string.IsNullOrWhiteSpace(entry.ShortOverview));
        Assert.True(entry.Name.Length <= 512);
        Assert.True(entry.Type.Length <= 256);
        Assert.True(entry.Overview!.Length <= 512);
        Assert.True(entry.ShortOverview!.Length <= 512);
    }

    [Fact]
    public void NotifyRejectedEntryContainsNoCandidateSecretValues()
    {
        var candidate = new PluginConfiguration
        {
            WebhookSecret = "sentinel-webhook-secret-8f1c2d",
            Sonarr = new ArrConnectionConfiguration
            {
                Enabled = true,
                BaseUrl = "not-a-url",
                ApiKey = "sentinel-sonarr-key-4a9b7e",
            },
            Radarr = new ArrConnectionConfiguration
            {
                Enabled = true,
                BaseUrl = "not-a-url",
                ApiKey = "sentinel-radarr-key-1d6f3a",
            },
        };

        var result = PluginConfigurationValidator.Validate(candidate);
        Assert.False(result.IsValid);

        var (manager, recorder) = RecordingActivityManager.Create();
        var notifier = new JellyfinConfigurationRejectionNotifier(ProviderWith(manager));

        notifier.NotifyRejected(result.Errors);

        var entry = Assert.Single(recorder.Entries);
        var combined = string.Join(" ", entry.Name, entry.Overview, entry.ShortOverview);
        Assert.DoesNotContain("sentinel-webhook-secret-8f1c2d", combined, StringComparison.Ordinal);
        Assert.DoesNotContain("sentinel-sonarr-key-4a9b7e", combined, StringComparison.Ordinal);
        Assert.DoesNotContain("sentinel-radarr-key-1d6f3a", combined, StringComparison.Ordinal);
        Assert.DoesNotContain("not-a-url", combined, StringComparison.Ordinal);
    }

    [Fact]
    public void NotifyRejectedCapsTheNumberOfSurfacedReasons()
    {
        var reasons = Enumerable
            .Range(0, 20)
            .Select(index => "validation-reason-" + index.ToString("00", System.Globalization.CultureInfo.InvariantCulture))
            .ToArray();

        var (manager, recorder) = RecordingActivityManager.Create();
        var notifier = new JellyfinConfigurationRejectionNotifier(ProviderWith(manager));

        notifier.NotifyRejected(reasons);

        var entry = Assert.Single(recorder.Entries);
        Assert.Contains("validation-reason-07", entry.Overview!, StringComparison.Ordinal);
        Assert.DoesNotContain("validation-reason-08", entry.Overview!, StringComparison.Ordinal);
        Assert.Contains("(+12 more)", entry.Overview!, StringComparison.Ordinal);
    }

    [Fact]
    public void NotifyRejectedTruncatesToTheActivityLogColumnBounds()
    {
        var longReason = new string('x', 2000);
        var reasons = Enumerable.Range(0, JellyfinConfigurationRejectionNotifier.MaxSurfacedReasons)
            .Select(_ => longReason)
            .ToArray();

        var (manager, recorder) = RecordingActivityManager.Create();
        var notifier = new JellyfinConfigurationRejectionNotifier(ProviderWith(manager));

        notifier.NotifyRejected(reasons);

        var entry = Assert.Single(recorder.Entries);
        Assert.NotNull(entry.Overview);
        Assert.NotNull(entry.ShortOverview);
        Assert.Equal(512, entry.Overview!.Length);
        Assert.True(entry.ShortOverview!.Length <= 512);
        Assert.True(entry.Name.Length <= 512);
        Assert.True(entry.Type.Length <= 256);
    }

    [Fact]
    public void NotifyRejectedNeverThrowsWhenTheActivityManagerFails()
    {
        var (manager, recorder) = RecordingActivityManager.Create();
        recorder.ThrowOnCreate = true;
        var notifier = new JellyfinConfigurationRejectionNotifier(ProviderWith(manager));

        var exception = Record.Exception(() => notifier.NotifyRejected(new[] { "rejected." }));

        Assert.Null(exception);
        Assert.Empty(recorder.Entries);
    }

    [Fact]
    public void NotifyRejectedContainsASlowActivityManagerWrite()
    {
        var (manager, recorder) = RecordingActivityManager.Create();
        recorder.PendingCreate = true;
        var notifier = new JellyfinConfigurationRejectionNotifier(
            ProviderWith(manager),
            TimeSpan.FromMilliseconds(50));

        var stopwatch = Stopwatch.StartNew();
        var exception = Record.Exception(() => notifier.NotifyRejected(new[] { "rejected." }));
        stopwatch.Stop();

        Assert.Null(exception);
        Assert.True(
            stopwatch.Elapsed < TimeSpan.FromSeconds(5),
            "The bounded wait must return promptly when the activity write hangs.");
    }

    [Fact]
    public void NotifyRejectedStripsControlCharactersAndCollapsesWhitespace()
    {
        var (manager, recorder) = RecordingActivityManager.Create();
        var notifier = new JellyfinConfigurationRejectionNotifier(ProviderWith(manager));

        notifier.NotifyRejected(new[] { "bad\u0000value\nwith\tcontrol   characters" });

        var entry = Assert.Single(recorder.Entries);
        Assert.NotNull(entry.Overview);
        Assert.DoesNotContain('\n', entry.Overview!);
        Assert.DoesNotContain('\t', entry.Overview!);
        Assert.DoesNotContain('\u0000', entry.Overview!);
        Assert.Contains("bad value with control characters", entry.Overview!, StringComparison.Ordinal);
    }

    [Fact]
    public void NotifyRejectedDoesNothingWhenTheActivityManagerIsUnavailable()
    {
        using var provider = new ServiceCollection().BuildServiceProvider();
        var notifier = new JellyfinConfigurationRejectionNotifier(provider);

        var exception = Record.Exception(() => notifier.NotifyRejected(new[] { "rejected." }));

        Assert.Null(exception);
    }

    [Fact]
    public void RejectionNotifierIsRegisteredForTheHostServiceProvider()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        new ArrTagsServiceRegistrator().RegisterServices(services, null!);

        using var provider = services.BuildServiceProvider();

        Assert.IsType<JellyfinConfigurationRejectionNotifier>(
            provider.GetRequiredService<IConfigurationRejectionNotifier>());
    }

    private static IServiceProvider ProviderWith(IActivityManager activityManager)
    {
        return new ServiceCollection()
            .AddSingleton(activityManager)
            .BuildServiceProvider();
    }
}
