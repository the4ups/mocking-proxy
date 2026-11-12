# TypedMock.Core

Contract-driven mock proxy and C# stub generator for .NET microservices.

Traditional record-and-replay tools (WireMock, Mountebank) record HTTP traffic into raw JSON files. While simple at first, raw JSON mappings suffer from silent schema drift, high maintenance costs, and zero compile-time safety.

**TypedMock** solves this by converting recorded HTTP traffic directly into **strongly-typed C# ASP.NET Core stub controllers** driven by published contract interfaces (e.g. **RestEase**):
- **Zero-JSON Stubs**: Traffic is emitted as native C# code with pattern-matching for route, query, and request body DTOs.
- **Compile-Time Drift Detection**: If an upstream team changes or renames a property in the contract NuGet, your mock controllers immediately fail at compile time (`CS0535` / `CS1061`).
- **Runtime Schema Validation**: In Record mode, every upstream response is validated against the contract return type, detecting breaking changes immediately.
- **Scenario Hooks & Extensibility**: Override virtual methods (`On<Method>Async`) to implement custom scenario logic or error simulations.
- **In-Memory & Standalone Hosting**: Run generated stubs via `StubServerHost.StartAsync()` in unit/integration tests or host them in standalone servers.

## Quick Start (Embedded in Integration Tests)

### 1. Programmatic Mocking (Replay Mode)

```csharp
using TypedMock.Core;
using RestEase;

// Start mock server in Replay mode
await using var mockServer = new TypedMockServer(new TypedMockOptions { Mode = MockMode.Replay });

// Setup typed mock rule
mockServer.For<IWeatherApi>()
    .Setup(x => x.GetForecastByCityAsync("London"))
    .Returns(new WeatherForecastDto
    {
        City = "London",
        TemperatureC = 15,
        Summary = "Mild"
    });

await mockServer.StartAsync();

// Use with RestEase client
var client = RestClient.For<IWeatherApi>(mockServer.BaseUrl);
var forecast = await client.GetForecastByCityAsync("London");
```

### 2. Record Traffic & Generate C# Stubs

```csharp
var mockServer = new TypedMockServer(new TypedMockOptions
{
    Mode = MockMode.Record,
    TargetUrl = "https://api.external-weather.com",
    OutputDirectory = "./GeneratedStubs"
});

mockServer.RegisterContract<IWeatherApi>();
await mockServer.StartAsync();

// Run tests or client calls through mockServer.BaseUrl...
// When stopped, TypedMock saves WeatherApiStubController.cs into OutputDirectory!
await mockServer.StopAsync();
```

### 3. Host Generated C# Stubs

```csharp
// Host the generated C# stub controller over HTTP in test runs
await using var stubHost = await StubServerHost.StartAsync<WeatherApiStubController>(port: 0);
var url = StubServerHost.GetServerUrl(stubHost);

var client = RestClient.For<IWeatherApi>(url);
var forecast = await client.GetForecastByCityAsync("London");
```

## License

MIT
