using System;
using System.Collections.Generic;
using System.Text.Json;
using ArrTags.Providers;

namespace ArrTags.Webhooks;

/// <summary>
/// Parses an authenticated inbound Arr webhook payload into the bounded,
/// provider-neutral <see cref="WebhookEvent"/>. Parsing is tolerant of unknown
/// JSON fields, case differences in property names, and the provider-specific
/// event vocabulary, but it is bounded: the caller enforces the byte limit, the
/// JSON depth is capped, and only a bounded number of episode entries is read.
/// A malformed, empty, or wrong-shaped payload is reported as a safe
/// <see cref="WebhookParseError"/> and never produces work (ADR-012).
/// </summary>
public static class WebhookEventParser
{
    /// <summary>
    /// The maximum JSON nesting depth accepted from a webhook payload.
    /// </summary>
    public const int MaxJsonDepth = 16;

    /// <summary>
    /// The bounded maximum number of episode entries read from a Sonarr payload.
    /// </summary>
    public const int MaxEpisodeEntries = 32;

    /// <summary>
    /// Attempts to parse a bounded webhook payload for the provider family the
    /// receiving route identified.
    /// </summary>
    /// <param name="payload">The bounded request body.</param>
    /// <param name="providerKind">The provider family the route identified.</param>
    /// <param name="webhookEvent">The parsed bounded event when the payload is usable.</param>
    /// <param name="error">The bounded parse outcome.</param>
    /// <returns><see langword="true"/> when the payload was usable.</returns>
    public static bool TryParse(
        ReadOnlyMemory<byte> payload,
        ArrProviderKind providerKind,
        out WebhookEvent? webhookEvent,
        out WebhookParseError error)
    {
        webhookEvent = null;

        if (payload.IsEmpty)
        {
            error = WebhookParseError.Empty;
            return false;
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(payload, new JsonDocumentOptions
            {
                MaxDepth = MaxJsonDepth,
                AllowTrailingCommas = false,
                CommentHandling = JsonCommentHandling.Disallow,
            });
        }
        catch (JsonException)
        {
            error = WebhookParseError.Malformed;
            return false;
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !TryGetProperty(root, "eventType", out var eventTypeElement)
                || eventTypeElement.ValueKind != JsonValueKind.String)
            {
                error = WebhookParseError.WrongShape;
                return false;
            }

            var rawEventType = eventTypeElement.GetString();
            if (string.IsNullOrWhiteSpace(rawEventType))
            {
                error = WebhookParseError.WrongShape;
                return false;
            }

            var eventType = MapEventType(rawEventType);
            var isUpgrade = ReadBool(root, "isUpgrade");

            if (eventType == WebhookEventType.Unsupported)
            {
                webhookEvent = new WebhookEvent(providerKind, eventType, isUpgrade);
                error = WebhookParseError.None;
                return true;
            }

            if (providerKind == ArrProviderKind.Radarr)
            {
                if (!TryReadRadarr(root, out var movieId, out var movieFileId))
                {
                    error = WebhookParseError.WrongShape;
                    return false;
                }

                webhookEvent = new WebhookEvent(
                    providerKind,
                    eventType,
                    isUpgrade,
                    movieId: movieId,
                    movieFileId: movieFileId);
                error = WebhookParseError.None;
                return true;
            }

            if (providerKind == ArrProviderKind.Sonarr)
            {
                if (!TryReadSonarr(root, out var seriesId, out var episodeIds, out var episodeFileId))
                {
                    error = WebhookParseError.WrongShape;
                    return false;
                }

                webhookEvent = new WebhookEvent(
                    providerKind,
                    eventType,
                    isUpgrade,
                    seriesId: seriesId,
                    episodeIds: episodeIds,
                    episodeFileId: episodeFileId);
                error = WebhookParseError.None;
                return true;
            }

            error = WebhookParseError.WrongShape;
            return false;
        }
    }

    private static WebhookEventType MapEventType(string rawEventType)
    {
        return rawEventType.ToUpperInvariant() switch
        {
            "DOWNLOAD" => WebhookEventType.Download,
            "RENAME" => WebhookEventType.Rename,
            "EPISODEFILEDELETE" => WebhookEventType.FileDelete,
            "MOVIEFILEDELETE" => WebhookEventType.FileDelete,
            "SERIESADD" => WebhookEventType.Added,
            "MOVIEADDED" => WebhookEventType.Added,
            "SERIESDELETE" => WebhookEventType.Deleted,
            "MOVIEDELETE" => WebhookEventType.Deleted,
            _ => WebhookEventType.Unsupported,
        };
    }

    private static bool TryReadRadarr(JsonElement root, out int? movieId, out int? movieFileId)
    {
        movieId = ReadNestedInt(root, "movie", "id");
        movieFileId = ReadNestedInt(root, "movieFile", "id");

        // A relevant Radarr event must identify a local movie; the movie-file id
        // is an optional file-change hint.
        return movieId is > 0;
    }

    private static bool TryReadSonarr(
        JsonElement root,
        out int? seriesId,
        out IReadOnlyList<int> episodeIds,
        out int? episodeFileId)
    {
        seriesId = ReadNestedInt(root, "series", "id");
        episodeFileId = ReadNestedInt(root, "episodeFile", "id");

        var episodes = ReadEpisodeEntries(root, out var firstSeriesId);
        seriesId ??= firstSeriesId;
        episodeIds = episodes;

        if (episodeFileId is null)
        {
            episodeFileId = ReadFirstArrayItemInt(root, "episodeFiles", "id");
        }

        // A relevant Sonarr event must identify a local series; episode and file
        // ids are optional hints.
        return seriesId is > 0;
    }

    private static IReadOnlyList<int> ReadEpisodeEntries(JsonElement root, out int? firstSeriesId)
    {
        firstSeriesId = null;

        if (!TryGetProperty(root, "episodes", out var episodes) || episodes.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<int>();
        }

        var ids = new List<int>();
        var inspected = 0;
        foreach (var episode in episodes.EnumerateArray())
        {
            if (inspected++ >= MaxEpisodeEntries)
            {
                break;
            }

            if (episode.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var episodeId = ReadInt(episode, "id");
            if (episodeId is > 0 && !ids.Contains(episodeId.Value))
            {
                ids.Add(episodeId.Value);
            }

            if (firstSeriesId is null)
            {
                var candidateSeriesId = ReadInt(episode, "seriesId");
                if (candidateSeriesId is > 0)
                {
                    firstSeriesId = candidateSeriesId;
                }
            }
        }

        return ids;
    }

    private static int? ReadFirstArrayItemInt(JsonElement root, string arrayName, string propertyName)
    {
        if (!TryGetProperty(root, arrayName, out var array) || array.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var inspected = 0;
        foreach (var item in array.EnumerateArray())
        {
            if (inspected++ >= MaxEpisodeEntries)
            {
                break;
            }

            if (item.ValueKind == JsonValueKind.Object)
            {
                var value = ReadInt(item, propertyName);
                if (value is > 0)
                {
                    return value;
                }
            }
        }

        return null;
    }

    private static int? ReadNestedInt(JsonElement root, string objectName, string propertyName)
    {
        return TryGetProperty(root, objectName, out var nested) && nested.ValueKind == JsonValueKind.Object
            ? ReadInt(nested, propertyName)
            : null;
    }

    private static int? ReadInt(JsonElement element, string propertyName)
    {
        return TryGetProperty(element, propertyName, out var value)
            && value.ValueKind == JsonValueKind.Number
            && value.TryGetInt32(out var parsed)
            ? parsed
            : null;
    }

    private static bool ReadBool(JsonElement element, string propertyName)
    {
        return TryGetProperty(element, propertyName, out var value)
            && value.ValueKind == JsonValueKind.True;
    }

    private static bool TryGetProperty(JsonElement element, string propertyName, out JsonElement value)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase))
                {
                    value = property.Value;
                    return true;
                }
            }
        }

        value = default;
        return false;
    }
}
