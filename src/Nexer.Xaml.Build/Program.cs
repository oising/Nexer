using Nexer.Xaml.Compiler;

// Nexer XAML build step, run by build\Nexer.Xaml.targets after CoreCompile:
//   Nexer.Xaml.Build <assembly> <referencesFile> <itemsFile> <outputAssembly>
// referencesFile: one reference path per line. itemsFile: one "fullPath|logicalName" per line.
// Diagnostics are written to stdout in the canonical MSBuild format so the Exec task surfaces them as errors.
if (args.Length != 4)
{
    Console.Error.WriteLine("Usage: Nexer.Xaml.Build <assembly> <referencesFile> <itemsFile> <outputAssembly>");
    return 2;
}

try
{
    var references = File.ReadAllLines(args[1]);
    var items = File.ReadAllLines(args[2])
        .Where(line => !string.IsNullOrWhiteSpace(line))
        .Select(line =>
        {
            var separator = line.IndexOf('|', StringComparison.Ordinal);
            return separator < 0
                ? new NexerXamlItem(line.Trim(), Path.GetFileName(line.Trim()))
                : new NexerXamlItem(line[..separator].Trim(), line[(separator + 1)..].Trim().Replace('\\', '/'));
        })
        .ToList();

    var result = NexerXamlAssemblyCompiler.Compile(args[0], references, items, args[3]);
    foreach (var diagnostic in result.Diagnostics)
    {
        Console.WriteLine(diagnostic.ToString());
    }

    if (result.AlreadyCompiled)
    {
        Console.WriteLine($"Nexer.Xaml.Build: '{args[0]}' was already processed.");
    }

    return result.Success ? 0 : 1;
}
catch (Exception e)
{
    Console.WriteLine($"{args[0]}: error NXAML0007: XAML build step failed: {e.Message.ReplaceLineEndings(" ")}");
    Console.Error.WriteLine(e);
    return 1;
}
