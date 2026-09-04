using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Diagnyx;

internal sealed class CliRunner
{
    private readonly string? _explicitBinaryPath;

    internal CliRunner(string? binaryPath)
    {
        _explicitBinaryPath = binaryPath;
    }

    /// <summary>
    /// Resolves the diagnyx binary and invokes it. Resolution and process-start
    /// failures are caught and reported as a console warning rather than thrown,
    /// so a missing or broken CLI never crashes the host application.
    /// </summary>
    internal int Run(string[] args)
    {
        string binaryPath;
        try
        {
            binaryPath = ResolveBinaryPath();
        }
        catch (InvalidOperationException ex)
        {
            Warn(ex.Message);
            return 1;
        }

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = binaryPath,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };

            foreach (var arg in args)
                psi.ArgumentList.Add(arg);

            using var proc = Process.Start(psi)
                ?? throw new InvalidOperationException($"failed to start process: {binaryPath}");

            // Read stderr before WaitForExit to avoid deadlock when only stderr is redirected.
            var stderr = proc.StandardError.ReadToEnd();
            proc.WaitForExit();

            if (proc.ExitCode != 0 && !string.IsNullOrWhiteSpace(stderr))
                Console.Error.WriteLine(stderr.TrimEnd());

            return proc.ExitCode;
        }
        catch (Exception ex)
        {
            Warn($"failed to invoke diagnyx CLI: {ex.Message}");
            return 1;
        }
    }

    private string ResolveBinaryPath() =>
        _explicitBinaryPath
        ?? Environment.GetEnvironmentVariable("DIAGNYX_PATH")
        ?? FindBundled()
        ?? FindInPath()
        ?? throw new InvalidOperationException(
            "diagnyx binary not found. " +
            "Install it from https://github.com/nachiketg/diagnyx/releases " +
            "or set the DIAGNYX_PATH environment variable to its path.");

    private static void Warn(string message) =>
        Console.Error.WriteLine($"diagnyx: warning: {message.TrimEnd('.')}. Log entry dropped.");

    private static readonly string BinaryFileName =
        RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "diagnyx.exe" : "diagnyx";

    /// <summary>
    /// Looks for the diagnyx binary shipped inside the NuGet package's
    /// "runtimes/{rid}/native/" folder, which NuGet copies next to the
    /// consuming app's own output at build time. Only the RIDs Diagnyx
    /// ships (win-x64, linux-x64, osx-arm64) are probed; other platforms
    /// fall through to <see cref="FindInPath"/>. Packed as "diagnyx.bin"
    /// rather than the CLI's real name -- see Diagnyx.Client.csproj for why.
    /// </summary>
    private static string? FindBundled()
    {
        var rid = CurrentRid();
        if (rid is null)
            return null;

        var candidate = Path.Combine(AppContext.BaseDirectory, "runtimes", rid, "native", "diagnyx.bin");
        return File.Exists(candidate) ? candidate : null;
    }

    private static string? CurrentRid()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows) && RuntimeInformation.OSArchitecture == Architecture.X64)
            return "win-x64";
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux) && RuntimeInformation.OSArchitecture == Architecture.X64)
            return "linux-x64";
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX) && RuntimeInformation.OSArchitecture == Architecture.Arm64)
            return "osx-arm64";
        return null;
    }

    private static string? FindInPath()
    {
        var pathEnv = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;

        foreach (var dir in pathEnv.Split(Path.PathSeparator))
        {
            if (string.IsNullOrWhiteSpace(dir)) continue;
            var candidate = Path.Combine(dir.Trim(), BinaryFileName);
            if (File.Exists(candidate))
                return candidate;
        }

        return null;
    }
}
