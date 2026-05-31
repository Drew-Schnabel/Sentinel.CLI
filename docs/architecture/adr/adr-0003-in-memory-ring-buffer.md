# ADR-0003: In-Memory Ring Buffer as the Sole Store (v0)

## Status
Accepted

## Context

Sentinel.CLI is a local debugging tool, not a production observability backend. The primary use case is: a developer runs `sentinel`, points an OTLP exporter at it, reproduces an issue, inspects the resulting traces. Session lifetime is minutes to hours. No one expects trace data to survive process restart.

The key volume constraint: local services in active development rarely produce more than a few hundred traces per minute. A "trace" here is a root span + its descendants across all services; a busy checkout flow might produce 10-20 spans. At 100 requests/minute, that is 100 traces/minute, or roughly 6000 traces/hour. At a default cap of 500 traces, the ring buffer represents the last ~5 minutes of activity — sufficient for a typical debugging session.

Persistence options considered were SQLite (embedded relational), LiteDB (embedded document), and raw binary file rotation. All three add non-trivial implementation complexity and bring persistent state management, schema migration, and file-locking concerns that are inappropriate for a tool in this problem domain.

The store boundary — `ITraceSink`/`ILogSink` for writes, `ITraceQueries`/`ILogQueries` for reads — is already the abstraction that would allow a persistent store to be substituted in a future version without changing the Application or Tui layers.

## Decision

The v0 store is an in-memory ring buffer implemented in `Sentinel.CLI.Infrastructure`. It holds up to `SentinelOptions.TraceCapacity` traces (default: 500). When the capacity is reached, the oldest trace is evicted (FIFO by arrival time). All data is lost on process exit. SQLite or any persistent store is out of scope for v0.

The store structure:

```
InMemoryStore
├── _traces: Dictionary<TraceId, TraceSlot>   — O(1) lookup by TraceId
├── _order: Queue<TraceId>                     — FIFO insertion order for eviction
├── _logs: Dictionary<TraceId, List<LogRecord>>  — correlated log records
└── _lock: ReaderWriterLockSlim               — guards all mutable state
```

`TraceSlot` wraps a mutable `Trace` (during ingest) and provides `Snapshot() → Trace` for the query side.

Eviction is by **trace count**, not span count. When `_traces.Count > TraceCapacity`, the front of `_order` is dequeued and the corresponding entry removed from `_traces` and `_logs`. This is O(1).

Logs are stored and evicted with their parent trace (same key). `ILogQueries.StreamAsync` returns logs sorted by `Timestamp` ascending; this requires a merge-sort across all trace log lists, which is acceptable at the volumes described above.

## Alternatives Considered

**SQLite via Microsoft.Data.Sqlite.** Provides persistence across restarts, full SQL query capability, and no memory cap. Rejected because: (a) persistence is explicitly not a user requirement for v0; (b) SQLite adds a native dependency, complicating `dotnet tool install` across Linux/macOS/Windows; (c) schema migrations add operational overhead inappropriate for a version-0 local tool; (d) query latency is dominated by disk I/O, not useful for a sub-10ms UI refresh target.

**LiteDB (embedded document store).** Similar rejection rationale as SQLite. Less mature; fewer test resources in the .NET ecosystem.

**Raw binary file rotation.** Custom serialization, no query capability, high implementation cost. Rejected.

**No cap / unbounded memory.** Rejected because a developer who accidentally points a load test at `sentinel` should not OOM their workstation. The cap with `DropOldest` is the safety valve.

## Consequences

**Easier:** implementation is straightforward, fully in-memory, no external dependencies. No migration strategy needed.

**Harder:** data is lost on restart; no historical analysis. Users who need persistence will work around this by keeping `sentinel` running. This is an acceptable constraint for a local debugging tool.

**New risks introduced:**
- A developer who runs a load test against their service while `sentinel` is attached will exhaust the ring buffer quickly. Mitigation: `--trace-capacity` CLI flag + status bar showing `DroppedSpanCount`.
- Memory usage is proportional to `TraceCapacity × average spans per trace × average attributes per span`. At 500 traces × 20 spans × 10 attributes × ~200 bytes/attribute, rough upper bound is ~20MB — well within normal developer workstation headroom.

**Follow-on decisions opened:**
- `SentinelOptions.TraceCapacity` default value (500) is a guess. The Phase 3 status bar showing `DroppedSpanCount` will inform whether this default needs revision.
- `SentinelOptions.ChannelCapacity` (the `Channel<Span>` bound) is a separate tunable. A reasonable default is `10 × TraceCapacity` = 5000 spans. To be confirmed at Phase 3.
- If a future version adds persistence, `InMemoryStore` is replaced behind the same `ITraceSink`/`ITraceQueries` interfaces. No Application or Tui changes required.
