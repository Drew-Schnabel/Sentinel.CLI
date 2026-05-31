namespace Sentinel.CLI.Tui.Views;

internal static class ErrorNavigation
{
    // Nearest error at a lower index than `current` — i.e. the next *newer* error in a
    // newest-first list. Scans upward toward index 0 and stops (returns -1) if there is no
    // error above `current`: one-directional, no wrap, so the jump is always predictable.
    public static int NewerErrorIndex(IReadOnlyList<bool> isError, int current)
    {
        ArgumentNullException.ThrowIfNull(isError);
        for (var i = current - 1; i >= 0 && i < isError.Count; i--)
        {
            if (isError[i])
            {
                return i;
            }
        }
        return -1;
    }
}
