# 11. Failure, consistency, and security policy

Failures are isolated by layer:

- Arr connection failures preserve last-known-good metadata for a bounded
  period, then leave the current usable artwork unchanged when no usable state
  remains.
- Authentication, malformed JSON, unsupported fields, and version drift are
  reported as connection/item status, not Jellyfin failures.
- No match or ambiguous match produces no badge.
- Renderer failures leave the current usable artwork unchanged and record a
  rate-limited error.
- A provenance mismatch or unverifiable active-image identity leaves the current
  artwork unchanged and disables automatic publication/restoration for that
  item.
- A missing or corrupt retained source artifact blocks restoration rather than
  permitting a guessed source or an unsafe replacement.
- Queue overflow coalesces or drops redundant work; it never blocks a library
  event indefinitely.
- Cancellation prevents publication of partial state or partial image output.
- Non-publication state is versioned and persisted for normal restart
  re-evaluation; non-terminal artwork operations are recovered before new work
  is accepted.
- Corrupt, torn, or ambiguous operation records fail closed and never trigger a
  blind image replay or cleanup.

Inbound webhook endpoints require a configured shared secret, validate content
size and payload shape, and rate-limit or coalesce requests. They must not
accept arbitrary item IDs as permission to perform unbounded work. ADR-012 fixes
the V1 contract: an anonymous plugin route authenticated by the
`X-ArrTags-Webhook-Secret` header through the versioned webhook lease and a
constant-time comparison before MVC model binding can read the request body (so
no body is read before the secret is verified for any content type, and a
non-JSON content type is rejected with a bounded `400`); a bounded request
payload rejected with a safe status before allocation; a bounded tolerant parser
that rejects malformed, wrong-shaped, or oversized payloads; a bounded intake
that coalesces duplicate, out-of-order, and replayed deliveries and drops
overflow without blocking; and a bounded provider-record-to-Jellyfin resolution
that enqueues only the same deduplicated work hints as every other trigger. A
webhook never publishes metadata, mutates artwork, calls an Arr endpoint, or
widens work beyond items ArrTags already tracks.

API-key and webhook-secret values are available only through the versioned
private secret boundary described in section 6 and ADR-005. Authentication
failures expose only bounded safe status codes; request headers, bodies, URLs,
and secret-bearing exceptions are never retained in diagnostics. ArrTags
diagnostics go through the host logging pipeline at the bounded, validated
`LogVerbosity`, and every log call emits only bounded, already-redacted values
under the ADR-020 clause 4 contract: authentication failures and secret-bearing
values (API keys, the webhook secret, `SecretLease` values,
`X-Api-Key`/`X-ArrTags-Webhook-Secret` headers, raw request/response bodies,
full provider payloads, and the mutable `PluginConfiguration`) are never logged,
the emitted data shape is identical at every verbosity level, and log output is
bounded by the code-owned `LogThrottle` (section 12). ADR-012
resolves the route exposure, authentication transport, payload policy, replay
handling, rate policy, and administration flow that ADR-005 left open.
