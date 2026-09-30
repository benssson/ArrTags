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
/// bound, non-printing-scalar removal with otherwise-verbatim emission,
/// empty/whitespace fallback, non-ASCII handling, and the item-identifier
/// fallback when no usable name is available. Task 21.11 extends the coverage
/// for SEC-21.5-02 (the removal predicate is the documented non-printing set
/// Cc/Cf/Zl/Zp, a superset of the pre-fix Cc-only removal) and SEC-21.5-03
/// (the final component is taken after the last '/' and the last '\' on every
/// host, so a Windows, UNC, or mixed-separator path cannot emit a directory
/// chain). These tests require no live host.
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

    // --- Task 21.11 (SEC-21.5-02): non-printing scalar removal -------------

    /// <summary>
    /// SEC-21.5-02: the task 15.1 removal predicate stripped Unicode Cc scalars
    /// only, so Cf format characters and the Zl/Zp separators reached the log
    /// verbatim. Every scalar of the documented non-printing set is removed,
    /// in the single scalar-wise pass, wherever it appears in the name.
    /// </summary>
    [Theory]
    [InlineData("\u202E")] // Cf RIGHT-TO-LEFT OVERRIDE
    [InlineData("\u200B")] // Cf ZERO WIDTH SPACE
    [InlineData("\u200D")] // Cf ZERO WIDTH JOINER
    [InlineData("\u00AD")] // Cf SOFT HYPHEN
    [InlineData("\u202A")] // Cf LEFT-TO-RIGHT EMBEDDING
    [InlineData("\u202C")] // Cf POP DIRECTIONAL FORMATTING
    [InlineData("\u2066")] // Cf LEFT-TO-RIGHT ISOLATE
    [InlineData("\u2069")] // Cf POP DIRECTIONAL ISOLATE
    [InlineData("\u2028")] // Zl LINE SEPARATOR
    [InlineData("\u2029")] // Zp PARAGRAPH SEPARATOR
    public void UnicodeNonPrintingScalarsAreRemoved(string scalar)
    {
        var subject = LogSubject.Create(ItemId, "/media/Movies/movie" + scalar + ".mkv");

        Assert.Equal("movie.mkv", subject);
        Assert.DoesNotContain(scalar, subject, StringComparison.Ordinal);
        Assert.DoesNotContain(subject.EnumerateRunes(), rune => IsNonPrintingRune(rune));
    }

    /// <summary>
    /// SEC-21.5-02 positive control: the widened predicate is a superset of the
    /// pre-fix behavior, so an ordinary Cc control is still removed alongside
    /// the new Cf/Zl/Zp scalars.
    /// </summary>
    [Fact]
    public void NonPrintingRemovalStillRemovesOrdinaryControlScalars()
    {
        var subject = LogSubject.Create(ItemId, "/media/Movies/mo\u202Evie\u0001\u0000.mkv");

        Assert.Equal("movie.mkv", subject);
    }

    /// <summary>
    /// SEC-21.5-02 positive control: characters outside the documented
    /// non-printing set (non-ASCII letters, a decomposed combining mark, an
    /// astral-plane emoji, and whitespace) are preserved verbatim while the Cf
    /// scalar between them is removed.
    /// </summary>
    [Fact]
    public void NonPrintingRemovalPreservesOtherCharactersVerbatim()
    {
        const string expected = "お気に入りCafe\u0301 🎬.mkv";

        var subject = LogSubject.Create(ItemId, "/media/Movies/お気に入り\u202ECafe\u0301 🎬.mkv");

        Assert.Equal(expected, subject);
        Assert.Equal(CountScalarValues(expected), CountScalarValues(subject));
    }

    /// <summary>
    /// SEC-21.5-02: the removal predicate does not collapse or trim the Zs
    /// whitespace around a removed Cf scalar.
    /// </summary>
    [Fact]
    public void NonPrintingRemovalDoesNotCollapseOrTrimWhitespace()
    {
        Assert.Equal("my  movie .mkv", LogSubject.Create(ItemId, "/media/Movies/my \u202E movie .mkv"));
    }

    /// <summary>
    /// SEC-21.5-02: the 128-scalar bound is applied after the widened removal,
    /// so removing the non-printing scalars cannot push the subject past the
    /// bound (and a 300-scalar visible name still truncates to exactly 128).
    /// </summary>
    [Fact]
    public void BoundIsAppliedAfterNonPrintingRemoval()
    {
        var name = string.Concat(Enumerable.Repeat("\u202E\u200B", 150)) + new string('a', 300);

        var subject = LogSubject.Create(ItemId, "/media/Movies/" + name);

        Assert.Equal(new string('a', LogSubject.MaximumScalarValues), subject);
        Assert.Equal(LogSubject.MaximumScalarValues, CountScalarValues(subject));
    }

    /// <summary>
    /// SEC-21.5-02: the single scalar-wise pass still never splits a surrogate
    /// pair after a widened removal drops the leading Cf scalar.
    /// </summary>
    [Fact]
    public void WidenedRemovalStillNeverSplitsASurrogatePair()
    {
        var subject = LogSubject.Create(
            ItemId,
            "/media/Movies/\u202E" + string.Concat(Enumerable.Repeat("\U0001F3AC", 200)) + ".mkv");

        Assert.Equal(string.Concat(Enumerable.Repeat("\U0001F3AC", LogSubject.MaximumScalarValues)), subject);
        Assert.Equal(256, subject.Length);
        Assert.Equal(LogSubject.MaximumScalarValues, CountScalarValues(subject));
    }

    // --- Task 21.11 (SEC-21.5-03): dual-separator final component ----------

    /// <summary>
    /// SEC-21.5-03: the final component is taken after the last '/' and the
    /// last '\', whichever is later, independently of host-platform separator
    /// semantics, so a Windows, UNC, or mixed-separator path yields only the
    /// file name on the pinned Linux host and on any host.
    /// </summary>
    [Theory]
    [InlineData("C:\\Users\\admin\\Videos\\secret.mkv", "secret.mkv")]
    [InlineData("\\\\fileserver\\media\\secret\\file.mkv", "file.mkv")]
    [InlineData("C:/Users\\admin/Videos/secret.mkv", "secret.mkv")]
    [InlineData("relative\\folder\\Episode.S01E01.mkv", "Episode.S01E01.mkv")]
    [InlineData("/media\\Movies/mixed.mkv", "mixed.mkv")]
    public void FinalComponentIsTakenOnBothSeparators(string primaryPath, string expected)
    {
        var subject = LogSubject.Create(ItemId, primaryPath);

        Assert.Equal(expected, subject);
        Assert.DoesNotContain('/', subject);
        Assert.DoesNotContain('\\', subject);
    }

    /// <summary>
    /// SEC-21.5-03: the Windows-style path named by the finding yields only
    /// <c>secret.mkv</c>, with no backslash, account name, or directory
    /// component anywhere in the subject.
    /// </summary>
    [Fact]
    public void WindowsStylePathEmitsNoAccountOrDirectoryComponent()
    {
        var subject = LogSubject.Create(ItemId, "C:\\Users\\admin\\Videos\\secret.mkv");

        Assert.Equal("secret.mkv", subject);
        Assert.DoesNotContain('\\', subject);
        Assert.DoesNotContain("admin", subject, StringComparison.Ordinal);
        Assert.DoesNotContain("Users", subject, StringComparison.Ordinal);
        Assert.DoesNotContain("Videos", subject, StringComparison.Ordinal);
        Assert.DoesNotContain("C:", subject, StringComparison.Ordinal);
    }

    /// <summary>
    /// SEC-21.5-03: a UNC share or drive-only path whose final component is
    /// empty still falls back to the item identifier in <c>D</c> format.
    /// </summary>
    [Theory]
    [InlineData("C:\\")]
    [InlineData("\\\\fileserver\\share\\")]
    [InlineData("\\")]
    [InlineData("\\/")]
    public void WindowsStyleSeparatorOnlyPathsFallBackToTheItemIdentifier(string primaryPath)
    {
        var expected = ItemId.ToString("D", CultureInfo.InvariantCulture);

        Assert.Equal(expected, LogSubject.Create(ItemId, primaryPath));
    }

    /// <summary>
    /// SEC-21.5-02/03: a final component that reduces to nothing after the
    /// widened non-printing removal still takes the post-normalization
    /// item-identifier fallback.
    /// </summary>
    [Theory]
    [InlineData("/media/Movies/\u202E\u200B")]
    [InlineData("/media/Movies/\u2066")]
    [InlineData("/media/Movies/ \u202E ")]
    public void NamesThatReduceToNothingAfterNonPrintingRemovalFallBackToTheItemIdentifier(string primaryPath)
    {
        var expected = ItemId.ToString("D", CultureInfo.InvariantCulture);

        Assert.Equal(expected, LogSubject.Create(ItemId, primaryPath));
    }

    /// <summary>
    /// SR-15.4-04 (folded into SEC-21.5-03): the final-component extraction is
    /// a path splitter, not a URL parser, so a query string attached to the
    /// final component is retained verbatim. Task 21.11 does not change this
    /// behavior; the test records it so it is neither silently altered nor
    /// silently dropped.
    /// </summary>
    [Fact]
    public void UrlShapedPathRetainsItsQueryUnchanged()
    {
        Assert.Equal("a.mkv?apikey=X", LogSubject.Create(ItemId, "/media/movies/a.mkv?apikey=X"));
    }

    private static bool IsNonPrintingRune(Rune rune)
    {
        return Rune.GetUnicodeCategory(rune) is
            UnicodeCategory.Control or
            UnicodeCategory.Format or
            UnicodeCategory.LineSeparator or
            UnicodeCategory.ParagraphSeparator;
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
