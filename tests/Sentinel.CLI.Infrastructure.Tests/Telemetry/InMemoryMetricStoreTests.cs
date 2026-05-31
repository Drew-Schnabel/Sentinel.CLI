using FluentAssertions;
using Sentinel.CLI.Infrastructure.Telemetry;
using static Sentinel.CLI.Infrastructure.Tests.TestHelpers.StoreTestHelpers;

namespace Sentinel.CLI.Infrastructure.Tests.Telemetry;

public class InMemoryMetricStoreTests
{
    private static readonly CancellationToken None = CancellationToken.None;

    [Fact]
    public async Task AcceptAsync_then_LatestAsync_returns_the_point()
    {
        var store = NewMetricStore();
        await store.AcceptAsync(MakeMetric("cpu", 0.5), None);

        var latest = await Collect(store.LatestAsync(None));

        latest.Should().ContainSingle().Which.Value.Should().Be(0.5);
    }

    [Fact]
    public async Task AcceptAsync_same_series_is_last_write_wins()
    {
        var store = NewMetricStore();
        await store.AcceptAsync(MakeMetric("cpu", 0.3, labels: ("host", "a")), None);
        await store.AcceptAsync(MakeMetric("cpu", 0.9, labels: ("host", "a")), None);

        store.SeriesCount.Should().Be(1);
        var latest = await Collect(store.LatestAsync(None));
        latest.Should().ContainSingle().Which.Value.Should().Be(0.9);
    }

    [Fact]
    public async Task AcceptAsync_different_labels_are_distinct_series()
    {
        var store = NewMetricStore();
        await store.AcceptAsync(MakeMetric("cpu", 0.3, labels: ("host", "a")), None);
        await store.AcceptAsync(MakeMetric("cpu", 0.4, labels: ("host", "b")), None);

        store.SeriesCount.Should().Be(2);
    }

    [Fact]
    public async Task AcceptAsync_at_cap_evicts_oldest_series()
    {
        var store = NewMetricStore(maxSeries: 2);
        await store.AcceptAsync(MakeMetric("m1", 1), None);
        await store.AcceptAsync(MakeMetric("m2", 2), None);
        await store.AcceptAsync(MakeMetric("m3", 3), None);

        store.SeriesCount.Should().Be(2);
        var names = (await Collect(store.LatestAsync(None))).Select(m => m.Name).ToList();
        names.Should().NotContain("m1");
    }

    [Fact]
    public async Task LatestAsync_snapshot_isolation_later_writes_do_not_change_taken_snapshot()
    {
        var store = NewMetricStore();
        await store.AcceptAsync(MakeMetric("m1", 1), None);

        var sequence = store.LatestAsync(None); // snapshot taken here
        await store.AcceptAsync(MakeMetric("m2", 2), None);

        (await Collect(sequence)).Should().ContainSingle();
    }

    [Fact]
    public async Task AcceptAsync_is_a_no_op_while_paused()
    {
        var gate = new IngestGate();
        var store = NewMetricStore(gate: gate);

        gate.Pause();
        await store.AcceptAsync(MakeMetric("cpu", 0.5), None);
        store.SeriesCount.Should().Be(0);

        gate.Resume();
        await store.AcceptAsync(MakeMetric("cpu", 0.5), None);
        store.SeriesCount.Should().Be(1);
    }

    [Fact]
    public async Task Clear_drops_all_series()
    {
        var store = NewMetricStore();
        await store.AcceptAsync(MakeMetric("cpu", 0.5), None);
        await store.AcceptAsync(MakeMetric("mem", 0.7), None);

        store.Clear();

        store.SeriesCount.Should().Be(0);
        (await Collect(store.LatestAsync(None))).Should().BeEmpty();
    }
}
