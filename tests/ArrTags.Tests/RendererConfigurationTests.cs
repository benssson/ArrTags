using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Xml.Serialization;
using ArrTags.Configuration;
using ArrTags.Rendering;
using ArrTags.Secrets;
using Xunit;

namespace ArrTags.Tests;

/// <summary>
/// Phase 4 task 4.10 checks for the persisted renderer configuration, its
/// validation, the immutable snapshot mapping, and the secret-free renderer
/// configuration fingerprint. These tests need no Skia native runtime.
/// </summary>
public class RendererConfigurationTests
{
    [Fact]
    public void DefaultConfigurationMapsToTheAdr009Defaults()
    {
        var configuration = new PluginConfiguration();

        Assert.True(PluginConfigurationValidator.Validate(configuration).IsValid);

        var snapshot = PluginConfigurationSnapshot.From(configuration);

        Assert.Equal(BadgeDefinition.V1Default.Count, snapshot.BadgeDefinitions.Count);
        for (var index = 0; index < BadgeDefinition.V1Default.Count; index++)
        {
            Assert.Equal(BadgeDefinition.V1Default[index].Selector, snapshot.BadgeDefinitions[index].Selector);
            Assert.Equal(BadgeDefinition.V1Default[index].Enabled, snapshot.BadgeDefinitions[index].Enabled);
            Assert.Equal(BadgeDefinition.V1Default[index].Template, snapshot.BadgeDefinitions[index].Template);
        }

        var policy = snapshot.RendererOutputPolicy;
        Assert.Equal(RenderOutputPolicy.Default.TechnicalBackground, policy.TechnicalBackground);
        Assert.Equal(RenderOutputPolicy.Default.TechnicalText, policy.TechnicalText);
        Assert.Equal(RenderOutputPolicy.Default.StatusBackground, policy.StatusBackground);
        Assert.Equal(RenderOutputPolicy.Default.StatusText, policy.StatusText);

        // The code-owned values must be preserved exactly by the resolver.
        Assert.Equal(RenderOutputPolicy.Default.OutputFormat, policy.OutputFormat);
        Assert.Equal(RenderOutputPolicy.Default.ColorSpace, policy.ColorSpace);
        Assert.Equal(RenderOutputPolicy.Default.AlphaPolicy, policy.AlphaPolicy);
        Assert.Equal(RenderOutputPolicy.Default.FontIdentity.Descriptor, policy.FontIdentity.Descriptor);
        Assert.Equal(RenderOutputPolicy.Default.ScaleReferenceWidth, policy.ScaleReferenceWidth);
        Assert.Equal(RenderOutputPolicy.Default.MinimumScale, policy.MinimumScale);
        Assert.Equal(RenderOutputPolicy.Default.MaximumScale, policy.MaximumScale);
        Assert.Equal(RenderOutputPolicy.Default.MaximumScalarValues, policy.MaximumScalarValues);
        Assert.Equal(RenderOutputPolicy.Default.RetainedPrefixScalarValues, policy.RetainedPrefixScalarValues);
        Assert.Equal(RenderOutputPolicy.Default.Ellipsis, policy.Ellipsis);

        Assert.False(string.IsNullOrEmpty(snapshot.RendererConfigurationFingerprint));
        Assert.Matches("^[0-9A-F]{64}$", snapshot.RendererConfigurationFingerprint);
    }

    [Fact]
    public void ValidSelectorOverridesAreMappedAndMissesKeepDefaults()
    {
        var configuration = new PluginConfiguration();
        configuration.Renderer.Selectors.Add(new BadgeSelectorConfiguration
        {
            Selector = BadgeSelector.Quality,
            Enabled = false,
        });
        configuration.Renderer.Selectors.Add(new BadgeSelectorConfiguration
        {
            Selector = BadgeSelector.Source,
            Enabled = true,
            Template = "SRC {value}",
        });

        Assert.True(PluginConfigurationValidator.Validate(configuration).IsValid);

        var snapshot = PluginConfigurationSnapshot.From(configuration);

        var quality = snapshot.BadgeDefinitions.Single(definition => definition.Selector == BadgeSelector.Quality);
        Assert.False(quality.Enabled);

        var source = snapshot.BadgeDefinitions.Single(definition => definition.Selector == BadgeSelector.Source);
        Assert.True(source.Enabled);
        Assert.Equal("SRC {value}", source.Template);

        var resolution = snapshot.BadgeDefinitions.Single(definition => definition.Selector == BadgeSelector.Resolution);
        Assert.True(resolution.Enabled);
        Assert.Equal(BadgeDefinition.ValuePlaceholder, resolution.Template);
    }

    [Fact]
    public void ValidatorRejectsUnknownSelector()
    {
        var configuration = new PluginConfiguration();
        configuration.Renderer.Selectors.Add(new BadgeSelectorConfiguration
        {
            Selector = (BadgeSelector)999,
        });

        var result = PluginConfigurationValidator.Validate(configuration);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.Contains("known V1 selector", StringComparison.Ordinal));
    }

    [Fact]
    public void ValidatorRejectsDuplicateSelectors()
    {
        var configuration = new PluginConfiguration();
        configuration.Renderer.Selectors.Add(new BadgeSelectorConfiguration { Selector = BadgeSelector.Quality });
        configuration.Renderer.Selectors.Add(new BadgeSelectorConfiguration { Selector = BadgeSelector.Quality });

        var result = PluginConfigurationValidator.Validate(configuration);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.Contains("unique", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("{value}{value}")]
    public void ValidatorRejectsUnusableTemplates(string template)
    {
        var configuration = new PluginConfiguration();
        configuration.Renderer.Selectors.Add(new BadgeSelectorConfiguration
        {
            Selector = BadgeSelector.Quality,
            Template = template,
        });

        Assert.False(PluginConfigurationValidator.Validate(configuration).IsValid);
    }

    [Fact]
    public void ValidatorRejectsTooLongTemplate()
    {
        var configuration = new PluginConfiguration();
        configuration.Renderer.Selectors.Add(new BadgeSelectorConfiguration
        {
            Selector = BadgeSelector.Quality,
            Template = new string('a', BadgeDefinition.MaximumTemplateLength + 1),
        });

        var result = PluginConfigurationValidator.Validate(configuration);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.Contains("must not exceed", StringComparison.Ordinal));
    }

    [Fact]
    public void PaletteOverrideIsAcceptedAndCanonicalized()
    {
        var configuration = new PluginConfiguration();
        configuration.Renderer.TechnicalBackground = "#000000";
        configuration.Renderer.TechnicalText = "#ffffff";

        Assert.True(PluginConfigurationValidator.Validate(configuration).IsValid);

        var snapshot = PluginConfigurationSnapshot.From(configuration);

        Assert.Equal("#000000", snapshot.RendererOutputPolicy.TechnicalBackground);
        Assert.Equal("#FFFFFF", snapshot.RendererOutputPolicy.TechnicalText);
        Assert.Equal(RenderOutputPolicy.Default.StatusBackground, snapshot.RendererOutputPolicy.StatusBackground);
        Assert.Equal(RenderOutputPolicy.Default.StatusText, snapshot.RendererOutputPolicy.StatusText);
    }

    [Fact]
    public void ValidatorRejectsMalformedColor()
    {
        var configuration = new PluginConfiguration();
        configuration.Renderer.TechnicalBackground = "not-a-color";

        var result = PluginConfigurationValidator.Validate(configuration);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.Contains("RRGGBB", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("#767676", "#FFFFFF", true)]
    [InlineData("#777777", "#FFFFFF", false)]
    [InlineData("#757575", "#000000", true)]
    [InlineData("#747474", "#000000", false)]
    public void PaletteContrastBoundaryIsEnforced(string background, string text, bool expectedValid)
    {
        var configuration = new PluginConfiguration();
        configuration.Renderer.TechnicalBackground = background;
        configuration.Renderer.TechnicalText = text;

        var result = PluginConfigurationValidator.Validate(configuration);

        Assert.Equal(expectedValid, result.IsValid);
        if (!expectedValid)
        {
            Assert.Contains(result.Errors, error => error.Contains("contrast", StringComparison.Ordinal));
        }
    }

    [Fact]
    public void SnapshotIsImmutableAndUnaffectedBySourceMutation()
    {
        var configuration = new PluginConfiguration();
        configuration.Renderer.Selectors.Add(new BadgeSelectorConfiguration
        {
            Selector = BadgeSelector.Quality,
            Enabled = false,
        });

        var snapshot = PluginConfigurationSnapshot.From(configuration);
        var fingerprint = snapshot.RendererConfigurationFingerprint;
        var definitionCount = snapshot.BadgeDefinitions.Count;

        // Mutating the persisted configuration after From must not change the snapshot.
        configuration.Renderer.Selectors.Add(new BadgeSelectorConfiguration
        {
            Selector = BadgeSelector.Source,
            Enabled = false,
        });
        configuration.Renderer.TechnicalBackground = "#000000";

        Assert.Equal(definitionCount, snapshot.BadgeDefinitions.Count);
        Assert.False(snapshot.BadgeDefinitions.Single(definition => definition.Selector == BadgeSelector.Quality).Enabled);
        Assert.Equal(RenderOutputPolicy.Default.TechnicalBackground, snapshot.RendererOutputPolicy.TechnicalBackground);
        Assert.Equal(fingerprint, snapshot.RendererConfigurationFingerprint);

        // The exposed definition list is read-only.
        Assert.Throws<NotSupportedException>(
            () => ((IList<BadgeDefinition>)snapshot.BadgeDefinitions).Add(BadgeDefinition.V1Default[0]));
    }

    [Fact]
    public void RendererFingerprintExcludesCredentialsAndWebhookSecret()
    {
        var first = new PluginConfiguration();
        first.Sonarr.ApiKey = "sonarr-secret-a";
        first.Radarr.ApiKey = "radarr-secret-a";
        first.WebhookSecret = "webhook-secret-a";

        var second = new PluginConfiguration();
        second.Sonarr.ApiKey = "sonarr-secret-b";
        second.Radarr.ApiKey = "radarr-secret-b";
        second.WebhookSecret = "webhook-secret-b";

        var firstSnapshot = PluginConfigurationSnapshot.From(first);
        var secondSnapshot = PluginConfigurationSnapshot.From(second);

        Assert.Equal(firstSnapshot.RendererConfigurationFingerprint, secondSnapshot.RendererConfigurationFingerprint);
        Assert.DoesNotContain("sonarr-secret-a", firstSnapshot.RendererConfigurationFingerprint, StringComparison.Ordinal);
        Assert.DoesNotContain("webhook-secret-a", firstSnapshot.RendererConfigurationFingerprint, StringComparison.Ordinal);
        Assert.DoesNotContain("sonarr-secret-b", secondSnapshot.RendererConfigurationFingerprint, StringComparison.Ordinal);
        Assert.DoesNotContain("webhook-secret-b", secondSnapshot.RendererConfigurationFingerprint, StringComparison.Ordinal);
    }

    [Fact]
    public void RendererFingerprintIsDeterministicAndSensitiveToEveryOutputAffectingValue()
    {
        var original = Fingerprint(new PluginConfiguration());
        Assert.Equal(original, Fingerprint(new PluginConfiguration()));

        var disabledQuality = new PluginConfiguration();
        disabledQuality.Renderer.Selectors.Add(new BadgeSelectorConfiguration
        {
            Selector = BadgeSelector.Quality,
            Enabled = false,
        });
        Assert.NotEqual(original, Fingerprint(disabledQuality));

        var changedTemplate = new PluginConfiguration();
        changedTemplate.Renderer.Selectors.Add(new BadgeSelectorConfiguration
        {
            Selector = BadgeSelector.Quality,
            Template = "Q:{value}",
        });
        Assert.NotEqual(original, Fingerprint(changedTemplate));

        var changedTechnicalBackground = new PluginConfiguration();
        changedTechnicalBackground.Renderer.TechnicalBackground = "#000000";
        Assert.NotEqual(original, Fingerprint(changedTechnicalBackground));

        var changedTechnicalText = new PluginConfiguration();
        changedTechnicalText.Renderer.TechnicalText = "#EEEEEE";
        Assert.NotEqual(original, Fingerprint(changedTechnicalText));

        var changedStatusBackground = new PluginConfiguration();
        changedStatusBackground.Renderer.StatusBackground = "#000000";
        Assert.NotEqual(original, Fingerprint(changedStatusBackground));

        var changedStatusText = new PluginConfiguration();
        changedStatusText.Renderer.StatusText = "#EEEEEE";
        Assert.NotEqual(original, Fingerprint(changedStatusText));

        // Equivalent colors and entry order are normalized, so neither changes the fingerprint.
        var upperCase = new PluginConfiguration();
        upperCase.Renderer.TechnicalBackground = "#111827";
        Assert.Equal(original, Fingerprint(upperCase));

        var ordered = new PluginConfiguration();
        ordered.Renderer.Selectors.Add(new BadgeSelectorConfiguration { Selector = BadgeSelector.Quality, Enabled = false });
        ordered.Renderer.Selectors.Add(new BadgeSelectorConfiguration { Selector = BadgeSelector.Source, Enabled = false });

        var reordered = new PluginConfiguration();
        reordered.Renderer.Selectors.Add(new BadgeSelectorConfiguration { Selector = BadgeSelector.Source, Enabled = false });
        reordered.Renderer.Selectors.Add(new BadgeSelectorConfiguration { Selector = BadgeSelector.Quality, Enabled = false });

        Assert.Equal(Fingerprint(ordered), Fingerprint(reordered));
    }

    [Fact]
    public void SnapshotServiceRetainsLastValidRendererConfigurationOnInvalidReplacement()
    {
        var initial = new PluginConfiguration();
        initial.Sonarr.ApiKey = "sonarr-key";
        initial.Renderer.Selectors.Add(new BadgeSelectorConfiguration
        {
            Selector = BadgeSelector.Quality,
            Enabled = false,
        });

        var service = new ConfigurationSnapshotService(initial);
        var previous = service.Current;
        var previousFingerprint = previous.RendererConfigurationFingerprint;

        var invalid = new PluginConfiguration();
        invalid.Sonarr.ApiKey = "replacement-key";
        invalid.Renderer.TechnicalBackground = "#000000";
        invalid.Renderer.TechnicalText = "#000000";

        Assert.False(service.TryReplace(invalid, out var result));
        Assert.False(result.IsValid);

        Assert.Same(previous, service.Current);
        Assert.Equal(previousFingerprint, service.Current.RendererConfigurationFingerprint);
        Assert.False(service.Current.BadgeDefinitions.Single(definition => definition.Selector == BadgeSelector.Quality).Enabled);

        // The private secret snapshot is also retained: the previous generation still resolves.
        Assert.True(service.TryAcquire(SecretReference.SonarrApiKey, service.Current.ConfigurationVersion, out var lease));
        Assert.NotNull(lease);
        lease!.Dispose();
    }

    [Fact]
    public void SnapshotServiceActivatesValidRendererReplacement()
    {
        var service = new ConfigurationSnapshotService(new PluginConfiguration());
        var previousFingerprint = service.Current.RendererConfigurationFingerprint;

        var replacement = new PluginConfiguration();
        replacement.Renderer.Selectors.Add(new BadgeSelectorConfiguration
        {
            Selector = BadgeSelector.Source,
            Enabled = false,
        });

        Assert.True(service.TryReplace(replacement, out var result));
        Assert.True(result.IsValid);
        Assert.NotEqual(previousFingerprint, service.Current.RendererConfigurationFingerprint);
        Assert.False(service.Current.BadgeDefinitions.Single(definition => definition.Selector == BadgeSelector.Source).Enabled);
    }

    [Fact]
    public void ValidatorAcceptsAndResolvesAllowedValues()
    {
        var configuration = new PluginConfiguration();
        var entry = new BadgeSelectorConfiguration { Selector = BadgeSelector.Quality };
        entry.AllowedValues.Add("Remux-2160p");
        entry.AllowedValues.Add("Bluray-1080p");
        configuration.Renderer.Selectors.Add(entry);

        Assert.True(PluginConfigurationValidator.Validate(configuration).IsValid);

        var snapshot = PluginConfigurationSnapshot.From(configuration);
        var quality = snapshot.BadgeDefinitions.Single(definition => definition.Selector == BadgeSelector.Quality);
        Assert.Equal(new[] { "Remux-2160p", "Bluray-1080p" }, quality.AllowedValues);

        // An absent allowlist keeps the code-owned "no restriction" default.
        var resolution = snapshot.BadgeDefinitions.Single(definition => definition.Selector == BadgeSelector.Resolution);
        Assert.Empty(resolution.AllowedValues);
    }

    [Fact]
    public void ValidatorTrimsAllowedValueEntries()
    {
        var configuration = new PluginConfiguration();
        var entry = new BadgeSelectorConfiguration { Selector = BadgeSelector.Quality };
        entry.AllowedValues.Add("  SDR  ");
        entry.AllowedValues.Add("\tHDR10\n");
        configuration.Renderer.Selectors.Add(entry);

        Assert.True(PluginConfigurationValidator.Validate(configuration).IsValid);

        var snapshot = PluginConfigurationSnapshot.From(configuration);
        var quality = snapshot.BadgeDefinitions.Single(definition => definition.Selector == BadgeSelector.Quality);
        Assert.Equal(new[] { "SDR", "HDR10" }, quality.AllowedValues);
    }

    [Fact]
    public void ValidatorRejectsTooManyAllowedValues()
    {
        var configuration = new PluginConfiguration();
        var entry = new BadgeSelectorConfiguration { Selector = BadgeSelector.Quality };
        for (var index = 0; index <= BadgeDefinition.MaximumAllowedValues; index++)
        {
            entry.AllowedValues.Add(FormattableString.Invariant($"value-{index}"));
        }

        configuration.Renderer.Selectors.Add(entry);

        var result = PluginConfigurationValidator.Validate(configuration);

        Assert.False(result.IsValid);
        Assert.Contains(
            result.Errors,
            error => error.Contains("must not exceed", StringComparison.Ordinal)
                && error.Contains(
                    BadgeDefinition.MaximumAllowedValues.ToString(CultureInfo.InvariantCulture),
                    StringComparison.Ordinal));
    }

    [Fact]
    public void ValidatorRejectsTooLongAllowedValue()
    {
        var configuration = new PluginConfiguration();
        var entry = new BadgeSelectorConfiguration { Selector = BadgeSelector.Quality };
        entry.AllowedValues.Add(new string('a', BadgeDefinition.MaximumAllowedValueLength + 1));
        configuration.Renderer.Selectors.Add(entry);

        var result = PluginConfigurationValidator.Validate(configuration);

        Assert.False(result.IsValid);
        Assert.Contains(
            result.Errors,
            error => error.Contains("must not exceed", StringComparison.Ordinal)
                && error.Contains("characters", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t")]
    public void ValidatorRejectsBlankAllowedValue(string value)
    {
        var configuration = new PluginConfiguration();
        var entry = new BadgeSelectorConfiguration { Selector = BadgeSelector.Quality };
        entry.AllowedValues.Add(value);
        configuration.Renderer.Selectors.Add(entry);

        var result = PluginConfigurationValidator.Validate(configuration);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.Contains("blank", StringComparison.Ordinal));
    }

    [Fact]
    public void ValidatorRejectsControlCharacterAllowedValue()
    {
        var configuration = new PluginConfiguration();
        var entry = new BadgeSelectorConfiguration { Selector = BadgeSelector.Quality };
        entry.AllowedValues.Add("SD\u0007R");
        configuration.Renderer.Selectors.Add(entry);

        var result = PluginConfigurationValidator.Validate(configuration);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.Contains("control characters", StringComparison.Ordinal));
    }

    [Fact]
    public void ValidatorRejectsCaseInsensitiveDuplicateAllowedValues()
    {
        var configuration = new PluginConfiguration();
        var entry = new BadgeSelectorConfiguration { Selector = BadgeSelector.Quality };
        entry.AllowedValues.Add("SDR");
        entry.AllowedValues.Add("sdr");
        configuration.Renderer.Selectors.Add(entry);

        var result = PluginConfigurationValidator.Validate(configuration);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.Contains("unique", StringComparison.Ordinal));
    }

    [Fact]
    public void AllowedValuesValidationMessagesAreSecretFree()
    {
        const string secret = "sentinel-allowlist-secret-4a9b7e";
        var configuration = new PluginConfiguration();
        var entry = new BadgeSelectorConfiguration { Selector = BadgeSelector.Quality };
        entry.AllowedValues.Add(secret);
        entry.AllowedValues.Add(secret.ToUpperInvariant());
        entry.AllowedValues.Add(secret + new string('x', BadgeDefinition.MaximumAllowedValueLength));
        configuration.Renderer.Selectors.Add(entry);

        var result = PluginConfigurationValidator.Validate(configuration);

        Assert.False(result.IsValid);
        Assert.NotEmpty(result.Errors);
        Assert.All(result.Errors, error =>
        {
            Assert.DoesNotContain(secret, error, StringComparison.Ordinal);
            Assert.DoesNotContain(secret.ToUpperInvariant(), error, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void RendererFingerprintIsSensitiveToAllowedValuesAndNormalizesCaseAndOrder()
    {
        var original = Fingerprint(new PluginConfiguration());

        var restricted = new PluginConfiguration();
        restricted.Renderer.Selectors.Add(AllowedValuesEntry("SDR"));
        Assert.NotEqual(original, Fingerprint(restricted));

        // Case is normalized: matching is case-insensitive.
        var lowerCase = new PluginConfiguration();
        lowerCase.Renderer.Selectors.Add(AllowedValuesEntry("sdr"));
        Assert.Equal(Fingerprint(restricted), Fingerprint(lowerCase));

        // Entry order is normalized: matching is order-independent.
        var ordered = new PluginConfiguration();
        ordered.Renderer.Selectors.Add(AllowedValuesEntry("SDR", "HDR"));
        var reordered = new PluginConfiguration();
        reordered.Renderer.Selectors.Add(AllowedValuesEntry("HDR", "SDR"));
        Assert.Equal(Fingerprint(ordered), Fingerprint(reordered));

        // A different value changes the fingerprint.
        var different = new PluginConfiguration();
        different.Renderer.Selectors.Add(AllowedValuesEntry("HDR"));
        Assert.NotEqual(Fingerprint(restricted), Fingerprint(different));

        // An explicit empty allowlist means no restriction and is identity-neutral.
        var empty = new PluginConfiguration();
        empty.Renderer.Selectors.Add(new BadgeSelectorConfiguration { Selector = BadgeSelector.Quality });
        Assert.Equal(original, Fingerprint(empty));
    }

    [Fact]
    public void SnapshotServiceRetainsLastValidRendererConfigurationOnInvalidAllowlist()
    {
        var initial = new PluginConfiguration();
        var service = new ConfigurationSnapshotService(initial);
        var previousFingerprint = service.Current.RendererConfigurationFingerprint;

        var invalid = new PluginConfiguration();
        var entry = new BadgeSelectorConfiguration { Selector = BadgeSelector.Quality };
        entry.AllowedValues.Add("SDR");
        entry.AllowedValues.Add("sdr");
        invalid.Renderer.Selectors.Add(entry);

        Assert.False(service.TryReplace(invalid, out var result));
        Assert.False(result.IsValid);
        Assert.Equal(previousFingerprint, service.Current.RendererConfigurationFingerprint);
    }

    [Fact]
    public void InvalidInitialRendererConfigurationFallsBackToDefaults()
    {
        var invalid = new PluginConfiguration();
        invalid.Renderer.TechnicalBackground = "#111827";
        invalid.Renderer.TechnicalText = "#111827";

        var service = new ConfigurationSnapshotService(invalid);

        Assert.Equal(RenderOutputPolicy.Default.TechnicalText, service.Current.RendererOutputPolicy.TechnicalText);
        Assert.Equal(BadgeDefinition.V1Default.Count, service.Current.BadgeDefinitions.Count);
    }

    [Fact]
    public void RendererConfigurationRoundTripsThroughXmlSerializer()
    {
        var configuration = new PluginConfiguration();
        var source = new BadgeSelectorConfiguration
        {
            Selector = BadgeSelector.Source,
            Enabled = false,
            Template = "SRC:{value}",
        };
        source.AllowedValues.Add("WEB-DL");
        source.AllowedValues.Add("Blu-ray");
        configuration.Renderer.Selectors.Add(source);
        configuration.Renderer.TechnicalBackground = "#000000";

        var serializer = new XmlSerializer(typeof(PluginConfiguration));
        using var writer = new StringWriter();
        serializer.Serialize(writer, configuration);

        using var reader = new StringReader(writer.ToString());
        var restored = (PluginConfiguration)serializer.Deserialize(reader)!;

        var entry = Assert.Single(restored.Renderer.Selectors);
        Assert.Equal(BadgeSelector.Source, entry.Selector);
        Assert.False(entry.Enabled);
        Assert.Equal("SRC:{value}", entry.Template);
        Assert.Equal(new[] { "WEB-DL", "Blu-ray" }, entry.AllowedValues);
        Assert.Equal("#000000", restored.Renderer.TechnicalBackground);
    }

    private static BadgeSelectorConfiguration AllowedValuesEntry(params string[] values)
    {
        var entry = new BadgeSelectorConfiguration { Selector = BadgeSelector.Quality };
        foreach (var value in values)
        {
            entry.AllowedValues.Add(value);
        }

        return entry;
    }

    private static string Fingerprint(PluginConfiguration configuration)
    {
        return PluginConfigurationSnapshot.From(configuration).RendererConfigurationFingerprint;
    }
}
