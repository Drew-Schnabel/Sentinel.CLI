# ADR-0004: Terminal.Gui v2 as the TUI Shell

## Status
Accepted

## Context

Sentinel.CLI is distributed as a `dotnet tool install -g` global tool. The TUI must run in a standard terminal on Windows (Windows Terminal, cmd, PowerShell), macOS (Terminal.app, iTerm2), and Linux (gnome-terminal, xterm, SSH sessions). The UI must support: a multi-pane layout (trace list, waterfall, details), keyboard navigation (Tab, arrow keys, Enter), and a read-only text area for attribute/log display. The tool has no web server and no browser dependency.

Options evaluated:

**Terminal.Gui v2 (chosen).** A .NET-native, cross-platform TUI framework. Provides `ListView`, `FrameView`, `Window`, layout primitives (`Dim.Percent`, `Pos.Right`), and a main-loop/event model (`IApplication.Invoke` for thread marshaling). v2 is a significant rewrite of v1 with breaking API changes. Version 2.4.3 is declared in CPM and a working spike with fixtures is already in `Sentinel.CLI.Tui`. `PackAsTool` with Terminal.Gui compiles to a self-contained NuGet tool package with no native dependencies beyond the .NET runtime.

**Spectre.Console.** Excellent for one-shot CLI rendering (tables, progress bars, markup). Does not provide a reactive, event-driven multi-pane TUI with keyboard focus management. Rejected.

**Custom ANSI escape sequences.** Full control; maximum portability. Prohibitive implementation cost for a first version. Rejected.

**Ink / React-based CLIs (Node.js).** Wrong runtime for a .NET tool. Rejected.

**Blazor Hybrid / MAUI.** Requires a window manager or browser. Incompatible with the `dotnet tool` distribution model. Rejected.

The spike validation confirmed that Terminal.Gui v2 can render the three-pane layout, respond to keyboard events, and marshal UI updates from background threads via `IApplication.Invoke()`. The spike is in `src/Sentinel.CLI.Tui/`.

## Decision

Terminal.Gui v2.4.3 is the TUI shell. All UI code lives in `Sentinel.CLI.Tui`. No other UI framework is used or evaluated for v0.

## Alternatives Considered

See Context above. No alternative met all three constraints simultaneously: .NET native, `dotnet tool`-compatible, multi-pane reactive layout.

## Consequences

**Easier:** the TUI spike is already working; the layout and event model are validated. `PackAsTool = true` + Terminal.Gui produces a single `dotnet tool install -g` artifact with no Docker or native dependency.

**Harder:** Terminal.Gui v2's API surface is partly unstable. `TextView` is marked obsolete in v2.4 in favor of an external `Editor` package that is not yet stable for read-only use. The current usage has `#pragma warning disable CS0618` in `MainWindow.cs` and is acceptable for a read-only details pane. This must be revisited when Terminal.Gui 2.5+ is released.

**New risks introduced:**
- Terminal.Gui v2 is an active rewrite; minor version upgrades may carry breaking changes. CPM pins the version; upgrades require explicit opt-in and re-validation of the spike.
- The `IApplication.Invoke()` threading contract is the only safe way to update UI state from background threads (store consumer, OTLP receiver). All non-UI-thread updates must go through this call site. Violating this causes silent rendering corruption. This is documented in `MainWindow` and must be enforced in code review.
- Color support depends on the terminal emulator. The waterfall bar (`#`, `!`) uses ASCII, not Unicode block characters, for maximum compatibility. Color differentiation by `SpanStatusCode` uses Terminal.Gui `ColorScheme`, which degrades gracefully to monochrome.

**Follow-on decisions opened:**
- When Terminal.Gui 2.5+ ships, evaluate the `Editor` package for the details pane and remove the `CS0618` suppression.
- Color-coding spans by service (Phase 3+) requires a `ColorScheme` allocation strategy. Terminal.Gui v2's color model supports 16 named colors; the number of distinct services visible simultaneously is bounded by screen height, so a fixed palette of 8 cycling colors is sufficient.
