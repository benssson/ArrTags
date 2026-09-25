using System;
using System.Globalization;
using System.Linq;
using System.Text;
using ArrTags.Logging;
using Xunit;

namespace ArrTags.Tests;

/// <summary>
/// Task 15.1 coverage for the bounded log-subject helper (ADR-026 clauses 1-2):
/// the file-name component only (never a directory), the documented scalar
/// bound, control-scalar removal with otherwise-verbatim emission,
/// empty/whitespace fallback, non-ASCII handling, and the item-identifier
/// fallback when no usable name is available. These tests require no live host.
/// </summary>
public sealed class LogSubjectTests
{
    private static readonly Guid ItemId = Guid.Parse("8f1d9c2a-3b4e-4a5f-9c6d-7e8f9a0b1c2d");

    [Theory]
    [InlineData("/media/Movies/Blade Runner (1982)/Blade.Runner.1982.2160p.mkv", "Blade.Runner.1982.2160p.mkv")]
    [InlineData("relative/folder/Episode.S01E01.mkv", "Episode.S01E01.mkv")]
    [InlineData("/media/TV/Show/Season 01/s01e01.mkv", "s01e01.mkv")]
    [InlineData("/media/Movies/./movie.mkv", "movie.mkv")]
    [InlineData("single.mkv", "single.mkv")]
    public void FileNameComponentOnlyIsEmitted(string primaryPath, string expected)
    {
        var subject = LogSubject.Create(ItemId, primaryPath);

        Assert.Equal(expected, subject);
        Assert.DoesNotContain('/', subject);
    }

    [Fact]
    public void DirectorySegmentsNeverAppearInTheSubject()
    {
        var subject = LogSubject.Create(
            ItemId,
            "/srv/media/Library/4K Movies/Some.Movie.2019.2160p.mkv");

        Assert.Equal("Some.Movie.2019.2160p.mkv", subject);
        Assert.DoesNotContain("srv", subject, StringComparison.Ordinal);
        Assert.DoesNotContain("media", subject, StringComparison.Ordinal);
        Assert.DoesNotContain("Library", subject, StringComparison.Ordinal);
        Assert.DoesNotContain("4K Movies", subject, StringComparison.Ordinal);
    }

    [Fact]
    public void DocumentedMaximumIsTheAdrBound()
    {
        Assert.Equal(128, LogSubject.MaximumScalarValues);
    }

    [Fact]
    public void NameAtTheBoundIsEmittedUnchanged()
    {
        var name = new string('a', LogSubject.MaximumScalarValues - 4) + ".mkv";

        var subject = LogSubject.Create(ItemId, "/media/Movies/" + name);

        Assert.Equal(name, subject);
        Assert.Equal(LogSubject.MaximumScalarValues, CountScalarValues(subject));
    }

    [Fact]
    public void NameBeyondTheBoundIsTruncatedToTheScalarBound()
    {
        var subject = LogSubject.Create(ItemId, "/media/Movies/" + new string('a', 200) + ".mkv");

        Assert.Equal(new string('a', LogSubject.MaximumScalarValues), subject);
        Assert.Equal(LogSubject.MaximumScalarValues, CountScalarValues(subject));
        Assert.DoesNotContain(".mkv", subject, StringComparison.Ordinal);
    }

    [Fact]
    public void BoundNeverSplitsASurrogatePair()
    {
        var subject = LogSubject.Create(
            ItemId,
            "/media/Movies/" + string.Concat(Enumerable.Repeat("\U0001F3AC", 200)) + ".mkv");

        Assert.Equal(string.Concat(Enumerable.Repeat("\U0001F3AC", LogSubject.MaximumScalarValues)), subject);
        Assert.Equal(256, subject.Length);
        Assert.Equal(LogSubject.MaximumScalarValues, CountScalarValues(subject));
    }

    [Fact]
    public void ControlScalarsAreRemoved()
    {
        Assert.Equal("movie.mkv", LogSubject.Create(ItemId, "/media/Movies/mo\u0000vie\u0001.mkv"));
        Assert.Equal("line1line2.mkv", LogSubject.Create(ItemId, "/media/Movies/line1\r\nline2.mkv"));
        Assert.Equal("tabhere.mkv", LogSubject.Create(ItemId, "/media/Movies/tab\there.mkv"));
        Assert.Equal("ab.mkv", LogSubject.Create(ItemId, "/media/Movies/a\u0085b.mkv"));
        Assert.Equal("del.mkv", LogSubject.Create(ItemId, "/media/Movies/del\u007f.mkv"));
    }

    [Fact]
    public void EmittedSubjectContainsNoControlScalars()
    {
        var subject = LogSubject.Create(ItemId, "/media/Movies/a\u0001b\u007fc\u009fd.mkv");

        Assert.Equal("abcd.mkv", subject);
        Assert.DoesNotContain(subject, character => char.IsControl(character));
    }

    [Fact]
    public void ControlRemovalDoesNotCollapseOrTrimWhitespace()
    {
        Assert.Equal("my  movie .mkv", LogSubject.Create(ItemId, "/media/Movies/my  movie .mkv"));
    }

    [Fact]
    public void BoundIsAppliedAfterControlRemoval()
    {
        var name = new string('\u0001', 100) + new string('a', 200);

        var subject = LogSubject.Create(ItemId, "/media/Movies/" + name);

        Assert.Equal(new string('a', LogSubject.MaximumScalarValues), subject);
    }

    [Theory]
    [InlineData("/media/日本語/お気に入りの映画.mkv", "お気に入りの映画.mkv")]
    [InlineData("/media/Movies/Amélie (2001).mkv", "Amélie (2001).mkv")]
    [InlineData("/media/Movies/Cafe\u0301.mkv", "Cafe\u0301.mkv")]
    [InlineData("/media/🎬/Movie 🎬.mkv", "Movie 🎬.mkv")]
    public void NonAsciiNameIsEmittedVerbatim(string primaryPath, string expected)
    {
        var subject = LogSubject.Create(ItemId, primaryPath);

        Assert.Equal(expected, subject);
        Assert.Equal(CountScalarValues(expected), CountScalarValues(subject));
    }

    [Fact]
    public void UnusableNamesFallBackToTheItemIdentifier()
    {
        var expected = ItemId.ToString("D", CultureInfo.InvariantCulture);

        Assert.Equal(expected, LogSubject.Create(ItemId));
        Assert.Equal(expected, LogSubject.Create(ItemId, null));
        Assert.Equal(expected, LogSubject.Create(ItemId, string.Empty));
        Assert.Equal(expected, LogSubject.Create(ItemId, "   "));
        Assert.Equal(expected, LogSubject.Create(ItemId, "\t"));
        Assert.Equal(expected, LogSubject.Create(ItemId, "/media/Movies/"));
        Assert.Equal(expected, LogSubject.Create(ItemId, "/"));
    }

    [Theory]
    [InlineData("/media/Movies/\u0001\u0002")]
    [InlineData("/media/Movies/ \u0001 ")]
    [InlineData("/media/Movies/\u0000")]
    public void NamesThatReduceToNothingOrWhitespaceFallBackToTheItemIdentifier(string primaryPath)
    {
        var expected = ItemId.ToString("D", CultureInfo.InvariantCulture);

        Assert.Equal(expected, LogSubject.Create(ItemId, primaryPath));
    }

    [Fact]
    public void FallbackIsTheItemIdentifierInDFormat()
    {
        var subject = LogSubject.Create(ItemId, null);

        Assert.Equal(36, subject.Length);
        Assert.True(Guid.TryParseExact(subject, "D", out var parsed));
        Assert.Equal(ItemId, parsed);
    }

    private static int CountScalarValues(string value)
    {
        var count = 0;
        foreach (var rune in value.EnumerateRunes())
        {
            count++;
        }

        return count;
    }
}
