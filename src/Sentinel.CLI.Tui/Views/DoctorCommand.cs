namespace Sentinel.CLI.Tui.Views;

// `:doctor` — check the selected trace for common instrumentation problems (broken context
// propagation, clock skew, exception-without-error-status, missing service.name) and list the
// findings in the Details pane. The analysis lives in the host (it owns the loaded trace).
internal sealed class DoctorCommand : ITuiCommand
{
    public string Verb => "doctor";
    public string Help => "check the selected trace for instrumentation problems";

    public CommandResult Execute(ParsedCommand command, CommandContext context) => context.Diagnose();
}
