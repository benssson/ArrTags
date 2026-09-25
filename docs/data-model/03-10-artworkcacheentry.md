# 3.10 ArtworkCacheEntry

**Purpose:** A bounded, evictable work record for one rendered image before or
around publication. It has no authority over metadata or publication state.

| Field | Type | Required | Field Ownership | Notes |
| --- | --- | --- | --- | --- |
| `cacheVersion` | Version identifier | Yes | Plugin | Allows cache format changes without migration assumptions. |
| `renderKey` | Opaque key | Yes | Generated | Includes item, surface, source image fingerprint, metadata/config fingerprints, and renderer version. |
| `outputFingerprint` | Opaque fingerprint | Yes | Generated | Fingerprint of the output-affecting inputs. |
| `artifact` | Image bytes or artifact reference | Yes | Generated/cache | Bounded and evictable; storage mechanism is intentionally unspecified. |
| `contentType` | MIME type | Yes | Generated | Content type of the artifact. |
| `width` / `height` | Integer dimensions | Yes | Generated | Dimensions of the cached representation. |
| `sizeBytes` | Integer | Yes | Generated | Used for resource limits and eviction. |
| `etag` | Internal artifact identity, if useful | Optional | Generated | Jellyfin owns the validator for the published item image. |
| `createdAt` | Timestamp | Yes | Generated | Creation time. |
| `lastAccessedAt` | Timestamp | Optional | Generated | Eviction accounting. |
| `expiresAt` | Timestamp | Yes | Cache policy/generated | Expired entries are not served. |

### 3.10.1 PublishedArtworkState

**Purpose:** The conceptual state connecting plugin-owned source-artwork
provenance to a derived image currently published as Jellyfin item artwork. It
is not a Jellyfin storage schema and does not require the original source to be
held in the canonical metadata snapshot. It is the authority for publication
ownership and guarded restoration; Jellyfin image metadata alone is not enough.

| Field | Type | Required | Field Ownership | Notes |
| --- | --- | --- | --- | --- |
| `modelVersion` | Version identifier | Yes | Plugin | Changes to ownership semantics invalidate or migrate the record. |
| `jellyfinItemId` | Jellyfin item identifier | Yes | Jellyfin/plugin | Local subject whose active image may be derived. |
| `imageSurface` | Image type and optional index | Yes | Jellyfin/configuration | V1 surface selected by the image policy. |
| `state` | `NotPublished`, `Published`, `OwnershipLost`, `OwnershipUnknown`, `RestorePending`, `Restored`, `RestoreBlocked`, or `Removed` | Yes | Generated | Controls whether ArrTags may publish, restore, or remove the active image. |
| `sourcePresence` | `Present` or `Absent` | Yes when a publication session exists | Generated | `Absent` means the surface had no image before the session. |
| `sourceArtifactId` | Opaque retained-artifact identifier | Required when source is present | Generated | Content-addressed plugin-owned artifact; never a Jellyfin cache path or media path. |
| `sourceFingerprint` | SHA-256 of retained source bytes | Required when source is present | Generated | Hashes the exact bounded bytes used for rendering and restoration. |
| `sourceCaptureIdentity` | ActiveImageIdentity | Required for a publication session | Generated | Identity observed immediately before the first ArrTags publication, including an explicit absent baseline. |
| `ownershipToken` | Opaque random token | Required for an active session | Generated | Stable for one original-to-derived session; not a Jellyfin token. |
| `publicationToken` | Opaque random token | Required when published | Generated | New for each successful ArrTags-derived publication. |
| `activeImageIdentity` | ActiveImageIdentity | Required when published | Jellyfin/plugin | Expected identity of the currently active ArrTags image; re-observe before mutation. |
| `publishedFingerprint` | Opaque logical publication fingerprint | Required when published | Generated | Includes source, metadata, configuration, renderer, and schema inputs; not ownership proof alone. |
| `rendererVersion` | Version identifier | Yes when published | Plugin | Changes require regeneration. |
| `lastOwnershipObservation` | ActiveImageIdentity plus result | Yes | Generated | Latest `Owned`, `Changed`, or `Unknown` comparison for diagnostics and restart revalidation. |
| `stateRevision` | Monotonic integer | Yes | Generated | Increments for each committed state transition on the item/surface. |
| `lastOperationId` | Opaque operation identifier | Optional | Generated | Links the committed state to the durable artwork operation that produced it. |
| `updatedAt` | Timestamp | Yes | Generated | Last publication or restoration-state change. |

`sourceArtifactId` identifies an immutable artifact stored under the ArrTags data
folder. The artifact contains the exact source bytes, MIME type, byte length, and
integrity metadata. Its path is an implementation detail and is not an ownership
signal. The artifact remains retained while the session is `Published` or
`RestorePending`; after `OwnershipLost`, it may only be removed by bounded
retention cleanup and must never be used for automatic restoration.

`ActiveImageIdentity` is the observable identity of one Jellyfin image surface:

| Field | Type | Semantics |
| --- | --- | --- |
| `imageSurface` | Image type and index | Prevents an image on another surface or index from satisfying the comparison. |
| `presence` | `Present` or `Absent` | Absence is an identity value, not an error. |
| `contentSha256` | SHA-256 | Required for a `Present` ownership proof; hashes the bounded active representation used for comparison. |
| `byteLength` | Integer | Supporting evidence and integrity check for the content hash. |
| `width` / `height` | Integer | Supporting Jellyfin image metadata. |
| `dateModifiedUtc` | Timestamp | Supporting Jellyfin image metadata; not sufficient by itself. |
| `jellyfinImageTag` | Opaque string | Supporting Jellyfin cache/representation validator when available; not an ArrTags ownership token. |

The comparison is fail-closed. The surface and presence must match, the active
content hash must match, and every Jellyfin identity value recorded at
publication must still match when observable. A missing required hash or an
otherwise unavailable observation produces `OwnershipUnknown`, not ownership. A
path is never sufficient evidence because Jellyfin may reuse a path for a
different image. The Jellyfin image tag is useful for detecting replacement but
is not content- or actor-specific and cannot prove ownership on its own.

### 3.10.2 PublishedArtworkState transitions

These are ownership transitions. Crash-consistent ordering and restart
reconciliation are defined separately by `ArtworkOperation` in section 3.10.3.

| Current state | Condition | Next state | Required behavior |
| --- | --- | --- | --- |
| `NotPublished` or `Restored` | A new publication is eligible | `Published` | Observe the surface, retain its exact source or record `Absent`, create a new ownership token, and publish only if the baseline is recoverable. |
| `Published` | Current identity exactly matches `activeImageIdentity` | `Published` | Reuse the original source artifact; do not capture the ArrTags image as a new source. A changed publication gets a new publication token and active identity while retaining the same ownership token and source artifact. |
| `Published` | Current identity differs from the expected identity | `OwnershipLost` | Treat the change as external, regardless of whether it was a user, Jellyfin, provider, or another plugin. Do not publish, restore, or remove automatically. |
| `Published` | Current identity cannot be completely observed or compared | `OwnershipUnknown` | Leave the active image unchanged and perform no guarded mutation. |
| `Published` | Disable or uninstall requests restoration | `RestorePending` | Re-observe the active image immediately before any restoration mutation. |
| `RestorePending` | Expected identity matches and source artifact passes integrity validation | `Restored` | Restore the retained source, or remove the ArrTags image when `sourcePresence` is `Absent`, then verify the resulting surface. |
| `RestorePending` | Identity differs or cannot be proven | `OwnershipLost` or `OwnershipUnknown` | Do not mutate the active image. Retain the diagnostic state and artifact subject to bounded cleanup. |
| `RestorePending` | Source artifact is missing or corrupt | `RestoreBlocked` | Leave the active image unchanged and do not claim restoration completed. |
| `RestorePending` | The restoration result cannot be verified | `RestoreBlocked` | Do not claim completion or perform another automatic mutation; the associated operation enters `RecoveryBlocked` until reconciled. |
| Any state | Jellyfin item is removed | `Removed` | Perform no image operation against the missing item; clean plugin-owned records/artifacts only under the retention policy. |

An `OwnershipLost`, `OwnershipUnknown`, or `RestoreBlocked` record is never
automatically re-baselined. A future explicit administrative re-baseline, if
supported, starts a new ownership session and captures the then-current image;
it does not silently reclaim a later image.

### 3.10.3 ArtworkOperation

**Purpose:** A durable write-ahead operation record for one publication or
restoration attempt. It bridges the non-transactional Jellyfin image APIs and
the plugin-owned `PublishedArtworkState`. It is not an evictable render-cache
entry and must remain available until the operation reaches a terminal state and
its artifacts are safe to clean up.

Only one non-terminal operation may exist for an item/image surface at a time.
The operation generation fences stale queued work from publishing or finalizing
after a newer operation or lifecycle fence has been accepted.

| Field | Type | Required | Field Ownership | Notes |
| --- | --- | --- | --- | --- |
| `modelVersion` | Version identifier | Yes | Plugin | Invalid operation records are quarantined and never replayed blindly. |
| `operationId` | Opaque random identifier | Yes | Generated | Correlates the journal, staged artifacts, diagnostics, and final state. |
| `kind` | `Publication` or `Restoration` | Yes | Generated | Selects the target state and artifact use. |
| `jellyfinItemId` | Jellyfin item identifier | Yes | Jellyfin/plugin | Operation subject. |
| `imageSurface` | Image type and optional index | Yes | Jellyfin/configuration | Operation scope; must match both image identities. |
| `generation` | Monotonic per-item/surface value | Yes | Generated | Prevents stale work from becoming authoritative. |
| `ownershipToken` | Opaque token | Yes for an owned session | Generated | Carries the Blocker 1 ownership session through recovery. |
| `priorPublicationToken` | Opaque token | Optional | Generated | Expected prior ArrTags publication when replacing an owned image. |
| `publicationToken` | Opaque token | Required for publication | Generated | Candidate publication identity to commit if the postcondition matches. |
| `expectedBeforeIdentity` | ActiveImageIdentity | Yes | Generated/Jellyfin observation | Exact precondition observed before the external image mutation. |
| `candidateAfterPresence` | `Present` or `Absent` | Yes | Generated | Explicit candidate after-target presence; `Absent` removes the ArrTags image rather than writing a new one. |
| `candidateAfterContentSha256` | SHA-256 | Required when the after target is present | Generated | Hash of the durable artifact intended to become active; an absent after target records `presence = Absent`; the complete after identity is learned by readback. |
| `observedAfterIdentity` | ActiveImageIdentity | Optional | Jellyfin observation | Recorded only after the effective active image is re-observed. |
| `sourcePresence` | `Present` or `Absent` | Yes | Generated | Explicit presence of the retained source baseline; `Absent` is an explicit baseline with no artifact. |
| `sourceArtifactId` | Opaque artifact identifier | Required when source is present | Generated | Retained original used by publication or restoration. |
| `derivedArtifactId` | Opaque artifact identifier | Required for publication | Generated | Durable, validated render output; never only an evictable cache entry. |
| `candidatePublicationFingerprint` | Opaque logical publication fingerprint | Required for publication | Generated | The target `publishedFingerprint` to commit when the after postcondition is recovered; recorded so recovery never has to re-render or re-derive it. |
| `rendererVersion` | Version identifier | Required for publication | Plugin | The renderer version that produced the candidate derived artifact; recorded so a recovered commit does not misattribute the active image to a later renderer version. |
| `phase` | ArtworkOperationPhase | Yes | Generated | Durable lower-bound marker for external side effects. |
| `lifecycleFence` | Normal, Disable, Uninstall, or ItemRemoved | Yes | Generated | Prevents new publication work during lifecycle transitions. |
| `attempt` | Non-negative integer | Yes | Generated | Bounded recovery/retry accounting. |
| `lastError` | Redacted error summary | Optional | Generated | Diagnostics only; never a provider secret or raw payload. |
| `createdAt` / `updatedAt` | Timestamp | Yes | Generated | Journal lifecycle timestamps. |

`ArtworkOperationPhase` has these values:

| Phase | Meaning |
| --- | --- |
| `Prepared` | Source/derived artifacts and the complete operation intent are durable; no external image mutation has been started by this operation. |
| `MutationStarted` | The operation has durably recorded that `SaveImage` or the supported image-removal operation may have started. A crash before or during the call is treated as uncertain. |
| `RepositoryUpdateStarted` | The image mutation may have completed and the durable item update may have started. Both effects are re-observed during recovery. |
| `VerificationPending` | The supported item-image state and effective image representation must be read again before finalization. |
| `FinalizationPending` | The intended postcondition was observed; the final `PublishedArtworkState` or `Restored` state must be durably committed. |
| `Committed` | Final plugin state is durable and references the operation; cleanup may proceed under the artifact rules. |
| `Aborted` | The operation was safely stopped without adopting its candidate result, usually because the before identity no longer matched or the item was removed. |
| `RecoveryBlocked` | The operation or required observation is corrupt, unavailable, or ambiguous; no further automatic image mutation is permitted. |

The phase is deliberately a lower-bound marker. Recovery must assume that an
external call may have happened whenever the phase is `MutationStarted` or
later, even if the corresponding acknowledgment was not persisted. A durable
operation record is written before the first external image mutation. Operation
records, state records, and artifact manifests use versioned integrity metadata,
stable-storage flushing, and atomic replacement; a torn or invalid record is
quarantined rather than guessed.

Staged artifacts are promoted from temporary files only after bounded size,
format, and hash validation. They are retained until the operation is committed,
aborted, or tombstoned as removed, and cleanup must first prove that the
artifact is not the current active image. If that proof is unavailable, the
artifact is retained or quarantined.

### 3.10.4 ArtworkOperation transitions and recovery

| Phase or observation | Recovery action |
| --- | --- |
| `Prepared` and current identity equals `expectedBeforeIdentity` | Revalidate the generation and lifecycle fence, then resume the deterministic operation. |
| Any mutation-started phase and current identity equals `expectedBeforeIdentity` | The mutation did not become observable or the image was restored to the exact expected identity. Revalidate immediately and retry the same operation only under the current generation; never recapture a new source. |
| Any phase and current identity matches `observedAfterIdentity` or a validated candidate after identity | Ensure the normal Jellyfin item update is persisted, commit the target plugin state, then mark the operation `Committed`. |
| Any phase and current identity differs from both before and after identities | Mark the ownership state `OwnershipLost` when the image is observable, abort the operation, and never restore or remove the external image. |
| Any phase and the current identity cannot be observed or compared | Mark `OwnershipUnknown` or `RecoveryBlocked`, leave the image untouched, and require later reconciliation or explicit administration. |
| Any phase and the Jellyfin item is absent | Write an `ItemRemoved` tombstone, perform no image mutation, and retain cleanup records until the removal decision is durable. |
| Final state is durable but journal is non-terminal | Treat the final state and verified active identity as authoritative, mark the operation `Committed`, and perform only safe cleanup. |

Recovery runs under the same per-item/image-surface serialization as normal
publication and completes before new work for that subject is accepted. It is
postcondition-based rather than a distributed transaction: Jellyfin and the
plugin cannot be committed atomically, so uncertainty always resolves toward
preserving the currently observable artwork.
