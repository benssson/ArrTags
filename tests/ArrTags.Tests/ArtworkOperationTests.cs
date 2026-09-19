using System;
using System.Text;
using ArrTags.Artwork;
using Xunit;

namespace ArrTags.Tests;

/// <summary>
/// Focused checks for the durable <see cref="ArtworkOperation"/> model and the
/// pure phase and lifecycle-fence rules from <c>docs/data-model.md</c> section
/// 3.10.3 and ADR-003. No live Jellyfin or Arr instance is required and no image
/// mutation is performed.
/// </summary>
public sealed class ArtworkOperationTests
{
    private static readonly Guid Item = ArtworkOperationFixtures.Item;
    private static readonly ArtworkImageSurface Surface = ArtworkOperationFixtures.Surface;
    private static readonly DateTimeOffset At = ArtworkOperationFixtures.At;

    [Fact]
    public void ValidPublicationPassesValidation()
    {
        var operation = Publication();

        Assert.True(operation.Validate(out var reason), reason);
        Assert.Equal(ArtworkOperationKind.Publication, operation.Kind);
        Assert.False(operation.IsTerminal);
        Assert.False(operation.MayHaveStartedMutation);
    }

    [Fact]
    public void ValidRestorationOfPresentSourcePassesValidation()
    {
        var operation = Restoration(ArtworkImagePresence.Present);

        Assert.True(operation.Validate(out var reason), reason);
        Assert.Equal(ArtworkImagePresence.Present, operation.CandidateAfterPresence);
        Assert.Equal(operation.SourceArtifactId, operation.CandidateAfterContentSha256);
    }

    [Fact]
    public void ValidRestorationOfAbsentBaselinePassesValidation()
    {
        var operation = Restoration(ArtworkImagePresence.Absent);

        Assert.True(operation.Validate(out var reason), reason);
        Assert.Equal(ArtworkImagePresence.Absent, operation.CandidateAfterPresence);
        Assert.Null(operation.SourceArtifactId);
        Assert.Null(operation.CandidateAfterContentSha256);
    }

    [Fact]
    public void EmptyOperationIdIsRejected()
    {
        var operation = Publication(operationId: string.Empty);

        Assert.False(operation.Validate(out _));
    }

    [Fact]
    public void EmptyItemIdIsRejected()
    {
        var operation = Publication(item: Guid.Empty);

        Assert.False(operation.Validate(out _));
    }

    [Fact]
    public void IndexedSurfaceIsRejected()
    {
        var operation = Publication(surface: new ArtworkImageSurface(ArtworkImageType.Primary, 0));

        Assert.False(operation.Validate(out _));
    }

    [Fact]
    public void UnsupportedModelVersionIsRejected()
    {
        var operation = Publication(modelVersion: ArtworkOperation.CurrentModelVersion + 1);

        Assert.False(operation.Validate(out _));
    }

    [Fact]
    public void PublicationWithoutPublicationTokenIsRejected()
    {
        var operation = Publication(publicationToken: null);

        Assert.False(operation.Validate(out _));
    }

    [Fact]
    public void PublicationWithoutDerivedArtifactIsRejected()
    {
        var operation = Publication(derivedArtifactId: null);

        Assert.False(operation.Validate(out _));
    }

    [Fact]
    public void PublicationWithAbsentAfterTargetIsRejected()
    {
        var operation = Publication(afterPresence: ArtworkImagePresence.Absent);

        Assert.False(operation.Validate(out _));
    }

    [Fact]
    public void PublicationWithMissingAfterHashIsRejected()
    {
        var operation = Publication(candidateAfterHash: null, omitAfterHash: true);

        Assert.False(operation.Validate(out _));
    }

    [Fact]
    public void PresentSourceWithoutArtifactIsRejected()
    {
        var operation = new ArtworkOperation(
            "op-0123456789abcdef",
            ArtworkOperationKind.Publication,
            Item,
            Surface,
            1,
            ArtworkStateFixtures.Present(Surface, "before-bytes"),
            ArtworkImagePresence.Present,
            ArtworkImagePresence.Present,
            ArtworkOperationPhase.Prepared,
            ArtworkLifecycleFence.Normal,
            At,
            At,
            ownershipToken: "own-0123456789abcdef",
            publicationToken: "pub-0123456789abcdef",
            candidateAfterContentSha256: ArtworkHashes.ComputeSha256(Encoding.UTF8.GetBytes("derived-bytes")),
            derivedArtifactId: "der-0123456789abcdef");

        Assert.False(operation.Validate(out _));
    }

    [Fact]
    public void AbsentSourceWithArtifactIsRejected()
    {
        var operation = Publication(
            sourcePresence: ArtworkImagePresence.Absent,
            sourceArtifactId: ArtworkHashes.ComputeSha256(Encoding.UTF8.GetBytes("source-bytes")));

        Assert.False(operation.Validate(out _));
    }

    [Fact]
    public void RestorationWithPublicationTokenIsRejected()
    {
        var operation = Restoration(ArtworkImagePresence.Present, publicationToken: "pub-0123456789abcdef");

        Assert.False(operation.Validate(out _));
    }

    [Fact]
    public void RestorationWithDerivedArtifactIsRejected()
    {
        var operation = Restoration(ArtworkImagePresence.Present, derivedArtifactId: "der-0123456789abcdef");

        Assert.False(operation.Validate(out _));
    }

    [Fact]
    public void RestorationAfterPresenceMustMatchSourcePresence()
    {
        var operation = Restoration(ArtworkImagePresence.Present, afterPresence: ArtworkImagePresence.Absent);

        Assert.False(operation.Validate(out _));
    }

    [Fact]
    public void RestorationPresentSourceMustTargetSourceContent()
    {
        var operation = Restoration(
            ArtworkImagePresence.Present,
            candidateAfterHash: ArtworkHashes.ComputeSha256(Encoding.UTF8.GetBytes("different-bytes")));

        Assert.False(operation.Validate(out _));
    }

    [Fact]
    public void MalformedOwnershipTokenIsRejected()
    {
        var operation = Publication(ownershipToken: "not a token!");

        Assert.False(operation.Validate(out _));
    }

    [Fact]
    public void MalformedPriorPublicationTokenIsRejected()
    {
        var operation = Publication(priorPublicationToken: "short");

        Assert.False(operation.Validate(out _));
    }

    [Fact]
    public void NegativeGenerationIsRejected()
    {
        var operation = Publication(generation: -1);

        Assert.False(operation.Validate(out _));
    }

    [Fact]
    public void AttemptAboveBoundIsRejected()
    {
        var operation = Publication(attempt: ArtworkOperation.MaxAttempt + 1);

        Assert.False(operation.Validate(out _));
    }

    [Fact]
    public void DefaultTimestampsAreRejected()
    {
        var operation = Publication(createdAt: default(DateTimeOffset), updatedAt: default(DateTimeOffset));

        Assert.False(operation.Validate(out _));
    }

    [Fact]
    public void BeforeIdentityOnAnotherSurfaceIsRejected()
    {
        var otherSurface = new ArtworkImageSurface(ArtworkImageType.Primary, 0);
        var operation = Publication(before: ArtworkStateFixtures.Present(otherSurface, "before-bytes"));

        Assert.False(operation.Validate(out _));
    }

    [Fact]
    public void ObservedAfterIdentityOnAnotherSurfaceIsRejected()
    {
        var otherSurface = new ArtworkImageSurface(ArtworkImageType.Primary, 0);
        var operation = Publication(observedAfter: ArtworkStateFixtures.Present(otherSurface, "after-bytes"));

        Assert.False(operation.Validate(out _));
    }

    [Fact]
    public void SourceArtifactWithPathCharactersIsRejected()
    {
        var operation = Publication(sourceArtifactId: "../../etc/passwd");

        Assert.False(operation.Validate(out _));
    }

    [Fact]
    public void DerivedArtifactWithPathCharactersIsRejected()
    {
        var operation = Publication(derivedArtifactId: "../../evil");

        Assert.False(operation.Validate(out _));
    }

    [Fact]
    public void LastErrorIsBoundedAndControlCharactersAreRedacted()
    {
        var raw = "failed\r\n\u0000payload\t" + new string('x', 2000);
        var operation = Publication(lastError: raw);

        Assert.NotNull(operation.LastError);
        Assert.True(operation.LastError!.Length <= ArtworkOperationErrors.MaxErrorLength);
        Assert.DoesNotContain('\r', operation.LastError);
        Assert.DoesNotContain('\n', operation.LastError);
        Assert.DoesNotContain('\t', operation.LastError);
        Assert.DoesNotContain('\0', operation.LastError);
    }

    [Fact]
    public void WhitespaceOnlyLastErrorIsDropped()
    {
        var operation = Publication(lastError: "   \t\r\n ");

        Assert.Null(operation.LastError);
    }

    [Fact]
    public void UndefinedEnumsAreRejectedByConstruction()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Publication(kind: (ArtworkOperationKind)99));
        Assert.Throws<ArgumentOutOfRangeException>(() => Publication(sourcePresence: (ArtworkImagePresence)99));
        Assert.Throws<ArgumentOutOfRangeException>(() => Publication(phase: (ArtworkOperationPhase)99));
        Assert.Throws<ArgumentOutOfRangeException>(() => Publication(lifecycleFence: (ArtworkLifecycleFence)99));
    }

    [Fact]
    public void NullSurfaceOrBeforeIdentityIsRejectedByConstruction()
    {
        Assert.Throws<ArgumentNullException>(() => Publication(surface: null, omitSurface: true));
        Assert.Throws<ArgumentNullException>(() => Publication(before: null, omitBefore: true));
    }

    [Fact]
    public void PhaseEnumMatchesTheDocumentedValues()
    {
        var expected = new[]
        {
            ArtworkOperationPhase.Prepared,
            ArtworkOperationPhase.MutationStarted,
            ArtworkOperationPhase.RepositoryUpdateStarted,
            ArtworkOperationPhase.VerificationPending,
            ArtworkOperationPhase.FinalizationPending,
            ArtworkOperationPhase.Committed,
            ArtworkOperationPhase.Aborted,
            ArtworkOperationPhase.RecoveryBlocked,
        };

        Assert.Equal(expected, Enum.GetValues<ArtworkOperationPhase>());
    }

    [Theory]
    [InlineData(ArtworkOperationPhase.Prepared, ArtworkOperationPhase.MutationStarted)]
    [InlineData(ArtworkOperationPhase.MutationStarted, ArtworkOperationPhase.RepositoryUpdateStarted)]
    [InlineData(ArtworkOperationPhase.RepositoryUpdateStarted, ArtworkOperationPhase.VerificationPending)]
    [InlineData(ArtworkOperationPhase.VerificationPending, ArtworkOperationPhase.FinalizationPending)]
    [InlineData(ArtworkOperationPhase.FinalizationPending, ArtworkOperationPhase.Committed)]
    [InlineData(ArtworkOperationPhase.Prepared, ArtworkOperationPhase.Committed)]
    [InlineData(ArtworkOperationPhase.Prepared, ArtworkOperationPhase.Aborted)]
    [InlineData(ArtworkOperationPhase.Prepared, ArtworkOperationPhase.RecoveryBlocked)]
    [InlineData(ArtworkOperationPhase.MutationStarted, ArtworkOperationPhase.Committed)]
    [InlineData(ArtworkOperationPhase.VerificationPending, ArtworkOperationPhase.RecoveryBlocked)]
    public void LegalPhaseTransitionsAreAllowed(ArtworkOperationPhase from, ArtworkOperationPhase to)
    {
        Assert.True(ArtworkOperationPhases.CanAdvance(from, to));
    }

    [Theory]
    [InlineData(ArtworkOperationPhase.MutationStarted, ArtworkOperationPhase.Prepared)]
    [InlineData(ArtworkOperationPhase.RepositoryUpdateStarted, ArtworkOperationPhase.MutationStarted)]
    [InlineData(ArtworkOperationPhase.VerificationPending, ArtworkOperationPhase.RepositoryUpdateStarted)]
    [InlineData(ArtworkOperationPhase.FinalizationPending, ArtworkOperationPhase.VerificationPending)]
    [InlineData(ArtworkOperationPhase.Prepared, ArtworkOperationPhase.RepositoryUpdateStarted)]
    [InlineData(ArtworkOperationPhase.Prepared, ArtworkOperationPhase.VerificationPending)]
    [InlineData(ArtworkOperationPhase.Prepared, ArtworkOperationPhase.Prepared)]
    [InlineData(ArtworkOperationPhase.Committed, ArtworkOperationPhase.FinalizationPending)]
    [InlineData(ArtworkOperationPhase.Committed, ArtworkOperationPhase.Aborted)]
    [InlineData(ArtworkOperationPhase.Aborted, ArtworkOperationPhase.Committed)]
    [InlineData(ArtworkOperationPhase.Aborted, ArtworkOperationPhase.RecoveryBlocked)]
    [InlineData(ArtworkOperationPhase.RecoveryBlocked, ArtworkOperationPhase.Committed)]
    [InlineData(ArtworkOperationPhase.RecoveryBlocked, ArtworkOperationPhase.Aborted)]
    public void IllegalPhaseTransitionsAreRefused(ArtworkOperationPhase from, ArtworkOperationPhase to)
    {
        Assert.False(ArtworkOperationPhases.CanAdvance(from, to));
    }

    [Fact]
    public void TryAdvanceExplainsARefusedTransition()
    {
        Assert.True(ArtworkOperationPhases.TryAdvance(
            ArtworkOperationPhase.Prepared,
            ArtworkOperationPhase.MutationStarted,
            out var allowedReason));
        Assert.Empty(allowedReason);

        Assert.False(ArtworkOperationPhases.TryAdvance(
            ArtworkOperationPhase.Committed,
            ArtworkOperationPhase.Aborted,
            out var refusedReason));
        Assert.False(string.IsNullOrWhiteSpace(refusedReason));
        Assert.True(refusedReason.Length <= 512);
    }

    [Theory]
    [InlineData(ArtworkOperationPhase.Committed, true)]
    [InlineData(ArtworkOperationPhase.Aborted, true)]
    [InlineData(ArtworkOperationPhase.RecoveryBlocked, true)]
    [InlineData(ArtworkOperationPhase.Prepared, false)]
    [InlineData(ArtworkOperationPhase.MutationStarted, false)]
    [InlineData(ArtworkOperationPhase.VerificationPending, false)]
    public void IsTerminalMatchesTheDocumentedTerminalPhases(ArtworkOperationPhase phase, bool expected)
    {
        Assert.Equal(expected, ArtworkOperationPhases.IsTerminal(phase));
    }

    [Theory]
    [InlineData(ArtworkOperationPhase.Prepared, false)]
    [InlineData(ArtworkOperationPhase.MutationStarted, true)]
    [InlineData(ArtworkOperationPhase.RepositoryUpdateStarted, true)]
    [InlineData(ArtworkOperationPhase.VerificationPending, true)]
    [InlineData(ArtworkOperationPhase.FinalizationPending, true)]
    [InlineData(ArtworkOperationPhase.Committed, true)]
    [InlineData(ArtworkOperationPhase.Aborted, true)]
    [InlineData(ArtworkOperationPhase.RecoveryBlocked, true)]
    public void MayHaveStartedMutationReflectsTheLowerBound(ArtworkOperationPhase phase, bool expected)
    {
        Assert.Equal(expected, ArtworkOperationPhases.MayHaveStartedMutation(phase));
    }

    [Theory]
    [InlineData(ArtworkLifecycleFence.Normal, true)]
    [InlineData(ArtworkLifecycleFence.Disable, false)]
    [InlineData(ArtworkLifecycleFence.Uninstall, false)]
    [InlineData(ArtworkLifecycleFence.ItemRemoved, false)]
    public void OnlyNormalFenceAllowsNewPublication(ArtworkLifecycleFence fence, bool expected)
    {
        Assert.Equal(expected, ArtworkOperationFencing.AllowsNewPublication(fence));
    }

    [Theory]
    [InlineData(ArtworkLifecycleFence.Normal, true)]
    [InlineData(ArtworkLifecycleFence.Disable, true)]
    [InlineData(ArtworkLifecycleFence.Uninstall, true)]
    [InlineData(ArtworkLifecycleFence.ItemRemoved, false)]
    public void RestorationIsAllowedExceptForItemRemoval(ArtworkLifecycleFence fence, bool expected)
    {
        Assert.Equal(expected, ArtworkOperationFencing.AllowsNewRestoration(fence));
    }

    [Theory]
    [InlineData(2, 1, true)]
    [InlineData(2, 2, false)]
    [InlineData(2, 3, false)]
    public void StaleGenerationIsDetected(long durable, long candidate, bool expected)
    {
        Assert.Equal(expected, ArtworkOperationFencing.IsStale(durable, candidate));
    }

    [Theory]
    [InlineData(2, 3, true)]
    [InlineData(2, 2, false)]
    [InlineData(2, 1, false)]
    public void OnlyNewerGenerationMaySupersede(long durable, long candidate, bool expected)
    {
        Assert.Equal(expected, ArtworkOperationFencing.CanSupersede(durable, candidate));
    }

    [Theory]
    [InlineData(2, 2, true)]
    [InlineData(2, 3, false)]
    public void SameGenerationIsDetected(long durable, long candidate, bool expected)
    {
        Assert.Equal(expected, ArtworkOperationFencing.IsSameGeneration(durable, candidate));
    }

    private static ArtworkOperation Publication(
        string operationId = "op-0123456789abcdef",
        Guid? item = null,
        ArtworkImageSurface? surface = null,
        bool omitSurface = false,
        long generation = 1,
        int modelVersion = ArtworkOperation.CurrentModelVersion,
        ActiveImageIdentity? before = null,
        bool omitBefore = false,
        ArtworkImagePresence sourcePresence = ArtworkImagePresence.Present,
        ArtworkImagePresence afterPresence = ArtworkImagePresence.Present,
        ArtworkOperationKind kind = ArtworkOperationKind.Publication,
        ArtworkOperationPhase phase = ArtworkOperationPhase.Prepared,
        ArtworkLifecycleFence lifecycleFence = ArtworkLifecycleFence.Normal,
        string? ownershipToken = "own-0123456789abcdef",
        string? priorPublicationToken = null,
        string? publicationToken = "pub-0123456789abcdef",
        string? candidateAfterHash = null,
        bool omitAfterHash = false,
        string? sourceArtifactId = null,
        string? derivedArtifactId = "der-0123456789abcdef",
        int attempt = 0,
        DateTimeOffset? createdAt = null,
        DateTimeOffset? updatedAt = null,
        string? lastError = null,
        ActiveImageIdentity? observedAfter = null)
    {
        var subject = item ?? Item;
        var imageSurface = surface ?? Surface;
        var at = updatedAt ?? At;
        var sourceHash = ArtworkHashes.ComputeSha256(Encoding.UTF8.GetBytes("source-bytes"));
        var derivedHash = ArtworkHashes.ComputeSha256(Encoding.UTF8.GetBytes("derived-bytes"));

        return new ArtworkOperation(
            operationId,
            kind,
            subject,
            omitSurface ? null! : imageSurface,
            generation,
            omitBefore ? null! : before ?? ArtworkStateFixtures.Present(imageSurface, "before-bytes"),
            sourcePresence,
            afterPresence,
            phase,
            lifecycleFence,
            createdAt ?? At,
            at,
            modelVersion,
            ownershipToken,
            priorPublicationToken,
            publicationToken,
            omitAfterHash ? null : candidateAfterHash ?? derivedHash,
            observedAfter,
            sourcePresence == ArtworkImagePresence.Present ? sourceArtifactId ?? sourceHash : sourceArtifactId,
            derivedArtifactId,
            attempt,
            lastError);
    }

    private static ArtworkOperation Restoration(
        ArtworkImagePresence sourcePresence,
        ArtworkImagePresence? afterPresence = null,
        string? publicationToken = null,
        string? derivedArtifactId = null,
        string? candidateAfterHash = null)
    {
        var present = sourcePresence == ArtworkImagePresence.Present;
        var sourceHash = ArtworkHashes.ComputeSha256(Encoding.UTF8.GetBytes("source-bytes"));

        return new ArtworkOperation(
            "op-0123456789abcdef",
            ArtworkOperationKind.Restoration,
            Item,
            Surface,
            1,
            ArtworkStateFixtures.Present(Surface, "active-bytes"),
            sourcePresence,
            afterPresence ?? sourcePresence,
            ArtworkOperationPhase.Prepared,
            ArtworkLifecycleFence.Normal,
            At,
            At,
            ownershipToken: "own-0123456789abcdef",
            priorPublicationToken: "prior-0123456789abcdef",
            publicationToken: publicationToken,
            candidateAfterContentSha256: present ? candidateAfterHash ?? sourceHash : null,
            sourceArtifactId: present ? sourceHash : null,
            derivedArtifactId: derivedArtifactId);
    }
}
