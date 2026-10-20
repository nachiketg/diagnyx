using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace Diagnyx.Core.Native;

/// <summary>
/// Keeps SQLite (the sqlite sink and the metrics counter store) working from
/// the single-file Native AOT binary (DX-060).
/// </summary>
/// <remarks>
/// Native AOT compiles managed code into the executable, but SQLitePCLRaw's
/// native e_sqlite3 library is still a separate file next to it, and every
/// distribution ships only the executable. Without that file, the first
/// SQLite call fails with "A type initializer threw an exception". So an AOT
/// publish embeds the library as a resource (see Diagnyx.Core.csproj), and
/// this extracts it on first use to ~/.diagnyx/native/e_sqlite3-{hash}/ and
/// points SQLitePCLRaw's P/Invokes at it. The directory name is a hash of
/// the content, so different versions coexist; an existing file is used
/// only if its bytes match the embedded copy exactly.
///
/// Builds that embed nothing (dotnet build/run) leave the default probing
/// alone, which finds the library in the output folder as usual.
/// </remarks>
internal static class EmbeddedSqlite
{
    private const string LibraryName = "e_sqlite3";
    private const string ResourcePrefix = "native/";

    private static readonly Assembly Self = typeof(EmbeddedSqlite).Assembly;
    private static readonly Lazy<string?> ExtractedPath = new(Extract);

    // Runs at startup for every command, so it only registers the resolver;
    // nothing is read or written until SQLite is actually used. It must never
    // throw: an exception here would break every command, not just SQLite.
    [ModuleInitializer]
    internal static void Register()
    {
        try
        {
            if (FindResource() is null)
                return;

            NativeLibrary.SetDllImportResolver(typeof(SQLitePCL.SQLite3Provider_e_sqlite3).Assembly, Resolve);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"warning: could not register the embedded SQLite library: {ex.Message}");
        }
    }

    private static IntPtr Resolve(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
    {
        if (libraryName != LibraryName || ExtractedPath.Value is not { } path)
            return IntPtr.Zero; // fall back to default probing

        return NativeLibrary.TryLoad(path, out var handle) ? handle : IntPtr.Zero;
    }

    private static string? FindResource() =>
        Self.GetManifestResourceNames().FirstOrDefault(n => n.StartsWith(ResourcePrefix, StringComparison.Ordinal));

    private static string? Extract()
    {
        try
        {
            var resourceName = FindResource()!;
            byte[] bytes;
            using (var stream = Self.GetManifestResourceStream(resourceName)!)
            using (var buffer = new MemoryStream())
            {
                stream.CopyTo(buffer);
                bytes = buffer.ToArray();
            }

            var hash = Convert.ToHexStringLower(SHA256.HashData(bytes))[..16];
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var dir = Path.Combine(home, ".diagnyx", "native", $"{LibraryName}-{hash}");
            var path = Path.Combine(dir, resourceName[ResourcePrefix.Length..]);

            if (Matches(path, bytes))
                return path;

            if (OperatingSystem.IsWindows())
                Directory.CreateDirectory(dir);
            else
                Directory.CreateDirectory(dir, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);

            // Write to a per-process temp file and rename it into place, so
            // concurrent "diagnyx log" processes never load a half-written file.
            var tmp = $"{path}.{Environment.ProcessId}.tmp";
            try
            {
                File.WriteAllBytes(tmp, bytes);
                File.Move(tmp, path, overwrite: true);
            }
            catch (IOException) when (Matches(path, bytes))
            {
                // Another process put an identical copy in place first (on
                // Windows, a loaded DLL can't be overwritten). Use that one.
            }
            finally
            {
                File.Delete(tmp);
            }

            return Matches(path, bytes) ? path : null;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"warning: could not extract the embedded SQLite library: {ex.Message}");
            return null;
        }
    }

    private static bool Matches(string path, byte[] expected)
    {
        try
        {
            return File.Exists(path) && File.ReadAllBytes(path).AsSpan().SequenceEqual(expected);
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }
}
