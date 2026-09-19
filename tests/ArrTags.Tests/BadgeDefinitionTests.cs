using System;
using System.Collections.Generic;
using System.Linq;
using ArrTags.Metadata;
using ArrTags.Rendering;
using Xunit;

namespace ArrTags.Tests;

/// <summary>
/// Phase 4 task 4.9 checks for the provider-neutral badge definitions and their
/// template application. The V1 default set must match ADR-009, disabled
/// selectors must not render, and a bounded template must be validated.
/// </summary>
public class BadgeDefinitionTests
{
    [Fact]
    public void V1DefaultsEnableEverySelectorWithTheAdr009Templates()
    {
        var definitions = BadgeDefinition.V1Default;

        Assert.Equal(Enum.GetValues<BadgeSelector>().Length, definitions.Count);
        Assert.All(definitions, definition => Assert.True(definition.Enabled));
        Assert.Equal(
            Enum.GetValues<BadgeSelector>().OrderBy(selector => selector),
            definitions.Select(definition => definition.Selector).OrderBy(selector => selector));

        foreach (var definition in definitions)
        {
            var expected = definition.Selector == BadgeSelector.UpgradePending
                ? BadgeSelectorResolver.UpgradeStatusText
                : BadgeDefinition.ValuePlaceholder;
            Assert.Equal(expected, definition.Template);
        }
    }

    [Fact]
    public void DefaultResolverProducesTheAdr009Selection()
    {
        var metadata = RenderTestFixtures.BuildMetadata(upgradePending: true);

        var selection = BadgeDefinitionResolver.Resolve(metadata, BadgeDefinition.V1Default);

        Assert.Contains(selection.TechnicalValues, value => value.Selector == BadgeSelector.Quality);
        Assert.NotNull(selection.StatusValue);
        Assert.Equal(BadgeSelectorResolver.UpgradeStatusText, selection.StatusValue!.Text);
    }

    [Fact]
    public void DisabledSelectorIsOmitted()
    {
        var definitions = new[]
        {
            new BadgeDefinition(BadgeSelector.Quality, false, BadgeDefinition.ValuePlaceholder),
        };
        var metadata = RenderTestFixtures.BuildMetadata();

        var selection = BadgeDefinitionResolver.Resolve(metadata, definitions);

        Assert.True(selection.IsEmpty);
    }

    [Fact]
    public void BoundedTemplateIsAppliedToConfirmedValues()
    {
        var definitions = new[]
        {
            new BadgeDefinition(BadgeSelector.Quality, true, "Q:{value}"),
        };
        var metadata = RenderTestFixtures.BuildMetadata();

        var selection = BadgeDefinitionResolver.Resolve(metadata, definitions);

        var value = Assert.Single(selection.TechnicalValues);
        Assert.Equal("Q:Bluray-1080p", value.Text);
    }

    [Fact]
    public void FirstDefinitionForASelectorWins()
    {
        var definitions = new[]
        {
            new BadgeDefinition(BadgeSelector.Quality, true, "FIRST"),
            new BadgeDefinition(BadgeSelector.Quality, true, "SECOND"),
        };
        var metadata = RenderTestFixtures.BuildMetadata();

        var selection = BadgeDefinitionResolver.Resolve(metadata, definitions);

        Assert.Equal("FIRST", Assert.Single(selection.TechnicalValues).Text);
    }

    [Fact]
    public void NullMetadataResolvesToEmptySelection()
    {
        Assert.True(BadgeDefinitionResolver.Resolve(null, BadgeDefinition.V1Default).IsEmpty);
    }

    [Fact]
    public void DefinitionValidationRejectsUnusableTemplates()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new BadgeDefinition((BadgeSelector)999, true, BadgeDefinition.ValuePlaceholder));
        Assert.Throws<ArgumentException>(
            () => new BadgeDefinition(BadgeSelector.Quality, true, string.Empty));
        Assert.Throws<ArgumentException>(
            () => new BadgeDefinition(BadgeSelector.Quality, true, new string('a', BadgeDefinition.MaximumTemplateLength + 1)));
        Assert.Throws<ArgumentException>(
            () => new BadgeDefinition(BadgeSelector.Quality, true, "{value}-{value}"));
    }

    [Fact]
    public void ResolverRejectsNullDefinitions()
    {
        Assert.Throws<ArgumentNullException>(
            () => BadgeDefinitionResolver.Resolve(null, null!));
        Assert.Throws<ArgumentNullException>(
            () => BadgeDefinitionResolver.Resolve(null, new List<BadgeDefinition> { null! }));
    }
}
