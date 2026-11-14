using TypedMock.Core;

if (args.Length > 0 && string.Equals(args[0], "init", StringComparison.OrdinalIgnoreCase))
{
    return RunInit(args);
}

Console.WriteLine("==================================================");
Console.WriteLine("   TypedMock Server - Contract-Driven Mock Proxy  ");
Console.WriteLine("==================================================");

// Parse CLI args
var portStr = GetArg(args, "--port") ?? "5050";
var targetUrl = GetArg(args, "--target");
var upstreamsArg = GetArg(args, "--upstreams");
var outDir = GetArg(args, "--out-dir") ?? Path.Combine(AppContext.BaseDirectory, "GeneratedStubs");
var contractsFile = GetArg(args, "--contracts");
var contractsDir = GetArg(args, "--contracts-dir");

var port = int.TryParse(portStr, out var p) ? p : 5050;

var options = new TypedMockOptions
{
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
Console.WriteLine("  * Upstream calls are recorded and strongly-typed C# stub controllers are generated on stop.");
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

return 0;

static int RunInit(string[] args)
{
    var name = GetArg(args, "--name");
    if (string.IsNullOrWhiteSpace(name))
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine("[Error] Missing required parameter: --name=<ProjectName>");
        Console.ResetColor();
        return 1;
    }

    var contracts = GetArg(args, "--contracts");
    if (string.IsNullOrWhiteSpace(contracts))
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine("[Error] Missing required parameter: --contracts=<path-to-contracts-csproj>");
        Console.ResetColor();
        return 1;
    }

    var outDir = GetArg(args, "--out-dir") ?? ".";
    var resolvedOutDir = Path.IsPathRooted(outDir)
        ? outDir
        : Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), outDir));
    var targetDir = Path.Combine(resolvedOutDir, name);

    if (Directory.Exists(targetDir) && Directory.GetFiles(targetDir, "*.csproj").Length > 0)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine($"[Error] Directory '{targetDir}' already contains a .csproj project file. Aborting to avoid overwrite.");
        Console.ResetColor();
        return 1;
    }

    Directory.CreateDirectory(targetDir);
    Directory.CreateDirectory(Path.Combine(targetDir, "Controllers"));

    var resolvedContracts = ResolvePath(contracts) ?? contracts;
    var relativeContracts = Path.GetRelativePath(targetDir, resolvedContracts).Replace('\\', '/');

    var csprojContent = $"""
<Project Sdk="Microsoft.NET.Sdk.Web">

  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>

  <ItemGroup>
    <ProjectReference Include="{relativeContracts}" />
  </ItemGroup>

</Project>
""";

    var programContent = $$"""
using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

Console.WriteLine("==================================================");
Console.WriteLine("  {{name}} - Standalone Mock Host   ");
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

var builder = WebApplication.CreateEmptyBuilder(new WebApplicationOptions
{
    Args = args,
    EnvironmentName = "Development"
});

builder.WebHost.UseKestrel(k =>
{
    k.Listen(IPAddress.Any, port);
});

builder.Services.AddRouting();
builder.Services.AddControllers();

var app = builder.Build();

app.Use(async (context, next) =>
{
    try
    {
        await next(context);
    }
    catch (KeyNotFoundException ex)
    {
        context.Response.StatusCode = StatusCodes.Status404NotFound;
        context.Response.ContentType = "application/json; charset=utf-8";
        await context.Response.WriteAsJsonAsync(new { error = ex.Message, statusCode = 404 });
    }
});

app.Use(async (context, next) =>
{
    var scenarioName = context.Request.Headers["X-Mock-Scenario"].FirstOrDefault();
    if (string.IsNullOrWhiteSpace(scenarioName))
    {
        scenarioName = context.Request.Query["__scenario"].FirstOrDefault();
    }
    scenarioName ??= "default";
    context.Items["TypedMock.Scenario"] = scenarioName;

    await next(context);
});

app.MapControllers();

Console.WriteLine($"[Running] {{name}} server listening on port: {port}");
await app.RunAsync();
""";

    var dockerfileContent = $$"""
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY . .
RUN dotnet restore
RUN dotnet publish -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "{{name}}.dll"]
CMD ["8080"]
""";

    File.WriteAllText(Path.Combine(targetDir, $"{name}.csproj"), csprojContent);
    File.WriteAllText(Path.Combine(targetDir, "Program.cs"), programContent);
    File.WriteAllText(Path.Combine(targetDir, "Dockerfile"), dockerfileContent);

    Console.WriteLine($"[Success] Scaffolded stubs project '{name}' at: {targetDir}");
    Console.WriteLine($"          - {name}.csproj (referencing {relativeContracts})");
    Console.WriteLine("          - Program.cs");
    Console.WriteLine("          - Dockerfile");
    Console.WriteLine("          - Controllers/");
    return 0;
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
