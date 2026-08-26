using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Diagnyx;

internal sealed class CliRunner
{
    private readonly string _binaryPath;

    internal CliRunner(string? binaryPath)
    {
        _binaryPath = binaryPath
            ?? Environment.GetEnvironmentVariable("DIAGNYX_PATH")
            ?? FindInPath()
            ?? throw new InvalidOperationException(
                "diagnyx binary not found. " +
                "Install it from https://github.com/nachiketg/diagnyx/releases " +
                "or set the DIAGNYX_PATH environment variable to its path.");
    }

    internal int Run(string[] args)
    {
        var psi = new ProcessStartInfo
        {
            FileName = _binaryPath,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        foreach (var arg in args)
            psi.ArgumentList.Add(arg);

        using var proc = Process.Start(psi)
            ?? throw new InvalidOperationException($"Failed to start process: {_binaryPath}");

        // Read stderr before WaitForExit to avoid deadlock when only stderr is redirected.
        var stderr = proc.StandardError.ReadToEnd();
        proc.WaitForExit();

        if (proc.ExitCode != 0 && !string.IsNullOrWhiteSpace(stderr))
            Console.Error.WriteLine(stderr.TrimEnd());

        return proc.ExitCode;
    }

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
