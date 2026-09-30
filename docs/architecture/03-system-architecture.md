# 3. System architecture

```mermaid
flowchart TD
    JF[Jellyfin host] --> PL[ArrTags plugin]
    PL --> CFG[Configuration]
    PL --> API[Sonarr/Radarr API clients]
    PL --> STATE[Plugin state and metadata cache]

    EVT[Library events] --> Q[Bounded deduplicating queue]
    TASK[Scheduled/post-scan reconciliation] --> Q
    WH[Authenticated Arr webhook] --> Q
    Q --> REC[Reconciliation coordinator]
    REC --> MATCH[Provider-ID matching]
    MATCH --> API
    MATCH --> STATE

    REC --> ART[Derived artwork generation and publication]
    ART --> STATE
    ART --> JFIMG[Standard Jellyfin item image APIs]
    REQ[Client image request] --> JFIMG
    JFIMG --> RESP[Standard Jellyfin image response]
```

The plugin has two related but separate paths:

1. **Reconciliation path:** obtains and fingerprints Arr metadata ahead of image
   requests. It is asynchronous, bounded, and restart-safe.
2. **Artwork path:** observes validated metadata and source-artwork state,
   renders derived artwork outside the image request path, and publishes it
   through Jellyfin's supported item-image APIs. Native clients then receive it
   through Jellyfin's standard image routes.

The artwork path is the canonical rendering strategy. ArrTags must retain
enough plugin-owned source and provenance state to avoid repeatedly overlaying
its own output and to restore the original source when appropriate. The active
derived image is delivered by Jellyfin's normal image controller, rather than
by an ArrTags response interceptor.
