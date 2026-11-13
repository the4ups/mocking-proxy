using System.Reflection;
using TypedMock.Core.Hosting;

Console.WriteLine("==================================================");
Console.WriteLine("  ExternalService.Stubs - Standalone Mock Host   ");
Console.WriteLine("==================================================");

var port = 5050;
foreach (var arg in args)
{
    if (int.TryParse(arg, out var p))
    {
        port = p;
    }
    else if (arg.StartsWith("--port=", StringComparison.OrdinalIgnoreCase) && int.TryParse(arg["--port=".Length..], out var pp))
    {
        port = pp;
    }
}

var app = await StubServerHost.StartAsync(typeof(Program).Assembly, port);
var url = StubServerHost.GetServerUrl(app);

Console.WriteLine($"[Running] ExternalService.Stubs server listening on: {url}");
Console.WriteLine("Press Ctrl+C to stop.");

var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cts.Cancel();
};

try
{
    await Task.Delay(Timeout.Infinite, cts.Token);
}
catch (OperationCanceledException)
{
    Console.WriteLine("Shutting down stub server...");
}
finally
{
    await app.StopAsync();
    await app.DisposeAsync();
}
