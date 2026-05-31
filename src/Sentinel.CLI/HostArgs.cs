namespace Sentinel.CLI;

// The bare mode flags (`--server`, `--demo`) are read directly off the raw args, but the SAME args
// feed WebApplication's command-line *configuration* provider. That provider pairs a valueless
// "--flag" with the following token as its value — so `--server --Receiver:GrpcPort=4319` would
// bind "server" = "--Receiver:GrpcPort=4319" and silently drop the port. Stripping the mode flags
// before the args reach the config provider lets `--Receiver:...` keys work in any order.
internal static class HostArgs
{
    private static readonly string[] ModeFlags = ["--server", "--demo"];

    public static bool Has(string[] args, string flag)
    {
        ArgumentNullException.ThrowIfNull(args);
        return args.Any(a => string.Equals(a, flag, StringComparison.OrdinalIgnoreCase));
    }

    // The args with the bare mode flags removed — safe to hand to the command-line config provider.
    public static string[] WithoutModeFlags(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);
        return args
            .Where(a => !ModeFlags.Any(f => string.Equals(a, f, StringComparison.OrdinalIgnoreCase)))
            .ToArray();
    }
}
