using System.Net;
using System.Text;
using Diagnyx.Core.Logging;

namespace Diagnyx.Core.Commands;

/// <summary>
/// diagnyx ask serve [--port &lt;port&gt;]
///
/// A minimal local alternative to "diagnyx ask": a single page with a form
/// (question, plus optional since/until/source) that posts to itself and
/// re-renders with the answer and cited entries below it. No JavaScript, no
/// build step, no external assets -- just HttpListener and a hand-built HTML
/// string, consistent with the rest of this codebase's no-reflection,
/// no-framework approach. Built entirely on AskService, the same pipeline
/// "diagnyx ask" uses, so the two can never disagree about an answer.
///
/// Binds to localhost only, same as "diagnyx metrics serve" -- "locally
/// served, no external hosting" per the ticket this implements, not a
/// server meant to be reachable from another machine.
/// </summary>
internal static class AskServeCommand
{
    private const int DefaultPort = 8080;

    internal static int Run(string[] args)
    {
        var port = DefaultPort;
        for (var i = 0; i < args.Length; i++)
        {
            if (args[i] == "--port" && i + 1 < args.Length)
            {
                if (!int.TryParse(args[++i], out port) || port is <= 0 or > 65535)
                    return Fail("invalid --port value. Must be an integer between 1 and 65535.");
            }
            else
            {
                return Fail($"unknown option '{args[i]}'. Run 'diagnyx --help' for usage.");
            }
        }

        using var listener = new HttpListener();
        listener.Prefixes.Add($"http://localhost:{port}/");

        try
        {
            listener.Start();
        }
        catch (Exception ex)
        {
            return Fail($"failed to start ask web UI on port {port}: {ex.Message}");
        }

        Console.WriteLine($"diagnyx ask web UI listening on http://localhost:{port}/ (Ctrl+C to stop)");

        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            listener.Stop();
        };

        while (listener.IsListening)
        {
            HttpListenerContext context;
            try
            {
                context = listener.GetContext();
            }
            catch (Exception) when (!listener.IsListening)
            {
                break; // listener.Stop() was called from the Ctrl+C handler.
            }

            HandleRequest(context);
        }

        return 0;
    }

    private static void HandleRequest(HttpListenerContext context)
    {
        try
        {
            var path = context.Request.Url?.AbsolutePath.TrimEnd('/') is { Length: > 0 } p ? p : "/";

            if (context.Request.HttpMethod == "GET" && path == "/")
            {
                WriteHtml(context, 200, AskPage.Render());
                return;
            }

            if (context.Request.HttpMethod == "GET" && path == "/ask.css")
            {
                WriteCss(context, AskPage.Stylesheet);
                return;
            }

            if (context.Request.HttpMethod == "POST" && path == "/ask")
            {
                var form = ParseForm(ReadBody(context.Request));
                var question = form.GetValueOrDefault("question", "").Trim();
                var sinceInput = NullIfEmpty(form.GetValueOrDefault("since", ""));
                var untilInput = NullIfEmpty(form.GetValueOrDefault("until", ""));
                var source = NullIfEmpty(form.GetValueOrDefault("source", ""));

                var outcome = BuildOutcome(question, sinceInput, untilInput, source);
                WriteHtml(context, 200, AskPage.Render(question, sinceInput, untilInput, source, outcome));
                return;
            }

            context.Response.StatusCode = 404;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"error: failed to serve ask web UI request: {ex.Message}");
            context.Response.StatusCode = 500;
        }
        finally
        {
            context.Response.OutputStream.Close();
        }
    }

    /// <summary>
    /// since/until are user-typed text ("1h", "2026-09-01", ...), same as
    /// the CLI's --since/--until -- CliTimeParser converts them to the
    /// absolute timestamps AskService (and the sinks underneath it) expect.
    /// </summary>
    private static AskService.Outcome BuildOutcome(string question, string? sinceInput, string? untilInput, string? source)
    {
        if (string.IsNullOrWhiteSpace(question))
            return new AskService.Outcome(false, "a question is required.");

        string? since = null, until = null;
        if (sinceInput is not null && !CliTimeParser.TryParse(sinceInput, out since))
            return new AskService.Outcome(false, CliTimeParser.InvalidMessage("since", sinceInput));
        if (untilInput is not null && !CliTimeParser.TryParse(untilInput, out until))
            return new AskService.Outcome(false, CliTimeParser.InvalidMessage("until", untilInput));
        if (since is not null && until is not null && string.CompareOrdinal(since, until) > 0)
            return new AskService.Outcome(false, "since must not be later than until.");

        return AskService.Run(question, since, until, source);
    }

    private static string ReadBody(HttpListenerRequest request)
    {
        using var reader = new StreamReader(request.InputStream, request.ContentEncoding);
        return reader.ReadToEnd();
    }

    /// <summary>Manual application/x-www-form-urlencoded parsing -- no extra dependency for three fields.</summary>
    private static Dictionary<string, string> ParseForm(string body)
    {
        var form = new Dictionary<string, string>();
        foreach (var pair in body.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = pair.Split('=', 2);
            var key = WebUtility.UrlDecode(parts[0]);
            var value = parts.Length > 1 ? WebUtility.UrlDecode(parts[1]) : "";
            form[key] = value;
        }

        return form;
    }

    private static string? NullIfEmpty(string value) => string.IsNullOrWhiteSpace(value) ? null : value;

    private static void WriteHtml(HttpListenerContext context, int statusCode, string html)
    {
        var body = Encoding.UTF8.GetBytes(html);
        context.Response.StatusCode = statusCode;
        context.Response.ContentType = "text/html; charset=utf-8";
        context.Response.ContentLength64 = body.Length;
        context.Response.OutputStream.Write(body, 0, body.Length);
    }

    private static void WriteCss(HttpListenerContext context, string css)
    {
        var body = Encoding.UTF8.GetBytes(css);
        context.Response.StatusCode = 200;
        context.Response.ContentType = "text/css; charset=utf-8";
        context.Response.ContentLength64 = body.Length;
        context.Response.OutputStream.Write(body, 0, body.Length);
    }

    private static int Fail(string message)
    {
        Console.Error.WriteLine($"error: {message}");
        return 1;
    }
}
