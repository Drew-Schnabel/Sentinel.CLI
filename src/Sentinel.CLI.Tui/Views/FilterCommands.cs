namespace Sentinel.CLI.Tui.Views;

// `:filter` — narrow the trace list by `service=`, `status=`, and/or free-text terms (matched
// across every span's service and name). `:filter` with no args clears the filter.
internal sealed class FilterCommand : ITuiCommand
{
    public string Verb => "filter";
    public string Help => "filter the trace list: service=… status=ok|error|unset and/or text (no args clears)";

    public CommandResult Execute(ParsedCommand command, CommandContext context)
    {
        var service = command.Options.TryGetValue("service", out var s) ? s : null;
        var status = command.Options.TryGetValue("status", out var st) ? st : null;
        var since = command.Options.TryGetValue("since", out var sn) ? sn : null;
        return FilterSupport.Apply(service, status, command.Positionals, context, since);
    }
}

// `:search <text>` — free-text shorthand for `:filter` with only terms. `:search` with no args
// clears. The parser splits `k=v` tokens into Options, but for a free-text search those are still
// just text the user typed, so fold them back into the terms (otherwise `:search service=x` would
// surprisingly *clear* the filter rather than search for that text).
internal sealed class SearchCommand : ITuiCommand
{
    public string Verb => "search";
    public string Help => "free-text search across services and span names (no args clears)";

    public CommandResult Execute(ParsedCommand command, CommandContext context)
    {
        var terms = command.Positionals
            .Concat(command.Options.Select(o => $"{o.Key}={o.Value}"))
            .ToList();
        return FilterSupport.Apply(service: null, statusText: null, terms, context);
    }
}

// `:reset` — clear the active filter/search, keeping all telemetry (unlike `:clear`, which drops
// it). Equivalent to `:filter` with no args, but an explicit, discoverable verb.
internal sealed class ResetCommand : ITuiCommand
{
    public string Verb => "reset";
    public string Help => "clear the active filter/search (keeps all telemetry — unlike :clear)";

    public CommandResult Execute(ParsedCommand command, CommandContext context)
    {
        context.SetFilter(null);
        return CommandResult.Ok("filter cleared");
    }
}

// Shared build-and-apply for the two filter verbs: parse into a TraceFilter, surface a parse error,
// otherwise push it to the host (null clears) and echo the active expression.
internal static class FilterSupport
{
    public static CommandResult Apply(
        string? service, string? statusText, IReadOnlyList<string> terms, CommandContext context,
        string? sinceText = null)
    {
        var (filter, error) = TraceFilter.Create(service, statusText, terms, sinceText);
        if (error is not null)
        {
            return CommandResult.Error(error);
        }

        context.SetFilter(filter);
        return CommandResult.Ok(filter is null ? "filter cleared" : $"filter: {filter.Expression}");
    }
}
