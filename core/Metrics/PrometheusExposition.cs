using System.Text;

namespace Diagnyx.Core.Metrics;

/// <summary>
/// Renders MetricsStore counts as Prometheus text exposition format
/// (https://prometheus.io/docs/instrumenting/exposition_formats/).
/// </summary>
internal static class PrometheusExposition
{
    public static string Render(IReadOnlyList<(string Level, string Source, long Count)> counts)
    {
        var sb = new StringBuilder();
        sb.Append("# HELP diagnyx_log_entries_total Total number of log entries processed by Diagnyx, by level and source.\n");
        sb.Append("# TYPE diagnyx_log_entries_total counter\n");

        foreach (var (level, source, count) in counts)
        {
            sb.Append("diagnyx_log_entries_total{level=\"");
            sb.Append(EscapeLabelValue(level));
            sb.Append("\",source=\"");
            sb.Append(EscapeLabelValue(source));
            sb.Append("\"} ");
            sb.Append(count);
            sb.Append('\n');
        }

        return sb.ToString();
    }

    private static string EscapeLabelValue(string value) =>
        value.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n");
}
