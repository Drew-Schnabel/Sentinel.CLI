# ADR-0002: Service Identity = Resource Fingerprint (Deferred to Receiver Phase)

## Status
Accepted — implementation deferred to Phase 3 (OTLP receiver)

## Context

In OTLP, each span is emitted with a `Resource` block containing key-value attributes. The `service.name` attribute is the most common service identifier, but it is not guaranteed to be unique. Two independently deployed instances of the same service (blue/green, canary, multi-tenant) share a `service.name` but differ in other resource attributes (`service.instance.id`, `deployment.environment`, `host.name`, etc.).

Sentinel.CLI displays a "service" label per span in the waterfall. The question is: what is the stable identity of a service for the purposes of coloring, labeling, and distinguishing spans from different sources?

The current domain model has only `ServiceName` (a validated string wrapping `service.name`). This was intentional: the OTLP receiver does not exist yet, and `ServiceName` is sufficient for the fixture-driven TUI spike (Phase 1).

When the receiver is implemented (Phase 3), spans will arrive with full `Resource` blocks. The resource fingerprint approach: hash over a normalized, sorted set of resource key-value pairs. The hash is the stable service identity. The display label is derived from `service.name` (always present per OTel semantic conventions); on hash collision (same `service.name`, different fingerprint), the label is disambiguated with a short suffix.

## Decision

Service identity will be a **resource fingerprint** — a stable hash over the normalized OTLP Resource attributes — implemented at the OTLP receiver's anti-corruption layer in Phase 3. The display label is derived from `service.name` with collision disambiguation.

**This decision is not yet implemented.** The domain currently uses `ServiceName` only. The receiver phase will introduce a `ResourceIdentity` type (or extend `ServiceName` with a fingerprint field) and a registry mapping fingerprints to display labels. The exact type design is deferred to Phase 3.

Until Phase 3, `ServiceName` remains the sole service identifier, populated from the `service.name` resource attribute or `"unknown"` if absent.

## Alternatives Considered

**`service.name` alone as identity.** Simple; works for most local development setups where services have unique names. Fails silently when two processes share a `service.name` but are different deployments — their spans are colored/labeled identically with no way to distinguish them in the waterfall.

**`service.name` + `service.instance.id`.** A composite of two well-known attributes. More robust than name alone, but still falls through if `service.instance.id` is absent (it is optional in OTel semantic conventions). Does not generalize to arbitrary resource differentiation.

**Full resource hash (chosen).** Deterministic, covers all resource attributes, stable across restarts if attributes are stable. Requires a registry to map hashes to display labels and to handle collisions. Collision probability is negligible in a local dev context (a developer is unlikely to run two services with identical resource attributes but different `service.name`). Deferred to Phase 3 because the receiver is the only place that has access to the full Resource block.

## Consequences

**Easier:** the display label is always readable (`service.name`), while the identity is always stable (hash). Collision handling is explicit and bounded.

**Harder:** introduces a registry (fingerprint → label) that must be populated at receiver time and queried at render time. The registry is an in-process, in-memory map with no persistence requirement.

**Phase 3 design tasks opened by this decision:**
- Define `ResourceIdentity` (fingerprint type + display label).
- Define `ResourceRegistry : IResourceRegistry` with `Register(Resource) → ResourceIdentity` and `Lookup(fingerprint) → string label`.
- Extend `Span` or the ACL to carry `ResourceIdentity` alongside (or replacing) `ServiceName`.
- Decide whether `Span.Service` remains a `ServiceName` or becomes a `ResourceIdentity`. This is the irreversible schema decision that this ADR defers.

**Risk:** deferring this decision means `Span.Service` is currently typed as `ServiceName`. If Phase 3 changes it to `ResourceIdentity`, all call sites that construct or pattern-match on `Span.Service` must be updated. Given `Span` is immutable-by-construction and the only constructor is `Span.Create(...)`, the refactor is mechanical and safe.
