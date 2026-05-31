namespace Sentinel.CLI.Tui;

public static class TerminalGuard
{
    // Terminal.Gui needs a real console for both rendering and key input. When stdout or
    // stdin is redirected (a pipe, a file, a non-tty CI step), starting the driver garbles
    // output or throws deep inside it — so the host refuses to launch the TUI instead.
    public static bool IsInteractive(bool outputRedirected, bool inputRedirected)
        => !outputRedirected && !inputRedirected;
}
