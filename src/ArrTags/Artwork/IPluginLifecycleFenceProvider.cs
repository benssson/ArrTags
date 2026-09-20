namespace ArrTags.Artwork;

/// <summary>
/// The host-neutral boundary that reports the lifecycle fence implied by the
/// host's current plugin install state. It is consulted only when the durable
/// fence is normal, so a plain server shutdown does not trigger restoration. The
/// single Jellyfin implementation reads the plugin manifest status through the
/// supported plugin manager API; no host type escapes this interface.
/// </summary>
public interface IPluginLifecycleFenceProvider
{
    /// <summary>
    /// Gets the lifecycle fence implied by the host's current plugin state, or
    /// <see cref="ArtworkLifecycleFence.Normal"/> when the plugin is active or the
    /// state cannot be determined.
    /// </summary>
    /// <returns>The implied lifecycle fence.</returns>
    ArtworkLifecycleFence GetLifecycleFence();
}
