using System;
using System.Threading;
using System.Threading.Tasks;
using ArrTags.Reconciliation;
using MediaBrowser.Controller.Library;

namespace ArrTags.PluginLifecycle;

/// <summary>
/// The Jellyfin 12 post-scan reconciliation hook. Jellyfin discovers a plugin's
/// concrete public <see cref="ILibraryPostScanTask"/> implementations by scanning
/// the plugin assembly and runs them at the end of a media-library scan with the
/// scan's progress and cancellation token. It owns no work of its own: it
/// delegates to the bounded, provider-neutral
/// <see cref="LibraryReconciliationService"/>, so a scan only enqueues the same
/// bounded work hints as every other trigger and never performs provider,
/// rendering, or image work inline.
/// </summary>
/// <remarks>
/// <see cref="ILibraryPostScanTask"/> is the dedicated Jellyfin post-scan
/// extension point invoked by <c>LibraryManager</c> after the library validation
/// completes. A cancelled run propagates <see cref="OperationCanceledException"/>
/// so the scan observes the cancellation exactly as it would for any other
/// post-scan task.
/// </remarks>
public sealed class ArrTagsPostScanTask : ILibraryPostScanTask
{
    private readonly LibraryReconciliationService _reconciliation;

    /// <summary>
    /// Initializes a new instance of the <see cref="ArrTagsPostScanTask"/> class.
    /// </summary>
    /// <param name="reconciliation">The bounded full-reconciliation service.</param>
    /// <exception cref="ArgumentNullException">The service is <see langword="null"/>.</exception>
    public ArrTagsPostScanTask(LibraryReconciliationService reconciliation)
    {
        _reconciliation = reconciliation ?? throw new ArgumentNullException(nameof(reconciliation));
    }

    /// <inheritdoc />
    public async Task Run(IProgress<double> progress, CancellationToken cancellationToken)
    {
        var result = await _reconciliation
            .ReconcileAsync(LibraryReconciliationSource.PostScan, progress, cancellationToken)
            .ConfigureAwait(false);

        if (result.Outcome == LibraryReconciliationOutcome.Cancelled)
        {
            cancellationToken.ThrowIfCancellationRequested();
        }
    }
}
