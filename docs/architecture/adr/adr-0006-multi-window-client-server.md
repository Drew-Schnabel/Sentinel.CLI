# ADR-0006 — Multi-window via a client/server split

**Status:** Proposed (spike done; viewer PoC not yet built)
**Date:** 2026-05-31

## Context

Users want to view different signals (traces, logs, metrics) in **separate OS terminal windows**.
A terminal app can't open multiple OS windows from one process, and Sentinel today is a single
process where the in-memory store, the OTLP receiver, and the Terminal.Gui UI all co-exist. Two
hard constraints:

- The OTLP ingest ports (`4317`/`4318`) are **exclusive** — only one process can bind them.
- The store is an **in-process singleton**; domain types (`Trace`, `Span`, `MetricPoint`) are not
  wire types.

So "multiple windows" cannot be "run the exe N times" — it requires splitting ingest/storage
(one **server**) from viewing (N **viewer** processes, each its own terminal).

## Decision (target architecture)

- **Server**: one headless process owns the receiver + store(s) and exposes a **read-only query
  API over loopback gRPC** (reusing the Kestrel + Grpc.AspNetCore already present; a query service
  mapped on the gRPC endpoint). Chosen over named pipes because the gRPC/protobuf/codegen
  toolchain is already in the solution and tested.
- **Viewers**: lightweight console processes, each rendering one signal (its own terminal window),
  polling the server's query API (later: streaming).

## What the spike built (verified)

- A `--server` flag (`Program.cs`): skips the `TerminalGuard` non-tty refusal and the
  `TuiRunner.Run()` blocking call, and instead awaits a SIGINT/SIGTERM shutdown. **Verified at
  runtime**: `sentinel --server` runs headless, binds `4317`/`4318`, and stays up with no TUI.

This closes the harder of the two blockers (headless host mode). The receiver→store path is
already real and tested, so the server side is essentially functional today as a headless collector.

## The remaining blocker (not yet built)

**Domain types aren't wire types.** Viewers need DTOs + a domain→DTO mapping layer — the *opposite*
direction from the OTLP ingest `OtlpMapper`. The viewer PoC is:

1. `Receiver/Protos/sentinel/query/v1/query.proto` — a `Query` service:
   `rpc RecentTraces(RecentTracesRequest{int32 limit}) returns (TraceSummaries{repeated TraceSummary})`
   where `TraceSummary { string trace_id; string root_name; string root_service; int32 span_count;
   double duration_ms; string status; fixed64 started_at_unix_nano; }` (the fields the TUI's
   `TraceSummary` already computes).
2. `Receiver/Query/QueryService.cs` (+ `QueryDtoMapper.cs`) — backed by `ITraceQueries`, mapping
   each `Trace` to the DTO (the same root/count/status logic as `TraceSummary.FromTrace`, which
   should move to a shared place to avoid a third copy).
3. `tools/Sentinel.Viewer/` — a console (mirrors `tools/Sentinel.LoadGenerator`) that connects to
   `localhost:4317` over h2c and lists recent trace summaries.

## Open questions for the full build

- **Push vs poll**: the PoC polls (like the 1s TUI timer); live multi-viewer wants gRPC streaming
  / the deferred `ILogSubscription` push port (Features #10). Confirm the proto can evolve to
  streaming without a breaking change.
- **Lifecycle / discovery**: how viewers find the query port; cleanup of orphaned viewers when the
  server exits.
- **Snapshot cost at the boundary**: whole-trace snapshots are fine for summary tables, costly for
  a high-rate cross-process log tail or per-series metric history.
- **Store ownership**: one store vs. one-per-signal; the PoC keeps today's singletons.
- **Logging**: `--server` currently inherits `Logging.ClearProviders()` (added to protect the TUI),
  so a headless server has no logs — wire a file/console sink for server mode (Features #14).

## Consequences

- Server side is reachable now (`--server`); the viewer round-trip is designed but unbuilt.
- A query-DTO + transport surface is net-new code to maintain alongside the OTLP ingest surface.
- In-app multiple Terminal.Gui windows (one process, one screen) were rejected — they are not
  separate OS windows, which is what was asked.
