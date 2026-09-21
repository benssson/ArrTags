using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using ArrTags.Artwork;
using ArrTags.Configuration;
using ArrTags.Media;
using ArrTags.Rendering;
using Xunit;

namespace ArrTags.Tests;

/// <summary>
/// Phase 5 task 5.10 checks for the resolved DG-8 Jellyfin Enhanced coexistence
/// policy (ADR-011). They assert that the ArrTags production assembly has no
/// Jellyfin Enhanced reference, that the renderer and publication surface expose
/// no Enhanced, spoiler, hidden, or duplicate/overlap suppression branch, that
/// badge output is controlled only by the existing ArrTags configuration, and
/// that the renderer decision is a pure function of the canonical render request.
/// They run without a live Jellyfin host, a live Enhanced install, or the Skia
/// native runtime.
/// </summary>
public class EnhancedCoexistenceTests
{
    private static readonly Assembly ProductionAssembly = typeof(Plugin).Assembly;

    private static readonly string[] ForbiddenNameTokens =
    {
        "enhanced",
        "jellyfinenhanced",
        "spoiler",
        "suppress",
        "blur",
        "hidden",
        "overlap",
    };

    private static readonly Type[] PolicySurfaceTypes =
    {
        typeof(PluginConfiguration),
        typeof(PluginConfigurationSnapshot),
        typeof(RendererConfiguration),
        typeof(MediaEligibility),
        typeof(BadgeSelectorResolver),
        typeof(RenderRequest),
        typeof(RenderFingerprintInput),
        typeof(RenderOutputPolicy),
        typeof(ArtworkGenerationRequest),
        typeof(ArtworkPublicationRequest),
    };

    private static readonly Type[] PolicyEnums =
    {
        typeof(RenderPassThroughReason),
        typeof(RenderFailureReason),
        typeof(ArtworkPublicationOutcome),
        typeof(ArtworkGenerationOutcome),
        typeof(ArtworkReconciliationAction),
        typeof(ArtworkLifecycleFence),
    };

    // ---- Structural: no Jellyfin Enhanced dependency ------------------------

    [Fact]
    public void ProductionAssemblyReferencesNoJellyfinEnhancedAssembly()
    {
        var references = ProductionAssembly.GetReferencedAssemblies();

        Assert.DoesNotContain(
            references,
            reference => ContainsForbiddenToken(reference.Name));
    }

    [Fact]
    public void ProductionAssemblyDeclaresNoEnhancedOrSpoilerType()
    {
        foreach (var type in ProductionAssembly.GetTypes())
        {
            Assert.False(
                ContainsForbiddenToken(type.FullName),
                $"Unexpected Enhanced/spoiler/suppression type '{type.FullName}'.");
        }
    }

    [Fact]
    public void PolicySurfaceExposesNoEnhancedSpoilerOrSuppressionMember()
    {
        foreach (var type in PolicySurfaceTypes)
        {
            foreach (var name in DeclaredNames(type))
            {
                Assert.False(
                    ContainsForbiddenToken(name),
                    $"Unexpected coexistence member '{type.FullName}.{name}'.");
            }
        }
    }

    [Fact]
    public void RendererAndPublicationReasonEnumsHaveNoSuppressionBranch()
    {
        foreach (var enumType in PolicyEnums)
        {
            foreach (var name in Enum.GetNames(enumType))
            {
                Assert.False(
                    ContainsForbiddenToken(name),
                    $"Unexpected suppression reason '{enumType.Name}.{name}'.");
            }
        }
    }

    // ---- Policy/behavior: output controlled only by ArrTags configuration ----

    [Fact]
    public void BadgeSurfaceEligibilityIsControlledOnlyByConfiguredPosterFlags()
    {
        var movie = LocalIdentity(MediaItemType.Movie);
        var episode = LocalIdentity(MediaItemType.Episode);
        var series = LocalIdentity(MediaItemType.Series);
        var season = LocalIdentity(MediaItemType.Season);

        var bothEnabled = Snapshot();
        Assert.True(MediaEligibility.IsBadgeSurface(movie, bothEnabled));
        Assert.True(MediaEligibility.IsBadgeSurface(episode, bothEnabled));
        Assert.False(MediaEligibility.IsBadgeSurface(series, bothEnabled));
        Assert.False(MediaEligibility.IsBadgeSurface(season, bothEnabled));

        var moviesDisabled = Snapshot(moviePosters: false);
        Assert.False(MediaEligibility.IsBadgeSurface(movie, moviesDisabled));
        Assert.True(MediaEligibility.IsBadgeSurface(episode, moviesDisabled));

        var episodesDisabled = Snapshot(episodePosters: false);
        Assert.True(MediaEligibility.IsBadgeSurface(movie, episodesDisabled));
        Assert.False(MediaEligibility.IsBadgeSurface(episode, episodesDisabled));

        var allDisabled = Snapshot(moviePosters: false, episodePosters: false);
        Assert.False(MediaEligibility.IsBadgeSurface(movie, allDisabled));
        Assert.False(MediaEligibility.IsBadgeSurface(episode, allDisabled));

        // The combined gate follows the same poster flags and no other input.
        Assert.True(MediaEligibility.IsEligible(movie, bothEnabled));
        Assert.True(MediaEligibility.IsEligible(episode, bothEnabled));
        Assert.False(MediaEligibility.IsEligible(movie, moviesDisabled));
        Assert.False(MediaEligibility.IsEligible(episode, episodesDisabled));
    }

    [Fact]
    public void BadgeOutputIsControlledOnlyByRendererSelectorEnablement()
    {
        var metadata = RenderTestFixtures.BuildMetadata();

        var defaults = Snapshot();
        var defaultSelection = BadgeSelectorResolver.Resolve(metadata, EnabledSelectors(defaults));
        Assert.Contains(defaultSelection.TechnicalValues, value => value.Selector == BadgeSelector.Quality);
        Assert.Contains(defaultSelection.TechnicalValues, value => value.Selector == BadgeSelector.Source);

        var qualityDisabled = Snapshot(selectorOverrides: new[] { (BadgeSelector.Quality, false) });
        var withoutQuality = BadgeSelectorResolver.Resolve(metadata, EnabledSelectors(qualityDisabled));
        Assert.DoesNotContain(withoutQuality.TechnicalValues, value => value.Selector == BadgeSelector.Quality);
        Assert.Contains(withoutQuality.TechnicalValues, value => value.Selector == BadgeSelector.Source);

        var allDisabled = Snapshot(selectorOverrides: Enum
            .GetValues<BadgeSelector>()
            .Select(selector => (selector, false))
            .ToArray());
        var empty = BadgeSelectorResolver.Resolve(metadata, EnabledSelectors(allDisabled));
        Assert.True(empty.IsEmpty);
    }

    // ---- Policy/behavior: spoiler state cannot alter renderer output ---------

    [Fact]
    public async Task RendererDecisionIsAPureFunctionWithNoSpoilerOrHiddenBranch()
    {
        // ArrTags receives no spoiler/hidden input (asserted structurally above),
        // so the honest behavioral check is that the renderer decision depends
        // only on the canonical render request and is stable across repeated
        // evaluation with no hidden global state.
        IRenderer renderer = new SkiaBadgeRenderer();

        var unavailable = RenderTestFixtures.BuildRequest(
            source: null,
            metadata: RenderTestFixtures.BuildMetadata());
        var unavailableFirst = await renderer.RenderAsync(unavailable, CancellationToken.None);
        var unavailableSecond = await renderer.RenderAsync(unavailable, CancellationToken.None);

        Assert.Equal(RenderStatus.PassThrough, unavailableFirst.Status);
        Assert.Equal(RenderPassThroughReason.SourceUnavailable, unavailableFirst.PassThroughReason);
        Assert.Equal(unavailableFirst.Status, unavailableSecond.Status);
        Assert.Equal(unavailableFirst.PassThroughReason, unavailableSecond.PassThroughReason);

        var noMetadata = RenderTestFixtures.BuildRequest(
            source: Source(),
            metadata: null);
        var noMetadataFirst = await renderer.RenderAsync(noMetadata, CancellationToken.None);
        var noMetadataSecond = await renderer.RenderAsync(noMetadata, CancellationToken.None);

        Assert.Equal(RenderStatus.PassThrough, noMetadataFirst.Status);
        Assert.Equal(RenderPassThroughReason.NoMetadata, noMetadataFirst.PassThroughReason);
        Assert.Equal(noMetadataFirst.Status, noMetadataSecond.Status);
        Assert.Equal(noMetadataFirst.PassThroughReason, noMetadataSecond.PassThroughReason);

        var series = new MediaIdentity(RenderTestFixtures.ItemId, MediaItemType.Series);
        var ineligible = RenderTestFixtures.BuildRequest(
            Source(),
            identity: series,
            match: RenderTestFixtures.BuildMatch(series),
            metadata: RenderTestFixtures.BuildMetadata());
        var ineligibleResult = await renderer.RenderAsync(ineligible, CancellationToken.None);

        Assert.Equal(RenderStatus.PassThrough, ineligibleResult.Status);
        Assert.Equal(RenderPassThroughReason.IneligibleSurface, ineligibleResult.PassThroughReason);
    }

    // ---- Helpers -------------------------------------------------------------

    private static bool ContainsForbiddenToken(string? name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return false;
        }

        foreach (var token in ForbiddenNameTokens)
        {
            if (name.Contains(token, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static IEnumerable<string> DeclaredNames(Type type)
    {
        const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic
            | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

        foreach (var field in type.GetFields(flags))
        {
            yield return field.Name;
        }

        foreach (var property in type.GetProperties(flags))
        {
            yield return property.Name;
        }

        foreach (var eventInfo in type.GetEvents(flags))
        {
            yield return eventInfo.Name;
        }

        foreach (var nested in type.GetNestedTypes(flags))
        {
            yield return nested.Name;
        }

        foreach (var method in type.GetMethods(flags))
        {
            yield return method.Name;
            foreach (var parameter in method.GetParameters())
            {
                yield return parameter.Name ?? string.Empty;
            }
        }

        foreach (var constructor in type.GetConstructors(flags))
        {
            foreach (var parameter in constructor.GetParameters())
            {
                yield return parameter.Name ?? string.Empty;
            }
        }
    }

    private static PluginConfigurationSnapshot Snapshot(
        bool moviePosters = true,
        bool episodePosters = true,
        params (BadgeSelector Selector, bool Enabled)[] selectorOverrides)
    {
        var configuration = new PluginConfiguration
        {
            BadgeMoviePosters = moviePosters,
            BadgeEpisodePosters = episodePosters,
        };

        foreach (var (selector, enabled) in selectorOverrides)
        {
            configuration.Renderer.Selectors.Add(new BadgeSelectorConfiguration
            {
                Selector = selector,
                Enabled = enabled,
            });
        }

        Assert.True(PluginConfigurationValidator.Validate(configuration).IsValid);
        return PluginConfigurationSnapshot.From(configuration);
    }

    private static IReadOnlySet<BadgeSelector> EnabledSelectors(PluginConfigurationSnapshot snapshot)
    {
        return snapshot.BadgeDefinitions
            .Where(definition => definition.Enabled)
            .Select(definition => definition.Selector)
            .ToHashSet();
    }

    private static MediaIdentity LocalIdentity(MediaItemType itemType)
    {
        return new MediaIdentity(
            Guid.NewGuid(),
            itemType,
            mediaLocation: new MediaLocationSummary(
                MediaLocationKind.FileSystem,
                isFileProtocol: true,
                mediaSourceCount: 1));
    }

    private static SourceImageInput Source()
    {
        var bytes = new byte[] { 1, 2, 3, 4 };
        return new SourceImageInput(
            bytes,
            "image/png",
            100,
            100,
            SourceImageInput.ComputeSha256(bytes));
    }
}
