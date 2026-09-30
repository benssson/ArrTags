using System;
using ArrTags.Artwork;
using Xunit;

namespace ArrTags.Tests;

/// <summary>
/// Focused checks for the guarded artwork ownership model: the fail-closed
/// identity comparison, the persisted-state invariants, and every documented
/// logical transition in <c>docs/data-model/03-10-artworkcacheentry.md</c> section 3.10.2. These tests
/// require no live Jellyfin or Arr instance and perform no image mutation.
/// </summary>
public sealed class ArtworkProvenanceTests
{
    private static readonly Guid Item = Guid.Parse("11111111-2222-3333-4444-555555555555");
    private static readonly ArtworkImageSurface Surface = ArtworkImageSurface.Primary;
    private static readonly ArtworkImageSurface IndexedSurface = new(ArtworkImageType.Primary, 1);
    private static readonly DateTimeOffset At = DateTimeOffset.UnixEpoch.AddDays(10);

    // ---- Ownership comparison -------------------------------------------------

    [Fact]
    public void CompareReturnsOwnedForMatchingPresentIdentity()
    {
        var expected = ArtworkStateFixtures.Present(Surface, "same");

        var result = ArtworkOwnershipComparer.Compare(expected, ArtworkStateFixtures.Present(Surface, "same"), At);

        Assert.Equal(ArtworkOwnershipStatus.Owned, result.Status);
    }

    [Fact]
    public void CompareReturnsOwnedForMatchingAbsentIdentity()
    {
        var expected = ActiveImageIdentity.Absent(Surface);

        var result = ArtworkOwnershipComparer.Compare(expected, ActiveImageIdentity.Absent(Surface), At);

        Assert.Equal(ArtworkOwnershipStatus.Owned, result.Status);
    }

    [Fact]
    public void CompareReturnsChangedWhenContentHashDiffers()
    {
        var expected = ArtworkStateFixtures.Present(Surface, "one");

        var result = ArtworkOwnershipComparer.Compare(expected, ArtworkStateFixtures.Present(Surface, "two"), At);

        Assert.Equal(ArtworkOwnershipStatus.Changed, result.Status);
    }

    [Fact]
    public void CompareReturnsChangedWhenPresenceDiffers()
    {
        var expected = ArtworkStateFixtures.Present(Surface, "one");

        var result = ArtworkOwnershipComparer.Compare(expected, ActiveImageIdentity.Absent(Surface), At);

        Assert.Equal(ArtworkOwnershipStatus.Changed, result.Status);
    }

    [Fact]
    public void CompareReturnsUnknownWhenObservationIsUnavailable()
    {
        var expected = ArtworkStateFixtures.Present(Surface, "one");

        var result = ArtworkOwnershipComparer.Compare(expected, null, At);

        Assert.Equal(ArtworkOwnershipStatus.Unknown, result.Status);
        Assert.Null(result.Observed);
    }

    [Fact]
    public void CompareReturnsUnknownWhenSurfaceDiffers()
    {
        var expected = ArtworkStateFixtures.Present(Surface, "one");
        var observed = ArtworkStateFixtures.Present(IndexedSurface, "one");

        var result = ArtworkOwnershipComparer.Compare(expected, observed, At);

        Assert.Equal(ArtworkOwnershipStatus.Unknown, result.Status);
    }

    [Fact]
    public void CompareReturnsUnknownWhenExpectedHashIsMissing()
    {
        var expected = new ActiveImageIdentity(Surface, ArtworkImagePresence.Present);
        var observed = ArtworkStateFixtures.Present(Surface, "one");

        var result = ArtworkOwnershipComparer.Compare(expected, observed, At);

        Assert.Equal(ArtworkOwnershipStatus.Unknown, result.Status);
    }

    [Fact]
    public void CompareReturnsUnknownWhenObservedHashIsMissing()
    {
        var expected = ArtworkStateFixtures.Present(Surface, "one");
        var observed = new ActiveImageIdentity(Surface, ArtworkImagePresence.Present, byteLength: 3);

        var result = ArtworkOwnershipComparer.Compare(expected, observed, At);

        Assert.Equal(ArtworkOwnershipStatus.Unknown, result.Status);
    }

    [Theory]
    [InlineData("byteLength")]
    [InlineData("width")]
    [InlineData("height")]
    [InlineData("dateModifiedUtc")]
    [InlineData("jellyfinImageTag")]
    public void CompareReturnsChangedWhenARecordedSupportingValueDiffers(string field)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes("one");
        var hash = ArtworkHashes.ComputeSha256(bytes);
        var expected = new ActiveImageIdentity(
            Surface,
            ArtworkImagePresence.Present,
            hash,
            byteLength: bytes.Length,
            width: 100,
            height: 150,
            dateModifiedUtc: At,
            jellyfinImageTag: "tag-a");
        var observed = field switch
        {
            "byteLength" => new ActiveImageIdentity(Surface, ArtworkImagePresence.Present, hash, byteLength: bytes.Length + 1, width: 100, height: 150, dateModifiedUtc: At, jellyfinImageTag: "tag-a"),
            "width" => new ActiveImageIdentity(Surface, ArtworkImagePresence.Present, hash, byteLength: bytes.Length, width: 101, height: 150, dateModifiedUtc: At, jellyfinImageTag: "tag-a"),
            "height" => new ActiveImageIdentity(Surface, ArtworkImagePresence.Present, hash, byteLength: bytes.Length, width: 100, height: 151, dateModifiedUtc: At, jellyfinImageTag: "tag-a"),
            "dateModifiedUtc" => new ActiveImageIdentity(Surface, ArtworkImagePresence.Present, hash, byteLength: bytes.Length, width: 100, height: 150, dateModifiedUtc: At.AddSeconds(1), jellyfinImageTag: "tag-a"),
            _ => new ActiveImageIdentity(Surface, ArtworkImagePresence.Present, hash, byteLength: bytes.Length, width: 100, height: 150, dateModifiedUtc: At, jellyfinImageTag: "tag-b"),
        };

        var result = ArtworkOwnershipComparer.Compare(expected, observed, At);

        Assert.Equal(ArtworkOwnershipStatus.Changed, result.Status);
    }

    [Fact]
    public void CompareReturnsOwnedWhenSupportingValuesAreNotObservable()
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes("one");
        var hash = ArtworkHashes.ComputeSha256(bytes);
        var expected = new ActiveImageIdentity(
            Surface,
            ArtworkImagePresence.Present,
            hash,
            byteLength: bytes.Length,
            width: 100,
            height: 150,
            dateModifiedUtc: At,
            jellyfinImageTag: "tag-a");
        var observed = new ActiveImageIdentity(Surface, ArtworkImagePresence.Present, hash);

        var result = ArtworkOwnershipComparer.Compare(expected, observed, At);

        Assert.Equal(ArtworkOwnershipStatus.Owned, result.Status);
    }

    // ---- Persisted state invariants ------------------------------------------

    [Fact]
    public void ValidPublishedStatePassesValidation()
    {
        var state = ArtworkStateFixtures.Published(Item, Surface);

        Assert.True(state.Validate(out var reason), reason);
        Assert.False(state.IsTerminal);
        Assert.Equal(PublishedArtworkState.CurrentModelVersion, state.ModelVersion);
    }

    [Fact]
    public void PublishedStateRequiresActiveIdentity()
    {
        var invalid = Clone(ArtworkStateFixtures.Published(Item, Surface), clearActiveImageIdentity: true);

        Assert.False(invalid.Validate(out _));
    }

    [Fact]
    public void PublishedStateRequiresPublicationToken()
    {
        var invalid = Clone(ArtworkStateFixtures.Published(Item, Surface), clearPublicationToken: true);

        Assert.False(invalid.Validate(out _));
    }

    [Fact]
    public void PublishedStateRequiresSourceBaseline()
    {
        var invalid = Clone(ArtworkStateFixtures.Published(Item, Surface), clearSourcePresence: true);

        Assert.False(invalid.Validate(out _));
    }

    [Fact]
    public void PublishedStateRequiresContentHashOnActiveIdentity()
    {
        var invalid = Clone(
            ArtworkStateFixtures.Published(Item, Surface),
            activeImageIdentity: new ActiveImageIdentity(Surface, ArtworkImagePresence.Present, byteLength: 4));

        Assert.False(invalid.Validate(out _));
    }

    [Fact]
    public void PresentSourceRequiresArtifactAndFingerprint()
    {
        var invalid = Clone(ArtworkStateFixtures.Published(Item, Surface), clearSourceArtifact: true);

        Assert.False(invalid.Validate(out _));
    }

    [Fact]
    public void ModelVersionMismatchIsRejected()
    {
        var invalid = Clone(ArtworkStateFixtures.Published(Item, Surface), modelVersion: 99);

        Assert.False(invalid.Validate(out _));
    }

    [Fact]
    public void PartialPublicationMetadataIsRejected()
    {
        var source = ArtworkStateFixtures.Published(Item, Surface);
        var partial = new PublishedArtworkState(
            Item,
            Surface,
            ArtworkPublicationState.NotPublished,
            At,
            stateRevision: 2,
            sourcePresence: source.SourcePresence,
            sourceArtifactId: source.SourceArtifactId,
            sourceFingerprint: source.SourceFingerprint,
            sourceCaptureIdentity: source.SourceCaptureIdentity,
            ownershipToken: source.OwnershipToken,
            publicationToken: source.PublicationToken);

        Assert.False(partial.Validate(out _));
    }

    [Fact]
    public void MalformedOwnershipTokenIsRejected()
    {
        var invalid = Clone(ArtworkStateFixtures.Published(Item, Surface), ownershipToken: "short");

        Assert.False(invalid.Validate(out _));
    }

    [Fact]
    public void IndexedSurfaceIsRejected()
    {
        var invalid = Clone(ArtworkStateFixtures.Published(Item, Surface), surface: IndexedSurface);

        Assert.False(invalid.Validate(out _));
    }

    [Fact]
    public void EmptyItemIdentifierIsRejected()
    {
        var invalid = Clone(ArtworkStateFixtures.Published(Item, Surface), jellyfinItemId: Guid.Empty);

        Assert.False(invalid.Validate(out _));
    }

    [Fact]
    public void AbsentSourceBaselineRecordsNoArtifact()
    {
        var capture = ActiveImageIdentity.Absent(Surface);
        var session = PublishedArtworkStateTransitions
            .CaptureSession(null, Item, Surface, capture, null, At)
            .State;

        Assert.True(session.Validate(out var reason), reason);
        Assert.Equal(ArtworkImagePresence.Absent, session.SourcePresence);
        Assert.Null(session.SourceArtifactId);
        Assert.Null(session.SourceFingerprint);
    }

    [Fact]
    public void PresentSourceCaptureRequiresMatchingArtifact()
    {
        var capture = ArtworkStateFixtures.Present(Surface, "one");
        var wrongArtifact = ArtworkStateFixtures.Artifact(System.Text.Encoding.UTF8.GetBytes("two"));

        Assert.Throws<ArgumentException>(() => PublishedArtworkStateTransitions.CaptureSession(null, Item, Surface, capture, wrongArtifact, At));
    }

    // ---- Transitions ----------------------------------------------------------

    [Fact]
    public void CaptureSessionCreatesANotPublishedSessionWithANewOwnershipToken()
    {
        var capture = ArtworkStateFixtures.Present(Surface, "source");
        var artifact = ArtworkStateFixtures.Artifact(System.Text.Encoding.UTF8.GetBytes("source"));

        var result = PublishedArtworkStateTransitions.CaptureSession(null, Item, Surface, capture, artifact, At);

        Assert.Equal(ArtworkTransitionAction.None, result.Action);
        Assert.Equal(ArtworkPublicationState.NotPublished, result.State.State);
        Assert.True(ArtworkTokens.IsValid(result.State.OwnershipToken));
        Assert.Equal(capture.ContentSha256, result.State.SourceCaptureIdentity!.ContentSha256);
        Assert.Equal(artifact.ArtifactId, result.State.SourceArtifactId);
        Assert.Equal(1, result.State.StateRevision);
        Assert.True(result.State.Validate(out var reason), reason);
    }

    [Fact]
    public void CaptureSessionRefusesToAutomaticallyRebaselineAPublishedState()
    {
        var published = ArtworkStateFixtures.Published(Item, Surface);
        var capture = ArtworkStateFixtures.Present(Surface, "source");
        var artifact = ArtworkStateFixtures.Artifact(System.Text.Encoding.UTF8.GetBytes("source"));

        var result = PublishedArtworkStateTransitions.CaptureSession(published, Item, Surface, capture, artifact, At);

        Assert.Equal(ArtworkTransitionAction.None, result.Action);
        Assert.Equal(ArtworkPublicationState.Published, result.State.State);
        Assert.Same(published, result.State);
    }

    [Fact]
    public void CaptureSessionRefusesToAutomaticallyRebaselineABlockedState()
    {
        var published = ArtworkStateFixtures.Published(Item, Surface);
        var lost = PublishedArtworkStateTransitions
            .BeginPublication(published, ArtworkStateFixtures.Present(Surface, "changed"), At)
            .State;
        Assert.Equal(ArtworkPublicationState.OwnershipLost, lost.State);

        var capture = ArtworkStateFixtures.Present(Surface, "source");
        var artifact = ArtworkStateFixtures.Artifact(System.Text.Encoding.UTF8.GetBytes("source"));
        var result = PublishedArtworkStateTransitions.CaptureSession(lost, Item, Surface, capture, artifact, At);

        Assert.Equal(ArtworkTransitionAction.None, result.Action);
        Assert.Same(lost, result.State);
    }

    [Fact]
    public void BeginPublicationFromANewSessionAllowsPublishWhenTheBaselineStillMatches()
    {
        var capture = ArtworkStateFixtures.Present(Surface, "source");
        var artifact = ArtworkStateFixtures.Artifact(System.Text.Encoding.UTF8.GetBytes("source"));
        var session = PublishedArtworkStateTransitions.CaptureSession(null, Item, Surface, capture, artifact, At).State;

        var result = PublishedArtworkStateTransitions.BeginPublication(session, capture, At);

        Assert.Equal(ArtworkTransitionAction.PublishDerived, result.Action);
        Assert.True(result.AllowsImageMutation);
        Assert.Equal(ArtworkPublicationState.NotPublished, result.State.State);
    }

    [Fact]
    public void BeginPublicationFromANewSessionRequiresRecaptureWhenTheBaselineChanged()
    {
        var capture = ArtworkStateFixtures.Present(Surface, "source");
        var artifact = ArtworkStateFixtures.Artifact(System.Text.Encoding.UTF8.GetBytes("source"));
        var session = PublishedArtworkStateTransitions.CaptureSession(null, Item, Surface, capture, artifact, At).State;

        var result = PublishedArtworkStateTransitions.BeginPublication(session, ArtworkStateFixtures.Present(Surface, "different"), At);

        Assert.Equal(ArtworkTransitionAction.None, result.Action);
        Assert.Equal(ArtworkPublicationState.NotPublished, result.State.State);
    }

    [Fact]
    public void BeginPublicationFromPublishedAllowsRepublishWhenIdentityMatches()
    {
        var published = ArtworkStateFixtures.Published(Item, Surface);

        var result = PublishedArtworkStateTransitions.BeginPublication(published, published.ActiveImageIdentity, At);

        Assert.Equal(ArtworkTransitionAction.PublishDerived, result.Action);
    }

    [Fact]
    public void BeginPublicationFromPublishedRecordsOwnershipLostOnMismatch()
    {
        var published = ArtworkStateFixtures.Published(Item, Surface);

        var result = PublishedArtworkStateTransitions.BeginPublication(published, ArtworkStateFixtures.Present(Surface, "external"), At);

        Assert.Equal(ArtworkTransitionAction.None, result.Action);
        Assert.Equal(ArtworkPublicationState.OwnershipLost, result.State.State);
        Assert.Equal(published.StateRevision + 1, result.State.StateRevision);
        Assert.True(result.State.Validate(out var reason), reason);
    }

    [Fact]
    public void BeginPublicationFromPublishedRecordsOwnershipUnknownWhenUnobservable()
    {
        var published = ArtworkStateFixtures.Published(Item, Surface);

        var result = PublishedArtworkStateTransitions.BeginPublication(published, null, At);

        Assert.Equal(ArtworkTransitionAction.None, result.Action);
        Assert.Equal(ArtworkPublicationState.OwnershipUnknown, result.State.State);
        Assert.True(result.State.Validate(out var reason), reason);
    }

    [Fact]
    public void BeginPublicationFromABlockedStateIsRefused()
    {
        var published = ArtworkStateFixtures.Published(Item, Surface);
        var unknown = PublishedArtworkStateTransitions.BeginPublication(published, null, At).State;

        var result = PublishedArtworkStateTransitions.BeginPublication(unknown, published.ActiveImageIdentity, At);

        Assert.Equal(ArtworkTransitionAction.None, result.Action);
        Assert.Same(unknown, result.State);
    }

    [Fact]
    public void RepeatedPublicationReusesSourceAndOwnershipTokenButIssuesANewPublicationToken()
    {
        var v1 = ArtworkStateFixtures.Published(Item, Surface, activeContent: "active-1");
        var v2Active = ArtworkStateFixtures.Present(Surface, "active-2", tag: "active-tag-2");
        var fingerprint = ArtworkHashes.ComputeSha256(System.Text.Encoding.UTF8.GetBytes("v2-fingerprint"));

        var guard = PublishedArtworkStateTransitions.BeginPublication(v1, v1.ActiveImageIdentity, At);
        Assert.Equal(ArtworkTransitionAction.PublishDerived, guard.Action);

        var v2 = PublishedArtworkStateTransitions.CommitPublication(v1, v2Active, fingerprint, 2, At).State;

        Assert.Equal(ArtworkPublicationState.Published, v2.State);
        Assert.Equal(v1.SourceArtifactId, v2.SourceArtifactId);
        Assert.Equal(v1.SourceFingerprint, v2.SourceFingerprint);
        Assert.Equal(v1.OwnershipToken, v2.OwnershipToken);
        Assert.NotEqual(v1.PublicationToken, v2.PublicationToken);
        Assert.Equal(v2Active.ContentSha256, v2.ActiveImageIdentity!.ContentSha256);
        Assert.Equal(v1.StateRevision + 1, v2.StateRevision);
        Assert.True(v2.Validate(out var reason), reason);
    }

    [Fact]
    public void CommitPublicationNeverCapturesTheActiveImageAsANewSource()
    {
        var v1 = ArtworkStateFixtures.Published(Item, Surface, sourceContent: "original", activeContent: "derived");

        var v2 = PublishedArtworkStateTransitions
            .CommitPublication(v1, ArtworkStateFixtures.Present(Surface, "derived-2"), ArtworkHashes.ComputeSha256(System.Text.Encoding.UTF8.GetBytes("fp")), 2, At)
            .State;

        Assert.Equal(v1.SourceArtifactId, v2.SourceArtifactId);
        Assert.Equal(ArtworkHashes.ComputeSha256(System.Text.Encoding.UTF8.GetBytes("original")), v2.SourceFingerprint);
        Assert.NotEqual(v2.ActiveImageIdentity!.ContentSha256, v2.SourceFingerprint);
    }

    [Fact]
    public void RequestRestoreMovesPublishedToRestorePending()
    {
        var published = ArtworkStateFixtures.Published(Item, Surface);

        var result = PublishedWorkflow(published);

        Assert.Equal(ArtworkPublicationState.RestorePending, result.State.State);
        Assert.Equal(ArtworkTransitionAction.None, result.Action);
        Assert.True(result.State.Validate(out var reason), reason);
    }

    [Fact]
    public void RequestRestoreIsRefusedWhenNotPublished()
    {
        var capture = ArtworkStateFixtures.Present(Surface, "source");
        var artifact = ArtworkStateFixtures.Artifact(System.Text.Encoding.UTF8.GetBytes("source"));
        var session = PublishedArtworkStateTransitions.CaptureSession(null, Item, Surface, capture, artifact, At).State;

        var result = PublishedArtworkStateTransitions.RequestRestore(session, At);

        Assert.Equal(ArtworkTransitionAction.None, result.Action);
        Assert.Same(session, result.State);
    }

    [Fact]
    public void AuthorizeRestorationAllowsSourceRestoreWhenOwned()
    {
        var pending = PublishedWorkflow(ArtworkStateFixtures.Published(Item, Surface)).State;

        var result = PublishedArtworkStateTransitions.AuthorizeRestoration(pending, pending.ActiveImageIdentity, true, At);

        Assert.Equal(ArtworkTransitionAction.RestoreSource, result.Action);
        Assert.Equal(ArtworkPublicationState.RestorePending, result.State.State);
    }

    [Fact]
    public void AuthorizeRestorationAllowsRemovalWhenTheBaselineWasAbsent()
    {
        var capture = ActiveImageIdentity.Absent(Surface);
        var session = PublishedArtworkStateTransitions.CaptureSession(null, Item, Surface, capture, null, At).State;
        var active = ArtworkStateFixtures.Present(Surface, "derived");
        var published = PublishedArtworkStateTransitions
            .CommitPublication(session, active, ArtworkHashes.ComputeSha256(System.Text.Encoding.UTF8.GetBytes("fp")), 2, At)
            .State;
        var pending = PublishedArtworkStateTransitions.RequestRestore(published, At).State;

        var result = PublishedArtworkStateTransitions.AuthorizeRestoration(pending, pending.ActiveImageIdentity, true, At);

        Assert.Equal(ArtworkTransitionAction.RemoveActiveImage, result.Action);
    }

    [Fact]
    public void AuthorizeRestorationRecordsOwnershipLostWhenTheActiveImageChanged()
    {
        var pending = PublishedWorkflow(ArtworkStateFixtures.Published(Item, Surface)).State;

        var result = PublishedArtworkStateTransitions.AuthorizeRestoration(pending, ArtworkStateFixtures.Present(Surface, "external"), true, At);

        Assert.Equal(ArtworkTransitionAction.None, result.Action);
        Assert.Equal(ArtworkPublicationState.OwnershipLost, result.State.State);
    }

    [Fact]
    public void AuthorizeRestorationRecordsOwnershipUnknownWhenUnobservable()
    {
        var pending = PublishedWorkflow(ArtworkStateFixtures.Published(Item, Surface)).State;

        var result = PublishedArtworkStateTransitions.AuthorizeRestoration(pending, null, true, At);

        Assert.Equal(ArtworkTransitionAction.None, result.Action);
        Assert.Equal(ArtworkPublicationState.OwnershipUnknown, result.State.State);
    }

    [Fact]
    public void AuthorizeRestorationBlocksWhenTheSourceArtifactIsCorrupt()
    {
        var pending = PublishedWorkflow(ArtworkStateFixtures.Published(Item, Surface)).State;

        var result = PublishedArtworkStateTransitions.AuthorizeRestoration(pending, pending.ActiveImageIdentity, false, At);

        Assert.Equal(ArtworkTransitionAction.None, result.Action);
        Assert.Equal(ArtworkPublicationState.RestoreBlocked, result.State.State);
    }

    [Fact]
    public void CommitRestorationBecomesRestoredWhenVerified()
    {
        var pending = PublishedWorkflow(ArtworkStateFixtures.Published(Item, Surface)).State;

        var result = PublishedArtworkStateTransitions.CommitRestoration(pending, true, At);

        Assert.Equal(ArtworkPublicationState.Restored, result.State.State);
        Assert.True(result.State.IsTerminal);
        Assert.True(result.State.Validate(out var reason), reason);
        Assert.Null(result.State.ActiveImageIdentity);
    }

    [Fact]
    public void CommitRestorationBecomesRestoreBlockedWhenUnverified()
    {
        var pending = PublishedWorkflow(ArtworkStateFixtures.Published(Item, Surface)).State;

        var result = PublishedArtworkStateTransitions.CommitRestoration(pending, false, At);

        Assert.Equal(ArtworkPublicationState.RestoreBlocked, result.State.State);
        Assert.True(result.State.IsTerminal);
        Assert.True(result.State.Validate(out var reason), reason);
    }

    [Fact]
    public void MarkRemovedPerformsNoImageMutation()
    {
        var published = ArtworkStateFixtures.Published(Item, Surface);

        var result = PublishedArtworkStateTransitions.MarkRemoved(published, At);

        Assert.Equal(ArtworkTransitionAction.None, result.Action);
        Assert.Equal(ArtworkPublicationState.Removed, result.State.State);
        Assert.True(result.State.IsTerminal);
        Assert.True(result.State.Validate(out var reason), reason);
    }

    [Fact]
    public void ObserveOwnershipRecordsOwnershipLostWhenChanged()
    {
        var published = ArtworkStateFixtures.Published(Item, Surface);

        var result = PublishedArtworkStateTransitions.ObserveOwnership(published, ArtworkStateFixtures.Present(Surface, "external"), At);

        Assert.Equal(ArtworkPublicationState.OwnershipLost, result.State.State);
        Assert.Equal(ArtworkOwnershipStatus.Changed, result.State.LastOwnershipObservation!.Status);
    }

    [Fact]
    public void ObserveOwnershipKeepsPublishedWhenStillOwned()
    {
        var published = ArtworkStateFixtures.Published(Item, Surface);

        var result = PublishedArtworkStateTransitions.ObserveOwnership(published, published.ActiveImageIdentity, At);

        Assert.Equal(ArtworkPublicationState.Published, result.State.State);
        Assert.Equal(ArtworkOwnershipStatus.Owned, result.State.LastOwnershipObservation!.Status);
    }

    private static ArtworkTransitionResult PublishedWorkflow(PublishedArtworkState published)
    {
        return PublishedArtworkStateTransitions.RequestRestore(published, At);
    }

    private static PublishedArtworkState Clone(
        PublishedArtworkState source,
        int? modelVersion = null,
        ArtworkPublicationState? state = null,
        Guid? jellyfinItemId = null,
        ArtworkImageSurface? surface = null,
        bool clearSourcePresence = false,
        ArtworkImagePresence? sourcePresence = null,
        ActiveImageIdentity? sourceCaptureIdentity = null,
        bool clearSourceArtifact = false,
        string? sourceArtifactId = null,
        string? ownershipToken = null,
        bool clearPublicationToken = false,
        string? publicationToken = null,
        bool clearActiveImageIdentity = false,
        ActiveImageIdentity? activeImageIdentity = null)
    {
        return new PublishedArtworkState(
            jellyfinItemId ?? source.JellyfinItemId,
            surface ?? source.ImageSurface,
            state ?? source.State,
            source.UpdatedAt,
            modelVersion ?? source.ModelVersion,
            source.StateRevision,
            clearSourcePresence ? null : sourcePresence ?? source.SourcePresence,
            clearSourceArtifact ? null : sourceArtifactId ?? source.SourceArtifactId,
            source.SourceFingerprint,
            sourceCaptureIdentity ?? source.SourceCaptureIdentity,
            ownershipToken ?? source.OwnershipToken,
            clearPublicationToken ? null : publicationToken ?? source.PublicationToken,
            clearActiveImageIdentity ? null : activeImageIdentity ?? source.ActiveImageIdentity,
            source.PublishedFingerprint,
            source.RendererVersion,
            source.LastOwnershipObservation,
            source.LastOperationId);
    }
}
