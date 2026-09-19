namespace ArrTags.Artwork;

/// <summary>
/// The lifecycle fence recorded on a durable <see cref="ArtworkOperation"/>.
/// Disable, uninstall, and confirmed item removal prevent new publication work
/// and are drained before cleanup. The fence is modeled and evaluated here; the
/// lifecycle event wiring that raises it belongs to a later task.
/// </summary>
public enum ArtworkLifecycleFence
{
    /// <summary>Normal operation; new publication work may be accepted.</summary>
    Normal,

    /// <summary>The plugin is being disabled; new publication work is refused and restoration may proceed.</summary>
    Disable,

    /// <summary>The plugin is being uninstalled; new publication work is refused and restoration may proceed.</summary>
    Uninstall,

    /// <summary>The Jellyfin item was confirmed removed; no image mutation is permitted.</summary>
    ItemRemoved,
}
