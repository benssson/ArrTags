# Project Goals

## Purpose

Create a Jellyfin plugin that automatically enhances media posters with visual badges containing metadata retrieved from Sonarr and Radarr.

The plugin should provide users with a convenient way to surface useful media information directly on Jellyfin's poster artwork without requiring manual poster editing.

## Primary Goals

### Jellyfin compatibility

* Support Jellyfin 12.
* Follow Jellyfin's plugin architecture and APIs appropriate for Jellyfin 12.
* Avoid relying on undocumented or version-specific internals where a supported API is available.

### Sonarr and Radarr integration

* Connect to Sonarr through its API.
* Connect to Radarr through its API.
* Retrieve metadata for the corresponding media item.
* Support metadata that is useful for displaying as poster badges, such as media quality.
* Handle unavailable, incomplete, or invalid metadata gracefully.

### Poster badges

* Render metadata as visually distinct badges on the item's Jellyfin poster.
* Allow the badge content and presentation to be configurable where appropriate.
* Preserve the original poster source and provide guarded restoration while
  avoiding modifications to original media files.
* Avoid repeatedly processing the same poster when no relevant metadata has changed.

### Jellyfin Enhanced compatibility

* Be compatible with the Jellyfin Enhanced plugin.
* Avoid interfering with functionality provided by Jellyfin Enhanced.
* Ensure that poster modifications remain compatible with Jellyfin Enhanced's handling of media artwork.

## User Experience Goals

The plugin should:

* Require minimal manual intervention after initial configuration.
* Automatically update badges when relevant Sonarr or Radarr metadata changes.
* Make it clear which metadata is being displayed.
* Fail gracefully when Sonarr/Radarr is unavailable or an item cannot be matched.
* Avoid noticeably degrading Jellyfin's responsiveness or library performance.

## Reliability and Performance

* Avoid unnecessary API requests to Sonarr and Radarr.
* Avoid unnecessary image processing.
* Avoid blocking Jellyfin operations with long-running work.
* Handle Jellyfin restarts and plugin reloads safely.
* Ensure partially completed or failed poster processing does not leave media in an unusable state.

## Scope

### In scope

* Jellyfin 12 plugin implementation.
* Sonarr API integration.
* Radarr API integration.
* Matching Jellyfin media items with their corresponding Sonarr/Radarr items.
* Retrieval of relevant metadata.
* Rendering metadata as poster badges.
* Configuration of Sonarr/Radarr connections and badge behaviour.
* Integration with Jellyfin Enhanced.

### Initially out of scope

* Supporting Jellyfin versions earlier than Jellyfin 12.
* Modifying the original media files.
* Replacing or managing Sonarr/Radarr metadata.
* Becoming a general-purpose poster/artwork management system.
* Supporting services other than Sonarr and Radarr unless subsequently required.

## Success Criteria

The project will be considered successful when:

1. The plugin installs and loads correctly on Jellyfin 12.
2. Sonarr and Radarr can be configured independently.
3. A Jellyfin movie or TV item can be matched to its corresponding Sonarr/Radarr item.
4. Metadata such as quality can be retrieved through the appropriate API.
5. The metadata is rendered as a badge on the item's poster.
6. Poster updates occur reliably without unnecessary repeated processing.
7. The plugin operates correctly alongside Jellyfin Enhanced.
8. Failures in Sonarr, Radarr, artwork processing, or matching do not adversely affect Jellyfin.
9. The project can be built and tested reproducibly.

## V1.1 Goals

V1.1 is an additive release. Its scope, goals, decision gates, and task outline
are defined in [`docs/plan/archive/v1.1-plan.md`](docs/plan/archive/v1.1-plan.md); the V1 scope and
success criteria above remain authoritative for V1. V1.1 decisions are recorded
as ADR-016 onward in [`docs/decisions.md`](docs/decisions.md).
