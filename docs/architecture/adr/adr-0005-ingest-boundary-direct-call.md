# ADR-0005: Ingest Boundary — Direct Sink Call for v0 (Channel Deferred)

## Status
Accepted

## Context

The OTLP receiver (Phase 3) calls `ITraceSink.AcceptAsync(span, ct)` to hand spans to the store. Two implementation shapes were considered:

**Direct call:** the receiver's gRPC handler calls `_traceSink.AcceptAsync(span, ct)` synchronously on the Kestrel thread pool thread. The store's `System.Threading.Lock` serializes concurrent writes. `AcceptAsync` completes as soon as the span is recorded (in-memory, O(1)) and returns `ValueTask.CompletedTask`.

**Channel boundary:** the receiver writes spans to a bounded `Channel<Span>`; a separate `StoreConsumerService : BackgroundService` reads from the channel and calls the store. This decouples receiver threads from the store lock, allows `BoundedChannelFullMode.DropOldest` as a backpressure policy, and provides a natural fan-out point for future consumers (e.g., a live-refresh pump for the TUI).

## Decision

The v0 ingest boundary is a **direct call**: the receiver calls `ITraceSink.AcceptAsync` on the Kestrel thread, the store lock serializes writes, and the method returns immediately. No `Channel<T>`, no `StoreConsumerService`, no background consumer.

## Alternatives Considered

**Channel<Span> + StoreConsumerService.** Adds: a bounded channel with a configurable capacity (`ChannelCapacity` option), a `BackgroundService` consuming it, `BoundedChannelFullMode.DropOldest` eviction, a `DroppedSpanCount` counter. Provides: decoupling of receiver threads from store write latency, memory-bounded burst absorption, a natural fan-out if multiple consumers are needed.

Rejected for v0 because the constraint that would justify it is hypothetical. At local development volumes (hundreds of spans per second at most), the store write is an in-memory `Dictionary` insert plus a brief lock hold — microseconds. The Kestrel thread is not blocked in any meaningful sense. The channel adds a `BackgroundService`, a bounded-channel tuning option (`ChannelCapacity`), and a drop policy decision (`DropOldest` vs `Wait` vs `DropWrite`) for a problem that has not been observed. This is exactly the cost-without-constraint pattern that should be refused.

## Consequences

**Easier:** store implementation is simpler — no consumer, no channel wiring in DI. The write path is a single call and a lock; it is trivially debuggable and testable.

**Harder:** if multiple Kestrel threads call `AcceptAsync` at high rate and the store lock becomes a contention point, there is no buffer to absorb the burst. At observed local development volumes this is not a problem; at artificial load-test volumes it may surface as receiver latency.

**Trigger for revisiting this decision:** observable Kestrel handler latency attributable to store lock contention, or the introduction of push-based live UI refresh (which would require a fan-out from ingest to UI notification — at which point a channel is the natural fit for both purposes). A `DroppedSpanCount` counter and receiver latency instrumentation would surface the former.

**Follow-on decision opened:** if the channel is added, `ChannelCapacity` needs a default value. A reasonable starting point is `10 × StoreOptions.MaxTraces` (e.g., 5000 spans). The drop policy should be `DropOldest` (not `Wait`) — the instrumented app must not block because Sentinel is behind.
