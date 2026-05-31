using System.Globalization;

namespace Sentinel.CLI.Tui.Views;

// `:capacity <n>` — resize the trace ring buffer live. Shrinking evicts the oldest traces
// immediately; growing just raises the cap. No args reports the current capacity.
internal sealed class CapacityCommand : ITuiCommand
{
    private const int Min = 1;
    private const int Max = 100_000; // matches StoreOptions.MaxTraces [Range]

    public string Verb => "capacity";
    public string Help => $"set the max retained traces ({Min}-{Max}); shrinking evicts oldest now";

    public CommandResult Execute(ParsedCommand command, CommandContext context)
    {
        if (command.Positionals.Count == 0)
        {
            return CommandResult.Error(
                $"usage: capacity <{Min}-{Max}> (currently {context.StoreControl.TraceCapacity})");
        }

        if (!int.TryParse(command.Positionals[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var n))
        {
            return CommandResult.Error($"capacity must be a whole number ({Min}-{Max})");
        }
        if (n < Min || n > Max)
        {
            return CommandResult.Error($"capacity must be between {Min} and {Max}");
        }

        context.StoreControl.SetTraceCapacity(n);
        return CommandResult.Ok($"trace capacity: {n}");
    }
}
