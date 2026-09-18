namespace ArrTags.Providers.Sonarr;

/// <summary>
/// The integration-boundary DTO for a Sonarr <c>MediaInfoResource</c>. Absent
/// technical values remain <see langword="null"/> and are unknown, not false.
/// </summary>
public sealed class SonarrMediaInfoResource
{
    /// <summary>
    /// Gets the audio channel count.
    /// </summary>
    public double? AudioChannels { get; init; }

    /// <summary>
    /// Gets the normalized audio codec.
    /// </summary>
    public string? AudioCodec { get; init; }

    /// <summary>
    /// Gets the reported audio languages.
    /// </summary>
    public string? AudioLanguages { get; init; }

    /// <summary>
    /// Gets the video bit depth.
    /// </summary>
    public int? VideoBitDepth { get; init; }

    /// <summary>
    /// Gets the normalized video codec.
    /// </summary>
    public string? VideoCodec { get; init; }

    /// <summary>
    /// Gets the video frame rate.
    /// </summary>
    public double? VideoFps { get; init; }

    /// <summary>
    /// Gets the dynamic-range family text.
    /// </summary>
    public string? VideoDynamicRange { get; init; }

    /// <summary>
    /// Gets the dynamic-range profile text.
    /// </summary>
    public string? VideoDynamicRangeType { get; init; }

    /// <summary>
    /// Gets the reported resolution text.
    /// </summary>
    public string? Resolution { get; init; }

    /// <summary>
    /// Gets the inspected video width in pixels.
    /// </summary>
    public int? Width { get; init; }

    /// <summary>
    /// Gets the inspected video height in pixels.
    /// </summary>
    public int? Height { get; init; }

    /// <summary>
    /// Gets the scan type.
    /// </summary>
    public string? ScanType { get; init; }

    /// <summary>
    /// Gets the reported subtitle languages.
    /// </summary>
    public string? Subtitles { get; init; }

    /// <summary>
    /// Gets the reported runtime text.
    /// </summary>
    public string? RunTime { get; init; }
}
