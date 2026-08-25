namespace Diagnyx.Core.Logging;

internal static class LogLevel
{
    private static readonly HashSet<string> ValidLevels =
        new(["debug", "info", "warn", "error", "fatal"], StringComparer.OrdinalIgnoreCase);

    public static bool IsValid(string level) => ValidLevels.Contains(level);
}
