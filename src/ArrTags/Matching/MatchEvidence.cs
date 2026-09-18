using System;
using System.Globalization;

namespace ArrTags.Matching;

/// <summary>
/// One recorded candidate-selection step. It captures the matching method and
/// evidence key that was applied, the value that satisfied it, and how many
/// candidates were considered and matched. It contains provider identifiers,
/// numbering, or mapped paths only and never contains a credential, request URL,
/// or provider payload.
/// </summary>
public sealed class MatchEvidence : IEquatable<MatchEvidence>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="MatchEvidence"/> class.
    /// </summary>
    /// <param name="method">The concrete matching method that was applied.</param>
    /// <param name="evidenceKey">The non-empty evidence key, such as a normalized provider identifier name.</param>
    /// <param name="matchedValue">The normalized value that satisfied the rule when any candidate matched.</param>
    /// <param name="candidateCount">The number of candidates the rule considered.</param>
    /// <param name="matchCount">The number of candidates that satisfied the rule.</param>
    /// <exception cref="ArgumentOutOfRangeException">The method is undefined or <see cref="MediaMatchMethod.None"/>, or a count is out of range.</exception>
    /// <exception cref="ArgumentException">The evidence key is empty.</exception>
    public MatchEvidence(
        MediaMatchMethod method,
        string evidenceKey,
        string? matchedValue,
        int candidateCount,
        int matchCount)
    {
        if (!Enum.IsDefined(method))
        {
            throw new ArgumentOutOfRangeException(nameof(method), method, "Unknown match method.");
        }

        if (method == MediaMatchMethod.None)
        {
            throw new ArgumentOutOfRangeException(
                nameof(method),
                method,
                "Match evidence requires a concrete matching method.");
        }

        if (string.IsNullOrWhiteSpace(evidenceKey))
        {
            throw new ArgumentException("Match evidence requires a non-empty evidence key.", nameof(evidenceKey));
        }

        if (candidateCount < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(candidateCount),
                candidateCount,
                "A candidate count cannot be negative.");
        }

        if (matchCount < 0 || matchCount > candidateCount)
        {
            throw new ArgumentOutOfRangeException(
                nameof(matchCount),
                matchCount,
                "A match count cannot be negative or exceed the candidate count.");
        }

        Method = method;
        EvidenceKey = evidenceKey;
        MatchedValue = string.IsNullOrWhiteSpace(matchedValue) ? null : matchedValue.Trim();
        CandidateCount = candidateCount;
        MatchCount = matchCount;
    }

    /// <summary>
    /// Gets the concrete matching method that was applied.
    /// </summary>
    public MediaMatchMethod Method { get; }

    /// <summary>
    /// Gets the evidence key the rule was applied under, such as a normalized
    /// provider identifier name.
    /// </summary>
    public string EvidenceKey { get; }

    /// <summary>
    /// Gets the normalized value shared by the matched candidates when the rule
    /// matched. It is <see langword="null"/> when the rule matched nothing.
    /// </summary>
    public string? MatchedValue { get; }

    /// <summary>
    /// Gets the number of candidates the rule considered.
    /// </summary>
    public int CandidateCount { get; }

    /// <summary>
    /// Gets the number of candidates that satisfied the rule.
    /// </summary>
    public int MatchCount { get; }

    /// <summary>
    /// Determines whether this evidence equals another evidence entry.
    /// </summary>
    /// <param name="other">The other evidence entry.</param>
    /// <returns><see langword="true"/> when every recorded value is equal.</returns>
    public bool Equals(MatchEvidence? other)
    {
        return other is not null
            && Method == other.Method
            && string.Equals(EvidenceKey, other.EvidenceKey, StringComparison.Ordinal)
            && string.Equals(MatchedValue, other.MatchedValue, StringComparison.Ordinal)
            && CandidateCount == other.CandidateCount
            && MatchCount == other.MatchCount;
    }

    /// <inheritdoc />
    public override bool Equals(object? obj)
    {
        return Equals(obj as MatchEvidence);
    }

    /// <inheritdoc />
    public override int GetHashCode()
    {
        return HashCode.Combine(Method, EvidenceKey, MatchedValue, CandidateCount, MatchCount);
    }

    /// <inheritdoc />
    public override string ToString()
    {
        return Method
            + ":" + EvidenceKey
            + "=" + (MatchedValue ?? string.Empty)
            + "[" + MatchCount.ToString(CultureInfo.InvariantCulture)
            + "/" + CandidateCount.ToString(CultureInfo.InvariantCulture) + "]";
    }
}
