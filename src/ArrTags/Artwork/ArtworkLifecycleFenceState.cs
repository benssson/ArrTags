namespace ArrTags.Artwork;

/// <summary>
/// The bounded result of reading the durable active <see cref="ArtworkLifecycleFence"/>.
/// An absent record is a normal fence; an invalid record fails closed and can
/// never permit new publication work. The result carries no path, credential, or
/// entity.
/// </summary>
public sealed class ArtworkLifecycleFenceState
{
    private ArtworkLifecycleFenceState(ArtworkLifecycleFence fence, bool isValid, string? reason)
    {
        Fence = fence;
        IsValid = isValid;
        Reason = reason;
    }

    /// <summary>
    /// Gets the normal fence used when no record exists.
    /// </summary>
    public static ArtworkLifecycleFenceState Normal { get; } =
        new(ArtworkLifecycleFence.Normal, isValid: true, reason: null);

    /// <summary>
    /// Gets the active lifecycle fence. An invalid result reports the most
    /// restrictive fence so a caller that only inspects the value still fails
    /// closed.
    /// </summary>
    public ArtworkLifecycleFence Fence { get; }

    /// <summary>
    /// Gets a value indicating whether the durable record was readable and valid.
    /// </summary>
    public bool IsValid { get; }

    /// <summary>
    /// Gets a bounded, non-secret explanation when the record was invalid.
    /// </summary>
    public string? Reason { get; }

    /// <summary>
    /// Gets a value indicating whether new publication work may be accepted.
    /// Only a valid normal fence permits it.
    /// </summary>
    public bool AllowsNewPublication =>
        IsValid && ArtworkOperationFencing.AllowsNewPublication(Fence);

    /// <summary>
    /// Gets a value indicating whether new restoration work may be accepted.
    /// </summary>
    public bool AllowsNewRestoration =>
        IsValid && ArtworkOperationFencing.AllowsNewRestoration(Fence);

    /// <summary>
    /// Creates a valid active-fence result.
    /// </summary>
    /// <param name="fence">The active fence.</param>
    /// <param name="reason">An optional bounded, non-secret explanation.</param>
    /// <returns>A valid fence state.</returns>
    public static ArtworkLifecycleFenceState Create(ArtworkLifecycleFence fence, string? reason = null)
    {
        return new ArtworkLifecycleFenceState(fence, isValid: true, ArtworkOperationErrors.Sanitize(reason));
    }

    /// <summary>
    /// Creates a fail-closed invalid result.
    /// </summary>
    /// <param name="reason">A bounded, non-secret explanation.</param>
    /// <returns>An invalid fence state.</returns>
    public static ArtworkLifecycleFenceState Invalid(string reason)
    {
        return new ArtworkLifecycleFenceState(
            ArtworkLifecycleFence.Uninstall,
            isValid: false,
            ArtworkOperationErrors.Sanitize(reason));
    }
}
