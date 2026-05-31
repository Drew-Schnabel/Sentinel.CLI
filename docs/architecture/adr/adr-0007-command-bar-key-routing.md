# ADR-0007 — Command-bar key routing

**Status:** Proposed
**Date:** 2026-05-31

## Context

We want a `:`-style command bar in the TUI (see `Features.md` § 0) — one input surface that hosts
search/filter, pause/clear, export, theming, and future verbs, instead of spending a scarce
single-letter keybinding per feature. Building it runs straight into how the TUI already handles
keys.

`MainWindow.BindKeys()` (`src/Sentinel.CLI.Tui/Views/MainWindow.cs:135`) subscribes to the global
`TGuiApp.Keyboard.KeyDown` event, runs the pure `MapKey(key)` (`:147`), and on a match calls
`InvokeCommand` and sets `key.Handled = true`. This was a deliberate fix, not an accident: the
focused `ListView` consumes printable chars as **type-ahead** before any view-level binding can see
them (documented in `todo.md`). Intercepting at the raw application event — ahead of the focused
view — is the only place shortcuts like `r`, `e`, and the digit keys reliably fire.

A command bar needs the opposite behavior for the duration of editing: the printable chars the user
types (`f`, `i`, `l`, …) must reach a `TextField`, not be swallowed by the global interceptor and
marked `Handled`. So the bar does **not** sidestep the type-ahead problem — it collides with the
existing fix. We must decide how the command bar's text entry coexists with the global key
interceptor.

## Decision

**Gate the global interceptor on a `_commandBarOpen` flag (chosen).**

- Add a `bool _commandBarOpen` field to `MainWindow`.
- The first line of the `KeyDown` handler (`MainWindow.cs:137`) becomes:
  `if (_commandBarOpen) return;` — crucially, it returns **without** setting `key.Handled`, so the
  keystroke falls through to the focused `TextField`.
- Pressing `:` (intercepted while the bar is closed) shows the command `TextField`, focuses it, and
  sets `_commandBarOpen = true`.
- `Enter` submits the line to the parser/registry; `Esc` cancels. Both hide the field and set
  `_commandBarOpen = false`, restoring normal shortcut routing.

Rejected alternative — **modal `Application.Run(dialog)`**: host the command bar as a Terminal.Gui
modal that runs its own nested run loop, which captures keys in its own context and naturally
bypasses `MainWindow`'s handler. It is more idiomatic Terminal.Gui, but: (a) it is heavier; (b) a
modal layered over a TUI that live-refreshes on a ~1s timer (`TuiRunner.cs:61`) needs extra care so
the timer and the nested loop don't fight over the screen; and (c) the gated-flag approach is a much
smaller, more testable delta against code that already works. The modal remains a fallback if the
gated flag proves leaky in practice.

## Consequences

- The `_commandBarOpen` early-return is **load-bearing**: it must run before any `MapKey`/`Handled`
  logic, and it must not set `key.Handled`. A regression here silently breaks command-bar typing —
  worth a comment at the call site and a dedicated unit test.
- The gate is trivially unit-testable headless (the same style as `MainWindowKeyTests.cs`): with the
  flag set, keys are not intercepted; with it clear, they are.
- `MapKey` stays pure and unchanged; only the handler that consults it gains the guard.
- Submitting the line is decoupled from key routing — the parser
  (`CommandLine.Parse`) and the command registry are independently testable and don't depend on a
  live `Application`.

## Open questions

- **Focus restoration:** after `Esc`/`Enter`, focus must return to the pane that had it before `:` —
  confirm Terminal.Gui restores the prior focused view or track it explicitly.
- **Refresh during editing:** the ~1s refresh timer keeps firing while the bar is open; ensure
  `PopulateAsync`'s UI updates don't steal focus from the command `TextField` or overwrite its
  contents (it writes `_statusLabel`, a sibling of the bar).
- **`:` as literal input:** once the bar is open, `:` should be an ordinary character in the
  `TextField`, not a re-open — the gate handles this (interceptor returns early), but verify on a
  real terminal.
- **Result echo vs. status counts:** `ShowCommandResult` and `StatusLine.Format` share `_statusLabel`
  — decide the precedence/duration so a command result isn't instantly overwritten by the next tick.
