using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using ArrTags.Configuration;
using ArrTags.PluginLifecycle;
using Jellyfin.Extensions.Json;
using MediaBrowser.Common.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ArrTags.Tests;

/// <summary>
/// Phase 16 task 16.6 G6/G8 integration verification. Unlike the per-aspect task
/// 16.1-16.5 tests, this class composes the shipped ends of the phase without a
/// live host:
/// <list type="bullet">
/// <item>the byte-limit display values an operator saves travel through the real
/// dashboard POST JSON deserialization (<c>JsonDefaults.Options</c>), the real
/// <see cref="Plugin.UpdateConfiguration"/> validate/persist/activate path, and
/// the real XML configuration boundary, and display back as the exact entered
/// value at every minimum, default, and maximum;</item>
/// <item>the code-owned <see cref="RestartRequiredSettings"/> classification is
/// composed with the settings page's byte mapping, restart mirror, always-present
/// note text, and reminder detection domain, so all of those surfaces agree.</item>
/// </list>
/// The page's inline JavaScript (the display/store conversion, the note text
/// wiring, and the modal call) cannot be executed here: the repository
/// deliberately has no JavaScript runtime and the pinned live host runs with
/// <c>--nowebclient</c>. Those surfaces are verified structurally against the
/// code-owned sources and the embedded page resource; the byte conversion itself
/// is exercised as real C# through <see cref="ByteLimitUnits"/>.
/// </summary>
public sealed class GoalG6G8IntegrationTests
{
    private const string RestartNoteText =
        "A Jellyfin server restart is required for a change to this setting to take effect.";

    private const string MixedNoteText =
        RestartNoteText + " Some paths apply the change immediately.";

    [Fact]
    public void EditedByteLimitsRoundTripThroughTheRealDashboardSaveValidationAndPersistencePath()
    {
        // The displayed values an operator would save: step multiples chosen away
        // from the defaults, so the round-trip proves the values travel through
        // the whole path instead of being replaced by defaults.
        var edits = new (string Property, string Display)[]
        {
            ("ProviderResponseLimitBytes", "0.125"),
            ("WebhookMaxPayloadBytes", "8"),
            ("SourceArtifactLimitBytes", "0.25"),
            ("DerivedArtifactLimitBytes", "1"),
            ("RenderCacheQuotaBytes", "128"),
            ("ArtifactStorageQuotaBytes", "512"),
            ("InventoryCacheMaxBytes", "2"),
        };

        var expected = new Dictionary<string, long>(StringComparer.Ordinal);
        foreach (var (property, display) in edits)
        {
            var field = ByteLimitUnits.Get(property);
            Assert.True(
                field.TryParseDisplay(display, out var bytes),
                $"'{display}' must be a valid {field.UnitLabel} value for {property}.");
            Assert.NotEqual(field.DefaultBytes, bytes);
            expected[property] = bytes;
        }

        using var harness = new G6G8SaveHarness();

        // The elevation-gated PluginsController POST deserializes the request body
        // with these exact pinned options; the page sends the converted byte
        // values as JSON numbers.
        var candidate = JsonSerializer.Deserialize<PluginConfiguration>(
            ByteLimitsPayload(expected),
            JsonDefaults.Options);
        Assert.NotNull(candidate);
        AssertLimits(expected, candidate!.Limits);

        // The real Plugin.UpdateConfiguration override validates before it
        // persists, then activates the running snapshot.
        harness.PluginInstance.UpdateConfiguration(candidate);
        Assert.NotNull(harness.PluginInstance.LastConfigurationValidationResult);
        Assert.True(harness.PluginInstance.LastConfigurationValidationResult!.IsValid);
        AssertLimits(expected, harness.Snapshot.Current.Limits);

        // The persisted XML keeps the exact byte values.
        var persisted = harness.ReadPersisted();
        AssertLimits(expected, persisted.Limits);

        // Display from store: the persisted bytes render as exactly the value the
        // operator entered, and store -> display -> store is the identity, so a
        // re-save cannot move the persisted value.
        foreach (var (property, display) in edits)
        {
            var field = ByteLimitUnits.Get(property);
            Assert.Equal(display, field.FormatDisplay(ReadLimit(persisted.Limits, property)));
            Assert.True(field.TryParseDisplay(display, out var reparsed));
            Assert.Equal(expected[property], reparsed);
        }
    }

    [Fact]
    public void EveryByteLimitMinimumDefaultAndMaximumRoundTripsThroughTheRealDashboardSavePath()
    {
        using var harness = new G6G8SaveHarness();

        foreach (var field in ByteLimitUnits.All)
        {
            var bounds = new (string Name, long Bytes)[]
            {
                ("minimum", field.MinimumBytes),
                ("default", field.DefaultBytes),
                ("maximum", field.MaximumBytes),
            };

            foreach (var (name, bound) in bounds)
            {
                var candidate = JsonSerializer.Deserialize<PluginConfiguration>(
                    ByteLimitsPayload(new Dictionary<string, long>(StringComparer.Ordinal)
                    {
                        [field.PropertyName] = bound,
                    }),
                    JsonDefaults.Options);
                Assert.NotNull(candidate);

                harness.PluginInstance.UpdateConfiguration(candidate!);

                Assert.True(
                    harness.PluginInstance.LastConfigurationValidationResult!.IsValid,
                    $"{field.PropertyName} at its {name} must validate.");
                Assert.Equal(bound, ReadLimit(harness.Snapshot.Current.Limits, field.PropertyName));
                Assert.Equal(bound, ReadLimit(harness.ReadPersisted().Limits, field.PropertyName));

                // The bound displays exactly at the field's fixed precision and
                // converts back to the same byte value (the three 64 KiB minima
                // display as the 0.0625 MB fraction).
                var display = field.FormatDisplay(bound);
                Assert.True(field.TryParseDisplay(display, out var reparsed));
                Assert.Equal(bound, reparsed);
            }
        }
    }

    [Fact]
    public void RestartRequiredClassificationComposesThePageMirrorNotesAndReminderDetectionDomain()
    {
        var page = ReadEmbeddedPage();
        var restartRequired = RestartRequiredSettings.RestartRequired;
        Assert.NotEmpty(restartRequired);

        // Classification -> static note text: the notes are derived from the
        // code-owned set, exactly one per restart-required setting, and they sit
        // before the script so they are always present in the served page.
        var staticMarkup = BeforePageScript(page);
        var noteIds = Regex
            .Matches(staticMarkup, "id=\"([A-Za-z0-9]+)RestartNote\"")
            .Select(match => match.Groups[1].Value)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToArray();
        var expectedIds = restartRequired
            .Select(entry => Assert.Single(entry.PageElementIds))
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(expectedIds, noteIds);

        foreach (var entry in restartRequired)
        {
            var id = Assert.Single(entry.PageElementIds);
            Assert.Contains(NoteMarkup(id, entry.IsMixed), page, StringComparison.Ordinal);
        }

        // Classification -> page restartRequiredFields mirror: exact equality, so
        // a classification change without the page mirror (or the reverse) fails.
        var expectedMirror = restartRequired
            .Select(entry => new
            {
                Setting = entry.Name[(entry.Name.LastIndexOf('.') + 1)..],
                ElementId = Assert.Single(entry.PageElementIds),
                entry.IsMixed,
            })
            .ToArray();
        var actualMirror = Regex
            .Matches(page, @"\{ setting: '([A-Za-z0-9]+)', elementId: '([A-Za-z0-9]+)', mixed: (true|false) \}")
            .Select(match => new
            {
                Setting = match.Groups[1].Value,
                ElementId = match.Groups[2].Value,
                IsMixed = bool.Parse(match.Groups[3].Value),
            })
            .ToArray();
        Assert.Equal(expectedMirror, actualMirror);

        // Classification -> reminder condition: the submit handler snapshots and
        // compares exactly the mirror's settings from the freshly fetched
        // configuration, so the always-present notes and the modal describe the
        // same domain.
        var submit = SubmitHandlerRegion(page);
        Assert.Contains(
            "previousRestartRequiredLimits[restartField.setting] = config.Limits[restartField.setting];",
            submit,
            StringComparison.Ordinal);
        Assert.Contains(
            "config.Limits[restartCheckField.setting] !== previousRestartRequiredLimits[restartCheckField.setting]",
            submit,
            StringComparison.Ordinal);
        Assert.Contains("if (!restartRequiredChanged)", ReminderFunction(page), StringComparison.Ordinal);

        // The modal message states the restart requirement and defers the mixed
        // immediate-application nuance to the per-setting note text, so the modal
        // never contradicts the classification-derived notes.
        var reminder = ReminderFunction(page);
        Assert.Contains("Jellyfin server restart is required", reminder, StringComparison.Ordinal);
        Assert.Contains(
            "the note text next to the setting states whether some paths apply the change immediately",
            reminder,
            StringComparison.Ordinal);
    }

    [Fact]
    public void RestartRequiredByteLimitsShareOneIdentityAcrossTheMappingAndTheReminderCondition()
    {
        var page = ReadEmbeddedPage();

        // The G6/G8 intersection: the restart-required settings that are also
        // byte-denominated limits must use the same element id and JSON property
        // key everywhere the two features meet.
        var restartByteLimits = RestartRequiredSettings.RestartRequired
            .Select(entry => (
                Property: entry.Name[(entry.Name.LastIndexOf('.') + 1)..],
                ElementId: Assert.Single(entry.PageElementIds)))
            .Where(entry => ByteLimitUnits.All.Any(field => field.PropertyName == entry.Property))
            .ToArray();

        Assert.Equal(
            new[]
            {
                "ArtifactStorageQuotaBytes",
                "DerivedArtifactLimitBytes",
                "RenderCacheQuotaBytes",
                "SourceArtifactLimitBytes",
            },
            restartByteLimits
                .Select(entry => entry.Property)
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToArray());

        foreach (var (property, elementId) in restartByteLimits)
        {
            var field = ByteLimitUnits.Get(property);
            Assert.Equal(field.PageElementId, elementId);

            // The note is attached to the same native input the byte mapping
            // declares and converts.
            var inputTag = Regex.Match(page, "<input[^>]*id=\"" + Regex.Escape(elementId) + "\"[^>]*>");
            Assert.True(inputTag.Success, $"Expected the settings-page input for {property}.");
            Assert.Contains(
                "step=\"" + field.FormatDisplay(field.StepBytes) + "\"",
                inputTag.Value,
                StringComparison.Ordinal);

            // The note attached to that input is the classification-derived note
            // (mixed where the classification says some paths apply immediately).
            var classification = RestartRequiredSettings.Get("OperationalLimits." + property);
            Assert.Contains(NoteMarkup(elementId, classification.IsMixed), page, StringComparison.Ordinal);

            // The submit handler writes the byte-converted value into the JSON
            // property key the reminder then compares, so the G6 conversion feeds
            // the G8 reminder condition.
            Assert.Contains("values[definition.property] = bytes;", page, StringComparison.Ordinal);
            Assert.Contains(
                "config.Limits[definition.property] = byteLimitValues[definition.property];",
                page,
                StringComparison.Ordinal);
            Assert.Contains("config.Limits[restartCheckField.setting]", page, StringComparison.Ordinal);

            // Re-saving the displayed persisted value converts back to the exact
            // same bytes at every bound (including the 64 KiB minima), so the
            // reminder cannot fire spuriously; a real operator edit to another
            // step multiple converts to a different number and is detected.
            foreach (var bound in new[] { field.MinimumBytes, field.DefaultBytes, field.MaximumBytes })
            {
                var display = field.FormatDisplay(bound);
                Assert.True(field.TryParseDisplay(display, out var reparsed));
                Assert.Equal(bound, reparsed);
            }

            var changedBytes = field.DefaultBytes + field.StepBytes <= field.MaximumBytes
                ? field.DefaultBytes + field.StepBytes
                : field.DefaultBytes - field.StepBytes;
            Assert.True(field.TryParseDisplay(field.FormatDisplay(changedBytes), out var changed));
            Assert.NotEqual(field.DefaultBytes, changed);
        }
    }

    private static long ReadLimit(OperationalLimits limits, string propertyName)
    {
        var property = typeof(OperationalLimits).GetProperty(
            propertyName,
            BindingFlags.Public | BindingFlags.Instance);
        Assert.NotNull(property);
        return (long)property!.GetValue(limits)!;
    }

    private static void AssertLimits(IReadOnlyDictionary<string, long> expected, OperationalLimits limits)
    {
        Assert.Equal(ByteLimitUnits.All.Count, expected.Count);
        foreach (var (property, bytes) in expected)
        {
            Assert.Equal(bytes, ReadLimit(limits, property));
        }
    }

    private static string ByteLimitsPayload(IReadOnlyDictionary<string, long> limits)
    {
        var entries = string.Join(
            ", ",
            limits.Select(pair => $"\"{pair.Key}\": {pair.Value.ToString(CultureInfo.InvariantCulture)}"));
        return "{\"Limits\": {" + entries + "}}";
    }

    private static string BeforePageScript(string page)
    {
        var scriptStart = page.IndexOf("<script", StringComparison.Ordinal);
        Assert.True(scriptStart > 0, "The settings page must declare its script block.");
        return page[..scriptStart];
    }

    private static string SubmitHandlerRegion(string page)
    {
        var start = page.IndexOf("addEventListener('submit'", StringComparison.Ordinal);
        Assert.True(start >= 0, "The settings page must declare its submit handler.");
        var end = page.IndexOf("</script>", start, StringComparison.Ordinal);
        Assert.True(end > start, "The submit handler must stay inside the page script.");
        return page[start..end];
    }

    private static string ReminderFunction(string page)
    {
        var declaration = "var showRestartRequiredReminder = function";
        var start = page.IndexOf(declaration, StringComparison.Ordinal);
        Assert.True(start >= 0, "The settings page must declare showRestartRequiredReminder.");
        var end = page.IndexOf("};", start, StringComparison.Ordinal);
        Assert.True(end > start, "The reminder function must be terminated.");
        return page[start..(end + 2)];
    }

    private static string NoteMarkup(string elementId, bool mixed)
    {
        return $"<div class=\"fieldDescription restartRequiredNote\" id=\"{elementId}RestartNote\">{NoteText(mixed)}</div>";
    }

    private static string NoteText(bool mixed) => mixed ? MixedNoteText : RestartNoteText;

    private static string ReadEmbeddedPage()
    {
        using var stream = typeof(Plugin).Assembly.GetManifestResourceStream(Plugin.SettingsPageResourceName);
        Assert.NotNull(stream);
        using var reader = new StreamReader(stream!);
        return reader.ReadToEnd();
    }

    /// <summary>
    /// The G6/G8 save-path harness. It composes the real
    /// <see cref="Plugin.UpdateConfiguration"/> override over a real
    /// <see cref="ConfigurationSnapshotService"/> and a real
    /// <see cref="ConfigurationActivationTests.TestXmlSerializer"/> file, so the
    /// byte limits travel through the same validate/persist/activate path the
    /// dashboard save uses, without a live host.
    /// </summary>
    private sealed class G6G8SaveHarness : IDisposable
    {
        private readonly string _root;

        public G6G8SaveHarness()
        {
            _root = Path.Combine(Path.GetTempPath(), "arrtags-g6g8-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(_root, "plugins"));
            Directory.CreateDirectory(Path.Combine(_root, "configurations"));

            Paths = CreateApplicationPaths(_root);
            Serializer = new ConfigurationActivationTests.TestXmlSerializer();
            var initial = new PluginConfiguration();
            Serializer.SerializeToFile(initial, ConfigurationFilePath);

            Snapshot = new ConfigurationSnapshotService(initial);
            var (activityManager, _) = RecordingActivityManager.Create();

            var services = new ServiceCollection();
            services.AddSingleton(Snapshot);
            services.AddSingleton(activityManager);
            services.AddSingleton<IConfigurationRejectionNotifier, JellyfinConfigurationRejectionNotifier>();
            Provider = services.BuildServiceProvider();
            PluginInstance = new Plugin(Paths, Serializer, Provider);
        }

        public IApplicationPaths Paths { get; }

        public ConfigurationActivationTests.TestXmlSerializer Serializer { get; }

        public ConfigurationSnapshotService Snapshot { get; }

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
            catch (UnauthorizedAccessException)
            {
                // Best-effort test cleanup.
            }
        }

        private static IApplicationPaths CreateApplicationPaths(string root)
        {
            var paths = DispatchProxy.Create<IApplicationPaths, ConfigurationActivationTests.TestApplicationPaths>();
            ((ConfigurationActivationTests.TestApplicationPaths)(object)paths).RootPath = root;
            return paths;
        }
    }
}
