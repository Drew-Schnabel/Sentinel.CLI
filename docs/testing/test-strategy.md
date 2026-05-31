# Sentinel.CLI — Test Strategy

_Scope: Domain assembly, InMemoryTelemetryStore, OTLP receiver (future). Date: 2026-05-30._

---

## 1. What the suite must verify

Three properties are load-bearing. A regression in any of them ships a broken tool.

1. **Cross-service trace assembly** — `Trace.Assemble()` produces a deterministic, ordered `SpanNode` tree from any mixture of span arrival orders, service boundaries, missing roots, orphan spans, and duplicate span_ids.
2. **Snapshot isolation in the store** — `InMemoryTelemetryStore` returns copies; mutating the store after a snapshot does not corrupt the snapshot. Without this, the UI thread and the receiver thread race on a live `Trace`.
3. **Receiver resilience** — the OTLP gRPC and HTTP endpoints never crash on malformed, partial, or empty export requests. Every accepted span reaches the store; every rejected export returns the correct gRPC/HTTP status without surfacing an unhandled exception.

---

## 2. Suite shape — Diamond

This is **not** a pyramid system. The domain has thin pure logic (the assembly algorithm and value-object validation); the rest is orchestration between a concurrent store, a network receiver, and a query interface. The value is in the integration seams, not in isolated units.

```
TUI tests:          near zero (deliberate — see §6)
Integration tests:  majority (store + receiver)
Unit tests:         domain assembly + value objects + store-logic fakes
```

**Why diamond, not pyramid.**
The assembly algorithm is rich enough to carry a full unit suite. The store has no interesting pure logic — its value is thread-safety and snapshot isolation, which only reveal themselves under concurrent access against the real implementation. The receiver's value is its resilience under real protobuf bytes on a real transport. Neither of those is meaningful in isolation against a fake.

**Why no Testcontainers.**
There are no external stateful dependencies: no relational database, no message broker, no Redis. Everything is in-memory. Testcontainers earns nothing here and would add Docker-on-CI overhead for zero benefit. The receiver integration tests use an **in-process Kestrel `WebApplicationFactory`**, which spins up in milliseconds and tears down cleanly.

---

## 3. Root predicate — critical clarification for implementers

`Trace.FindRoot()` (line 28–43 of `Trace.cs`) uses this predicate as a root candidate:

```csharp
span.ParentSpanId is null || !_spans.ContainsKey(span.ParentSpanId.Value)
```

A span is a root candidate if its `ParentSpanId` is **absent from the trace's span dictionary**. This means:

- A span with no parent (`ParentSpanId is null`) is a root — the strict case.
- A span whose parent has not yet been recorded (or will never arrive) is **also** a root — the orphan case.

`FindRoot()` returns `null` if more than one such candidate exists (the multiple-roots / all-orphans case).

`Trace.Assemble()` (to be implemented) will use the **same predicate** but collect all candidates instead of short-circuiting. The result is a forest, ordered by `StartTime` ascending. A trace with all orphans produces a forest of N roots. A trace with a single strict root produces a tree of depth ≥ 1.

**This matters for the matrix:** orphans are not dropped — they become top-level nodes in the forest. Every matrix cell in §4 encodes this contract.

Also note: `Trace.Record()` stores spans by `SpanId` in a `Dictionary<SpanId, Span>`. Recording a span with a previously-seen `SpanId` **overwrites** the prior entry. This is last-write-wins by construction of the dictionary.

---

## 4. Cross-service assembly test matrix

**Target:** `Trace.Assemble()` on `Sentinel.CLI.Domain.Tests/Telemetry/Spans/TraceAssemblyTests.cs`

Notation: spans listed as `id(parentId)` where `—` means no parent. `StartTime` offset in ms from a fixed `t0 = new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero)`. Tree notation: `root → [child1 → [grandchild], child2]`.

**Naming convention:** matches `TraceIdTests.cs` — `Method_condition_expected_outcome` in lowercase words.

| # | Test name | Input spans (id, parent, service, start ms) | Expected tree shape | Notes |
|---|---|---|---|---|
| 1 | `Assemble_single_root_no_children_returns_single_node_tree` | `A(—, svc-a, 0)` | `[A]` | Baseline |
| 2 | `Assemble_single_root_with_children_returns_correct_depth` | `A(—, svc-a, 0)`, `B(A, svc-a, 10)`, `C(A, svc-a, 20)` | `[A → [B, C]]` | Children ordered by StartTime |
| 3 | `Assemble_deep_chain_returns_correct_nesting` | `A(—, 0)`, `B(A, 10)`, `C(B, 20)`, `D(C, 30)` | `[A → [B → [C → [D]]]]` | Depth 4 |
| 4 | `Assemble_cross_service_parent_child_ignores_service_boundary` | `A(—, svc-a, 0)`, `B(A, svc-b, 10)`, `C(B, svc-a, 20)` | `[A → [B → [C]]]` | Proves assembly keys on span_id only, not service |
| 5 | `Assemble_child_arrives_before_parent_still_assembles_correctly` | Record in order: `B(A, 10)`, `A(—, 0)` | `[A → [B]]` | Dict keying makes arrival order irrelevant |
| 6 | `Assemble_multiple_roots_returns_forest_ordered_by_start_time` | `A(—, 0)`, `B(—, 5)` | `[A, B]` (forest) | Two strict roots |
| 7 | `Assemble_all_orphans_each_becomes_root` | `A(X, 0)`, `B(Y, 5)` where X and Y are absent | `[A, B]` (forest) | X and Y never recorded; both spans become roots |
| 8 | `Assemble_orphan_parent_never_arrives_orphan_is_root` | `A(—, 0)`, `B(A, 10)`, `C(Z, 5)` where Z absent | `[A(0) → [B], C(5)]` | C's parent never arrives; C promoted to root; roots sorted ascending: A(0) then C(5) |
| 9 | `Assemble_empty_trace_returns_empty_forest` | No spans | `[]` | `Trace.Empty()` with no `Record()` calls |
| 10 | `Assemble_duplicate_span_id_last_write_wins` | Record `A_v1(—, name="first", 0)`, then `A_v2(—, name="second", 0)` (same SpanId) | `[A_v2]` — node carries v2's name | Dict overwrites on same key |
| 11 | `Assemble_cross_service_chain_a_b_a_assembles_linear_depth_3` | `A(—, svc-a, 0)`, `B(A, svc-b, 10)`, `C(B, svc-a, 20)` — services cycle a→b→a | `[A → [B → [C]]]` | Same as case 4 but explicitly names the cycle |
| 12 | `Assemble_sibling_order_by_start_time_ascending` | `A(—, 0)`, `B(A, 30)`, `C(A, 10)`, `D(A, 20)` | `[A → [C(10), D(20), B(30)]]` | Children sorted by StartTime within each node |
| 13 | `Assemble_multiple_roots_with_children_correct_forest` | `A(—, 0)`, `B(A, 5)`, `C(—, 10)`, `D(C, 15)` | `[A → [B], C → [D]]` | Forest with each root having a child |
| 14 | `Assemble_single_orphan_no_other_spans_orphan_is_only_root` | `A(Z, 0)` where Z absent | `[A]` | Single-span trace where parent reference is dangling |

**Ordering rule to encode:** roots are sorted by `StartTime` ascending; children at each node are sorted by `StartTime` ascending.

**For case 8:** `C` has `StartTime = 5ms` and `A` has `StartTime = 0ms`. After promotion, roots sort ascending: `[A(0), C(5)]`. The expected column reflects this. Verify fixture times are distinct when writing the test.

---

## 5. `FindRoot()` test matrix

**Target:** `Sentinel.CLI.Domain.Tests/Telemetry/Spans/TraceTests.cs`  
These are already partially implied by the assembly cases but `FindRoot()` needs its own coverage since it is a separate method with its own short-circuit.

| # | Test name | Input | Expected |
|---|---|---|---|
| 1 | `FindRoot_single_root_returns_it` | `A(—)` | `A` |
| 2 | `FindRoot_multiple_strict_roots_returns_null` | `A(—)`, `B(—)` | `null` |
| 3 | `FindRoot_root_with_children_returns_root` | `A(—)`, `B(A)`, `C(A)` | `A` |
| 4 | `FindRoot_all_orphans_returns_null` | `A(X)`, `B(Y)` (parents absent) | `null` |
| 5 | `FindRoot_empty_trace_returns_null` | No spans | `null` |
| 6 | `FindRoot_orphan_promoted_to_sole_root_returns_orphan` | `A(X)` (parent absent), no other spans | `A` |

---

## 6. Store test matrix

**Target:** `Sentinel.CLI.Infrastructure.Tests/Telemetry/InMemoryTelemetryStoreTests.cs`

The `InMemoryTelemetryStore` implements `ITraceSink`, `ILogSink`, `ITraceQueries`, and `ILogQueries`. It does not exist yet in source; these tests drive its specification.

### 6.1 Span acceptance and retrieval

| # | Test name | What it verifies |
|---|---|---|
| 1 | `AcceptAsync_single_span_trace_appears_in_find_async` | After `AcceptAsync(span)`, `FindAsync(traceId)` returns a trace containing that span |
| 2 | `AcceptAsync_multiple_spans_same_trace_single_trace_with_all_spans` | Two spans with same `TraceId` land in one `Trace`; `FindAsync` returns both |
| 3 | `AcceptAsync_different_traces_isolated_correctly` | Spans from trace A do not appear in `FindAsync` for trace B |
| 4 | `FindAsync_unknown_trace_id_returns_null` | `FindAsync` on a trace id never accepted returns `null` |

### 6.2 `RecentAsync` ordering

| # | Test name | What it verifies |
|---|---|---|
| 5 | `RecentAsync_multiple_traces_ordered_by_most_recent_span_start_time_descending` | Traces returned newest-first (by the latest `StartTime` across spans in each trace) |
| 6 | `RecentAsync_limit_respected_returns_at_most_limit` | With 10 traces accepted, `RecentAsync(3)` returns exactly 3 |
| 7 | `RecentAsync_empty_store_returns_empty` | No spans accepted → empty sequence |

> **Note on ordering rule:** The brief says "FIFO ring-buffer" and "recent" — the natural interpretation is insertion order descending (newest accepted last, returned first). Confirm with the implementer whether "recent" means insertion order or latest-span-start-time. The test name above encodes an assumption; adjust when the rule is confirmed. The test itself must use fixed `DateTimeOffset` values so it does not depend on wall-clock ordering.

### 6.3 Snapshot isolation — highest-priority test

| # | Test name | What it verifies |
|---|---|---|
| 8 | `FindAsync_snapshot_isolation_mutating_store_later_does_not_alter_returned_trace` | After `FindAsync` returns `traceA`, accepting another span into the same trace does not change `traceA.Spans.Count` |
| 9 | `RecentAsync_snapshot_isolation_mutating_store_during_enumeration_does_not_throw` | Accepting spans into the store while a caller is mid-enumeration of `RecentAsync` does not throw `InvalidOperationException` |

Test 8 is the determinism keystone. Its AAA sketch:

```csharp
[Fact]
public async Task FindAsync_snapshot_isolation_mutating_store_later_does_not_alter_returned_trace()
{
    // Arrange
    var store = new InMemoryTelemetryStore();
    var span1 = MakeSpan(traceId: T1, spanId: S1, parentSpanId: null, startMs: 0);
    await store.AcceptAsync(span1, CancellationToken.None);

    // Act
    var snapshot = await store.FindAsync(span1.TraceId, CancellationToken.None);
    var countBefore = snapshot!.Spans.Count;

    var span2 = MakeSpan(traceId: T1, spanId: S2, parentSpanId: S1, startMs: 10);
    await store.AcceptAsync(span2, CancellationToken.None);

    // Assert
    snapshot.Spans.Count.Should().Be(countBefore);
}
```

`MakeSpan` is a private helper that wraps `Span.Create(...)` with fixed `DateTimeOffset` values — no `DateTimeOffset.UtcNow`.

### 6.4 FIFO ring-buffer eviction

The brief states a cap of approximately 500 traces. The cap value must be read from a constant in the production implementation (e.g., `InMemoryTelemetryStore.TraceCapacity`) and referenced symbolically in the test — do not hardcode 500.

| # | Test name | What it verifies |
|---|---|---|
| 10 | `AcceptAsync_at_cap_oldest_trace_evicted` | After accepting `Cap + 1` distinct traces, the first trace is no longer returned by `FindAsync` or `RecentAsync` |
| 11 | `AcceptAsync_at_cap_newest_trace_present` | The `Cap + 1`th trace is present |
| 12 | `AcceptAsync_below_cap_no_eviction` | Accepting `Cap` traces evicts nothing; all are findable |

AAA sketch for case 10:

```csharp
[Fact]
public async Task AcceptAsync_at_cap_oldest_trace_evicted()
{
    // Arrange
    var store = new InMemoryTelemetryStore();
    // MakeTraceId(i) must not produce all-zeros (TraceId.Parse rejects them).
    // Start enumeration at 1, or format as e.g. i.ToString("x31") + "1" to guarantee non-zero.
    var firstTraceId = MakeTraceId(1);
    await store.AcceptAsync(MakeSpan(firstTraceId, MakeSpanId(1), startMs: 0), CancellationToken.None);

    for (var i = 2; i <= InMemoryTelemetryStore.TraceCapacity; i++)
    {
        var tid = MakeTraceId(i);
        await store.AcceptAsync(MakeSpan(tid, MakeSpanId(i), startMs: i * 10), CancellationToken.None);
    }

    // Act — push one past the cap
    var lastTraceId = MakeTraceId(InMemoryTelemetryStore.TraceCapacity + 1);
    await store.AcceptAsync(
        MakeSpan(lastTraceId, MakeSpanId(InMemoryTelemetryStore.TraceCapacity + 1), startMs: 0),
        CancellationToken.None);

    // Assert
    var evicted = await store.FindAsync(firstTraceId, CancellationToken.None);
    evicted.Should().BeNull();
}
```

### 6.5 Log acceptance and correlation

`ILogSink.AcceptAsync(LogRecord)` and `ILogQueries.StreamAsync` / `ForTraceAsync`.

Note: `ILogQueries.ForTraceAsync` filters by `TraceId` only; span-level log correlation is the TUI's responsibility (see `MainWindow.RenderDetails`). The store tests below reflect this — no span_id filtering at the store boundary.

| # | Test name | What it verifies |
|---|---|---|
| 13 | `AcceptAsync_log_appears_in_stream_async` | After `AcceptAsync(log)`, `StreamAsync` yields the log |
| 14 | `AcceptAsync_log_with_trace_id_appears_in_for_trace_async` | A log with `TraceId = T1` appears in `ForTraceAsync(T1)` |
| 15 | `ForTraceAsync_log_from_other_trace_not_returned` | A log with `TraceId = T2` does not appear in `ForTraceAsync(T1)` |
| 16 | `ForTraceAsync_unknown_trace_id_returns_empty` | `ForTraceAsync` on a trace with no correlated logs yields an empty sequence |
| 17 | `AcceptAsync_log_without_trace_id_appears_only_in_stream_not_in_for_trace` | A log with `TraceId = null` appears in `StreamAsync` but not in any `ForTraceAsync` |
| 18 | `StreamAsync_capped_log_stream_oldest_log_evicted` | After accepting `LogCap + 1` logs, the first log is no longer in `StreamAsync` |

### 6.6 Concurrency / thread-safety

This is not a correctness test — it is an absence-of-exception test. Under concurrent access, no operation should throw, deadlock, or return a `Trace` with internally inconsistent state (e.g., a span list whose count differs from what the dictionary holds).

```csharp
[Fact]
public async Task ConcurrentProducersAndReaders_never_throw_and_snapshots_are_consistent()
{
    // Arrange
    var store = new InMemoryTelemetryStore();
    var traceId = MakeTraceId(1); // non-zero; MakeTraceId(0) would produce all-zeros, rejected by TraceId.Parse
    var exceptions = new System.Collections.Concurrent.ConcurrentBag<Exception>();

    // Act — 4 producers, 2 readers, all concurrent
    var producers = Enumerable.Range(0, 4).Select(i => Task.Run(async () =>
    {
        for (var j = 0; j < 250; j++)
        {
            try
            {
                var span = MakeSpan(traceId, MakeSpanId(i * 1000 + j), startMs: j);
                await store.AcceptAsync(span, CancellationToken.None);
            }
            catch (Exception ex) { exceptions.Add(ex); }
        }
    }));

    var readers = Enumerable.Range(0, 2).Select(_ => Task.Run(async () =>
    {
        for (var k = 0; k < 100; k++)
        {
            try
            {
                var trace = await store.FindAsync(traceId, CancellationToken.None);
                // Invariant: if the trace is returned, its Spans collection must be
                // internally consistent — no null entries.
                trace?.Spans.Should().NotContainNulls();
            }
            catch (Exception ex) { exceptions.Add(ex); }
        }
    }));

    await Task.WhenAll(producers.Concat(readers));

    // Assert
    exceptions.Should().BeEmpty();
}
```

**Key constraint:** the assertion is an invariant ("count is non-negative, no null entries"), not an exact count. Under any schedule, the exact number of spans in the snapshot is non-deterministic. Asserting an exact count here would be a false determinism claim. If the test ever asserts an exact count under concurrent load, it is wrong.

---

## 7. `TraceSummary.FromTrace()` — domain-adjacent unit tests

`TraceSummary` lives in `Sentinel.CLI.Tui` but contains logic that is purely a function over a `Trace` — it is not TUI event handling. It warrants its own unit tests in the Application.Tests project (or a dedicated Tui.Tests project if created).

**Target:** `Sentinel.CLI.Application.Tests/Telemetry/TraceSummaryTests.cs` (or `Tui.Tests` if scaffolded)

| # | Test name | What it verifies |
|---|---|---|
| 1 | `FromTrace_empty_trace_returns_empty_sentinel_values` | Empty trace returns `SpanCount = 0`, `Status = Unset`, `StartedAt = DateTimeOffset.MinValue` |
| 2 | `FromTrace_all_ok_status_is_ok` | All spans `SpanStatus.Ok` → `Status = Ok` |
| 3 | `FromTrace_any_error_status_is_error` | One error span among ok spans → `Status = Error` |
| 4 | `FromTrace_multiple_roots_root_name_is_no_root` | `FindRoot()` returns null → `RootName = "(no root)"` |
| 5 | `FromTrace_duration_is_spans_min_to_max` | Duration = max(EndTime) − min(StartTime) across all spans |

These are unit tests — pure function, fixed timestamps, no I/O.

---

## 8. OTLP receiver contract testing (future)

When the OTLP receiver is implemented, split testing into two layers.

### 8.1 Mapping unit tests (no network — highest value)

**Target:** `Sentinel.CLI.Infrastructure.Tests/Telemetry/OtlpSpanMapperTests.cs`

Craft `ExportTraceServiceRequest` messages directly (using the generated protobuf types from `OpenTelemetry.Proto`). Call the mapper. Assert domain types. Zero network, zero Kestrel.

This layer covers:

- `OtlpSpanMapper_valid_span_maps_to_domain_span_preserves_all_fields` — round-trip all non-optional fields
- `OtlpSpanMapper_span_with_attributes_maps_text_integer_number_bool_list` — all `AttributeValue` discriminated union arms
- `OtlpSpanMapper_root_span_parent_span_id_is_null` — span with no parent span id maps to `ParentSpanId = null`
- `OtlpSpanMapper_missing_service_name_throws_or_uses_default` — specifies the behavior when `resource.attributes` lacks `service.name`
- `OtlpSpanMapper_empty_span_name_rejected` — `Span.Create` enforces non-whitespace name; mapper must either skip or propagate the validation
- `OtlpSpanMapper_end_time_before_start_time_rejected` — protobuf allows this; the mapper must enforce the domain constraint
- `OtlpSpanMapper_all_zero_trace_id_rejected` — `TraceId.Parse` rejects all-zeros; mapper propagates
- `OtlpSpanMapper_all_zero_span_id_rejected` — same for `SpanId`
- `OtlpSpanMapper_large_attribute_map_maps_without_truncation` — verify no silent truncation of attribute lists

**Do not use `Testcontainers` for this layer.** There is nothing to containerize.

### 8.2 Transport integration tests (in-proc Kestrel)

**Target:** `Sentinel.CLI.Infrastructure.Tests/Telemetry/OtlpReceiverIntegrationTests.cs`

Use `WebApplicationFactory<Program>` (or a minimal `WebApplication` factory scoped to the receiver endpoint) with `ConfigureTestServices` to swap `ITraceSink`/`ILogSink` for a capturing fake. Send real gRPC-encoded bytes via `GrpcChannel` pointed at the factory's `HttpClient`.

This layer covers:

- `OtlpGrpc_valid_export_request_returns_200_and_span_reaches_store` — happy path, one span accepted
- `OtlpGrpc_empty_resource_spans_returns_200_no_spans_added` — empty body is valid in OTLP; no crash
- `OtlpGrpc_malformed_bytes_returns_grpc_invalid_argument_does_not_crash` — send garbage bytes; assert the receiver returns an error status and the host process is still alive
- `OtlpGrpc_multiple_resource_spans_multiple_scopes_all_spans_accepted` — batch with N resource spans each with M scopes; count accepted = N×M spans
- `OtlpHttp_valid_export_request_returns_200` — same happy path for the HTTP/protobuf endpoint (:4318)
- `OtlpHttp_content_type_missing_returns_415` — HTTP endpoint must validate `Content-Type: application/x-protobuf`
- `OtlpGrpc_cancellation_during_export_does_not_crash` — cancel the client-side call; server handles gracefully

**Do not use Testcontainers.** The receiver is in-process; the test factory instantiates the Kestrel host in-memory.

---

## 9. Determinism rules

### 9.1 Time

`Span.StartTime` and `Span.EndTime` are `DateTimeOffset` — correct. `LogRecord.Timestamp` is `DateTimeOffset` — correct.

**Rule:** Every test fixture constructs timestamps from a fixed base offset:

```csharp
private static readonly DateTimeOffset T0 = new(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);
```

Never use `DateTimeOffset.UtcNow` inside a test method or a test helper. The fixture file `FixtureTraces.cs` violates this rule (`t0 = DateTimeOffset.UtcNow.AddSeconds(-30)`); that fixture is for the TUI spike, not for the test suite. The test helpers must not inherit this pattern.

**Flag — `FixtureTraces.cs` uses `DateTimeOffset.UtcNow`:** This is fine for the TUI demo fixture, but if any test method ever calls `FixtureTraces.Build()` directly, the test is time-dependent. Do not import `FixtureTraces` into the test projects. Write independent test builders.

### 9.2 Ordering

`Trace.Assemble()` ordering must be deterministic. Ordering rule: root-level nodes sorted by `StartTime` ascending; children at each node sorted by `StartTime` ascending. Test fixtures must assign distinct `StartTime` values so that ordering is unambiguous. If two spans have identical `StartTime`, the tie-break rule must be specified by the implementer and encoded in a test (e.g., `Assemble_two_spans_identical_start_time_tie_broken_by_span_id`).

### 9.3 Concurrency tests

The concurrency test in §6.6 asserts invariants, not outcomes. It uses `Task.WhenAll` with a deterministic completion condition (all tasks complete). No `Thread.Sleep`. No `Task.Delay` as a synchronization mechanism.

### 9.4 No network

No test makes a real network call to `:4317` or `:4318` on the host machine. Receiver tests use the `WebApplicationFactory` in-proc server, which binds to a random port inside the test process.

### 9.5 No shared static mutable state

`TelemetryAttributes.Empty` is static but immutable — safe. `SpanStatus.Unset` and `SpanStatus.Ok` are static singletons — safe (record types, immutable). The test classes must not hold static mutable fields. Each test method constructs its own `InMemoryTelemetryStore` instance.

### 9.6 `FixtureTraces.cs` — note for TUI tests

`MainWindow` and `TuiRunner` are not tested automatically. They cannot be driven headlessly without Terminal.Gui's internal event loop, which requires a real terminal driver. The assembly logic was extracted into the domain specifically to avoid this constraint. If smoke-testing of the TUI display is ever needed, use the `FakeDriver` mode in Terminal.Gui v2 with a scripted key sequence — but this is not part of the current strategy and is deferred.

---

## 10. Test project and file locations

```
tests/
  Sentinel.CLI.Domain.Tests/
    Telemetry/
      Common/
        TraceIdTests.cs          (exists — style template)
        SpanIdTests.cs           (new — mirrors TraceIdTests pattern)
        ServiceNameTests.cs      (new)
        AttributeValueTests.cs   (new — discriminated union arms)
      Spans/
        TraceTests.cs            (new — FindRoot matrix §5, Record overwrite, Record wrong TraceId)
        TraceAssemblyTests.cs    (new — Assemble matrix §4)
        SpanTests.cs             (new — Span.Create validations)
      Logs/
        LogRecordTests.cs        (new — LogRecord.Create validations)

  Sentinel.CLI.Infrastructure.Tests/
    Telemetry/
      InMemoryTelemetryStoreTests.cs     (new — §6 full matrix)
      OtlpSpanMapperTests.cs             (future — §8.1)
      OtlpReceiverIntegrationTests.cs    (future — §8.2)

  Sentinel.CLI.Application.Tests/
    Telemetry/
      TraceSummaryTests.cs               (new — §7)
```

---

## 11. Test helper conventions

### SpanBuilder

A private static helper (per test class, or extracted to `TestHelpers/SpanBuilder.cs` inside each test project) wrapping `Span.Create` with sensible defaults, so test fixtures are concise:

```csharp
// SpanBuilder.cs (in test project, not production code)
internal static class SpanBuilder
{
    private static readonly TraceId DefaultTraceId = TraceId.Parse("4bf92f3577b34da6a3ce929d0e0e4736");
    private static readonly DateTimeOffset Epoch = new(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);

    public static Span Make(
        string spanId,
        string? parentSpanId = null,
        string? traceId = null,
        string service = "svc-a",
        string name = "op",
        int startMs = 0,
        int durationMs = 10,
        SpanStatus? status = null)
    {
        var tid = TraceId.Parse(traceId ?? "4bf92f3577b34da6a3ce929d0e0e4736");
        return Span.Create(
            tid,
            SpanId.Parse(spanId),
            parentSpanId is null ? null : SpanId.Parse(parentSpanId),
            ServiceName.From(service),
            name,
            SpanKind.Internal,
            status ?? SpanStatus.Ok,
            Epoch.AddMilliseconds(startMs),
            Epoch.AddMilliseconds(startMs + durationMs));
    }
}
```

SpanId literals in the matrix use the 16-char hex strings from the domain's existing examples (e.g., `"0000000000000001"` through `"000000000000000e"` for simple test cases, to keep the fixtures readable). Do not reuse the real IDs from `FixtureTraces.cs` — keep test fixtures independent.

### `SpanNode` shape assertion

Once `SpanNode` is defined (as a record or class wrapping `Span` + `IReadOnlyList<SpanNode> Children`), add an extension for readable assertions:

```csharp
// extension for test readability — lives in test project only
internal static class SpanNodeAssertions
{
    public static void ShouldBeChain(this IReadOnlyList<SpanNode> roots, params string[] expectedSpanIds)
    {
        // walks the linear chain and asserts span_ids in order
    }
}
```

Exact implementation is left to the test implementer; the point is that the matrix assertions should read as `roots.Should().HaveCount(1)` and `roots[0].Children.Should().HaveCount(2)`, not as a multi-line nested property-chain.

---

## 12. What the implementer needs to build first

Before any test in §4 can be written, `Trace.Assemble()` must be defined. It does not exist yet. The implementer should:

1. Define `SpanNode` (record or class: `Span Span`, `IReadOnlyList<SpanNode> Children`) in the Domain layer.
2. Add `Trace.Assemble()` returning `IReadOnlyList<SpanNode>`, using the root predicate from `FindRoot()` but collecting all roots.
3. The test file `TraceAssemblyTests.cs` is blocked until this exists.

`TraceTests.cs` (for `FindRoot` and `Record`) can be written against the existing `Trace.cs` immediately.

`InMemoryTelemetryStoreTests.cs` requires `InMemoryTelemetryStore` to exist with its cap constant visible (either `internal` with `InternalsVisibleTo` or `public`). The Infrastructure project's DI extension (`AddInfrastructure`) is currently a stub — the store registration goes there.

---

## 13. Open questions blocking implementation

| Question | Blocks |
|---|---|
| What is the exact `RecentAsync` ordering rule — insertion order or latest-span-`StartTime`? | Store tests §6.2 |
| What is the log cap value (brief says "capped global log stream")? | Store test §6.5 case 18 |
| Does `SpanNode` carry `Depth` or is depth computed by the caller (e.g., the waterfall renderer)? | Assembly tests §4 — affects `SpanNode` definition |
| What is the tie-break rule when two spans share identical `StartTime`? | Assembly tests §4 ordering note |
| Is `InMemoryTelemetryStore.TraceCapacity` `public` or `internal`? | Store eviction tests §6.4 |
| Should `OtlpSpanMapper` skip or throw on unmappable spans (e.g., missing service name)? | Receiver mapping tests §8.1 |

---

## Acceptance checklist for this strategy artifact

- [ ] Assembly test matrix complete — 14 cases with input/expected specified
- [ ] `FindRoot` test matrix complete — 6 cases
- [ ] Store test matrix complete — 18 cases across acceptance, ordering, snapshot isolation, eviction, log correlation, concurrency
- [ ] `TraceSummary` unit tests specified — 5 cases
- [ ] Receiver testing split into mapping-unit (zero network) and transport-integration (in-proc Kestrel) layers
- [ ] No Testcontainers recommendation — justified
- [ ] No TUI automated tests — justified
- [ ] Determinism rules stated — fixed `DateTimeOffset`, no `UtcNow` in fixtures, `FixtureTraces.cs` flagged
- [ ] Concurrency test encodes invariant assertions, not outcome assertions
- [ ] Snapshot-isolation test is the named highest-priority store test
- [ ] Root predicate documented and shared with `FindRoot()` / `Assemble()` implementer
- [ ] `SpanBuilder` helper convention specified
- [ ] Open questions blocking implementation named and mapped to tests
