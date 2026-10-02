using System.Net;
using System.Reflection;
using System.Text;

namespace Diagnyx.Core.Commands;

/// <summary>
/// Renders the single page AskServeCommand serves, from the markup and
/// stylesheet in core/Web/ (embedded resources -- the design lives in real
/// .html/.css files, not C# strings, while the published binary still
/// stays a single self-contained file with no companion asset to ship
/// separately). No JavaScript: the form POSTs to /ask and the server
/// re-renders the whole page, pre-filled, with the answer or error
/// appended. Every value that isn't part of the fixed template (the
/// submitted question/since/until/source, and the answer/citations text,
/// which embeds real log content) is HTML-escaped before being
/// substituted in -- log messages and LLM output are not trusted input.
/// </summary>
internal static class AskPage
{
    private static readonly string Template = LoadResource("AskPage.html");

    /// <summary>Served by AskServeCommand at GET /ask.css, referenced from Template via &lt;link&gt;.</summary>
    public static readonly string Stylesheet = LoadResource("AskPage.css");

    private static string LoadResource(string fileName)
    {
        var assembly = Assembly.GetExecutingAssembly();
        var resourceName = $"Diagnyx.Core.Web.{fileName}";
        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"embedded resource '{resourceName}' not found.");
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    public static string Render(
        string? question = null,
        string? since = null,
        string? until = null,
        string? source = null,
        AskService.Outcome? outcome = null)
    {
        return Template
            .Replace("{{QUESTION}}", Html(question ?? ""))
            .Replace("{{SINCE}}", Html(since ?? ""))
            .Replace("{{UNTIL}}", Html(until ?? ""))
            .Replace("{{SOURCE}}", Html(source ?? ""))
            .Replace("{{RESULT}}", RenderResult(outcome));
    }

    private static string RenderResult(AskService.Outcome? outcome)
    {
        if (outcome is null)
            return "";

        var cssClass = outcome.Success ? "ok" : "error";
        return $"<div class=\"result {cssClass}\">{Html(outcome.Text)}</div>";
    }

    private static string Html(string text) => WebUtility.HtmlEncode(text);
}
