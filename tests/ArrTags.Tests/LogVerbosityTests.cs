using System;
using System.Collections.Concurrent;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Xml.Serialization;
using ArrTags.Artwork;
using ArrTags.Configuration;
using ArrTags.PluginLifecycle;
using ArrTags.Rendering;
using Jellyfin.Extensions.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ArrTags.Tests;

/// <summary>
/// Phase 10 task 10.1 coverage for the logging foundation: the bounded
/// <see cref="LogVerbosity"/> enum, its configuration validation and page/XML
/// exposure, the plugin-owned gating boundary read from the current snapshot,
/// host DI resolution without a custom provider, and the proof that verbosity is
/// excluded from the renderer/configuration output fingerprints and
/// <see cref="RenderVersion"/> (ADR-020 clauses 1, 2, 3, and 5). The
/// output-fingerprint exclusion is proven on the real production
/// <see cref="ArtworkRegenerationPlanner"/> path rather than by re-assembling a
/// <see cref="RenderFingerprintInput"/> in the test.
/// </summary>
public class LogVerbosityTests
{
    private static readonly ArtworkImageSurface Surface = ArtworkImageSurface.Primary;

    private static readonly DateTimeOffset At = DateTimeOffset.UnixEpoch.AddDays(1);

    private static readonly string PlaceholderFingerprint = new('A', 64);

    [Fact]
    public void DefaultVerbosityIsWarning()
    {
        var configuration = new PluginConfiguration();

        Assert.Equal(LogVerbosity.Warning, configuration.LogVerbosity);
        Assert.True(PluginConfigurationValidator.Validate(configuration).IsValid);
        Assert.Equal(LogVerbosity.Warning, PluginConfigurationSnapshot.From(configuration).LogVerbosity);
    }

    [Theory]
    [InlineData(LogVerbosity.Off)]
    [InlineData(LogVerbosity.Error)]
    [InlineData(LogVerbosity.Warning)]
    [InlineData(LogVerbosity.Information)]
    [InlineData(LogVerbosity.Debug)]
    [InlineData(LogVerbosity.Trace)]
    public void ValidatorAcceptsEveryDefinedVerbosity(LogVerbosity verbosity)
    {
        var configuration = new PluginConfiguration { LogVerbosity = verbosity };

        Assert.True(PluginConfigurationValidator.Validate(configuration).IsValid);
        Assert.Equal(verbosity, PluginConfigurationSnapshot.From(configuration).LogVerbosity);
    }

    [Fact]
    public void ValidatorRejectsUndefinedVerbosityWithASecretFreeMessage()
    {
        var configuration = new PluginConfiguration
        {
            LogVerbosity = (LogVerbosity)999,
            WebhookSecret = "webhook-super-secret",
        };
        configuration.Sonarr.ApiKey = "sonarr-super-secret";

        var result = PluginConfigurationValidator.Validate(configuration);
        var combined = string.Join(" ", result.Errors);

        Assert.False(result.IsValid);
        Assert.Contains("verbosity", combined, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("webhook-super-secret", combined, StringComparison.Ordinal);
        Assert.DoesNotContain("sonarr-super-secret", combined, StringComparison.Ordinal);
    }

    [Fact]
    public void SnapshotServiceRejectsUndefinedVerbosityAndKeepsTheLastValidSnapshot()
    {
        var service = new ConfigurationSnapshotService(new PluginConfiguration
        {
            LogVerbosity = LogVerbosity.Information,
        });

        var invalid = new PluginConfiguration { LogVerbosity = (LogVerbosity)999 };

        Assert.False(service.TryReplace(invalid, out var result));
        Assert.False(result.IsValid);
        Assert.Equal(LogVerbosity.Information, service.Current.LogVerbosity);
    }

    [Fact]
    public void PostRoundTripPopulatesLogVerbosity()
    {
        var configuration = JsonSerializer.Deserialize<PluginConfiguration>(
            """{ "LogVerbosity": "Debug" }""",
            JsonDefaults.Options);

        Assert.NotNull(configuration);
        Assert.Equal(LogVerbosity.Debug, configuration!.LogVerbosity);
        Assert.True(PluginConfigurationValidator.Validate(configuration).IsValid);
    }

    [Fact]
    public void XmlRoundTripsLogVerbosity()
    {
        var configuration = new PluginConfiguration { LogVerbosity = LogVerbosity.Debug };

        var serializer = new XmlSerializer(typeof(PluginConfiguration));
        using var writer = new StringWriter();
        serializer.Serialize(writer, configuration);

        Assert.Contains("<LogVerbosity>Debug</LogVerbosity>", writer.ToString(), StringComparison.Ordinal);

        using var reader = new StringReader(writer.ToString());
        var restored = (PluginConfiguration)serializer.Deserialize(reader)!;

        Assert.Equal(LogVerbosity.Debug, restored.LogVerbosity);
    }

    [Fact]
    public void XmlWithoutLogVerbosityLoadsTheWarningDefault()
    {
        // The persisted XML shape stays loadable: an existing ArrTags.xml without
        // the new element keeps the default.
        var serializer = new XmlSerializer(typeof(PluginConfiguration));
        using var reader = new StringReader(
            "<PluginConfiguration xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\" xmlns:xsd=\"http://www.w3.org/2001/XMLSchema\"><BadgeMoviePosters>true</BadgeMoviePosters></PluginConfiguration>");

        var restored = (PluginConfiguration)serializer.Deserialize(reader)!;

        Assert.Equal(LogVerbosity.Warning, restored.LogVerbosity);
    }

    [Fact]
    public void UndefinedVerbosityFallsBackToWarningInTheGateMapping()
    {
        Assert.Equal(LogLevel.Warning, LogVerbosityGate.ToLogLevel((LogVerbosity)999));
    }

    [Theory]
    [InlineData(LogVerbosity.Off, false, false, false, false, false)]
    [InlineData(LogVerbosity.Error, true, false, false, false, false)]
    [InlineData(LogVerbosity.Warning, true, true, false, false, false)]
    [InlineData(LogVerbosity.Information, true, true, true, false, false)]
    [InlineData(LogVerbosity.Debug, true, true, true, true, false)]
    [InlineData(LogVerbosity.Trace, true, true, true, true, true)]
    public void GateEnablesAtOrAboveTheConfiguredLevel(
        LogVerbosity verbosity,
        bool error,
        bool warning,
        bool information,
        bool debug,
        bool trace)
    {
        var gate = CreateGate(verbosity);

        Assert.Equal(error, gate.IsEnabled(LogLevel.Error));
        Assert.Equal(error, gate.IsEnabled(LogLevel.Critical));
        Assert.Equal(warning, gate.IsEnabled(LogLevel.Warning));
        Assert.Equal(information, gate.IsEnabled(LogLevel.Information));
        Assert.Equal(debug, gate.IsEnabled(LogLevel.Debug));
        Assert.Equal(trace, gate.IsEnabled(LogLevel.Trace));
        Assert.False(gate.IsEnabled(LogLevel.None));
    }

    [Fact]
    public void GateAppliesAReplacedVerbosityWithoutARestart()
    {
        var service = new ConfigurationSnapshotService(new PluginConfiguration
        {
            LogVerbosity = LogVerbosity.Warning,
        });
        var gate = new LogVerbosityGate(service);

        Assert.Equal(LogLevel.Warning, gate.EffectiveLevel);
        Assert.False(gate.IsEnabled(LogLevel.Debug));

        Assert.True(service.TryReplace(new PluginConfiguration { LogVerbosity = LogVerbosity.Debug }, out var result));
        Assert.True(result.IsValid);

        Assert.Equal(LogLevel.Debug, gate.EffectiveLevel);
        Assert.True(gate.IsEnabled(LogLevel.Debug));
        Assert.True(gate.IsEnabled(LogLevel.Information));
    }

    [Fact]
    public void ChangingVerbosityDoesNotChangeTheRendererConfigurationFingerprint()
    {
        var warningSnapshot = PluginConfigurationSnapshot.From(new PluginConfiguration
        {
            LogVerbosity = LogVerbosity.Warning,
        });
        var traceSnapshot = PluginConfigurationSnapshot.From(new PluginConfiguration
        {
            LogVerbosity = LogVerbosity.Trace,
        });

        Assert.NotEqual(warningSnapshot.LogVerbosity, traceSnapshot.LogVerbosity);
        Assert.Equal(warningSnapshot.RendererConfigurationFingerprint, traceSnapshot.RendererConfigurationFingerprint);
    }

    [Fact]
    public void ChangingVerbosityDoesNotChangeTheProductionRegenerationDecision()
    {
        var warningSnapshot = PluginConfigurationSnapshot.From(new PluginConfiguration
        {
            LogVerbosity = LogVerbosity.Warning,
        });
        var traceSnapshot = PluginConfigurationSnapshot.From(new PluginConfiguration
        {
            LogVerbosity = LogVerbosity.Trace,
        });

        Assert.NotEqual(warningSnapshot.LogVerbosity, traceSnapshot.LogVerbosity);

        var identity = RenderTestFixtures.BuildMovieIdentity();
        var metadata = RenderTestFixtures.BuildMetadata();

        // The published fingerprint is produced by the real production planner
        // from the warning snapshot's snapshot-derived definitions, output
        // policy, and configuration fingerprint. If verbosity ever entered any of
        // those values (or the output fingerprint or RenderVersion), the trace
        // snapshot would compute a different desired fingerprint and the gate
        // would return Generate instead of Skip.
        var desiredWarning = ArtworkRegenerationPlanner.ComputeDesiredFingerprint(
            PublishedState(SourceA(), PlaceholderFingerprint),
            identity,
            metadata,
            warningSnapshot.BadgeDefinitions,
            warningSnapshot.RendererOutputPolicy,
            warningSnapshot.RendererConfigurationFingerprint,
            RenderVersion.CurrentRendererVersion,
            RenderVersion.CurrentBadgeSchemaVersion);

        var published = PublishedState(SourceA(), desiredWarning);

        var desiredTrace = ArtworkRegenerationPlanner.ComputeDesiredFingerprint(
            published,
            identity,
            metadata,
            traceSnapshot.BadgeDefinitions,
            traceSnapshot.RendererOutputPolicy,
            traceSnapshot.RendererConfigurationFingerprint,
            RenderVersion.CurrentRendererVersion,
            RenderVersion.CurrentBadgeSchemaVersion);

        var warningDecision = ArtworkRegenerationPlanner.Decide(
            published,
            identity,
            metadata,
            metadataUsable: true,
            warningSnapshot.BadgeDefinitions,
            warningSnapshot.RendererOutputPolicy,
            warningSnapshot.RendererConfigurationFingerprint,
            RenderVersion.CurrentRendererVersion,
            RenderVersion.CurrentBadgeSchemaVersion);

        var traceDecision = ArtworkRegenerationPlanner.Decide(
            published,
            identity,
            metadata,
            metadataUsable: true,
            traceSnapshot.BadgeDefinitions,
            traceSnapshot.RendererOutputPolicy,
            traceSnapshot.RendererConfigurationFingerprint,
            RenderVersion.CurrentRendererVersion,
            RenderVersion.CurrentBadgeSchemaVersion);

        // A verbosity-only snapshot change yields the same production fingerprint
        // and no artwork work, so it never republishes artwork.
        Assert.Equal(desiredWarning, desiredTrace);
        Assert.False(warningDecision.ShouldGenerate);
        Assert.Null(warningDecision.DesiredFingerprint);
        Assert.False(traceDecision.ShouldGenerate);
        Assert.Null(traceDecision.DesiredFingerprint);
    }

    [Fact]
    public void RegistratorResolvesTheGateFromTheCurrentSnapshot()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        new ArrTagsServiceRegistrator().RegisterServices(services, null!);

        using var provider = services.BuildServiceProvider();
        var gate = provider.GetRequiredService<ILogVerbosityGate>();

        Assert.Equal(LogLevel.Warning, gate.EffectiveLevel);
        Assert.False(gate.IsEnabled(LogLevel.Debug));
        Assert.True(gate.IsEnabled(LogLevel.Warning));

        var configuration = provider.GetRequiredService<ConfigurationSnapshotService>();
        Assert.True(configuration.TryReplace(new PluginConfiguration { LogVerbosity = LogVerbosity.Debug }, out _));

        Assert.True(gate.IsEnabled(LogLevel.Debug));
    }

    [Fact]
    public void RegistratorDoesNotReplaceTheHostLoggerFactoryOrRegisterAProvider()
    {
        var services = new ServiceCollection();
        using var hostFactory = new LoggerFactory();
        services.AddSingleton<ILoggerFactory>(hostFactory);

        new ArrTagsServiceRegistrator().RegisterServices(services, null!);

        // ADR-020 clause 3: no custom ILoggerProvider/sink is registered and the
        // host ILoggerFactory is never replaced.
        Assert.DoesNotContain(services, descriptor => descriptor.ServiceType == typeof(ILoggerProvider));

        using var provider = services.BuildServiceProvider();
        Assert.Same(hostFactory, provider.GetRequiredService<ILoggerFactory>());
    }

    [Fact]
    public void HostLoggingResolvesWithArrTagsCategoryPrefixes()
    {
        var capturing = new CapturingLoggerProvider();
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddProvider(capturing));
        new ArrTagsServiceRegistrator().RegisterServices(services, null!);

        using var provider = services.BuildServiceProvider();
        var logger = provider.GetRequiredService<ILogger<Plugin>>();

        Assert.NotNull(logger);
        Assert.Contains(typeof(Plugin).FullName!, capturing.Categories);
        Assert.StartsWith("ArrTags.", typeof(Plugin).FullName!, StringComparison.Ordinal);
    }

    [Fact]
    public void SettingsPageExposesTheBoundedVerbosityControl()
    {
        using var stream = typeof(Plugin).Assembly.GetManifestResourceStream(Plugin.SettingsPageResourceName);
        Assert.NotNull(stream);
        using var reader = new StreamReader(stream!);
        var page = reader.ReadToEnd();

        Assert.Contains("id=\"logVerbosity\"", page, StringComparison.Ordinal);
        Assert.Contains("config.LogVerbosity", page, StringComparison.Ordinal);
        Assert.Contains("emby-select", page, StringComparison.Ordinal);

        foreach (var name in Enum.GetNames<LogVerbosity>())
        {
            Assert.Contains("value=\"" + name + "\"", page, StringComparison.Ordinal);
        }
    }

    private static ILogVerbosityGate CreateGate(LogVerbosity verbosity)
    {
        return new LogVerbosityGate(new ConfigurationSnapshotService(new PluginConfiguration
        {
            LogVerbosity = verbosity,
        }));
    }

    private static PublishedArtworkState PublishedState(byte[] sourceBytes, string publishedFingerprint)
    {
        var sha = ArtworkHashes.ComputeSha256(sourceBytes);
        var capture = new ActiveImageIdentity(
            Surface,
            ArtworkImagePresence.Present,
            sha,
            sourceBytes.Length,
            100,
            150,
            At,
            "source-tag");
        var artifact = new SourceArtifactInfo(sha, "image/png", sourceBytes.Length, sha);
        var session = PublishedArtworkStateTransitions
            .CaptureSession(null, RenderTestFixtures.ItemId, Surface, capture, artifact, At)
            .State;
        var active = new ActiveImageIdentity(
            Surface,
            ArtworkImagePresence.Present,
            ArtworkHashes.ComputeSha256(Encoding.UTF8.GetBytes("active-derived")),
            Encoding.UTF8.GetBytes("active-derived").Length,
            100,
            150,
            At,
            "active-tag");

        return PublishedArtworkStateTransitions
            .CommitPublication(session, active, publishedFingerprint, RenderVersion.CurrentRendererVersion, At)
            .State;
    }

    private static byte[] SourceA()
    {
        return Encoding.UTF8.GetBytes("original-source-a");
    }

    private sealed class CapturingLoggerProvider : ILoggerProvider
    {
        public ConcurrentBag<string> Categories { get; } = new ConcurrentBag<string>();

        public ILogger CreateLogger(string categoryName)
        {
            Categories.Add(categoryName);
            return NullLogger.Instance;
        }

        public void Dispose()
        {
        }
    }
}
