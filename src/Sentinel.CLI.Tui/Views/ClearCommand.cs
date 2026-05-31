namespace Sentinel.CLI.Tui.Views;

// `:clear` — drop every received trace, log, and metric so you can start a clean session.
// Clears both stores via the IStoreControl composite; the next refresh tick repaints empty panes.
internal sealed class ClearCommand : ITuiCommand
{
    public string Verb => "clear";
    public string Help => "drop all received traces, logs, and metrics";

    public CommandResult Execute(ParsedCommand command, CommandContext context)
    {
        context.StoreControl.Clear();
        return CommandResult.Ok("cleared — all telemetry dropped");
    }
}
