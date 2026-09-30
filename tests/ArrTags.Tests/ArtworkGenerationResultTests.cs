using System;
using ArrTags.Artwork;
using ArrTags.Rendering;
using Xunit;

namespace ArrTags.Tests;

/// <summary>
/// Task 20.2 bounded-classification checks for the ADR-024 empty-selection
/// restoration result. <see cref="ArtworkGenerationResult.FromRestoration"/>
/// maps every reconciliation outcome to exactly one bounded generation outcome
/// and retains the pass-through reason that drove the obligation, so a caller
/// can never read a refused, cancelled, or uncertain restoration as a completed
/// artwork change.
/// </summary>
public class ArtworkGenerationResultTests
{
    private static readonly ArtworkImageSurface Surface = ArtworkImageSurface.Primary;

    [Theory]
    [InlineData(ArtworkReconciliationOutcome.Completed, ArtworkGenerationOutcome.Restored)]
    [InlineData(ArtworkReconciliationOutcome.Cancelled, ArtworkGenerationOutcome.Cancelled)]
    [InlineData(ArtworkReconciliationOutcome.NothingToReconcile, ArtworkGenerationOutcome.RenderPassThrough)]
    [InlineData(ArtworkReconciliationOutcome.OwnershipLost, ArtworkGenerationOutcome.Blocked)]
    [InlineData(ArtworkReconciliationOutcome.OwnershipUnknown, ArtworkGenerationOutcome.Blocked)]
    [InlineData(ArtworkReconciliationOutcome.RecoveryBlocked, ArtworkGenerationOutcome.Blocked)]
    public void FromRestorationMapsTheBoundedReconciliationOutcome(
        ArtworkReconciliationOutcome reconciliationOutcome,
        ArtworkGenerationOutcome expectedOutcome)
    {
        var result = ArtworkGenerationResult.FromRestoration(
            ArtworkReconciliationResult.Create(reconciliationOutcome, "bounded restore reason", "op-restore-1"),
            RenderPassThroughReason.NoDisplayableValue);

        Assert.Equal(expectedOutcome, result.Outcome);
        Assert.Equal(reconciliationOutcome, result.ReconciliationOutcome);
        Assert.Equal(RenderPassThroughReason.NoDisplayableValue, result.PassThroughReason);
        Assert.Equal("bounded restore reason", result.Reason);
        Assert.Equal("op-restore-1", result.OperationId);
        Assert.Null(result.State);

        // Only a completed restoration changed the active artwork; every other
        // mapping leaves the currently usable artwork in place.
        Assert.Equal(reconciliationOutcome != ArtworkReconciliationOutcome.Completed, result.Preserved);
    }

    [Fact]
    public void CompletedRestorationIsRestoredNotPreservedAndCarriesTheCommittedState()
    {
        var state = ArtworkStateFixtures.Published(Guid.NewGuid(), Surface);

        var result = ArtworkGenerationResult.FromRestoration(
            ArtworkReconciliationResult.Create(
                ArtworkReconciliationOutcome.Completed,
                "The retained baseline was restored and verified.",
                "op-restore-2",
                state),
            RenderPassThroughReason.NoDisplayableValue);

        Assert.Equal(ArtworkGenerationOutcome.Restored, result.Outcome);
        Assert.False(result.Preserved);
        Assert.Equal(ArtworkReconciliationOutcome.Completed, result.ReconciliationOutcome);
        Assert.Same(state, result.State);
        Assert.Equal(RenderPassThroughReason.NoDisplayableValue, result.PassThroughReason);
    }

    [Fact]
    public void RefusedRestorationKeepsTheRenderPassThroughClassification()
    {
        var result = ArtworkGenerationResult.FromRestoration(
            ArtworkReconciliationResult.Create(
                ArtworkReconciliationOutcome.NothingToReconcile,
                "The active lifecycle fence refuses restoration work."),
            RenderPassThroughReason.NoDisplayableValue);

        Assert.Equal(ArtworkGenerationOutcome.RenderPassThrough, result.Outcome);
        Assert.Equal(RenderPassThroughReason.NoDisplayableValue, result.PassThroughReason);
        Assert.Equal(ArtworkReconciliationOutcome.NothingToReconcile, result.ReconciliationOutcome);
        Assert.True(result.Preserved);
    }

    [Fact]
    public void NullReconciliationIsRejected()
    {
        Assert.Throws<ArgumentNullException>(
            () => ArtworkGenerationResult.FromRestoration(null!, RenderPassThroughReason.NoDisplayableValue));
    }
}
