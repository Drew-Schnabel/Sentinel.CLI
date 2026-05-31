namespace Sentinel.CLI.Tui.Views;

// `:errors` — filter the trace list to error traces only. A one-word shortcut for
// `:filter status=error`, built on the same TraceFilter wiring; `:reset` clears it.
internal sealed class ErrorsCommand : ITuiCommand
{
    public string Verb => "errors";
    public string Help => "show only error traces (shortcut for filter status=error)";

    public CommandResult Execute(ParsedCommand command, CommandContext context)
    {
        var (filter, error) = TraceFilter.Create(service: null, statusText: "error", terms: []);
        if (error is not null)
        {
            return CommandResult.Error(error); // unreachable — "error" is a valid status
        }
        context.SetFilter(filter);
        return CommandResult.Ok("filter: status=error");
    }
}
