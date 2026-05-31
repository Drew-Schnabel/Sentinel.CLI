# Sentinel.CLI — TODO

Running backlog. Newly accumulated to-dos get logged here. Priority order within each
section; check items off as they land. Phases refer to `docs/architecture/design.md` §5.

_Last updated: 2026-05-31._

## In progress

_(nothing actively in progress)_

> ✅ **Keyboard shortcuts fixed & user-confirmed (2026-05-31).** Two root causes, found via a
> file-based key log on a real terminal:
> 1. The spike used the *instance* `Application.Create()`, which renders + runs timers but never
>    receives keyboard input. Switched to the **static `Application` facade** (obsolete-but-
>    functional; CS0618 suppressed) — it's the path Terminal.Gui 2.4.3 delivers keys to.
> 2. The focused `ListView` swallows printable chars (type-ahead) before any binding sees them.
>    So shortcuts are handled in the **raw `Application.KeyDown` event** (fires for every key
>    first) → `MapKey` → `InvokeCommand`, with `key.Handled = true` to consume. `MapKey` is
>    unit-tested. Diagnostic file-logging + title probes have been removed.

> ✅ **Real-terminal verification (2026-05-31):** keyboard shortcuts, view-switching (`1`-`4`/`0`/`m`),
> auto-refresh, and error-nav (`e`) confirmed working by the user. The status bar, Logs pane,
> producer section, and metrics view render in the same live session (no breakage reported) but
> weren't each individually signed off — flag if any look off. Pure logic is unit-tested
> (`TraceSelection`, `LogPresenter`, `MetricPresenter`, `StatusLine`, `ErrorNavigation`,
> `ViewLayout`, `MainWindowKeyTests`). 148 tests green.

## Backlog (prioritized)

### P1 — remaining TUI follow-ups
_(all cleared — the P1 TUI polish set is done; see Done below.)_

### P2 — multi-window viewer PoC (finish the spike)
- [ ] Build the query API + viewer described in `docs/architecture/adr/adr-0006-...md`:
  `query.proto` + `QueryService`/`QueryDtoMapper` (backed by `ITraceQueries`) + a
  `tools/Sentinel.Viewer` console. `--server` headless mode is done and verified.

### Testing / CI
- [ ] Headless TUI smoke test (Terminal.Gui `FakeDriver` + scripted input) so auto-refresh
  and rendering can be verified in CI without a real terminal — would retire the auto-refresh
  verification debt above. Exploratory; API needs investigation.
- [ ] (Low priority) Mapper fidelity vs a **real** OTel SDK: capture one real OTLP protobuf
  payload as a binary golden fixture and assert the mapper decodes it as expected — closes the
  "generator + mapper share my proto interpretation" gap cheaply. (Considered + rejected doing
  this via an Aspire/live sample app: too heavy, still not CI-runnable, marginal gain over the
  existing real-wire e2e test.)

### P3 — Ship: `dotnet tool` publish (the distribution wedge)
Packaging + **local** install are done (see Done). Remaining steps need the owner's
git/GitHub/nuget.org access + decisions:
- [ ] **Choose a license** → set `PackageLicenseExpression` (+ add a `LICENSE` file). Left unset on purpose.
- [ ] `git init` + push to GitHub; set `RepositoryUrl`/`PackageProjectUrl` + replace `OWNER`.
- [ ] Switch the static `<Version>0.1.0</Version>` to **MinVer** (git-tag driven) once the repo exists.
- [ ] `<RestorePackagesWithLockFile>true</RestorePackagesWithLockFile>` + commit `packages.lock.json` (CI `--locked-mode`).
- [ ] (Optional) `assets/icon.png` + `PackageIcon` for the nuget.org listing thumbnail.
- [ ] Configure nuget.org Trusted Publishing + the `release` GH environment, then tag `v0.x.0`
  to trigger `release.yml` (pack → push). The real push is owner-triggered (credentials + irreversible).

## Done

### Doctor — instrumentation health checks `:doctor` (2026-05-31)
- [x] **Diagnose the selected trace for OTel instrumentation problems** — the differentiating
  local-dev feature (watches your own instrumentation). Pure `TraceDoctor.Diagnose(IReadOnlyList<Span>)`:
  orphaned spans (broken propagation), >1 root (fragmented), clock skew (child >1ms before parent;
  sub-ms ignored), exception-without-Error-status, missing `service.name` (`"unknown"` fallback).
  `:doctor` → `MainWindow.DiagnoseSelectedTrace` (uses already-loaded `_waterfallRows`, synchronous) →
  pinned Details pane. New `CommandContext.Diagnose` (`Func<CommandResult>`) delegate.
  - **Honesty (advisor catch):** the store assembles-on-view + FIFO-evicts, so in-flight/evicted
    traces legitimately have orphans/extra roots → findings are worded as **possibilities**, never
    verdicts; a test asserts the orphan wording stays tentative (the guard the complete fixtures
    can't give). ⚠ manual-tty: confirm it doesn't cry wolf on live in-flight traffic.
  - **329 tests green** (Tui 205, Infra 33, Domain 40, Receiver 33, host 18).

### Filter — `since=` time window (2026-05-31)
- [x] **`:filter since=5m`** — show only recent traces. Pure `DurationParse` (`ms`/`s`/`m`/`h`/`d`,
  total → null on malformed). `TraceFilter` gained an optional `since` window; `Matches` signature is
  now `(Trace, status, DateTimeOffset now)` — `now` captured once per `PopulateAsync` tick (so the
  window slides live + tests use a fixed clock) and excludes traces older than `now - since`.
  Composes with `service=`/`status=`/text; rides the existing status-bar filter suffix. Tests:
  `DurationParseTests` (valid units + malformed→null) + `TraceFilter` since matching with a fixed
  clock + invalid-since error + expression. **319 tests green** (Tui 195, Infra 33, Domain 40,
  Receiver 33, host 18).

### Command bar — `:errors` + exception spotlight (2026-05-31)
- [x] **"What failed?" front and center.** Pure `ErrorSpotlight.For(Span)` gathers `exception.*` from
  the span's own attributes **and** its OTel `exception` event (last-wins), and renders a
  `*** ERROR ***` block (status message + type/message/stacktrace, stack truncated to 3 lines + a
  "N more lines" note) prepended to `RenderDetails` for any error-or-exception span. `:errors` is a
  one-word `ErrorsCommand` applying a `status=error` `TraceFilter` (reuses the shipped filter wiring;
  `:reset` clears). Tests: `ErrorSpotlightTests` (ok→empty / status message / exception attrs while
  status unset / exception-event source / stacktrace truncation) + `:errors` dispatch. **301 tests
  green** (Tui 177, Infra 33, Domain 40, Receiver 33, host 18).

### Fix — CLI port args swallowed by bare mode flags (2026-05-31)
- [x] **`--Receiver:GrpcPort=…` was silently dropped depending on argument order.** Root cause: the
  bare `--server`/`--demo` flags are read via `args.Contains`, but the same `args` feed
  `WebApplication`'s command-line **config** provider, which pairs a valueless `--server` with the
  *next* token as its value — so `--server --Receiver:GrpcPort=4319` bound `server` = the port arg
  and left gRPC at the default. Confirmed empirically (`--server` first → gRPC stayed 4317; ports
  first → both applied). Fix: new pure `HostArgs` helper — `Program.cs` reads mode flags via
  `HostArgs.Has` and passes `HostArgs.WithoutModeFlags(args)` to `CreateSlimBuilder`, so config args
  bind in **any order**. Verified: `--server --Receiver:GrpcPort=14317 --Receiver:HttpPort=14318`
  now binds 14317/14318. `HostArgsTests` (7) in the host project. **295 tests green** (Tui 171,
  Infra 33, Domain 40, Receiver 33, host 18). (Ports remain startup-only — no runtime change; not
  viable since Kestrel binds listeners once.)

### Command bar — `:capacity <n>` (2026-05-31)
- [x] **Live trace ring-buffer resize.** `InMemoryTelemetryStore` got a mutable `_maxTraces` (seeded
  from `StoreOptions.MaxTraces`, under `_gate`) + `SetMaxTraces(int)` that resizes and runs the
  existing `EvictTracesIfNeeded()` — shrinking evicts oldest immediately, growing just raises the cap.
  Exposed via `IStoreControl.SetTraceCapacity(int)` / `TraceCapacity`; `CapacityCommand` validates
  1–100000 (no-arg reports current). Tests: store shrink-evicts-now + grow-keeps-all; dispatch valid
  + bad-input theory (no arg/non-numeric/below-min/above-max) rejects without resizing. **288 tests
  green** (Tui 171, Infra 33, Domain 40, Receiver 33, host 11).

### Command-bar echo persistence (2026-05-31)
- [x] **Status-line command echoes lingered too briefly** (the ~1s refresh tick's `UpdateStatus`
  clobbered them after one tick). Now an echo (`exported …`, `filter cleared`, `theme: light`,
  errors) is held for `CommandMessageTicks = 5` refresh ticks (~5s at the default 1s rate) AND rides
  **alongside** the telemetry counts (`StatusLine.Format` gained a `message` segment), so counts stay
  visible. Tick-based (deterministic/testable, no wall clock); tuned for the default refresh rate.
  `UpdateStatus`/`StatusText` exposed internal for a linger test (shows immediately → survives a tick
  → ages out). **281 tests green.**

### Command bar — `:theme <name>` (2026-05-31)
- [x] **Color themes** (accessibility): `dark` (default) / `light` / `high-contrast` / `colorblind`.
  `Theme` = base scheme colors + service palette; pure `Themes.Resolve` (case-insensitive, total →
  null, callers fall back to Default). `MainWindow.ApplyTheme` builds a TG `Scheme` and `SetScheme`s
  the window **and every pane** (per-pane, not relying on cascade) + rebuilds `ServiceColorMap` from
  the palette + re-sources (preserving the selected trace). Both coloring paths
  (`TraceListSource.Render` + waterfall/logs `RowRender`) read the live `Normal.Background`, so
  foregrounds adapt to the new bg automatically. `ThemeCommand`; new `CommandContext.SetTheme`
  delegate. `Tui:Theme` config — lenient (bad name → default, no throw — per review). `ServiceColorMap`
  now takes a palette (`DarkPalette` is the default). Headless probe confirmed `SetScheme`/`GetScheme`
  work without a driver; a test proves the scheme reaches the custom-source trace-list pane
  (`TraceListScheme`). **277 tests green** (Tui 162, Infra 31, Domain 40, Receiver 33, host 11).
  - **Colorblind status colors done (same day):** `Theme.StatusTokenColor` makes the trace-list
    OK/ERR token theme-aware — colorblind = blue(OK)/vermillion(ERR), breaking the green/red trap;
    other themes fall back to the shared `RowColors` default (additive). The trace-list token is the
    only true green/red pair (waterfall/logs use red-vs-varied + `!`/`#` fills). **279 tests green.**
  - ⚠️ Real-terminal check (visual only — the mechanism is headlessly proven): `:theme light` on a
    light terminal is readable; `:theme colorblind` OK/ERR distinguishable; service colors distinct;
    `:theme dark` restores; `Tui:Theme=light` at startup applies.

### Command bar — `:export <path>` (2026-05-31)
- [x] **Export the selected trace + logs to JSON.** Export DTOs + a pure `TraceExporter.ToJson`
  in `Sentinel.CLI.Application/Serialization/` (shared so a future `:import`/`--replay` reuses them).
  Flat span list with `parentSpanId` (importer rebuilds via `Trace.Assemble()`), ids as strings,
  `AttributeValue` union flattened to JSON primitives — **no custom converters needed**. camelCase,
  indented, nulls omitted. `ExportCommand` delegates via a new `CommandContext.Export` delegate to
  `MainWindow.ExportSelectedTrace`, which serializes the already-loaded `_waterfallRows` +
  `_selectedTraceLogs` (synchronous — no re-query) and `File.WriteAllText`s, catching IO/access
  errors into a status echo. Tests: `TraceExporterTests` (real fixtures — services, typed attrs,
  parent links, temp-file round-trip), dispatch (no-path error / path delegates), no-trace guard.
  **256 tests green** (Tui 141, Infra 31, Domain 40, Receiver 33, host 11). Unlocks `:import`/replay.
  - ⚠️ Real-terminal check: select a trace, `:export ./x.json`, confirm the file is written and the
    status echoes "exported … (N spans, M logs)"; the success file-write path isn't unit-covered
    (needs loaded UI state), only the serializer + guard are.

### Command bar — `:pause` / `:resume` (2026-05-31)
- [x] **Store-level ingest freeze.** A shared `IngestGate` (volatile bool, registered singleton)
  checked at the top of all three sink `AcceptAsync` paths (`InMemoryTelemetryStore` spans+logs,
  `InMemoryMetricStore`); while paused, incoming telemetry is **dropped** (not buffered) so the view
  holds still and won't jump on resume. Flipped via `IStoreControl.SetPaused(bool)` (one method —
  `Resume` is a reserved keyword CA1716 flags on a public interface) + `IsPaused`; `StoreControl`
  composite delegates to the gate. `:pause`/`:resume` commands; status bar shows `[PAUSED]`
  (`StatusLine.Format` gained a `paused` flag). Tests: gate no-op-while-paused on both stores,
  registry dispatch toggles without clearing, StatusLine indicator. **248 tests green** (Tui 133,
  Infra 31, Domain 40, Receiver 33, host 11).

### Command bar — `:filter` / `:search` (2026-05-31)
- [x] **Trace-list filter + free-text search**, the first high-value command-bar customer. Pure
  immutable `TraceFilter` (`Create` → `Matches(Trace, status)`) matches across **every span's
  service + name** (not just the root — `:filter service=payment-service` finds it as a non-root
  span too), plus `status=ok|error|unset` against the aggregated trace status; free-text terms are
  AND-ed, case-insensitive. `:filter`/`:search` with no args clears. `FilterCommand`/`SearchCommand`
  push the filter to the view via a new `CommandContext.SetFilter` (`Action<TraceFilter?>` →
  `MainWindow.ApplyFilter`, which reloads from the first match). `PopulateAsync` captures the filter
  once (immutable → off-thread-safe) and filters the loaded traces before the row diff; the active
  filter + match count shows in the status bar (`StatusLine.Format` gained an optional suffix, e.g.
  `filter [service=payment-service] 2/3`). View-only — the store is untouched.
  - Tests: `TraceFilterTests` (parse, status aliases, matching against the **real UI fixtures**
    incl. the non-root-service case), registry dispatch (`:filter`/`:search`/invalid-status), and
    `RunCommand` set/clear of `ActiveFilter`. **239 tests green** (Tui 126, Infra 29, Domain 40,
    Receiver 33, host 11).
  - Hardening (from review): a stale `SelectedItem` after a filter narrows the list is **clamped**
    in `PopulateAsync` (can't index past the shorter list); `:search service=x` folds the `k=v`
    token back into search text instead of surprisingly clearing.
  - ⚠️ **Real-terminal check** (adds to the command-bar tty list): `:filter service=…` / `:search …`
    visibly narrows the list and the status bar shows the count; no-args clears; an out-of-range
    selection after filtering doesn't strand the panes.

### Command bar — `:help` / `:clear` (2026-05-31)
- [x] **`:`-style command bar (§0 of `Features.md`).** Gated key interceptor: `MainWindow.HandleKey`
  is the single decision seam — while the bar is open it consumes only `Enter` (submit) / `Esc`
  (cancel) and lets every other key fall through (not `Handled`) to the command `TextField`; closed,
  `:` opens it and the usual shortcuts apply. Colon is detected modifier-agnostically
  (`key.AsRune.Value == ':'`, since `:` is Shift+`;`). Pure `CommandLine.Parse` (verb + positionals +
  `k=v`) + an `ICommand`/`CommandRegistry` surface — each new verb is one class.
  - **`:help`** lists the registry into the **Details pane**, *pinned* (`_detailsPinned`) so the ~1s
    refresh tick doesn't clobber it; any navigation unpins.
  - **`:clear`** drops both stores via a new `IStoreControl` Application port + a `StoreControl`
    composite over `InMemoryTelemetryStore` + `InMemoryMetricStore` (TUI stays off Infrastructure).
    Also blanks the center/right panes when the trace list goes empty (`ClearTracePanes`) — without
    it, `PopulateAsync` left the last trace's waterfall/logs/details on screen after a clear.
  - Tests: `CommandLineTests`, `CommandRegistryTests`, a headless `HandleKey` gate test, the
    `RunCommand` result→UI mapping (`:help` → solo Details + pin; `:clear` → store cleared, no view
    change; navigation unpins; unknown-verb no-op), and store `Clear()` tests. **217 tests green**
    (Tui 104, Infra 29, Domain 40, Receiver 33, host 11).
  - **Trigger is `F2`, bar in the header (top)** — changed from the original `:` at the bottom: `:`
    is Shift+`;` and TG delivers it as base `;`+ShiftMask, so the bar never opened on a real terminal.
    `F2` is a named key (matched by `==`), so no shifted-character ambiguity. (Also: `run-with-traffic.ps1`
    silently runs a stale `sentinel.exe` if its build fails — a leftover `sentinel-load.exe` locking
    `Receiver.dll` is the usual cause; kill stray processes before testing.)
  - ✅ **Real-terminal verified by the user (2026-05-31):** F2 opens the header bar, typing works,
    Esc/F2 cancel, Esc backs out of solo views, `:filter`/`:reset` behave. (The earlier `:`-at-bottom
    design and the Esc-quits-the-app bug were both found and fixed during this live testing.)

### dotnet tool packaging + local install (2026-05-31)
- [x] **Packable host + verified local install.** Completed host nuget metadata (Authors, Title,
  Description, PackageTags, packed README via `PackageReadmeFile`, static `Version 0.1.0`); made
  libraries non-packable by default in `Directory.Build.props` (host overrides to `true`).
  `PackageIcon` intentionally omitted (no asset → would NU5046). `dotnet pack -c Release`
  produces `artifacts/Sentinel.CLI.0.1.0.nupkg` with **zero warnings**; the package bundles
  `sentinel.dll` + all deps + `DotnetToolSettings.xml` + README. **Verified end to end locally:**
  `dotnet tool install --global` from a temp local-feed `nuget.config` (needed because the global
  config has package-source-mapping that rejects `--add-source`) → ran `sentinel` (hit the non-tty
  guard, exit 2) → uninstalled cleanly. Remaining publish steps are owner-gated (see P3 above).

### Receiver/host log file sink (2026-05-31)
- [x] **File logging** — `Program.cs` clears console providers (they corrupt the TUI) and now adds
  a minimal custom `FileLoggerProvider` so warnings/errors are durable instead of vanishing. No
  third-party logging dep. Default `Warning`+ (avoids per-request hosting/Kestrel Information spam),
  default path `%LOCALAPPDATA%/Sentinel.CLI/logs/sentinel-<date>.log`; both overridable via
  `Logging:File:LogLevel` / `Logging:File:Path`. Writes a session-start marker; `--server` prints
  the resolved path. Helps `--server` mode too (ADR-0006 noted it had no logs). Pure
  `FileLogFormat` + path/level resolution + a real write/filter round-trip unit-tested in the
  **new `tests/Sentinel.CLI.Tests`** host test project (`InternalsVisibleTo`). Verified live via a
  headless `--server` run (file created with the marker).

### Metrics sparklines (2026-05-31)
- [x] **Per-series sparklines in the metrics view** — the metrics table now shows an inline trend
  (`▁▂▃▄▅▆▇█`) per series next to the latest value. The store is last-write-wins (no history), so
  a rolling per-series window is accumulated in the TUI (`MetricSparklines`, keyed by
  `MetricSeriesKey`, capacity 24) — **sampled on the timer every tick** (even while the view is
  hidden, so the trend is ready on open), rendered only when the Metrics view is showing. This
  changes metrics from refresh-on-switch to live-sampled. `Sparkline.Render` (min..max normalize,
  flat→mid bar) + `MetricPresenter.SparkValue` (gauge/sum value; histogram mean) are pure +
  unit-tested. _Caveats: x-axis is refresh ticks, not data timestamps; histogram mean is flat with
  the current generator (constant). Glyph rendering pending real-terminal verification._

### Global live log-stream pane (2026-05-31)
- [x] **Global log stream (all services)** — a 5th full-screen view (key `5`, via the spare
  `Command.Edit` slot; `0`/`m` exit) showing the firehose across every service from
  `ILogQueries.StreamAsync`, newest-first (cap 500), severity-colored (reuses `RowColors` +
  `RowRender`). New `PaneId.GlobalLogs` (own view, not in combined — `ViewLayout` updated).
  Refreshes on the timer **while visible** (a firehose should be live; metrics stay
  refresh-on-switch by design). `LogPresenter.FormatWithService` (service column instead of the
  span tag) unit-tested. _Known UX: under live traffic the list rebuilds most ticks so it resets
  to the top — fine for newest-at-top watching, but scrolling down to read older lines gets
  yanked back. Escape hatches (pause-while-scrolled, or tail-style newest-at-bottom) deferred
  pending user preference. Render pending real-terminal verification._

### Color-by-service (2026-05-31)
- [x] **Color-by-service in the trace list + waterfall** — each service gets a soft, distinct color
  from a **muted RGB palette** (`ServiceColorMap`, first-seen assignment so distinct services are
  reliably distinguishable and a service keeps its color across refreshes; red excluded). Trace-list
  rows colored by root service, waterfall rows by span service, **error status takes precedence
  (red)**. Same `RowRender`/`HasFocus` mechanics as severity coloring. Unit-tested (stable per
  service, distinct within palette, wraps after exhaustion). The **load generator now alternates two
  trace shapes** — `checkout` (root `orders-api`) and `inventory-sync` (root `inventory-service`,
  child `warehouse-db`) — so the trace list shows multiple root-service colors to compare. _Render
  pending real-terminal verification._
  - Earlier palette used the bright ANSI set, which read as too aggressive — softened to muted RGB.
  - **Trace list refined (2026-05-31):** row text colored by service, but **only the status token**
    is status-colored (ERR red, OK green, unset grey) — needs a custom `IListDataSource`
    (`TraceListSource`) since `RowRender` only colors a whole row; it draws each row per-column
    (rows are ASCII), delegating Count/marking/events to an inner `ListWrapper`. Status text now
    `OK` (was `ok`). `RowColors.StatusToken` unit-tested. _Hand-rolled `Render` (horizontal-scroll
    + selection highlight) pending real-terminal verification._

### TUI row coloring (2026-05-31)
- [x] **Severity-colored log rows + color-by-status waterfall** — done via the `ListView.RowRender`
  event (sets `ListViewRowEventArgs.RowAttribute` per row), **not** a custom `IListDataSource`
  (`ListWrapper.Render` is sealed; `RowRender` is the supported per-row hook). Logs: error/fatal
  red, warn yellow, debug/trace dim, info default; waterfall: error spans red. The selected row
  is left at the focus color so the cursor stays visible; only the foreground is swapped (the
  pane's background is preserved via `GetAttributeForRole(Normal).Background`). Color choices are
  pure + unit-tested (`RowColors`); the load generator now also emits a **WARN** log so warn
  coloring is exercised, **plus every 7th trace is "chatty" (~50 correlated logs cycling all
  severities)** so the Logs pane scrolls — which is the case that discriminates whether
  `RowRender.Row` is the item index vs the viewport row. _Render pending real-terminal
  verification: confirm colors track the correct rows **after scrolling** a chatty trace._

### TUI live-update (2026-05-31)
- [x] **Live-update the selected trace's panes** — on each refresh tick the open trace's
  waterfall/details/logs reload while **preserving the inspected span** (`ResolveSpanIndex`,
  unit-tested) and **skipping the rebuild entirely when nothing changed** (so a static trace
  you're reading doesn't flicker/scroll-reset each second). Wired in `MainWindow.PopulateAsync`
  via `SelectTraceAsync(preserveSpan: true)`; `OnSpanSelectionChanged` now honors the
  suppress flag. _Render behavior pending real-terminal verification._
- [x] **Configurable refresh interval** — `TuiOptions.RefreshMs` (`[Range(100, 60_000)]`, default
  1000), bound from the `Tui` config section, consumed by `TuiRunner`. (Features #6.)

### MVP-finalization goal (events/links + metrics + windowing + spike)
- [x] **Span events & links** — modeled in the domain (`SpanEvent`/`SpanLink`), mapped by
  `OtlpMapper` (malformed children skipped, span kept), rendered in the Details pane.
- [x] **Status bar + polish** — `IIngestDiagnostics`/`ITelemetryStats` ports → bottom status bar
  (traces/logs/metrics/dropped); `LogRecord.SeverityText`; resource attributes folded as
  `resource.*` + surfaced as a **producer** section in Details; jump-to-next-error (`e`).
- [x] **Metrics end-to-end** — `MetricPoint`/`MetricKind`/`MetricSeriesKey` domain,
  `IMetricSink`/`IMetricQueries`, sibling `InMemoryMetricStore` (last-write-wins, FIFO),
  `OtlpMapper.MapMetrics`, real gRPC + HTTP handlers, status-bar count, generator emits
  gauge/sum/histogram, metrics table view. Full tests incl. a real-wire HTTP round-trip.
- [x] **Windowed / focus mode** — `1`-`4` solo views, `0` combined, `m` maximize focused pane
  (`ViewLayout` decision unit-tested; metrics pane added).
- [x] **Multi-window spike** — `--server` headless mode (verified at runtime); full client/server
  design + remaining query-DTO blocker documented in ADR-0006. Viewer PoC intentionally not built.
- [x] **Features.md** — 19-feature themed backlog.

### Earlier
- [x] **Fixed broken keybindings** — moved shortcut handling from the non-firing raw
  `app.Keyboard.KeyDown` event to `MainWindow.OnKeyDownNotHandled`; mapping unit-tested
  (`MainWindowKeyTests`). Runtime routing pending user confirmation (see note above).
- [x] **Fixed TUI corruption** — `Program.cs` was missing `Logging.ClearProviders()`, so Kestrel
  /hosting console logs painted over the Terminal.Gui screen every few seconds. Silenced now.
- [x] Phase 1 — Terminal.Gui v2 TUI spike.
- [x] Phase 2 — domain `Trace.Assemble()` + `InMemoryTelemetryStore` (snapshot-on-read, FIFO).
- [x] Phase 3 — OTLP receiver (gRPC :4317 + HTTP :4318), ACL mapper, host pivot, port-in-use
  handling, SIGTERM, manual `r` refresh.
- [x] Phase 4 (partial) — live TUI auto-refresh (1s timer, selection-preserving;
  `TraceSelection.ResolveIndex` unit-tested). _Pending real-terminal verification — see above._
- [x] End-to-end test: OTLP gRPC → mapper → **real** `InMemoryTelemetryStore` → `ITraceQueries`
  → `Trace.Assemble()` reconstructs a cross-service tree (the headline feature, over the wire).
- [x] `ReceiverOptions` validation made non-vestigial — `Program.cs` binds + `Validator`-checks
  the ports before Kestrel binds.
- [x] Non-tty guard — `Program.cs` refuses to launch the TUI with a clear message (exit 2) when
  stdin/stdout is redirected (`TerminalGuard.IsInteractive`, unit-tested + verified by running
  the tool headless).
- [x] First-class **Logs pane** (Phase 4) — dedicated pane under Details showing the selected
  trace's logs, span-correlation tagged, severity-labelled (`LogPresenter`, unit-tested).
  _Rendering/layout pending real-terminal verification; severity color still a follow-up._
- [x] **Load generator** (`tools/Sentinel.LoadGenerator`, `--count`/`--endpoint`) — emits a
  synthetic cross-service checkout trace + logs over OTLP/HTTP so the live path can be watched
  on a real terminal. Smoke-verified (builds, sends valid OTLP, graceful when no server).
- [x] **One-command launcher** — `run-with-traffic.ps1` starts Sentinel + the generator
  together (generator in its own window, stopped on quit). Per-project `launchSettings.json`
  profiles added for VS multiple-startup / `dotnet run --launch-profile`.
