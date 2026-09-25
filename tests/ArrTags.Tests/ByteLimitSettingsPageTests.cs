using System;
using System.IO;
using System.Text.RegularExpressions;
using ArrTags.Configuration;
using Xunit;

namespace ArrTags.Tests;

/// <summary>
/// ADR-028 / goal G6 coverage for the dashboard settings page: the page declares
/// the same per-field unit, step, precision, and byte range as the code-owned
/// <see cref="ByteLimitUnits"/> mapping, shows a unit-labelled validation range,
/// converts stored bytes to the display unit and a displayed value back to bytes
/// on save, and rejects a displayed value that is not a step multiple. There is
/// no JavaScript runtime in this repository, so the page contract is pinned
/// structurally against the C# mapping and the conversion algorithm it mirrors.
/// </summary>
public class ByteLimitSettingsPageTests
{
    [Fact]
    public void PageDeclaresTheByteLimitMappingUsedByTheConversion()
    {
        var page = ReadEmbeddedPage();

        // One mapping entry per byte-denominated limit, and no more.
        Assert.Equal(ByteLimitUnits.All.Count, Regex.Matches(page, "property: '").Count);

        foreach (var field in ByteLimitUnits.All)
        {
            var expected = FormattableString.Invariant(
                $"{{ property: '{field.PropertyName}', id: '{field.PageElementId}', unit: '{field.UnitLabel}', unitBytes: {field.UnitBytes}, stepBytes: {field.StepBytes}, precision: {field.DisplayPrecision} }}");
            Assert.Contains(expected, page, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void PageDeclaresTheNativeUnitStepAndRangeForEveryByteLimit()
    {
        var page = ReadEmbeddedPage();

        foreach (var field in ByteLimitUnits.All)
        {
            var tag = Regex.Match(page, "<input[^>]*id=\"" + Regex.Escape(field.PageElementId) + "\"[^>]*>");
            Assert.True(tag.Success, $"Expected a byte-limit input for {field.PropertyName}.");

            Assert.Contains("type=\"number\"", tag.Value, StringComparison.Ordinal);
            Assert.Contains(
                "min=\"" + field.FormatDisplay(field.MinimumBytes) + "\"",
                tag.Value,
                StringComparison.Ordinal);
            Assert.Contains(
                "max=\"" + field.FormatDisplay(field.MaximumBytes) + "\"",
                tag.Value,
                StringComparison.Ordinal);
            Assert.Contains(
                "step=\"" + field.FormatDisplay(field.StepBytes) + "\"",
                tag.Value,
                StringComparison.Ordinal);

            // The native step/min/max constraints own the range and step; the
            // integer-only pattern of the byte inputs must not survive, because
            // it cannot express a fractional display value.
            Assert.DoesNotContain("pattern=", tag.Value, StringComparison.Ordinal);

            var label = Regex.Match(tag.Value, "label=\"([^\"]*)\"");
            Assert.True(label.Success, $"Expected a label for {field.PropertyName}.");
            Assert.EndsWith("(" + field.UnitLabel + ")", label.Groups[1].Value, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void PageShowsAUnitLabelledValidationRangeForEveryByteLimit()
    {
        var page = ReadEmbeddedPage();

        foreach (var field in ByteLimitUnits.All)
        {
            var expected = "<div class=\"fieldDescription\" id=\"" + field.PageElementId
                + "Range\">Valid range: " + field.FormatRange() + ".</div>";
            Assert.Contains(expected, page, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void PageConvertsDisplayedValuesToBytesBeforeSavingAndRejectsOffStepValues()
    {
        var page = ReadEmbeddedPage();

        // Display from store and store from display both use the field mapping.
        Assert.Contains("byteLimitToDisplay(config.Limits[definition.property], definition)", page, StringComparison.Ordinal);
        Assert.Contains("byteLimitToBytes(input.value, definition)", page, StringComparison.Ordinal);

        // The ADR-028 conversion algorithm: scaled decimal -> rounded bytes, and
        // the explicit step-multiple rejection that stops a silent rounding.
        Assert.Contains("Math.round(scaled * definition.unitBytes / divisor)", page, StringComparison.Ordinal);
        Assert.Contains("(scaled / divisor).toFixed(definition.precision)", page, StringComparison.Ordinal);
        Assert.Contains("bytes % definition.stepBytes !== 0", page, StringComparison.Ordinal);

        // The submit path stores the converted bytes, and an off-step value
        // aborts the save before the configuration request is made.
        Assert.Contains("values[definition.property] = bytes;", page, StringComparison.Ordinal);
        Assert.Contains("config.Limits[definition.property] = byteLimitValues[definition.property];", page, StringComparison.Ordinal);
        Assert.Contains("var byteLimitValues = readByteLimitValues();", page, StringComparison.Ordinal);
        Assert.Contains("if (byteLimitValues === null)", page, StringComparison.Ordinal);
        Assert.True(
            page.IndexOf("var byteLimitValues = readByteLimitValues();", StringComparison.Ordinal)
                < page.IndexOf("ApiClient.updatePluginConfiguration", StringComparison.Ordinal),
            "The byte-limit conversion must run before the configuration save request.");
    }

    [Fact]
    public void PageNoLongerWritesRawDisplayNumbersIntoByteFields()
    {
        var page = ReadEmbeddedPage();

        foreach (var field in ByteLimitUnits.All)
        {
            Assert.DoesNotContain(
                "config.Limits." + field.PropertyName + " = parseInt",
                page,
                StringComparison.Ordinal);
            Assert.DoesNotContain(
                "parseInt(document.querySelector('#" + field.PageElementId + "')",
                page,
                StringComparison.Ordinal);
            Assert.DoesNotContain(
                "config.Limits." + field.PropertyName + " = document.querySelector",
                page,
                StringComparison.Ordinal);
        }

        // The table-driven submit path must not parse a byte field either.
        Assert.DoesNotContain("config.Limits[definition.property] = parseInt", page, StringComparison.Ordinal);
        Assert.DoesNotContain("parseInt(document.querySelector('#' + definition.id)", page, StringComparison.Ordinal);
        Assert.DoesNotContain("parseInt(input.value", page, StringComparison.Ordinal);
    }

    private static string ReadEmbeddedPage()
    {
        using var stream = typeof(Plugin).Assembly.GetManifestResourceStream(Plugin.SettingsPageResourceName);
        Assert.NotNull(stream);
        using var reader = new StreamReader(stream!);
        return reader.ReadToEnd();
    }
}
