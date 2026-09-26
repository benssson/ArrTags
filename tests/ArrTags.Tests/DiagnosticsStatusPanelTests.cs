using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using ArrTags.Diagnostics;
using ArrTags.Providers;
using Xunit;

namespace ArrTags.Tests;

/// <summary>
/// Phase 17 task 17.3 coverage for the ADR-025 clause 2 read-only diagnostics
/// panel on the embedded settings page. The repository deliberately has no
/// JavaScript runtime, so the panel fetch/render cannot be executed here; these
/// structural tests pin the panel against the code-owned endpoint contract
/// (<see cref="DiagnosticsSnapshot"/>, <see cref="MatchingFailureCounts"/>,
/// <see cref="RenderFailureCounts"/>, <see cref="ArrConnectionHealth"/>, and
/// <see cref="ArrTagsStatusController.RoutePrefix"/>): the fixed counter table,
/// the web-client GET, the bounded count/health fallbacks, the fail-closed
/// branch, and the absence of any input, settings write, or save-path wiring.
/// </summary>
public class DiagnosticsStatusPanelTests
{
    private const string CounterTableDeclaration = "var diagnosticsCounters =";
    private const string DiagnosticsBlockEnd = "document.querySelector('#configPage')";

    [Fact]
    public void PageDeclaresExactlyTheFixedEndpointCounterSet()
    {
        var page = ReadEmbeddedPage();
        var entries = DiagnosticsCounterEntries(page);
        var expected = ExpectedCounterPaths();

        // A single counter table is the render domain.
        Assert.Single(Regex.Matches(page, Regex.Escape(CounterTableDeclaration)));
        Assert.Equal(expected.Length, entries.Length);

        // Exact set equality against the endpoint model (DiagnosticsSnapshot,
        // MatchingFailureCounts, RenderFailureCounts): a missing counter, an
        // extra counter, and a duplicate counter all fail. Adding or removing a
        // response field without the panel fails too (ADR-025 clause 3).
        Assert.Equal(
            expected.Select(entry => entry.Path).OrderBy(path => path, StringComparer.Ordinal).ToArray(),
            entries.Select(entry => entry.Path).OrderBy(path => path, StringComparer.Ordinal).ToArray());

        foreach (var entry in entries)
        {
            // A stable page element id per counter, derived from the response path.
            Assert.Equal(
                "diagnostics" + entry.Path.Replace(".", string.Empty, StringComparison.Ordinal),
                entry.Id);
            Assert.False(string.IsNullOrWhiteSpace(entry.Group));
            Assert.False(string.IsNullOrWhiteSpace(entry.Label));
        }

        // Only the two provider-health fields render through the bounded health
        // path; every other field is a count.
        foreach (var (path, isHealth) in expected)
        {
            var entry = Assert.Single(entries, candidate => candidate.Path == path);
            Assert.Equal(isHealth, entry.Health);
        }
    }

    [Fact]
    public void PageRendersCountsAndHealthOnlyThroughBoundedFallbacks()
    {
        var page = ReadEmbeddedPage();
        var block = DiagnosticsBlock(page);

        // A missing or non-numeric count renders the 0 fallback.
        Assert.Contains(
            "return typeof value === 'number' && isFinite(value) ? value : 0;",
            block,
            StringComparison.Ordinal);

        // Health renders only one of the declared ArrConnectionHealth names;
        // anything else (including an unexpected free-form response string)
        // renders Unknown, never the raw response text.
        var whitelist = Regex.Match(block, @"var diagnosticsHealthNames = \[(?<names>[^\]]*)\];");
        Assert.True(whitelist.Success, "The page must declare the bounded health-name whitelist.");
        Assert.Equal(
            Enum.GetNames<ArrConnectionHealth>(),
            Regex.Matches(whitelist.Groups["names"].Value, "'([A-Za-z]+)'")
                .Select(match => match.Groups[1].Value)
                .ToArray());
        Assert.Contains("value === diagnosticsHealthNames[healthIndex]", block, StringComparison.Ordinal);
        Assert.Contains("return 'Unknown';", block, StringComparison.Ordinal);

        // The render reads response values only through the two bounded readers,
        // writes only fixed page literals and bounded values into the DOM, and
        // never iterates or stringifies the response object.
        Assert.Contains("valueElement.textContent = entry.kind === 'health'", block, StringComparison.Ordinal);
        Assert.Contains("? diagnosticsReadHealth(snapshot, entry.path)", block, StringComparison.Ordinal);
        Assert.Contains(": String(diagnosticsReadCount(snapshot, entry.path));", block, StringComparison.Ordinal);
        var textAssignmentCount = Regex.Matches(block, "textContent = ").Cast<Match>().Count();
        Assert.Equal(4, textAssignmentCount);
        Assert.Contains("heading.textContent = entry.group;", block, StringComparison.Ordinal);
        Assert.Contains("label.textContent = entry.label + ': ';", block, StringComparison.Ordinal);
        Assert.DoesNotContain("snapshot[", block, StringComparison.Ordinal);
        Assert.Single(Regex.Matches(block, "String\\("));
        Assert.DoesNotContain("JSON.parse", block, StringComparison.Ordinal);
        Assert.DoesNotContain("JSON.stringify", block, StringComparison.Ordinal);
        Assert.DoesNotContain("Object.keys", block, StringComparison.Ordinal);
        Assert.DoesNotContain("for (var key in", block, StringComparison.Ordinal);
        Assert.DoesNotContain("innerHTML", block, StringComparison.Ordinal);
    }

    [Fact]
    public void PageFetchesTheElevationGatedRouteThroughTheWebClientOncePerPageShow()
    {
        var page = ReadEmbeddedPage();
        var block = DiagnosticsBlock(page);

        Assert.Contains("ApiClient.ajax({", block, StringComparison.Ordinal);
        Assert.Contains("type: 'GET',", block, StringComparison.Ordinal);
        Assert.Contains(
            "url: ApiClient.getUrl('" + ArrTagsStatusController.RoutePrefix + "'),",
            block,
            StringComparison.Ordinal);
        Assert.Contains("dataType: 'json'", block, StringComparison.Ordinal);

        // Exactly one read of the status route, one request, and one load call.
        Assert.Single(Regex.Matches(page, "ApiClient\\.ajax\\("));
        Assert.Single(Regex.Matches(page, "ApiClient\\.getUrl\\("));
        Assert.Single(Regex.Matches(page, "diagnosticsLoad\\(\\);"));

        // The single call sits inside the pageshow listener, after the
        // configuration load request and before the form listeners.
        var pageshow = page.IndexOf("addEventListener('pageshow'", StringComparison.Ordinal);
        var configLoad = page.IndexOf("ApiClient.getPluginConfiguration", StringComparison.Ordinal);
        var loadCall = page.IndexOf("diagnosticsLoad();", StringComparison.Ordinal);
        var formListener = page.IndexOf("addEventListener('input'", StringComparison.Ordinal);
        Assert.True(pageshow >= 0, "The page must declare its pageshow listener.");
        Assert.True(
            loadCall > pageshow && loadCall > configLoad && loadCall < formListener,
            "diagnosticsLoad() must run once from the pageshow listener after the configuration load.");

        // Read-only and bounded: no write verb, no retry, and no polling.
        foreach (var verb in new[] { "'POST'", "'PUT'", "'PATCH'", "'DELETE'" })
        {
            Assert.DoesNotContain("type: " + verb, page, StringComparison.Ordinal);
        }

        Assert.DoesNotContain("setTimeout", block, StringComparison.Ordinal);
        Assert.DoesNotContain("setInterval", block, StringComparison.Ordinal);
    }

    [Fact]
    public void PageFailsClosedWhenTheRequestIsRejectedOrTheBodyIsMalformed()
    {
        var page = ReadEmbeddedPage();
        var block = DiagnosticsBlock(page);

        // The static panel container starts hidden, so nothing shows before a
        // bounded snapshot renders.
        var panelTag = Regex.Match(page, "<div id=\"diagnosticsPanel\"[^>]*>");
        Assert.True(panelTag.Success, "The page must declare the diagnostics panel container.");
        Assert.Contains("style=\"display:none;\"", panelTag.Value, StringComparison.Ordinal);

        // Only a bounded object renders; a null, array, string, or number body
        // leaves the panel cleared and hidden.
        Assert.Contains(
            "if (!snapshot || typeof snapshot !== 'object' || Array.isArray(snapshot)) {",
            block,
            StringComparison.Ordinal);

        // A rejected request clears the panel through the second then argument.
        Assert.Matches(
            @"\},\s*function\s*\(\)\s*\{\s*diagnosticsClearPanel\(\);\s*\}\);",
            page);

        // A render exception is caught and cleared, never surfaced.
        Assert.Matches(
            @"try\s*\{\s*diagnosticsRenderPanel\(snapshot\);\s*\}\s*catch\s*\(\s*[A-Za-z_]+\s*\)\s*\{\s*diagnosticsClearPanel\(\);\s*\}",
            block);

        // The clear path is the only thing that writes to the panel container,
        // and it writes no data and no message.
        Assert.Single(Regex.Matches(block, "panel\\.textContent = "));
        Assert.Contains("panel.textContent = '';", block, StringComparison.Ordinal);
        Assert.Contains("panel.style.display = 'none';", block, StringComparison.Ordinal);
        Assert.Contains("panel.style.display = '';", block, StringComparison.Ordinal);
    }

    [Fact]
    public void PageAddsNoSettingsFieldNoWritePathAndNoSaveWiring()
    {
        var page = ReadEmbeddedPage();
        var block = DiagnosticsBlock(page);

        // The panel markup is a plain static section with one container: no form
        // control, no settings field, and no form element.
        var section = Regex.Match(
            page,
            @"<div class=""verticalSection"" id=""diagnosticsSection"">(?<body>.*?)\n                    </div>",
            RegexOptions.Singleline);
        Assert.True(section.Success, "The page must declare the diagnostics section.");
        var sectionBody = section.Groups["body"].Value;
        Assert.Contains("<h2>Diagnostics</h2>", sectionBody, StringComparison.Ordinal);
        Assert.Contains("id=\"diagnosticsPanel\"", sectionBody, StringComparison.Ordinal);
        foreach (var control in new[] { "<input", "<select", "<textarea", "<button", "<form" })
        {
            Assert.DoesNotContain(control, sectionBody, StringComparison.Ordinal);
        }

        // The diagnostics block touches no settings value and no configuration
        // request/response, so it cannot add or alter a saved field, and it
        // references no credential.
        Assert.DoesNotContain("config.", block, StringComparison.Ordinal);
        Assert.DoesNotContain("getPluginConfiguration", block, StringComparison.Ordinal);
        Assert.DoesNotContain("updatePluginConfiguration", block, StringComparison.Ordinal);
        Assert.DoesNotContain("readByteLimitValues", block, StringComparison.Ordinal);
        Assert.DoesNotContain("restartRequiredFields", block, StringComparison.Ordinal);
        Assert.DoesNotContain("ApiKey", block, StringComparison.Ordinal);
        Assert.DoesNotContain("Secret", block, StringComparison.Ordinal);

        // The save path is unchanged: the submit handler never references the
        // panel and keeps its single configuration read and write, and the page
        // keeps the pre-existing read/write counts.
        var submit = SubmitHandlerRegion(page);
        Assert.DoesNotContain("diagnostics", submit, StringComparison.OrdinalIgnoreCase);
        Assert.Single(Regex.Matches(submit, "ApiClient\\.getPluginConfiguration"));
        Assert.Single(Regex.Matches(submit, "ApiClient\\.updatePluginConfiguration"));
        var configurationReads = Regex.Matches(page, "ApiClient\\.getPluginConfiguration").Cast<Match>().Count();
        var configurationWrites = Regex.Matches(page, "ApiClient\\.updatePluginConfiguration").Cast<Match>().Count();
        Assert.Equal(2, configurationReads);
        Assert.Equal(1, configurationWrites);
    }

    [Fact]
    public void PagePlacesThePanelAfterLoggingAndBeforeTheSaveButton()
    {
        var page = ReadEmbeddedPage();

        var logging = page.IndexOf("<h2>Logging</h2>", StringComparison.Ordinal);
        var diagnostics = page.IndexOf("<h2>Diagnostics</h2>", StringComparison.Ordinal);
        var save = page.IndexOf("class=\"raised button-submit block\"", StringComparison.Ordinal);

        Assert.True(logging >= 0, "The Logging section must remain on the page.");
        Assert.True(diagnostics > logging, "The diagnostics panel must follow the Logging section.");
        Assert.True(save > diagnostics, "The diagnostics panel must precede the Save button.");
    }

    private static (string Path, bool IsHealth)[] ExpectedCounterPaths()
    {
        var entries = new List<(string Path, bool IsHealth)>();
        foreach (var property in typeof(DiagnosticsSnapshot)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (property.PropertyType == typeof(ArrConnectionHealth))
            {
                entries.Add((property.Name, true));
            }
            else if (property.PropertyType == typeof(int) || property.PropertyType == typeof(long))
            {
                entries.Add((property.Name, false));
            }
            else
            {
                foreach (var child in property.PropertyType
                    .GetProperties(BindingFlags.Public | BindingFlags.Instance))
                {
                    entries.Add((property.Name + "." + child.Name, false));
                }
            }
        }

        return entries.ToArray();
    }

    private static (string Id, string Group, string Label, string Path, bool Health)[] DiagnosticsCounterEntries(
        string page)
    {
        var matches = Regex.Matches(
            DiagnosticsBlock(page),
            @"\{ id: '([A-Za-z0-9]+)', group: '([^']+)', label: '([^']+)', path: '([A-Za-z0-9.]+)'(?:, kind: '([a-z]+)')? \}");
        Assert.NotEmpty(matches);

        return matches
            .Select(match => (
                Id: match.Groups[1].Value,
                Group: match.Groups[2].Value,
                Label: match.Groups[3].Value,
                Path: match.Groups[4].Value,
                Health: match.Groups[5].Success && match.Groups[5].Value == "health"))
            .ToArray();
    }

    private static string DiagnosticsBlock(string page)
    {
        var start = page.IndexOf(CounterTableDeclaration, StringComparison.Ordinal);
        Assert.True(start >= 0, "The settings page must declare the diagnostics counter table.");
        var end = page.IndexOf(DiagnosticsBlockEnd, start, StringComparison.Ordinal);
        Assert.True(end > start, "The diagnostics block must precede the pageshow listener.");
        return page[start..end];
    }

    private static string SubmitHandlerRegion(string page)
    {
        var start = page.IndexOf("addEventListener('submit'", StringComparison.Ordinal);
        Assert.True(start >= 0, "The settings page must declare its submit handler.");
        var end = page.IndexOf("</script>", start, StringComparison.Ordinal);
        Assert.True(end > start, "The submit handler must stay inside the page script.");
        return page[start..end];
    }

    private static string ReadEmbeddedPage()
    {
        using var stream = typeof(Plugin).Assembly.GetManifestResourceStream(Plugin.SettingsPageResourceName);
        Assert.NotNull(stream);
        using var reader = new StreamReader(stream!);
        return reader.ReadToEnd();
    }
}
