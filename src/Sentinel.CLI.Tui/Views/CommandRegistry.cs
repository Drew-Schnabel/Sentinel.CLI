using Sentinel.CLI.Application.Telemetry.Ports;

namespace Sentinel.CLI.Tui.Views;

// Where a command's message should be shown. Status = the one-line bottom bar (overwritten on the
// next refresh tick — fine for short confirmations/errors). Details = the multi-line Details pane,
// pinned so the refresh tick won't clobber it (used by `:help`).
internal enum CommandOutput
{
    Status,
    Details,
}

// The outcome of running a command: a message to echo and where to show it. Success is carried for
// future use (e.g. distinct error styling); today both paths render plainly.
internal sealed record CommandResult(bool Success, string Message, CommandOutput Output = CommandOutput.Status)
{
    public static CommandResult Ok(string message, CommandOutput output = CommandOutput.Status)
        => new(true, message, output);

    public static CommandResult Error(string message)
        => new(false, message, CommandOutput.Status);
}

// Everything a command needs to act, kept off the View so commands stay unit-testable: the store
// control surface, the registry's command list (for `:help`), and a sink for the active trace-list
// filter (`:filter`/`:search`; null clears).
internal sealed class CommandContext
{
    public CommandContext(
        IStoreControl storeControl,
        IReadOnlyList<ITuiCommand> commands,
        Action<TraceFilter?> setFilter,
        Func<string, CommandResult> export,
        Action<Theme> setTheme,
        Func<CommandResult> diagnose)
    {
        StoreControl = storeControl;
        Commands = commands;
        SetFilter = setFilter;
        Export = export;
        SetTheme = setTheme;
        Diagnose = diagnose;
    }

    public IStoreControl StoreControl { get; }
    public IReadOnlyList<ITuiCommand> Commands { get; }
    public Action<TraceFilter?> SetFilter { get; }

    // Export the currently selected trace + its logs to the given path; returns the result to echo.
    public Func<string, CommandResult> Export { get; }

    // Apply a color theme at runtime.
    public Action<Theme> SetTheme { get; }

    // Diagnose the currently selected trace; returns the findings to show (Details output).
    public Func<CommandResult> Diagnose { get; }
}

// One verb's behavior. Each command is a small class — this is the extensibility surface every
// future bar feature (:filter, :export, …) plugs into.
internal interface ITuiCommand
{
    string Verb { get; }
    string Help { get; }
    CommandResult Execute(ParsedCommand command, CommandContext context);
}

// Parses input, looks up the verb, and dispatches. Total: blank input is a silent no-op and an
// unknown verb returns an error result rather than throwing.
internal sealed class CommandRegistry
{
    private readonly IReadOnlyList<ITuiCommand> _commands;
    private readonly Dictionary<string, ITuiCommand> _byVerb;
    private readonly IStoreControl _storeControl;
    private readonly Action<TraceFilter?> _setFilter;
    private readonly Func<string, CommandResult> _export;
    private readonly Action<Theme> _setTheme;
    private readonly Func<CommandResult> _diagnose;

    public CommandRegistry(
        IStoreControl storeControl,
        Action<TraceFilter?> setFilter,
        Func<string, CommandResult>? export = null,
        Action<Theme>? setTheme = null,
        Func<CommandResult>? diagnose = null,
        IEnumerable<ITuiCommand>? commands = null)
    {
        ArgumentNullException.ThrowIfNull(storeControl);
        ArgumentNullException.ThrowIfNull(setFilter);
        _storeControl = storeControl;
        _setFilter = setFilter;
        _export = export ?? (_ => CommandResult.Error("export is unavailable"));
        _setTheme = setTheme ?? (_ => { });
        _diagnose = diagnose ?? (() => CommandResult.Error("no trace selected"));
        _commands = (commands ?? DefaultCommands()).ToList();
        _byVerb = _commands.ToDictionary(c => c.Verb, StringComparer.Ordinal);
    }

    public IReadOnlyList<ITuiCommand> Commands => _commands;

    public CommandResult Dispatch(string? input)
    {
        var parsed = CommandLine.Parse(input);
        if (parsed is null)
        {
            return CommandResult.Ok(string.Empty); // blank input — no echo
        }
        if (!_byVerb.TryGetValue(parsed.Verb, out var command))
        {
            return CommandResult.Error($"unknown command '{parsed.Verb}' — try :help");
        }
        return command.Execute(parsed, new CommandContext(_storeControl, _commands, _setFilter, _export, _setTheme, _diagnose));
    }

    private static IEnumerable<ITuiCommand> DefaultCommands() =>
    [
        new HelpCommand(), new ClearCommand(),
        new FilterCommand(), new SearchCommand(), new ResetCommand(),
        new PauseCommand(), new ResumeCommand(),
        new ExportCommand(), new ThemeCommand(), new CapacityCommand(),
        new ErrorsCommand(), new DoctorCommand(),
    ];
}
