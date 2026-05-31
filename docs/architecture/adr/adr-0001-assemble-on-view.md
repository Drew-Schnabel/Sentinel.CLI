# ADR-0001: Assemble-on-View for Cross-Service Trace Assembly

## Status
Accepted

## Context

Sentinel.CLI's headline feature is assembling spans from multiple local services — each emitting to the same OTLP endpoint — into a single waterfall view keyed on `trace_id`. Spans from different services arrive in arbitrary order and at arbitrary times. When the user selects a trace in the list, the waterfall must show the complete tree as it exists at that moment.

Two assembly strategies were considered:

**Incremental assembly**: maintain a live, partially-assembled tree in the store. Each new span is inserted into the tree in real time. The TUI reads the already-assembled tree on selection.

**Assemble-on-view**: store spans flat (by `span_id`). When a trace is selected, build the tree from the stored flat set at that moment. The store does no assembly.

The volume context: local development, not production. A busy local service might produce a few hundred spans per second. A single trace is unlikely to exceed a few thousand spans. Assembly of a 1000-span trace in a depth-first walk is microseconds on any modern CPU.

The key correctness observation: with assemble-on-view, ordering and orphan-tolerance are automatic. A span whose parent has not yet arrived is stored flat and becomes a root candidate on view. If the parent arrives later, the next time the user views that trace, the child is correctly nested. There is no repair step, no pending-children pool, and no partial-tree state to manage.

## Decision

Assembly is performed in `Trace.Assemble()`, called by the TUI on trace selection, operating on an immutable snapshot of the flat span set. The store records spans flat (keyed by `span_id`) and does no assembly. There is no incremental tree maintenance.

## Alternatives Considered

**Incremental assembly with a pending-children pool.** Maintain a live tree per trace. On each new span, insert it at the correct position; if the parent is not yet present, park the span in a `Dictionary<SpanId, List<Span>>` pending pool and attach it when the parent arrives. This is the approach used by some production APM backends (Jaeger, Zipkin) where spans may arrive hours apart across distributed infrastructure.

Rejected because: (a) local development traces are short-lived and spans typically arrive within milliseconds of each other; (b) the pending pool adds complexity (pool eviction, memory bound, repair logic) that provides no user-visible benefit in the local context; (c) it couples store mutation to assembly logic, making both harder to test in isolation; (d) assemble-on-view gives identical correctness with zero extra state — the flat store IS the pending pool, implicitly.

**Pre-sorting on ingest.** Sort spans by `StartTime` as they enter the store and maintain sorted order. Rejected because sort order is not needed at the store layer — `Assemble()` sorts at assembly time from the flat set.

## Consequences

**Easier:** store implementation is simple (flat map); assembly logic is isolated in `Trace.Assemble()` and fully unit-testable without a store; orphan and out-of-order spans are handled for free.

**Harder:** if a trace is actively receiving spans while the user is viewing it, the waterfall does not update until the user re-selects the trace. This is acceptable for local debugging; the trace list refresh (Section 2c of the design) signals when new spans have arrived.

**Risk introduced:** `Assemble()` is called on every selection. If a trace grows pathologically large (thousands of spans), the O(n) walk is still fast but is not free. A benchmark gate at Phase 2 guards against this.

**Follow-on decisions:** `Trace.Snapshot()` is required to give `Assemble()` a stable, immutable span set to operate on (ADR-0003 and design Section 2b).
