using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;

namespace ArrTags.PluginLifecycle;

/// <summary>
/// The ArrTags hosted lifecycle. It owns library-event subscription for the
/// duration of its lifetime and performs no provider, rendering, or
/// full-library work during registration or startup. It remains idle until the
/// reconciliation milestones register bounded workers.
/// </summary>
public sealed class ArrTagsLifecycleService : IHostedService, IDisposable
{
    private readonly ILibraryEventSource _libraryEvents;
    private readonly EventHandler<LibraryItemChangedEventArgs> _libraryChangedHandler = (_, _) => { };
    private int _subscribed;

    /// <summary>
    /// Initializes a new instance of the <see cref="ArrTagsLifecycleService"/> class.
    /// </summary>
    /// <param name="libraryEvents">The library event source to observe.</param>
    public ArrTagsLifecycleService(ILibraryEventSource libraryEvents)
    {
        _libraryEvents = libraryEvents ?? throw new ArgumentNullException(nameof(libraryEvents));
    }

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (Interlocked.Exchange(ref _subscribed, 1) == 0)
        {
            _libraryEvents.ItemAdded += _libraryChangedHandler;
            _libraryEvents.ItemUpdated += _libraryChangedHandler;
            _libraryEvents.ItemRemoved += _libraryChangedHandler;
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken)
    {
        Unsubscribe();
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        Unsubscribe();
    }

    private void Unsubscribe()
    {
        if (Interlocked.Exchange(ref _subscribed, 0) == 0)
        {
            return;
        }

        _libraryEvents.ItemAdded -= _libraryChangedHandler;
        _libraryEvents.ItemUpdated -= _libraryChangedHandler;
        _libraryEvents.ItemRemoved -= _libraryChangedHandler;
    }
}
