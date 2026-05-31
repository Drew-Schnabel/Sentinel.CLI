namespace Sentinel.CLI.Tui.Views;

// `:pause` — freeze ingest so the view holds still while you read a trace. Telemetry received
// while paused is dropped (not buffered), so the list won't jump when you resume.
internal sealed class PauseCommand : ITuiCommand
{
    public string Verb => "pause";
    public string Help => "freeze ingest so the view holds still (:resume to continue)";

    public CommandResult Execute(ParsedCommand command, CommandContext context)
    {
        context.StoreControl.SetPaused(true);
        return CommandResult.Ok("ingest paused — :resume to continue");
    }
}

// `:resume` — resume accepting telemetry after `:pause`.
internal sealed class ResumeCommand : ITuiCommand
{
    public string Verb => "resume";
    public string Help => "resume ingest after :pause";

    public CommandResult Execute(ParsedCommand command, CommandContext context)
    {
        context.StoreControl.SetPaused(false);
        return CommandResult.Ok("ingest resumed");
    }
}
