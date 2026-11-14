using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

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

// Exception handling middleware for 404s
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

// Scenario resolution middleware
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

Console.WriteLine($"[Running] ExternalService.Stubs server listening on port: {port}");
await app.RunAsync();
