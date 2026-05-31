namespace Sentinel.CLI.Tui.Views;

// `:help` — list every registered verb and its one-line description. Output goes to the Details
// pane (multi-line, pinned) rather than the one-line status bar. Self-describing via the registry,
// so new commands appear here for free.
internal sealed class HelpCommand : ITuiCommand
{
    public string Verb => "help";
    public string Help => "list available commands";

    public CommandResult Execute(ParsedCommand command, CommandContext context)
    {
        var lines = context.Commands
            .OrderBy(c => c.Verb, StringComparer.Ordinal)
            .Select(c => $"  :{c.Verb,-10}{c.Help}");
        var body = "Commands  (Esc to close · 0 for combined view)" + Environment.NewLine
            + string.Join(Environment.NewLine, lines);
        return CommandResult.Ok(body, CommandOutput.Details);
    }
}
