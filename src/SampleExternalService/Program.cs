using System.Net;
using ExternalService.Contracts;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;

namespace SampleExternalService;

public partial class Program
{
    public static async Task Main(string[] args)
    {
        var port = args.Length > 0 && int.TryParse(args[0], out var p) ? p : 5001;
        var app = await StartServerAsync(port);
        var addresses = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>();
        Console.WriteLine($"SampleExternalService running at {addresses?.Addresses.FirstOrDefault()}");
        await app.WaitForShutdownAsync();
    }

    public static async Task<WebApplication> StartServerAsync(int port = 0)
    {
        var builder = WebApplication.CreateEmptyBuilder(new WebApplicationOptions());
        builder.Services.AddRouting();
        builder.WebHost.UseKestrel(k => k.Listen(IPAddress.Loopback, port));

        var app = builder.Build();

        var forecasts = new List<WeatherForecastDto>
        {
            new() { Date = DateOnly.FromDateTime(DateTime.Today), City = "London", TemperatureC = 12, Summary = "Chilly" },
            new() { Date = DateOnly.FromDateTime(DateTime.Today), City = "Paris", TemperatureC = 18, Summary = "Mild" },
            new() { Date = DateOnly.FromDateTime(DateTime.Today), City = "Tokyo", TemperatureC = 22, Summary = "Warm" }
        };

        app.MapGet("/weatherforecast", () => Results.Ok(forecasts));

        app.MapGet("/weatherforecast/{city}", (string city) =>
        {
            var forecast = forecasts.FirstOrDefault(f => string.Equals(f.City, city, StringComparison.OrdinalIgnoreCase));
            return forecast is not null
                ? Results.Ok(forecast)
                : Results.NotFound(new { message = $"Forecast for {city} not found" });
        });

        app.MapPost("/weatherforecast", (CreateForecastRequest request) =>
        {
            var created = new WeatherForecastDto
            {
                Date = DateOnly.FromDateTime(DateTime.Today),
                City = request.City,
                TemperatureC = request.TemperatureC,
                Summary = request.Summary
            };
            forecasts.Add(created);
            return Results.Created($"/weatherforecast/{created.City}", created);
        });

        await app.StartAsync();
        return app;
    }
}
