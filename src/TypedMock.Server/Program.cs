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
    OutputDirectory = Path.GetFullPath(outDir)
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
if (!string.IsNullOrWhiteSpace(contractsFile))
{
    var resolvedFile = ResolvePath(contractsFile);
    if (File.Exists(resolvedFile))
    {
        Console.WriteLine($"[Discovery] Loading contracts from assembly: {resolvedFile}");
        server.RegisterContractsFromAssembly(resolvedFile);
        contractsLoadedCount = server.RegisteredEndpoints.Select(e => e.InterfaceType).Distinct().Count();
    }
    else
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine($"[Error] Contracts assembly not found: {contractsFile} (resolved: {resolvedFile})");
        Console.ResetColor();
    }
}
else if (!string.IsNullOrWhiteSpace(contractsDir))
{
    var resolvedDir = ResolvePath(contractsDir);
    if (Directory.Exists(resolvedDir))
    {
        Console.WriteLine($"[Discovery] Scanning contracts directory: {resolvedDir}");
        server.RegisterContractsFromDirectory(resolvedDir);
        contractsLoadedCount = server.RegisteredEndpoints.Select(e => e.InterfaceType).Distinct().Count();
    }
    else
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine($"[Error] Contracts directory not found: {contractsDir} (resolved: {resolvedDir})");
        Console.ResetColor();
    }
}
else
{
    // Check standard conventional contracts directory if present
    var defaultDir = ResolvePath("contracts");
    if (Directory.Exists(defaultDir))
    {
        Console.WriteLine($"[Discovery] Scanning default contracts directory: {defaultDir}");
        server.RegisterContractsFromDirectory(defaultDir);
        contractsLoadedCount = server.RegisteredEndpoints.Select(e => e.InterfaceType).Distinct().Count();
    }
}

if (contractsLoadedCount == 0)
{
    Console.ForegroundColor = ConsoleColor.Yellow;
    Console.WriteLine("[Warning] No contracts were loaded.");
    Console.WriteLine("          Provide contracts using:");
    Console.WriteLine("            --contracts=<path/to/Assembly.dll>");
    Console.WriteLine("            --contracts-dir=<path/to/contracts_dir>");
    Console.ResetColor();
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

static string? ResolvePath(string? path)
{
    if (string.IsNullOrWhiteSpace(path)) return null;
    if (Path.IsPathRooted(path)) return path;

    var cwd = Directory.GetCurrentDirectory();
    var relativeToCwd = Path.GetFullPath(Path.Combine(cwd, path));
    if (File.Exists(relativeToCwd) || Directory.Exists(relativeToCwd)) return relativeToCwd;

    var relativeToBase = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, path));
    if (File.Exists(relativeToBase) || Directory.Exists(relativeToBase)) return relativeToBase;

    // Walk up directory tree to support running via 'dotnet run --project ...' from repository root
    var dir = new DirectoryInfo(cwd);
    while (dir != null)
    {
        var candidate = Path.GetFullPath(Path.Combine(dir.FullName, path));
        if (File.Exists(candidate) || Directory.Exists(candidate)) return candidate;
        dir = dir.Parent;
    }

    return relativeToCwd;
}
