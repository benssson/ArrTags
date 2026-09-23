using System;
using ArrTags.Matching;
using ArrTags.Metadata;

namespace ArrTags.Providers;

/// <summary>
/// The canonical, secret-free observation of one Arr record and its current file
/// as reused by the provider inventory cache (ADR-018 clause 1). It pairs the
/// provider-neutral <see cref="MatchCandidate"/> with the canonical
/// <see cref="BadgeMetadata"/> mapped from the record's current file, so the
/// cached inventory carries no provider DTO, credential, request URL, or
/// provider-specific concept. A record without an imported file carries an
/// explicit absent-file identity on the candidate and no file observation.
/// </summary>
public sealed class ArrInventoryRecordObservation
{
    /// <summary>
    /// Initializes a new instance of the
    /// <see cref="ArrInventoryRecordObservation"/> class.
    /// </summary>
    /// <param name="candidate">The canonical record and file identity plus matching context.</param>
    /// <param name="fileObservation">The canonical file observation when the record has an imported file.</param>
    /// <exception cref="ArgumentNullException">The candidate is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">The file observation does not belong to the candidate record.</exception>
    public ArrInventoryRecordObservation(MatchCandidate candidate, BadgeMetadata? fileObservation = null)
    {
        ArgumentNullException.ThrowIfNull(candidate);

        if (fileObservation is not null)
        {
            if (fileObservation.Provider.Kind != candidate.ProviderKind)
            {
                throw new ArgumentException(
                    "The file observation provider kind must match the candidate provider kind.",
                    nameof(fileObservation));
            }

            if (!fileObservation.RecordIdentity.Equals(candidate.RecordIdentity))
            {
                throw new ArgumentException(
                    "The file observation identity must match the candidate record identity.",
                    nameof(fileObservation));
            }
        }

        Candidate = candidate;
        FileObservation = fileObservation;
    }

    /// <summary>
    /// Gets the canonical record and file identity plus matching context.
    /// </summary>
    public MatchCandidate Candidate { get; }

    /// <summary>
    /// Gets the canonical file observation when the record has an imported file;
    /// otherwise <see langword="null"/> with an explicit absent-file identity on
    /// the candidate.
    /// </summary>
    public BadgeMetadata? FileObservation { get; }
}
