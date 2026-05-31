namespace Sentinel.CLI.Tui.Views;

// A parsed command line: a verb plus positional arguments and key=value options.
// Verb is lower-cased for case-insensitive lookup; positionals/options keep their casing.
internal sealed record ParsedCommand(
    string Verb,
    IReadOnlyList<string> Positionals,
    IReadOnlyDictionary<string, string> Options);

// Pure, total parser for the `:`-style command bar. Never throws; returns null for blank input.
// Grammar: `verb [positional ...] [key=value ...]`. Mirrors MapKey — a pure helper unit-tested
// without the TUI. A leading `:` is tolerated (the bar opens without inserting it, but pasted or
// scripted input may include it).
internal static class CommandLine
{
    public static ParsedCommand? Parse(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return null;
        }

        var text = input.Trim();
        if (text.StartsWith(':'))
        {
            text = text[1..].TrimStart();
        }

        var tokens = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length == 0)
        {
            return null;
        }

        var verb = tokens[0].ToLowerInvariant();
        var positionals = new List<string>();
        var options = new Dictionary<string, string>(StringComparer.Ordinal);

        for (var i = 1; i < tokens.Length; i++)
        {
            var token = tokens[i];
            var eq = token.IndexOf('=', StringComparison.Ordinal);
            if (eq > 0) // key must be non-empty; "=foo" is treated as a positional
            {
                options[token[..eq]] = token[(eq + 1)..];
            }
            else
            {
                positionals.Add(token);
            }
        }

        return new ParsedCommand(verb, positionals, options);
    }
}
