using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using ArrTags.Media;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using Xunit;

namespace ArrTags.Tests;

/// <summary>
/// Focused checks for the task 19.1 identity-anchored resume capability of the
/// Jellyfin library enumerator (ADR-029 clause 2): the walk uses only the
/// supported <c>StartIndex</c>/<c>Limit</c> paging surface in bounded pages,
/// locates the unique anchor item by identity, returns the page that begins
/// strictly after it, reports an unlocatable anchor, and honors cancellation.
/// No live Jellyfin instance is required.
/// </summary>
public sealed class MediaLibraryResumeTests
{
    [Fact]
    public async Task ResumeLocatesTheAnchorAndReturnsThePageStrictlyAfterIt()
    {
        var items = Items("item-0", "item-1", "item-2", "item-3", "item-4", "item-5", "item-6");
        var (enumerator, library) = Enumerator(items);

        var result = await enumerator.ResumeCandidatesAsync(items[4].Id, maxItems: 3, CancellationToken.None);

        Assert.Equal(MediaLibraryResumeOutcome.Located, result.Outcome);
        Assert.Equal(5, result.StartIndex);
        Assert.Equal(new[] { items[5].Id, items[6].Id }, result.Candidates.Select(item => item.Id));

        // The locate walk is bounded by the requested page size and uses only
        // the offset paging surface.
        Assert.Equal(new[] { (0, 3), (3, 3), (5, 3) }, library.Requests);
    }

    [Fact]
    public async Task ResumeReturnsAnEmptyPageWhenTheAnchorIsTheLastItem()
    {
        var items = Items("item-0", "item-1", "item-2");
        var (enumerator, library) = Enumerator(items);

        var result = await enumerator.ResumeCandidatesAsync(items[2].Id, maxItems: 2, CancellationToken.None);

        Assert.Equal(MediaLibraryResumeOutcome.Located, result.Outcome);
        Assert.Equal(3, result.StartIndex);
        Assert.Empty(result.Candidates);
        Assert.Equal(new[] { (0, 2), (2, 2), (3, 2) }, library.Requests);
    }

    [Fact]
    public async Task ResumeReportsAnUnlocatableAnchorAfterWalkingTheWholeOrder()
    {
        var items = Items("item-0", "item-1", "item-2");
        var (enumerator, library) = Enumerator(items);

        var result = await enumerator.ResumeCandidatesAsync(Guid.NewGuid(), maxItems: 2, CancellationToken.None);

        Assert.Equal(MediaLibraryResumeOutcome.AnchorNotFound, result.Outcome);
        Assert.Equal(0, result.StartIndex);
        Assert.Empty(result.Candidates);
        Assert.Equal(new[] { (0, 2), (2, 2) }, library.Requests);
    }

    [Fact]
    public async Task ResumeReportsAnEmptyAnchorAsUnlocatableWithoutQueryingTheLibrary()
    {
        var (enumerator, library) = Enumerator(Items("item-0"));

        var result = await enumerator.ResumeCandidatesAsync(Guid.Empty, maxItems: 2, CancellationToken.None);

        Assert.Equal(MediaLibraryResumeOutcome.AnchorNotFound, result.Outcome);
        Assert.Empty(library.Requests);
    }

    [Fact]
    public async Task ResumeHonorsAnAlreadyCancelledToken()
    {
        var (enumerator, library) = Enumerator(Items("item-0", "item-1"));
        var token = new CancellationToken(canceled: true);

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => enumerator.ResumeCandidatesAsync(Guid.NewGuid(), maxItems: 2, token));

        Assert.Empty(library.Requests);
    }

    [Fact]
    public async Task ResumeCancelsInsideAPageAndDoesNotReadTheNextPage()
    {
        var items = Items("item-0", "item-1", "item-2", "item-3", "item-4");
        var (enumerator, library) = Enumerator(items);

        using var cts = new CancellationTokenSource();
        library.OnRequest = start =>
        {
            if (start == 3)
            {
#pragma warning disable CA1849 // A synchronous query hook cannot await CancelAsync.
                cts.Cancel();
#pragma warning restore CA1849
            }
        };

        // The cancelled page is the last (partial) page and does not contain the
        // anchor: without the in-page cancellation check the walk would report
        // AnchorNotFound instead of honoring the cancellation.
        await Assert.ThrowsAsync<OperationCanceledException>(
            () => enumerator.ResumeCandidatesAsync(Guid.NewGuid(), maxItems: 3, cts.Token));

        Assert.Equal(new[] { (0, 3), (3, 3) }, library.Requests);
    }

    private static List<BaseItem> Items(params string[] names)
    {
        var items = new List<BaseItem>(names.Length);
        foreach (var name in names)
        {
            var id = Guid.NewGuid();
            items.Add(new ReconciliationTestMovie { Id = id, Name = name, SortName = name });
        }

        return items;
    }

    private static (JellyfinMediaLibraryEnumerator Enumerator, FakeLibraryManager Library) Enumerator(
        IReadOnlyList<BaseItem> items)
    {
        var proxy = DispatchProxy.Create<ILibraryManager, FakeLibraryManager>();
        var library = (FakeLibraryManager)(object)proxy;
        library.Items = items.ToList();
        return (new JellyfinMediaLibraryEnumerator(proxy), library);
    }

    /// <summary>
    /// A minimal <see cref="ILibraryManager"/> proxy that serves the candidate
    /// query from an in-memory host-ordered list and records every paged read.
    /// </summary>
    public class FakeLibraryManager : DispatchProxy
    {
        public List<BaseItem> Items { get; set; } = new();

        public List<(int Start, int Max)> Requests { get; } = new();

        public Action<int>? OnRequest { get; set; }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod is null)
            {
                return null;
            }

            if (targetMethod.Name == nameof(ILibraryManager.GetItemList))
            {
                var query = (InternalItemsQuery)args![0]!;
                var start = query.StartIndex ?? 0;
                var limit = query.Limit ?? 100;
                OnRequest?.Invoke(start);
                Requests.Add((start, limit));
                return Items.Skip(start).Take(limit).ToList();
            }

            if (targetMethod.Name == nameof(ILibraryManager.GetCount))
            {
                return Items.Count;
            }

            throw new NotSupportedException(targetMethod.Name);
        }
    }
}
