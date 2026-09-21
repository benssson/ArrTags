using System.Threading;
using System.Threading.Tasks;

namespace ArrTags.Updates;

/// <summary>
/// The narrow dispatch boundary between the Phase 6 work queue and the
/// reconciliation pipeline. Task 6.2 owns only the queue, coalescing,
/// single-flight, cancellation, retry, and overflow mechanics; the concrete
/// implementation that resolves the connection, matches the item, fetches
/// metadata, and drives artwork publication is delivered by later Phase 6 tasks.
/// An implementation must honor the supplied cancellation token so a host
/// shutdown can cancel in-flight work within the bounded shutdown limits.
/// </summary>
public interface IWorkItemProcessor
{
    /// <summary>
    /// Processes one queued work item and classifies the outcome.
    /// </summary>
    /// <param name="item">The bounded queued work item.</param>
    /// <param name="cancellationToken">The token that cancels host shutdown and worker cancellation.</param>
    /// <returns>The classified processing outcome.</returns>
    Task<WorkProcessingResult> ProcessAsync(LibraryWorkItem item, CancellationToken cancellationToken);
}
