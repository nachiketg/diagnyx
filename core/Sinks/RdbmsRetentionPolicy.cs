using Diagnyx.Core.Logging;

namespace Diagnyx.Core.Sinks;

/// <summary>
/// Decides whether RdbmsSink should run its delete-older-than cleanup on this
/// write. There is no background process, so cleanup is checked
/// opportunistically on writes rather than on a wall-clock schedule: with
/// CheckProbability less than 1, most writes skip the check entirely,
/// keeping the common case a plain insert while still bounding table growth
/// over many writes. Setting it to 1 makes cleanup run on every write --
/// useful for a low-volume sink, or for tests that need it deterministic.
/// </summary>
internal sealed class RdbmsRetentionPolicy(TimeSpan maxAge, double checkProbability)
{
    public bool ShouldCheck() => Random.Shared.NextDouble() < checkProbability;

    public string Cutoff() => TimestampUtil.ToTimestamp(DateTimeOffset.UtcNow - maxAge);
}
