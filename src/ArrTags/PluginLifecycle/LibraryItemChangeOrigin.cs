namespace ArrTags.PluginLifecycle;

/// <summary>
/// The origin of an observed Jellyfin library item change. It lets the event
/// boundary distinguish a general metadata/content change from an image-only
/// update, which includes ArrTags' own image publication, so an internal change
/// can be ignored without a provider, rendering, or image read.
/// </summary>
public enum LibraryItemChangeOrigin
{
    /// <summary>A general library metadata or content change.</summary>
    Library,

    /// <summary>An image-only update, including ArrTags' own item-image publication.</summary>
    Image,
}
