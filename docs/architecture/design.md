# Sentinel.CLI — Architecture Design

**Scope:** v0 — live local OTLP ingestion + cross-service trace assembly in a terminal UI, distributed as a `dotnet tool`.
**Date:** 2026-05-30
**Status:** Accepted

---

## 1. Module and Boundary Map

### Layer overview

```
┌────────────────────────────────────────────────────────────────────────────┐
│  Sentinel.CLI (entrypoint)                                                 │
│  WebApplication.CreateSlimBuilder — wires layers, maps gRPC endpoints,    │
│  starts Kestrel, then blocks main thread on TuiRunner.Run()               │
├────────────────────────────────────────────────────────────────────────────┤
│  Sentinel.CLI.Tui                                                          │
│  Terminal.Gui v2 shell — reads via ITraceQueries / ILogQueries             │
│  No domain logic; no mutation of stored state                              │
├──────────────────────┬─────────────────────┬──────────────────────────────┤
│  Sentinel.CLI.       │  Sentinel.CLI.       │  Sentinel.CLI.Application    │
│  Receiver (Phase 3)  │  Infrastructure      │  Ports (ITraceSink,          │
│  OTLP gRPC + HTTP    │  InMemoryTelemetry-  │    ILogSink) — write side    │
│  service classes;    │  Store: implements   │  Queries (ITraceQueries,     │
│  ACL: OTLP wire      │  all four ports;     │    ILogQueries) — read side  │
│  bytes → domain;     │  System.Threading.   │  No implementations          │
│  Grpc.AspNetCore +   │  Lock + FIFO ring    │                              │
│  AspNetCore.App ref  │  buffer; snapshot    │                              │
│                      │  on every read       │                              │
├──────────────────────┴─────────────────────┴──────────────────────────────┤
│  Sentinel.CLI.Domain                                                       │
│  Value objects: TraceId, SpanId, ServiceName, AttributeValue,             │
│  TelemetryAttributes, SpanKind, SpanStatus, LogSeverity                   │
│  Entities: Span, Trace (including FindRoot(), Assemble())                 │
│  Entity: LogRecord                                                         │
│  No framework dependencies                                                 │
└────────────────────────────────────────────────────────────────────────────┘
```

### Dependency direction

```
Tui        ──► Application ──► Domain
Tui        ──► Domain              (direct; Tui consumes Span/Trace for rendering)
Receiver   ──► Application ──► Domain
Infrastructure ──► Application ──► Domain
Sentinel.CLI (host) ──► all five
```

Infrastructure and Receiver are **peers** — neither references the other.
Infrastructure has **no** ASP.NET dependency.
Receiver carries `Grpc.AspNetCore` + `Microsoft.AspNetCore.App` framework reference.
Tui **never** references Infrastructure or Receiver.
The host is the only assembly that references all five.

### Assessment of the current hexagonal split

The split is sound. Three things to confirm:

1. **`Trace` mutability (`Record()`) is scoped to the store phase.** Once the store hands a `Trace` snapshot to the query side, that object must be immutable. See Section 2 for the snapshot contract.

2. **`BuildWaterfallRows()` in `MainWindow` reimplements root detection.** The root predicate (`ParentSpanId is null || !_spans.ContainsKey(ParentSpanId)`) is currently duplicated across `Trace.FindRoot()`, `MainWindow.BuildWaterfallRows()`, and `TraceSummary.FromTrace()`. When `Trace.Assemble()` is implemented, `MainWindow.BuildWaterfallRows()` and `TraceSummary.FromTrace()` must be refactored to consume it. The domain owns the predicate; the TUI only renders.

3. **OTLP receiver placement.** The receiver lives in a dedicated `Sentinel.CLI.Receiver` project, not in `Sentinel.CLI.Infrastructure`. The justification: `InMemoryTelemetryStore` (Infrastructure) carries no ASP.NET dependency and must remain independently testable. Adding `Grpc.AspNetCore` + the `Microsoft.AspNetCore.App` framework reference to Infrastructure would force the Infrastructure test project to carry the ASP.NET footprint. `Sentinel.CLI.Receiver` and `Sentinel.CLI.Infrastructure` are peers — both reference Application and Domain; neither references the other. See Section 2b and ADR-0005.

---

## 2. Ingest → Store → View Data Flow

### Process model

After Phase 3, `Program.cs` uses `WebApplication.CreateSlimBuilder`. Kestrel binds on background threads via `app.StartAsync()`. The main thread blocks in `TuiRunner.Run()`. This is why the entire tool is a single `dotnet tool` process with no Docker dependency.

```
[App being debugged]
        │  OTLP/gRPC :4317 (HTTP/2, loopback only)
        │  OTLP/HTTP :4318 (HTTP/1.1, loopback only)
        ▼
┌──────────────────────────────────────────────────────────────────┐
│ TraceExportService / LogsExportService (Sentinel.CLI.Receiver)   │
│  — Kestrel gRPC handler, Kestrel thread pool                     │
│  — ACL: OTLP protobuf bytes → domain objects                     │
│  — Per-span try/catch; drops malformed spans, counts them        │
│  — Direct call: ITraceSink.AcceptAsync(span, ct)                 │
└──────────────────────────┬───────────────────────────────────────┘
                           │ direct call, no channel
                           ▼
┌──────────────────────────────────────────────────────────────────┐
│ InMemoryTelemetryStore (Sentinel.CLI.Infrastructure)             │
│  — Implements ITraceSink, ILogSink, ITraceQueries, ILogQueries   │
│  — System.Threading.Lock (_gate) serializes all access           │
│  — Dictionary<TraceId, Trace> + Queue<TraceId> (FIFO eviction)  │
│  — Snapshot on every read: caller gets immutable Trace copy      │
│  — StoreOptions { MaxTraces=500, MaxLogStream=5000 }             │
└──────────────────────────┬───────────────────────────────────────┘
                           │ ITraceQueries / ILogQueries
                           ▼
┌──────────────────────────────────────────────────────────────────┐
│ TuiRunner / MainWindow (Sentinel.CLI.Tui)                        │
│  — Reads via query interfaces; never writes                      │
│  — _app.Invoke() marshals all widget mutations onto TUI thread   │
└──────────────────────────────────────────────────────────────────┘
```

### 2a. Hosting pivot: `WebApplication.CreateSlimBuilder`

`Program.cs` currently uses `Host.CreateApplicationBuilder(args)`, which returns a `HostApplicationBuilder`. That type has no web-host integration — `MapGrpcService<T>()` and endpoint routing do not exist on a plain `HostApplicationBuilder`. The OTLP receiver requires the ASP.NET Core routing + endpoint middleware pipeline.

**The composition root must change to `WebApplication.CreateSlimBuilder(args)`** at Phase 3. `SlimBuilder` is still a generic host — it runs `IHostedService`s, exposes `IServiceCollection` and `IConfiguration`, and supports the same `StartAsync`/`StopAsync` lifecycle — but it adds Kestrel and endpoint routing without pulling in MVC/Razor/HTTPS redirects.

`Program.cs` shape after the pivot (only the builder type and the `MapGrpcService` calls change; the `StartAsync` → TUI → `StopAsync` shape is preserved):

```csharp
var builder = WebApplication.CreateSlimBuilder(args);

builder.Services
    .AddApplication()
    .AddInfrastructure(builder.Configuration)
    .AddOtlpReceiver(builder.Configuration)
    .AddTui();

builder.Services.AddGrpc();
builder.WebHost.ConfigureKestrel(/* loopback endpoints §2d */);

var app = builder.Build();
app.MapGrpcService<TraceExportService>();
app.MapGrpcService<LogsExportService>();
app.MapOtlpHttp();

await app.StartAsync();   // bind failure surfaces here, before TUI launches
try { app.Services.GetRequiredService<TuiRunner>().Run(); }
finally { await app.StopAsync(); }
```

This is deferred to Phase 3 (the receiver). Phase 2 (store) continues with the current `Host.CreateApplicationBuilder` shape.

### 2b. Receiver project placement

The gRPC service classes (`TraceExportService : TraceService.TraceServiceBase`), the OTLP/HTTP minimal-API endpoints, the generated proto types, and the ACL mapper go in a **dedicated `Sentinel.CLI.Receiver` project**, not in `Sentinel.CLI.Infrastructure`.

Rationale: `InMemoryTelemetryStore` (Infrastructure) has no ASP.NET dependency and is independently testable against its own ports. Adding `Grpc.AspNetCore` and the `Microsoft.AspNetCore.App` framework reference to Infrastructure would force every Infrastructure test project to carry the ASP.NET footprint. Separating receiver from store keeps each project's test surface lean and their dependency footprints distinct.

`Sentinel.CLI.Receiver` references Application + Domain + `Grpc.AspNetCore`; Infrastructure references Application + Domain + no ASP.NET; they are peers, not a stack.

### 2c. Ingest boundary: direct call (no channel in v0)

Receiver threads call `ITraceSink.AcceptAsync(span, ct)` directly. The store's `System.Threading.Lock` (`_gate`) serializes concurrent writes. No `Channel<T>`, no `StoreConsumerService`, no background consumer in v0.

**Why:** at local development volumes (hundreds of spans per second at most), the direct call is correct, cheap, and testable. A `Channel<Span>` + background consumer adds a `BackgroundService`, bounded-channel configuration, a drop-oldest eviction decision, and a dropped-count counter — for a problem (receiver blocking on the store lock) that does not yet exist and may never arise given the store's O(1) write. The channel is not cargo-culted in.

**Trigger for adding the channel:** observable receiver-to-store latency impacting gRPC handler throughput, or the introduction of push-based live UI refresh (which would naturally use a fan-out channel). See ADR-0005.

### 2d. Anti-corruption layer: OTLP wire → domain

OTLP encodes trace_id and span_id as raw bytes (16 and 8 bytes respectively). The domain's `TraceId.Parse()` and `SpanId.Parse()` require 32- and 16-character lowercase hex strings. Conversion: `Convert.ToHexStringLower(bytes)` (.NET 9+). Timestamps are unix-nanos: `DateTimeOffset.UnixEpoch.AddTicks(nanos / 100)`.

The ACL mapper is a pure static function invoked by both the gRPC service class and the OTLP/HTTP endpoint; it is not duplicated per transport.

| Input condition | ACL action |
|---|---|
| trace_id bytes.Length != 16 | Drop span; increment `DroppedSpanCount` |
| span_id bytes.Length != 8 | Drop span; increment `DroppedSpanCount` |
| trace_id or span_id all-zeros | Drop span; increment `DroppedSpanCount` |
| `Span.Create` throws (`FormatException`/`ArgumentException`) | Drop span; increment `DroppedSpanCount`; continue batch |
| Unknown `SpanKind`/`SpanStatusCode` enum value | Map to `Unspecified`/`Unset`; do not drop |
| Missing `service.name` resource attribute | Use `"unknown"` as `ServiceName` |
| AnyValue type not in `AttributeValue` discriminated union | Skip that attribute; keep rest of span |

Catch is **narrow** (`FormatException`/`ArgumentException`). `OperationCanceledException` propagates. Unexpected exception types bubble — do not blanket-`catch (Exception)`. The `ExportTraceServiceResponse.partial_success.rejected_spans` field is populated with the dropped count so well-behaved exporters see their own errors.

### 2e. Snapshot / immutability contract at the store boundary

`Trace` is mutable via `Record(Span)`. The store keeps live, mutable `Trace` objects internally. The query side must never receive a live object.

**Snapshot mechanism:** a private static `Snapshot(Trace live)` method **inside `InMemoryTelemetryStore`** copies span references (spans are immutable; only the dictionary is new) while holding `_gate`. The snapshot is a fresh `Trace` whose `_spans` no writer can touch. `Trace.Snapshot()` is **not added to the domain** — the store owns the snapshot concern.

Every read path (`FindAsync`, `RecentAsync`, `ForTraceAsync`, `StreamAsync`) materializes a snapshot under `_gate`, releases `_gate`, then returns. No `yield return` executes while `_gate` is held (yielding while holding a lock would suspend the iterator with the lock held).

`Trace.Assemble()` (Section 3) operates on the snapshot's span collection, which is stable for the duration of the call.

Store types:
- Class: `InMemoryTelemetryStore` (not `InMemoryStore`)
- Lock: `System.Threading.Lock` (the .NET 9+ `lock`-statement-compatible type), not `ReaderWriterLockSlim`
- Options: `StoreOptions { MaxTraces = 500, MaxLogStream = 5000 }` bound from `configuration.GetSection("Store")`

### 2f. Live refresh

`ILogQueries.StreamAsync` snapshots the capped log buffer and completes; it is not an open live tail. Live tailing is explicitly deferred — when built, it will be an additive port (e.g., `ILogSubscription`) that does not change the existing `StreamAsync` signature. `MainWindow` currently loads once; refresh is a Phase 3+ concern.

### 2g. Security: loopback-only binding

Bind Kestrel to loopback only, never `0.0.0.0`. A telemetry receiver on all interfaces is a silent security hole.

- `127.0.0.1:4317` and `[::1]:4317` — HTTP/2 (h2c, plaintext) for OTLP/gRPC
- `127.0.0.1:4318` and `[::1]:4318` — HTTP/1.1 for OTLP/HTTP

Use `ListenLocalhost(port, o => o.Protocols = ...)` which binds both IPv4 and IPv6 loopback. Ports are configurable via `ReceiverOptions { GrpcPort = 4317, HttpPort = 4318 }` with `ValidateOnStart`.

### 2h. SIGTERM handling

The TUI captures Ctrl-C as a keystroke (`OnAppKey` in `TuiRunner`). SIGTERM (`kill`, `docker stop`, CI teardown) is invisible to the TUI loop — without handling, the process is killed mid-loop, Kestrel is not drained, and the terminal may be left in raw/garbled state.

Fix: `PosixSignalRegistration.Create(PosixSignal.SIGTERM, ctx => { ctx.Cancel = true; tguiApp.RequestStop(); })` in `Program.cs`. This is cross-platform: on Windows it maps to the console control handler. Do not layer `UseConsoleLifetime` under the TUI — two things would fight over Ctrl-C.

### 2i. Failure modes

| Failure | Behavior |
|---|---|
| Port :4317 or :4318 in use | `app.StartAsync()` throws `IOException`/`SocketException(AddressAlreadyInUse)`; catch in `Program.cs`, print plain message, exit non-zero. Never launch the TUI over a dead receiver. |
| Malformed OTLP batch (partial protobuf) | gRPC: framework returns `INVALID_ARGUMENT`. HTTP: `InvalidProtocolBufferException` → 400. Per-span failures are caught and counted; valid spans in the same batch are ingested. |
| `Trace.Assemble()` called on empty trace | Returns `AssembledTrace.Empty`; caller renders "(no spans in trace)". |
| Burst ingest (load test pointed at localhost) | No channel; store lock is held briefly per-span. At extreme rates, gRPC handler threads contend on `_gate`. If this becomes observable, add Channel<T> per ADR-0005 trigger. |

---

## 3. Cross-Service Trace Assembly

### 3a. Design intent

Assembly is **assemble-on-view**: when the user selects a trace in the trace list, the TUI calls `ITraceQueries.FindAsync`, receives a snapshot, and calls `Trace.Assemble()` on it. The store does no assembly. The domain owns the assembly logic.

### 3b. The shared root predicate

A span is a **root candidate** if and only if:

```csharp
bool IsRoot(Span s, IReadOnlyDictionary<SpanId, Span> all) =>
    s.ParentSpanId is null ||
    !all.ContainsKey(s.ParentSpanId.Value);
```

This predicate is used in exactly two places in the domain:

- `Trace.FindRoot()` — returns the single root if there is exactly one, otherwise `null`.
- `Trace.Assemble()` — collects all root candidates as the starting set for the walk.

`TraceSummary.FromTrace()` in the Tui layer must be refactored to call `trace.FindRoot()` rather than duplicating the predicate. `MainWindow.BuildWaterfallRows()` must be replaced with `Trace.Assemble()` + a flattening render pass.

### 3c. `Trace.Assemble()` — full contract

**Signature:**

```csharp
// Domain: Sentinel.CLI.Domain.Telemetry.Spans
public sealed class Trace
{
    // Returns a depth-first, root-ordered assembly of all spans in this trace.
    // Safe to call on a snapshot returned by ITraceQueries (immutable _spans).
    public AssembledTrace Assemble();
}

public sealed class AssembledTrace
{
    public static AssembledTrace Empty { get; }

    // All root-level nodes, ordered by StartTime ascending, ties broken by SpanId lexicographic order.
    public IReadOnlyList<SpanNode> Roots { get; }

    // Depth-first flattening of Roots + their subtrees.
    // Each entry carries its depth for indent-based rendering.
    public IReadOnlyList<(SpanNode Node, int Depth)> Flatten();

    // Wall-clock envelope: min(StartTime) across all spans, max(EndTime) across all spans.
    // Returns (DateTimeOffset.MinValue, DateTimeOffset.MinValue) when empty.
    public (DateTimeOffset TraceStart, DateTimeOffset TraceEnd) Envelope { get; }

    // Count of all spans represented. Equals Roots.Sum(recursively count all nodes).
    public int SpanCount { get; }
}

public sealed class SpanNode
{
    public Span Span { get; }
    // Children ordered by StartTime ascending, ties broken by SpanId lexicographic order.
    public IReadOnlyList<SpanNode> Children { get; }
}
```

**Algorithm (iterative to avoid stack overflow on deep traces):**

```
1. Build children map: Dictionary<SpanId, List<Span>> from all spans where ParentSpanId != null.
2. Collect root candidates: all spans where IsRoot(s, _spans) is true.
3. Sort root candidates by (StartTime asc, SpanId asc).
4. Walk each root iteratively (explicit stack), building SpanNode tree.
   - For each span, look up children; sort children by (StartTime asc, SpanId asc).
   - Push children in reverse order onto the stack so the first child is processed first.
5. Track visited SpanIds. After the root walk, collect any unvisited spans
   (unreachable due to cycles or logic error) and append them as additional
   root-level nodes sorted by (StartTime asc, SpanId asc). These are "orphaned"
   in the graph-theoretic sense even if they have a ParentSpanId set.
6. Return AssembledTrace with Roots and computed Envelope.
```

**Behavior matrix:**

| Scenario | Behavior |
|---|---|
| Single root, all children present | Root at Roots[0]; full tree. |
| Multiple roots (cross-service, concurrent entry points) | All roots in Roots, ordered by StartTime. |
| No root (all spans have ParentSpanId set to a span not in the trace) | Every span qualifies as a root candidate (IsRoot = true for all); all appear in Roots, ordered by StartTime. |
| Orphan span (parent_span_id set but parent never arrived) | IsRoot returns true for this span; it appears as a top-level node in Roots. |
| Deep nesting | Iterative walk; no stack overflow for any depth. Tested at 10,000 levels. |
| Cycle (A→B, B→A) | Neither A nor B qualifies as a root candidate (both have parents present). After the root walk completes, both are "unvisited" and appended as extra root-level nodes. |
| Empty trace | Returns `AssembledTrace.Empty`. |
| Single span, no parent | Single root, zero children. |

**Completeness invariant:** every span in `Trace.Spans` appears exactly once in the output of `Assemble()` — either reachable from a root via the child walk, or in the orphan-appended set. This is a post-condition that should be asserted in unit tests.

**Determinism:** For any given set of spans, `Assemble()` returns the same tree on every call. Sort keys are `(StartTime, SpanId)` where SpanId is the 16-character hex string compared with `StringComparer.Ordinal`. `StartTime` equality on two different spans from different services is unlikely but handled.

### 3d. TUI rendering after assembly

`MainWindow.BuildWaterfallRows()` is replaced by:

```csharp
var assembled = trace.Assemble();
var rows = assembled.Flatten()
    .Select(entry => WaterfallRow.From(entry.Node.Span, entry.Depth, assembled.Envelope))
    .ToList();
```

`WaterfallRow.From` is a static factory that produces the display string using the depth for indentation and the `Envelope` for the percentage-width bar — the same bar rendering currently in `BuildBar()`.

---

## 4. Architecture Decision Records

See `docs/architecture/adr/` for the full records:

- [ADR-0001: Assemble-on-View vs. Incremental Assembly](adr/adr-0001-assemble-on-view.md)
- [ADR-0002: Service Identity = Resource Fingerprint (Deferred)](adr/adr-0002-service-identity-resource-fingerprint.md)
- [ADR-0003: In-Memory Ring Buffer vs. Persistence](adr/adr-0003-in-memory-ring-buffer.md)
- [ADR-0004: Terminal.Gui v2 as the TUI Shell](adr/adr-0004-terminal-gui-v2.md)
- [ADR-0005: Ingest Boundary — Direct Sink Call vs. Channel](adr/adr-0005-ingest-boundary-direct-call.md)

---

## 5. Build Sequencing

Each phase is independently shippable and leaves the system in a working, runnable state.

### Phase 1 — TUI spike (DONE)

**Deliverable:** `sentinel` tool runs, renders fixture traces, waterfall, details pane. No live ingest.

**State on completion:** `FixtureTraceQueries` satisfies `ITraceQueries` + `ILogQueries`. `InfrastructureServiceCollectionExtensions.AddInfrastructure` is a no-op. `TuiServiceCollectionExtensions` wires fixtures.

**Independently revertible:** Yes — the fixture path is the only path.

---

### Phase 2 — In-memory store + `Trace.Assemble()` (NEXT)

**What ships:**
- `Trace.Assemble()` implemented in Domain with full test coverage (all scenarios in Section 3c).
- `InMemoryTelemetryStore` in `Sentinel.CLI.Infrastructure` implementing `ITraceSink`, `ILogSink`, `ITraceQueries`, `ILogQueries`.
  - `System.Threading.Lock (_gate)` serializes all access.
  - `Dictionary<TraceId, Trace>` + `Queue<TraceId>` for FIFO eviction.
  - Private static `Snapshot(Trace live)` method inside the store; **not** on `Trace` in Domain.
  - `StoreOptions { MaxTraces = 500, MaxLogStream = 5000 }` bound from `"Store"` config section, `ValidateDataAnnotations()`, `ValidateOnStart()`.
  - No `Channel<T>`, no `BackgroundService` consumer (see ADR-0005).
- `AddInfrastructure` registers `InMemoryTelemetryStore` as singleton under all four interface names.
- `AddTui` stops registering `FixtureTraceQueries` as `ITraceQueries`/`ILogQueries`; fixtures remain in source for tests and a future `--demo` flag.
- `MainWindow.BuildWaterfallRows` replaced by `Assemble()` + `Flatten()`.
- `TraceSummary.FromTrace` refactored to call `trace.FindRoot()`.
- Host stays `Host.CreateApplicationBuilder` for Phase 2 (pivot deferred to Phase 3).

**State on completion:** Tool runs, takes no live data yet, shows an empty trace list. Assemble logic is fully tested.

**Independently revertible:** Yes — swap `AddInfrastructure` back to no-op + restore fixture DI.

**Gate to Phase 3:** `InMemoryTelemetryStore` integration test: write N spans via `ITraceSink`, read back via `ITraceQueries`, assert snapshot immutability (write after read does not affect returned snapshot), assert `Assemble()` output matches expected tree.

---

### Phase 3 — OTLP receiver

**What ships:**
- New `Sentinel.CLI.Receiver` project added to the solution.
  - `PackageReference` for `Grpc.AspNetCore` + `Google.Protobuf` (both in CPM, no version bump needed).
  - `FrameworkReference` for `Microsoft.AspNetCore.App`.
  - `TraceExportService : TraceService.TraceServiceBase`, `LogsExportService`.
  - OTLP/HTTP minimal-API endpoints (`POST /v1/traces`, `/v1/logs`).
  - ACL mapper: OTLP wire bytes → domain objects (bytes→hex, unix-nanos→DateTimeOffset, AnyValue→AttributeValue).
  - `ReceiverOptions { GrpcPort = 4317, HttpPort = 4318 }` with `ValidateOnStart`.
  - `DroppedSpanCount` counter.
- `Program.cs` pivots from `Host.CreateApplicationBuilder` to `WebApplication.CreateSlimBuilder`.
  - Kestrel bound loopback-only (Section 2g).
  - `MapGrpcService<TraceExportService>()`, `MapGrpcService<LogsExportService>()`, `MapOtlpHttp()`.
  - Port-in-use error handling (Section 2i).
  - `PosixSignalRegistration` for SIGTERM (Section 2h).
- `DroppedSpanCount` surfaced in a TUI status bar.
- Startup error if port is in use: clear console message + non-zero exit.

**State on completion:** `dotnet tool install -g Sentinel.CLI` + point any OTLP exporter at `localhost:4317`; traces appear in the TUI.

**Independently revertible:** The `Sentinel.CLI.Receiver` project is only wired in by `AddOtlpReceiver()` + the endpoint mapping in `Program.cs`. Removing those calls and reverting the builder type restores store-only mode.

---

### Phase 4 — Log correlation

**What ships:**
- `ILogQueries.ForTraceAsync` exercised with real data.
- `ILogQueries.StreamAsync` powering a live log pane (second tab or second list).
- Severity-based coloring in the log pane (Terminal.Gui `ColorScheme`).
- Log-to-span linking: the detail pane already correlates logs by `SpanId`; this becomes live.

**State on completion:** Developer sees span details + correlated logs in one pane, live.

---

### Phase 5 — Metrics (scope TBD)

See Section 6, open question 3. Scope is not locked; this phase requires a design checkpoint before implementation begins.

---

## 6. Risks and Open Questions

### Risks

| # | Risk | Likelihood | Impact | Leading indicator | Mitigation | Contingency |
|---|---|---|---|---|---|---|
| R1 | Ring-buffer default of 500 traces too small for high-throughput local services (e.g., 1000 RPS with 5-span traces = ~10s of data) | Medium | Medium | Users complain traces drop before they can inspect them | Expose `--trace-capacity` CLI flag; document the math | Allow `--trace-capacity 0` = no cap (memory-unbounded; user's problem) |
| R2 | `Trace.Assemble()` is O(n) in spans per trace but called on every trace selection — acceptable for local dev volumes | Low | Low | Noticeable lag on traces with >1000 spans | Benchmark with 1000 spans; add a depth cap warning | Cache assembled result per (TraceId, SpanCount) tuple — invalidate on new span |
| R3 | Terminal.Gui v2 does not have a stable non-obsolete read-only text widget; current `TextView` is deprecated | Medium | Low | Breaking change in Terminal.Gui 2.5+ | Monitor Terminal.Gui release notes; the `#pragma warning disable CS0618` is already in place | Evaluate the external `Editor` package when it stabilizes |
| R4 | Port :4317 conflict with another local OTLP receiver (Aspire, OTel Collector) | High for teams running Aspire | Medium | `host.StartAsync()` fails with `Address already in use` | Print clear error with suggestion; add `--otlp-grpc-port` / `--otlp-http-port` flags | |
| R5 | Cycle in span parent_span_id graph (rare but possible with buggy instrumentation) | Low | Low | Spans vanish from waterfall without explanation | Cycle detection + orphan-append in `Assemble()` covers this; add a status indicator when orphans are promoted | |
| R6 | Store lock contention on burst ingest (load test pointed at localhost; many Kestrel threads contending on `_gate`) | Low | Medium | Kestrel handler latency rises; `DroppedSpanCount` absent (there is no channel drop in v0) | Direct call is O(1) under lock; monitor receiver latency. If contention observed, add Channel<T> per ADR-0005 | |

### Open questions

**Q1 — CLI flag surface.**
`SentinelOptions` needs to be populated from command-line args (not just `appsettings.json`). Which flags are v0? At minimum: `--otlp-grpc-port` (default 4317), `--otlp-http-port` (default 4318), `--trace-capacity` (default 500). Recommend `System.CommandLine` over `args[]` parsing. Decision needed before Phase 3.

**Q2 — Ring-buffer eviction unit.**
The current design evicts by **trace count** (oldest trace dropped when `TraceCapacity` exceeded). An alternative is eviction by **span count** (total spans across all traces). Span-count eviction is more predictable in terms of memory but requires tracking aggregate span count. Trace-count eviction is simpler and matches the user mental model ("last N requests"). Trace-count is the v0 default; revisit if memory complaints emerge.

**Q3 — Metrics model.**
Metrics in OTLP are a different data shape from traces and logs: gauge, sum, histogram, exponential histogram. A terminal display of histograms is non-trivial. Options range from a raw attribute dump (simplest, zero insight) to a sparkline per gauge series (meaningful, non-trivial layout). This is the least-specified v0 scope item. A design checkpoint is required before Phase 5. Do not begin implementation without it.

**Q4 — OTLP receiver project placement.**
Resolved: a dedicated `Sentinel.CLI.Receiver` project (Section 2b, ADR-0005). Infrastructure stays clean of ASP.NET dependency.

**Q5 — `Trace.Snapshot()` immutability mechanism.**
Resolved: snapshot is a private static method inside `InMemoryTelemetryStore`, not on `Trace` in the domain (Section 2e). This was the simpler option and keeps the domain free of store concerns.

---

## Acceptance criteria for this artifact

- [ ] `Trace.Assemble()` contract is specified completely enough that an engineer can implement it from Section 3c alone, including the cycle/completeness invariant.
- [ ] All five ADRs are written and linked from Section 4.
- [ ] The snapshot/immutability contract is stated (Section 2e): snapshot is in the store, not in the domain.
- [ ] The ACL drop policy is stated (Section 2d): narrow catch, partial-success response, malformed-span counter.
- [ ] The ingest concurrency model is explicit and consistent with `implementation-notes.md`: direct call, no channel in v0 (Section 2c, ADR-0005).
- [ ] The hosting pivot to `WebApplication.CreateSlimBuilder` is stated and its reason given (Section 2a).
- [ ] `Sentinel.CLI.Receiver` as a separate project is stated with justification (Section 2b).
- [ ] Loopback-only binding and SIGTERM handling are stated (Sections 2g, 2h).
- [ ] Phase sequencing shows each phase is independently shippable (Section 5).
- [ ] Risk register is present with concrete mitigations (Section 6).
- [ ] No design-history annotation references in production code are implied or required by this document.
