using ExternalService.Contracts;
using TypedMock.Core;

Console.WriteLine("==================================================");
Console.WriteLine("   TypedMock Server - Contract-Driven Mock Proxy  ");
Console.WriteLine("==================================================");

// Parse CLI args
var modeStr = GetArg(args, "--mode") ?? "Replay";
var portStr = GetArg(args, "--port") ?? "5050";
var targetUrl = GetArg(args, "--target");
var fixturesDir = GetArg(args, "--fixtures") ?? Path.Combine(AppContext.BaseDirectory, "fixtures");

if (!Enum.TryParse<MockMode>(modeStr, true, out var mode))
{
    mode = MockMode.Replay;
}

var port = int.TryParse(portStr, out var p) ? p : 5050;

var options = new TypedMockOptions
{
    Mode = mode,
    Port = port,
    TargetUrl = targetUrl,
    FixturesDirectory = Path.GetFullPath(fixturesDir)
};

Console.WriteLine($"[Config] Mode:              {options.Mode}");
Console.WriteLine($"[Config] Port:              {options.Port}");
Console.WriteLine($"[Config] Target (upstream): {options.TargetUrl ?? "(none)"}");
Console.WriteLine($"[Config] Fixtures Path:     {options.FixturesDirectory}");
Console.WriteLine();

var server = new TypedMockServer(options);

// Register contracts from NuGet
server.RegisterContract<IWeatherApi>();

await server.StartAsync();

Console.WriteLine($"[Status] TypedMock Server running at: {server.BaseUrl}");
Console.WriteLine($"[Status] Diagnostic Endpoint:        {server.BaseUrl}/__typedmock/status");
Console.WriteLine();
Console.WriteLine("Registered Contracts & Endpoints:");
Console.WriteLine("  - IWeatherApi (RestEase)");
Console.WriteLine("      * GET  /weatherforecast");
Console.WriteLine("      * GET  /weatherforecast/{city}");
Console.WriteLine("      * POST /weatherforecast");
Console.WriteLine();
Console.WriteLine("Usage Tips:");
Console.WriteLine("  * Send header 'X-Mock-Scenario: <name>' to test specific scenarios (e.g. StormWarning, Error500).");
Console.WriteLine("  * When --mode=Record, calls forward to --target and save typed JSON fixtures.");
Console.WriteLine("  * When --mode=Replay, responses match RestEase DTO schemas.");
Console.WriteLine();
Console.WriteLine("Press Ctrl+C to stop.");

var cts = new CancellationTokenSource();
Console.CancelKeyPress += (s, e) =>
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
    Console.WriteLine("Shutting down TypedMock Server...");
}
finally
{
    await server.StopAsync();
    Console.WriteLine("TypedMock Server stopped.");
}

static string? GetArg(string[] args, string name)
{
    foreach (var arg in args)
    {
        if (arg.StartsWith($"{name}=", StringComparison.OrdinalIgnoreCase))
        {
            return arg[(name.Length + 1)..];
        }
    }
    return null;
}
