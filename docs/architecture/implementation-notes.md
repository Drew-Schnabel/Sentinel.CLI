# Implementation notes — Infrastructure (store + OTLP receiver)

Design for the two riskiest Infrastructure pieces of Sentinel.CLI. This is the
spec the implementer follows; it does **not** ship code yet. It builds strictly
within the locked decisions (assemble-on-view, snapshot-on-read, dumb-FIFO ring
buffer, deferred fingerprint identity / pending-children pool, no design-history
references in production source).

Target: .NET 10, CPM, `TreatWarningsAsErrors=true`, `AnalysisLevel=latest-recommended`,
Nullable + ImplicitUsings on. Everything below must compile clean under those.

---

## 0. Orientation — what's already there

- Host is `Host.CreateApplicationBuilder(args)` in `src/Sentinel.CLI/Program.cs`,
  with the shape `await host.StartAsync()` → `TuiRunner.Run()` (blocking, on the
  main thread) → `await host.StopAsync()` in `finally`. **We keep this exact
  shape** — only the builder type changes (§2.1).
- `Trace.Spans` returns `_spans.Values` — a **live** view over the writer's dict.
  `Span`, `LogRecord`, and the value objects are immutable with throwing factories
  (`Span.Create`, `TraceId.Parse`, `SpanId.Parse`, `ServiceName.From`).
- `Trace.Assemble()` is a **locked future addition** in the domain; it does not
  exist yet. The store does **no** assembly — it stores flat and hands out
  snapshots. The UI's `MainWindow.BuildWaterfallRows` already does parent/child
  layout from a flat span set, so the store does not need `Assemble()` to be a
  correct drop-in today.
- The drop-in target is `FixtureTraceQueries` (TUI), which implements
  `ITraceQueries` **and** `ILogQueries`. The store must be a behavioral drop-in:
  the UI swap is a one-line DI change (§1.6).

---

## 1. `InMemoryTelemetryStore`

A single class in `Sentinel.CLI.Infrastructure` implementing all four ports:
`ITraceSink`, `ILogSink`, `ITraceQueries`, `ILogQueries`. One class because the
write paths and read paths share one lock and one set of backing collections;
splitting them would mean sharing mutable state across types behind the same lock,
which is strictly worse.

### 1.1 Failure mode this defends against

The receiver thread(s) call `Record` on a `Trace` while the UI thread enumerates
`trace.Spans` (`BuildWaterfallRows` → `trace.Spans.ToList()`). `Dictionary<,>`
enumeration throws `InvalidOperationException` ("collection was modified") on
concurrent mutation. The store closes this by (a) serializing all access under one
lock and (b) **never** handing the live `Trace` to a reader — every read path
returns a fresh snapshot whose dict no writer can touch.

### 1.2 Field layout

```csharp
using System.Threading;                       // System.Threading.Lock (net9+)
using Sentinel.CLI.Application.Telemetry.Ports;
using Sentinel.CLI.Application.Telemetry.Queries;
using Sentinel.CLI.Domain.Telemetry.Common;
using Sentinel.CLI.Domain.Telemetry.Logs;
using Sentinel.CLI.Domain.Telemetry.Spans;

internal sealed class InMemoryTelemetryStore
    : ITraceSink, ILogSink, ITraceQueries, ILogQueries
{
    private readonly Lock _gate = new();                       // net10 Lock, not object

    // Trace storage. Dictionary for O(1) find/record; Queue for FIFO eviction order.
    private readonly Dictionary<TraceId, Trace> _traces = new();
    private readonly Queue<TraceId> _insertionOrder = new();   // first-seen order, for eviction

    // Per-trace log index (correlated logs for the detail pane / ForTraceAsync).
    private readonly Dictionary<TraceId, List<LogRecord>> _logsByTrace = new();

    // Capped global log stream (StreamAsync). Ring of recent logs across all traces.
    private readonly Queue<LogRecord> _logStream = new();

    private readonly StoreOptions _options;                    // IOptions<StoreOptions>.Value

    public InMemoryTelemetryStore(IOptions<StoreOptions> options)
        => _options = options.Value;
}
```

Why these structures:

- `Dictionary<TraceId, Trace>` — find is O(1); record-into-existing-trace is O(1).
- `Queue<TraceId>` for `_insertionOrder` — FIFO eviction is "drop the oldest
  first-seen trace." A queue dequeues the eldest in O(1). We do **not** enqueue
  on every span — only the first time a `TraceId` is seen (§1.4), so the queue
  holds each trace id exactly once and its `Count` tracks the dict size.
- `Dictionary<TraceId, List<LogRecord>>` — `ForTraceAsync` is a direct lookup.
- `Queue<LogRecord>` for `_logStream` — `StreamAsync` is "recent logs across all
  services," capped FIFO. Independent cap from traces; a chatty service shouldn't
  evict traces and vice versa.

`TraceId`/`SpanId` are `readonly record struct` with value equality, so they are
correct dictionary keys with no custom comparer.

### 1.3 Options (IOptions<T> + DataAnnotations + ValidateOnStart)

```csharp
using System.ComponentModel.DataAnnotations;

public sealed class StoreOptions
{
    public const string SectionName = "Store";

    [Range(1, 100_000)]
    public int MaxTraces { get; set; } = 500;

    [Range(1, 1_000_000)]
    public int MaxLogStream { get; set; } = 5_000;
}
```

Registered with validation that fails fast at startup (before the TUI launches):

```csharp
services.AddOptions<StoreOptions>()
    .Bind(configuration.GetSection(StoreOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();
```

`ValidateOnStart` runs during `host.StartAsync()` — a bad config surfaces as a
clear exception before the screen is taken over by the TUI, which matters because
exceptions thrown after Terminal.Gui has the terminal are hard to read.

### 1.4 Write paths (sinks)

Both sinks are synchronous work wrapped in a completed `ValueTask` — the store is
in-memory, there is nothing to await. We hold the `Lock` for the brief mutation
only (no `await` inside the locked region — see §4).

```csharp
public ValueTask AcceptAsync(Span span, CancellationToken cancellationToken)
{
    ArgumentNullException.ThrowIfNull(span);
    cancellationToken.ThrowIfCancellationRequested();

    lock (_gate)
    {
        if (!_traces.TryGetValue(span.TraceId, out var trace))
        {
            trace = Trace.Empty(span.TraceId);
            _traces[span.TraceId] = trace;
            _insertionOrder.Enqueue(span.TraceId);   // enqueue only on first sight
            EvictTracesIfNeeded();
        }
        trace.Record(span);                           // O(1) upsert into the live dict
    }
    return ValueTask.CompletedTask;
}

public ValueTask AcceptAsync(LogRecord record, CancellationToken cancellationToken)
{
    ArgumentNullException.ThrowIfNull(record);
    cancellationToken.ThrowIfCancellationRequested();

    lock (_gate)
    {
        // Global capped stream.
        _logStream.Enqueue(record);
        while (_logStream.Count > _options.MaxLogStream)
        {
            _logStream.Dequeue();
        }

        // Per-trace index — only when the log carries a trace_id.
        if (record.TraceId is { } traceId)
        {
            if (!_logsByTrace.TryGetValue(traceId, out var list))
            {
                list = new List<LogRecord>();
                _logsByTrace[traceId] = list;
            }
            list.Add(record);
        }
    }
    return ValueTask.CompletedTask;
}
```

Note `lock (_gate)` works because `System.Threading.Lock` has a `lock`-statement
pattern in C# 13 / net9+ (the compiler calls `EnterScope()`); no `Monitor` and no
plain `object` sentinel. If we ever needed `TryEnter` with a timeout we'd switch to
the explicit `_gate.EnterScope()` form, but we don't here.

### 1.5 Eviction (dumb FIFO, locked)

```csharp
// Caller MUST hold _gate.
private void EvictTracesIfNeeded()
{
    while (_traces.Count > _options.MaxTraces && _insertionOrder.Count > 0)
    {
        var evicted = _insertionOrder.Dequeue();
        _traces.Remove(evicted);
        _logsByTrace.Remove(evicted);   // drop correlated logs with the trace
    }
}
```

Semantics, per the locked decision: oldest **first-seen** trace is dropped past the
cap. No LRU — a trace that's actively receiving spans is **not** kept alive by that
activity. A late span arriving for an already-evicted trace simply re-creates a
fresh one-span trace (the `TryGetValue` miss path in §1.4 re-enqueues it). That is
the documented, accepted behavior — not a bug to guard against.

`_logsByTrace` is pruned in lockstep so it cannot outgrow `_traces`. `_logStream`
is capped independently and is not touched by trace eviction (a log's correlated
trace may be gone while the log is still in the recent-stream tail — acceptable;
the stream view is "recent logs," not "logs for live traces").

### 1.6 Read paths — snapshot strategy (the core of the design)

**Every** read path returns a snapshot. The single-trace path (`FindAsync`) is the
easiest place to forget this and the one most likely to throw, because the UI
immediately calls `.Spans.ToList()` on the result.

A snapshot is a fresh `Trace` built under the lock by copying span **references**
(spans are immutable, so only the dict is new — cheap at hundreds of spans):

```csharp
// Caller MUST hold _gate. Returns a Trace whose dict no writer can mutate.
private static Trace Snapshot(Trace live)
{
    var copy = Trace.Empty(live.Id);
    foreach (var span in live.Spans)   // enumerated under the lock — safe
    {
        copy.Record(span);             // re-inserts the same immutable Span ref
    }
    return copy;
}
```

> Implementer note: this relies only on `Trace.Empty` + `Trace.Record` + `Trace.Spans`,
> which exist today. If/when `Trace.Assemble()` lands, the snapshot stays the
> store's job and assembly stays the **domain/view's** job — do not move assembly
> into `Snapshot`. The store hands out an unassembled flat snapshot; the caller
> assembles. That preserves the locked "store does no assembly" boundary.

#### `FindAsync` — snapshot, not the live trace

```csharp
public ValueTask<Trace?> FindAsync(TraceId id, CancellationToken cancellationToken)
{
    cancellationToken.ThrowIfCancellationRequested();
    lock (_gate)
    {
        return _traces.TryGetValue(id, out var live)
            ? ValueTask.FromResult<Trace?>(Snapshot(live))
            : ValueTask.FromResult<Trace?>(null);
    }
}
```

This is the behavioral difference from `FixtureTraceQueries.FindAsync`, which
returns the stored instance directly. The fixture can get away with it because
nothing mutates fixtures; the live store cannot.

#### The async-enumerable pattern that never yields under the lock

The rule (locked): **materialize the snapshot list under the lock, release the
lock, then yield.** `yield return` while holding a lock would suspend the iterator
with the lock held until the consumer pulls the next item — a deadlock waiting to
happen against the UI thread. We split each streaming query into a synchronous
`private` snapshot method (takes the lock, returns a `List<>`) and a thin `async`
iterator (no lock, just yields).

```csharp
public IAsyncEnumerable<Trace> RecentAsync(int limit, CancellationToken cancellationToken)
    => Drain(SnapshotRecent(limit), cancellationToken);

private List<Trace> SnapshotRecent(int limit)
{
    lock (_gate)
    {
        // Most-recent first = reverse first-seen order. Snapshot each.
        var result = new List<Trace>(Math.Min(limit, _insertionOrder.Count));
        foreach (var id in _insertionOrder.Reverse())   // newest first
        {
            if (result.Count >= limit) break;
            if (_traces.TryGetValue(id, out var live))
            {
                result.Add(Snapshot(live));
            }
        }
        return result;
    }
}

public IAsyncEnumerable<LogRecord> StreamAsync(CancellationToken cancellationToken)
    => Drain(SnapshotLogStream(), cancellationToken);

private List<LogRecord> SnapshotLogStream()
{
    lock (_gate)
    {
        return _logStream.ToList();   // LogRecord is immutable; copy the list only
    }
}

public IAsyncEnumerable<LogRecord> ForTraceAsync(TraceId id, CancellationToken cancellationToken)
    => Drain(SnapshotLogsForTrace(id), cancellationToken);

private List<LogRecord> SnapshotLogsForTrace(TraceId id)
{
    lock (_gate)
    {
        return _logsByTrace.TryGetValue(id, out var list)
            ? new List<LogRecord>(list)   // copy under lock
            : new List<LogRecord>();
    }
}

// Single shared iterator. No lock here. Honors cancellation between items.
private static async IAsyncEnumerable<T> Drain<T>(
    List<T> snapshot,
    [EnumeratorCancellation] CancellationToken cancellationToken)
{
    foreach (var item in snapshot)
    {
        cancellationToken.ThrowIfCancellationRequested();
        yield return item;
    }
    await Task.CompletedTask;   // keeps the method a valid async iterator
}
```

`Queue<T>.Reverse()` (LINQ) allocates an enumerator over the queue; it is executed
**inside** the lock so it sees a consistent state, and its result is fully
materialized into `result` before the lock releases. We never hand a lazy LINQ
sequence over the live collections out past the lock.

### 1.7 `StreamAsync` semantics — resolved ambiguity (not silently)

The headline feature is "live," but the current `MainWindow` loads **once**
(`LoadAsync`) with no refresh loop, and the drop-in target (`FixtureTraceQueries`)
yields-then-completes. We match the **drop-in contract**: `StreamAsync` snapshots
the capped buffer, yields it, and completes. It is *not* an open live tail.

Live tailing (a subscription / `Channel<T>` fan-out from the sinks to the UI, or a
UI refresh timer that re-queries) is a **separate future piece** and is explicitly
out of scope here. Building it now would deliver updates the UI cannot consume and
would break the "one-line DI change" drop-in property. When live tail is built, it
is an *additive* port (e.g. `ILogSubscription`), not a change to `StreamAsync`.

### 1.8 DI registration + the one-line UI swap

Infrastructure registers the store once and exposes it under all four port
interfaces (single shared singleton — the same instance must serve writes and
reads):

```csharp
// InfrastructureServiceCollectionExtensions.AddInfrastructure
services.AddOptions<StoreOptions>()
    .Bind(configuration.GetSection(StoreOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();

services.AddSingleton<InMemoryTelemetryStore>();
services.AddSingleton<ITraceSink>(sp => sp.GetRequiredService<InMemoryTelemetryStore>());
services.AddSingleton<ILogSink>(sp => sp.GetRequiredService<InMemoryTelemetryStore>());
services.AddSingleton<ITraceQueries>(sp => sp.GetRequiredService<InMemoryTelemetryStore>());
services.AddSingleton<ILogQueries>(sp => sp.GetRequiredService<InMemoryTelemetryStore>());
```

The UI swap in `TuiServiceCollectionExtensions.AddTui` is then deleting the three
fixture lines (`FixtureTraceQueries` + its two interface registrations). Because
Infrastructure already registers `ITraceQueries`/`ILogQueries`, `TuiRunner`'s
constructor resolves the real store with no code change. Keep `FixtureTraceQueries`
in the TUI project for tests / `--demo`; just stop registering it by default.

> Registration ordering: `AddInfrastructure` runs before `AddTui` in `Program.cs`.
> If both register `ITraceQueries`, the last wins for a single resolve. Cleanest is
> for `AddTui` to register **no** query implementation once the store exists; that
> removes the ambiguity rather than relying on ordering.

### 1.9 Drop-in equivalence checklist (acceptance)

- [ ] Implements `ITraceQueries` + `ILogQueries` with identical signatures to the fixture.
- [ ] `FindAsync` returns a **snapshot**, never the live `Trace`.
- [ ] `RecentAsync` returns newest-first, `limit`-capped, each a snapshot.
- [ ] `ForTraceAsync` returns `[]` (yield break equivalent) for unknown trace, like the fixture.
- [ ] No `yield return` executes while `_gate` is held (verified by structure: iterators take no lock).
- [ ] Swapping DI registration runs the existing TUI unchanged against an empty store (shows empty panes, no exception).

---

## 2. OTLP receiver (design only — step 3)

Goal: host gRPC (OTLP/gRPC on `:4317`, the three Export services — trace, metrics,
logs) **and** OTLP/HTTP (`:4318`) inside the same `dotnet tool` process, alongside
the Terminal.Gui event loop, and survive malformed/partial input without crashing.

### 2.1 Hosting model — `WebApplication.CreateSlimBuilder` (the pivot)

`Host.CreateApplicationBuilder` returns `HostApplicationBuilder`, which has **no**
web-host integration (no `ConfigureWebHostDefaults`, no endpoint routing). Grpc.AspNetCore
is endpoint-based: `MapGrpcService<T>()` requires the ASP.NET routing + endpoint
middleware pipeline. Hand-rolling that under a custom `IHostedService` means
reconstructing `KestrelServer` + `IHttpApplication` + routing middleware + the gRPC
`ServiceMethodProvider` — i.e. reimplementing `GenericWebHostService`. That is a
large, fragile surface and is rejected.

Use **`WebApplication.CreateSlimBuilder(args)`**:

- It **is** a generic host: runs `IHostedService`s, exposes `IServiceCollection`
  and `IConfiguration`, supports `StartAsync`/`StopAsync`.
- `SlimBuilder` (vs `CreateBuilder`) drops MVC/Razor/HTTPS-redirect/etc. — the
  right minimal surface for a localhost tool.
- Program.cs keeps **exactly** today's shape — `StartAsync` → run TUI on the main
  thread → `StopAsync` in `finally`. The TUI does not care what host type produced
  `TuiRunner`.

```csharp
var builder = WebApplication.CreateSlimBuilder(args);

builder.Services
    .AddApplication()
    .AddInfrastructure(builder.Configuration)   // registers the store (all 4 ports)
    .AddOtlpReceiver(builder.Configuration)      // gRPC services + ACL + options (new)
    .AddTui();

builder.Services.AddGrpc();
builder.WebHost.ConfigureKestrel(/* loopback endpoints — §2.4 */);

var app = builder.Build();

app.MapGrpcService<TraceExportService>();
app.MapGrpcService<MetricsExportService>();
app.MapGrpcService<LogsExportService>();
app.MapOtlpHttp();   // OTLP/HTTP minimal-API endpoints — §2.5

await app.StartAsync();          // Kestrel binds here; bind failure throws here (§2.6)
try
{
    app.Services.GetRequiredService<TuiRunner>().Run();   // blocks on the main thread
}
finally
{
    await app.StopAsync();       // graceful Kestrel drain
}
```

**Consequence on the composition root:** `Sentinel.CLI` (the tool exe) gains the
`Microsoft.AspNetCore.App` framework reference (`<FrameworkReference Include="Microsoft.AspNetCore.App" />`).
That framework reference, plus `Grpc.AspNetCore`, lives in whichever project hosts
the gRPC service classes — see §2.3 for where that is.

### 2.2 Threading model

- **Kestrel** owns its own background threads (thread-pool driven). It is started
  by `StartAsync` *before* the TUI loop begins, and accepts connections on those
  threads independently of the main thread.
- **Terminal.Gui** owns the **foreground / main thread** via the blocking
  `app.Run(window)` inside `TuiRunner.Run()`. This is the only thread allowed to
  touch UI widgets.
- **Bridge:** receiver threads → store is a direct call. The store's `Lock` is
  already the synchronization boundary; a gRPC handler maps protobuf → domain and
  calls `_traceSink.AcceptAsync(span, ct)` directly. At hundreds of spans this is
  the simplest correct design.
- **Store → UI** is *not* push today. The UI pulls via the query ports on its own
  thread and marshals widget mutations through `IApplication.Invoke(...)`
  (already in `MainWindow`). The store never calls into the UI.

#### Channel<T> — named as the heavier option, with its trigger

A `Channel<T>` between receiver and store is **not** in this design. Add it only
when one of these is true:

- **Backpressure / decoupling** is needed — e.g. a burst export must not block the
  gRPC handler on the store lock, or ingest must be smoothed. Then: handler writes
  to a bounded `Channel<Span>` (`BoundedChannelFullMode.DropWrite` or `Wait`), a
  single `BackgroundService` reader drains it into the store. Bounded + drop-oldest
  keeps memory flat under flood; the dropped-count is a metric.
- **Push-based live UI** is built (§1.7) — then the channel doubles as the fan-out
  to a UI refresh pump.

Until then, the direct call is correct and cheaper. Don't cargo-cult the channel in.

### 2.3 Anti-corruption layer — where protobuf→domain mapping lives

One mapper, invoked by **both** transports. The OTLP `.proto` messages are
identical whether they arrive over gRPC (`:4317`) or HTTP (`:4318`) — same
generated C# types — so there is exactly one mapping surface, not two.

Projects:

- The gRPC service classes (`TraceExportService` : `TraceService.TraceServiceBase`,
  etc.), the OTLP/HTTP endpoints, the generated proto types, and the ACL mapper go
  in **a dedicated receiver adapter** — either `Sentinel.CLI.Infrastructure` or a
  new `Sentinel.CLI.Receiver` project. Recommendation: a **separate
  `Sentinel.CLI.Receiver` project** that references Application + Domain +
  `Grpc.AspNetCore` and takes the `Microsoft.AspNetCore.App` framework reference.
  Rationale: it keeps `InMemoryTelemetryStore` (Infrastructure) testable **without**
  pulling ASP.NET into the store's test project. The store and the receiver have
  different dependency footprints and different test needs; separating them earns
  its keep here.
- The store stays in `Sentinel.CLI.Infrastructure` with no ASP.NET dependency.

The mapper is a pure, static, total function over the proto graph:

```
ExportTraceServiceRequest
  → resource_spans[]            (carries Resource{ service.name, ... })
    → scope_spans[]
      → spans[]                 → domain Span  (via Span.Create)
```

`service.name` is read from `Resource.attributes` and threaded down to each
`Span`/`LogRecord` (`ServiceName.From`). OTLP ids are raw bytes; convert to the
domain's lowercase-hex via a `Convert.ToHexStringLower(bytes)` (.NET 9+) before
`TraceId.Parse` / `SpanId.Parse`. Timestamps are unix-nanos → `DateTimeOffset`
(`DateTimeOffset.UnixEpoch.AddTicks(nanos / 100)`; OTLP nanos, ticks are 100ns).
OTLP `AnyValue` → domain `AttributeValue` is a switch over the proto value oneof
(`string→Text`, `int→Integer`, `double→Number`, `bool→Flag`, `array→TextList`;
unsupported kinds like kvlist/bytes → skip or stringify, documented choice).

### 2.4 Endpoints / loopback-only binding (security)

"Local-first": exporters point at `localhost`, so bind **loopback only**, never
`0.0.0.0`. A telemetry receiver on all interfaces is an unstated security hole.

- `127.0.0.1:4317` **and** `[::1]:4317` — **HTTP/2** (h2c, plaintext is fine on
  loopback) for OTLP/gRPC.
- `127.0.0.1:4318` **and** `[::1]:4318` — **HTTP/1.1** for OTLP/HTTP.

Configure via `ConfigureKestrel` with `ListenLocalhost(port, o => o.Protocols = ...)`
(`ListenLocalhost` binds both IPv4 and IPv6 loopback). Ports are options-bound
(`ReceiverOptions { GrpcPort=4317, HttpPort=4318 }`, DataAnnotations `[Range(1,65535)]`,
`ValidateOnStart`) so a user can relocate them if 4317/4318 clash.

### 2.5 OTLP/HTTP on :4318

OTLP/HTTP carries the **same** protobuf bodies (default `Content-Type:
application/x-protobuf`) on `POST /v1/traces`, `/v1/metrics`, `/v1/logs`. Implement
as minimal-API endpoints that:

1. Read the body, `ExportTraceServiceRequest.Parser.ParseFrom(stream)`.
2. Run the **same ACL mapper** as gRPC (§2.3).
3. Return `200` with an empty (or partial-success) `ExportTraceServiceResponse`.

JSON OTLP (`application/json`) is a documented non-goal for v1 (return `415` if a
JSON content-type arrives) unless trivially free via the proto JSON formatter —
implementer's call, but don't block on it.

### 2.6 Resilience — survive bad/partial input

This is **inbound** resilience (tolerate garbage), not outbound retry. Mechanisms,
concretely:

1. **Per-item try/catch around the throwing domain factories.** A single malformed
   span in a batch (bad id, empty name, `end < start`, zero trace id) must be
   caught, counted (dropped-spans counter + a single structured warn log — not
   per-span log spam), and skipped. It must **not** propagate out of the Export
   RPC, or the client sees an error and the tool looks broken when the fault is the
   client's data.

   ```csharp
   foreach (var protoSpan in scope.Spans)
   {
       Span domainSpan;
       try { domainSpan = _mapper.ToDomain(protoSpan, service); }
       catch (Exception ex) when (ex is FormatException or ArgumentException)
       {
           _metrics.DroppedSpans.Add(1);
           continue;   // skip the bad span, keep the batch
       }
       await _traceSink.AcceptAsync(domainSpan, ct);
   }
   ```

   Catch is **narrow** (`FormatException`/`ArgumentException` — exactly what the
   factories throw). `OperationCanceledException` must propagate (shutdown). A
   truly unexpected exception type is allowed to bubble — don't blanket-`catch
   (Exception)` and hide real bugs.

2. **Protobuf decode failure → 4xx, never 5xx, never crash.** For OTLP/HTTP, a
   `ParseFrom` that throws `InvalidProtocolBufferException` → `400 Bad Request`.
   For gRPC, a decode failure is handled by the framework as a gRPC status; a
   mapping failure inside the handler returns a normal (possibly partial-success)
   response — the connection is not torn down.

3. **Partial success is first-class.** OTLP's `ExportTraceServiceResponse` has a
   `partial_success { rejected_spans, error_message }` field. Populate
   `rejected_spans` with the dropped count so a well-behaved exporter learns it
   sent garbage, while the good spans are still ingested.

4. **No unbounded growth from input.** The store cap (§1.5) is the backstop; the
   receiver does not need its own dedup. Metrics/logs Export services may be
   accepted-and-dropped in v1 (the store has no metrics model yet) — return `200`
   so exporters don't error, document that metrics are not yet rendered.

### 2.7 Startup / shutdown via the host

- **Startup:** `AddOtlpReceiver` registers the gRPC services + ACL + options;
  `app.MapGrpcService<...>()` + `MapOtlpHttp()` wire endpoints. Kestrel binds
  during `app.StartAsync()`.
- **Port-in-use:** a bind clash surfaces during `StartAsync()` as
  `IOException`/`SocketException` (`SocketError.AddressAlreadyInUse`) — **before**
  the TUI launches. Catch it in `Program.cs`, print a plain message
  (`"Port 4317 in use — is another Sentinel already running? (set Receiver:GrpcPort to relocate)"`),
  and exit non-zero. **Never launch a TUI over a dead receiver** — the user would
  stare at empty panes with no idea why.

  ```csharp
  try { await app.StartAsync(); }
  catch (IOException ex) when (ex.InnerException is SocketException
                               { SocketErrorCode: SocketError.AddressAlreadyInUse })
  {
      Console.Error.WriteLine("Port in use — another Sentinel running? ...");
      return 1;
  }
  ```

- **Shutdown:** the TUI loop exits (user presses `q`/`Ctrl-C`-as-keystroke), control
  returns from `Run()`, `finally` calls `app.StopAsync()` which drains Kestrel and
  stops hosted services gracefully (default 30s; tune `HostOptions.ShutdownTimeout`
  if needed). See §3 for the SIGTERM asymmetry.

---

## 3. Cross-platform (Windows / Linux / macOS)

A TUI + Kestrel `dotnet tool` has real per-OS differences. The ones that matter:

### 3.1 Console driver (Terminal.Gui)

`Application.Create()/Init()` auto-selects a driver. On Windows it can use the
Win32 console API; on Linux/macOS it uses an ANSI/`curses`-style driver against the
terminal. Implications:

- Don't assume a specific driver. Let Terminal.Gui pick. If a CI/redirected/non-tty
  environment is detected (`Console.IsOutputRedirected`), the TUI must refuse to
  start with a clear message rather than throwing deep in the driver — a `dotnet
  tool` will sometimes be invoked in a pipe.
- Color/Unicode box-drawing degrades on minimal terminals (Windows legacy console,
  `TERM=dumb`). Not a correctness issue; note it.

### 3.2 Signal handling on shutdown — the asymmetry

This is the real cross-platform shutdown trap:

- The TUI captures **Ctrl-C as a keystroke** (`OnAppKey` in `TuiRunner`) and stops
  via `app.RequestStop(window)`. That covers interactive Ctrl-C on all OSes.
- **SIGTERM** (`docker stop`, `kill`, systemd, CI teardown) is **invisible** to the
  TUI loop — no keystroke arrives. Without handling, the process is killed mid-loop
  and `StopAsync()`/Kestrel drain never runs, and worse, the terminal may be left
  in a raw/garbled state.

Fix: register `PosixSignalRegistration` for `SIGTERM` (and `SIGINT` as a backstop)
that calls `app.RequestStop()` to break the TUI loop so the normal `finally →
StopAsync` path runs and Terminal.Gui restores the terminal:

```csharp
using var sigterm = PosixSignalRegistration.Create(PosixSignal.SIGTERM, ctx =>
{
    ctx.Cancel = true;          // we handle it; don't let the runtime hard-kill
    tguiApp.RequestStop();      // break app.Run(), unwind into finally/StopAsync
});
```

`PosixSignalRegistration` is cross-platform: on Windows it maps to the console
control handler (Ctrl-C / Ctrl-Break / close), on Unix to the real signals.

**Do not** layer the host's console lifetime (`RunConsoleAsync` /
`UseConsoleLifetime`) under the TUI. Two things would fight over Ctrl-C — the host
lifetime and Terminal.Gui's keyboard handler. The current manual
`StartAsync`/`Run`/`StopAsync` shape (no console lifetime) is correct; keep it.

### 3.3 File paths / config

- The store and receiver are in-memory — no data files. The only paths are config
  (`appsettings.json` if used) and any future export-to-disk. Use `Path.Combine` /
  `Environment.GetFolderPath(SpecialFolder.LocalApplicationData)` /
  `Environment.GetFolderPath(SpecialFolder.UserProfile)`; never hard-code `\` or `/`.
- `dotnet tool` install location differs per OS but is resolved by the tool host;
  no action needed.

### 3.4 Globalization / ICU

`dotnet tool` runs on the user's SDK runtime, so ICU is normally present (unlike a
trimmed Alpine container). The UI already uses `CultureInfo.InvariantCulture` for
attribute rendering — keep all wire/format parsing invariant. Don't enable
`InvariantGlobalization` blindly (it would change how any user-facing culture text
renders); not needed for a dev tool on a full runtime.

### 3.5 Line endings / wire formats

`MainWindow` uses `Environment.NewLine` for the on-screen detail text — correct
(platform-native display). The OTLP wire is protobuf (binary) — no line-ending
concern. Keep `\n` for any future protocol/text output, `Environment.NewLine` only
for terminal display.

---

## 4. Threading-correctness checklist

The three actors and the exact races between them, and how the design closes each:

| # | Race | Closed by |
|---|------|-----------|
| 1 | Receiver thread `Record`s into a `Trace` while the UI thread enumerates `trace.Spans` → `InvalidOperationException` "collection modified". | **Snapshot on every read** (§1.6). The UI only ever holds a fresh `Trace` whose dict no writer can touch. The live `Trace` never leaves the store. |
| 2 | `FindAsync` returns the live trace (easy to forget — single-trace path). | `FindAsync` calls `Snapshot` like every other read (§1.6). Covered by the acceptance checklist. |
| 3 | Iterator `yield return`s while holding `_gate` → suspends with the lock held → deadlock vs the UI thread / another receiver thread. | **Materialize under lock, yield outside** (§1.6). Streaming queries are split into a sync snapshot method (locks) + a lockless `Drain` iterator. No `yield` ever sees the lock. |
| 4 | Two receiver threads (concurrent gRPC + HTTP, or concurrent gRPC calls) `Record` into the same/different traces simultaneously → dict corruption. | All writes take `_gate` (§1.4). `System.Threading.Lock` serializes them. |
| 5 | Eviction removes a trace from `_traces` while a reader holds a reference. | Reader holds a **snapshot**, not the live trace; eviction of the live trace is invisible to an in-flight snapshot. The snapshot is built and the lock released before any await/yield. |
| 6 | UI widget mutated from a receiver/background thread (Terminal.Gui is not thread-safe). | The store never calls the UI. The UI marshals **all** widget mutations through `IApplication.Invoke(...)` (already in `MainWindow.LoadAsync` / `SelectTraceAsync`). Receiver threads only touch the store, never widgets. |
| 7 | `await` inside the locked region (would hold the lock across a continuation, possibly resumed on another thread — and `Lock` is not held across `await`). | No `await` inside any `lock (_gate)` block (§1.4, §1.6). The store's locked work is pure in-memory CPU work; the `ValueTask` is already completed. |
| 8 | SIGTERM kills the process mid-loop; terminal left raw, Kestrel not drained. | `PosixSignalRegistration` → `app.RequestStop()` → normal `finally`/`StopAsync` unwind (§3.2). |

**Invariant the implementer must preserve:** the live `Trace` (and the live
`_logsByTrace` lists, `_logStream`) never escape a `lock (_gate)` region.
Everything that leaves the store is either an immutable value (`Span`, `LogRecord`,
value objects) or a freshly-built snapshot collection. Hold that line and races
1–7 are structurally impossible.

---

## 5. Out of scope (explicit)

- Resource-fingerprint trace identity and the pending-children pool — **deferred**
  (locked). Trace identity is the raw `trace_id`.
- Incremental assembly — **locked** to assemble-on-view; the store stores flat.
- Live tailing / push to UI — §1.7; additive future port, not a change to existing signatures.
- Metrics rendering — receiver accepts and drops metrics in v1 (returns 200).
- JSON OTLP over `:4318` — protobuf only in v1 (415 on JSON), unless free via proto JSON.
- Outbound resilience (retry/circuit-breaker) — not applicable; Sentinel makes no outbound calls.
