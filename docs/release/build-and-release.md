# ArrTags build and release

This document is the canonical record for the ArrTags release build (Phase 7
task 7.5; extended by Phase 8 task 8.5 for the `1.0.1.0` release). It records the
pinned toolchain and inputs, the supported version ranges, the exact
build/test/package commands, the reproducible clean-checkout procedure, the
release artifact identity, the Jellyfin plugin-repository `manifest.json` and the
`scripts/publish-release.sh` release procedure, and the release checklist. It
owns Phase 7 acceptance criterion 5 ("The release artifact and build process are
reproducible and documented").

Architecture and accepted decisions stay authoritative in
`docs/architecture.md` and `docs/decisions.md`; the live pinned test host is
documented in `docs/testing/jellyfin-12-musl-test-host.md`.

## Pinned toolchain and inputs

| Item | Value | Source |
| --- | --- | --- |
| Plugin version | `1.0.1.0` | `build.yaml`, `Directory.Build.props` |
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
sha256sum artifacts/ArrTags_1.0.1.0.zip
```

**0.1.0 history.** Task 7.5 evidence: two independent clean exports (each 526
files, no `.git`, `bin`, `obj`, or `artifacts`) built from the same sources at
different absolute paths, plus a third run in the first export after wiping its
build outputs, all produced the identical archive. This records the `0.1.0.0`
artifact before the release security fix SEC-1, so it is historical only. SEC-1
later changed `ArrTags.dll` (the post-fix `0.1.0.0` identity was `f6b6a515…`),
and Phase 8 task 8.3 then bumped the plugin version to `1.0.1.0`; neither
`bd10b9b6…` nor `f6b6a515…` is the current identity. The current identity is in
"Release artifact identity" below; see `docs/changelog.md`.

| Run (0.1.0, pre-SEC-1) | Tree | SHA-256 | Size |
| --- | --- | --- | --- |
| 1 | clean export A | `bd10b9b6bf5d31049082d27625b18ba127eb6e2860a454fe2d3c35ccebaaee51` | 567,856 |
| 2 | clean export B | `bd10b9b6bf5d31049082d27625b18ba127eb6e2860a454fe2d3c35ccebaaee51` | 567,856 |
| 3 | clean export A (build outputs wiped) | `bd10b9b6bf5d31049082d27625b18ba127eb6e2860a454fe2d3c35ccebaaee51` | 567,856 |
| 4 | repository working tree (with `.git`) | `bd10b9b6bf5d31049082d27625b18ba127eb6e2860a454fe2d3c35ccebaaee51` | 567,856 |

For those `0.1.0` task 7.5 builds, the build reported 0 warnings / 0 errors in
every tree. The default suite was Failed 0, Passed 1218, Skipped 60, Total 1278;
the host-guarded suite (`ARRTAGS_JELLYFIN_HOST_DIR=/tmp/jf/jellyfin`) was
Failed 0, Passed 1234, Skipped 44, Total 1278. These are historical `0.1.0`
counts; the current `1.0.1.0` counts (default 1228/60/1288 and host-guarded
1244/44/1288) are recorded in `docs/changelog.md`.

## Release artifact identity

`artifacts/ArrTags_1.0.1.0.zip` (the current `1.0.1.0` release artifact)

| Property | Value |
| --- | --- |
| Size | 568,248 bytes |
| SHA-256 | `de4c34841d77b5ff74b6bc9edeb515a4c5fcc5a9b09d7d24a2da5d64d31b4b8c` |
| MD5 | `16baa5a7324b8e14fdb113d84b944d09` |
| Entries | 7 |

This is the identity after Phase 8 task 8.3 bumped the plugin version to
`1.0.1.0`; the release security fix SEC-1 and the `0.1.0.0` identity it produced
are recorded in `docs/changelog.md` and in the `0.1.0` history above. The values
above were produced by running `. /config/arrtags-env.sh && ./build.sh package`
in the repository working tree; the archive is stable across repeated
`./build.sh package` runs. The MD5 is the Jellyfin plugin-repository manifest
checksum (see "Jellyfin plugin repository" below).

Entry list (ordinal order, all stamped `2000-01-01 00:00`):

| Entry | Size (bytes) | SHA-256 |
| --- | --- | --- |
| `ArrTags.deps.json` | 5,899 | `62f63fcc72485fcd2f40564443ee846084f8722f37d99c704e1897e740814d35` |
| `ArrTags.dll` | 1,146,368 | `c384eccf7618c9ad111dfb2d9a9ddf719cb28e993d854c0891485f3bf7dc6e6b` |
| `THIRD-PARTY-NOTICES.md` | 1,368 | `3656c9f037624237e0530c8729dbe792791ded877b93c28807034ff55bf181b1` |
| `build.yaml` | 892 | `7a33566158620df65267790d1be5d556f0058d522734b8990845a18eca38b769` |
| `licenses/DejaVu-Fonts-License.txt` | 8,816 | `7a083b136e64d064794c3419751e5c7dd10d2f64c108fe5ba161eae5e5958a93` |
| `licenses/SkiaSharp-LICENSE.txt` | 1,129 | `89101e35a8c66fd4d6dffc1763259161d35cb564c169714ec227a768c89f2938` |
| `licenses/SkiaSharp-THIRD-PARTY-NOTICES.txt` | 139,775 | `21504c46c4c58aa64c1055bd2dcbc5f9a136b4b8c412ed3cc6740e22c5b127f5` |

The per-entry SHA-256 values were produced by extracting the archive
(`unzip -q artifacts/ArrTags_1.0.1.0.zip -d /tmp/arrtags-pkg`) and running
`sha256sum` on each extracted entry.

The archive contains no `SkiaSharp.dll` or `libSkiaSharp.so` (ADR-015). The
`build.yaml` `artifacts` list (`ArrTags.dll`, `ArrTags.deps.json`) is a subset of
the archive, and `PluginPackagingTests` asserts the package contract, including
the duplicate-runtime regression.

## Jellyfin plugin repository

ArrTags is prepared to be installable through the standard Jellyfin plugin
catalog from the public repository `benssson/ArrTags` once the user pushes the
committed manifest and publishes the GitHub release. The manifest commit
(`8cba85b`) and the annotated tag `v1.0.1` currently exist only in the local
repository (no push), so catalog installation does not work yet. Jellyfin is
pointed at the raw manifest URL:

```text
https://raw.githubusercontent.com/benssson/ArrTags/main/manifest.json
```

`manifest.json` is committed at the repository root and is a Jellyfin
plugin-repository document: a JSON array of plugin objects, each with a
`versions` array. The ArrTags entry has these fields (values match `build.yaml`
and the current `1.0.1.0` artifact):

| Field | Value |
| --- | --- |
| `category` | `General` |
| `guid` | `40322d52-5680-449f-b33e-e01836ee2f46` |
| `name` | `ArrTags` |
| `description` | `build.yaml` `description` (the shipped v1 behavior text) |
| `owner` | `arrtags` |
| `overview` | `Adds Sonarr and Radarr metadata badges to Jellyfin posters.` |
| `versions[0].version` | `1.0.1.0` |
| `versions[0].targetAbi` | `12.0.0.0` |
| `versions[0].checksum` | `16baa5a7324b8e14fdb113d84b944d09` (MD5 of `artifacts/ArrTags_1.0.1.0.zip`) |
| `versions[0].sourceUrl` | `https://github.com/benssson/ArrTags/releases/download/v1.0.1/ArrTags_1.0.1.0.zip` |
| `versions[0].timestamp` | `2026-09-22T14:40:57Z` (generated UTC; regenerated on each manifest write) |
| `versions[0].changelog` | `build.yaml` `changelog` (the release notes) |

The `checksum` must equal `md5sum artifacts/ArrTags_1.0.1.0.zip`; the
`sourceUrl` is the GitHub release asset URL for the tag derived from the version
(`v1.0.1`). Versions are written newest-first.

`scripts/write-manifest.cs` is the .NET 10 file-based app that generates the
manifest. It reads the plugin metadata from `build.yaml`, upserts the entry for
the current version (replacing an existing same-version entry), preserves any
other plugin and version entries, and writes stable indented JSON (2-space
indent, UTF-8 without BOM, one trailing newline), so identical inputs produce
byte-identical output. It also supports `--print-changelog` to print the
`build.yaml` `changelog` value for the GitHub release notes. It is not part of
the solution and adds no plugin dependency.

`scripts/publish-release.sh` is the release orchestrator over
`./build.sh restore/build/test/package`, SHA-256/MD5 computation, manifest
generation, git commit/tag/push, and a `gh`/GitHub-REST-API release with
post-upload MD5 verification. It has three modes:

- `--dry-run` builds, tests, and packages, computes the checksums, writes
  `manifest.json`, and prints the intended actions, but performs no commit, tag,
  push, or GitHub call.
- `--prepare-only` generates and commits `manifest.json`, creates the annotated
  tag `v1.0.1`, and pushes the branch and tag (unless `--no-push`), with no
  GitHub call.
- `--release-only` pushes the existing branch and tag (unless `--no-push`),
  then creates/refreshes the GitHub release and uploads the asset with a
  post-upload MD5 verification. It performs no build, manifest, commit, or tag;
  the local tag must already exist (created by `--prepare-only`), and the script
  fails if it does not.

The default mode (no mode flag) prepares the manifest/tag and publishes the
GitHub release in one run. Other flags are `--skip-build`, `--skip-tests`,
`--no-push`, `--force` (move an existing tag to HEAD), `--tag <tag>` (override
the derived tag), and `--repo <owner/name>` (default `benssson/ArrTags`). The
GitHub release and asset upload remain the user's manual step; task 8.6 only
commits `manifest.json` and creates the annotated tag.

## Verification steps

```bash
# 1. Reproducibility: package twice from clean state and compare.
./build.sh package && sha256sum artifacts/ArrTags_1.0.1.0.zip
rm -rf src/ArrTags/bin src/ArrTags/obj tests/ArrTags.Tests/bin tests/ArrTags.Tests/obj artifacts
./build.sh restore && ./build.sh build && ./build.sh package
sha256sum artifacts/ArrTags_1.0.1.0.zip     # must equal the first hash

# 2. Contents and entry identity.
unzip -l artifacts/ArrTags_1.0.1.0.zip
unzip -q artifacts/ArrTags_1.0.1.0.zip -d /tmp/arrtags-pkg && \
  find /tmp/arrtags-pkg -type f | sort | xargs sha256sum

# 3. Required entries present and non-empty.
#    ArrTags.dll, ArrTags.deps.json, build.yaml, THIRD-PARTY-NOTICES.md,
#    licenses/DejaVu-Fonts-License.txt, licenses/SkiaSharp-LICENSE.txt,
#    licenses/SkiaSharp-THIRD-PARTY-NOTICES.txt
```

## Release checklist

The checklist is the reusable release procedure (the boxes are intentionally
unchecked in this runbook). The 0.1.0 run satisfied every item; Phase 8 task 8.5
re-verified the `1.0.1.0` package identity and contents, and task 8.6 performs
the final release-readiness verification.

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
- Byte-reproducibility depends on the pinned toolchain: it is demonstrated with
  the pinned SDK (`global.json` pins `10.0.0` with `latestMinor`, validated with
  `10.0.401`), and a different .NET SDK could in principle change compiler or
  deflate output. No automated test guards byte-identity; `PluginPackagingTests`
  asserts the package contents (required entries, no duplicate SkiaSharp
  runtime) but not the archive bytes, so a regression in `scripts/pack-release.cs`
  or the build properties would not fail the suite.
- The debug-metadata suppression described under "Deterministic
  (byte-reproducible) packaging" is a deliberate trade-off: the shipped
  `ArrTags.dll` has `AssemblyInformationalVersion` `1.0.1.0` with no commit
  suffix and its PDB carries no SourceLink mapping, so source-level debugging of
  a released assembly is harder.
- `dotnet build src/ArrTags/ArrTags.csproj -p:PackagePlugin=true` alone stages
  the release files into `artifacts/staging` and does not write the archive;
  `./build.sh package` is the entry point that writes
  `artifacts/ArrTags_<version>.zip`.
- The package retains `licenses/SkiaSharp-LICENSE.txt` and
  `licenses/SkiaSharp-THIRD-PARTY-NOTICES.txt` even though it ships no SkiaSharp
  runtime (deliberate, ADR-015).

The complete consolidated limitations and deferred-decision record, including
which `GOALS.md` success criteria and Phase 7 acceptance criteria are met, is in
`docs/limitations.md`.
