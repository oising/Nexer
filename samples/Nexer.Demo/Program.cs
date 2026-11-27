using Nexer.Demo;
using Nexer.Hosting;

// Nexer demo: a XAML port of Jexer's demo. --smoke renders every demo window headlessly and exits (--dump also prints the frames);
// --screenshots <dir> writes the README screenshots as SVG.
if (args.Contains("--smoke"))
{
    return DemoSmoke.Run(Console.Out, dump: args.Contains("--dump"));
}

int shots = Array.IndexOf(args, "--screenshots");
if (shots >= 0)
{
    if (shots + 1 >= args.Length)
    {
        Console.Error.WriteLine("Usage: --screenshots <directory>");
        return 2;
    }

    return DemoScreenshots.Run(args[shots + 1], Console.Out);
}

ConsoleTerminalHost host;
try
{
    host = await ConsoleTerminalHost.StartAsync(new ConsoleTerminalHostOptions { MouseMotion = true });
}
catch (Exception ex) when (ex is InvalidOperationException or PlatformNotSupportedException)
{
    Console.Error.WriteLine($"Cannot start the terminal host: {ex.Message}");
    Console.Error.WriteLine("Run the demo in an interactive terminal, or pass --smoke for a headless check.");
    return 2;
}

await using (host)
{
    var app = new DemoApplication();
    await app.RunAsync(host);
}

return 0;
