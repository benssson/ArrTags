using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Xml.Serialization;
using ArrTags.Configuration;
using Xunit;

namespace ArrTags.Tests;

/// <summary>
/// ADR-028 / goal G6 coverage for the code-owned byte-limit display mapping: the
/// fixed unit, step, and precision per field, the unit-labelled range, exact
/// display/store round-trip at every minimum, default, and maximum (including
/// the 64 KiB minima), and the explicit rejection of an off-step or
/// over-precision value. The mapping's bounds are cross-checked against the
/// unchanged <see cref="OperationalLimits.Validate"/> byte-range validation and
/// the property defaults.
/// </summary>
public class ByteLimitUnitsTests
{
    public static IEnumerable<object[]> Fields()
    {
        return ByteLimitUnits.All.Select(field => new object[] { field.PropertyName });
    }

    public static IEnumerable<object[]> Bounds()
    {
        foreach (var field in ByteLimitUnits.All)
        {
            yield return new object[] { field.PropertyName, field.MinimumBytes };
            yield return new object[] { field.PropertyName, field.DefaultBytes };
            yield return new object[] { field.PropertyName, field.MaximumBytes };
        }
    }

    [Fact]
    public void MappingCoversTheSevenByteDenominatedLimitsInPageOrder()
    {
        Assert.Equal(
            new[]
            {
                "ProviderResponseLimitBytes",
                "WebhookMaxPayloadBytes",
                "SourceArtifactLimitBytes",
                "DerivedArtifactLimitBytes",
                "RenderCacheQuotaBytes",
                "ArtifactStorageQuotaBytes",
                "InventoryCacheMaxBytes",
            },
            ByteLimitUnits.All.Select(field => field.PropertyName).ToArray());

        foreach (var field in ByteLimitUnits.All)
        {
            Assert.Same(field, ByteLimitUnits.Get(field.PropertyName));
        }

        Assert.Throws<ArgumentNullException>(() => ByteLimitUnits.Get(null!));
        Assert.Throws<ArgumentException>(() => ByteLimitUnits.Get("QueueCapacity"));
    }

    [Theory]
    [InlineData("ProviderResponseLimitBytes", "MB", 1048576L, 65536L, 4)]
    [InlineData("WebhookMaxPayloadBytes", "KB", 1024L, 1024L, 0)]
    [InlineData("SourceArtifactLimitBytes", "MB", 1048576L, 65536L, 4)]
    [InlineData("DerivedArtifactLimitBytes", "MB", 1048576L, 65536L, 4)]
    [InlineData("RenderCacheQuotaBytes", "MB", 1048576L, 1048576L, 0)]
    [InlineData("ArtifactStorageQuotaBytes", "MB", 1048576L, 1048576L, 0)]
    [InlineData("InventoryCacheMaxBytes", "MB", 1048576L, 1048576L, 0)]
    public void UnitStepAndPrecisionMatchAdr028(
        string propertyName,
        string unitLabel,
        long unitBytes,
        long stepBytes,
        int displayPrecision)
    {
        var field = ByteLimitUnits.Get(propertyName);

        Assert.Equal(unitLabel, field.UnitLabel);
        Assert.Equal(unitBytes, field.UnitBytes);
        Assert.Equal(stepBytes, field.StepBytes);
        Assert.Equal(displayPrecision, field.DisplayPrecision);
        Assert.Equal(char.ToLowerInvariant(propertyName[0]) + propertyName[1..], field.PageElementId);

        // The step divides the unit and every bound, which is what makes the
        // display -> store round-trip exact at the minimum, default, and maximum.
        Assert.Equal(0L, unitBytes % stepBytes);
        Assert.Equal(0L, field.MinimumBytes % stepBytes);
        Assert.Equal(0L, field.DefaultBytes % stepBytes);
        Assert.Equal(0L, field.MaximumBytes % stepBytes);
        Assert.True(field.MinimumBytes <= field.DefaultBytes);
        Assert.True(field.DefaultBytes <= field.MaximumBytes);
    }

    [Theory]
    [MemberData(nameof(Fields))]
    public void BoundsMatchTheUnchangedOperationalLimitsValidation(string propertyName)
    {
        var field = ByteLimitUnits.Get(propertyName);
        var limits = new OperationalLimits();
        var property = typeof(OperationalLimits).GetProperty(
            propertyName,
            BindingFlags.Public | BindingFlags.Instance);
        Assert.NotNull(property);
        Assert.Equal(field.DefaultBytes, (long)property!.GetValue(limits)!);

        AssertAccepted(field.PropertyName, field.MinimumBytes);
        AssertAccepted(field.PropertyName, field.MaximumBytes);
        AssertRejected(field.PropertyName, field.MinimumBytes - 1);
        AssertRejected(field.PropertyName, field.MaximumBytes + 1);
    }

    [Theory]
    [InlineData("ProviderResponseLimitBytes", "0.0625 - 64 MB")]
    [InlineData("WebhookMaxPayloadBytes", "4 - 4096 KB")]
    [InlineData("SourceArtifactLimitBytes", "0.0625 - 128 MB")]
    [InlineData("DerivedArtifactLimitBytes", "0.0625 - 128 MB")]
    [InlineData("RenderCacheQuotaBytes", "64 - 65536 MB")]
    [InlineData("ArtifactStorageQuotaBytes", "256 - 262144 MB")]
    [InlineData("InventoryCacheMaxBytes", "1 - 256 MB")]
    public void RangeLabelStaysInTheFieldUnitAndShowsTheSubMebibyteBoundAsAFraction(
        string propertyName,
        string expected)
    {
        var field = ByteLimitUnits.Get(propertyName);
        var range = field.FormatRange();

        Assert.Equal(expected, range);
        Assert.EndsWith(" " + field.UnitLabel, range, StringComparison.Ordinal);
        Assert.DoesNotContain(string.Equals(field.UnitLabel, "MB", StringComparison.Ordinal) ? "KB" : "MB", range, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("ProviderResponseLimitBytes", 65536L, "0.0625")]
    [InlineData("ProviderResponseLimitBytes", 131072L, "0.125")]
    [InlineData("ProviderResponseLimitBytes", 1048576L, "1")]
    [InlineData("ProviderResponseLimitBytes", 8388608L, "8")]
    [InlineData("ProviderResponseLimitBytes", 67108864L, "64")]
    [InlineData("WebhookMaxPayloadBytes", 4096L, "4")]
    [InlineData("WebhookMaxPayloadBytes", 262144L, "256")]
    [InlineData("WebhookMaxPayloadBytes", 4194304L, "4096")]
    [InlineData("SourceArtifactLimitBytes", 131072L, "0.125")]
    [InlineData("SourceArtifactLimitBytes", 134217728L, "128")]
    [InlineData("RenderCacheQuotaBytes", 67108864L, "64")]
    [InlineData("RenderCacheQuotaBytes", 1073741824L, "1024")]
    [InlineData("RenderCacheQuotaBytes", 68719476736L, "65536")]
    [InlineData("ArtifactStorageQuotaBytes", 4294967296L, "4096")]
    [InlineData("ArtifactStorageQuotaBytes", 274877906944L, "262144")]
    [InlineData("InventoryCacheMaxBytes", 1048576L, "1")]
    [InlineData("InventoryCacheMaxBytes", 268435456L, "256")]
    public void DisplayUsesTheFixedUnitAndPrecisionWithTrailingZerosTrimmed(
        string propertyName,
        long bytes,
        string expected)
    {
        Assert.Equal(expected, ByteLimitUnits.Get(propertyName).FormatDisplay(bytes));
    }

    [Theory]
    [MemberData(nameof(Bounds))]
    public void MinimumDefaultAndMaximumRoundTripThroughTheDisplayedValue(string propertyName, long bytes)
    {
        var field = ByteLimitUnits.Get(propertyName);

        // store -> display -> store preserves the exact byte value.
        var display = field.FormatDisplay(bytes);
        Assert.True(field.TryParseDisplay(display, out var parsed), $"'{display}' must parse for {propertyName}.");
        Assert.Equal(bytes, parsed);

        // display -> store -> display is stable.
        Assert.Equal(display, field.FormatDisplay(parsed));
    }

    [Theory]
    [InlineData("ProviderResponseLimitBytes")]
    [InlineData("SourceArtifactLimitBytes")]
    [InlineData("DerivedArtifactLimitBytes")]
    public void TheSixtyFourKibibyteMinimumDisplaysAsAnExactFractionOfTheFieldUnit(string propertyName)
    {
        var field = ByteLimitUnits.Get(propertyName);

        Assert.Equal(65536L, field.MinimumBytes);
        Assert.Equal("0.0625", field.FormatDisplay(field.MinimumBytes));
        Assert.StartsWith("0.0625 - ", field.FormatRange(), StringComparison.Ordinal);
        Assert.EndsWith(" MB", field.FormatRange(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("ProviderResponseLimitBytes", "0.0625", 65536L)]
    [InlineData("ProviderResponseLimitBytes", "0.125", 131072L)]
    [InlineData("ProviderResponseLimitBytes", "0.1875", 196608L)]
    [InlineData("ProviderResponseLimitBytes", "  8  ", 8388608L)]
    [InlineData("WebhookMaxPayloadBytes", "4", 4096L)]
    [InlineData("WebhookMaxPayloadBytes", "256", 262144L)]
    [InlineData("WebhookMaxPayloadBytes", "4096", 4194304L)]
    [InlineData("RenderCacheQuotaBytes", "64", 67108864L)]
    [InlineData("RenderCacheQuotaBytes", "65536", 68719476736L)]
    [InlineData("ArtifactStorageQuotaBytes", "262144", 274877906944L)]
    [InlineData("InventoryCacheMaxBytes", "1", 1048576L)]
    public void StepMultiplesInTheFieldUnitConvertToTheExactByteValue(
        string propertyName,
        string text,
        long expected)
    {
        var field = ByteLimitUnits.Get(propertyName);

        Assert.True(field.TryParseDisplay(text, out var bytes), $"'{text}' must parse for {propertyName}.");
        Assert.Equal(expected, bytes);
    }

    [Theory]
    [InlineData("ProviderResponseLimitBytes", "0.1")]
    [InlineData("ProviderResponseLimitBytes", "0.05")]
    [InlineData("ProviderResponseLimitBytes", "0.0001")]
    [InlineData("ProviderResponseLimitBytes", "0.1001")]
    [InlineData("ProviderResponseLimitBytes", "63.9999")]
    [InlineData("SourceArtifactLimitBytes", "0.1")]
    [InlineData("DerivedArtifactLimitBytes", "0.1")]
    public void OffStepDisplayValuesAreRejectedRatherThanRounded(string propertyName, string text)
    {
        var field = ByteLimitUnits.Get(propertyName);

        Assert.False(field.TryParseDisplay(text, out var bytes), $"'{text}' must not be accepted for {propertyName}.");
        Assert.Equal(0L, bytes);
    }

    [Fact]
    public void TheAdrExampleOffStepValueWouldRoundToAnUnalignedByteCount()
    {
        var field = ByteLimitUnits.Get("ProviderResponseLimitBytes");

        // ADR-028: 0.1 MB for the 64-KiB-step field computes 104858 bytes,
        // which is not a multiple of the 65536-byte step.
        var rounded = (long)Math.Round(1000m * field.UnitBytes / 10000m, MidpointRounding.AwayFromZero);
        Assert.Equal(104858L, rounded);
        Assert.NotEqual(0L, rounded % field.StepBytes);
        Assert.False(field.TryParseDisplay("0.1", out _));
    }

    [Theory]
    [InlineData("ProviderResponseLimitBytes", "0.062500")]
    [InlineData("ProviderResponseLimitBytes", "8.00000")]
    [InlineData("WebhookMaxPayloadBytes", "4.5")]
    [InlineData("WebhookMaxPayloadBytes", "256.1")]
    [InlineData("RenderCacheQuotaBytes", "1024.5")]
    [InlineData("ArtifactStorageQuotaBytes", "0.5")]
    [InlineData("InventoryCacheMaxBytes", "1.5")]
    public void ValuesWithMoreFractionalDigitsThanTheFieldPrecisionAreRejected(string propertyName, string text)
    {
        var field = ByteLimitUnits.Get(propertyName);

        Assert.False(field.TryParseDisplay(text, out _), $"'{text}' must not be accepted for {propertyName}.");
    }

    [Theory]
    [InlineData("ProviderResponseLimitBytes", null)]
    [InlineData("ProviderResponseLimitBytes", "")]
    [InlineData("ProviderResponseLimitBytes", "   ")]
    [InlineData("ProviderResponseLimitBytes", "abc")]
    [InlineData("ProviderResponseLimitBytes", "-1")]
    [InlineData("ProviderResponseLimitBytes", "+1")]
    [InlineData("ProviderResponseLimitBytes", "1e3")]
    [InlineData("ProviderResponseLimitBytes", "1.2.3")]
    [InlineData("ProviderResponseLimitBytes", ".5")]
    [InlineData("ProviderResponseLimitBytes", "8.")]
    [InlineData("ProviderResponseLimitBytes", "1,5")]
    [InlineData("WebhookMaxPayloadBytes", "0x10")]
    [InlineData("WebhookMaxPayloadBytes", "4096 KB")]
    public void MalformedDisplayValuesAreRejected(string propertyName, string? text)
    {
        var field = ByteLimitUnits.Get(propertyName);

        Assert.False(field.TryParseDisplay(text, out _), $"'{text}' must not be accepted for {propertyName}.");
    }

    [Fact]
    public void AnExistingConfigurationWithAnOffStepByteValueStillLoadsUnchanged()
    {
        // The pre-16.1 settings page accepted any in-range byte count, so a
        // persisted ArrTags.xml can carry a value that is not a step multiple.
        // The persisted representation and the byte-range validation are
        // unchanged, so such a configuration still loads, validates, activates,
        // and survives the XML boundary with the exact byte value; only the
        // settings-page save path rejects an off-step displayed value.
        var configuration = new PluginConfiguration();
        configuration.Limits.ProviderResponseLimitBytes = 100000;

        Assert.NotEqual(0L, 100000L % ByteLimitUnits.Get("ProviderResponseLimitBytes").StepBytes);
        Assert.True(PluginConfigurationValidator.Validate(configuration).IsValid);

        var snapshot = new ConfigurationSnapshotService(configuration);
        Assert.Equal(100000L, snapshot.Current.Limits.ProviderResponseLimitBytes);

        var serializer = new XmlSerializer(typeof(PluginConfiguration));
        using var writer = new StringWriter();
        serializer.Serialize(writer, configuration);
        using var reader = new StringReader(writer.ToString());
        var restored = (PluginConfiguration)serializer.Deserialize(reader)!;
        Assert.Equal(100000L, restored.Limits.ProviderResponseLimitBytes);
    }

    private static void AssertAccepted(string propertyName, long bytes)
    {
        var errors = ValidateWith(propertyName, bytes);
        Assert.Empty(errors);
    }

    private static void AssertRejected(string propertyName, long bytes)
    {
        var errors = ValidateWith(propertyName, bytes);
        var error = Assert.Single(errors);
        Assert.Contains(propertyName, error, StringComparison.Ordinal);
    }

    private static List<string> ValidateWith(string propertyName, long bytes)
    {
        var limits = new OperationalLimits();
        typeof(OperationalLimits)
            .GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance)!
            .SetValue(limits, bytes);

        var errors = new List<string>();
        limits.Validate(errors);
        return errors;
    }
}
