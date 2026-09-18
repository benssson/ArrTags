using System;
using ArrTags.Providers;
using Xunit;

namespace ArrTags.Tests;

/// <summary>
/// Task 2.5 checks for the canonical, connection-scoped Arr record and file
/// identity model. These tests require no live Jellyfin or Arr instance.
/// </summary>
public class CanonicalIdentityTests
{
    [Fact]
    public void AbsentFileIdentityIsExplicitAndNeverZero()
    {
        Assert.Equal(ArrFilePresence.Absent, ArrFileIdentity.Absent.Presence);
        Assert.Null(ArrFileIdentity.Absent.FileId);
        Assert.NotEqual(ArrFileIdentity.Present(1), ArrFileIdentity.Absent);
    }

    [Fact]
    public void PresentFileIdentityRequiresPositiveIdentifier()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ArrFileIdentity.Present(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => ArrFileIdentity.Present(-1));
    }

    [Fact]
    public void PresentFileIdentityComparesByIdentifier()
    {
        Assert.Equal(ArrFileIdentity.Present(42), ArrFileIdentity.Present(42));
        Assert.NotEqual(ArrFileIdentity.Present(42), ArrFileIdentity.Present(43));
        Assert.Equal(ArrFileIdentity.Present(42).GetHashCode(), ArrFileIdentity.Present(42).GetHashCode());
    }

    [Fact]
    public void RecordIdentityIsScopedByConnection()
    {
        var connectionA = ArrConnectionId.For(ArrProviderKind.Radarr, "http://radarr.local:7878");
        var connectionB = ArrConnectionId.For(ArrProviderKind.Radarr, "http://other.local:7878");

        var first = new RadarrIdentity(connectionA, 7, ArrFileIdentity.Present(42));
        var same = new RadarrIdentity(connectionA, 7, ArrFileIdentity.Present(42));
        var otherConnection = new RadarrIdentity(connectionB, 7, ArrFileIdentity.Present(42));

        Assert.Equal(first, same);
        Assert.Equal(first.GetHashCode(), same.GetHashCode());
        Assert.NotEqual(first, otherConnection);
    }

    [Fact]
    public void RadarrIdentityEqualityIncludesFileIdentityPresenceAndValue()
    {
        var connection = ArrConnectionId.For(ArrProviderKind.Radarr, "http://radarr.local:7878");
        var present = new RadarrIdentity(connection, 7, ArrFileIdentity.Present(42));
        var changedFile = new RadarrIdentity(connection, 7, ArrFileIdentity.Present(43));
        var absentFile = new RadarrIdentity(connection, 7, ArrFileIdentity.Absent);
        var changedMovie = new RadarrIdentity(connection, 8, ArrFileIdentity.Present(42));

        Assert.NotEqual(present, changedFile);
        Assert.NotEqual(present, absentFile);
        Assert.NotEqual(present, changedMovie);
    }

    [Fact]
    public void SonarrEpisodeIdentityDistinguishesSeriesEpisodeAndFile()
    {
        var connection = ArrConnectionId.For(ArrProviderKind.Sonarr, "http://sonarr.local:8989");
        var episode = new SonarrIdentity(connection, 12, 73, ArrFileIdentity.Present(418));
        var same = new SonarrIdentity(connection, 12, 73, ArrFileIdentity.Present(418));
        var differentEpisode = new SonarrIdentity(connection, 12, 74, ArrFileIdentity.Present(418));
        var differentFile = new SonarrIdentity(connection, 12, 73, ArrFileIdentity.Present(419));
        var absentFile = new SonarrIdentity(connection, 12, 73, ArrFileIdentity.Absent);

        Assert.Equal(episode, same);
        Assert.NotEqual(episode, differentEpisode);
        Assert.NotEqual(episode, differentFile);
        Assert.NotEqual(episode, absentFile);
    }

    [Fact]
    public void SonarrEpisodeIdentityRequiresExplicitFileIdentity()
    {
        var connection = ArrConnectionId.For(ArrProviderKind.Sonarr, "http://sonarr.local:8989");

        Assert.Throws<ArgumentException>(() => new SonarrIdentity(connection, 12, 73, null!));
    }

    [Fact]
    public void SonarrSeriesIdentityCarriesSeriesOnly()
    {
        var connection = ArrConnectionId.For(ArrProviderKind.Sonarr, "http://sonarr.local:8989");
        var series = new SonarrIdentity(connection, 12);

        Assert.Equal(12, series.SeriesId);
        Assert.Null(series.EpisodeId);
        Assert.Null(series.EpisodeFileIdentity);
        Assert.Equal(ArrProviderKind.Sonarr, series.ProviderKind);
    }

    [Fact]
    public void RecordIdentityCannotMixSonarrAndRadarrShapes()
    {
        var connection = ArrConnectionId.For(ArrProviderKind.Sonarr, "http://media.local:8080");

        var sonarr = new SonarrIdentity(connection, 7, 8, ArrFileIdentity.Absent);
        var radarr = new RadarrIdentity(connection, 7, ArrFileIdentity.Absent);

        Assert.NotEqual<ArrRecordIdentity>(sonarr, radarr);
    }
}
