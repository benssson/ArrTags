namespace ArrTags.Artwork;

/// <summary>
/// The pure generation and lifecycle-fence decisions that reject stale artwork
/// work. Generation is monotonic per item/image surface, so a candidate at an
/// older generation can never overwrite a newer durable record, and a lifecycle
/// fence prevents accepting new publication work during disable, uninstall, or
/// confirmed item removal. This type performs no lifecycle event wiring; the
/// later lifecycle task raises the fence and the store enforces it.
/// </summary>
public static class ArtworkOperationFencing
{
    /// <summary>
    /// Determines whether a new publication operation may be accepted under the
    /// lifecycle fence. Only a normal fence permits new publication work; a
    /// disable, uninstall, or confirmed item removal refuses it.
    /// </summary>
    /// <param name="fence">The active lifecycle fence.</param>
    /// <returns><see langword="true"/> when new publication work may be accepted.</returns>
    public static bool AllowsNewPublication(ArtworkLifecycleFence fence)
    {
        return fence == ArtworkLifecycleFence.Normal;
    }

    /// <summary>
    /// Determines whether a restoration operation may be accepted under the
    /// lifecycle fence. Disable and uninstall require restoration, so they permit
    /// it; a confirmed item removal performs no image mutation at all and refuses
    /// it.
    /// </summary>
    /// <param name="fence">The active lifecycle fence.</param>
    /// <returns><see langword="true"/> when restoration work may be accepted.</returns>
    public static bool AllowsNewRestoration(ArtworkLifecycleFence fence)
    {
        return fence is ArtworkLifecycleFence.Normal
            or ArtworkLifecycleFence.Disable
            or ArtworkLifecycleFence.Uninstall;
    }

    /// <summary>
    /// Determines whether a candidate generation is stale relative to the durable
    /// generation. A stale generation must never overwrite the newer record.
    /// </summary>
    /// <param name="durableGeneration">The generation already durable for the subject.</param>
    /// <param name="candidateGeneration">The generation of the candidate work.</param>
    /// <returns><see langword="true"/> when the candidate is stale.</returns>
    public static bool IsStale(long durableGeneration, long candidateGeneration)
    {
        return candidateGeneration < durableGeneration;
    }

    /// <summary>
    /// Determines whether a candidate generation may supersede the durable
    /// generation. Only a strictly newer generation replaces a durable record.
    /// </summary>
    /// <param name="durableGeneration">The generation already durable for the subject.</param>
    /// <param name="candidateGeneration">The generation of the candidate work.</param>
    /// <returns><see langword="true"/> when the candidate may supersede the durable record.</returns>
    public static bool CanSupersede(long durableGeneration, long candidateGeneration)
    {
        return candidateGeneration > durableGeneration;
    }

    /// <summary>
    /// Determines whether a candidate generation is the same generation as the
    /// durable record, which means it advances that same operation rather than
    /// replacing it.
    /// </summary>
    /// <param name="durableGeneration">The generation already durable for the subject.</param>
    /// <param name="candidateGeneration">The generation of the candidate work.</param>
    /// <returns><see langword="true"/> when the generations are equal.</returns>
    public static bool IsSameGeneration(long durableGeneration, long candidateGeneration)
    {
        return candidateGeneration == durableGeneration;
    }
}
