using System;

namespace ArrTags.Artwork;

/// <summary>
/// The bounded decision of whether one subject's artwork must be (re)generated
/// or published again. <see cref="ShouldGenerate"/> is <see langword="false"/>
/// only when the publication fingerprint and the relevant renderer/schema
/// identities are unchanged, when the metadata is not usable as current, or when
/// the current ownership state does not permit publication. In every one of those
/// cases no render is performed and no image is mutated.
/// </summary>
public sealed class ArtworkRegenerationDecision
{
    private ArtworkRegenerationDecision(bool shouldGenerate, string reason, string? desiredFingerprint)
    {
        ShouldGenerate = shouldGenerate;
        Reason = reason;
        DesiredFingerprint = desiredFingerprint;
    }

    /// <summary>
    /// Gets a value indicating whether artwork generation must run.
    /// </summary>
    public bool ShouldGenerate { get; }

    /// <summary>
    /// Gets a bounded, non-secret explanation of the decision.
    /// </summary>
    public string Reason { get; }

    /// <summary>
    /// Gets the fingerprint the current output-affecting inputs would produce, or
    /// <see langword="null"/> when a new recoverable source baseline is required.
    /// </summary>
    public string? DesiredFingerprint { get; }

    /// <summary>
    /// Creates a decision that no artwork work is required.
    /// </summary>
    /// <param name="reason">A bounded, non-secret explanation.</param>
    /// <returns>A skip decision.</returns>
    /// <exception cref="ArgumentException">The reason is empty.</exception>
    public static ArtworkRegenerationDecision Skip(string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        return new ArtworkRegenerationDecision(false, reason, null);
    }

    /// <summary>
    /// Creates a decision that artwork must be generated.
    /// </summary>
    /// <param name="reason">A bounded, non-secret explanation.</param>
    /// <param name="desiredFingerprint">The desired output fingerprint when it could be computed, or <see langword="null"/>.</param>
    /// <returns>A generate decision.</returns>
    /// <exception cref="ArgumentException">The reason is empty.</exception>
    public static ArtworkRegenerationDecision Generate(string reason, string? desiredFingerprint = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        return new ArtworkRegenerationDecision(true, reason, desiredFingerprint);
    }
}
