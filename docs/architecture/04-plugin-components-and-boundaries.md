# 4. Plugin components and boundaries

| Component | Responsibility | Boundary |
| --- | --- | --- |
| `Plugin` | Identity, configuration, data-folder ownership, uninstall hook | Thin `BasePlugin<PluginConfiguration>` entry point; state root relocated to `ProgramDataPath/ArrTags` outside `PluginsPath` (ADR-014) |
| Service registrator | Register services, hosted services, controllers, artwork publication, and tasks | Parameterless `IPluginServiceRegistrator` |
| Configuration service | Validate and publish immutable configuration snapshots | Uses plugin configuration persistence; never exposes secrets |
| Credential boundary | Publish the private versioned secret snapshot and issue bounded credential leases | Singleton `IPluginSecretResolver`; never serializes or persists secret values |
| Sonarr client | Read Sonarr v3 resources and probe connections | `IHttpClientFactory`; no write endpoints |
| Radarr client | Read Radarr v3 resources and probe connections | `IHttpClientFactory`; no write endpoints |
| Matching service | Match a Jellyfin item to one configured Arr record | Provider IDs first; ambiguity is not auto-accepted |
| Metadata cache | Store current Arr snapshot and badge-relevant fingerprint | Versioned plugin-owned state under `DataFolderPath` |
| Reconciliation coordinator | Fetch, match, fingerprint, and enqueue affected items | Runs outside event handlers and image requests where possible |
| Work queue | Coalesce item work and bound memory/concurrency | Hosted service with cancellation-aware workers |
| Artwork publisher | Publish validated derived artwork through Jellyfin's public image APIs | Does not write media files or Jellyfin's image-cache directory directly |
| Badge renderer | Draw configured labels onto retained source artwork | Plugin-owned, provider-neutral SkiaSharp service with bundled DejaVu Sans Bold 2.37; bounded input/output and render concurrency |
| Render work cache | Avoid repeated generation before publication where useful | Optional, bounded, fingerprint-keyed work state; not the client response path |
| Webhook controller | Accept authenticated low-latency Arr hints | Anonymous plugin route; validates the shared secret with a constant-time lease comparison and a bounded payload, then submits to a bounded intake; never trusts payload as source of truth (ADR-012) |
| Webhook intake | Resolve accepted hints and feed the bounded work queue off the request path | Bounded, coalescing, non-blocking; resolves only already-known provider-record associations and never publishes, mutates artwork, or calls Arr |
| Scheduled task | Manual and periodic full reconciliation | Cancellable, progress-reporting, retry-safe |
| Post-scan task | Reconciliation after a Jellyfin media-library scan | Jellyfin `ILibraryPostScanTask`; bounded, cancellable, progress-reporting |

No component accesses Jellyfin database tables, image-cache directories,
`ImageSaver`, or concrete Jellyfin implementation types when a plugin-facing
API is available.
