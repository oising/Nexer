using MyApp;
using MyApp.Views;
using Nexer.Hosting;
using Nexer.Ui;
using Nexer.Ui.Testing;

// --smoke builds every window headlessly, renders one frame each and exits 0 (--dump prints the frames).
if (args.Contains("--smoke"))
{
    Func<Window>[] windows = [() => new MainWindow()];
    foreach (var create in windows)
    {
        var smokeApp = new App();
        smokeApp.Windows.Add(create());
        var driver = new UiDriver(smokeApp, 80, 25);
        if (args.Contains("--dump")) Console.WriteLine(driver.Text());
    }

    Console.WriteLine("smoke: OK");
    return 0;
}

ConsoleTerminalHost host;
try
{
    host = await ConsoleTerminalHost.StartAsync(new ConsoleTerminalHostOptions());
}
catch (Exception ex) when (ex is InvalidOperationException or PlatformNotSupportedException)
{
    Console.Error.WriteLine($"Cannot start the terminal host: {ex.Message}");
    Console.Error.WriteLine("Run in an interactive VT terminal, or pass --smoke for a headless check.");
    return 2;
}

await using (host)
{
    var app = new App();
    app.Windows.Add(new MainWindow());
    await app.RunAsync(host);
}

return 0;
