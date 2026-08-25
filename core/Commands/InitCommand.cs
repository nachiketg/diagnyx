using System.Text;

namespace Diagnyx.Core.Commands;

internal static class InitCommand
{
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);
    private const string DefaultConfig = """
        {
          "$schema": "https://raw.githubusercontent.com/nachiketg/diagnyx/main/docs/config.schema.json",
          "sink": {
            "type": "file",
            "file": {
              "path": "~/.diagnyx/logs/diagnyx.log"
            }
          },
          "defaults": {
            "source": "app"
          }
        }
        """;

    internal static int Run(string[] args)
    {
        var path = Path.Combine(Directory.GetCurrentDirectory(), "diagnyx.config.json");

        if (File.Exists(path))
        {
            Console.WriteLine($"Config already exists: {path}");
            Console.WriteLine("No changes made. Edit it manually, or delete it and re-run to reset.");
            return 0;
        }

        File.WriteAllText(path, DefaultConfig + "\n", Utf8NoBom);
        Console.WriteLine($"Created: {path}");
        Console.WriteLine("Edit the file to configure your sink and defaults.");
        return 0;
    }
}
