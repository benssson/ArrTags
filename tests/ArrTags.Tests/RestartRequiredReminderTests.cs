using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using ArrTags.Configuration;
using Xunit;

namespace ArrTags.Tests;

/// <summary>
/// Phase 16 task 16.5 coverage for the best-effort restart-required modal
/// reminder (ADR-028 clause 4, G8). The repository deliberately has no
/// JavaScript runtime, so the modal cannot be executed here; these structural
/// tests over the embedded settings page prove the reminder function exists with
/// its fail-open guard and swallowing try/catch, that it is called exactly once
/// inside the successful <c>ApiClient.updatePluginConfiguration</c> callback
/// after the preserved <c>Dashboard.processPluginConfigurationUpdateResult</c>
/// call, that the change detection snapshots the persisted restart-required
/// values before mutating the fetched configuration and compares the submitted
/// values against that snapshot, and that the call is the plain-text
/// <c>window.Dashboard.alert</c> object form rather than the absent
/// <c>Dashboard.showMessage</c>, the toast form, or a two-button dialog. The
/// detection domain is derived from the code-owned
/// <see cref="RestartRequiredSettings"/> classification through the page mirror
/// those tests pin.
/// </summary>
public class RestartRequiredReminderTests
{
    private const string ReminderDeclaration = "var showRestartRequiredReminder = function";
    private const string ReminderCall = "showRestartRequiredReminder(";

    [Fact]
    public void TheReminderFunctionFailsOpenWhenTheModalApiIsMissingOrFails()
    {
        var page = ReadEmbeddedPage();
        var reminder = ReminderFunction(page);

        // Missing or false state fails open: nothing is shown.
        Assert.Contains("if (!restartRequiredChanged)", reminder, StringComparison.Ordinal);

        // The exact availability guard for the lazily resolved web-client shim.
        Assert.Contains(
            "!window.Dashboard || typeof window.Dashboard.alert !== 'function'",
            reminder,
            StringComparison.Ordinal);

        // A failing modal call is swallowed; no rethrow escapes the reminder.
        Assert.Contains("try {", reminder, StringComparison.Ordinal);
        var catchBlock = Regex.Match(reminder, @"catch \(\w+\) \{\s*(?<body>[^}]*)\}");
        Assert.True(catchBlock.Success, "The reminder call must be wrapped in a swallowing try/catch.");
        Assert.DoesNotContain("throw", catchBlock.Groups["body"].Value, StringComparison.Ordinal);

        // window.Dashboard is referenced only inside this function, so the
        // script's top-level execution (a standalone page load without the host
        // web client) never evaluates it.
        Assert.Equal(
            CountOccurrences(page, "window.Dashboard"),
            CountOccurrences(reminder, "window.Dashboard"));

        // No bare Dashboard.alert reference, which would raise a ReferenceError
        // instead of failing open.
        Assert.DoesNotMatch(@"(?<!window\.)Dashboard\.alert\(", reminder);
    }

    [Fact]
    public void TheReminderIsCalledOnlyFromTheSuccessfulUpdateCallbackAfterTheExistingResultHandler()
    {
        var page = ReadEmbeddedPage();

        // Exactly one call site: it is not in the load path (pageshow) and not
        // in any rejection path; a rejected POST shows no restart reminder.
        Assert.Single(Regex.Matches(page, Regex.Escape(ReminderCall)));

        const string marker =
            "ApiClient.updatePluginConfiguration(PluginConfig.pluginId, config).then(function (result) {";
        var start = page.IndexOf(marker, StringComparison.Ordinal);
        Assert.True(start >= 0, "The successful update callback must preserve its ApiClient.updatePluginConfiguration call.");
        var end = page.IndexOf("});", start, StringComparison.Ordinal);
        Assert.True(end > start, "The successful update callback must be terminated.");
        var callback = page[start..(end + 3)];

        // The existing SettingsSaved result handler is preserved and runs first.
        var resultIndex = callback.IndexOf(
            "Dashboard.processPluginConfigurationUpdateResult(result);",
            StringComparison.Ordinal);
        Assert.True(resultIndex >= 0, "The existing processPluginConfigurationUpdateResult call must be preserved.");

        var reminderIndex = callback.IndexOf(
            ReminderCall + "restartRequiredChanged);",
            StringComparison.Ordinal);
        Assert.True(
            reminderIndex > resultIndex,
            "The reminder must run after processPluginConfigurationUpdateResult inside the same success callback.");
        Assert.Single(Regex.Matches(callback, Regex.Escape(ReminderCall)));
    }

    [Fact]
    public void TheReminderDetectionDomainIsTheCodeOwnedRestartRequiredSet()
    {
        var page = ReadEmbeddedPage();

        // A single mirrored list is the detection domain; the detection code
        // below consumes it rather than repeating a second literal list.
        Assert.Single(Regex.Matches(page, "var restartRequiredFields ="));

        var pageEntries = Regex
            .Matches(page, @"\{ setting: '([A-Za-z0-9]+)', elementId: '([A-Za-z0-9]+)', mixed: (true|false) \}")
            .Select(match => (
                Setting: match.Groups[1].Value,
                ElementId: match.Groups[2].Value,
                Mixed: bool.Parse(match.Groups[3].Value)))
            .ToArray();
        var codeEntries = RestartRequiredSettings.RestartRequired
            .Select(entry => (
                Setting: entry.Name[(entry.Name.LastIndexOf('.') + 1)..],
                ElementId: Assert.Single(entry.PageElementIds),
                Mixed: entry.IsMixed))
            .ToArray();

        Assert.NotEmpty(codeEntries);
        Assert.Equal(codeEntries, pageEntries);
    }

    [Fact]
    public void TheChangeDetectionSnapshotsPersistedValuesBeforeMutationAndComparesBeforeSaving()
    {
        var page = ReadEmbeddedPage();
        var submit = SubmitHandlerRegion(page);

        // The snapshot reads the persisted values from the freshly fetched
        // config through the classification mirror.
        var snapshot = Regex.Match(
            submit,
            @"(?<snapshot>[A-Za-z_][A-Za-z0-9_]*)\[(?<entry>[A-Za-z_][A-Za-z0-9_]*)\.setting\] = config\.Limits\[\k<entry>\.setting\];");
        Assert.True(
            snapshot.Success,
            "The submit handler must snapshot the persisted restart-required values from the fetched config.");
        Assert.Contains(
            "var " + snapshot.Groups["entry"].Value + " = restartRequiredFields[",
            submit,
            StringComparison.Ordinal);

        // The comparison checks each submitted value against that snapshot and
        // is also driven by the classification mirror.
        var comparison = Regex.Match(
            submit,
            @"config\.Limits\[(?<entry>[A-Za-z_][A-Za-z0-9_]*)\.setting\] !== (?<snapshot>[A-Za-z_][A-Za-z0-9_]*)\[\k<entry>\.setting\]");
        Assert.True(
            comparison.Success,
            "The submit handler must compare each submitted restart-required value against the snapshot.");
        Assert.Contains(
            "var " + comparison.Groups["entry"].Value + " = restartRequiredFields[",
            submit,
            StringComparison.Ordinal);

        // Ordering: snapshot first, then the handler's writes, then the
        // comparison, then the update request.
        var snapshotIndex = submit.IndexOf(snapshot.Value, StringComparison.Ordinal);
        var firstMutationIndex = submit.IndexOf("config.Sonarr.Enabled =", StringComparison.Ordinal);
        var comparisonIndex = submit.IndexOf(comparison.Value, StringComparison.Ordinal);
        var updateIndex = submit.IndexOf("ApiClient.updatePluginConfiguration", StringComparison.Ordinal);

        Assert.True(firstMutationIndex > snapshotIndex, "The snapshot must be taken before the first mutation of the fetched config.");
        Assert.True(comparisonIndex > snapshotIndex, "The comparison must run after the snapshot.");
        Assert.True(comparisonIndex < updateIndex, "The comparison must run before the update request.");
        Assert.Contains("var restartRequiredChanged = false;", submit, StringComparison.Ordinal);
    }

    [Fact]
    public void TheReminderUsesThePlainTextSingleButtonAlertNotTheToastOrMissingApis()
    {
        var page = ReadEmbeddedPage();
        var reminder = ReminderFunction(page);

        var options = Regex.Match(reminder, @"window\.Dashboard\.alert\(\{(?<body>[^}]*)\}\)");
        Assert.True(options.Success, "The reminder must call window.Dashboard.alert with an options object.");
        var body = options.Groups["body"].Value;

        Assert.Contains("title: 'Jellyfin restart required'", body, StringComparison.Ordinal);
        Assert.Contains("message: '", body, StringComparison.Ordinal);
        Assert.Contains("Jellyfin server restart is required", body, StringComparison.Ordinal);

        // The body key is `message`, not `text` (which renders an empty
        // dialog), and the body is plain text rather than markup.
        Assert.DoesNotContain("text:", body, StringComparison.Ordinal);
        Assert.DoesNotContain("<", body, StringComparison.Ordinal);

        // The toast string form, the absent shim method, the two-button dialog,
        // and the raw dialog helper are not used.
        Assert.DoesNotContain("Dashboard.alert('", page, StringComparison.Ordinal);
        Assert.DoesNotContain("showMessage", page, StringComparison.Ordinal);
        Assert.DoesNotContain("Dashboard.confirm", page, StringComparison.Ordinal);
        Assert.DoesNotContain("dialogHelper", page, StringComparison.Ordinal);
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
        var start = page.IndexOf(ReminderDeclaration, StringComparison.Ordinal);
        Assert.True(start >= 0, "The settings page must declare showRestartRequiredReminder.");
        var end = page.IndexOf("};", start, StringComparison.Ordinal);
        Assert.True(end > start, "The reminder function must be terminated.");
        return page[start..(end + 2)];
    }

    private static int CountOccurrences(string text, string value)
    {
        var count = 0;
        var index = text.IndexOf(value, StringComparison.Ordinal);
        while (index >= 0)
        {
            count++;
            index = text.IndexOf(value, index + value.Length, StringComparison.Ordinal);
        }

        return count;
    }

    private static string ReadEmbeddedPage()
    {
        using var stream = typeof(Plugin).Assembly.GetManifestResourceStream(Plugin.SettingsPageResourceName);
        Assert.NotNull(stream);
        using var reader = new StreamReader(stream!);
        return reader.ReadToEnd();
    }
}
