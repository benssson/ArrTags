# 5. Plugin lifecycle and registration

### Startup

1. Jellyfin discovers the `BasePlugin<PluginConfiguration>` implementation.
2. Jellyfin constructs the parameterless service registrator before building
   the service provider.
3. The registrator registers configuration, API clients, caches, matching and
   rendering services, the hosted worker, scheduled task, webhook controller,
   and artwork publication services. Jellyfin discovers the plugin's exported
   `ControllerBase` webhook controller through its standard plugin controller
   registration; ArrTags does not register a route outside that mechanism.
4. The hosted service subscribes to library events and starts the bounded queue
   consumer after the host is ready.
5. Startup must not perform unbounded Arr requests or a full-library render
   synchronously.

### Shutdown and uninstall

- Unsubscribe library events during hosted-service shutdown.
- Establish a durable disable/uninstall fence, stop accepting new publication
  work, and recover or terminally classify in-flight artwork operations before
  artifact cleanup.
- Cancel queued work and await workers within host shutdown limits, but never
  discard a non-terminal artwork operation or its source/derived artifacts.
- Do not leave unmanaged threads or fire-and-forget tasks running.
- Plugin-owned cache/state and source-artwork provenance may be removed on
  uninstall only after the lifecycle fence and all artwork operations are
  terminal. Active ArrTags artwork must be restored first when it is still
  identified as ArrTags-owned; unresolved or externally changed artwork keeps
  the recovery records and artifacts for later reconciliation.
