using System.Threading;
using System.Threading.Tasks;

namespace ArrTags.Updates;

/// <summary>
/// The Phase 6 placeholder dispatch implementation. Task 6.2 delivers the queue
/// and worker mechanics only; the reconciliation pipeline that resolves the
/// connection, matches the item, publishes metadata state, and drives artwork
/// generation is delivered by tasks 6.3-6.6. Until then this processor completes
/// each dequeued item as a safe no-op so the bounded queue and cancellation
/// behavior are wired and observable without publishing anything.
/// </summary>
public sealed class DeferredWorkItemProcessor : IWorkItemProcessor
{
    /// <inheritdoc />
    public Task<WorkProcessingResult> ProcessAsync(LibraryWorkItem item, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(WorkProcessingResult.Completed(
            "The Phase 6 reconciliation pipeline is not implemented yet; the work item was discarded."));
    }
}
