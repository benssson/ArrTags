using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using ArrTags.Configuration;
using Jellyfin.Database.Implementations.Entities;
using MediaBrowser.Model.Activity;

namespace ArrTags.Tests;

/// <summary>
/// A <see cref="DispatchProxy"/> double for the host <see cref="IActivityManager"/>
/// that records the entries created through it. It avoids implementing the full
/// interface and its query DTOs. <see cref="ThrowOnCreate"/> makes the write
/// fail so the caller's containment can be exercised.
/// </summary>
public class RecordingActivityManager : DispatchProxy
{
    /// <summary>
    /// Gets the entries created through this manager, in creation order.
    /// </summary>
    public List<ActivityLog> Entries { get; } = new List<ActivityLog>();

    /// <summary>
    /// Gets or sets a value indicating whether <c>CreateAsync</c> throws.
    /// </summary>
    public bool ThrowOnCreate { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether <c>CreateAsync</c> returns a task
    /// that never completes, so the caller's bounded wait times out.
    /// </summary>
    public bool PendingCreate { get; set; }

    /// <summary>
    /// Creates a proxy and its recording view over the same instance.
    /// </summary>
    /// <returns>The interface proxy and the recording proxy instance.</returns>
    public static (IActivityManager Manager, RecordingActivityManager Recorder) Create()
    {
        var proxy = DispatchProxy.Create<IActivityManager, RecordingActivityManager>();
        return (proxy, (RecordingActivityManager)(object)proxy);
    }

    /// <inheritdoc />
    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
    {
        switch (targetMethod?.Name)
        {
            case "CreateAsync":
                if (ThrowOnCreate)
                {
                    throw new InvalidOperationException("Simulated activity-log failure.");
                }

                if (PendingCreate)
                {
                    return new TaskCompletionSource<bool>().Task;
                }

                Entries.Add((ActivityLog)args![0]!);
                return Task.CompletedTask;
            case "add_EntryCreated":
            case "remove_EntryCreated":
                return null;
            default:
                throw new NotSupportedException(targetMethod?.Name);
        }
    }
}

/// <summary>
/// A rejection notifier that always throws, used to prove the plugin contains a
/// failing notifier and still retains the last valid configuration.
/// </summary>
public sealed class ThrowingRejectionNotifier : IConfigurationRejectionNotifier
{
    /// <inheritdoc />
    public void NotifyRejected(IReadOnlyList<string> reasons)
    {
        throw new InvalidOperationException("Simulated notifier failure.");
    }
}
