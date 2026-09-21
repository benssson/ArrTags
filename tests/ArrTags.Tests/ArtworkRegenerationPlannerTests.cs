using System;
using System.Text;
using ArrTags.Artwork;
using ArrTags.Configuration;
using ArrTags.Metadata;
using ArrTags.Reconciliation;
using ArrTags.Rendering;
using Xunit;

namespace ArrTags.Tests;

/// <summary>
/// Task 6.6 focused checks for the publication-fingerprint gate. The planner is a
/// pure decision over authoritative state, so each output-affecting input is
/// exercised directly without a host, a renderer, or the native Skia runtime.
/// </summary>
public sealed class ArtworkRegenerationPlannerTests
{
    private static readonly ArtworkImageSurface Surface = ArtworkImageSurface.Primary;
    private static readonly DateTimeOffset At = DateTimeOffset.UnixEpoch.AddDays(1);

    private static readonly string ConfigurationFingerprint =
        PluginConfigurationSnapshot.From(new PluginConfiguration()).RendererConfigurationFingerprint;

    private static readonly string PlaceholderFingerprint = new('A', 64);

    [Fact]
    public void UnchangedFingerprintSkipsWithoutGenerating()
    {
        var identity = RenderTestFixtures.BuildMovieIdentity();
        var metadata = RenderTestFixtures.BuildMetadata();

        var desired = ArtworkRegenerationPlanner.ComputeDesiredFingerprint(
            PublishedState(SourceA(), PlaceholderFingerprint),
            identity,
            metadata,
            BadgeDefinition.V1Default,
            RenderOutputPolicy.Default,
            ConfigurationFingerprint,
            RenderVersion.CurrentRendererVersion,
            RenderVersion.CurrentBadgeSchemaVersion);

        var decision = Decide(PublishedState(SourceA(), desired), metadata);

        Assert.False(decision.ShouldGenerate);
        Assert.Null(decision.DesiredFingerprint);
    }

    [Fact]
    public void ChangedMetadataFingerprintGenerates()
    {
        var identity = RenderTestFixtures.BuildMovieIdentity();
        var first = RenderTestFixtures.BuildMetadata();
        var second = RenderTestFixtures.BuildMetadata(qualityLabel: "Bluray-2160p");

        var fingerprint = ArtworkRegenerationPlanner.ComputeDesiredFingerprint(
            PublishedState(SourceA(), PlaceholderFingerprint),
            identity,
            first,
            BadgeDefinition.V1Default,
            RenderOutputPolicy.Default,
            ConfigurationFingerprint,
            RenderVersion.CurrentRendererVersion,
            RenderVersion.CurrentBadgeSchemaVersion);

        var decision = Decide(PublishedState(SourceA(), fingerprint), second);

        Assert.True(decision.ShouldGenerate);
        Assert.NotNull(decision.DesiredFingerprint);
        Assert.NotEqual(fingerprint, decision.DesiredFingerprint);
    }

    [Fact]
    public void ChangedConfigurationFingerprintGenerates()
    {
        var identity = RenderTestFixtures.BuildMovieIdentity();
        var metadata = RenderTestFixtures.BuildMetadata();

        var fingerprint = ArtworkRegenerationPlanner.ComputeDesiredFingerprint(
            PublishedState(SourceA(), PlaceholderFingerprint),
            identity,
            metadata,
            BadgeDefinition.V1Default,
            RenderOutputPolicy.Default,
            ConfigurationFingerprint,
            RenderVersion.CurrentRendererVersion,
            RenderVersion.CurrentBadgeSchemaVersion);

        var decision = ArtworkRegenerationPlanner.Decide(
            PublishedState(SourceA(), fingerprint),
            identity,
            metadata,
            metadataUsable: true,
            BadgeDefinition.V1Default,
            RenderOutputPolicy.Default,
            new string('B', 64),
            RenderVersion.CurrentRendererVersion,
            RenderVersion.CurrentBadgeSchemaVersion);

        Assert.True(decision.ShouldGenerate);
    }

    [Fact]
    public void ChangedRendererVersionGenerates()
    {
        var identity = RenderTestFixtures.BuildMovieIdentity();
        var metadata = RenderTestFixtures.BuildMetadata();

        var fingerprint = ArtworkRegenerationPlanner.ComputeDesiredFingerprint(
            PublishedState(SourceA(), PlaceholderFingerprint),
            identity,
            metadata,
            BadgeDefinition.V1Default,
            RenderOutputPolicy.Default,
            ConfigurationFingerprint,
            RenderVersion.CurrentRendererVersion,
            RenderVersion.CurrentBadgeSchemaVersion);

        var decision = ArtworkRegenerationPlanner.Decide(
            PublishedState(SourceA(), fingerprint),
            identity,
            metadata,
            metadataUsable: true,
            BadgeDefinition.V1Default,
            RenderOutputPolicy.Default,
            ConfigurationFingerprint,
            RenderVersion.CurrentRendererVersion + 1,
            RenderVersion.CurrentBadgeSchemaVersion);

        Assert.True(decision.ShouldGenerate);
    }

    [Fact]
    public void ChangedBadgeSchemaVersionGenerates()
    {
        var identity = RenderTestFixtures.BuildMovieIdentity();
        var metadata = RenderTestFixtures.BuildMetadata();

        var fingerprint = ArtworkRegenerationPlanner.ComputeDesiredFingerprint(
            PublishedState(SourceA(), PlaceholderFingerprint),
            identity,
            metadata,
            BadgeDefinition.V1Default,
            RenderOutputPolicy.Default,
            ConfigurationFingerprint,
            RenderVersion.CurrentRendererVersion,
            RenderVersion.CurrentBadgeSchemaVersion);

        var decision = ArtworkRegenerationPlanner.Decide(
            PublishedState(SourceA(), fingerprint),
            identity,
            metadata,
            metadataUsable: true,
            BadgeDefinition.V1Default,
            RenderOutputPolicy.Default,
            ConfigurationFingerprint,
            RenderVersion.CurrentRendererVersion,
            RenderVersion.CurrentBadgeSchemaVersion + 1);

        Assert.True(decision.ShouldGenerate);
    }

    [Fact]
    public void ChangedSourceFingerprintGenerates()
    {
        var identity = RenderTestFixtures.BuildMovieIdentity();
        var metadata = RenderTestFixtures.BuildMetadata();

        var stateWithSourceA = PublishedState(SourceA(), PlaceholderFingerprint);
        var fingerprintA = ArtworkRegenerationPlanner.ComputeDesiredFingerprint(
            stateWithSourceA,
            identity,
            metadata,
            BadgeDefinition.V1Default,
            RenderOutputPolicy.Default,
            ConfigurationFingerprint,
            RenderVersion.CurrentRendererVersion,
            RenderVersion.CurrentBadgeSchemaVersion);

        // A session whose retained baseline differs from the one the active
        // ArrTags output was produced from: the stored publication fingerprint is
        // stale for the current source identity.
        var stateWithSourceB = PublishedState(SourceB(), fingerprintA);
        var fingerprintB = ArtworkRegenerationPlanner.ComputeDesiredFingerprint(
            stateWithSourceB,
            identity,
            metadata,
            BadgeDefinition.V1Default,
            RenderOutputPolicy.Default,
            ConfigurationFingerprint,
            RenderVersion.CurrentRendererVersion,
            RenderVersion.CurrentBadgeSchemaVersion);

        Assert.NotEqual(fingerprintA, fingerprintB);

        var decision = Decide(stateWithSourceB, metadata);

        Assert.True(decision.ShouldGenerate);
        Assert.Equal(fingerprintB, decision.DesiredFingerprint);
    }

    [Fact]
    public void UnusableMetadataIsNotUsedToGenerateOrPublish()
    {
        var metadata = RenderTestFixtures.BuildMetadata();
        var published = PublishedState(SourceA(), PlaceholderFingerprint);

        var decision = ArtworkRegenerationPlanner.Decide(
            published,
            RenderTestFixtures.BuildMovieIdentity(),
            metadata,
            metadataUsable: false,
            BadgeDefinition.V1Default,
            RenderOutputPolicy.Default,
            ConfigurationFingerprint,
            RenderVersion.CurrentRendererVersion,
            RenderVersion.CurrentBadgeSchemaVersion);

        Assert.False(decision.ShouldGenerate);
    }

    [Fact]
    public void ExpiredMetadataEntryIsNotUsableAsCurrent()
    {
        var identity = ReconciliationFixtures.MovieIdentity();
        var match = ReconciliationFixtures.MatchedMovieMatch(identity);
        var entry = MetadataStateEntry.From(
            identity,
            match,
            ReconciliationFixtures.MovieMetadata(identity, match.RecordIdentity),
            DateTimeOffset.UtcNow.AddDays(-3),
            staleWindow: TimeSpan.FromMinutes(60));

        Assert.False(entry.IsUsableAsCurrent(DateTimeOffset.UtcNow));
        Assert.Equal(MetadataFreshness.Expired, entry.EvaluateFreshness(DateTimeOffset.UtcNow));
    }

    [Fact]
    public void BlockedOwnershipStateIsNotAutomaticallyRegenerated()
    {
        var blocked = new PublishedArtworkState(
            RenderTestFixtures.ItemId,
            Surface,
            ArtworkPublicationState.OwnershipLost,
            At);

        var decision = Decide(blocked, RenderTestFixtures.BuildMetadata());

        Assert.False(decision.ShouldGenerate);
    }

    [Fact]
    public void NoSessionRequiresANewBaseline()
    {
        var decision = Decide(null, RenderTestFixtures.BuildMetadata());

        Assert.True(decision.ShouldGenerate);
        Assert.Null(decision.DesiredFingerprint);
    }

    [Fact]
    public void CapturedNotPublishedBaselineIsReadyToPublish()
    {
        var identity = RenderTestFixtures.BuildMovieIdentity();
        var capture = new ActiveImageIdentity(
            Surface,
            ArtworkImagePresence.Present,
            ArtworkHashes.ComputeSha256(SourceA()),
            SourceA().Length,
            100,
            150,
            At,
            "tag");
        var session = PublishedArtworkStateTransitions
            .CaptureSession(
                null,
                RenderTestFixtures.ItemId,
                Surface,
                capture,
                new SourceArtifactInfo(capture.ContentSha256!, "image/png", SourceA().Length, capture.ContentSha256!),
                At)
            .State;

        var decision = Decide(session, RenderTestFixtures.BuildMetadata());

        Assert.True(decision.ShouldGenerate);
    }

    [Fact]
    public void AbsentBaselineIsNotRenderedFromTheActiveSurface()
    {
        var absent = new PublishedArtworkState(
            RenderTestFixtures.ItemId,
            Surface,
            ArtworkPublicationState.Published,
            At,
            sourcePresence: ArtworkImagePresence.Absent);

        var decision = Decide(absent, RenderTestFixtures.BuildMetadata());

        Assert.False(decision.ShouldGenerate);
    }

    private static ArtworkRegenerationDecision Decide(PublishedArtworkState? state, BadgeMetadata metadata)
    {
        return ArtworkRegenerationPlanner.Decide(
            state,
            RenderTestFixtures.BuildMovieIdentity(),
            metadata,
            metadataUsable: true,
            BadgeDefinition.V1Default,
            RenderOutputPolicy.Default,
            ConfigurationFingerprint,
            RenderVersion.CurrentRendererVersion,
            RenderVersion.CurrentBadgeSchemaVersion);
    }

    private static PublishedArtworkState PublishedState(byte[] sourceBytes, string publishedFingerprint)
    {
        var sha = ArtworkHashes.ComputeSha256(sourceBytes);
        var capture = new ActiveImageIdentity(
            Surface,
            ArtworkImagePresence.Present,
            sha,
            sourceBytes.Length,
            100,
            150,
            At,
            "source-tag");
        var artifact = new SourceArtifactInfo(sha, "image/png", sourceBytes.Length, sha);
        var session = PublishedArtworkStateTransitions
            .CaptureSession(null, RenderTestFixtures.ItemId, Surface, capture, artifact, At)
            .State;
        var active = new ActiveImageIdentity(
            Surface,
            ArtworkImagePresence.Present,
            ArtworkHashes.ComputeSha256(Encoding.UTF8.GetBytes("active-derived")),
            Encoding.UTF8.GetBytes("active-derived").Length,
            100,
            150,
            At,
            "active-tag");

        return PublishedArtworkStateTransitions
            .CommitPublication(session, active, publishedFingerprint, RenderVersion.CurrentRendererVersion, At)
            .State;
    }

    private static byte[] SourceA()
    {
        return Encoding.UTF8.GetBytes("original-source-a");
    }

    private static byte[] SourceB()
    {
        return Encoding.UTF8.GetBytes("original-source-b");
    }
}
