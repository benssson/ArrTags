using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ArrTags.Reconciliation;
using MediaBrowser.Model.Tasks;

namespace ArrTags.PluginLifecycle;

/// <summary>
/// The Jellyfin 12 scheduled reconciliation task. It appears in Jellyfin's
/// scheduled-task list under the ArrTags category, supports the default periodic
/// trigger, and can be started manually from the scheduled-task surface. It owns
/// no work of its own: it delegates to the bounded, provider-neutral
/// <see cref="LibraryReconciliationService"/>, which enqueues the same bounded
/// work hints as every other trigger. Construction and registration perform no
/// provider, rendering, or library work.
/// </summary>
/// <remarks>
/// Jellyfin discovers a plugin's concrete public <see cref="IScheduledTask"/>
/// implementations by scanning the plugin assembly and instantiates this type
/// through the service provider, so its constructor dependencies must be
/// registered by the plugin service registrator. A cancelled run propagates
/// <see cref="OperationCanceledException"/> so Jellyfin records the task as
/// cancelled.
/// </remarks>
public sealed class ArrTagsReconciliationTask : IScheduledTask
{
    /// <summary>
    /// The stable task key shown to administrators.
    /// </summary>
    public const string TaskKey = "ArrTagsReconciliation";

    /// <summary>
    /// The task category shown to administrators.
    /// </summary>
    public const string TaskCategory = "ArrTags";

    /// <summary>
    /// The default interval between periodic reconciliations.
    /// </summary>
    public static readonly TimeSpan DefaultInterval = TimeSpan.FromHours(12);

    private readonly LibraryReconciliationService _reconciliation;

    /// <summary>
    /// Initializes a new instance of the <see cref="ArrTagsReconciliationTask"/> class.
    /// </summary>
    /// <param name="reconciliation">The bounded full-reconciliation service.</param>
    /// <exception cref="ArgumentNullException">The service is <see langword="null"/>.</exception>
    public ArrTagsReconciliationTask(LibraryReconciliationService reconciliation)
    {
        _reconciliation = reconciliation ?? throw new ArgumentNullException(nameof(reconciliation));
    }

    /// <inheritdoc />
    public string Name => "ArrTags: reconcile badge metadata";

    /// <inheritdoc />
    public string Key => TaskKey;

    /// <inheritdoc />
    public string Description =>
        "Re-reads current Sonarr and Radarr metadata for the configured library scope and refreshes affected badge artwork. Runs periodically and can be started manually.";

    /// <inheritdoc />
    public string Category => TaskCategory;

    /// <inheritdoc />
    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
    {
        yield return new TaskTriggerInfo
        {
            Type = TaskTriggerInfoType.IntervalTrigger,
            IntervalTicks = DefaultInterval.Ticks,
        };
    }

    /// <inheritdoc />
    public async Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        var result = await _reconciliation
            .ReconcileAsync(LibraryReconciliationSource.Scheduled, progress, cancellationToken)
            .ConfigureAwait(false);

        if (result.Outcome == LibraryReconciliationOutcome.Cancelled)
        {
            cancellationToken.ThrowIfCancellationRequested();
        }
    }
}
