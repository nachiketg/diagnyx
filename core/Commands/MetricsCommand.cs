using System.Net;
using System.Text;
using Diagnyx.Core.Config;
using Diagnyx.Core.Metrics;

namespace Diagnyx.Core.Commands;

internal static class MetricsCommand
{
    private const int DefaultPort = 9464; // IANA-registered default for Prometheus exporters.

    internal static int Run(string[] args)
    {
        if (args.Length == 0 || args[0] != "serve")
            return Fail("Usage: diagnyx metrics serve [--port <port>]");

        var port = DefaultPort;
        for (var i = 1; i < args.Length; i++)
        {
            if (args[i] == "--port" && i + 1 < args.Length)
            {
                if (!int.TryParse(args[++i], out port) || port is <= 0 or > 65535)
                    return Fail($"invalid --port value. Must be an integer between 1 and 65535.");
            }
        }

        var config = ConfigLoader.Load();
        if (config.Metrics is not { Enabled: true })
            return Fail(
                "metrics are disabled. Set \"metrics\": { \"enabled\": true } in your config, " +
                "then run 'diagnyx log' to start collecting counts. See docs/CONFIG.md.");

        var dbPath = ConfigLoader.ExpandPath(config.Metrics.Path);

        using var listener = new HttpListener();
        listener.Prefixes.Add($"http://localhost:{port}/");

        try
        {
            listener.Start();
        }
        catch (Exception ex)
        {
            return Fail($"failed to start metrics server on port {port}: {ex.Message}");
        }

        Console.WriteLine($"diagnyx metrics server listening on http://localhost:{port}/metrics (Ctrl+C to stop)");

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

            HandleRequest(context, dbPath);
        }

        return 0;
    }

    private static void HandleRequest(HttpListenerContext context, string dbPath)
    {
        var path = context.Request.Url?.AbsolutePath.TrimEnd('/');

        try
        {
            if (path != "/metrics")
            {
                context.Response.StatusCode = 404;
                return;
            }

            var counts = MetricsStore.ReadAll(dbPath);
            var body = Encoding.UTF8.GetBytes(PrometheusExposition.Render(counts));

            context.Response.StatusCode = 200;
            context.Response.ContentType = "text/plain; version=0.0.4; charset=utf-8";
            context.Response.ContentLength64 = body.Length;
            context.Response.OutputStream.Write(body, 0, body.Length);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"error: failed to serve metrics request: {ex.Message}");
            context.Response.StatusCode = 500;
        }
        finally
        {
            context.Response.OutputStream.Close();
        }
    }

    private static int Fail(string message)
    {
        Console.Error.WriteLine($"error: {message}");
        return 1;
    }
}
