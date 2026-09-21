using System;
using ArrTags.Matching;
using ArrTags.Metadata;
using ArrTags.Providers;

namespace ArrTags.Reconciliation;

/// <summary>
/// The bounded outcome of reading and matching one provider for an item. A
/// successful read always carries the canonical match (which may be
/// non-matched); a failed read carries a redacted <see cref="ArrProviderError"/>
/// classified with the provider retry vocabulary. A matched read carries the
/// normalized badge metadata, or no metadata when the provider reported a match
/// without usable file metadata.
/// </summary>
public sealed class ArrMetadataReadResult
{
    private ArrMetadataReadResult(
        bool isSuccess,
        MediaMatch? match,
        BadgeMetadata? metadata,
        string? providerVersion,
        string? providerVersionToken,
        ArrProviderError? error)
    {
        IsSuccess = isSuccess;
        Match = match;
        Metadata = metadata;
        ProviderVersion = providerVersion;
        ProviderVersionToken = providerVersionToken;
        Error = error;
    }

    /// <summary>
    /// Gets a value indicating whether the provider read and match succeeded.
    /// </summary>
    public bool IsSuccess { get; }

    /// <summary>
    /// Gets the canonical match when the read succeeded.
    /// </summary>
    public MediaMatch? Match { get; }

    /// <summary>
    /// Gets the normalized metadata when the match succeeded and metadata was
    /// observed.
    /// </summary>
    public BadgeMetadata? Metadata { get; }

    /// <summary>
    /// Gets the observed provider version when available.
    /// </summary>
    public string? ProviderVersion { get; }

    /// <summary>
    /// Gets the observed provider revision token when available. Absence is normal.
    /// </summary>
    public string? ProviderVersionToken { get; }

    /// <summary>
    /// Gets the redacted failure when the read did not succeed.
    /// </summary>
    public ArrProviderError? Error { get; }

    /// <summary>
    /// Creates a successful read outcome.
    /// </summary>
    /// <param name="match">The canonical match.</param>
    /// <param name="metadata">The normalized metadata when the match succeeded.</param>
    /// <param name="providerVersion">The observed provider version when available.</param>
    /// <param name="providerVersionToken">The observed provider revision token when available.</param>
    /// <returns>The successful outcome.</returns>
    /// <exception cref="ArgumentNullException">The match is <see langword="null"/>.</exception>
    public static ArrMetadataReadResult Success(
        MediaMatch match,
        BadgeMetadata? metadata = null,
        string? providerVersion = null,
        string? providerVersionToken = null)
    {
        ArgumentNullException.ThrowIfNull(match);
        return new ArrMetadataReadResult(
            isSuccess: true,
            match,
            metadata,
            providerVersion,
            providerVersionToken,
            error: null);
    }

    /// <summary>
    /// Creates a failed read outcome classified with the provider retry vocabulary.
    /// </summary>
    /// <param name="error">The redacted failure.</param>
    /// <returns>The failed outcome.</returns>
    /// <exception cref="ArgumentNullException">The error is <see langword="null"/>.</exception>
    public static ArrMetadataReadResult Failure(ArrProviderError error)
    {
        ArgumentNullException.ThrowIfNull(error);
        return new ArrMetadataReadResult(
            isSuccess: false,
            match: null,
            metadata: null,
            providerVersion: null,
            providerVersionToken: null,
            error);
    }
}
