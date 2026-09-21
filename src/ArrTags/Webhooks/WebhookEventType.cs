namespace ArrTags.Webhooks;

/// <summary>
/// The bounded, provider-neutral event vocabulary an inbound Arr webhook can map
/// into. Unknown provider event names map to <see cref="Unsupported"/> and
/// produce no work. Only the event kinds that can affect a badge-relevant
/// provider record or file are considered reconciliation hints (ADR-012).
/// </summary>
public enum WebhookEventType
{
    /// <summary>An import or upgrade completed. Sonarr/Radarr <c>Download</c>.</summary>
    Download,

    /// <summary>Files were renamed in place. Sonarr/Radarr <c>Rename</c>.</summary>
    Rename,

    /// <summary>A media file was deleted. Sonarr <c>EpisodeFileDelete</c>, Radarr <c>MovieFileDelete</c>.</summary>
    FileDelete,

    /// <summary>A series or movie was added. Sonarr <c>SeriesAdd</c>, Radarr <c>MovieAdded</c>.</summary>
    Added,

    /// <summary>A series or movie was deleted. Sonarr <c>SeriesDelete</c>, Radarr <c>MovieDelete</c>.</summary>
    Deleted,

    /// <summary>
    /// Any other provider event (<c>Test</c>, <c>Grab</c>, <c>Health</c>,
    /// <c>ApplicationUpdate</c>, <c>ManualInteractionRequired</c>, or an unknown
    /// value). It is acknowledged but produces no work.
    /// </summary>
    Unsupported,
}
