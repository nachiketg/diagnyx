using Diagnyx.Core.Logging;

namespace Diagnyx.Core.Sinks;

internal interface ISink
{
    int Write(LogEntry entry);
}
