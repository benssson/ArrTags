using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ArrTags.Configuration;
using ArrTags.PluginLifecycle;
using ArrTags.Reconciliation;
using ArrTags.Rendering;
using ArrTags.Secrets;
using Jellyfin.Extensions.Json;
using MediaBrowser.Common.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ArrTags.Tests;

/// <summary>
/// Phase 9 task 9.5 Goal A integration verification. Unlike the per-aspect task
/// 9.1-9.4 tests, this class composes the real dashboard save path end to end:
/// the pinned elevation-gated <c>PluginsController</c> JSON deserialization, the
/// real <see cref="Plugin.UpdateConfiguration"/> override, the real
/// <see cref="ConfigurationSnapshotService"/>, the real
/// <see cref="ConfigurationReconciliationTrigger"/>, the real bounded
/// <see cref="LibraryReconciliationService"/>, and the real artwork publishing
/// pipeline. A valid save round-trips its collections, activates on the running
/// services without a host restart, rotates the private secrets, requests exactly
/// one bounded reconciliation, and re-renders an existing poster; an invalid save
/// retains the last valid snapshot and private secrets, leaves the persisted file
/// unchanged, surfaces exactly one secret-free administrator entry, and requests
/// no reconciliation. No live Jellyfin or Arr instance is required.
/// </summary>
public sealed class GoalAIntegrationTests
{
    [Fact]
    public async Task DashboardSaveRoundTripsActivatesAndBoundedlyReconcilesAnExistingPoster()
    {
        using var harness = new GoalAIntegrationHarness();
        harness.Phase.Reader.Handler = Phase6Harness.Matched();
        harness.Phase.Host.CurrentBytes = Phase6Harness.Original();

        // Publish the initial poster under configuration version 1.
        await harness.Phase.Recovering.ProcessAsync(harness.Phase.WorkItem(), CancellationToken.None);
        Assert.Equal(1, harness.Phase.Host.SaveCalls);
        Assert.Equal(1, harness.Phase.Renderer.Calls);
        var initialFingerprint = harness.Phase.States
            .Read(harness.Phase.ItemId, Phase6Harness.Surface).Value!.PublishedFingerprint;
        var initialConfigurationFingerprint = harness.Phase.Renderer.LastRequest!.ConfigurationFingerprint;

        await harness.Trigger.StartAsync(CancellationToken.None);
        try
        {
            // The dashboard POST body: PascalCase JSON deserialized with the same
            // pinned options the elevation-gated PluginsController POST uses. Both
            // previously get-only collections are populated.
            var candidate = Deserialize(SettingsPayload(
                harness.Phase.LibraryId.ToString(),
                "second-library"));
            Assert.Equal(
                new[] { harness.Phase.LibraryId.ToString(), "second-library" },
                candidate.EnabledLibraries.ToArray());

            harness.PluginInstance.UpdateConfiguration(candidate);

            // Round-trip + activation: the running snapshot service the pipeline
            // already uses observes the replacement, so no host restart occurred.
            Assert.NotNull(harness.PluginInstance.LastConfigurationValidationResult);
            Assert.True(harness.PluginInstance.LastConfigurationValidationResult!.IsValid);
            Assert.Equal(2, harness.Phase.Configuration.Current.ConfigurationVersion);
            Assert.Equal(
                new[] { harness.Phase.LibraryId.ToString(), "second-library" },
                harness.Phase.Configuration.Current.EnabledLibraries.ToArray());
            Assert.True(harness.Phase.Configuration.Current.RadarrEnabled);
            Assert.False(harness.Phase.Configuration.Current.BadgeEpisodePosters);

            // The collections and the changed renderer template survived the XML
            // persistence step, so a dashboard save does not silently drop them.
            var persisted = harness.ReadPersisted();
            Assert.Equal(
                new[] { harness.Phase.LibraryId.ToString(), "second-library" },
                persisted.EnabledLibraries.ToArray());
            Assert.Equal("Q{value}", persisted.Renderer.Selectors.Single().Template);

            // Secret rotation is atomic with the replacement: the new value is
            // resolvable at the new version and the retired value is not.
            Assert.True(harness.Phase.Configuration.TryAcquire(
                SecretReference.WebhookAuthentication,
                2,
                out var lease));
            Assert.True(lease!.Matches("rotated-goala-webhook-secret-9f0a"));
            Assert.False(lease.Matches("stable-goala-webhook-secret-3b2c"));
            lease.Dispose();
            Assert.False(harness.Phase.Configuration.TryAcquire(
                SecretReference.WebhookAuthentication,
                1,
                out _));

            // The successful save requested the bounded post-save reconciliation,
            // which ran the real bounded reconciliation service off the save
            // thread and enqueued work at the new snapshot version.
            await Phase6Wait.UntilAsync(() => harness.Phase.Queue.Count >= 1);
            var workItem = await harness.Phase.Queue.DequeueAsync(CancellationToken.None);
            Assert.Equal(2, workItem.ConfigurationVersion);

            await harness.Phase.Publishing.ProcessAsync(workItem, CancellationToken.None);

            // The existing published poster re-rendered with the saved settings.
            Assert.Equal(2, harness.Phase.Host.SaveCalls);
            Assert.Equal(2, harness.Phase.Renderer.Calls);
            Assert.NotEqual(
                initialConfigurationFingerprint,
                harness.Phase.Renderer.LastRequest!.ConfigurationFingerprint);
            Assert.Contains(
                harness.Phase.Renderer.LastRequest.BadgeDefinitions,
                definition => definition.Selector == BadgeSelector.Quality && definition.Template == "Q{value}");
            var reRenderedFingerprint = harness.Phase.States
                .Read(harness.Phase.ItemId, Phase6Harness.Surface).Value!.PublishedFingerprint;
            Assert.NotNull(reRenderedFingerprint);
            Assert.NotEqual(initialFingerprint, reRenderedFingerprint);
        }
        finally
        {
            await harness.Trigger.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task InvalidDashboardSaveRetainsLastValidSnapshotAndSecretsWithoutReconciling()
    {
        using var harness = new GoalAIntegrationHarness();
        var initialVersion = harness.Phase.Configuration.Current.ConfigurationVersion;

        await harness.Trigger.StartAsync(CancellationToken.None);
        try
        {
            var candidate = Deserialize(InvalidSettingsPayload());

            Assert.Null(Record.Exception(() => harness.PluginInstance.UpdateConfiguration(candidate)));

            // The rejected candidate was not activated: the running service still
            // reports the last valid snapshot at the unchanged version.
            Assert.NotNull(harness.PluginInstance.LastConfigurationValidationResult);
            Assert.False(harness.PluginInstance.LastConfigurationValidationResult!.IsValid);
            Assert.Equal(initialVersion, harness.Phase.Configuration.Current.ConfigurationVersion);
            Assert.True(harness.Phase.Configuration.Current.RadarrEnabled);
            Assert.Equal("http://radarr.test", harness.Phase.Configuration.Current.RadarrBaseUrl);
            Assert.Empty(harness.Phase.Configuration.Current.EnabledLibraries);

            // The private secret generation is retained at the retained version.
            Assert.True(harness.Phase.Configuration.TryAcquire(
                SecretReference.WebhookAuthentication,
                initialVersion,
                out var lease));
            Assert.True(lease!.Matches("stable-goala-webhook-secret-3b2c"));
            Assert.False(lease.Matches("rejected-goala-webhook-secret-7d4e"));
            lease.Dispose();

            // The rejected candidate was never persisted.
            var persisted = harness.ReadPersisted();
            Assert.Equal("stable-goala-webhook-secret-3b2c", persisted.WebhookSecret);
            Assert.Equal("http://radarr.test", persisted.Radarr.BaseUrl);
            Assert.Empty(persisted.EnabledLibraries);

            // Exactly one bounded, secret-free administrator-visible entry.
            var entry = Assert.Single(harness.ActivityRecorder.Entries);
            var combined = string.Join(" ", entry.Name, entry.Overview, entry.ShortOverview);
            Assert.DoesNotContain("rejected-goala-webhook-secret-7d4e", combined, StringComparison.Ordinal);
            Assert.DoesNotContain("not-a-url", combined, StringComparison.Ordinal);

            // The rejected save requested no reconciliation: the running trigger
            // loop observes no request and the bounded queue stays empty.
            await AssertStableAsync(() => harness.Phase.Queue.Count, expected: 0);
        }
        finally
        {
            await harness.Trigger.StopAsync(CancellationToken.None);
        }
    }

    private static PluginConfiguration Deserialize(string payload)
    {
        var configuration = JsonSerializer.Deserialize<PluginConfiguration>(payload, JsonDefaults.Options);
        Assert.NotNull(configuration);
        return configuration!;
    }

    private static string SettingsPayload(params string[] libraries)
    {
        var librariesJson = string.Join(", ", libraries.Select(library => $"\"{library}\""));
        return $$"""
            {
              "Sonarr": { "Enabled": false },
              "Radarr": {
                "Enabled": true,
                "BaseUrl": "http://radarr.test",
                "ApiKey": "rotated-radarr-api-key"
              },
              "WebhookSecret": "rotated-goala-webhook-secret-9f0a",
              "EnabledLibraries": [ {{librariesJson}} ],
              "BadgeMoviePosters": true,
              "BadgeEpisodePosters": false,
              "Renderer": {
                "Selectors": [
                  { "Selector": "Quality", "Enabled": true, "Template": "Q{value}" }
                ],
                "TechnicalBackground": "",
                "TechnicalText": "",
                "StatusBackground": "",
                "StatusText": ""
              }
            }
            """;
    }

    private static string InvalidSettingsPayload()
    {
        return """
            {
              "Sonarr": { "Enabled": false },
              "Radarr": {
                "Enabled": true,
                "BaseUrl": "not-a-url",
                "ApiKey": "rejected-radarr-api-key"
              },
              "WebhookSecret": "rejected-goala-webhook-secret-7d4e",
              "EnabledLibraries": [ "rejected-library" ],
              "BadgeMoviePosters": true,
              "BadgeEpisodePosters": false
            }
            """;
    }

    /// <summary>
    /// Polls <paramref name="observe"/> for a bounded stability window and fails
    /// if it ever differs from <paramref name="expected"/>, so a negative
    /// assertion does not rely on a single fixed delay.
    /// </summary>
    /// <param name="observe">The observed value.</param>
    /// <param name="expected">The value that must hold throughout the window.</param>
    /// <param name="stabilityMilliseconds">The bounded stability window.</param>
    /// <returns>A task that completes when the window elapses.</returns>
    private static async Task AssertStableAsync(Func<int> observe, int expected, int stabilityMilliseconds = 300)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(stabilityMilliseconds);
        while (DateTime.UtcNow < deadline)
        {
            Assert.Equal(expected, observe());
            await Task.Delay(10).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// The Goal A end-to-end harness. It composes the real plugin save path with
    /// the real bounded post-save reconciliation trigger over the real bounded
    /// reconciliation service, the real configuration snapshot service, and the
    /// real artwork publishing pipeline. The plugin persists to a real temporary
    /// XML configuration file, so no live host is required.
    /// </summary>
    private sealed class GoalAIntegrationHarness : IDisposable
    {
        private readonly string _root;

        public GoalAIntegrationHarness()
        {
            _root = Path.Combine(Path.GetTempPath(), "arrtags-goala-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(_root, "plugins"));
            Directory.CreateDirectory(Path.Combine(_root, "configurations"));

            Paths = CreateApplicationPaths(_root);
            Serializer = new ConfigurationActivationTests.TestXmlSerializer();

            var initial = CreateInitialConfiguration();
            Serializer.SerializeToFile(initial, ConfigurationFilePath);

            Phase = new Phase6Harness(configuration: initial);
            Enumerator = new FakeMediaLibraryEnumerator();
            Enumerator.Items.Add(Phase.Library.Items[Phase.ItemId]);

            var reconciliation = new LibraryReconciliationService(
                Phase.Configuration,
                Phase.Library,
                Enumerator,
                Phase.Queue,
                Phase.Fences);
            Trigger = new ConfigurationReconciliationTrigger(reconciliation, TimeSpan.FromSeconds(2));

            var (activityManager, activityRecorder) = RecordingActivityManager.Create();
            ActivityRecorder = activityRecorder;

            var services = new ServiceCollection();
            services.AddSingleton(Phase.Configuration);
            services.AddSingleton<IConfigurationReconciliationTrigger>(Trigger);
            services.AddSingleton(activityManager);
            services.AddSingleton<IConfigurationRejectionNotifier, JellyfinConfigurationRejectionNotifier>();

            Provider = services.BuildServiceProvider();
            PluginInstance = new Plugin(Paths, Serializer, Provider);
        }

        public IApplicationPaths Paths { get; }

        public ConfigurationActivationTests.TestXmlSerializer Serializer { get; }

        public Phase6Harness Phase { get; }

        public FakeMediaLibraryEnumerator Enumerator { get; }

        public ConfigurationReconciliationTrigger Trigger { get; }

        public RecordingActivityManager ActivityRecorder { get; }

        public ServiceProvider Provider { get; }

        public Plugin PluginInstance { get; }

        private string ConfigurationFilePath => Path.Combine(_root, "configurations", "ArrTags.xml");

        public PluginConfiguration ReadPersisted()
        {
            return (PluginConfiguration)Serializer.DeserializeFromFile(
                typeof(PluginConfiguration),
                ConfigurationFilePath);
        }

        public void Dispose()
        {
            Provider.Dispose();
            Trigger.Dispose();
            Phase.Dispose();
            Serializer.Dispose();

            try
            {
                if (Directory.Exists(_root))
                {
                    Directory.Delete(_root, recursive: true);
                }
            }
            catch (IOException)
            {
                // Best-effort test cleanup.
            }
            catch (UnauthorizedAccessException)
            {
                // Best-effort test cleanup.
            }
        }

        private static PluginConfiguration CreateInitialConfiguration()
        {
            return new PluginConfiguration
            {
                WebhookSecret = "stable-goala-webhook-secret-3b2c",
                BadgeMoviePosters = true,
                Radarr = new ArrConnectionConfiguration
                {
                    Enabled = true,
                    BaseUrl = "http://radarr.test",
                    ApiKey = "test-radarr-api-key",
                },
            };
        }

        private static IApplicationPaths CreateApplicationPaths(string root)
        {
            var paths = DispatchProxy.Create<IApplicationPaths, ConfigurationActivationTests.TestApplicationPaths>();
            ((ConfigurationActivationTests.TestApplicationPaths)(object)paths).RootPath = root;
            return paths;
        }
    }
}
