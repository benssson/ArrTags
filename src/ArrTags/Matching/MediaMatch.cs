using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using ArrTags.Media;
using ArrTags.Providers;

namespace ArrTags.Matching;

/// <summary>
/// The validated, connection-scoped relationship between a Jellyfin item and one
/// Arr record. A match may be unresolved, ambiguous, unsupported, or stale rather
/// than silently guessed; only <see cref="MediaMatchStatus.Matched"/> permits
/// provider metadata to be used for a badge. The match carries a typed
/// <see cref="ArrRecordIdentity"/>, never a provider DTO.
/// </summary>
public sealed class MediaMatch
{
    /// <summary>
    /// The version of the match meaning and fingerprint semantics. It is included
    /// in the fingerprint so a semantic change invalidates dependent state.
    /// </summary>
    public const int MatchSchemaVersion = 1;

    private static readonly IReadOnlyDictionary<string, string> NoProviderIds =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            .ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Initializes a new instance of the <see cref="MediaMatch"/> class and
    /// computes its deterministic fingerprint.
    /// </summary>
    /// <param name="mediaIdentity">The Jellyfin-side subject being matched.</param>
    /// <param name="provider">The provider identity the match is scoped to.</param>
    /// <param name="connectionId">The connection scope for the contained local identifiers.</param>
    /// <param name="status">The bounded match status.</param>
    /// <param name="matchMethod">How the match was established, or <see cref="MediaMatchMethod.None"/> when none.</param>
    /// <param name="recordIdentity">The connection-scoped record identity; required when the status is <see cref="MediaMatchStatus.Matched"/>.</param>
    /// <param name="matchedProviderIds">The provider identifiers that agreed, for diagnostics and invalidation.</param>
    /// <param name="ambiguityReason">A safe reason for a non-matched status; never contains a credential.</param>
    /// <param name="matchedAt">When the match was last validated, when known.</param>
    /// <exception cref="ArgumentNullException">A required value is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The status or method is undefined.</exception>
    /// <exception cref="ArgumentException">The status and identity evidence are inconsistent, or the record identity does not belong to the supplied connection and provider scope.</exception>
    public MediaMatch(
        MediaIdentity mediaIdentity,
        ArrProvider provider,
        ArrConnectionId connectionId,
        MediaMatchStatus status,
        MediaMatchMethod matchMethod,
        ArrRecordIdentity? recordIdentity = null,
        IReadOnlyDictionary<string, string>? matchedProviderIds = null,
        string? ambiguityReason = null,
        DateTimeOffset? matchedAt = null)
    {
        ArgumentNullException.ThrowIfNull(mediaIdentity);
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentNullException.ThrowIfNull(connectionId);

        if (!Enum.IsDefined(status))
        {
            throw new ArgumentOutOfRangeException(nameof(status), status, "Unknown match status.");
        }

        if (!Enum.IsDefined(matchMethod))
        {
            throw new ArgumentOutOfRangeException(nameof(matchMethod), matchMethod, "Unknown match method.");
        }

        if (status == MediaMatchStatus.Matched && recordIdentity is null)
        {
            throw new ArgumentException("A matched status requires a connection-scoped record identity.", nameof(recordIdentity));
        }

        if (status == MediaMatchStatus.Matched && matchMethod == MediaMatchMethod.None)
        {
            throw new ArgumentException("A matched status requires a concrete matching method.", nameof(matchMethod));
        }

        if ((status is MediaMatchStatus.NotFound or MediaMatchStatus.Ambiguous or MediaMatchStatus.Unsupported)
            && recordIdentity is not null)
        {
            throw new ArgumentException(
                "A non-matched status cannot carry a record identity.",
                nameof(recordIdentity));
        }

        if (recordIdentity is not null)
        {
            if (!recordIdentity.ConnectionId.Equals(connectionId))
            {
                throw new ArgumentException(
                    "A record identity must be scoped to the match connection.",
                    nameof(recordIdentity));
            }

            if (recordIdentity.ProviderKind != provider.Kind)
            {
                throw new ArgumentException(
                    "A record identity must belong to the match provider kind.",
                    nameof(recordIdentity));
            }
        }

        MediaIdentity = mediaIdentity;
        Provider = provider;
        ConnectionId = connectionId;
        Status = status;
        MatchMethod = matchMethod;
        RecordIdentity = recordIdentity;
        MatchedProviderIds = NormalizeProviderIds(matchedProviderIds);
        AmbiguityReason = string.IsNullOrWhiteSpace(ambiguityReason) ? null : ambiguityReason.Trim();
        MatchedAt = matchedAt;
        MatchFingerprint = ComputeFingerprint();
    }

    /// <summary>
    /// Gets the Jellyfin-side subject being matched.
    /// </summary>
    public MediaIdentity MediaIdentity { get; }

    /// <summary>
    /// Gets the provider identity the match is scoped to.
    /// </summary>
    public ArrProvider Provider { get; }

    /// <summary>
    /// Gets the connection scope for the contained local identifiers.
    /// </summary>
    public ArrConnectionId ConnectionId { get; }

    /// <summary>
    /// Gets the bounded match status. Only <see cref="MediaMatchStatus.Matched"/>
    /// permits provider metadata to be used for a badge.
    /// </summary>
    public MediaMatchStatus Status { get; }

    /// <summary>
    /// Gets how the match was established, or <see cref="MediaMatchMethod.None"/>
    /// when no match was accepted.
    /// </summary>
    public MediaMatchMethod MatchMethod { get; }

    /// <summary>
    /// Gets the connection-scoped record and file identity when the status is
    /// <see cref="MediaMatchStatus.Matched"/> or <see cref="MediaMatchStatus.Stale"/>.
    /// </summary>
    public ArrRecordIdentity? RecordIdentity { get; }

    /// <summary>
    /// Gets the provider identifiers that agreed. It is diagnostic and
    /// invalidation evidence, not ownership proof.
    /// </summary>
    public IReadOnlyDictionary<string, string> MatchedProviderIds { get; }

    /// <summary>
    /// Gets a safe reason for a non-matched status. It never contains a
    /// credential.
    /// </summary>
    public string? AmbiguityReason { get; }

    /// <summary>
    /// Gets when the match was last validated, when known.
    /// </summary>
    public DateTimeOffset? MatchedAt { get; }

    /// <summary>
    /// Gets the deterministic fingerprint over the match schema version, the
    /// Jellyfin subject, the connection and provider scope, the status and
    /// method, every record identity component including explicit file presence,
    /// and the matching provider identifiers. It excludes the observation
    /// timestamp and never contains a credential.
    /// </summary>
    public string MatchFingerprint { get; }

    private string ComputeFingerprint()
    {
        var builder = new StringBuilder();
        Append(builder, "matchSchemaVersion", MatchSchemaVersion.ToString(CultureInfo.InvariantCulture));
        Append(builder, "itemId", MediaIdentity.JellyfinItemId.ToString("N", CultureInfo.InvariantCulture));
        Append(builder, "itemType", MediaIdentity.ItemType.ToString());
        Append(builder, "providerKind", Provider.Kind.ToApiName());
        Append(builder, "providerInstanceId", Provider.ProviderInstanceId);
        Append(builder, "connectionId", ConnectionId.Value);
        Append(builder, "status", Status.ToString());
        Append(builder, "method", MatchMethod.ToString());
        Append(builder, "recordIdentity", RecordIdentity?.ToString());
        Append(builder, "matchedProviderIds", string.Join(
            ";",
            MatchedProviderIds
                .Select(pair => pair.Key.ToUpperInvariant() + "=" + pair.Value)
                .OrderBy(entry => entry, StringComparer.Ordinal)));

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString())));
    }

    private static void Append(StringBuilder builder, string name, string? value)
    {
        builder.Append(name).Append('=').Append(value ?? string.Empty).Append('\n');
    }

    private static IReadOnlyDictionary<string, string> NormalizeProviderIds(IReadOnlyDictionary<string, string>? providerIds)
    {
        if (providerIds is null || providerIds.Count == 0)
        {
            return NoProviderIds;
        }

        var copy = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in providerIds)
        {
            if (!string.IsNullOrWhiteSpace(pair.Key) && !string.IsNullOrWhiteSpace(pair.Value))
            {
                copy[pair.Key.Trim()] = pair.Value.Trim();
            }
        }

        return copy.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
    }
}
