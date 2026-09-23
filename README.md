# ArrTags

ArrTags is a Jellyfin plugin that reads metadata from independently configured
[Sonarr](https://sonarr.tv/) and [Radarr](https://radarr.video/) instances and
publishes configurable badges — for example the actual file quality — onto
Jellyfin Movie and Episode posters. Badges are written through Jellyfin's
supported item-image APIs, so the original poster is preserved and can be
restored.

## Requirements

- **Jellyfin `12.0.0` only.** The plugin targets `net10.0` with
  `targetAbi: 12.0.0.0`; no Jellyfin version before 12 is supported.
- **Sonarr `3.x`–`4.x` and/or Radarr `3.x`–`6.x`**, reached over the `/api/v3`
  API. Each provider is configured independently, so you can run only Sonarr,
  only Radarr, or both.
- **A host-supplied SkiaSharp.** The plugin compiles against the pinned
  SkiaSharp version but bundles no renderer runtime and shares the host's copy.
  Jellyfin 12 supplies a compatible SkiaSharp. If the host does not,
  badge rendering fails closed and the current artwork is left unchanged rather
  than falling back to a bundled copy (`docs/limitations.md` F5).
- ArrTags is verified on the pinned `linux-musl-x64` Jellyfin `12.0.0` host and
  makes no RID-specific claim; it does not distinguish musl from glibc.

## Install from the Jellyfin plugin repository

1. In Jellyfin, open **Dashboard → Plugins → Repositories**.
2. Add the repository URL:
   `https://raw.githubusercontent.com/benssson/ArrTags/main/manifest.json`.
3. Open the **Catalog**, find **ArrTags**, and select **Install**.
4. Restart Jellyfin.

## Manual install (fallback)

1. Download the release package `ArrTags_1.0.1.0.zip` from the GitHub Releases
   page for `v1.0.1` at <https://github.com/benssson/ArrTags/releases>.
2. Extract it so the plugin files sit in a versioned folder under Jellyfin's
   plugins directory: `<plugins>/ArrTags_1.0.1.0/`. The plugins directory is the
   `plugins` folder under Jellyfin's data directory.
3. Restart Jellyfin.

## Configuration

A dashboard settings page is available at **Dashboard → Plugins → ArrTags**. It
loads the current configuration and saves it through Jellyfin's administrator-gated
configuration API, so the plugin no longer has to be configured by editing XML by
hand. The page covers the provider connections and their API keys, the webhook
secret, the Movie/Episode poster flags, the enabled-library scope, the renderer
selectors/templates and palette overrides, and the operational limits.

The page itself embeds no secret and only shows a secret value that Jellyfin's
existing administrator configuration API already returns. Runtime activation is
not yet wired, so a saved change is **not observed until the process restarts**
(`docs/limitations.md` F2); the XML below remains the persisted shape and can
still be edited directly at `plugins/configurations/ArrTags.xml` (that is,
`<data>/plugins/configurations/ArrTags.xml`).

### Fields

Provider connections (`Sonarr` and `Radarr`, each configured independently):

| Field | Meaning |
| --- | --- |
| `Enabled` | Whether this connection is used. Disabled by default, so a fresh install performs no provider I/O. |
| `BaseUrl` | The absolute base URL of the instance **without** an API path, for example `http://sonarr:8989`. |
| `ApiKey` | The provider API key, sent to the instance as the `X-Api-Key` header. |
| `RequestTimeoutSeconds` | Finite per-request timeout in seconds (default `15`). |
| `AllowInsecureTls` | Relax certificate validation for this connection only. |

Other fields:

| Field | Meaning |
| --- | --- |
| `WebhookSecret` | The shared secret required by the inbound webhook endpoints. |
| `BadgeMoviePosters` | Whether Movie posters are eligible for badges (default `true`). |
| `BadgeEpisodePosters` | Whether Episode posters are eligible for badges (default `true`). |
| `EnabledLibraries` | The Jellyfin library identifiers eligible for badges. An empty set means no library restriction. |
| `Renderer.Selectors` | The configured badge selectors (see below). |
| `Renderer` palette overrides | Optional `TechnicalBackground`, `TechnicalText`, `StatusBackground`, and `StatusText` colors (see below). |
| `Limits` | The operational bounds (queue capacity, concurrency, timeouts, payload and artifact sizes, cache and retention windows). Defaults are validated when the configuration loads. |

`Renderer.Selectors` holds one entry per badge selector:

| Field | Meaning |
| --- | --- |
| `Selector` | The provider-neutral selector name: `Quality`, `Resolution`, `DynamicRange`, `Source`, `VideoCodec`, `Audio`, `CustomBadge`, or `UpgradePending`. |
| `Enabled` | Whether the selector participates in rendering. A disabled selector produces no badge even when its value is known. |
| `Template` | A bounded display template containing at most one `{value}` placeholder; a template without a placeholder renders its literal text. |

The optional `Renderer` palette overrides are `RRGGBB` hexadecimal colors. An
empty value keeps the default color, and each text/background pair must reach at
least 4.5:1 contrast.

### Minimal `ArrTags.xml` example

This is the Jellyfin plugin-configuration XML shape (`BasePluginConfiguration`);
omit any element to keep its default.

```xml
<?xml version="1.0" encoding="utf-8"?>
<PluginConfiguration xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance" xmlns:xsd="http://www.w3.org/2001/XMLSchema">
  <Sonarr>
    <Enabled>true</Enabled>
    <BaseUrl>http://sonarr:8989</BaseUrl>
    <ApiKey>YOUR_SONARR_API_KEY</ApiKey>
  </Sonarr>
  <Radarr>
    <Enabled>true</Enabled>
    <BaseUrl>http://radarr:7878</BaseUrl>
    <ApiKey>YOUR_RADARR_API_KEY</ApiKey>
  </Radarr>
  <WebhookSecret>YOUR_SHARED_WEBHOOK_SECRET</WebhookSecret>
  <BadgeMoviePosters>true</BadgeMoviePosters>
  <BadgeEpisodePosters>true</BadgeEpisodePosters>
</PluginConfiguration>
```

### Inbound webhook endpoints

Sonarr and Radarr can push update events to two anonymous endpoints:

- `POST /ArrTags/Webhook/Sonarr`
- `POST /ArrTags/Webhook/Radarr`

Both authenticate the caller with the shared secret in the
`X-ArrTags-Webhook-Secret` header; configure the same value as `WebhookSecret`
in `ArrTags.xml`. A request without the correct secret is rejected with `401`,
an accepted delivery is acknowledged with `202`, and a malformed or oversized
payload is rejected with a bounded error status. A delivery never reflects the
secret or its body, and a missed delivery is repaired by the plugin's periodic
reconciliation.

## Updating

If ArrTags was installed from the plugin repository, Jellyfin shows an available
update for it. Update from the catalog and restart Jellyfin. If you installed it
manually, download the newer release package and replace the versioned plugin
folder, then restart.

## Uninstall

Uninstall ArrTags from **Dashboard → Plugins**. On a completed uninstall the
plugin restores the original poster for every item it changed and removes its
state root, `ProgramDataPath/ArrTags` (outside the plugins directory). If the
uninstall drain cannot complete — for example because an item's image was
changed externally — the current artwork and its recovery records are retained
instead (`docs/limitations.md`).

## Known limitations

The canonical record of what ArrTags does not yet do or has not yet verified is
`docs/limitations.md`. In short:

- **No provider inventory/catalogue cache.** Every reconciliation work item
  re-reads the provider library, so the "avoid unnecessary provider requests"
  goal is only partially met for provider fetches; rendering and publication are
  fingerprint-gated (F1).
- **Configuration changes require a restart.** Runtime configuration
  replacement is not wired to Jellyfin's save path (F2).
- **Jellyfin Enhanced coexistence is verified at the contract level only**;
  Enhanced is not installed on the pinned host (V3).
- **Live-verification gaps.** There is no live Sonarr/Radarr instance, the live
  image read-back is manual, and the live uninstall drain is not covered by an
  automated live test (V1, V4, V5).
- **The host must supply a compatible SkiaSharp**; there is no bundled fallback
  (F5).

## More information

- `docs/project-status.md` — project status and contributor information.
- `docs/release/build-and-release.md` — build and release process.
