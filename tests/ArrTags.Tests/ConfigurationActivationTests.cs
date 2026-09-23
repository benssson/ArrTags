using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Serialization;
using ArrTags.Concurrency;
using ArrTags.Configuration;
using ArrTags.PluginLifecycle;
using ArrTags.Providers;
using ArrTags.Secrets;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Model.Activity;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace ArrTags.Tests;

/// <summary>
/// Phase 9 task 9.3 coverage for the elevation-gated save path and runtime
/// configuration activation (ADR-016 clauses 3, 4, and 5 first bullet). These
/// tests drive the real <see cref="Plugin.UpdateConfiguration"/> override: a
/// valid save activates the running snapshot without a host restart, an invalid
/// save is rejected while the last valid snapshot and private secrets stay
/// active and the invalid candidate is not left persisted, the rejection writes
/// exactly one bounded secret-free administrator-visible activity-log entry
/// (ADR-021), the override never throws, no custom configuration-save route is
/// added, and services that resolve from the current snapshot per operation
/// observe the replacement. No live Jellyfin or Arr instance is required.
/// </summary>
public sealed class ConfigurationActivationTests
{
    [Fact]
    public void ValidSaveActivatesTheNewSnapshotWithoutRestart()
    {
        var initial = new PluginConfiguration { WebhookSecret = "stable-webhook-secret-7a1c" };
        using var harness = new ActivationHarness(initial);
        var initialVersion = harness.Snapshot.Current.ConfigurationVersion;

        var candidate = new PluginConfiguration
        {
            WebhookSecret = "stable-webhook-secret-7a1c",
            Radarr = new ArrConnectionConfiguration
            {
                Enabled = true,
                BaseUrl = "https://radarr.local:7878",
                ApiKey = "radarr-key",
            },
        };

        harness.PluginInstance.UpdateConfiguration(candidate);

        // The running snapshot is the new configuration; no restart is needed.
        Assert.True(harness.Snapshot.Current.RadarrEnabled);
        Assert.True(harness.Snapshot.Current.RadarrHasApiKey);
        Assert.Equal("https://radarr.local:7878", harness.Snapshot.Current.RadarrBaseUrl);
        Assert.Equal(initialVersion + 1, harness.Snapshot.Current.ConfigurationVersion);

        // The bounded, secret-free activation result is valid.
        Assert.NotNull(harness.PluginInstance.LastConfigurationValidationResult);
        Assert.True(harness.PluginInstance.LastConfigurationValidationResult!.IsValid);

        // The active and persisted configuration match the candidate.
        Assert.True(harness.PluginInstance.Configuration.Radarr.Enabled);
        Assert.True(harness.ReadPersisted().Radarr.Enabled);
    }

    [Fact]
    public void InvalidSaveRetainsLastValidSnapshotAndPrivateSecrets()
    {
        var initial = new PluginConfiguration
        {
            WebhookSecret = "stable-webhook-secret-7a1c",
        };
        using var harness = new ActivationHarness(initial);
        var initialVersion = harness.Snapshot.Current.ConfigurationVersion;

        var invalid = new PluginConfiguration
        {
            WebhookSecret = "rejected-webhook-secret-3f9d",
            Radarr = new ArrConnectionConfiguration
            {
                Enabled = true,
                BaseUrl = "not-a-url",
                ApiKey = "rejected-radarr-key-1b2e",
            },
        };

        harness.PluginInstance.UpdateConfiguration(invalid);

        // The last valid public snapshot is retained.
        Assert.Equal(initialVersion, harness.Snapshot.Current.ConfigurationVersion);
        Assert.False(harness.Snapshot.Current.RadarrEnabled);
        Assert.Equal(string.Empty, harness.Snapshot.Current.RadarrBaseUrl);

        // The private secret generation is retained: the previous value is still
        // resolvable at the retained version and the rejected value is not.
        Assert.True(harness.Snapshot.TryAcquire(
            SecretReference.WebhookAuthentication,
            initialVersion,
            out var lease));
        Assert.True(lease!.Matches("stable-webhook-secret-7a1c"));
        Assert.False(lease.Matches("rejected-webhook-secret-3f9d"));
        lease.Dispose();
    }

    [Fact]
    public void InvalidSaveDoesNotPersistTheRejectedCandidate()
    {
        var initial = new PluginConfiguration
        {
            WebhookSecret = "stable-webhook-secret-7a1c",
        };
        using var harness = new ActivationHarness(initial);

        var invalid = new PluginConfiguration
        {
            WebhookSecret = "rejected-webhook-secret-3f9d",
            Radarr = new ArrConnectionConfiguration
            {
                Enabled = true,
                BaseUrl = "not-a-url",
                ApiKey = "rejected-radarr-key-1b2e",
            },
        };

        harness.PluginInstance.UpdateConfiguration(invalid);

        // The candidate is validated before the base implementation persists
        // anything, so a rejected candidate is never written and the last valid
        // configuration remains active and persisted (security finding S-9.2-03,
        // security finding SEC-9.3-01).
        Assert.False(harness.PluginInstance.Configuration.Radarr.Enabled);
        var persisted = harness.ReadPersisted();
        Assert.False(persisted.Radarr.Enabled);
        Assert.Equal("stable-webhook-secret-7a1c", persisted.WebhookSecret);
        Assert.DoesNotContain("rejected-webhook-secret-3f9d", persisted.WebhookSecret, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ConcurrentValidAndInvalidSavesKeepSnapshotConfigurationAndPersistedStateConsistent()
    {
        var initial = new PluginConfiguration { WebhookSecret = "initial-webhook-secret-1a2b" };
        using var harness = new ActivationHarness(initial);

        var invalid = new PluginConfiguration
        {
            WebhookSecret = "rejected-webhook-secret-3f9d",
            Radarr = new ArrConnectionConfiguration
            {
                Enabled = true,
                BaseUrl = "not-a-url",
                ApiKey = "rejected-radarr-key-1b2e",
            },
        };
        var valid = new PluginConfiguration
        {
            WebhookSecret = "accepted-webhook-secret-5c6d",
            Radarr = new ArrConnectionConfiguration
            {
                Enabled = true,
                BaseUrl = "https://radarr.local:7878",
                ApiKey = "accepted-radarr-key-7e8f",
            },
        };

        // Under the previous non-atomic implementation the invalid save persisted
        // its candidate and then re-persisted the pre-save configuration, which
        // could overwrite a concurrent valid save. Block the invalid save inside
        // its persistence step if (and only if) it is persisted at all, so the
        // interleaving that reproduced the divergence is deterministic.
        harness.Serializer.BlockOn(candidate => ReferenceEquals(candidate, invalid));

        var invalidSave = Task.Run(() => harness.PluginInstance.UpdateConfiguration(invalid));
        var blocked = harness.Serializer.WaitForBlock(TimeSpan.FromSeconds(2));

        // Run the valid save to completion while the invalid save is (or is not)
        // held inside persistence.
        harness.PluginInstance.UpdateConfiguration(valid);

        if (blocked)
        {
            harness.Serializer.ReleaseBlock();
        }

        await invalidSave.WaitAsync(TimeSpan.FromSeconds(10));

        // The invalid candidate is validated before persistence, so it must never
        // reach the persistence step (under the previous non-atomic
        // implementation it did, and the later restore could overwrite the valid
        // save).
        Assert.False(blocked, "The rejected candidate must not be persisted.");

        // The running snapshot, the in-memory configuration, and the persisted
        // file must all agree on the valid candidate.
        Assert.True(harness.Snapshot.Current.RadarrEnabled);
        Assert.Equal("https://radarr.local:7878", harness.Snapshot.Current.RadarrBaseUrl);
        Assert.True(harness.PluginInstance.Configuration.Radarr.Enabled);
        Assert.Equal("https://radarr.local:7878", harness.PluginInstance.Configuration.Radarr.BaseUrl);
        Assert.Equal("accepted-webhook-secret-5c6d", harness.PluginInstance.Configuration.WebhookSecret);

        var persisted = harness.ReadPersisted();
        Assert.True(persisted.Radarr.Enabled);
        Assert.Equal("https://radarr.local:7878", persisted.Radarr.BaseUrl);
        Assert.Equal("accepted-webhook-secret-5c6d", persisted.WebhookSecret);
    }

    [Fact]
    public void InvalidSaveWritesExactlyOneBoundedSecretFreeActivityEntry()
    {
        var initial = new PluginConfiguration { WebhookSecret = "stable-webhook-secret-7a1c" };
        using var harness = new ActivationHarness(initial);

        var invalid = new PluginConfiguration
        {
            WebhookSecret = "rejected-webhook-secret-3f9d",
            Radarr = new ArrConnectionConfiguration
            {
                Enabled = true,
                BaseUrl = "not-a-url",
                ApiKey = "rejected-radarr-key-1b2e",
            },
        };

        harness.PluginInstance.UpdateConfiguration(invalid);

        // The rejection is surfaced as exactly one administrator-visible entry.
        var entry = Assert.Single(harness.ActivityRecorder.Entries);
        Assert.Equal(JellyfinConfigurationRejectionNotifier.EntryName, entry.Name);
        Assert.Equal(JellyfinConfigurationRejectionNotifier.EntryType, entry.Type);
        Assert.Equal(Guid.Empty, entry.UserId);
        Assert.Equal(LogLevel.Warning, entry.LogSeverity);
        Assert.True(entry.Overview!.Length <= 512);
        Assert.True(entry.ShortOverview!.Length <= 512);

        var combined = string.Join(" ", entry.Name, entry.Overview, entry.ShortOverview);
        Assert.DoesNotContain("rejected-webhook-secret-3f9d", combined, StringComparison.Ordinal);
        Assert.DoesNotContain("rejected-radarr-key-1b2e", combined, StringComparison.Ordinal);
        Assert.DoesNotContain("stable-webhook-secret-7a1c", combined, StringComparison.Ordinal);
        Assert.DoesNotContain("not-a-url", combined, StringComparison.Ordinal);

        // The bounded, secret-free validation result is still retained for
        // diagnostics.
        Assert.NotNull(harness.PluginInstance.LastConfigurationValidationResult);
        Assert.False(harness.PluginInstance.LastConfigurationValidationResult!.IsValid);
    }

    [Fact]
    public void ValidSaveWritesNoActivityEntry()
    {
        using var harness = new ActivationHarness(new PluginConfiguration());

        var candidate = new PluginConfiguration
        {
            Radarr = new ArrConnectionConfiguration
            {
                Enabled = true,
                BaseUrl = "https://radarr.local",
                ApiKey = "radarr-key",
            },
        };

        harness.PluginInstance.UpdateConfiguration(candidate);

        Assert.Empty(harness.ActivityRecorder.Entries);
    }

    [Fact]
    public void InvalidSaveDoesNotThrowWhenTheNotifierFailsAndRetainsLastValid()
    {
        var initial = new PluginConfiguration { WebhookSecret = "stable-webhook-secret-7a1c" };
        using var harness = new ActivationHarness(initial, rejectionNotifier: new ThrowingRejectionNotifier());
        var initialVersion = harness.Snapshot.Current.ConfigurationVersion;

        var invalid = new PluginConfiguration
        {
            WebhookSecret = "rejected-webhook-secret-3f9d",
            Radarr = new ArrConnectionConfiguration
            {
                Enabled = true,
                BaseUrl = "not-a-url",
                ApiKey = "rejected-radarr-key-1b2e",
            },
        };

        Assert.Null(Record.Exception(() => harness.PluginInstance.UpdateConfiguration(invalid)));

        // A failing notifier does not prevent the last valid retention.
        Assert.Equal(initialVersion, harness.Snapshot.Current.ConfigurationVersion);
        Assert.False(harness.Snapshot.Current.RadarrEnabled);
        Assert.False(harness.PluginInstance.Configuration.Radarr.Enabled);
        Assert.Empty(harness.ActivityRecorder.Entries);
    }

    [Fact]
    public void UpdateConfigurationNeverThrows()
    {
        using var harness = new ActivationHarness(new PluginConfiguration());

        var valid = new PluginConfiguration
        {
            Radarr = new ArrConnectionConfiguration
            {
                Enabled = true,
                BaseUrl = "https://radarr.local",
                ApiKey = "radarr-key",
            },
        };
        var invalid = new PluginConfiguration
        {
            Radarr = new ArrConnectionConfiguration
            {
                Enabled = true,
                BaseUrl = "not-a-url",
                ApiKey = "radarr-key",
            },
        };

        Assert.Null(Record.Exception(() => harness.PluginInstance.UpdateConfiguration(valid)));
        Assert.Null(Record.Exception(() => harness.PluginInstance.UpdateConfiguration(invalid)));
    }

    [Fact]
    public void UpdateConfigurationDoesNotThrowWhenTheSnapshotServiceIsUnavailable()
    {
        using var harness = new ActivationHarness(new PluginConfiguration(), registerSnapshotService: false);

        var candidate = new PluginConfiguration
        {
            Radarr = new ArrConnectionConfiguration
            {
                Enabled = true,
                BaseUrl = "https://radarr.local",
                ApiKey = "radarr-key",
            },
        };

        Assert.Null(Record.Exception(() => harness.PluginInstance.UpdateConfiguration(candidate)));

        // Validation now happens before persistence, so the bounded secret-free
        // result is recorded even when the running snapshot cannot be resolved;
        // the persisted candidate is activated on the next host startup.
        Assert.NotNull(harness.PluginInstance.LastConfigurationValidationResult);
        Assert.True(harness.PluginInstance.LastConfigurationValidationResult!.IsValid);
    }

    [Fact]
    public void PluginAddsNoCustomConfigurationSaveRoute()
    {
        var pluginType = typeof(Plugin);

        // The plugin declares the supported IHasPluginConfiguration override and
        // no MVC route/action of its own.
        var overrideMethod = pluginType.GetMethod(nameof(Plugin.UpdateConfiguration));
        Assert.NotNull(overrideMethod);
        Assert.True(overrideMethod!.IsVirtual);
        Assert.Equal(pluginType, overrideMethod.DeclaringType);

        var routeAttributeNames = new HashSet<string>(StringComparer.Ordinal)
        {
            "RouteAttribute",
            "HttpPostAttribute",
            "HttpPutAttribute",
            "HttpPatchAttribute",
            "HttpGetAttribute",
            "ApiControllerAttribute",
        };

        Assert.DoesNotContain(
            pluginType.GetCustomAttributesData(),
            attribute => routeAttributeNames.Contains(attribute.AttributeType.Name));
        Assert.DoesNotContain(
            pluginType.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .SelectMany(method => method.GetCustomAttributesData()),
            attribute => routeAttributeNames.Contains(attribute.AttributeType.Name));

        // No controller in the plugin assembly exposes a configuration route;
        // configuration is saved only through the elevation-gated
        // PluginsController path (ADR-016 clause 3).
        var configurationRoutes = pluginType.Assembly
            .GetTypes()
            .Where(type => typeof(ControllerBase).IsAssignableFrom(type))
            .SelectMany(type => type.GetCustomAttributesData()
                .Concat(type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                    .SelectMany(method => method.GetCustomAttributesData())))
            .Where(attribute => attribute.AttributeType.Name == "RouteAttribute")
            .Select(attribute => attribute.ConstructorArguments.Count == 0
                ? null
                : attribute.ConstructorArguments[0].Value as string)
            .Where(route => route is not null && route.Contains("Configuration", StringComparison.OrdinalIgnoreCase))
            .ToList();

        Assert.Empty(configurationRoutes);
    }

    [Fact]
    public async Task PerOperationSnapshotResolutionObservesTheReplacedSnapshot()
    {
        var initial = new PluginConfiguration
        {
            Limits = new OperationalLimits
            {
                ProviderConcurrencyGlobal = 1,
                ProviderConcurrencyPerConnection = 8,
            },
        };
        using var harness = new ActivationHarness(initial);

        // A service built from the configuration snapshot resolves its limit on
        // every operation rather than capturing it at construction.
        using var limiter = new ProviderConcurrencyLimiter(harness.Snapshot);
        var connection = ArrConnectionId.For(ArrProviderKind.Radarr, "http://radarr.test");

        using (await limiter.AcquireAsync(connection, CancellationToken.None))
        {
            Assert.Equal(1, limiter.GlobalActiveCount);
        }

        harness.PluginInstance.UpdateConfiguration(new PluginConfiguration
        {
            Limits = new OperationalLimits
            {
                ProviderConcurrencyGlobal = 2,
                ProviderConcurrencyPerConnection = 8,
            },
        });

        // The next acquisition observes the replaced snapshot without a rebuild.
        using var first = await limiter.AcquireAsync(connection, CancellationToken.None);
        using var second = await limiter.AcquireAsync(connection, CancellationToken.None);
        Assert.Equal(2, limiter.GlobalActiveCount);
    }

    /// <summary>
    /// A plugin harness with a real XML configuration file and a service
    /// provider that resolves the configuration snapshot singleton, mirroring
    /// the host's plugin construction and elevation-gated save path.
    /// </summary>
    private sealed class ActivationHarness : IDisposable
    {
        private readonly string _root;

        public ActivationHarness(
            PluginConfiguration initial,
            bool registerSnapshotService = true,
            IConfigurationRejectionNotifier? rejectionNotifier = null)
        {
            _root = Path.Combine(Path.GetTempPath(), "arrtags-activation-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(_root, "plugins"));
            Directory.CreateDirectory(Path.Combine(_root, "configurations"));

            Paths = CreateApplicationPaths(_root);
            Serializer = new TestXmlSerializer();
            Serializer.SerializeToFile(initial, ConfigurationFilePath);

            Snapshot = new ConfigurationSnapshotService(initial);
            var (activityManager, activityRecorder) = RecordingActivityManager.Create();
            ActivityManager = activityManager;
            ActivityRecorder = activityRecorder;

            var services = new ServiceCollection();
            if (registerSnapshotService)
            {
                services.AddSingleton(Snapshot);
            }

            services.AddSingleton(ActivityManager);
            if (rejectionNotifier is null)
            {
                services.AddSingleton<IConfigurationRejectionNotifier, JellyfinConfigurationRejectionNotifier>();
            }
            else
            {
                services.AddSingleton<IConfigurationRejectionNotifier>(rejectionNotifier);
            }

            Provider = services.BuildServiceProvider();
            PluginInstance = new Plugin(Paths, Serializer, Provider);
        }

        public IApplicationPaths Paths { get; }

        public TestXmlSerializer Serializer { get; }

        public ConfigurationSnapshotService Snapshot { get; }

        public IActivityManager ActivityManager { get; }

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
        }

        private static IApplicationPaths CreateApplicationPaths(string root)
        {
            var paths = DispatchProxy.Create<IApplicationPaths, TestApplicationPaths>();
            ((TestApplicationPaths)(object)paths).RootPath = root;
            return paths;
        }
    }

    /// <summary>
    /// A minimal application-paths double that supplies the members the plugin
    /// constructor and configuration persistence read.
    /// </summary>
    public class TestApplicationPaths : DispatchProxy
    {
        public string RootPath { get; set; } = string.Empty;

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            return targetMethod?.Name switch
            {
                "get_ProgramDataPath" => RootPath,
                "get_PluginsPath" => Path.Combine(RootPath, "plugins"),
                "get_PluginConfigurationsPath" => Path.Combine(RootPath, "configurations"),
                "get_DataPath" => Path.Combine(RootPath, "data"),
                _ => throw new NotSupportedException(targetMethod?.Name),
            };
        }
    }

    /// <summary>
    /// A real <see cref="IXmlSerializer"/> over <see cref="XmlSerializer"/> so
    /// the plugin's base configuration persistence is exercised end to end
    /// without the Jellyfin host.
    /// </summary>
    public sealed class TestXmlSerializer : IXmlSerializer, IDisposable
    {
        private readonly object _gateLock = new object();
        private Func<object, bool>? _blockPredicate;
        private ManualResetEventSlim? _entered;
        private ManualResetEventSlim? _release;

        /// <summary>
        /// Blocks the next file serialization whose object matches the predicate.
        /// Used to force the concurrent-save interleaving deterministically.
        /// </summary>
        /// <param name="predicate">Matches the object to hold inside persistence.</param>
        public void BlockOn(Func<object, bool> predicate)
        {
            lock (_gateLock)
            {
                _blockPredicate = predicate;
                _entered = new ManualResetEventSlim(false);
                _release = new ManualResetEventSlim(false);
            }
        }

        /// <summary>
        /// Waits for the blocked serialization to be entered.
        /// </summary>
        /// <param name="timeout">The bounded wait.</param>
        /// <returns><see langword="true"/> when a matching serialization blocked.</returns>
        public bool WaitForBlock(TimeSpan timeout)
        {
            ManualResetEventSlim? entered;
            lock (_gateLock)
            {
                entered = _entered;
            }

            return entered is not null && entered.Wait(timeout);
        }

        /// <summary>
        /// Releases the blocked serialization and clears the predicate.
        /// </summary>
        public void ReleaseBlock()
        {
            lock (_gateLock)
            {
                _release?.Set();
                _blockPredicate = null;
            }
        }

        /// <inheritdoc />
        public object DeserializeFromStream(Type type, Stream stream)
        {
            var serializer = new XmlSerializer(type);
            return serializer.Deserialize(stream)!;
        }

        /// <inheritdoc />
        public void SerializeToStream(object obj, Stream stream)
        {
            var serializer = new XmlSerializer(obj.GetType());
            serializer.Serialize(stream, obj);
        }

        /// <inheritdoc />
        public void SerializeToFile(object obj, string file)
        {
            Func<object, bool>? predicate;
            ManualResetEventSlim? entered;
            ManualResetEventSlim? release;
            lock (_gateLock)
            {
                predicate = _blockPredicate;
                entered = _entered;
                release = _release;
            }

            if (predicate is not null && predicate(obj))
            {
                entered?.Set();
                release?.Wait(TimeSpan.FromSeconds(10));
            }

            var serializer = new XmlSerializer(obj.GetType());
            using var writer = new StreamWriter(file);
            serializer.Serialize(writer, obj);
        }

        /// <inheritdoc />
        public object DeserializeFromFile(Type type, string file)
        {
            var serializer = new XmlSerializer(type);
            using var reader = new StreamReader(file);
            return serializer.Deserialize(reader)!;
        }

        /// <inheritdoc />
        public object DeserializeFromBytes(Type type, byte[] buffer)
        {
            var serializer = new XmlSerializer(type);
            using var stream = new MemoryStream(buffer);
            return serializer.Deserialize(stream)!;
        }

        /// <inheritdoc />
        public void Dispose()
        {
            lock (_gateLock)
            {
                _entered?.Dispose();
                _release?.Dispose();
                _entered = null;
                _release = null;
            }
        }
    }
}
