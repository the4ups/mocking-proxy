using ExternalService.Contracts;
using TypedMock.Core;

Console.WriteLine("==================================================");
Console.WriteLine("   TypedMock Server - Contract-Driven Mock Proxy  ");
Console.WriteLine("==================================================");

// Parse CLI args
var modeStr = GetArg(args, "--mode") ?? "Replay";
var portStr = GetArg(args, "--port") ?? "5050";
var targetUrl = GetArg(args, "--target");
var upstreamsArg = GetArg(args, "--upstreams");
var outDir = GetArg(args, "--out-dir") ?? Path.Combine(AppContext.BaseDirectory, "GeneratedStubs");
var outStubs = GetArg(args, "--out-stubs");
var contractsFile = GetArg(args, "--contracts");
var contractsDir = GetArg(args, "--contracts-dir");

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
    OutputDirectory = Path.GetFullPath(outDir),
    OutputStubPath = outStubs != null ? Path.GetFullPath(outStubs) : null
};

// Parse multi-upstream table (e.g. "IWeatherApi=http://localhost:5001,IPaymentApi=http://localhost:5002")
if (!string.IsNullOrWhiteSpace(upstreamsArg))
{
    var pairs = upstreamsArg.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    foreach (var pair in pairs)
    {
        var eqIndex = pair.IndexOf('=');
        if (eqIndex > 0)
        {
            var key = pair[..eqIndex].Trim();
            var val = pair[(eqIndex + 1)..].Trim();
            options.Upstreams[key] = val;
        }
    }
}

Console.WriteLine($"[Config] Mode:              {options.Mode}");
Console.WriteLine($"[Config] Port:              {options.Port}");
Console.WriteLine($"[Config] Default Upstream:  {options.TargetUrl ?? "(none)"}");
if (options.Upstreams.Count > 0)
{
    Console.WriteLine("[Config] Upstream Map:");
    foreach (var (contract, url) in options.Upstreams)
    {
        Console.WriteLine($"           - {contract} -> {url}");
    }
}
Console.WriteLine($"[Config] Output Directory:  {options.OutputDirectory}");
Console.WriteLine();

var server = new TypedMockServer(options);

// Dynamic Contract Discovery
var contractsLoadedCount = 0;
if (!string.IsNullOrWhiteSpace(contractsFile) && File.Exists(contractsFile))
{
    Console.WriteLine($"[Discovery] Loading contracts from assembly: {contractsFile}");
    server.RegisterContractsFromAssembly(contractsFile);
    contractsLoadedCount = server.RegisteredEndpoints.Select(e => e.InterfaceType).Distinct().Count();
}
else if (!string.IsNullOrWhiteSpace(contractsDir) && Directory.Exists(contractsDir))
{
    Console.WriteLine($"[Discovery] Scanning contracts directory: {contractsDir}");
    server.RegisterContractsFromDirectory(contractsDir);
    contractsLoadedCount = server.RegisteredEndpoints.Select(e => e.InterfaceType).Distinct().Count();
}

// Fallback for standalone demo run if no external assemblies provided
if (contractsLoadedCount == 0)
{
    server.RegisterContract<IWeatherApi>();
}

await server.StartAsync();

Console.WriteLine($"[Status] TypedMock Server running at: {server.BaseUrl}");
Console.WriteLine($"[Status] Diagnostic Endpoint:        {server.BaseUrl}/__typedmock/status");
Console.WriteLine();
Console.WriteLine("Registered Contracts & Endpoints:");
var registeredInterfaces = server.RegisteredEndpoints.Select(e => e.InterfaceType).Distinct();
foreach (var iface in registeredInterfaces)
{
    Console.WriteLine($"  - {iface.Name} ({iface.Namespace})");
    var endpoints = server.RegisteredEndpoints.Where(e => e.InterfaceType == iface);
    foreach (var ep in endpoints)
    {
        Console.WriteLine($"      * {ep.HttpMethod.Method,-6} {ep.PathTemplate}");
    }
}

Console.WriteLine();
Console.WriteLine("Usage Tips:");
Console.WriteLine("  * When --mode=Record, calls forward to their upstream and generate strongly-typed C# stub controllers.");
Console.WriteLine("  * When --mode=Replay, responses are verified against RestEase DTO schemas.");
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
    Console.WriteLine($"[Status] TypedMock Server stopped. Stubs saved to: {options.OutputDirectory}");
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
