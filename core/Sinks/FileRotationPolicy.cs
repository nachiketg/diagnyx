namespace Diagnyx.Core.Sinks;

/// <summary>
/// Decides when FileSink's active log file should roll over, and performs the
/// roll: diagnyx.log -> diagnyx.log.1 -> diagnyx.log.2 -> ... -> deleted past
/// MaxBackups. Rotation is checked before each write, not on a timer -- there
/// is no background process, so "the file just turned a week old" is only
/// noticed the next time something logs.
/// </summary>
internal sealed class FileRotationPolicy(TimeSpan? maxAge, long? maxSizeBytes, int maxBackups)
{
    private readonly int _maxBackups = Math.Max(0, maxBackups);

    public bool ShouldRotate(string path)
    {
        if (!File.Exists(path))
            return false;

        if (maxSizeBytes is { } sizeLimit && new FileInfo(path).Length >= sizeLimit)
            return true;

        // The active file's own age, not any rotated backup's -- a fresh file
        // (just rotated, or never rotated) always has a recent creation time,
        // which a rename preserves and a newly-created file sets to now.
        if (maxAge is { } ageLimit && DateTime.UtcNow - File.GetCreationTimeUtc(path) >= ageLimit)
            return true;

        return false;
    }

    public void Rotate(string path)
    {
        for (var i = _maxBackups - 1; i >= 1; i--)
        {
            var from = $"{path}.{i}";
            if (File.Exists(from))
                File.Move(from, $"{path}.{i + 1}", overwrite: true);
        }

        if (_maxBackups == 0)
            File.Delete(path);
        else
            File.Move(path, $"{path}.1", overwrite: true);

        PruneBeyondRetention(path);
    }

    // The shift above never itself produces a backup numbered past
    // MaxBackups, so this is normally a no-op. It exists to clean up
    // leftovers from a previously higher MaxBackups value -- otherwise
    // lowering the config wouldn't actually shrink what's kept on disk.
    private void PruneBeyondRetention(string path)
    {
        var dir = Path.GetDirectoryName(path);
        var searchDir = string.IsNullOrEmpty(dir) ? "." : dir;
        if (!Directory.Exists(searchDir))
            return;

        var prefix = Path.GetFileName(path) + ".";
        foreach (var file in Directory.EnumerateFiles(searchDir, prefix + "*"))
        {
            var suffix = Path.GetFileName(file)[prefix.Length..];
            if (int.TryParse(suffix, out var n) && n > _maxBackups)
                File.Delete(file);
        }
    }
}
