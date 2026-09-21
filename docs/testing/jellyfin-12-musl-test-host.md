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
  ArrTags state under `data/plugins/ArrTags` while using the versioned layout;
  the release blocker is recorded in `PLANS.md` and
  `docs/implementation/7.2/worker-report.json`.
