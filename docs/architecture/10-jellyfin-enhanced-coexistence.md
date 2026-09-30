# 10. Jellyfin Enhanced coexistence

ArrTags has no source-level dependency on Jellyfin Enhanced. Enhanced's quality
tags are client-side Web overlays; ArrTags' badges are persisted through the
standard server-side image path. DG-8 is resolved by
[ADR-011](../decisions/00-index.md), and the coexistence policy is:

- ArrTags does not implement automatic duplicate-badge detection, overlap
  suppression, or any dependency on Enhanced internals. Jellyfin Enhanced can
  choose where it draws its own overlays, so overlap handling is deferred to the
  user.
- ArrTags badge output is controlled only by the existing ArrTags configuration:
  the `BadgeMoviePosters`/`BadgeEpisodePosters` poster enable flags and the
  renderer selector enablement. No duplicate/overlap suppression knob or new
  coexistence field is introduced. A user who does not want overlapping
  presentation disables the relevant ArrTags poster surface or selector, or
  configures Enhanced.
- Enhanced's Spoiler Guard has no material effect on ArrTags badge display.
  ArrTags renders its derived badge normally and adds no special handling for a
  spoiler or hidden state; it never reads or reproduces Enhanced filter ordering.
- Native clients receive ArrTags badges without requiring the Web UI, and
  Jellyfin Web may show both systems and duplicate information. That duplicate
  presentation is a documented, user-managed outcome rather than an ArrTags
  detection problem.
- Documentation may recommend disabling overlapping Enhanced quality tags, but
  ArrTags does not alter Enhanced configuration.
