namespace Sentinel.CLI.Tui.Views;

// `:theme <name>` — switch the color theme at runtime. Resolves the name (pure) and hands the theme
// to the host to apply; an unknown name lists the valid options rather than changing anything.
internal sealed class ThemeCommand : ITuiCommand
{
    public string Verb => "theme";
    public string Help => $"switch color theme: {Themes.Names}";

    public CommandResult Execute(ParsedCommand command, CommandContext context)
    {
        if (command.Positionals.Count == 0)
        {
            return CommandResult.Error($"usage: theme <name> — one of: {Themes.Names}");
        }

        var name = command.Positionals[0];
        if (Themes.Resolve(name) is not { } theme)
        {
            return CommandResult.Error($"unknown theme '{name}' — try one of: {Themes.Names}");
        }

        context.SetTheme(theme);
        return CommandResult.Ok($"theme: {theme.Name.ToString().ToLowerInvariant()}");
    }
}
