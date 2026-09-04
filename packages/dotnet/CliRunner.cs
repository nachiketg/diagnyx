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
        ?? FindInPath()
        ?? throw new InvalidOperationException(
            "diagnyx binary not found. " +
            "Install it from https://github.com/nachiketg/diagnyx/releases " +
            "or set the DIAGNYX_PATH environment variable to its path.");

    private static void Warn(string message) =>
        Console.Error.WriteLine($"diagnyx: warning: {message.TrimEnd('.')}. Log entry dropped.");

    private static string? FindInPath()
    {
        var binary = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? "diagnyx.exe"
            : "diagnyx";

        var pathEnv = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;

        foreach (var dir in pathEnv.Split(Path.PathSeparator))
        {
            if (string.IsNullOrWhiteSpace(dir)) continue;
            var candidate = Path.Combine(dir.Trim(), binary);
            if (File.Exists(candidate))
                return candidate;
        }

        return null;
    }
}
