# Sentinel.CLI — Feature Backlog

Forward-looking, execution-ready backlog. Each feature carries a rough effort
(**S**mall / **M**edium / **L**arge), the files it touches, the existing ports/types it reuses, and
a test approach. Items here are *not yet started*; the day-to-day working backlog lives in
[`todo.md`](todo.md).

This backlog is organized around a single thesis: **build the command bar first, and most of the
other UI features become small commands on top of it.** `(was #N)` cross-references the prior flat
list so nothing is lost.

_Last updated: 2026-05-31._

---

## § 0 — The command bar (meta-feature) — **M–L** — ✅ FOUNDATION SHIPPED (2026-05-31)

A `:`-style (vim-like) command line at the bottom of the TUI that accepts a verb plus arguments.
This is the highest-leverage item in the backlog: it is *one* input surface that pays for itself
across at least five features that are otherwise each scoped as "invent a keybinding + build a
bespoke UI" — search/filter (was #6), pause/resume/clear (README roadmap), export (was #15), and
theming (was #2). With the bar in place, each of those becomes **a registry entry + a parser case**,
not a new screen. The tool is also nearly out of single-letter keys (`0`–`5`, `m`, `e`, `r`, `q`),
and single letters can't take arguments — the bar removes that ceiling.

> **Status — shipped 2026-05-31.** The gated interceptor, pure `CommandLine.Parse`, the
> `ICommand`/`CommandRegistry` surface, and the first two commands (`:help`, `:clear`) are in.
> Implementation matched the plan below. Notable refinements found during build:
> - **`:help` renders to the Details pane, pinned.** The status label *and* the Details pane are
>   both rebuilt by the ~1s refresh tick (`SelectTraceAsync` rewrites `_details.Text`), so a
>   multi-line help listing in either is clobbered within a second. A `_detailsPinned` flag makes
>   the tick skip the Details write while `:help` output is showing; any navigation (`0`/`1`-`5`/`m`
>   or selecting a trace/span) unpins.
> - **`:clear` clears *both* stores** via a new `IStoreControl` Application port + a `StoreControl`
>   composite over `InMemoryTelemetryStore` + `InMemoryMetricStore` (the TUI stays off Infrastructure).
> - **Trigger is `F2`, bar lives in the header (top).** Originally `:` at the bottom, but `:` is
>   Shift+`;` — Terminal.Gui encodes it as base `;` + ShiftMask, so the obvious `AsRune == ':'` check
>   missed it and the bar never opened on a real terminal. Switched to the named key `F2` (matched by
>   `==` like the other shortcuts — no shifted-character ambiguity) and moved the input to a header
>   row, per the chosen UX.
> - **Routing lives in one testable seam:** `MainWindow.HandleKey(Key) → bool`, headlessly unit-tested
>   for the gate (keys pass through while open, intercepted while closed). The *live* wiring (a key
>   actually reaching the TextField, focus surviving the refresh tick) still needs a real-terminal
>   pass — see `todo.md`.
>
> **Remaining Tier-A commands (`:filter`, `:pause`/`:resume`, `:export`, `:capacity`) are now each a
> small `ITuiCommand` + parser case** — the hard part (the surface) is done.

### The constraint that decides whether it's buildable

`MainWindow.BindKeys()` (`src/Sentinel.CLI.Tui/Views/MainWindow.cs:135`) subscribes to
`TGuiApp.Keyboard.KeyDown`, runs the pure `MapKey(key)` (`:147`), and on a match calls
`InvokeCommand` and sets `key.Handled = true`. That global interceptor exists *because* the focused
`ListView` swallows printable chars (type-ahead) before any binding sees them. A command bar needs
those exact printable chars routed to a `TextField` — so the interceptor would **eat command input**
before the field sees it.

**Resolution — gated interceptor (chosen).** Add a `bool _commandBarOpen` field; the first line of
the `KeyDown` handler at `MainWindow.cs:137` becomes `if (_commandBarOpen) return;` — note: do *not*
set `key.Handled`, so the key falls through to the focused `TextField`. `:` opens the bar and sets
the flag; `Enter` (submit) and `Esc` (cancel) close it and clear the flag. The alternative — a modal
`Application.Run(dialog)` with its own run context — is recorded and rejected in
[ADR-0007](docs/architecture/adr/adr-0007-command-bar-key-routing.md).

### Design

- **Command `TextField`** anchored at the bottom like `_statusLabel` (`MainWindow.cs:36,98`, via
  `Pos.AnchorEnd(1)`); hidden until `:` is pressed.
- **Pure parser** `CommandLine.Parse(string) -> ParsedCommand?` — total, never throws, mirrors how
  `MapKey` is pure and unit-tested. Grammar: `verb [positional] [key=value ...]`.
- **Command registry**: `ICommand { string Verb; string Help; CommandResult Execute(args, ctx); }`;
  flow is parser → registry lookup → execute; an unknown verb produces a status-line error. Each new
  command is one class — this is the extensibility surface every Tier A feature plugs into.
- **Result echo**: `StatusLine.Format` (`src/Sentinel.CLI.Tui/Views/StatusLine.cs:6`) only emits
  telemetry counts, so add a transient `MainWindow.ShowCommandResult(string)` that writes
  `_statusLabel` (e.g. `filter: 12/340 traces`, `exported ./trace.json`,
  `unknown command 'thmee' — try :help`) and is overwritten on the next refresh tick.

### Tests
- `CommandLine.Parse` `[Theory]` cases (verbs, args, `k=v`, malformed input).
- Registry dispatch (verb → command, unknown → error result).
- Gate-flag behavior — keys pass through when `_commandBarOpen`, intercepted when closed (headless,
  like `MainWindowKeyTests.cs`).

### Caveat
**Do not block the release on this.** The ship-blockers come first (icon / NU5046 pack failure,
committing lock files, the `release.yml` dry-run — see [`todo.md`](todo.md) P3); an unpublished tool
gains nothing from a command bar. Effort is **M–L, not S** — modal/gated input + parser + registry +
result echo + the first commands. If it slips, `:clear` / `:pause` / theme can each ship as
standalone keybindings (they compose better with the bar but don't require it).

---

## § Tier A — command-bar customers (high value, ride the bar)

### `:filter` / `:search` — **M** (was #6) — ✅ shipped 2026-05-31
Filter the trace list by `service=`, `status=`, and/or free-text terms; `:search <text>` is the
free-text shorthand. `:filter` / `:search` with no args clears.
- **Mechanism (as built):** a pure, immutable `TraceFilter` (`TraceFilter.Create` →
  `TraceFilter.Matches(Trace, status)`) applied in `MainWindow.PopulateAsync` against the **full
  `Trace`** (every span's service + name), not just the root — so `service=payment-service` finds
  traces where it appears in *any* span. `status=` matches the aggregated `TraceSummary.Status`
  (any-span-error → error). Terms are AND-ed, case-insensitive substring across service + span names.
- **Wiring:** commands push the filter to the view via `CommandContext.SetFilter` (a
  `Action<TraceFilter?>` → `MainWindow.ApplyFilter`, which reloads from the first match). The filter
  is captured once at the top of `PopulateAsync` (immutable → safe off the UI thread). Active filter
  + match count shows in the status bar (`StatusLine.Format` gained an optional `filter` suffix,
  e.g. `filter [service=payment-service] 2/3`).
- **Tests:** `TraceFilterTests` (parse + status aliases + matching against the **real UI fixtures**,
  incl. the non-root-service case); registry dispatch for `:filter`/`:search`/invalid-status;
  `MainWindow.RunCommand` sets/clears `ActiveFilter`. The list-narrowing render itself is the
  manual-tty part.

### `:pause` / `:resume` / `:clear` — **S each** (README roadmap)
Freeze ingest while reading a trace; drop everything to start clean.
- **`:clear`** → ✅ **shipped 2026-05-31.** `void Clear()` (under `_gate`) on both
  `InMemoryTelemetryStore` and `InMemoryMetricStore`, fanned out by a `StoreControl` composite behind
  the `IStoreControl` Application port so the TUI doesn't depend on Infrastructure.
- **`:pause` / `:resume`** → ✅ **shipped 2026-05-31.** A shared `IngestGate` (volatile bool) checked
  at the top of all three `AcceptAsync` paths (trace/log/metric); paused → telemetry dropped (not
  buffered) so the view holds still and doesn't jump on resume. Flipped via `IStoreControl.SetPaused`
  (single method — `Resume` is a reserved keyword the analyzer flags on a public interface). Status
  bar shows `[PAUSED]`. (Dropped-while-paused counting was deemed unnecessary — the indicator signals
  the freeze.)
- **Alternative (view-only freeze):** stop the refresh timer in `TuiRunner.Run`
  (`src/Sentinel.CLI.Tui/TuiRunner.cs:61`) — simpler, but ingest keeps filling the store. Recommend
  **store-level pause** for a true freeze; document both.
- **Tests:** `Clear()` empties the snapshot; `AcceptAsync` is a no-op while paused.

### `:export <path>` — **S→M** (was #15) — ✅ shipped 2026-05-31
**As built:** export DTOs + a pure `TraceExporter` (`ToJson`) live in `Sentinel.CLI.Application/Serialization/`
(shared so a future `:import`/`--replay` reuses them). Spans are a flat list with `parentSpanId`
(so an importer rebuilds the tree via `Trace.Assemble()`); ids are strings and the `AttributeValue`
union is flattened to JSON primitives — so **no custom converters were needed**, just a small DTO
mapper. `System.Text.Json`, camelCase, indented, nulls omitted. The command delegates to
`MainWindow.ExportSelectedTrace`, which serializes the **already-loaded** `_waterfallRows` +
`_selectedTraceLogs` (synchronous — no re-query) and `File.WriteAllText`s, catching IO/access errors
into a status-line message. Tested against the real fixtures (services, typed attrs, parent links,
file round-trip) + dispatch + the no-trace-selected guard.
_Original plan (kept for reference):_
Dump the selected assembled trace + its correlated logs to JSON for bug reports / sharing.
- **Source:** `ITraceQueries.FindAsync` → `Trace.Assemble()`
  (`src/Sentinel.CLI.Domain/Telemetry/Spans/Trace.cs:55`) + `ILogQueries.ForTraceAsync`.
- **Serialization gap:** domain types have **no JSON support** today. `TraceId`/`SpanId`
  (`src/Sentinel.CLI.Domain/Telemetry/Common/TraceId.cs:5`) are `readonly record struct`s with a
  string `Value`, and `AttributeValue`
  (`src/Sentinel.CLI.Domain/Telemetry/Common/AttributeValue.cs:6`) is a discriminated union —
  both need custom `System.Text.Json` converters (or `[JsonPolymorphic]` for the union). Keep the
  converters in a new `Sentinel.CLI.Application` (or a dedicated serialization) location so
  `:import` (Tier B) can reuse them.
- **Tests:** round-trip a fixture assembled trace; per-converter unit tests for `TraceId`/`SpanId`
  and each `AttributeValue` case.

### `:capacity <n>` — **S** — ✅ shipped 2026-05-31
Resize the trace ring buffer live (1–100000, matching `StoreOptions.MaxTraces`).
- **As built:** `InMemoryTelemetryStore` got a mutable `_maxTraces` (seeded from options, guarded by
  `_gate`) + `SetMaxTraces(int)` which sets it and calls the existing `EvictTracesIfNeeded()` — so
  shrinking drops the oldest traces immediately. Exposed via `IStoreControl.SetTraceCapacity`/
  `TraceCapacity`; `CapacityCommand` validates the range (no-arg reports the current cap).
- **Tests:** store shrink-evicts-now / grow-keeps-all; dispatch valid resizes + a `[Theory]` of bad
  inputs (no arg / non-numeric / below-min / above-max) reject without resizing.

### `:help` — **S** — ✅ shipped 2026-05-31
Lists the registry's verbs + each command's `Help` string into the **Details pane** (pinned against
the refresh tick). Falls out of the registry for free — new commands self-document here.

---

## § Tier B — net-new capabilities

### Doctor — instrumentation health checks — `:doctor` — ✅ shipped 2026-05-31
The differentiating local-dev move: Sentinel watches *your own* instrumentation as you write it, so
it can flag what's *wrong*, not just display traces. Pure `TraceDoctor.Diagnose(spans)` over the
selected trace, findings rendered to the pinned Details pane via `:doctor`.
- **Checks:** orphaned spans (broken context propagation), >1 root (fragmented/merged), clock skew
  (child starts >1ms before its parent — sub-ms ignored), exception recorded without an Error status,
  missing `service.name` (the mapper's `"unknown"` fallback).
- **Honesty (key design point, per review):** the store assembles on view and FIFO-evicts, so an
  in-flight or evicted-parent trace *legitimately* has orphans/extra roots. Findings are therefore
  worded as **possibilities** ("…or the parent hasn't arrived yet / was evicted"), never verdicts. A
  dedicated test asserts the orphan wording stays tentative (fixtures are all complete, so this is
  the guard fixtures can't provide). "Doesn't cry wolf on live in-flight traffic" is on the
  manual-tty checklist.
- **Tests:** `TraceDoctorTests` (healthy→empty, orphan-tentative, multi-root, skew + sub-ms ignored,
  exception-without-error both ways, missing service.name); `:doctor` dispatch; no-trace guard.

### Trace replay — `--replay <file>` / `:import` — **M**
Re-open a captured failure offline; the feature that makes `:export` worth more than a curiosity
(attach a trace dump to a failing test, then replay it).
- **Mechanism:** deserialize the `:export` JSON → domain objects → inject via
  `ITraceSink.AcceptAsync`, exactly the seeding pattern in `DemoSeed.SeedAsync`
  (`src/Sentinel.CLI.Tui/DemoSeed.cs:12`). Reuses the `:export` converters.
- **CLI flag:** parse `--replay` alongside the existing manual `--demo` / `--server` checks in
  `src/Sentinel.CLI/Program.cs:20,104` (no `System.CommandLine` yet — was #13).
- **Tests:** import a previously exported fixture and assert `Trace.Assemble()` reconstructs the same
  cross-service tree.

### Latency outlier surfacing — `:slow` — **M**
Flag the slow trace, not the average one — the usual entry point for "why is this endpoint slow."
- **Mechanism:** durations are already computed for the waterfall (`AssembledTrace.Envelope`, plus
  per-`Span` start/end). Add a **pure percentile helper** + a filter/column flag (e.g. spans/traces
  above the Nth percentile).
- **Tests:** percentile math; the above-threshold flagging predicate.

### Trace diff — **L** (Tier 2 roadmap) — *needs a design checkpoint*
Compare two traces (typically a fast vs. slow instance of the same operation) and highlight the
diverging span / attribute.
- **Mechanism (sketch only):** a pure `TraceDiff.Compare(AssembledTrace, AssembledTrace)` returning a
  structural delta; rendering is the hard part and deferred.
- Flagged as design-first; do not start without a checkpoint.

### Error / exception spotlight — `:errors` — **S** — ✅ shipped 2026-05-31
The #1 reason someone opens a trace debugger is "something failed — what?"
- **As built:** pure `ErrorSpotlight.For(Span)` collects `exception.*` from the span's attributes
  **and** its OTel `exception` event, and (with the status message) renders a `*** ERROR ***` block
  prepended to the Details pane for an error/exception span (stacktrace truncated to 3 lines).
  `:errors` is a one-word `ErrorsCommand` that applies a `status=error` `TraceFilter` (reuses the
  shipped filter wiring; `:reset` clears).
- **Tests:** `ErrorSpotlightTests` (ok→empty, status message, exception attrs even when status unset,
  exception-event, stacktrace truncation); registry dispatch for `:errors` → `status=error` filter.

---

## § Tier C — ingest / fidelity / ops (lower day-1 value, wider reach)

### JSON OTLP — **M** (was #11) — *table stakes for non-.NET SDKs*
Many non-.NET SDKs default to OTLP/JSON on `:4318`; today a non-protobuf content-type returns `415`
at `OtlpReceiverExtensions.cs:57,159` (`IsProtobuf`), silently losing those users.
- **Plan:** branch on `application/json`, parse with the protobuf **JSON formatter** into the same
  `Export*ServiceRequest` types, then reuse the existing `OtlpMapper`
  (`src/Sentinel.CLI.Receiver/Telemetry/OtlpMapper.cs:22`) **unchanged**.
- **Tests:** a JSON-wire round-trip mirroring the existing protobuf end-to-end test.

### Span-count eviction option — **M** (was #12)
Evict by aggregate span count rather than trace count, for predictable memory under high
spans-per-trace loads. Touches `EvictTracesIfNeeded()` and `StoreOptions`.

### `--once` / snapshot-to-stdout — **S**
Headless: ingest for N seconds, print a trace summary (text or JSON) to stdout, exit — makes Sentinel
scriptable in CI without the TUI. Complements `--server`. Reuses the `:export` serialization; the
non-tty guard already exists (`TerminalGuard`, refuses the TUI when stdin/stdout is redirected).

### Receiver / host log file sink — **S** (was #14)
`Program.cs` calls `Logging.ClearProviders()` to protect the TUI, so warnings/errors currently go
nowhere. Route them to a file (or an in-app buffer) so they stay diagnosable. Also unblocks logging
for `--server` (see ADR-0006 open questions).

---

## § Tier D — navigation & live delight (independent of the core sequencing)

Lightweight, everyday-experience features for watching live traffic and moving around fast. None
depend on each other; all ride the shipped surface (the command registry, `TraceFilter`,
`PopulateAsync`'s refresh tick, `TraceSelection`, `TraceSummary`). Ordered by appeal-per-effort.

### Follow / tail mode — **S** — *highest bang-for-buck*
A toggle (`f` key or `:follow`) that auto-selects the **newest trace** every refresh tick — `tail -f`
for traces.
- **Mechanism:** a `bool _follow` field; in `MainWindow.PopulateAsync` (`MainWindow.cs:389`), branch —
  when following, set selection to index 0 instead of `TraceSelection.ResolveIndex(preserve)`. The
  preserve path already exists; this is its sibling branch. Add `Command.Follow` to `MapKey` (or a
  `:follow` `ITuiCommand`).
- **Reuse:** `TraceSelection`, the existing tick. ~20 lines.
- **Tests:** selection resolves to newest when follow is on, preserves when off (pure).
- **Gotcha:** auto-disable follow the instant the user navigates (↑/↓) so it doesn't fight them.

### Bell / flash on error-trace arrival — **S** — *ambient awareness*
When a **new** error trace lands between ticks, ring the terminal bell and briefly flash the status
bar — so you can work in another window and get pulled back.
- **Mechanism:** track the error-trace count (or max trace id) seen last tick; in `PopulateAsync`, if
  new error traces appeared, trigger an `Application` beep + a transient highlight via the existing
  `ShowCommandResult` result-echo path.
- **Reuse:** the snapshot already fetched each tick; the result-echo surface.
- **Tests:** pure "did new errors arrive" detection given prev/next snapshots.
- **Gotchas:** debounce (no beep on initial load or every tick); **opt-in** via `Tui:BellOnError`
  (default off — bells annoy) using the same `TuiOptions` binding pattern as `RefreshMs`.

### Mouse support — **S–M** — *modern, immediately satisfying*
Click a row in the trace list or a bar in the waterfall to select it; scroll-wheel to scroll lists.
- **Mechanism:** Terminal.Gui v2 raises mouse events and `ListView` already maps a click to
  `SelectedItem`; the existing selection-change handlers (`OnSpanSelectionChanged`) already drive
  population — so much of this is **verifying the events flow** once mouse reporting is on, plus
  wiring click→focus-the-pane and optionally double-click→`ToggleMaximize`.
- **Reuse:** the selection-change → populate pipeline; selection logic stays pure (`TraceSelection`).
- **Tests:** mostly a real-terminal pass (mouse is hard to unit-test headlessly); keep any new logic
  in pure helpers.
- **Gotcha:** confirm the global `KeyDown` interceptor / focus model doesn't swallow mouse focus; some
  terminals need mouse reporting explicitly enabled.

### Trace grouping / dedup — **M** — *fixes the firehose*
Collapse identical traces into one row with a count + latency spread:
`GET /api/orders ×42  p50 12ms p95 80ms  3 err`. Toggle via `:group`; drill into a group's members.
- **Mechanism:** a **pure** grouping function keyed on root service + root operation (+ status) over
  the `RecentAsync` snapshot, aggregating count / latency distribution / error tally. A grouped
  `IListDataSource` variant (sibling to `TraceListSource`), selected when grouping is on.
- **Reuse:** `TraceSummary` (root service/name/status/duration already computed), `ServiceColorMap`,
  and the **sparkline** component for the inline mini-distribution.
- **Tests:** pure grouping/aggregation over fixtures (count, percentiles, error tally).
- **Gotcha:** two-level selection (group vs. member) — simplest v1: selecting a group drills into its
  worst/newest member. *(The grouped data-source variant is the one notable design choice here —
  noted inline; not large enough for an ADR.)*

### Time-range filter / scrubber — **S → M** — ✅ light form shipped 2026-05-31
Show only traces from the last N minutes.
- **Light form — done.** `:filter since=5m` (units `ms`/`s`/`m`/`h`/`d`, parsed by a pure
  `DurationParse`). `TraceFilter` gained an optional `since` window; `Matches(Trace, status, now)`
  takes the instant as a parameter (captured once per refresh tick in `PopulateAsync`, injectable in
  tests) and excludes traces whose start is older than `now - since`. Composes with
  `service=`/`status=`/text and shows in the status-bar filter suffix for free. Tests:
  `DurationParseTests` + `TraceFilter` since-window matching with a fixed clock.
- **Delight form (M–L, not built):** an interactive visual scrubber (a time slider you arrow across).
  Only worth it if the filter form proves insufficient.

### Bonus — sticky log-stream scroll — **S–M** — *retires a known wart*
The global log-stream view (`5`) currently **yanks back to the top** every tick under live traffic
(documented as deferred in [`todo.md`](todo.md)). Add a sticky-scroll / newest-at-bottom tail
(pause-while-scrolled, resume-at-bottom) so older lines can be read without being snapped away.
- **Reuse:** the existing `LogPresenter.FormatWithService` global-stream rendering; only the
  scroll/selection retention on rebuild changes.
- This is net-removal of an annoyance, not new surface — cheap and squarely "live delight."

---

## § Tier E — detail-pane readability

The Details pane is the most-used view and the least polished: every attribute renders as one flat
`key = value` line, so SQL, JSON, and stacktraces are unreadable one-liners and there's no
at-a-glance summary of what a span *did*. This tier makes that pane pleasant to read. (Onboarding and
interop additions can join Tier E later; readability is first.)

### Pretty-print attribute values + semantic-convention summary — **M**
Format `db.statement` (SQL), JSON attribute values, and `exception.stacktrace` instead of flattening
them; and lead the pane with a one-line summary of what the span did (`HTTP  GET /api/orders → 200`,
`DB  postgresql  SELECT …`) derived from OTel semantic-convention attributes.
- **The two seams (both already exist):**
  - **Value formatting** rides the single chokepoint `AttributeText.Render(AttributeValue)` that
    every line in `RenderDetails` (`src/Sentinel.CLI.Tui/Views/MainWindow.cs:~1155–1234`) already
    calls. Add a **key-aware** pure formatter `AttributeFormatter.Render(string key, AttributeValue)`
    → possibly multi-line text:
    - `db.statement` / `db.query.text` → wrap/indent the SQL.
    - string values that **parse** as JSON (start `{`/`[` and `System.Text.Json` accepts them) →
      indent; otherwise return unchanged. Conservative + **total, never throws** (like the rest).
    - `exception.stacktrace` → split into indented lines (today it's one giant line; `ErrorSpotlight`
      only shows a 3-line head).
    - any over-long value → truncate with a `… (+N chars)` marker (static truncation in v1; an
      interactive "expand this attr" is a noted follow-up — it needs selection inside the `TextView`).
  - **Summary line** mirrors the shipped `ErrorSpotlight.For(Span)` pattern
    (`src/Sentinel.CLI.Tui/Views/ErrorSpotlight.cs`): a new pure `SemanticSummary.For(Span)` inspects
    `http.request.method`+`url.path`/`http.target`+`http.response.status_code`, `db.system`+
    `db.statement`, and `rpc.*`/`messaging.*`, returning an optional header line prepended in
    `RenderDetails` just like the spotlight block. No-convention spans → empty (unchanged output).
- **Reuse:** `RenderDetails` (single composition point), `AttributeText.Render` (wrap/extend it),
  the `ErrorSpotlight` prepend pattern, the existing `_detailsPinned` tick-guard (multi-line values
  grow the text but the `TextView` already scrolls — no conflict).
- **Tests (all pure → headless):** `AttributeFormatter` `[Theory]` — SQL key wraps, JSON string
  indents, non-JSON string is untouched, stacktrace splits to lines, long value truncates with the
  marker; `SemanticSummary.For` `[Theory]` — http→`GET /path → 200`, db→system+statement, rpc/
  messaging, no-convention→empty.
- **Gotcha (the Terminal.Gui-coupled part):** the Details pane is plain text (`_details.Text`), so
  **color** on the status code / summary (e.g. red 5xx) needs a colored renderer and is a follow-up;
  v1 is plain-text formatting + the summary line. The visual look is the only manual-tty step.

---

## § Theming — `:theme <name>` — **M** — ✅ shipped 2026-05-31

**As built:** `dark` (default) / `light` / `high-contrast` / `colorblind`. A `Theme` (base scheme
colors + service palette) + pure `Themes.Resolve` (case-insensitive, total → null, callers fall back
to Default). `MainWindow.ApplyTheme` builds a `Terminal.Gui.Drawing.Scheme` and `SetScheme`s the
window **and every pane** (set per-pane — not relying on cascade, which the reflection findings
suggested might be name-resolved) + rebuilds `ServiceColorMap` from the theme palette + re-sources
to repaint (preserving the selected trace). Both coloring paths adapt: `TraceListSource.Render` and
the waterfall/logs `RowRender` both read the live `Normal.Background`, so foregrounds sit on the new
bg for free. `Tui:Theme` config (lenient — bad name → default, does **not** throw). Headlessly
**proven**: a test asserts the scheme reaches the custom-source trace-list pane (`TraceListScheme`).
**Colorblind status colors done:** `Theme.StatusTokenColor` makes the trace-list OK/ERR token
theme-aware — colorblind uses blue(OK)/vermillion(ERR) to break the green/red trap; other themes
fall back to the shared `RowColors` default (additive, dark/light/high-contrast unchanged). The
trace-list status token is the only true green/red pair (the waterfall/logs use red-vs-varied + the
`!`/`#` fill, never red/green). The runtime visual *look* is the only manual-tty part.

_Original framing:_ accessibility, not decoration.

Frame this as accessibility; "pick a pretty color" would be gold-plating, but there is a real
readability bug to fix.
- **Scope:** swap Terminal.Gui's internal `ColorScheme` + the service palette. **Not** OSC escape
  sequences to repaint the host terminal — those are fragile and don't restore cleanly on exit, and
  fight Terminal.Gui for screen ownership.
- **The bug it fixes:** `ServiceColorMap` (`src/Sentinel.CLI.Tui/Views/ServiceColorMap.cs`) uses a
  muted RGB palette tuned for dark backgrounds — likely unreadable on a light terminal. And the
  OK/ERR green/red in `RowColors` (`RowColors.cs:25`) is the classic colorblind trap.
- **Palettes:** `dark` | `light` | `high-contrast` | `colorblind`. `:theme <name>` swaps the base
  scheme + a theme-aware `ServiceColorMap` variant, then redraws (`SetNeedsDisplay` / refresh tick).
- **Persistence:** add a `Theme` property to `TuiOptions` (`src/Sentinel.CLI.Tui/TuiOptions.cs`) using
  the same `.Bind(...).ValidateDataAnnotations()` pattern as `RefreshMs`, so `Tui:Theme` (env
  `Tui__Theme`) round-trips from config. Persisting a *runtime* `:theme` change is a separate
  follow-up — `IOptions<T>` is read-only from config sources, so it requires writing a user settings
  file. **Not v1.**
- **Tests:** pure theme→palette resolution; `ServiceColorMap` distinctness within each theme.

---

## § Recommended sequencing

1. **Ship-blockers** — icon / NU5046, lock files, `release.yml` dry-run. Nothing matters until
   `dotnet tool install -g sentinel` works. *(owner-gated)*
2. ✅ **Command bar** (§ 0, gated interceptor) + `:help` + `:clear` — **done 2026-05-31.** Surface
   proven with the cheapest commands. `:pause`/`:resume` still open.
3. ✅ **`:filter` / `:search`** — **done 2026-05-31.** First high-value customer; retired was-#6.
   `TraceFilter` (pure) + two thin `ITuiCommand`s + `CommandContext.SetFilter`.
4. **`:theme`** — accessibility palettes, persisted via config.
5. **`:export` → replay/import** — the bug-report / CI story.

---

## § Nice-to-have — command-bar UX polish (after Tier A / B / C)

Deliberately gated: build these **only once the command bar has real commands to complete and
recall** (Tier A/B/C shipped). They make an already-working bar pleasant; none of them block a
feature. All ride the surface shipped in § 0 (`CommandRegistry`, the gated key path, the command
`TextField`) — so each is mostly a pure helper plus a small bit of Terminal.Gui wiring.

### Inline command suggestion (ghost text + Tab to accept) — **S–M**
As the user types `:f` in the command bar, show the completion `ilter` in **dim/grey ghost text**
after the cursor; pressing **Tab** accepts it (the field becomes `:filter`). This is the
fish-shell / IntelliSense "ghost suffix" pattern, not a dropdown.
- **Suggestion source — pure, unit-testable.** The `CommandRegistry` already enumerates every verb.
  Add `CommandSuggest.Complete(string input, IReadOnlyList<string> verbs) -> string?` returning the
  ghost **suffix** (the part not yet typed), mirroring how `CommandLine.Parse` / `MapKey` are pure.
  Rules: match verbs by prefix on the verb token; one match → its suffix; multiple matches → the
  longest common prefix of the candidates (or `null` to stay quiet); no match → `null`.
- **Rendering — the Terminal.Gui-coupled part (the real cost).** The accepted suffix must **not** be
  part of `TextField.Text` (otherwise it would submit/parse). Two routes:
  - **Custom draw (matches the ask):** draw the ghost suffix right after the cursor in a dim
    `Attribute` from `DrawContent`, keeping it out of `Text`. Inline grey, exactly as described.
  - **Built-in fallback (lower effort):** Terminal.Gui's `TextField` autocomplete renders a
    **dropdown popup**, not inline ghost text — cheaper but a different look. Note the trade-off and
    pick during a short spike.
- **Tab acceptance.** Intercept Tab on the gated command-bar key path (the same seam as
  `MainWindow.HandleKey`): when a suggestion is showing, set `Text += suffix`, move the cursor to the
  end, and **consume** the key so Tab doesn't move focus; with no suggestion, let Tab behave normally.
- **Later:** extend `Complete` to suggest **argument keys** once the verb is done — e.g. after
  `:filter ` ghost `service=` / `status=` from the command's arg metadata (pairs with *inline
  argument hints* below).
- **Tests:** `CommandSuggest.Complete` `[Theory]` — `:f`→`ilter`, `:cl`→`ear`, `:x`→`null`, ambiguous
  prefix (`:s` with both `search`/`slow`) → common prefix or `null`. The ghost rendering + Tab
  consumption need a **real-terminal pass** (consistent with the rest of the TUI's verification debt).

### Command history (↑ / ↓ recall) — **S**
While the bar is open, `↑`/`↓` cycle previously entered command lines into the `TextField`. A small
in-memory ring (most-recent-first, capped ~50) owned by `MainWindow`; arrows handled on the same
gated key path and consumed so they don't move list selection. Pure `CommandHistory` (add/dedupe
consecutive/navigate index) is unit-testable; wiring is a few lines. No persistence in v1.

### Inline argument hints + pre-submit validation — **S–M**
Once a verb is complete, show a dim hint of its expected arguments (e.g. `:filter ▏service= status=
<text>`), and flag an unknown `key=` **before** execution rather than failing in the handler.
- Extend `ICommand` with an optional arg descriptor (allowed keys + a usage string); `:help` already
  reads `Help`, so this is the same metadata surfaced two ways.
- A pure `CommandValidate(ParsedCommand, descriptor) -> error?` feeds both the hint and a red
  `ShowCommandResult` on bad input. Unit-tested; rendering reuses the existing result-echo path.

---

## § Retained tail (still valid, unaffected by the thesis)

- **Resource-fingerprint service identity** — **L** (was #9, ADR-0002): identify producers by a
  stable hash over the normalized resource attribute set; group/color by it.
- **Live push subscription** — **L** (was #10): an additive `ILogSubscription` / trace push port so
  the UI updates event-driven instead of polling; also unblocks multi-window push.
- **Optional SQLite persistence** — **L** (was #16): survive restarts and reload past sessions.
- **Multi-process viewer split** — **L** (was #17): headless `--server` + per-signal viewer
  processes over a loopback gRPC query API. Server side runs today; the query-DTO layer is the
  remaining blocker. See
  [`adr-0006`](docs/architecture/adr/adr-0006-multi-window-client-server.md).
- **Headless TUI smoke test** — **M** (was #18): drive Terminal.Gui via `FakeDriver` + scripted input
  in CI to verify auto-refresh / view-switching without a real terminal.
- **Mapper fidelity golden fixture** — **S** (was #19): capture one real OTel-SDK OTLP payload as a
  binary fixture and assert the mapper decodes it as expected.
