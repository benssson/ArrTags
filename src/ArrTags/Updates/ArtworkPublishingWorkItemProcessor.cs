using System;
using System.Threading;
using System.Threading.Tasks;
using ArrTags.Artwork;
using ArrTags.Configuration;
using ArrTags.Reconciliation;
using ArrTags.Rendering;
using ArrTags.State;

namespace ArrTags.Updates;

/// <summary>
/// The Phase 6 composition of metadata reconciliation and artwork publication.
/// It runs the unchanged reconciliation first and, only when atomic metadata state
/// was published, evaluates the publication-fingerprint gate through
/// <see cref="ArtworkRegenerationPlanner"/> and drives
/// <see cref="ArtworkGenerationCoordinator"/> for the item's V1 unindexed Primary
/// surface when regeneration is required.
/// </summary>
/// <remarks>
/// The artwork stage never weakens the reconciliation result: a discarded, failed,
/// or retried metadata attempt performs no artwork work, and artwork generation is
/// best-effort after a successful publication so a render or publication failure
/// preserves the current usable artwork without turning the (already successful)
/// metadata publication into a retry. A subject whose durable metadata state is
/// not usable as current under the task 6.5 freshness policy is never rendered or
/// published. Every drive enters the publisher through its normal durable
/// write-ahead protocol and per-subject gate, so the task 6.4 recovery-fence
/// guarantees are unchanged.
/// </remarks>
public sealed class ArtworkPublishingWorkItemProcessor : IWorkItemProcessor
{
    private readonly MetadataReconciliationProcessor _reconciliation;
    private readonly ArtworkGenerationCoordinator _coordinator;
    private readonly PublishedArtworkStateStore _states;
    private readonly ConfigurationSnapshotService _configuration;

    /// <summary>
    /// Initializes a new instance of the <see cref="ArtworkPublishingWorkItemProcessor"/> class.
    /// </summary>
    /// <param name="reconciliation">The metadata reconciliation pipeline.</param>
    /// <param name="coordinator">The single-subject artwork generation coordinator.</param>
    /// <param name="states">The authoritative published-artwork state store.</param>
    /// <param name="configuration">The current public configuration snapshot.</param>
    /// <exception cref="ArgumentNullException">A dependency is <see langword="null"/>.</exception>
    public ArtworkPublishingWorkItemProcessor(
        MetadataReconciliationProcessor reconciliation,
        ArtworkGenerationCoordinator coordinator,
        PublishedArtworkStateStore states,
        ConfigurationSnapshotService configuration)
    {
        _reconciliation = reconciliation ?? throw new ArgumentNullException(nameof(reconciliation));
        _coordinator = coordinator ?? throw new ArgumentNullException(nameof(coordinator));
        _states = states ?? throw new ArgumentNullException(nameof(states));
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
    }

    /// <inheritdoc />
    public async Task<WorkProcessingResult> ProcessAsync(LibraryWorkItem item, CancellationToken cancellationToken)
    {
        var reconciliation = await _reconciliation.ReconcileAsync(item, cancellationToken).ConfigureAwait(false);
        if (!reconciliation.HasPublishedState
            || reconciliation.Entry is null
            || reconciliation.Identity is null
            || reconciliation.Match is null)
        {
            return reconciliation.ProcessingResult;
        }

        var stateRead = _states.Read(item.Key.ItemId, item.Key.Surface);
        if (stateRead.Status is StateReadStatus.InvalidQuarantined or StateReadStatus.InvalidDiscarded)
        {
            // The authoritative ownership state is invalid; fail closed and
            // perform no artwork work rather than re-baselining from the active
            // image.
            return reconciliation.ProcessingResult;
        }

        var snapshot = _configuration.Current;
        if (snapshot.ConfigurationVersion != item.ConfigurationVersion)
        {
            // The configuration advanced after the metadata state was published.
            // The work item's basis is stale, so no artwork work is performed under
            // it; the newer configuration is applied by the work item that carries
            // its version.
            return reconciliation.ProcessingResult;
        }

        var decision = ArtworkRegenerationPlanner.Decide(
            stateRead.Value,
            reconciliation.Identity,
            reconciliation.Metadata,
            reconciliation.Entry.IsUsableAsCurrent(DateTimeOffset.UtcNow),
            snapshot.BadgeDefinitions,
            snapshot.RendererOutputPolicy,
            snapshot.RendererConfigurationFingerprint,
            RenderVersion.CurrentRendererVersion,
            RenderVersion.CurrentBadgeSchemaVersion);

        if (!decision.ShouldGenerate)
        {
            return reconciliation.ProcessingResult;
        }

        var request = new ArtworkGenerationRequest(
            item.Key.ItemId,
            item.Key.Surface,
            reconciliation.Identity,
            reconciliation.Match,
            reconciliation.Metadata,
            snapshot.BadgeDefinitions,
            snapshot.RendererConfigurationFingerprint,
            snapshot.RendererOutputPolicy,
            snapshot.Limits);

        ArtworkGenerationResult result;
        try
        {
            result = await _coordinator.GenerateAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }

        if (result.Outcome == ArtworkGenerationOutcome.Cancelled && cancellationToken.IsCancellationRequested)
        {
            cancellationToken.ThrowIfCancellationRequested();
        }

        return reconciliation.ProcessingResult;
    }
}
