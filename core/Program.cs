using Diagnyx.Core.Commands;

if (args.Length == 0 || args[0] is "--help" or "-h" or "help")
    return PrintHelp();

if (args[0] is "--version" or "-v" or "version")
{
    Console.WriteLine("diagnyx 0.1.0");
    return 0;
}

try
{
    return args[0] switch
    {
        "log"  => LogCommand.Run(args[1..]),
        "init" => InitCommand.Run(args[1..]),
        _      => Fail($"Unknown command '{args[0]}'. Run 'diagnyx --help' for usage.")
    };
}
catch (Exception ex)
{
    return Fail(ex.Message);
}

static int PrintHelp()
{
    Console.WriteLine("""
        diagnyx — structured logger

        Usage:
          diagnyx log --level <level> --message <text> [--source <name>] [--context <json>]
          diagnyx init

        Commands:
          log     Write a structured log entry to the configured sink.
          init    Scaffold a default diagnyx.config.json in the current directory.

        Options:
          --help, -h      Show this help text.
          --version, -v   Show version.
        """);
    return 0;
}

static int Fail(string message)
{
    Console.Error.WriteLine($"error: {message}");
    return 1;
}
