# SkiaSharp host compatibility (task 4.8 spike)

## Scope and method

This document is the evidence record for PLANS.md task 4.8: the SkiaSharp /
HarfBuzzSharp host-compatibility spike for the Jellyfin 12 plugin renderer
(ADR-010). It is an evidence document, not an architecture or phase definition.
It does not change ADR-009's visual specification or ADR-010's accepted
library/font decisions; it records what was verified about the pinned host and
runtime, what was only inferred, and what the Phase 5 packaging task must still
validate.

Evidence labels used here:

| Label | Meaning |
| --- | --- |
| **Confirmed** | Directly observed in the pinned artifacts, the pinned Jellyfin 12.0.0 source, or an executed command in this environment. |
| **Inferred** | A conclusion drawn from confirmed evidence, not directly observed on the live host. |
| **Phase 5 to validate** | A host-level question this spike could not close and that the packaging/publication task must confirm. |

The spike did **not** build the renderer service contract or drawing engine
(task 4.9). All probe sources and generated images used to obtain the evidence
lived under `/tmp/opencode` and were not added to the repository, except for the
committed round-trip test described in section 4.

## 1. Pinned Jellyfin 12.0.0 host artifacts (Confirmed)

The pinned host is the portable Jellyfin `v12.0` amd64 build extracted at
`/tmp/opencode/jf/jellyfin`. Its `jellyfin.deps.json` records the host's pinned
managed and native package versions. The relevant entries are:

| Package | Version | Managed file / native file | Notes |
| --- | --- | --- | --- |
| `SkiaSharp` | `3.119.4` | `lib/net10.0/SkiaSharp.dll` | assemblyVersion `3.119.0.0`, fileVersion `3.119.4.0` |
| `SkiaSharp.NativeAssets.Linux` | `3.119.4` | `runtimes/linux-x64/native/libSkiaSharp.so` | native asset for the Linux x64 host |
| `SkiaSharp.HarfBuzz` | `3.119.4` | `lib/net10.0/SkiaSharp.HarfBuzz.dll` | depends on `HarfBuzzSharp 8.3.1.5`, `SkiaSharp 3.119.4` |
| `HarfBuzzSharp` | `8.3.1.5` | `lib/net10.0/HarfBuzzSharp.dll` | assemblyVersion `1.0.0.0`, fileVersion `8.3.1.5` |
| `HarfBuzzSharp.NativeAssets.Linux` | `8.3.1.5` | `runtimes/linux-x64/native/libHarfBuzzSharp.so` | native asset for the Linux x64 host |
| `Jellyfin.Drawing.Skia` | `12.0.0` | `Jellyfin.Drawing.Skia.dll` | host component that depends on all of the above |

Evidence files and exact excerpts (from
`/tmp/opencode/jf/jellyfin/jellyfin.deps.json`):

```text
"SkiaSharp/3.119.4"                       -> lib/net10.0/SkiaSharp.dll            (3.119.0.0 / 3.119.4.0)
"SkiaSharp.NativeAssets.Linux/3.119.4"    -> runtimes/linux-x64/native/libSkiaSharp.so
"SkiaSharp.HarfBuzz/3.119.4"              -> depends: HarfBuzzSharp 8.3.1.5, SkiaSharp 3.119.4
                                             lib/net10.0/SkiaSharp.HarfBuzz.dll     (3.119.0.0 / 3.119.4.0)
"HarfBuzzSharp/8.3.1.5"                   -> lib/net10.0/HarfBuzzSharp.dll        (1.0.0.0 / 8.3.1.5)
"HarfBuzzSharp.NativeAssets.Linux/8.3.1.5"-> runtimes/linux-x64/native/libHarfBuzzSharp.so
```

`Jellyfin.Drawing.Skia/12.0.0` declares dependencies on `SkiaSharp`,
`SkiaSharp.HarfBuzz`, `SkiaSharp.NativeAssets.Linux`,
`HarfBuzzSharp.NativeAssets.Linux`, `BlurHashSharp.SkiaSharp`, and `Svg.Skia`.
The plugin's task 4.7 pins are therefore exactly the host's managed and Linux
native SkiaSharp version (`3.119.4`); no `HarfBuzzSharp` package is referenced by
the plugin.

## 2. Native library names, architecture, and hashes (Confirmed)

The host directory contains both native libraries. Their ELF header class/data
values are `02 01` (ELF64, little-endian) and their `e_machine` is `0x003e`
(`EM_X86_64`), so both are Linux x86-64 ELF shared objects.

| Native file | File name | ELF | Bytes | SHA-256 |
| --- | --- | --- | --- | --- |
| Skia | `libSkiaSharp.so` | ELF64 x86-64 | 11,170,296 | `66c856eaf1a47a00b23204c30c6ee407987bf5086ecc0a1a6b4fd67526b0cd02` |
| HarfBuzz | `libHarfBuzzSharp.so` | ELF64 x86-64 | 2,808,040 | `1d5c3afef13545bf34bf8f068b14e25ee619c3b6dee235c44e260fe61cb24018` |

Embedded SONAMEs: `libSkiaSharp.so.119.0.0` and
`libHarfBuzzSharp.so.0.60831.0`.

The host's `libSkiaSharp.so` is byte-for-byte identical to the local NuGet
`skiaSharp.nativeassets.linux/3.119.4` `runtimes/linux-x64/native/libSkiaSharp.so`
(same SHA-256). The host's `SkiaSharp.dll` is byte-for-byte identical to the
local NuGet `skiaSharp/3.119.4` `lib/net10.0/SkiaSharp.dll` (SHA-256
`aaaaa18c68ba1f3a3408b00dff28b11d5705198e17ba9d3aa59222bfd35407c8`). The host's
`SkiaSharp.HarfBuzz.dll` is SHA-256
`a3395e68c225d9fbe950312117e50ba475240a4b6b1deb8feb3c27dfcf6a767a`.

Native dependency facts (Confirmed): `libSkiaSharp.so` links
`libfontconfig.so.1` (and references `libGL.so.1`). The pinned sysroot at
`/tmp/opencode/jf/sysroot/usr/lib/x86_64-linux-gnu` provides
`libfontconfig.so.1 -> libfontconfig.so.1.12.1`, `libfreetype.so.6`, and
`libpng16.so.16`, but this directory is **not** on the default dynamic loader
path. Any process that loads `libSkiaSharp.so` in this environment must put that
directory on `LD_LIBRARY_PATH`. Fontconfig also needs `FONTCONFIG_PATH` /
`XDG_DATA_DIRS` to find its configuration, though an error there is non-fatal for
the `SKTypeface.FromData` path used by the renderer (see section 4).

## 3. Plugin managed/native resolution

### 3.1 Jellyfin 12.0.0 plugin loading (Confirmed, from pinned source)

Inspected from the pinned Jellyfin `v12.0` source tag
`6c073e19ddf604b2369c638716164fdab4c952dc` and the host's
`Emby.Server.Implementations.xml`:

- `Emby.Server.Implementations.Plugins.PluginLoadContext` derives from
  `AssemblyLoadContext` with `base(isCollectible: true)` and wraps
  `new AssemblyDependencyResolver(path)`. Its `Load(AssemblyName)` returns
  `LoadFromAssemblyPath(_resolver.ResolveAssemblyToPath(assemblyName))` when the
  resolver finds a path, otherwise `null`.
- `PluginManager.LoadAssemblies()` constructs
  `new PluginLoadContext(plugin.Path)` once per enabled plugin, where
  `LocalPlugin.Path` is the **plugin folder** (`LoadManifest(dir)` stores the
  directory). It then loads **every DLL in that folder** (or the manifest
  `assemblies` whitelist when non-empty) into the plugin context with
  `LoadFromAssemblyPath`, before it calls `assembly.GetTypes()`.
- `PluginLoadContext` overrides only managed assembly loading. It does not
  override native library loading (`LoadUnmanagedDll`) or handle
  `ResolvingUnmanagedDll`.

A `null` from `PluginLoadContext.Load` makes the runtime fall back to the default
load context, which resolves from the host's `jellyfin.deps.json`. This means the
resolution outcome depends on whether the plugin ships its own managed SkiaSharp
DLL in its folder.

### 3.2 Empirical resolver behavior (Confirmed)

Executed on the pinned runtime (`.NET 10.0.12`, `linux-x64`) with a throwaway
harness that replicates `PluginLoadContext` exactly:

```text
--- AssemblyDependencyResolver(plugin DIRECTORY) ---
ResolveAssemblyToPath(SkiaSharp)       = <null>
ResolveUnmanagedDllToPath(libSkiaSharp) = <null>

--- AssemblyDependencyResolver(plugin DLL path) ---
ResolveAssemblyToPath(SkiaSharp)       = /tmp/opencode/alctest/plugin/SkiaSharp.dll
ResolveUnmanagedDllToPath(libSkiaSharp) = /tmp/opencode/alctest/plugin/runtimes/linux-x64/native/libSkiaSharp.so
```

Because Jellyfin passes the plugin **folder**, its `AssemblyDependencyResolver`
resolves nothing from a deps.json inside the folder. This is why the first-party
load path for bundled dependencies is `LoadFromAssemblyPath` over the folder's
DLLs, not the resolver. The committed test
`SkiaHostCompatibilityTests.PluginDirectoryStyleResolutionDoesNotDiscoverBundledManagedSkiaSharp`
guards this measured behavior without needing the native runtime.

### 3.3 Empirical load-context scenarios (Confirmed)

A throwaway probe plugin (`ProbePlugin.dll`) that references SkiaSharp 3.119.4
was loaded through a byte-for-byte replica of Jellyfin's `PluginLoadContext` and
`LoadFromAssemblyPath` loop. SkiaSharp's managed location, execution load
context, and the mapped `libSkiaSharp.so` path (from `/proc/self/maps`) were
recorded.

| Scenario | Plugin folder contents | Managed SkiaSharp bound to | Native `libSkiaSharp.so` mapped |
| --- | --- | --- | --- |
| A: shared host | `ProbePlugin.dll` only; host SkiaSharp preloaded in the default context | host `/tmp/opencode/jf/jellyfin/SkiaSharp.dll` (default context) | host `/tmp/opencode/jf/jellyfin/libSkiaSharp.so` |
| B: bundled at root | `ProbePlugin.dll`, `SkiaSharp.dll`, `libSkiaSharp.so` | plugin-local `SkiaSharp.dll` (plugin context) | plugin-local `libSkiaSharp.so` |
| B-x64 / B-runtimes | same, native under `x64/` or `runtimes/linux-x64/native/` | plugin-local `SkiaSharp.dll` | **`DllNotFoundException`** (those subdirectories are not probed for a plugin-context assembly) |
| C: host + bundled | host managed **and** native SkiaSharp preloaded, then plugin loads its own managed+native at root | plugin-local `SkiaSharp.dll` | **both** libraries mapped at once; the plugin used its own copy |

Conclusions (Confirmed by the executed scenarios):

1. A plugin that ships **no** SkiaSharp DLL resolves the host's shared managed
   SkiaSharp through the default load context, and the host's native library is
   the one used.
2. A plugin that ships `SkiaSharp.dll` in its folder gets that copy loaded into
   its own load context by Jellyfin's folder DLL scan, even though the deps.json
   is not discovered by the resolver.
3. For that plugin-local managed copy, the runtime's default native probing
   finds `libSkiaSharp.so` **only when it sits in the same directory as
   `SkiaSharp.dll`** (the plugin folder root). `x64/` and
   `runtimes/linux-x64/native/` are not probed and fail to load.
4. Native isolation is real: when the host had already loaded its own
   `libSkiaSharp.so`, the plugin's root-level copy was loaded as a second,
   distinct mapping and was the one used by the plugin's rendering call.

### 3.4 Resulting Phase 5 packaging constraint

ADR-010 requires the plugin package to carry the native assets for every Linux
architecture it claims and forbids silently loading an arbitrary system Skia
library. The measured constraint for the Phase 5 packaging task is:

- To stay independent of the host's private copy, the plugin must ship
  `SkiaSharp.dll` **and** the matching `libSkiaSharp.so` in the **plugin folder
  root, next to the plugin assembly**. The `x64/` or `runtimes/<rid>/native/`
  layouts produced by the NuGet content/runtime-asset mechanism are not found
  when the DLL lives in a plugin load context.
- The plugin manifest's `assemblies` whitelist, when it is non-empty, must
  include the plugin's `SkiaSharp.dll` (or the whitelist must remain empty so the
  folder scan loads it). The current package manifest carries
  `"assemblies": []`.
- Shipping only `SkiaSharp.dll` without the root native asset produces a
  `DllNotFoundException` (not a graceful fallback) unless the host's native
  library happens to be reachable; this is exactly the failure ADR-010 requires
  the package to avoid.
- Loading a second SkiaSharp managed assembly and native library in-process is
  acceptable because the renderer treats SkiaSharp as a private implementation
  detail. Renderer code must not pass SkiaSharp types across the plugin
  load-context boundary (for example into host services or canonical models).
- The plugin's own `.deps.json` is not what makes managed resolution work; it is
  still worth shipping for completeness, but the effective mechanism is
  Jellyfin's folder DLL scan plus same-directory native probing.

### 3.5 What Phase 5 must still confirm on the live host

Resolved by task 5.4 (packaging), except where noted:

- **Resolved (task 5.4, live host):** the packaged plugin was installed on the
  pinned Jellyfin 12.0.0 host with the root-level managed and native SkiaSharp
  assets. The host log records
  `Loaded assembly "SkiaSharp, Version=3.119.0.0, ..." from ".../ArrTags_0.1.0.0/SkiaSharp.dll"`
  and `Loaded plugin: "ArrTags" "0.1.0.0"`, and the host's
  `/proc/<pid>/maps` maps both `ArrTags.dll` and `SkiaSharp.dll` from the plugin
  folder. A full artwork render through Jellyfin is not wired until tasks
  5.5/5.11, so the host did not invoke the renderer.
- **Resolved (task 5.4, replicated load context):** a byte-for-byte replica of
  Jellyfin's `PluginLoadContext` was run against the exact extracted package with
  the host's managed and native SkiaSharp preloaded (scenario C). The plugin ALC
  bound `SkiaSharp` to the plugin-folder copy, a native encode call succeeded,
  and `/proc/self/maps` showed the plugin-folder `libSkiaSharp.so` mapped as a
  distinct second copy alongside the host's. The plugin-local native is the
  one used by the plugin-context call.
- **Resolved (task 5.4):** V1 claims only `linux-x64`. The package carries the
  single `runtimes/linux-x64/native/libSkiaSharp.so` asset at the plugin folder
  root, asserted to be an ELF64 x86-64 shared object. Other Linux RIDs are not
  claimed in V1 and no arbitrary system Skia library is loaded.
- **Inferred:** the host loads its own SkiaSharp from
  `Jellyfin.Drawing.Skia` before or independently of ArrTags; the spike modeled
  this by preloading the host's managed and native libraries in scenario C. The
  live-host and replicated-load-context runs above confirm that both copies
  coexist.

## 4. Decode/draw/encode round-trip proof (Confirmed)

The proof is the committed, environment-guarded test
`tests/ArrTags.Tests/SkiaHostCompatibilityTests.cs`:
`PinnedSkiaRuntimeDecodesDrawsAndEncodesARoundTripPng`. It:

1. Asserts the loaded `SkiaSharp` is assembly version `3.119.0.0` / file version
   `3.119.4.0` (the host pin).
2. Decodes a repository-owned 16x16 synthetic PNG fixture from base64 bytes.
3. Draws the decoded image scaled onto a 240x360 surface, draws an ADR-009-style
   rounded pill, and draws `1080p Blu-ray` with an `SKTypeface` created from the
   **exact embedded bundled DejaVu Sans Bold bytes** (`RenderFontIdentity`).
4. Encodes a PNG and asserts the PNG signature, IHDR width/height, 8-bit depth,
   RGB/RGBA color type, and zero compression/filter/interlace bytes
   (non-interlaced).
5. Decodes the encoded result back and asserts it returns the expected
   dimensions.

The test is guarded because the default suite environment does not put the
transitive `libfontconfig.so.1` on the loader path. The guard is the committed
`SkiaNativeFactAttribute`: the fact is reported as **skipped** unless
`ARRTAGS_SKIA_COMPAT=1` is set, so `./build.sh test` stays green where the native
runtime is unavailable.

Exact commands and environment (from this environment):

```bash
export DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1
export PATH="/config/.dotnet:$PATH"
cd /config/workspace/ArrTags

# Default suite: the native proof is skipped, everything else runs.
./build.sh test
# -> Passed: 439, Skipped: 1, Total: 440

# Forced native proof on the pinned runtime and library.
LD_LIBRARY_PATH=/tmp/opencode/jf/sysroot/usr/lib/x86_64-linux-gnu \
FONTCONFIG_PATH=/tmp/opencode/jf/sysroot/etc/fonts \
XDG_DATA_DIRS=/tmp/opencode/jf/sysroot/usr/share \
ARRTAGS_SKIA_COMPAT=1 \
dotnet test tests/ArrTags.Tests/ArrTags.Tests.csproj \
  --configuration Release --no-build \
  --filter "FullyQualifiedName~SkiaHostCompatibilityTests"
# -> Passed: 2, Failed: 0, Skipped: 0
```

Observed test output:

```text
Passed!  - Failed: 0, Passed: 2, Skipped: 0, Total: 2 - ArrTags.Tests.dll (net10.0)
```

The same round trip was independently executed on the pinned host
`libSkiaSharp.so` (`/tmp/opencode/jf/jellyfin/libSkiaSharp.so`) and the sysroot,
producing `source=320x480 output=320x480 pngBytes=4178` and mapping the expected
native file. A `Fontconfig error: Cannot load default config file` message is
non-fatal and does not prevent the bundled-font render, because the typeface is
loaded from plugin bytes rather than from a host font family.

## 5. HarfBuzzSharp decision (Confirmed rationale)

V1 does not need `HarfBuzzSharp` or `SkiaSharp.HarfBuzz`. The 4.7 choice to omit
them stands.

- ADR-009's text is single-line, normalized, bounded to 24 Unicode scalar
  values, with no font fallback; the vocabulary is technical/status labels
  (resolution, source, codec, audio tokens, and custom values) in a bundled
  sans-serif. This does not require OpenType shaping: the executed round trip
  drew and encoded text using plain `SkiaSharp` `SKTypeface`/`SKFont` without any
  HarfBuzz assembly.
- The host still ships `SkiaSharp.HarfBuzz 3.119.4` / `HarfBuzzSharp 8.3.1.5`
  because `Jellyfin.Drawing.Skia` depends on them; that is a host-internal
  dependency, not a renderer requirement.
- If a future visual decision requires complex-script shaping or ligatures, that
  is a new renderer decision; adding `HarfBuzzSharp` would require its own
  version pin, native asset (`libHarfBuzzSharp.so`), notice, and fingerprint
  participation. It is out of V1.

Consequently, the plugin keeps only `SkiaSharp` and
`SkiaSharp.NativeAssets.Linux` at `3.119.4`, and the Phase 5 package must carry
`SkiaSharp.dll` + `libSkiaSharp.so`.

## 6. Limitations and not-yet-verified items

- No live Jellyfin host run was performed for this spike; the host artifacts and
  the pinned Jellyfin source were inspected, and the plugin load context was
  replicated on the exact pinned .NET runtime. The host-level integration is
  **Phase 5 to validate** (section 3.5).
- Only the `linux-x64` RID and the Ubuntu 24.04 sysroot were exercised.
- The round-trip proof is not part of the default `./build.sh test` execution;
  it runs only with `ARRTAGS_SKIA_COMPAT=1` and the sysroot on the loader path.
- The plugin is now packaged with the managed and root-level native SkiaSharp
  assets (task 5.4). The live-host run and the replicated load-context run in
  section 3.5 confirm the layout; a full image render through Jellyfin remains
  task 5.5/5.11.

## 7. References

- `PLANS.md`, Milestone 4, task 4.8
- `docs/decisions.md`, ADR-009 and ADR-010
- `docs/architecture.md`, section 9
- `docs/implementation-readiness.md`, compatibility target and ADR-010 section
- `docs/research/poster-rendering-strategies.md`
- Pinned Jellyfin `v12.0` commit `6c073e19ddf604b2369c638716164fdab4c952dc`:
  `Emby.Server.Implementations/Plugins/PluginLoadContext.cs`,
  `Emby.Server.Implementations/Plugins/PluginManager.cs`,
  `MediaBrowser.Common/Plugins/LocalPlugin.cs`
- `/tmp/opencode/jf/jellyfin/jellyfin.deps.json` and the host's SkiaSharp /
  HarfBuzzSharp managed and native files
- .NET runtime `v10.0.0`
  `System.Private.CoreLib/System/Runtime/Loader/AssemblyDependencyResolver.cs`
  and `src/native/corehost/hostpolicy/*`
- SkiaSharp `v3.119.4` `binding/Binding.Shared/LibraryLoader.cs` and
  `SkiaSharp.NativeAssets.Linux` `buildTransitive` targets

## Appendix A: load-context probe harness (not committed)

The load-context scenarios in section 3.3 used a throwaway harness under
`/tmp/opencode/alcprobe2` plus a throwaway probe plugin under
`/tmp/opencode/probeplugin`. They are reproduced here because they were not
committed. The probe is a `net10.0` class library referencing
`SkiaSharp 3.119.4` and `SkiaSharp.NativeAssets.Linux 3.119.4` whose
`Probe.RoundTrip(byte[])` runs the decode/draw/encode sequence of section 4 and
whose `Probe.DescribeManaged()` returns `typeof(SKBitmap).Assembly.Location` and
its load context. The harness is a `net10.0` console app that contains the
following byte-for-byte replica of Jellyfin's load context:

```csharp
sealed class PluginLoadContext : AssemblyLoadContext
{
    private readonly AssemblyDependencyResolver _resolver;

    public PluginLoadContext(string path) : base(true)
    {
        _resolver = new AssemblyDependencyResolver(path);
    }

    protected override Assembly? Load(AssemblyName assemblyName)
    {
        var assemblyPath = _resolver.ResolveAssemblyToPath(assemblyName);
        return assemblyPath is not null ? LoadFromAssemblyPath(assemblyPath) : null;
    }
}
```

For each scenario the harness optionally preloads the host's managed SkiaSharp
(`AssemblyLoadContext.Default.LoadFromAssemblyPath(hostSkiaSharpDll)`) and/or the
host's native library (`NativeLibrary.Load(hostNativeSo)`), then:

```csharp
var ctx = new PluginLoadContext(pluginFolder);
foreach (var dll in Directory.GetFiles(pluginFolder, "*.dll", SearchOption.AllDirectories))
    ctx.LoadFromAssemblyPath(dll);

var probeAssembly = ctx.Assemblies.Single(a => a.GetName().Name == "ProbePlugin");
// invoke Probe.DescribeManaged() / Probe.RoundTrip(fontBytes), then read
// /proc/self/maps for the mapped libSkiaSharp.so path.
```

Reproduction environment: `DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1`,
`PATH=/config/.dotnet:$PATH`, and, for any run that loads the native library,
`LD_LIBRARY_PATH=/tmp/opencode/jf/sysroot/usr/lib/x86_64-linux-gnu`. The
`RoundTrip` `fontBytes` argument is the exact embedded font resource, for example
`File.ReadAllBytes("/config/workspace/ArrTags/src/ArrTags/Resources/DejaVuSans-Bold.ttf")`.
