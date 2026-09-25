using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using ArrTags.Artwork;
using ArrTags.Configuration;
using ArrTags.State;
using Xunit;

namespace ArrTags.Tests;

/// <summary>
/// Phase 16 task 16.2 coverage for the authoritative, code-evidenced
/// restart-required set (ADR-028 clause 2, G8). These tests make the
/// classification testable: the restart-required and mixed sets are pinned to
/// their evidenced membership, the binary rule is enforced structurally, every
/// writable property of the configuration model types is classified (no silent
/// member), every restart-required setting names its real singleton-construction
/// capture site, and the settings-page mirror matches the code-owned set. A
/// flipped scope, a flipped mixed flag, a new unclassified setting, a changed
/// capture site, or a missing page element fails these tests.
/// </summary>
public class RestartRequiredSettingsTests
{
    /// <summary>
    /// The exact authoritative restart-required set (ADR-028 clause 2; task 16.2
    /// is the single owner of the complete set). Order is the published set order
    /// mirrored by the settings page.
    /// </summary>
    private static readonly string[] ExpectedRestartRequired =
    {
        "OperationalLimits.SourceArtifactLimitBytes",
        "OperationalLimits.DerivedArtifactLimitBytes",
        "OperationalLimits.MaxImageDimensionPixels",
        "OperationalLimits.RenderCacheTtlMinutes",
        "OperationalLimits.RenderCacheQuotaBytes",
        "OperationalLimits.ArtifactStorageQuotaBytes",
        "OperationalLimits.TerminalProvenanceRetentionDays",
    };

    /// <summary>
    /// The mixed settings: a construction-captured consumer and a per-operation
    /// consumer both exist, so the setting is restart-required and its note text
    /// states that some paths apply the change immediately (ADR-028 clause 2).
    /// </summary>
    private static readonly string[] ExpectedMixed =
    {
        "OperationalLimits.SourceArtifactLimitBytes",
        "OperationalLimits.DerivedArtifactLimitBytes",
        "OperationalLimits.MaxImageDimensionPixels",
    };

    /// <summary>
    /// The real singleton-construction capture types each restart-required
    /// setting's evidence must name. The behavioral probes in
    /// <see cref="RestartRequiredConsumerEvidenceTests"/> prove these sites
    /// actually capture the value at construction.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, Type[]> ExpectedConstructionCaptureTypes =
        new Dictionary<string, Type[]>(StringComparer.Ordinal)
        {
            ["OperationalLimits.SourceArtifactLimitBytes"] = new[]
            {
                typeof(SourceArtifactStore),
                typeof(JellyfinArtworkImageAccess),
                typeof(ArtworkSourceReader),
            },
            ["OperationalLimits.DerivedArtifactLimitBytes"] = new[] { typeof(ArtworkPublisher) },
            ["OperationalLimits.MaxImageDimensionPixels"] = new[] { typeof(ArtworkSourceReader) },
            ["OperationalLimits.RenderCacheTtlMinutes"] = new[] { typeof(StateRepository) },
            ["OperationalLimits.RenderCacheQuotaBytes"] = new[] { typeof(StateRepository) },
            ["OperationalLimits.ArtifactStorageQuotaBytes"] = new[] { typeof(SourceArtifactStore) },
            ["OperationalLimits.TerminalProvenanceRetentionDays"] = new[] { typeof(StateRepository) },
        };

    [Fact]
    public void EveryEntryIsUniqueAndSatisfiesTheBinaryRule()
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in RestartRequiredSettings.All)
        {
            Assert.True(seen.Add(entry.Name), $"Duplicate classified setting name '{entry.Name}'.");

            if (entry.IsMixed)
            {
                Assert.Equal(SettingApplicationScope.RestartRequired, entry.Scope);
                Assert.NotEmpty(entry.ConstructionCapturedEvidence);
                Assert.NotEmpty(entry.PerOperationEvidence);
            }

            if (entry.Scope == SettingApplicationScope.PerOperation)
            {
                Assert.Empty(entry.ConstructionCapturedEvidence);
            }
            else
            {
                Assert.True(
                    entry.ConstructionCapturedEvidence.Count > 0 || !string.IsNullOrWhiteSpace(entry.Note),
                    $"Restart-required setting '{entry.Name}' carries no construction evidence and no audited note.");
            }
        }

        Assert.NotEmpty(RestartRequiredSettings.All);
    }

    [Fact]
    public void TheRestartRequiredSetIsExactlyTheEvidencedConstructionCapturedSettings()
    {
        var actual = RestartRequiredSettings.RestartRequired.Select(entry => entry.Name).ToArray();

        Assert.Equal(ExpectedRestartRequired, actual);
        Assert.Equal(RestartRequiredSettings.All.Count(entry => entry.RestartRequired), actual.Length);
    }

    [Fact]
    public void TheMixedSetIsExactlyTheEvidencedDualConsumerSettings()
    {
        var actual = RestartRequiredSettings.All.Where(entry => entry.IsMixed).Select(entry => entry.Name).ToArray();

        Assert.Equal(ExpectedMixed, actual);

        // ADR-028 clause 2: every mixed setting is restart-required.
        foreach (var mixed in actual)
        {
            Assert.True(RestartRequiredSettings.IsRestartRequired(mixed), $"Mixed setting '{mixed}' must be restart-required.");
        }
    }

    [Fact]
    public void EveryRestartRequiredEntryNamesItsRealConstructionCaptureSite()
    {
        var classifiedCaptureNames = RestartRequiredSettings.RestartRequired
            .Select(entry => entry.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();
        var expectedCaptureNames = ExpectedConstructionCaptureTypes.Keys
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(expectedCaptureNames, classifiedCaptureNames);

        foreach (var (name, captureTypes) in ExpectedConstructionCaptureTypes)
        {
            var entry = RestartRequiredSettings.Get(name);
            var evidence = string.Join(" ", entry.ConstructionCapturedEvidence);

            Assert.NotEmpty(entry.ConstructionCapturedEvidence);
            foreach (var captureType in captureTypes)
            {
                Assert.Contains(captureType.Name, evidence, StringComparison.Ordinal);
            }
        }
    }

    [Fact]
    public void LookupAgreesWithTheSetAndRejectsUnknownNames()
    {
        foreach (var entry in RestartRequiredSettings.All)
        {
            Assert.Same(entry, RestartRequiredSettings.Get(entry.Name));
            Assert.Equal(entry.RestartRequired, RestartRequiredSettings.IsRestartRequired(entry.Name));
        }

        Assert.Throws<ArgumentNullException>(() => RestartRequiredSettings.Get(null!));
        Assert.Throws<ArgumentException>(() => RestartRequiredSettings.Get("OperationalLimits.NotARealLimit"));
    }

    [Fact]
    public void OnlyTheAuditedInertSettingHasNoRuntimeConsumer()
    {
        var withoutConsumer = RestartRequiredSettings.All.Where(entry => !entry.HasRuntimeConsumer).ToList();

        var entry = Assert.Single(withoutConsumer);
        Assert.Equal("OperationalLimits.RequestTimeoutSeconds", entry.Name);
        Assert.Equal(SettingApplicationScope.PerOperation, entry.Scope);
        Assert.False(entry.RestartRequired);
        Assert.False(string.IsNullOrWhiteSpace(entry.Note));
        Assert.Contains("ArrConnectionConfiguration.RequestTimeoutSeconds", entry.Note, StringComparison.Ordinal);
    }

    [Fact]
    public void EveryUserAdjustableConfigurationPropertyIsClassified()
    {
        var classified = new HashSet<string>(
            RestartRequiredSettings.All.Select(entry => entry.Name),
            StringComparer.Ordinal);
        var expected = ExpectedClassifiedNames();

        var missing = expected.Where(name => !classified.Contains(name)).OrderBy(name => name, StringComparer.Ordinal).ToArray();
        var unknown = classified.Where(name => !expected.Contains(name)).OrderBy(name => name, StringComparer.Ordinal).ToArray();

        Assert.Empty(missing);
        Assert.Empty(unknown);
    }

    [Fact]
    public void EveryRestartRequiredSettingHasAStaticSettingsPageElement()
    {
        var page = ReadEmbeddedPage();

        foreach (var entry in RestartRequiredSettings.RestartRequired)
        {
            var id = Assert.Single(entry.PageElementIds);
            Assert.Contains($"id=\"{id}\"", page, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void ThePageRestartRequiredMirrorMatchesTheCodeOwnedClassification()
    {
        var page = ReadEmbeddedPage();
        var block = ExtractMirrorBlock(page);

        var matches = Regex.Matches(
            block,
            @"\{ setting: '([A-Za-z0-9]+)', elementId: '([A-Za-z0-9]+)', mixed: (true|false) \}");
        var pageEntries = matches
            .Select(match => (
                Property: match.Groups[1].Value,
                Id: match.Groups[2].Value,
                Mixed: bool.Parse(match.Groups[3].Value)))
            .ToArray();

        var codeEntries = RestartRequiredSettings.RestartRequired
            .Select(entry => (
                Property: entry.Name[(entry.Name.LastIndexOf('.') + 1)..],
                Id: Assert.Single(entry.PageElementIds),
                Mixed: entry.IsMixed))
            .ToArray();

        Assert.Equal(codeEntries.Length, pageEntries.Length);
        Assert.Equal(codeEntries, pageEntries);
    }

    [Fact]
    public void TheClassificationRejectsAnInconsistentEntry()
    {
        // ADR-028 clause 2: a mixed setting must be restart-required.
        Assert.Throws<ArgumentException>(() => new SettingClassification(
            "Test.Mixed",
            SettingApplicationScope.PerOperation,
            isMixed: true,
            new[] { "testMixed" },
            Array.Empty<string>(),
            new[] { "per-operation consumer" }));

        // ADR-028 clause 2: a per-operation setting must have no
        // singleton-construction-captured consumer.
        Assert.Throws<ArgumentException>(() => new SettingClassification(
            "Test.PerOperation",
            SettingApplicationScope.PerOperation,
            isMixed: false,
            new[] { "testPerOperation" },
            new[] { "construction-captured consumer" },
            new[] { "per-operation consumer" }));

        // A restart-required setting needs construction-capture evidence or an
        // audited no-runtime-consumer note.
        Assert.Throws<ArgumentException>(() => new SettingClassification(
            "Test.RestartRequired",
            SettingApplicationScope.RestartRequired,
            isMixed: false,
            new[] { "testRestartRequired" },
            Array.Empty<string>(),
            Array.Empty<string>()));
    }

    private static string ExtractMirrorBlock(string page)
    {
        const string marker = "var restartRequiredFields = [";
        var start = page.IndexOf(marker, StringComparison.Ordinal);
        Assert.True(start >= 0, "The settings page must declare the restartRequiredFields mirror.");

        var end = page.IndexOf("];", start + marker.Length, StringComparison.Ordinal);
        Assert.True(end > start, "The restartRequiredFields mirror must be a terminated array literal.");

        return page[start..end];
    }

    private static HashSet<string> ExpectedClassifiedNames()
    {
        var names = new HashSet<string>(StringComparer.Ordinal);

        // Every model type is reflected with the same writable-leaf filter,
        // including PluginConfiguration: its container properties (Sonarr,
        // Radarr, Limits, Renderer) are not leaf types and the inherited
        // BasePluginConfiguration exposes no writable members, so this yields
        // exactly the leaf settings that must be classified. A new writable
        // leaf property on any of the five types fails
        // EveryUserAdjustableConfigurationPropertyIsClassified.
        foreach (var property in WritableLeafProperties(typeof(PluginConfiguration)))
        {
            names.Add("PluginConfiguration." + property.Name);
        }

        foreach (var property in WritableLeafProperties(typeof(ArrConnectionConfiguration)))
        {
            names.Add("ArrConnectionConfiguration." + property.Name);
        }

        foreach (var property in WritableLeafProperties(typeof(OperationalLimits)))
        {
            names.Add("OperationalLimits." + property.Name);
        }

        foreach (var property in WritableLeafProperties(typeof(RendererConfiguration)))
        {
            names.Add("RendererConfiguration." + property.Name);
        }

        foreach (var property in WritableLeafProperties(typeof(BadgeSelectorConfiguration)))
        {
            // The Selector enum names a fixed code-owned selector; the page
            // presents it as the row identity and it is not user-adjustable.
            if (property.Name == nameof(BadgeSelectorConfiguration.Selector))
            {
                continue;
            }

            names.Add("BadgeSelectorConfiguration." + property.Name);
        }

        return names;
    }

    private static IEnumerable<PropertyInfo> WritableLeafProperties(Type type)
    {
        return type
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(property => property.GetIndexParameters().Length == 0
                && property.SetMethod?.IsPublic == true
                && IsClassifiedLeaf(property.PropertyType));
    }

    private static bool IsClassifiedLeaf(Type type)
    {
        return type.IsPrimitive
            || type.IsEnum
            || type == typeof(string)
            || type == typeof(Collection<string>);
    }

    private static string ReadEmbeddedPage()
    {
        using var stream = typeof(Plugin).Assembly.GetManifestResourceStream(Plugin.SettingsPageResourceName);
        Assert.NotNull(stream);
        using var reader = new StreamReader(stream!);
        return reader.ReadToEnd();
    }
}
