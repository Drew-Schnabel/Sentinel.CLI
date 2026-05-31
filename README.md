# Sentinel.CLI

See every span, log, and metric your app exports — in your terminal, no Docker required.

Point any OTLP exporter at `localhost:4317` (gRPC) or `localhost:4318` (HTTP) and watch
your traces assemble live in a navigable three-pane TUI. Spans from multiple services that
share a `trace_id` fold into one waterfall automatically, regardless of which service
emitted each span.

**Why Sentinel instead of a collector + Jaeger/Aspire dashboard?**
It installs as a single `dotnet tool` — one command, no container runtime, no compose file,
no sidecar. It runs inside your existing terminal session and quits cleanly when you do.

---

> **Status — active development (pre-release)**
>
> The OTLP receiver and the three-pane TUI both work today from source. Point an OTLP exporter
> at `localhost:4317` (gRPC) or `localhost:4318` (HTTP) and spans/logs flow into Sentinel's
> in-memory store; the trace list **auto-refreshes** (~1s) as data arrives, and `r` forces an
> immediate refresh. `--demo` seeds sample telemetry if you just want to explore the UI.
>
> The NuGet package is not yet published, so `dotnet tool install -g` isn't available yet —
> run from source (see [below](#run-it-today-build-from-source)).

---

## Run it today — build from source

Requires the [.NET 10 SDK](https://dotnet.microsoft.com/download) (version `10.0.300` or
later, per `global.json`).

```bash
git clone https://github.com/<your-org>/Sentinel.CLI
cd Sentinel.CLI
dotnet build
dotnet run --project src/Sentinel.CLI -- --demo
```

`--demo` seeds the in-memory store with **sample telemetry** — three synthetic services with
pre-built spans and correlated logs — so you can explore navigation without a live app.

To use it with a real app, run **without** `--demo` and point your OTLP exporter at
`localhost:4317` (gRPC) or `localhost:4318` (HTTP); press `r` to refresh as traces arrive:

```bash
dotnet run --project src/Sentinel.CLI
# then, in your app:
#   OTEL_EXPORTER_OTLP_ENDPOINT=http://localhost:4317   (gRPC)
#   OTEL_EXPORTER_OTLP_ENDPOINT=http://localhost:4318   (HTTP/protobuf)
```

Press `q` to quit.

### No app handy? Generate test traffic

If you don't have an instrumented app to point at Sentinel, the bundled load generator emits a
synthetic cross-service checkout trace (3 services, 4 spans, correlated logs, an occasional
error) over OTLP/HTTP.

**Both at once (recommended):** the launcher starts Sentinel *and* the generator from a single
command — the generator runs in its own window and is stopped when you quit Sentinel:

```powershell
./run-with-traffic.ps1            # stream traffic until you quit
./run-with-traffic.ps1 -Count 20  # send 20 traces then stop the generator
```

In **Visual Studio**, right-click the solution → *Configure Startup Projects* → *Multiple
startup projects*, set `Sentinel.CLI` and `Sentinel.LoadGenerator` to *Start*, and press F5
(each launches in its own console). Per-project launch profiles — including `--demo` and
`--count=20` variants — are in each project's `Properties/launchSettings.json`. (A single
`launchSettings.json` profile can only start one project, which is why launching both uses the
script or the multiple-startup-projects setting.)

**Or two terminals manually:**

```bash
dotnet run --project tools/Sentinel.LoadGenerator           # streams until Ctrl-C
dotnet run --project tools/Sentinel.LoadGenerator -- --count=20
dotnet run --project tools/Sentinel.LoadGenerator -- --endpoint=http://localhost:4318
```

Traces should appear in the list within ~1.5s and the view auto-refreshes; select one to see
the cross-service waterfall and its correlated logs.

### Run the test suite

```bash
dotnet test
```

---

## Keybindings

| Key | Action |
|-----|--------|
| `Tab` | Cycle focus between panes |
| `↑` / `↓` | Navigate the focused list |
| `1` / `2` / `3` / `4` / `5` | Full-screen a single signal: Traces / Waterfall / Logs / Metrics / all-service log stream |
| `0` | Back to the combined view |
| `m` | Maximize the focused pane (toggle) |
| `e` | Jump to the next (newer) error trace in the list |
| `r` | Force an immediate refresh (the trace list also auto-refreshes ~1s) |
| `F2` | Open the command bar (top of screen) — type a command, `Enter` to run, `Esc`/`F2` to cancel |
| `Esc` | Close the command bar; otherwise return from a single-signal/maximized view to the combined view |
| `q` / `Ctrl+C` | Quit |

### Command bar

Press `F2` to open a command line in the header (top) of the screen. Available commands:

| Command | Action |
|---------|--------|
| `:help` | List the available commands (shown in the Details pane) |
| `:filter [service=…] [status=ok\|error\|unset] [since=…] [text…]` | Narrow the trace list; matches across every span. `since=` is a window like `30s`/`5m`/`2h`. No args clears |
| `:search <text…>` | Free-text shorthand — match text against any service or span name |
| `:reset` | Clear the active filter/search — keeps all telemetry (no args on `:filter`/`:search` does the same) |
| `:pause` / `:resume` | Freeze / unfreeze ingest so the view holds still while you read (status bar shows `[PAUSED]`) |
| `:export <path>` | Write the selected trace (assembled spans + correlated logs) to a JSON file |
| `:theme <name>` | Switch color theme: `dark` / `light` / `high-contrast` / `colorblind` |
| `:capacity <n>` | Resize the trace ring buffer live (1–100000); shrinking evicts the oldest now |
| `:errors` | Show only error traces (shortcut for `filter status=error`) |
| `:doctor` | Health-check the selected trace: broken context propagation, clock skew, exception-without-error, missing `service.name` |
| `:clear` | Drop **all** received traces, logs, and metrics to start a clean session |

`:filter` and `:search` are view-only (the store is untouched); the status bar shows the active
filter and match count (e.g. `filter [service=payment-service] 2/3`). Filters match across the whole
cross-service trace, so `:filter service=payment-service` finds traces where that service appears in
*any* span, not just the root.

The startup theme can be set via configuration — `Tui:Theme` (env `Tui__Theme`), one of `dark`
(default) / `light` / `high-contrast` / `colorblind`; an unrecognized value falls back to the
default. `light` is the one to reach for on a light-background terminal. (`:theme` at runtime
overrides it for the session; persisting a runtime change isn't wired yet.)

Combined view: **Traces** (left) — **Waterfall** (center) — a right column split into
**Details** (top) and **Logs** (bottom) — and a bottom **status bar** (live trace/log/metric
counts + dropped counters). Select a trace to populate the waterfall and the Logs pane (tagged
with the short span id each log correlates to); select a span to populate Details with its
attributes, timing, **producer** (service + resource attributes), span events/links, and
correlated logs. Metrics get their own full-screen view (`4`).

---

## Global-tool install (once published)

The receiver works today from source. Once the package is published to NuGet, the
zero-friction flow becomes:

```bash
# Install globally (not yet published to NuGet)
dotnet tool install -g Sentinel.CLI

# Launch Sentinel
sentinel

# In your app, point the OTLP exporter at localhost
# OTEL_EXPORTER_OTLP_ENDPOINT=http://localhost:4317  (gRPC)
# OTEL_EXPORTER_OTLP_ENDPOINT=http://localhost:4318  (HTTP)
```

Sentinel binds to loopback only (`127.0.0.1` and `[::1]`). If ports 4317 or 4318 are already
in use (e.g., another local OTLP receiver), startup fails with a clear message before the TUI
launches; relocate via the `Receiver:GrpcPort` / `Receiver:HttpPort` configuration:

```bash
sentinel --Receiver:GrpcPort=14317 --Receiver:HttpPort=14318   # command-line
Receiver__GrpcPort=14317 Receiver__HttpPort=14318 sentinel      # environment variables
```

Ports are set at startup (the listeners bind once); there's no runtime port-change command.
The flags compose with `--server` / `--demo` in any order.

The TUI owns the screen, so console logging is disabled — receiver/host warnings and errors are
written to a log file instead (default `%LOCALAPPDATA%/Sentinel.CLI/logs/sentinel-<date>.log`,
or `~/.local/share/...` on Linux). Override the location with `Logging:File:Path` and the level
(default `Warning`) with `Logging:File:LogLevel`. `--server` mode prints the resolved path on start.

Metrics are ingested (gauge / sum / histogram) and shown in a dedicated full-screen Metrics view
(`4`); exponential-histogram and summary points are counted-and-dropped. JSON-encoded OTLP is not
supported (`application/x-protobuf` only); a JSON content-type returns `415`. A `--server` flag
runs the receiver + store headless with no TUI (basis for a future multi-window split — ADR-0006).

---

## Roadmap

The feature set is organized into tiers. Checked items are implemented.

**Tier 0 — core loop**

- [x] Three-pane TUI shell (trace list, waterfall, span detail)
- [x] Cross-service trace assembly (`Trace.Assemble()`, `AssembledTrace`/`SpanNode` forest) — Phase 2
- [x] In-memory store (`InMemoryTelemetryStore`) with snapshot isolation + FIFO eviction — Phase 2
- [x] OTLP gRPC receiver (`:4317`) — Phase 3
- [x] OTLP HTTP receiver (`:4318`) — Phase 3
- [x] Span events & links (modeled, mapped, shown in Details)
- [x] Metrics ingest (gauge/sum/histogram) + Metrics table view
- [x] Status bar — live trace/log/metric counts + dropped counters
- [x] Windowed / focus mode — single-signal full-screen views + maximize (`1`-`4`/`0`/`m`)
- [x] Live auto-refresh of the trace list (~1s timer, configurable via `Tui:RefreshMs`)
- [x] Live-update the selected trace's panes on the timer (preserves the inspected span)
- [x] Per-trace Logs pane (span-correlated, severity-labelled) _(pending real-terminal verification)_
- [x] Global live log-stream pane (all services) — full-screen view (`5`), severity-colored
- [x] Command bar (`:`) — `:help`, `:clear`; the extensible surface for future verbs
- [x] Pause / resume ingest — `:pause` / `:resume` (store-level freeze; `[PAUSED]` in the status bar)
- [x] Clear stored traces — `:clear`

**Tier 1 — developer ergonomics**
- [x] Trace ↔ log correlation (Details shows span-correlated logs; Logs pane shows the trace's logs)
- [x] Jump to next error trace (`e`)
- [x] Severity-colored Logs rows + color-by-status waterfall (via `ListView.RowRender`)
- [x] Color-by-service in the trace list + waterfall (error status takes precedence)
- [x] Service filter — `:filter service=…` (also `status=`; matches across all spans)
- [x] Full-text search across traces — `:search <text>` / `:filter <text>`

See [`Features.md`](Features.md) for the full forward-looking backlog.

**Tier 2 — advanced**
- [ ] Trace diff (compare two traces side by side)
- [ ] Metrics display (gauge / sum / histogram)
- [ ] SQLite persistence across sessions
- [x] JSON export — `:export <path>` (assembled trace + correlated logs)

---

## Architecture at a glance

```
Sentinel.CLI (host)
  ├── Sentinel.CLI.Tui          Terminal.Gui v2 shell; reads via query interfaces, never writes
  ├── Sentinel.CLI.Receiver     OTLP gRPC + HTTP endpoints; ACL from wire bytes to domain (Phase 3, done)
  ├── Sentinel.CLI.Infrastructure   InMemoryTelemetryStore; implements all four ports (Phase 2, done)
  ├── Sentinel.CLI.Application  Ports (ITraceSink, ILogSink) and query interfaces (ITraceQueries, ILogQueries)
  └── Sentinel.CLI.Domain       Span, Trace, LogRecord, value objects; zero framework dependencies
```

Dependency direction: `Tui → Application → Domain`; `Infrastructure → Application → Domain`;
`Receiver → Application → Domain`. Infrastructure and Receiver are peers — neither references
the other. The Tui layer never references Infrastructure or Receiver.

The headline design decision: **cross-service trace assembly is assemble-on-view**. The store
holds flat spans keyed by id. When a trace is selected, the TUI calls `Trace.Assemble()` in the
domain, which builds an `AssembledTrace` (a `SpanNode` forest) using a shared root predicate —
spans whose parent is absent from the trace are promoted to root candidates, so late-arriving or
cross-service spans assemble correctly regardless of arrival order. The walk is iterative (safe
on deep traces) and every span appears in the output exactly once. The TUI's waterfall renders
`AssembledTrace.Flatten()`; assembly logic lives entirely in the domain.

Full architecture documentation: [`docs/architecture/design.md`](docs/architecture/design.md)

Architecture decision records: [`docs/architecture/adr/`](docs/architecture/adr/)

---

## Contributing

### Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download) `10.0.300+`
- Any terminal with ANSI color support (Windows Terminal, iTerm2, standard Linux terminals)

### Build

```bash
dotnet build
```

The solution builds with `TreatWarningsAsErrors=true` — no warnings allowed.

### Test

```bash
dotnet test
```

### Project layout

```
src/
  Sentinel.CLI              Host — composition root, entry point
  Sentinel.CLI.Application  Ports and query interfaces (no implementations)
  Sentinel.CLI.Domain       Pure domain model — Span, Trace, LogRecord, value objects
  Sentinel.CLI.Infrastructure   In-memory store (InMemoryTelemetryStore, stub today)
  Sentinel.CLI.Tui          Terminal.Gui v2 shell and views
tests/
  Sentinel.CLI.Domain.Tests
  Sentinel.CLI.Application.Tests
  Sentinel.CLI.Infrastructure.Tests
```

Package management uses [Central Package Management](https://learn.microsoft.com/en-us/nuget/consume-packages/central-package-management) — add versions to `Directory.Packages.props`, not to individual `.csproj` files.

Issues, questions, and pull requests are welcome.
