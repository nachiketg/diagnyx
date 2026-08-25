using System.Text.Json;

namespace Diagnyx.Core.Config;

internal static class ConfigLoader
{
    public static DiagnyxConfig Load()
    {
        var path = ResolveConfigPath();
        if (path is null || !File.Exists(path))
            return new DiagnyxConfig();

        try
        {
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize(json, ConfigJsonContext.Default.DiagnyxConfig)
                   ?? new DiagnyxConfig();
        }
        catch
        {
            return new DiagnyxConfig();
        }
    }

    private static string? ResolveConfigPath()
    {
        var envPath = Environment.GetEnvironmentVariable("DIAGNYX_CONFIG");
        if (!string.IsNullOrWhiteSpace(envPath))
            return envPath;

        var localPath = Path.Combine(Directory.GetCurrentDirectory(), "diagnyx.config.json");
        if (File.Exists(localPath))
            return localPath;

        var homePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".diagnyx", "diagnyx.config.json");
        if (File.Exists(homePath))
            return homePath;

        return null;
    }

    public static string ExpandPath(string path)
    {
        if (path.StartsWith("~/") || path == "~")
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return Path.Combine(home, path[2..]);
        }
        return path;
    }
}
