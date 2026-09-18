using System;
using System.Collections.Generic;
using System.Linq;

namespace ArrTags.Matching;

/// <summary>
/// The bounded result of applying ordered candidate-selection rules to a set of
/// provider candidates. It carries the surviving candidates and the recorded
/// evidence steps. It does not itself accept or reject a match: mapping the
/// survivor count to a <see cref="MediaMatchStatus"/> is a separate policy step.
/// </summary>
public sealed class CandidateSelection
{
    /// <summary>
    /// Initializes a new instance of the <see cref="CandidateSelection"/> class.
    /// </summary>
    /// <param name="survivors">The candidates selected by the applied rules.</param>
    /// <param name="evidence">The recorded evidence steps, in application order.</param>
    /// <exception cref="ArgumentNullException">A required value is <see langword="null"/>.</exception>
    public CandidateSelection(IReadOnlyList<MatchCandidate> survivors, IReadOnlyList<MatchEvidence> evidence)
    {
        ArgumentNullException.ThrowIfNull(survivors);
        ArgumentNullException.ThrowIfNull(evidence);

        Survivors = survivors.ToArray();
        Evidence = evidence.ToArray();
    }

    /// <summary>
    /// Gets the candidates selected by the applied rules.
    /// </summary>
    public IReadOnlyList<MatchCandidate> Survivors { get; }

    /// <summary>
    /// Gets the recorded evidence steps in application order.
    /// </summary>
    public IReadOnlyList<MatchEvidence> Evidence { get; }

    /// <summary>
    /// Gets the number of surviving candidates.
    /// </summary>
    public int SurvivorCount => Survivors.Count;

    /// <summary>
    /// Gets a value indicating whether no candidate survived.
    /// </summary>
    public bool IsEmpty => Survivors.Count == 0;

    /// <summary>
    /// Gets a value indicating whether exactly one candidate survived.
    /// </summary>
    public bool IsUnique => Survivors.Count == 1;

    /// <summary>
    /// Gets a value indicating whether more than one candidate survived.
    /// </summary>
    public bool IsAmbiguous => Survivors.Count > 1;

    /// <summary>
    /// Gets the single surviving candidate when exactly one survived.
    /// </summary>
    public MatchCandidate? UniqueCandidate => IsUnique ? Survivors[0] : null;

    /// <summary>
    /// Gets the last recorded evidence step, which is the step that selected the
    /// survivors, or <see langword="null"/> when no rule was applied.
    /// </summary>
    public MatchEvidence? DecidingEvidence => Evidence.Count == 0 ? null : Evidence[^1];
}
