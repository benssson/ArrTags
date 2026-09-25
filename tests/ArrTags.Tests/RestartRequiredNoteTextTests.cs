using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using ArrTags.Configuration;
using Xunit;

namespace ArrTags.Tests;

/// <summary>
/// Phase 16 task 16.3 coverage for ADR-028 clause 3 (G8): every restart-required
/// setting in the settings page carries always-present note text stating that a
/// Jellyfin server restart is required for a change to take effect, and a mixed
/// setting's note also states that some paths apply the change immediately. The
/// expected element ids and the mixed subset are derived from the code-owned
/// <see cref="RestartRequiredSettings"/> classification, and the exact note
/// strings and their element association are pinned, so a missing, extra,
/// changed, or mislabelled note fails. The note is static markup placed before
/// the page script, so the contract holds without any JavaScript execution.
/// </summary>
public class RestartRequiredNoteTextTests
{
    /// <summary>
    /// The restart-only note text (ADR-028 clause 3), pinned exactly.
    /// </summary>
    private const string RestartNoteText =
        "A Jellyfin server restart is required for a change to this setting to take effect.";

    /// <summary>
    /// The mixed note text (ADR-028 clause 3): the restart statement plus the
    /// immediate-application clause, pinned exactly.
    /// </summary>
    private const string MixedNoteText =
        RestartNoteText + " Some paths apply the change immediately.";

    private const string ImmediateApplicationClause = "Some paths apply the change immediately.";
    private const string NoteClass = "restartRequiredNote";

    [Fact]
    public void TheStaticPageCarriesExactlyTheRestartRequiredNotes()
    {
        var page = ReadEmbeddedPage();
        var staticMarkup = BeforePageScript(page);

        var noteIds = Regex
            .Matches(staticMarkup, "id=\"([A-Za-z0-9]+)RestartNote\"")
            .Select(match => match.Groups[1].Value)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToArray();
        var expectedIds = RestartRequiredSettings.RestartRequired
            .Select(entry => Assert.Single(entry.PageElementIds))
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToArray();

        // Exact set equality: a restart-required setting whose note is removed,
        // an extra note, and a duplicate note all fail.
        Assert.Equal(expectedIds, noteIds);
    }

    [Fact]
    public void EveryRestartRequiredSettingCarriesItsExactNoteNextToItsElement()
    {
        var page = ReadEmbeddedPage();
        var scriptStart = page.IndexOf("<script", StringComparison.Ordinal);
        Assert.True(scriptStart > 0, "The settings page must declare its script block.");

        foreach (var entry in RestartRequiredSettings.RestartRequired)
        {
            var id = Assert.Single(entry.PageElementIds);
            var expected = NoteMarkup(id, entry.IsMixed);

            var inputIndex = page.IndexOf($"id=\"{id}\"", StringComparison.Ordinal);
            Assert.True(inputIndex >= 0, $"Expected the settings-page element for {entry.Name}.");

            var noteIndex = page.IndexOf(expected, StringComparison.Ordinal);
            Assert.True(noteIndex >= 0, $"Restart-required setting {entry.Name} must carry exactly: {expected}");

            // The note must be adjacent to its own element: after the input and
            // before the next input container, so a note cannot be detached from
            // the setting it describes.
            Assert.True(noteIndex > inputIndex, $"The note for {entry.Name} must follow its input.");
            var nextContainer = page.IndexOf("class=\"inputContainer\"", inputIndex + 1, StringComparison.Ordinal);
            Assert.True(nextContainer > noteIndex, $"The note for {entry.Name} must stay inside its own input container.");

            // The note is always present in the served page: static markup
            // before the script, so it does not depend on JavaScript running.
            Assert.True(noteIndex < scriptStart, $"The note for {entry.Name} must be static markup, not script output.");
        }

        Assert.Equal(
            RestartRequiredSettings.RestartRequired.Count,
            CountOccurrences(page, "class=\"fieldDescription " + NoteClass + "\""));
    }

    [Fact]
    public void MixedNotesStateImmediateApplicationAndRestartOnlyNotesDoNot()
    {
        var page = ReadEmbeddedPage();
        var mixed = RestartRequiredSettings.RestartRequired.Where(entry => entry.IsMixed).ToArray();
        var restartOnly = RestartRequiredSettings.RestartRequired.Where(entry => !entry.IsMixed).ToArray();

        Assert.NotEmpty(mixed);
        Assert.NotEmpty(restartOnly);

        foreach (var entry in RestartRequiredSettings.RestartRequired)
        {
            var id = Assert.Single(entry.PageElementIds);
            var match = Regex.Match(
                page,
                "<div class=\"fieldDescription " + NoteClass + "\" id=\"" + id + "RestartNote\">([^<]*)</div>");
            Assert.True(match.Success, $"Expected a static note for {entry.Name}.");

            var noteText = match.Groups[1].Value;
            Assert.StartsWith(RestartNoteText, noteText, StringComparison.Ordinal);

            if (entry.IsMixed)
            {
                Assert.Contains(ImmediateApplicationClause, noteText, StringComparison.Ordinal);
            }
            else
            {
                // A restart-only setting must not claim anything applies
                // immediately; a mislabelled mixed note fails here too.
                Assert.DoesNotContain("immediately", noteText, StringComparison.OrdinalIgnoreCase);
            }
        }
    }

    private static string BeforePageScript(string page)
    {
        var scriptStart = page.IndexOf("<script", StringComparison.Ordinal);
        Assert.True(scriptStart > 0, "The settings page must declare its script block.");
        return page[..scriptStart];
    }

    private static string NoteMarkup(string elementId, bool mixed)
    {
        return $"<div class=\"fieldDescription {NoteClass}\" id=\"{elementId}RestartNote\">{NoteText(mixed)}</div>";
    }

    private static string NoteText(bool mixed) => mixed ? MixedNoteText : RestartNoteText;

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
