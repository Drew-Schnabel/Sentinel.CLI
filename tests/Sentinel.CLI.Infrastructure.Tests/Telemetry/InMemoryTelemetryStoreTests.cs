using System.Collections.Concurrent;
using FluentAssertions;
using Sentinel.CLI.Infrastructure.Telemetry;
using static Sentinel.CLI.Infrastructure.Tests.TestHelpers.StoreTestHelpers;

namespace Sentinel.CLI.Infrastructure.Tests.Telemetry;

public class InMemoryTelemetryStoreTests
{
    private static readonly CancellationToken None = CancellationToken.None;

    // ---- 6.1 Span acceptance and retrieval ------------------------------------

    [Fact]
    public async Task AcceptAsync_single_span_trace_appears_in_find_async()
    {
        var store = NewStore();
        var span = MakeSpan(TraceId(1), SpanId(1));
        await store.AcceptAsync(span, None);

        var trace = await store.FindAsync(TraceId(1), None);

        trace.Should().NotBeNull();
        trace!.Spans.Should().ContainSingle().Which.SpanId.Should().Be(SpanId(1));
    }

    [Fact]
    public async Task AcceptAsync_multiple_spans_same_trace_single_trace_with_all_spans()
    {
        var store = NewStore();
        await store.AcceptAsync(MakeSpan(TraceId(1), SpanId(1)), None);
        await store.AcceptAsync(MakeSpan(TraceId(1), SpanId(2), parentSpanId: SpanId(1), startMs: 5), None);

        var trace = await store.FindAsync(TraceId(1), None);

        trace!.Spans.Should().HaveCount(2);
    }

    [Fact]
    public async Task AcceptAsync_different_traces_isolated_correctly()
    {
        var store = NewStore();
        await store.AcceptAsync(MakeSpan(TraceId(1), SpanId(1)), None);
        await store.AcceptAsync(MakeSpan(TraceId(2), SpanId(2)), None);

        var traceA = await store.FindAsync(TraceId(1), None);

        traceA!.Spans.Should().ContainSingle().Which.SpanId.Should().Be(SpanId(1));
    }

    [Fact]
    public async Task FindAsync_unknown_trace_id_returns_null()
    {
        var store = NewStore();

        var trace = await store.FindAsync(TraceId(999), None);

        trace.Should().BeNull();
    }

    [Fact]
    public async Task Clear_drops_all_traces_and_logs()
    {
        var store = NewStore();
        await store.AcceptAsync(MakeSpan(TraceId(1), SpanId(1)), None);
        await store.AcceptAsync(MakeLog(traceId: TraceId(1), spanId: SpanId(1)), None);

        store.Clear();

        store.TraceCount.Should().Be(0);
        store.LogCount.Should().Be(0);
        (await store.FindAsync(TraceId(1), None)).Should().BeNull();
        (await Collect(store.RecentAsync(10, None))).Should().BeEmpty();
        (await Collect(store.StreamAsync(None))).Should().BeEmpty();
    }

    [Fact]
    public async Task SetMaxTraces_shrinking_evicts_oldest_immediately()
    {
        var store = NewStore(maxTraces: 5);
        for (var i = 1; i <= 5; i++)
        {
            await store.AcceptAsync(MakeSpan(TraceId(i), SpanId(i)), None);
        }

        store.SetMaxTraces(2);

        store.MaxTraces.Should().Be(2);
        var recent = await Collect(store.RecentAsync(10, None));
        recent.Select(t => t.Id).Should().Equal(TraceId(5), TraceId(4)); // newest two kept
    }

    [Fact]
    public async Task SetMaxTraces_growing_keeps_all_and_raises_the_cap()
    {
        var store = NewStore(maxTraces: 2);
        await store.AcceptAsync(MakeSpan(TraceId(1), SpanId(1)), None);
        await store.AcceptAsync(MakeSpan(TraceId(2), SpanId(2)), None);

        store.SetMaxTraces(10);
        await store.AcceptAsync(MakeSpan(TraceId(3), SpanId(3)), None);

        store.MaxTraces.Should().Be(10);
        (await Collect(store.RecentAsync(10, None))).Should().HaveCount(3); // nothing evicted
    }

    [Fact]
    public async Task AcceptAsync_is_a_no_op_while_paused_then_resumes()
    {
        var gate = new IngestGate();
        var store = NewStore(gate: gate);

        gate.Pause();
        await store.AcceptAsync(MakeSpan(TraceId(1), SpanId(1)), None);
        await store.AcceptAsync(MakeLog(traceId: TraceId(1), spanId: SpanId(1)), None);

        store.TraceCount.Should().Be(0);
        store.LogCount.Should().Be(0);

        gate.Resume();
        await store.AcceptAsync(MakeSpan(TraceId(1), SpanId(1)), None);

        store.TraceCount.Should().Be(1);
    }

    [Fact]
    public async Task Clear_then_accept_works_and_does_not_resurrect_evicted_ids()
    {
        var store = NewStore();
        await store.AcceptAsync(MakeSpan(TraceId(1), SpanId(1)), None);
        store.Clear();

        await store.AcceptAsync(MakeSpan(TraceId(2), SpanId(2)), None);

        var recent = await Collect(store.RecentAsync(10, None));
        recent.Select(t => t.Id).Should().Equal(TraceId(2));
    }

    // ---- 6.2 RecentAsync ordering ---------------------------------------------

    [Fact]
    public async Task RecentAsync_returns_traces_newest_first_by_insertion_order()
    {
        var store = NewStore();
        await store.AcceptAsync(MakeSpan(TraceId(1), SpanId(1)), None);
        await store.AcceptAsync(MakeSpan(TraceId(2), SpanId(2)), None);
        await store.AcceptAsync(MakeSpan(TraceId(3), SpanId(3)), None);

        var recent = await Collect(store.RecentAsync(10, None));

        recent.Select(t => t.Id).Should().Equal(TraceId(3), TraceId(2), TraceId(1));
    }

    [Fact]
    public async Task RecentAsync_limit_respected_returns_at_most_limit()
    {
        var store = NewStore();
        for (var i = 1; i <= 5; i++)
        {
            await store.AcceptAsync(MakeSpan(TraceId(i), SpanId(i)), None);
        }

        var recent = await Collect(store.RecentAsync(3, None));

        recent.Should().HaveCount(3);
    }

    [Fact]
    public async Task RecentAsync_empty_store_returns_empty()
    {
        var store = NewStore();

        var recent = await Collect(store.RecentAsync(10, None));

        recent.Should().BeEmpty();
    }

    // ---- 6.3 Snapshot isolation (highest priority) ----------------------------

    [Fact]
    public async Task FindAsync_snapshot_isolation_mutating_store_later_does_not_alter_returned_trace()
    {
        var store = NewStore();
        await store.AcceptAsync(MakeSpan(TraceId(1), SpanId(1)), None);

        var snapshot = await store.FindAsync(TraceId(1), None);
        var countBefore = snapshot!.Spans.Count;

        await store.AcceptAsync(MakeSpan(TraceId(1), SpanId(2), parentSpanId: SpanId(1), startMs: 5), None);

        snapshot.Spans.Count.Should().Be(countBefore);
    }

    [Fact]
    public async Task RecentAsync_snapshot_isolation_mutating_store_during_enumeration_does_not_throw()
    {
        var store = NewStore();
        await store.AcceptAsync(MakeSpan(TraceId(1), SpanId(1)), None);

        var act = async () =>
        {
            var index = 2;
            await foreach (var _ in store.RecentAsync(10, None))
            {
                await store.AcceptAsync(MakeSpan(TraceId(index), SpanId(index)), None);
                index++;
            }
        };

        await act.Should().NotThrowAsync();
    }

    // ---- 6.4 FIFO ring-buffer eviction ----------------------------------------

    [Fact]
    public async Task AcceptAsync_at_cap_oldest_trace_evicted()
    {
        var store = NewStore(maxTraces: 3);
        for (var i = 1; i <= 4; i++)   // one past the cap
        {
            await store.AcceptAsync(MakeSpan(TraceId(i), SpanId(i), startMs: i * 10), None);
        }

        var evicted = await store.FindAsync(TraceId(1), None);

        evicted.Should().BeNull();
    }

    [Fact]
    public async Task AcceptAsync_at_cap_newest_trace_present()
    {
        var store = NewStore(maxTraces: 3);
        for (var i = 1; i <= 4; i++)
        {
            await store.AcceptAsync(MakeSpan(TraceId(i), SpanId(i), startMs: i * 10), None);
        }

        var newest = await store.FindAsync(TraceId(4), None);

        newest.Should().NotBeNull();
    }

    [Fact]
    public async Task AcceptAsync_below_cap_no_eviction()
    {
        var store = NewStore(maxTraces: 3);
        for (var i = 1; i <= 3; i++)
        {
            await store.AcceptAsync(MakeSpan(TraceId(i), SpanId(i)), None);
        }

        var recent = await Collect(store.RecentAsync(10, None));

        recent.Should().HaveCount(3);
    }

    // ---- 6.5 Log acceptance and correlation -----------------------------------

    [Fact]
    public async Task AcceptAsync_log_appears_in_stream_async()
    {
        var store = NewStore();
        await store.AcceptAsync(MakeLog(traceId: TraceId(1), body: "hello"), None);

        var logs = await Collect(store.StreamAsync(None));

        logs.Should().ContainSingle().Which.Body.Should().Be("hello");
    }

    [Fact]
    public async Task AcceptAsync_log_with_trace_id_appears_in_for_trace_async()
    {
        var store = NewStore();
        await store.AcceptAsync(MakeLog(traceId: TraceId(1)), None);

        var logs = await Collect(store.ForTraceAsync(TraceId(1), None));

        logs.Should().ContainSingle();
    }

    [Fact]
    public async Task ForTraceAsync_log_from_other_trace_not_returned()
    {
        var store = NewStore();
        await store.AcceptAsync(MakeLog(traceId: TraceId(2)), None);

        var logs = await Collect(store.ForTraceAsync(TraceId(1), None));

        logs.Should().BeEmpty();
    }

    [Fact]
    public async Task ForTraceAsync_unknown_trace_id_returns_empty()
    {
        var store = NewStore();

        var logs = await Collect(store.ForTraceAsync(TraceId(1), None));

        logs.Should().BeEmpty();
    }

    [Fact]
    public async Task AcceptAsync_log_without_trace_id_appears_only_in_stream_not_in_for_trace()
    {
        var store = NewStore();
        await store.AcceptAsync(MakeLog(traceId: null, body: "uncorrelated"), None);

        var stream = await Collect(store.StreamAsync(None));
        var forTrace = await Collect(store.ForTraceAsync(TraceId(1), None));

        stream.Should().ContainSingle().Which.Body.Should().Be("uncorrelated");
        forTrace.Should().BeEmpty();
    }

    [Fact]
    public async Task StreamAsync_capped_log_stream_oldest_log_evicted()
    {
        var store = NewStore(maxLogStream: 3);
        for (var i = 1; i <= 4; i++)   // one past the cap
        {
            await store.AcceptAsync(MakeLog(traceId: TraceId(1), body: $"log-{i}", atMs: i), None);
        }

        var logs = await Collect(store.StreamAsync(None));

        logs.Should().HaveCount(3);
        logs.Select(l => l.Body).Should().NotContain("log-1");
    }

    // ---- 6.6 Concurrency / thread-safety --------------------------------------

    [Fact]
    public async Task ConcurrentProducersAndReaders_never_throw_and_snapshots_are_consistent()
    {
        var store = NewStore();
        var traceId = TraceId(1);
        var exceptions = new ConcurrentBag<Exception>();

        var producers = Enumerable.Range(0, 4).Select(i => Task.Run(async () =>
        {
            for (var j = 0; j < 250; j++)
            {
                try
                {
                    // +1 guarantees a non-zero span id (SpanId.Parse rejects all-zeros).
                    await store.AcceptAsync(MakeSpan(traceId, SpanId(i * 1000 + j + 1), startMs: j), None);
                }
                catch (Exception ex)
                {
                    exceptions.Add(ex);
                }
            }
        }));

        var readers = Enumerable.Range(0, 2).Select(_ => Task.Run(async () =>
        {
            for (var k = 0; k < 100; k++)
            {
                try
                {
                    var trace = await store.FindAsync(traceId, None);
                    trace?.Spans.Should().NotContainNulls();
                }
                catch (Exception ex)
                {
                    exceptions.Add(ex);
                }
            }
        }));

        await Task.WhenAll(producers.Concat(readers));

        exceptions.Should().BeEmpty();
    }
}
