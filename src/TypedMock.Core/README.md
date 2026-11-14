# TypedMock.Core

Contract-driven mock proxy and C# stub generator for .NET microservices.

Traditional record-and-replay tools (WireMock, Mountebank) record HTTP traffic into raw JSON files. While simple at first, raw JSON mappings suffer from silent schema drift, high maintenance costs, and zero compile-time safety.

**TypedMock** solves this by converting recorded HTTP traffic directly into **strongly-typed C# ASP.NET Core stub controllers** driven by published contract interfaces (e.g. **RestEase**):
- **Zero-JSON Stubs**: Traffic is emitted as native C# code with pattern-matching for route, query, and request body DTOs.
- **Compile-Time Drift Detection**: If an upstream team changes or renames a property in the contract NuGet, your mock controllers immediately fail at compile time (`CS0535` / `CS1061`).
- **Runtime Schema Validation**: Every upstream response is validated against the contract DTO return type in real time, detecting breaking changes immediately.
- **Scenario Hooks & Extensibility**: Override virtual methods (`On<Method>Async`) to implement custom scenario logic or error simulations.
- **In-Memory & Standalone Hosting**: Run generated stubs via `StubServerHost.StartAsync()` in unit/integration tests or host them in standalone servers with zero runtime dependency on `TypedMock.Core`.

## Quick Start

### 1. Record Traffic & Generate C# Stubs

```csharp
var mockServer = new TypedMockServer(new TypedMockOptions
{
    TargetUrl = "https://api.external-weather.com",
    OutputDirectory = "./src/MyService.Stubs/Controllers",
    StubNamespace = "MyService.Stubs.Controllers"
});

mockServer.RegisterContract<IWeatherApi>();
await mockServer.StartAsync();

// Run tests or client calls through mockServer.BaseUrl...
// When stopped, TypedMock saves WeatherApiStubController.cs into OutputDirectory!
await mockServer.StopAsync();
```

### 2. Host Generated C# Stubs in Tests

```csharp
// Host the compiled C# stub controller over HTTP in test runs
await using var stubHost = await StubServerHost.StartAsync<WeatherApiStubController>(port: 0);
var url = StubServerHost.GetServerUrl(stubHost);

var client = RestClient.For<IWeatherApi>(url);
var forecast = await client.GetForecastByCityAsync("London");
```

### 3. Customize Scenarios with Extensibility Hooks

```csharp
[ApiController]
public class CustomWeatherStubController : WeatherApiStubController
{
    protected override Task<WeatherForecastDto?> OnGetForecastByCityAsync(string city)
    {
        if (string.Equals(city, "StormCity", StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult<WeatherForecastDto?>(new WeatherForecastDto
            {
                City = "StormCity",
                TemperatureC = -15,
                Summary = "Blizzard"
            });
        }

        return base.OnGetForecastByCityAsync(city);
    }
}
```

## License

MIT
