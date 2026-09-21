using System;
using System.Collections.Generic;
using ArrTags.Configuration;
using ArrTags.Media;
using ArrTags.PluginLifecycle;
using ArrTags.Updates;

namespace ArrTags.Tests;

/// <summary>
/// Shared test doubles for the library-event and update-hint boundaries. They
/// require no live Jellyfin or Arr instance.
/// </summary>
internal sealed class FakeLibraryEventSource : ILibraryEventSource
{
    private EventHandler<LibraryItemChangedEventArgs>? _added;
    private EventHandler<LibraryItemChangedEventArgs>? _updated;
    private EventHandler<LibraryItemChangedEventArgs>? _removed;

    public int AddedSubscriberCount { get; private set; }

    public int UpdatedSubscriberCount { get; private set; }

    public int RemovedSubscriberCount { get; private set; }

    public int SubscriberCount => AddedSubscriberCount + UpdatedSubscriberCount + RemovedSubscriberCount;

    public void RaiseAdded(LibraryItemChangedEventArgs args)
    {
        _added?.Invoke(this, args);
    }

    public void RaiseUpdated(LibraryItemChangedEventArgs args)
    {
        _updated?.Invoke(this, args);
    }

    public void RaiseRemoved(Guid itemId)
    {
        RaiseRemoved(new LibraryItemChangedEventArgs(itemId, LibraryWorkReason.Removed, MediaItemType.Movie));
    }

    public void RaiseRemoved(LibraryItemChangedEventArgs args)
    {
        _removed?.Invoke(this, args);
    }

    public event EventHandler<LibraryItemChangedEventArgs>? ItemAdded
    {
        add
        {
            AddedSubscriberCount++;
            _added += value;
        }

        remove
        {
            AddedSubscriberCount--;
            _added -= value;
        }
    }

    public event EventHandler<LibraryItemChangedEventArgs>? ItemUpdated
    {
        add
        {
            UpdatedSubscriberCount++;
            _updated += value;
        }

        remove
        {
            UpdatedSubscriberCount--;
            _updated -= value;
        }
    }

    public event EventHandler<LibraryItemChangedEventArgs>? ItemRemoved
    {
        add
        {
            RemovedSubscriberCount++;
            _removed += value;
        }

        remove
        {
            RemovedSubscriberCount--;
            _removed -= value;
        }
    }
}

/// <summary>
/// A recording <see cref="IWorkHintSink"/> double that captures the bounded
/// hints a library-event handler enqueues.
/// </summary>
internal sealed class RecordingWorkHintSink : IWorkHintSink
{
    private readonly object _gate = new object();
    private readonly List<LibraryWorkHint> _hints = new List<LibraryWorkHint>();

    public int Capacity { get; set; } = 512;

    public int Count
    {
        get
        {
            lock (_gate)
            {
                return _hints.Count;
            }
        }
    }

    public IReadOnlyList<LibraryWorkHint> Hints
    {
        get
        {
            lock (_gate)
            {
                return _hints.ToArray();
            }
        }
    }

    public bool TryEnqueue(in LibraryWorkHint hint)
    {
        lock (_gate)
        {
            if (_hints.Count >= Capacity)
            {
                return false;
            }

            _hints.Add(hint);
            return true;
        }
    }
}

/// <summary>
/// Builders for validated configuration snapshots and bounded change events used
/// by the library-event boundary tests.
/// </summary>
internal static class LibraryEventFixtures
{
    public static ConfigurationSnapshotService CreateConfiguration(
        bool sonarrEnabled = false,
        bool radarrEnabled = false)
    {
        var configuration = new PluginConfiguration();
        if (sonarrEnabled)
        {
            configuration.Sonarr = new ArrConnectionConfiguration
            {
                Enabled = true,
                BaseUrl = "http://sonarr.test",
                ApiKey = "test-sonarr-key",
            };
        }

        if (radarrEnabled)
        {
            configuration.Radarr = new ArrConnectionConfiguration
            {
                Enabled = true,
                BaseUrl = "http://radarr.test",
                ApiKey = "test-radarr-key",
            };
        }

        return new ConfigurationSnapshotService(configuration);
    }

    public static LibraryItemChangedEventArgs Change(
        Guid itemId,
        LibraryWorkReason reason,
        MediaItemType? itemType,
        LibraryItemChangeOrigin origin = LibraryItemChangeOrigin.Library)
    {
        return new LibraryItemChangedEventArgs(itemId, reason, itemType, origin);
    }
}
