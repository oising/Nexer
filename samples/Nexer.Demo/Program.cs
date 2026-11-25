using Nexer.Demo;
using Nexer.Hosting;

// Nexer demo: a XAML port of Jexer's demo. --smoke renders every demo window headlessly and exits (--dump also prints the frames).
if (args.Contains("--smoke"))
{
    return DemoSmoke.Run(Console.Out, dump: args.Contains("--dump"));
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
