using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ArrTags.Artwork;
using ArrTags.Configuration;
using ArrTags.Metadata;
using ArrTags.Rendering;
using ArrTags.State;
using Xunit;

namespace ArrTags.Tests;

/// <summary>
/// The Phase 4 configuration-to-render integration test carried into Phase 6
/// (phase-5 review MEDIUM "Test coverage carried forward from Phase 4"). It builds
/// a <see cref="PluginConfiguration"/> with an overridden palette and a disabled
/// selector, resolves it through <see cref="PluginConfigurationSnapshot"/> /
/// <see cref="RendererConfigurationResolver"/>, and drives the real
/// <see cref="ArtworkGenerationCoordinator"/> with an in-memory fingerprinting
/// renderer. The assertions are on the exact renderer input and the resulting
/// output fingerprint, so no native Skia runtime is required.
/// </summary>
public sealed class RendererConfigurationIntegrationTests : IDisposable
{
    private static readonly ArtworkImageSurface Surface = ArtworkImageSurface.Primary;

    private readonly string _root;
    private readonly StateRepository _repository;
    private readonly SourceArtifactStore _artifacts;
    private readonly PublishedArtworkStateStore _states;
    private readonly ArtworkOperationStore _operations;
    private readonly PipelineArtworkHost _host = new();
    private readonly FingerprintingRenderer _renderer = new();
    private readonly ArtworkGenerationCoordinator _coordinator;

    public RendererConfigurationIntegrationTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "arrtags-config-render-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _repository = new StateRepository(_root);
        _artifacts = new SourceArtifactStore(_repository);
        _states = new PublishedArtworkStateStore(_repository);
        _operations = new ArtworkOperationStore(_repository);
        var publisher = new ArtworkPublisher(_host, _host, _artifacts, _states, _operations, new OperationalLimits());
        _coordinator = new ArtworkGenerationCoordinator(_host, _renderer, publisher, _states, _artifacts);
    }

    [Fact]
    public async Task ResolvedConfigurationReachesTheRenderAndChangesTheOutput()
    {
        var configuration = new PluginConfiguration
        {
            BadgeMoviePosters = true,
            Radarr = new ArrConnectionConfiguration
            {
                Enabled = true,
                BaseUrl = "http://radarr.test",
                ApiKey = "test-radarr-api-key",
            },
        };
        configuration.Renderer.TechnicalBackground = "#000000";
        configuration.Renderer.Selectors.Add(new BadgeSelectorConfiguration
        {
            Selector = BadgeSelector.Quality,
            Enabled = false,
        });

        Assert.True(PluginConfigurationValidator.Validate(configuration).IsValid);

        var snapshot = PluginConfigurationSnapshot.From(configuration);

        // The resolved snapshot is not the code-owned default.
        Assert.Equal("#000000", snapshot.RendererOutputPolicy.TechnicalBackground);
        Assert.NotEqual(RenderOutputPolicy.Default.TechnicalBackground, snapshot.RendererOutputPolicy.TechnicalBackground);
        Assert.False(snapshot.BadgeDefinitions.Single(definition => definition.Selector == BadgeSelector.Quality).Enabled);
        Assert.True(snapshot.BadgeDefinitions.Single(definition => definition.Selector == BadgeSelector.Source).Enabled);
        Assert.NotEqual(
            RendererConfigurationResolver.ComputeFingerprint(BadgeDefinition.V1Default, RenderOutputPolicy.Default),
            snapshot.RendererConfigurationFingerprint);

        var identity = RenderTestFixtures.BuildMovieIdentity();
        var match = RenderTestFixtures.BuildMatch(identity);
        var metadata = RenderTestFixtures.BuildMetadata(qualityLabel: "Bluray-1080p");
        var source = Encoding.UTF8.GetBytes("integration-source-poster");
        _host.CurrentBytes = source;

        var request = new ArtworkGenerationRequest(
            identity.JellyfinItemId,
            Surface,
            identity,
            match,
            metadata,
            snapshot.BadgeDefinitions,
            snapshot.RendererConfigurationFingerprint,
            snapshot.RendererOutputPolicy,
            snapshot.Limits);

        var result = await _coordinator.GenerateAsync(request, CancellationToken.None);

        Assert.Equal(ArtworkGenerationOutcome.Published, result.Outcome);

        var renderRequest = _renderer.LastRequest;
        Assert.NotNull(renderRequest);

        // The renderer received the resolved definitions and policy, not the defaults.
        Assert.Equal("#000000", renderRequest!.OutputPolicy.TechnicalBackground);
        Assert.Equal(snapshot.RendererConfigurationFingerprint, renderRequest.ConfigurationFingerprint);
        Assert.False(renderRequest.BadgeDefinitions.Single(definition => definition.Selector == BadgeSelector.Quality).Enabled);

        var selection = BadgeDefinitionResolver.Resolve(renderRequest.Metadata, renderRequest.BadgeDefinitions);
        Assert.DoesNotContain(selection.TechnicalValues, value => value.Selector == BadgeSelector.Quality);
        Assert.Contains(selection.TechnicalValues, value => value.Selector == BadgeSelector.Resolution);

        // The published output fingerprint reflects the resolved configuration and
        // differs from the default-configuration fingerprint.
        var expected = RenderFingerprint.ComputeOutputFingerprint(new RenderFingerprintInput(
            identity.JellyfinItemId,
            identity.ItemType,
            ArtworkHashes.ComputeSha256(source),
            renderRequest.SourceImage!.OrientedWidth,
            renderRequest.SourceImage.OrientedHeight,
            metadata.MetadataFingerprint,
            snapshot.RendererConfigurationFingerprint,
            selection,
            snapshot.RendererOutputPolicy,
            RenderVersion.CurrentRendererVersion,
            RenderVersion.CurrentBadgeSchemaVersion));

        Assert.Equal(expected, result.State!.PublishedFingerprint);

        var defaultSelection = BadgeDefinitionResolver.Resolve(metadata, BadgeDefinition.V1Default);
        var defaultFingerprint = RenderFingerprint.ComputeOutputFingerprint(new RenderFingerprintInput(
            identity.JellyfinItemId,
            identity.ItemType,
            ArtworkHashes.ComputeSha256(source),
            renderRequest.SourceImage.OrientedWidth,
            renderRequest.SourceImage.OrientedHeight,
            metadata.MetadataFingerprint,
            RendererConfigurationResolver.ComputeFingerprint(BadgeDefinition.V1Default, RenderOutputPolicy.Default),
            defaultSelection,
            RenderOutputPolicy.Default,
            RenderVersion.CurrentRendererVersion,
            RenderVersion.CurrentBadgeSchemaVersion));

        Assert.NotEqual(defaultFingerprint, expected);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
