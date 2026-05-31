namespace Sentinel.CLI.Tui.Views;

// `:export <path>` — write the currently selected trace (its assembled spans + correlated logs)
// to a JSON file for bug reports / sharing. The actual write is delegated to the host (it owns the
// loaded trace state); this command just validates the path argument.
internal sealed class ExportCommand : ITuiCommand
{
    public string Verb => "export";
    public string Help => "export the selected trace + logs to a JSON file: export <path>";

    public CommandResult Execute(ParsedCommand command, CommandContext context)
    {
        if (command.Positionals.Count == 0)
        {
            return CommandResult.Error("usage: export <path>");
        }
        return context.Export(command.Positionals[0]);
    }
}
