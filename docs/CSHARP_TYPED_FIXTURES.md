# Authoring C# Typed Fixtures vs. Raw JSON

Traditional mock proxies (such as WireMock, Mountebank, or MockServer) store traffic recordings as flat, untyped JSON files. While simple at first, raw JSON mocks quickly introduce maintenance problems:
1. **No Compile-Time Verification**: Renaming a property in a shared DTO leaves raw JSON files untouched, creating silent runtime test failures.
2. **No IntelliSense / Refactoring**: Developers and QA engineers must manually guess property names, casing conventions, and nested structures.
3. **Duplication**: Similar test cases duplicate large JSON blocks rather than reusing typed object builders.

`TypedMock` allows you to write **strongly-typed C# fixtures** using your published RestEase contract interfaces and DTOs.

---

## Architecture Overview

```mermaid
flowchart TD
    subgraph CSharpSource["C# Typed Fixture Definition"]
        DTO["Published Contract DTOs\n(e.g., WeatherForecastDto)"]
        Interface["RestEase Contract Interface\n(e.g., IWeatherApi)"]
        Setup["mockServer.For<IWeatherApi>()\n.Setup(x => x.GetForecastByCityAsync('London'))\n.Returns(new WeatherForecastDto { ... })"]
        DTO --> Setup
        Interface --> Setup
    end

    subgraph ExecutionModes["3 Execution Patterns"]
        Mode1["1. In-Memory Tests\n(Direct xUnit / Integration Tests)"]
        Mode2["2. Compiled Fixture DLL\n(Loaded into TypedMock.Server on Test Stands)"]
        Mode3["3. Code-First Generator\n(Exports Verified JSON to Disk)"]
        
        Setup --> Mode1
        Setup --> Mode2
        Setup --> Mode3
    end

    subgraph MockEngine["TypedMock Server"]
        Mode1 --> InMemStore["In-Memory Programmatic Rules"]
        Mode2 --> InMemStore
        Mode3 --> JSONStore["Validated JSON Fixtures\n{Contract}/{Method}/{Scenario}.json"]
        JSONStore --> InMemStore
    end
```

---

## 1. In-Memory C# Fixtures (For Integration Tests)

For unit and integration tests (such as xUnit, NUnit, or MSTest), you can declare mock scenarios directly in C# without creating or touching any JSON files on disk.

### Implementation:

```csharp
[Fact]
public async Task WeatherService_WhenStormReported_ReturnsWarning()
{
    // 1. Start mock server in Replay mode
    var options = new TypedMockOptions { Mode = MockMode.Replay };
    await using var mockServer = new TypedMockServer(options);

    // 2. Define strongly typed fixture directly in C#
    mockServer.For<IWeatherApi>()
        .Setup(x => x.GetForecastByCityAsync("London"))
        .WithScenario("StormWarning")
        .Returns(new WeatherForecastDto
        {
            City = "London",
            Date = DateOnly.FromDateTime(DateTime.Today),
            TemperatureC = 8,
            Summary = "Severe Thunderstorm"
        });

    // 3. Error simulation with status code
    mockServer.For<IWeatherApi>()
        .Setup(x => x.GetForecastByCityAsync("BrokenCity"))
        .ReturnsStatus(500, new { error = "Upstream weather station unreachable" });

    await mockServer.StartAsync();

    // 4. Act: RestEase client calls mockServer.BaseUrl
    var client = RestClient.For<IWeatherApi>(mockServer.BaseUrl);
    var forecast = await client.GetForecastByCityAsync("London");

    // 5. Assert
    forecast.Summary.Should().Be("Severe Thunderstorm");
}
```

### Key Advantages:
- **Zero Disk I/O**: Runs entirely in memory, maximizing test execution speed.
- **Refactoring Safe**: Renaming a field in `WeatherForecastDto` automatically updates all test setups via IDE refactoring (`Shift+F6` / `F2`).
- **Compiler Enforced**: If the external contract changes signature (e.g., parameter added), tests will fail to compile rather than failing at runtime.

---

## 2. Compiled C# Fixture Assemblies for Standalone Server (Test Stands & Containers)

When running `TypedMock.Server` as a standalone process (e.g., in a Docker container or Kubernetes pod on a test stand), you can load compiled **C# Fixture Assemblies** rather than mounting directories of JSON files.

### Step 1: Define a Fixture Interface

```csharp
namespace TypedMock.Core.Fixtures;

public interface IContractFixture<TContract> where TContract : class
{
    void Configure(ContractMockSetup<TContract> setup);
}
```

### Step 2: Implement Fixtures in a Class Library Project

Teams author scenarios in a standard C# project that references the contracts NuGet package:

```csharp
public class WeatherServiceFixtures : IContractFixture<IWeatherApi>
{
    public void Configure(ContractMockSetup<IWeatherApi> setup)
    {
        // Default scenario
        setup.Setup(x => x.GetForecastByCityAsync("London"))
             .Returns(new WeatherForecastDto
             {
                 City = "London",
                 TemperatureC = 20,
                 Summary = "Clear Sky"
             });

        // "Storm" scenario for QA functional testing
        setup.Setup(x => x.GetForecastByCityAsync("London"))
             .WithScenario("Storm")
             .Returns(new WeatherForecastDto
             {
                 City = "London",
                 TemperatureC = 5,
                 Summary = "Gale Warning"
             });

        // Edge case: Freezing temperatures
        setup.Setup(x => x.GetForecastByCityAsync("Oslo"))
             .Returns(new WeatherForecastDto
             {
                 City = "Oslo",
                 TemperatureC = -15,
                 Summary = "Blizzard"
             });
    }
}
```

### Step 3: Run the Server Pointing to the Assembly

```bash
dotnet run --project src/TypedMock.Server -- \
  --mode=Replay \
  --port=5050 \
  --fixtures-dll=./bin/MyTestStandFixtures.dll
```

At startup, `TypedMock.Server` uses reflection or `AssemblyLoadContext` to scan `MyTestStandFixtures.dll`, instantiate each `IContractFixture<T>`, and register all rules into memory.

---

## 3. C# Code-First Fixture Generator (Exporting Verified JSON)

If your organization prefers having static JSON files stored in Git (e.g., for cross-language compatibility, non-.NET tools, or visual diffs in PRs), you can author scenarios in C# and **compile/export them to JSON**.

`ProgrammaticMockRule` contains a built-in `.ToFixture()` method that converts a C# rule into a `TypedFixture`:

```csharp
// Program.cs in a "FixtureGenerator" console tool
var store = new TypedFixtureStore();
var targetDirectory = "./fixtures";

void Register<T>(Action<ContractMockSetup<T>> configure) where T : class
{
    var setup = new ContractMockSetup<T>(async rule =>
    {
        var fixture = rule.ToFixture();
        await store.SaveFixtureAsync(targetDirectory, fixture);
    });
    configure(setup);
}

// Author fixtures in C#:
Register<IWeatherApi>(setup =>
{
    setup.Setup(x => x.GetForecastByCityAsync("London"))
         .WithScenario("default")
         .Returns(new WeatherForecastDto 
         { 
             City = "London", 
             TemperatureC = 22, 
             Summary = "Sunny" 
         });

    setup.Setup(x => x.GetForecastByCityAsync("London"))
         .WithScenario("RainyDay")
         .Returns(new WeatherForecastDto 
         { 
             City = "London", 
             TemperatureC = 14, 
             Summary = "Light Rain" 
         });
});
```

When executed, this outputs:
```
fixtures/
  └── IWeatherApi/
      └── GetForecastByCityAsync/
          ├── default.json
          └── RainyDay.json
```

**Benefits of this hybrid model:**
- Fixtures are generated from real C# types (preventing typos and invalid structures).
- Version-controlled JSON files remain readable and reviewable in pull requests.
- Can be automated as a step in CI/CD (`dotnet run --project FixtureGenerator`).

---

## 4. Reusable Object Builders with Bogus / Test Data Factories

Writing fixtures in C# unlocks the entire .NET test tooling ecosystem (such as [Bogus](https://github.com/bchavez/Bogus) for realistic fake data or custom domain builders):

```csharp
public class WeatherForecastBuilder
{
    private string _city = "London";
    private int _temperatureC = 20;
    private string _summary = "Sunny";

    public WeatherForecastBuilder ForCity(string city) { _city = city; return this; }
    public WeatherForecastBuilder AsStorm() { _temperatureC = 5; _summary = "Severe Storm"; return this; }
    public WeatherForecastBuilder AsHeatwave() { _temperatureC = 39; _summary = "Extreme Heat"; return this; }

    public WeatherForecastDto Build() => new()
    {
        City = _city,
        Date = DateOnly.FromDateTime(DateTime.Today),
        TemperatureC = _temperatureC,
        Summary = _summary
    };
}

// In your fixture setup:
setup.Setup(x => x.GetForecastByCityAsync("London"))
     .WithScenario("Storm")
     .Returns(new WeatherForecastBuilder().ForCity("London").AsStorm().Build());
```

---

## Comparison Matrix

| Feature | Raw JSON Fixtures | In-Memory C# Fixtures | C# Fixture DLLs | C# Generator $\rightarrow$ JSON |
| :--- | :--- | :--- | :--- | :--- |
| **Language** | Plain JSON | C# | C# | C# (Outputs JSON) |
| **Compile-Time Safety** | ❌ None | ✅ Complete | ✅ Complete | ✅ Complete |
| **IDE Refactoring Support** | ❌ None | ✅ Full (`F2` / Rename) | ✅ Full (`F2` / Rename) | ✅ Full (`F2` / Rename) |
| **IntelliSense Autocomplete** | ❌ None | ✅ Yes | ✅ Yes | ✅ Yes |
| **Works with Standalone Server** | ✅ Yes | ❌ In-process only | ✅ Yes (`--fixtures-dll`) | ✅ Yes (Serves generated JSON) |
| **Git Diff Visibility** | ✅ Raw JSON diff | ❌ In code only | ❌ In code only | ✅ JSON diffs in PRs |
| **Reusable Builders / Faker** | ❌ No | ✅ Yes | ✅ Yes | ✅ Yes |
