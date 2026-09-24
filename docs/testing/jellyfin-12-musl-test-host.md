# Pinned Jellyfin 12.0.0 musl test host

This document records how to provision and run the **pinned Jellyfin `12.0.0`
host** used by the ArrTags testing/release verification tasks (Phase 7,
`PLANS.md` tasks 7.2-7.5) on this Alpine/musl environment. It is a test fixture
only: no host binary is shipped with the plugin and nothing here changes the
plugin build.

The reusable entry point is:

```bash
scripts/provision-jellyfin-test-host.sh [PREFIX]     # PREFIX defaults to /tmp/arrtags-jellyfin
```

## Environment

- Alpine Linux `3.24.2`, `x86_64`, **musl** libc, non-root user (uid 1000).
- .NET is not required by the host: the Jellyfin amd64-musl archive is a
  self-contained build that carries its own runtime.
- Network access to `repo.jellyfin.org` (server archive) and
  `dl-cdn.alpinelinux.org` (Alpine runtime packages) is required.
- `apk` (the package tool) is present but the user is non-root, so this recipe
  **fetches and extracts** packages into a private prefix instead of installing
  them system-wide.

## Pinned host artifact

| Item | Value |
| --- | --- |
| Server | Jellyfin `12.0.0` (`Jellyfin.Server 12.0.0.0`) |
| Archive | `https://repo.jellyfin.org/files/server/linux/stable/v12.0/amd64-musl/jellyfin_12.0-amd64-musl.tar.gz` |
| Archive size | ~119 MB (119,083,810 bytes) |
| Manifest ABI | `targetAbi: 12.0.0.0` (matches `build.yaml`) |

The **musl** archive (`amd64-musl`) is used deliberately. The default `amd64`
portable archive is glibc-linked and does not run under musl without a glibc
compatibility layer; this system has neither `gcompat` nor an `ld-linux` loader,
so the musl build is the only supported option here.

## Why the extra packages are required

The archived server extracts and launches, but three runtime dependencies are
not bundled and must be provided from Alpine packages (extracted, not installed):

1. **`fontconfig` (+ deps).** Jellyfin's bundled `libSkiaSharp.so` links
   `libfontconfig.so.1`. Without it the host aborts in `ConfigureServices` with
   `System.DllNotFoundException: Unable to load shared library 'libSkiaSharp'`
   / `Error loading shared library libfontconfig.so.1`. `apk fetch -R fontconfig`
   pulls `freetype`, `libexpat`, `libpng`, `libbz2`, `brotli-libs` and `zlib`.
2. **`icu-libs` + `icu-data-full`.** Jellyfin requires real culture support and
   constructs `en-US` during startup. Two failure modes were observed:
   - Without ICU data, .NET fails with `Could not load ICU data. UErrorCode: 2`.
   - Setting `DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1` (as the ArrTags test
     environment does) gets past the loader but then fails with
     `System.Globalization.CultureNotFoundException: ... en-US is an invalid
     culture identifier`.
   Therefore the host must run with real ICU **and** `DOTNET_SYSTEM_GLOBALIZATION_INVARIANT`
   must be unset. `icu-data-full` supplies `usr/share/icu/78.1/icudt78l.dat`,
   which is pointed at with `ICU_DATA`.
3. **`ffmpeg` (+ deps).** Jellyfin 12 refuses to start when no FFmpeg is found:
   `FFmpeg: Failed version check: ffmpeg` / `FFmpeg: Path set by system $PATH is
   invalid` followed by a fatal `Error while starting server`. Alpine's `ffmpeg`
   `8.1.2` is used via `--ffmpeg <prefix>/sysroot/root/usr/bin/ffmpeg`. Its
   `libpulse.so.0` needs `libpulsecommon-17.0.so`, which lives in
   `usr/lib/pulseaudio/`, so that directory must be on `LD_LIBRARY_PATH`.

## What the script does

1. Downloads and extracts the pinned server archive into `PREFIX/jellyfin`.
2. `apk fetch --no-cache -R -o PREFIX/apks fontconfig icu-libs icu-data-full ffmpeg`
   and extracts every `.apk` (they are gzip tarballs) into
   `PREFIX/sysroot/root`.
3. Stops any previous host and waits for port `8096` (override with
   `JELLYFIN_PORT`) to be free.
4. Writes `PREFIX/start-jellyfin.sh` (the launcher below) and starts it.
5. Waits for both `Startup complete` in the console log and a successful
   `GET /System/Info/Public`, then prints the data/config/cache/log directories.

### Launcher environment

```bash
export LD_LIBRARY_PATH="$SYSROOT/usr/lib:$SYSROOT/usr/lib/pulseaudio:$SYSROOT/lib:/config/sysroot/usr/lib:${LD_LIBRARY_PATH:-}"
export FONTCONFIG_PATH="$SYSROOT/etc/fonts"
export XDG_CACHE_HOME="$PREFIX/fccache"          # writable fontconfig cache
export ICU_DATA="$SYSROOT/usr/share/icu/78.1/"
export PATH="$SYSROOT/usr/bin:$PATH"
unset DOTNET_SYSTEM_GLOBALIZATION_INVARIANT      # Jellyfin needs real cultures
cd "$HOST_DIR"
exec ./jellyfin \
    --datadir  "$DATA_DIR" \
    --configdir "$CONFIG_DIR" \
    --cachedir "$CACHE_DIR" \
    --logdir   "$LOG_DIR" \
    --nowebclient \
    --ffmpeg   "$SYSROOT/usr/bin/ffmpeg"
```

`--nowebclient` is used because only the server-side API/plugin behavior is
needed; the archive does not ship the web client. The host therefore has no web
UI, which is sufficient for plugin discovery, install/upgrade/reload/uninstall,
route, and image-response verification.

## Verification

```bash
curl -sS http://127.0.0.1:8096/System/Info/Public
# {"LocalAddress":"http://127.0.0.1:8096","ServerName":"...","Version":"12.0.0",
#  "ProductName":"Jellyfin Server","OperatingSystem":"",...,"StartupWizardCompleted":false}
```

Host directories under `PREFIX`:

| Path | Purpose |
| --- | --- |
| `jellyfin/` | Extracted server and its managed/native assemblies (includes `Jellyfin.Api.dll`) |
| `sysroot/root/` | Extracted Alpine runtime packages (`usr/lib`, `usr/bin/ffmpeg`, ICU data) |
| `data/` | Jellyfin data (`--datadir`, Jellyfin's `ProgramDataPath`) and database; **plugins live in `data/plugins/`** |
| `config/` | Jellyfin configuration (`--configdir`); plugin configuration XML lives in `data/plugins/configurations/` |
| `cache/`, `log/` | Jellyfin cache and logs; `console.log` is the launcher console |

`--datadir` is Jellyfin's `ProgramDataPath`, so `PluginManager`'s `PluginsPath`
is `PREFIX/data/plugins` (confirmed by the `.jellyfin-plugin` marker and the
`configurations/` plugin-config directory created at startup), **not**
`PREFIX/config/plugins`. `--configdir` holds `system.xml`, `encoding.xml`, and
the other configuration files. A plugin package is extracted into a versioned
folder `data/plugins/<Name>_<Version>/`, which is the layout
`InstallationManager` produces for a repository install.

Because the host is running, `ARRTAGS_JELLYFIN_HOST_DIR` can be pointed at
`PREFIX/jellyfin` to unskip the reflection-over-`Jellyfin.Api.dll` host facts in
the ArrTags test suite.

## Caveats

- The prefix is under `/tmp` and is **ephemeral**; re-run the script to rebuild
  it. `/tmp/opencode` is not writable by the non-root user, so `/tmp/<prefix>` is
  used instead.
- A killed host may briefly remain as an unreaped **zombie** in this container
  (PID 1 does not reap). Zombies hold no socket; the script matches on the
  process `comm` and waits for the port to free rather than trusting `pgrep -x`.
- During startup the setup host binds the port before the main host does; a
  single early response is not proof of readiness, which is why the script waits
  for `Startup complete` as well.
- `StartupWizardCompleted` is `false`. Task 7.2 does not require the wizard;
  administrative API calls need the wizard completed or an API key, and plugin
  filesystem installation works without either.
- No live Sonarr or Radarr instance is available here; provider behavior remains
  contract/unit tested. Only the Jellyfin host is exercised live.
- Jellyfin derives a plugin's `DataFolderPath` as `PluginsPath/<assembly name>`
  (for ArrTags, `data/plugins/ArrTags`). Task 7.2 found that the standard
  versioned install folder `data/plugins/ArrTags_<version>/` and that data folder
  are treated by `PluginManager` as two versions of the same-named plugin, and
  the older/other folder is deleted on the next host restart. Do not leave
  ArrTags state under `data/plugins/ArrTags` while using the versioned layout.
  Task 7.7 resolved the plugin side of this by relocating the plugin state root
  to `ProgramDataPath/ArrTags` outside `PluginsPath` (ADR-014), so the plugin no
  longer creates `data/plugins/ArrTags`; an unreleased developer install that
  already created that folder must delete it once. The original blocker is
  recorded in `PLANS.md` and `docs/implementation/7.2/worker-report.json`.

## Mock Arr fixture and end-to-end verification (task 7.3)

Task 7.3 exercises the full plugin pipeline against this host without a live
Sonarr/Radarr instance, using committed test-support fixtures.

### Mock Sonarr/Radarr server

`scripts/mock-arr-fixture.cs` is a .NET 10 file-based app (it is **not** part of
`ArrTags.slnx` and adds no plugin dependency). Run it with:

```bash
. /config/arrtags-env.sh
ARRTAGS_MOCK_RADARR_KEY=radarrkey123 \
ARRTAGS_MOCK_SONARR_KEY=sonarrkey456 \
  dotnet run scripts/mock-arr-fixture.cs -- \
    --fixtures scripts/mock-arr-fixtures \
    --radarr-port 7878 --sonarr-port 8989
```

It serves the exact read endpoints the clients call (`/api/v3/system/status`,
`/api/v3/movie`, `/api/v3/moviefile?movieId=`, `/api/v3/series`,
`/api/v3/episode?seriesId=&includeEpisodeFile=true`, `/api/v3/episodeFile?seriesId=`),
requires the configured `X-Api-Key` (otherwise `401`), logs one line per request
(method, path, query, API-key presence/validity, status, file - never the key
value), and reads each payload from disk on every request. Editing a fixture file
therefore changes the served observation without restarting the mock, which is
how the changed/unchanged update behaviour is verified. Creating
`radarr/failure` or `sonarr/failure` makes that provider answer `503` while the
mock stays up. Body payloads include representative identity (`tmdbId`/`tvdbId`/
`imdbId`), quality, `mediaInfo`, custom formats, and at least one sparse
record with optional fields absent (ADR-013).

### Reproduction of the task 7.3 end-to-end run

1. Build, package, and install: `. /config/arrtags-env.sh && ./build.sh build &&
   ./build.sh package`, then extract `artifacts/ArrTags_1.1.0.0.zip` into
   `PREFIX/data/plugins/ArrTags_1.1.0.0` and run
   `scripts/provision-jellyfin-test-host.sh PREFIX`.
2. Configure the plugin by writing `PREFIX/data/plugins/configurations/ArrTags.xml`
   (enable each provider independently, set the mock base URL/API key) and
   restart the host; runtime configuration replacement is a documented Phase 7
   deferral, so a restart is the supported mechanism.
3. Create local media with `.nfo` provider ids and real poster images (a real
   tiny video per item so the location is an eligible local file), then add the
   libraries with `POST /Library/VirtualFolders?...&refreshLibrary=true` and
   complete the startup wizard / authenticate to read items. Disable the remote
   metadata and image fetchers in the library options so local `.nfo` metadata is
   authoritative and no external network is needed.
4. Trigger reconciliation with the ArrTags scheduled task
   (`POST /ScheduledTasks/Running/<taskId>`, id from `GET /ScheduledTasks`) or a
   library refresh, and inspect the mock request log, the plugin state under
   `PREFIX/data/ArrTags`, and the standard image route
   `GET /Items/{id}/Images/Primary` (anonymous).

### Task 7.3 finding and task 7.8 resolution

Task 7.3 found **release blocker 7.3-F1**: the then-packaged bundled
`SkiaSharp.dll`/`libSkiaSharp.so` conflicted fatally with this host's own
SkiaSharp, so the first badge publication through `ProviderManager.SaveImage`
aborted the process with `InvalidCastException` (types A/B are the host
default-context and plugin-context `SkiaSharp.dll`). Removing the two bundled
files from the installed plugin folder (leaving the plugin to share the host's
SkiaSharp) made the whole pipeline work.

Task 7.8 resolves this by ADR-015: the plugin no longer ships the renderer
runtime and shares the host's SkiaSharp through the default load context. The
re-run reproduction on this host with the committed package passes end to end:
the package loads with no error, a badge publishes with no host crash, the
standard image route serves the published bytes matching the persisted
`ActiveImageIdentity`, the original source posters are preserved, changed mock
metadata republishes and unchanged metadata does not, and a provider outage
leaves the host up with the current artwork unchanged. See `PLANS.md` task 7.8,
`docs/decisions.md` ADR-015, and `docs/implementation/7.8/worker-report.json`.

## v1.1 live matrix (Phase 14 task 14.3)

Task 14.3 re-ran the pinned-host end-to-end verification for the v1.1 release
candidate (`artifacts/ArrTags_1.1.0.0.zip`, 594,931 bytes, SHA-256
`85730fe7b3fb8b03c86a87228dc1043d42844b372a9493d4caf5bba4a7e836e1`) against a
fresh prefix (`/tmp/arrtags-14.3`). The machine-readable matrix is
`docs/implementation/14.3/live-verification.json`; the completion report is
`docs/implementation/14.3/worker-report.json`. The V1 sections above remain the
record for Phase 7.

The v1.1 matrix adds the Goal A/F/C settings-page, runtime-activation, post-save
re-render, configuration-rejection activity-log, inventory-cache, and logging
checks on top of the Phase 7 install/load, publication, source-preservation,
outage, restart, and uninstall checks. Reuse the task 7.3/7.8 reproduction as the
base and add the following.

### v1.1 procedure additions

1. Complete the startup wizard. `GET /Startup/User` creates the default first
   user; `POST /Startup/User` then sets the name/password (posting `User` before
   the `GET` returns `404` on this host), followed by `POST /Startup/Configuration`,
   `POST /Startup/RemoteAccess`, and `POST /Startup/Complete`.
2. Authenticate as the admin. On Jellyfin 12 the client header is
   `Authorization: MediaBrowser Client="...", Device="...", DeviceId="...", Version="..."`
   (not `X-Emby-Authorization`); `POST /Users/AuthenticateByName` with the
   `Username`/`Pw` body returns the access token.
3. The dashboard page routes are `GET /web/ConfigurationPage?name=ArrTags`
   (anonymous static resource) and `GET /web/ConfigurationPages`
   (elevation-gated). The configuration round-trip is `GET`/`POST
   /Plugins/40322d52-5680-449f-b33e-e01836ee2f46/Configuration`; an anonymous
   save is rejected with `401`.
4. Add the libraries with `EnableInternetProviders=false` **and
   `SaveLocalMetadata=false`**. `SaveLocalMetadata=true` makes Jellyfin's own
   local image saver rewrite the media folder (it writes the published badge as
   `folder.png`/`-thumb.png` and removes the original `poster.jpg`/`S01E01.jpg`
   sidecars), which invalidates the source-preservation check. This is host
   behavior, not ArrTags behavior, but it must stay disabled for the check to be
   meaningful.
5. Exercise the v1.1 rows: settings page load/save through the elevation-gated
   path; a valid save applying at runtime with no restart; a post-save re-render
   after a `Renderer.Position`/`Renderer.Size` change; the ADR-021 rejection
   (`POST` an invalid candidate such as `Sonarr.BaseUrl="not-a-url"`, then
   `GET /System/ActivityLog/Entries` and confirm exactly one bounded, secret-free
   Warning entry of Type `ArrTagsConfigurationRejected` and none for a valid
   save); inventory-cache counts in the mock request log (one `/api/v3/movie`
   and one `/api/v3/series` per reconciliation window with multiple work items);
   and configurable log verbosity observed in the host log at `Warning`,
   `Information`, and `Off` with no restart.

### v1.1 result

The v1.1 matrix passed: the `1.1.0.0` package installs and loads (`targetAbi`
`12.0.0.0`, no plugin-folder SkiaSharp, `0 [FTL]`), the settings page loads and
saves only through the elevation-gated path, a valid save activates at runtime
and re-renders existing posters without a restart, a rejected save writes
exactly one bounded secret-free activity-log entry and is not persisted, the
provider inventory cache serves one library read per connection per
reconciliation window and is invalidated by the post-save and library-refresh
triggers, the configurable log verbosity is honored and secret-free, and the
image-route readback equals `ActiveImageIdentity` with the original source
artwork byte-unchanged, a safe provider outage, and a clean
install/restart/uninstall.

One documented fail-closed observation: a full library scan that re-adopts the
local sidecar poster as the Primary image is detected as `OwnershipLost`, and
ArrTags then intentionally does not auto-republish (see `docs/data-model.md`
section 3.10.4 and `docs/architecture.md`); the readback serves the host image
until an explicit administrative action starts a new session. See findings
F-14.3-1 through F-14.3-4 in `docs/implementation/14.3/live-verification.json`.
