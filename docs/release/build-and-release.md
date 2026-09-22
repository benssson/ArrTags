# ArrTags build and release

This document is the canonical record for the ArrTags release build (Phase 7
task 7.5). It records the pinned toolchain and inputs, the supported version
ranges, the exact build/test/package commands, the reproducible clean-checkout
procedure, the release artifact identity, and the release checklist. It owns
Phase 7 acceptance criterion 5 ("The release artifact and build process are
reproducible and documented").

Architecture and accepted decisions stay authoritative in
`docs/architecture.md` and `docs/decisions.md`; the live pinned test host is
documented in `docs/testing/jellyfin-12-musl-test-host.md`.

## Pinned toolchain and inputs

| Item | Value | Source |
| --- | --- | --- |
| Plugin version | `0.1.0.0` | `build.yaml`, `Directory.Build.props` |
| Target framework | `net10.0` | `src/ArrTags/ArrTags.csproj` |
| Manifest ABI | `targetAbi: 12.0.0.0` | `build.yaml` |
| .NET SDK | `10.0.x` (validated with `10.0.401`) | `global.json` pins `10.0.0` with `rollForward: latestMinor` |
| .NET runtime | `Microsoft.NETCore.App` / `Microsoft.AspNetCore.App` `10.0.12` | build environment |
| Jellyfin host packages | `Jellyfin.Controller` `12.0.0`, `Jellyfin.Model` `12.0.0` (`ExcludeAssets=runtime`) | `src/ArrTags/ArrTags.csproj`, `packages.lock.json` |
| Renderer stack (compile-time only) | `SkiaSharp` `3.119.4`, `SkiaSharp.NativeAssets.Linux` `3.119.4` (`ExcludeAssets=runtime`) | ADR-015 |
| Test packages | `Microsoft.NET.Test.Sdk` `18.10.1`, `xunit` `2.9.3`, `xunit.runner.visualstudio` `3.1.5`, `coverlet.collector` `10.0.1` | `tests/ArrTags.Tests/ArrTags.Tests.csproj` |
| Analyzers | `SerilogAnalyzer` `0.15.0`, `StyleCop.Analyzers` `1.2.0-beta.556`, `SmartAnalyzers.MultithreadingAnalyzer` `1.1.31` | `src/ArrTags/ArrTags.csproj` |
| Restore mode | locked (`--locked-mode`) | `build.sh`, `packages.lock.json` |

The plugin references the Jellyfin 12.0.0 packages with `ExcludeAssets=runtime`
and the renderer stack with `ExcludeAssets=runtime` (ADR-015), so the package
carries no Jellyfin or SkiaSharp runtime and the host provides both.

## Supported version ranges

- **Jellyfin:** `12.0.0` only (manifest `targetAbi: 12.0.0.0`, framework
  `net10.0`). No Jellyfin version before 12 is supported.
- **Sonarr:** `3.x`-`4.x` on the `/api/v3` contract.
- **Radarr:** `3.x`-`6.x` on the `/api/v3` contract.
- The provider ranges and the optional-field policy are ADR-013: absent optional
  fields map to explicit unknown values and a malformed required field fails
  closed as `ProviderIncompatible`, with no version-number gate. Provider
  contract fixtures (`ProviderContractFixtureTests`) exercise every declared
  line.

## Prerequisites

- `.NET SDK 10.0.x` on `PATH` (the repository pins `10.0.0` with
  `latestMinor` roll-forward).
- A NuGet package cache or network access for the locked package set; restore
  runs in locked mode.
- `dotnet run` support for the .NET 10 file-based packer
  (`scripts/pack-release.cs`).
- `unzip` for archive inspection/verification. No `zip` binary is required.
- For the host-guarded suite: the pinned Jellyfin `12.0.0` musl host, with
  `ARRTAGS_JELLYFIN_HOST_DIR` pointing at its extracted server directory (see
  `docs/testing/jellyfin-12-musl-test-host.md`).

## Build, test, and package commands

From the repository root:

```bash
. /config/arrtags-env.sh          # pinned SDK/runtime environment (build container)
./build.sh restore                # dotnet restore ArrTags.slnx --locked-mode
./build.sh build                  # dotnet build ArrTags.slnx -c Release --no-restore
./build.sh test                   # dotnet test  ArrTags.slnx -c Release --no-build
./build.sh package                # stage the plugin files, then write the deterministic archive
```

`./build.sh all` runs restore, build, test, and package in sequence.
`CONFIGURATION=Debug` selects a different build configuration; the release
default is `Release`.

Host-guarded suite:

```bash
ARRTAGS_JELLYFIN_HOST_DIR=/tmp/jf/jellyfin ./build.sh test
```

`./build.sh package` stages the plugin files through the MSBuild `PackagePlugin`
target into `artifacts/staging` (the plugin assembly, its `.deps.json`, the
plugin manifest, the third-party notices, and the `licenses/` files), then
invokes `scripts/pack-release.cs` to write
`artifacts/ArrTags_<version>.zip`, then removes the staging directory.

## Deterministic (byte-reproducible) packaging

MSBuild's `ZipDirectory` task enumerated the staging directory in filesystem
order and stamped every entry with the source file's modification time, so
repeated `./build.sh package` runs produced the same extracted contents with
different bytes and SHA-256. The release archive is now produced by
`scripts/pack-release.cs`, a .NET 10 file-based app that:

- writes entries in ordinal order of their forward-slash relative path;
- stamps every entry with one fixed ZIP timestamp (`2000-01-01 00:00:00`,
  interpreted as local wall-clock so it is timezone-independent);
- uses the pinned SDK's deterministic deflate implementation.

Three further inputs are normalized so a build from a git working tree, a
build from a `.git`-less export (for example `git archive`), and a build from a
different checkout directory all produce the same bytes:

- `src/ArrTags/ArrTags.csproj` sets `PathMap` to map the project directory to
  the fixed root `/_/ArrTags`, so the compiler does not embed the absolute
  source/PDB path (which otherwise changes the assembly and its SHA-256).
- `Directory.Build.props` sets
  `IncludeSourceRevisionInInformationalVersion=false`, so the SDK does not append
  the checkout's git commit id to `AssemblyInformationalVersion`.
- `Directory.Build.props` sets `SuppressImplicitGitSourceLink=true`, so the SDK
  does not generate a git-derived `*.sourcelink.json` (repository URL + commit)
  and feed it to the compiler.

Given identical source bytes, the staged files and the archive are identical.
The mechanism adds no plugin runtime dependency: the packer is a build-time
script that is not part of `ArrTags.slnx`.

## Clean-checkout release build (procedure and evidence)

To build from clean committed sources rather than stale `bin`/`obj`, export a
clean tree that excludes VCS metadata and build outputs, for example:

```bash
git archive HEAD | tar -x -C /tmp/release-src      # committed sources, no .git
# or, to export the current working-tree content of tracked files (including
# uncommitted edits to tracked files):
git ls-files | tar -T - -cf - | tar -x -C /tmp/release-src
```

The export must contain `scripts/pack-release.cs` (it is a tracked build script
once committed), the two `packages.lock.json` files, and the other sources.

Then run the four commands in that directory:

```bash
cd /tmp/release-src
. /config/arrtags-env.sh
./build.sh restore && ./build.sh build && ./build.sh test && ./build.sh package
sha256sum artifacts/ArrTags_0.1.0.0.zip
```

Task 7.5 evidence: two independent clean exports (each 526 files, no `.git`,
`bin`, `obj`, or `artifacts`) built from the same sources at different absolute
paths, plus a third run in the first export after wiping its build outputs, all
produced the identical archive:

| Run | Tree | SHA-256 | Size |
| --- | --- | --- | --- |
| 1 | clean export A | `bd10b9b6bf5d31049082d27625b18ba127eb6e2860a454fe2d3c35ccebaaee51` | 567,856 |
| 2 | clean export B | `bd10b9b6bf5d31049082d27625b18ba127eb6e2860a454fe2d3c35ccebaaee51` | 567,856 |
| 3 | clean export A (build outputs wiped) | `bd10b9b6bf5d31049082d27625b18ba127eb6e2860a454fe2d3c35ccebaaee51` | 567,856 |
| 4 | repository working tree (with `.git`) | `bd10b9b6bf5d31049082d27625b18ba127eb6e2860a454fe2d3c35ccebaaee51` | 567,856 |

The build reported 0 warnings / 0 errors in every tree. The default suite was
Failed 0, Passed 1218, Skipped 60, Total 1278; the host-guarded suite
(`ARRTAGS_JELLYFIN_HOST_DIR=/tmp/jf/jellyfin`) was Failed 0, Passed 1234,
Skipped 44, Total 1278.

## Release artifact identity

`artifacts/ArrTags_0.1.0.0.zip`

| Property | Value |
| --- | --- |
| Size | 567,856 bytes |
| SHA-256 | `bd10b9b6bf5d31049082d27625b18ba127eb6e2860a454fe2d3c35ccebaaee51` |
| Entries | 7 |

Entry list (ordinal order, all stamped `2000-01-01 00:00`):

| Entry | Size (bytes) | SHA-256 |
| --- | --- | --- |
| `ArrTags.deps.json` | 5,899 | `34e49970b08f33202aa2d397b219ca6963f61d372d1f46b9b1862a65e192c70c` |
| `ArrTags.dll` | 1,145,344 | `1e03b0659cd05e932ec879141443791ee43da726082ec1342529a14f23174584` |
| `THIRD-PARTY-NOTICES.md` | 1,368 | `3656c9f037624237e0530c8729dbe792791ded877b93c28807034ff55bf181b1` |
| `build.yaml` | 695 | `937794740b3f05db7b3059637369167a9d1387c8bf812b10615dfa8874058a0f` |
| `licenses/DejaVu-Fonts-License.txt` | 8,816 | `7a083b136e64d064794c3419751e5c7dd10d2f64c108fe5ba161eae5e5958a93` |
| `licenses/SkiaSharp-LICENSE.txt` | 1,129 | `89101e35a8c66fd4d6dffc1763259161d35cb564c169714ec227a768c89f2938` |
| `licenses/SkiaSharp-THIRD-PARTY-NOTICES.txt` | 139,775 | `21504c46c4c58aa64c1055bd2dcbc5f9a136b4b8c412ed3cc6740e22c5b127f5` |

The archive contains no `SkiaSharp.dll` or `libSkiaSharp.so` (ADR-015). The
`build.yaml` `artifacts` list (`ArrTags.dll`, `ArrTags.deps.json`) is a subset of
the archive, and `PluginPackagingTests` asserts the package contract, including
the duplicate-runtime regression.

## Verification steps

```bash
# 1. Reproducibility: package twice from clean state and compare.
./build.sh package && sha256sum artifacts/ArrTags_0.1.0.0.zip
rm -rf src/ArrTags/bin src/ArrTags/obj tests/ArrTags.Tests/bin tests/ArrTags.Tests/obj artifacts
./build.sh restore && ./build.sh build && ./build.sh package
sha256sum artifacts/ArrTags_0.1.0.0.zip     # must equal the first hash

# 2. Contents and entry identity.
unzip -l artifacts/ArrTags_0.1.0.0.zip
unzip -q artifacts/ArrTags_0.1.0.0.zip -d /tmp/arrtags-pkg && \
  find /tmp/arrtags-pkg -type f | sort | xargs sha256sum

# 3. Required entries present and non-empty.
#    ArrTags.dll, ArrTags.deps.json, build.yaml, THIRD-PARTY-NOTICES.md,
#    licenses/DejaVu-Fonts-License.txt, licenses/SkiaSharp-LICENSE.txt,
#    licenses/SkiaSharp-THIRD-PARTY-NOTICES.txt
```

## Release checklist

The task 7.5 clean-build run satisfied every item below; the checklist is
retained as the reusable release procedure (the boxes are intentionally
unchecked in this runbook).

- [ ] `./build.sh restore` succeeds in locked mode.
- [ ] `./build.sh build` reports 0 warnings / 0 errors.
- [ ] Default `./build.sh test` passes (Failed 0).
- [ ] Host-guarded suite passes with `ARRTAGS_JELLYFIN_HOST_DIR` set to the
      pinned Jellyfin `12.0.0` host.
- [ ] `./build.sh package` succeeds from a clean checkout.
- [ ] The archive SHA-256 is identical across repeated clean builds and matches
      the recorded identity above.
- [ ] `unzip -l` shows the 7 expected entries and no `SkiaSharp.dll` /
      `libSkiaSharp.so`.
- [ ] `build.yaml` `version`/`targetAbi`/`framework` match the declared pins.
- [ ] The package is installed on the pinned Jellyfin `12.0.0` host and loads
      with no error (task 7.2/7.7/7.8 procedure).

## Known environment notes

- The build/test container is Alpine/musl with a non-root user; it has `unzip`
  and `tar` but no `zip` binary, which is why the deterministic archive is
  written by the C# packer rather than a system `zip`.
- `DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1` is set for build/test; the live
  Jellyfin host must run with real ICU instead (see
  `docs/testing/jellyfin-12-musl-test-host.md`).
- The native SkiaSharp cases in the test suite are environment-guarded; the
  default `./build.sh test` run skips them, and the host-guarded run unskips the
  pinned-host facts.
- The artifact identity above is stable from the committed sources regardless of
  checkout path or `.git` presence; it is not a function of the commit id,
  because the SDK's git-derived inputs are suppressed for reproducibility.
