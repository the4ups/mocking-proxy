using System.Reflection;
using ExternalService.Contracts;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.Extensions.DependencyInjection;
using RestEase;
using TypedMock.Core;
using TypedMock.Core.Exceptions;
using TypedMock.Core.Generators;
using TypedMock.Core.Hosting;

namespace TypedMock.Tests;

public class CSharpStubIntegrationTests
{
    [Fact]
    public async Task Test1_TrafficRecording_GeneratesValidCompilableCSharpStub()
    {
        // 1. Arrange: Start real external service
        await using var realService = await SampleExternalService.Program.StartServerAsync(port: 0);
        var realServiceAddresses = realService.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>();
        var realServiceUrl = realServiceAddresses!.Addresses.First();

        // 2. Start TypedMock in Record mode
        var mockOptions = new TypedMockOptions
        {
            Mode = MockMode.Record,
            TargetUrl = realServiceUrl
        };

        await using var mockServer = new TypedMockServer(mockOptions);
        mockServer.RegisterContract<IWeatherApi>();
        await mockServer.StartAsync();

        // 3. Client calls multiple endpoints through the mock proxy
        var client = RestClient.For<IWeatherApi>(mockServer.BaseUrl);
        var allForecasts = await client.GetForecastsAsync();
        var london = await client.GetForecastByCityAsync("London");
        var paris = await client.GetForecastByCityAsync("Paris");
        var created = await client.CreateForecastAsync(new CreateForecastRequest
        {
            City = "Berlin",
            TemperatureC = 23,
            Summary = "Sunny and clear"
        });

        london.City.Should().Be("London");
        paris.City.Should().Be("Paris");
        created.City.Should().Be("Berlin");
        allForecasts.Should().HaveCount(3);

        // 4. Act: Generate C# Stub Controller source code from recorded traffic
        var generatedSource = mockServer.GenerateStubSourceCode<IWeatherApi>(new CSharpStubGeneratorOptions
        {
            Namespace = "ExternalService.Stubs",
            ClassName = "WeatherApiStubController"
        });

        // 5. Assert: Inspect generated source structure
        generatedSource.Should().Contain("public partial class WeatherApiStubController : ControllerBase, IWeatherApi");
        generatedSource.Should().Contain("[HttpGet(\"weatherforecast\")]");
        generatedSource.Should().Contain("[HttpGet(\"weatherforecast/{city}\")]");
        generatedSource.Should().Contain("[HttpPost(\"weatherforecast\")]");
        generatedSource.Should().Contain("return (city?.ToLowerInvariant()) switch");
        generatedSource.Should().Contain("\"london\" =>");
        generatedSource.Should().Contain("\"paris\" =>");
        generatedSource.Should().Contain("protected virtual Task<WeatherForecastDto?> OnGetForecastByCityAsync");
        generatedSource.Should().NotContain("TemperatureF ="); // Computed getter-only property must not be emitted

        // 6. Assert: Compile generated code in-memory with Roslyn
        var assembly = CompileSourceToAssembly(generatedSource);
        assembly.Should().NotBeNull();

        var controllerType = assembly.GetType("ExternalService.Stubs.WeatherApiStubController");
        controllerType.Should().NotBeNull();
        typeof(IWeatherApi).IsAssignableFrom(controllerType).Should().BeTrue();
    }

    [Fact]
    public async Task Test2_GeneratedStub_HostedOverHttp_ServesRecordedTrafficToRestEaseClient()
    {
        // 1. Arrange: Record traffic to produce stub source
        await using var realService = await SampleExternalService.Program.StartServerAsync(port: 0);
        var realServiceAddresses = realService.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>();
        var realServiceUrl = realServiceAddresses!.Addresses.First();

        await using var mockServer = new TypedMockServer(new TypedMockOptions
        {
            Mode = MockMode.Record,
            TargetUrl = realServiceUrl
        });
        mockServer.RegisterContract<IWeatherApi>();
        await mockServer.StartAsync();

        var recordingClient = RestClient.For<IWeatherApi>(mockServer.BaseUrl);
        await recordingClient.GetForecastsAsync();
        await recordingClient.GetForecastByCityAsync("London");
        await recordingClient.GetForecastByCityAsync("Paris");

        var sourceCode = mockServer.GenerateStubSourceCode<IWeatherApi>();

        // 2. Compile generated C# stub into an assembly
        var assembly = CompileSourceToAssembly(sourceCode);
        var controllerType = assembly.GetType("Generated.Stubs.WeatherApiStubController")!;

        // 3. Act: Host the generated controller over HTTP using StubServerHost (port 0)
        await using var stubHost = await StubServerHost.StartAsync(controllerType, port: 0);
        var stubUrl = StubServerHost.GetServerUrl(stubHost);

        // 4. Assert: Consumer client talks to the compiled stub over HTTP
        var client = RestClient.For<IWeatherApi>(stubUrl);

        var london = await client.GetForecastByCityAsync("London");
        london.City.Should().Be("London");
        london.TemperatureC.Should().Be(12);

        var paris = await client.GetForecastByCityAsync("Paris");
        paris.City.Should().Be("Paris");
        paris.TemperatureC.Should().Be(18);

        var all = await client.GetForecastsAsync();
        all.Should().HaveCount(3);

        // Unknown city returns 404
        var act = () => client.GetForecastByCityAsync("Tokyo");
        await act.Should().ThrowAsync<ApiException>()
            .Where(ex => ex.StatusCode == System.Net.HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Test3_ManualScenarioHooking_AllowsSubclassOverridesAndFaultInjection()
    {
        // 1. Generate base stub code with recorded London & Paris data
        var interactions = new List<RecordedInteraction>
        {
            new()
            {
                Endpoint = TypedMock.Core.Contracts.RestEaseContractScanner.Scan<IWeatherApi>()
                    .First(e => e.Method.Name == nameof(IWeatherApi.GetForecastByCityAsync)),
                PathParameters = new() { { "city", "London" } },
                StatusCode = 200,
                ResponseBody = "{}",
                DeserializedResponse = new WeatherForecastDto
                {
                    City = "London",
                    TemperatureC = 15,
                    Summary = "Recorded London Base",
                    Date = DateOnly.FromDateTime(DateTime.Today)
                }
            }
        };

        var baseSource = CSharpStubGenerator.Generate<IWeatherApi>(interactions, new CSharpStubGeneratorOptions
        {
            Namespace = "ExternalService.Stubs",
            ClassName = "BaseWeatherStubController",
            IsAbstract = true
        });

        // 2. User-written subclass implementing custom scenarios (hooks)
        var customSubclassSource = """
            using System;
            using System.Threading.Tasks;
            using Microsoft.AspNetCore.Mvc;
            using ExternalService.Contracts;
            using TypedMock.Core.Exceptions;

            namespace ExternalService.Stubs;

            [ApiController]
            public class CustomScenarioWeatherController : BaseWeatherStubController
            {
                protected override Task<WeatherForecastDto?> OnGetForecastByCityAsync(string city)
                {
                    // Scenario 1: Custom logic for dynamic city
                    if (string.Equals(city, "StormCity", StringComparison.OrdinalIgnoreCase))
                    {
                        return Task.FromResult<WeatherForecastDto?>(new WeatherForecastDto
                        {
                            City = "StormCity",
                            TemperatureC = -10,
                            Summary = "Blizzard Warning",
                            Date = DateOnly.FromDateTime(DateTime.Today)
                        });
                    }

                    // Scenario 2: Chaos / fault injection
                    if (string.Equals(city, "BrokenStation", StringComparison.OrdinalIgnoreCase))
                    {
                        throw new StubHttpException(503, "Sensor communication timeout");
                    }

                    // Return null to fall through to baseline recorded data!
                    return Task.FromResult<WeatherForecastDto?>(null);
                }
            }
            """;

        // 3. Compile both base and custom scenario into one assembly
        var assembly = CompileSourceToAssembly(baseSource, customSubclassSource);
        var customControllerType = assembly.GetType("ExternalService.Stubs.CustomScenarioWeatherController")!;

        // 4. Host custom scenario controller over HTTP
        await using var stubHost = await StubServerHost.StartAsync(customControllerType, port: 0);
        var stubUrl = StubServerHost.GetServerUrl(stubHost);
        var client = RestClient.For<IWeatherApi>(stubUrl);

        // Custom scenario 1: returns custom data
        var storm = await client.GetForecastByCityAsync("StormCity");
        storm.City.Should().Be("StormCity");
        storm.TemperatureC.Should().Be(-10);
        storm.Summary.Should().Be("Blizzard Warning");

        // Custom scenario 2: returns 503 error
        var actFault = () => client.GetForecastByCityAsync("BrokenStation");
        await actFault.Should().ThrowAsync<ApiException>()
            .Where(ex => ex.StatusCode == System.Net.HttpStatusCode.ServiceUnavailable);

        // Recorded baseline fallback: London still works
        var london = await client.GetForecastByCityAsync("London");
        london.City.Should().Be("London");
        london.Summary.Should().Be("Recorded London Base");
    }

    [Fact]
    public void Test4_ContractDrift_FailsAtCompileTime()
    {
        // 1. Suppose a stub was generated implementing IWeatherApi
        var interactions = new List<RecordedInteraction>();
        var generatedSource = CSharpStubGenerator.Generate<IWeatherApi>(interactions, new CSharpStubGeneratorOptions
        {
            Namespace = "ExternalService.Stubs",
            ClassName = "DriftTestWeatherController"
        });

        // 2. Simulate contract drift: A new contract version adds a method or changes signature
        // If the contract requires a new method 'GetRadarMapAsync()', the generated class fails to compile:
        var alteredContractSource = """
            using System.Threading.Tasks;
            using ExternalService.Contracts;

            namespace ExternalService.Contracts.Drifted;

            public interface IDriftedWeatherApi : IWeatherApi
            {
                Task<string> GetRadarMapAsync();
            }
            """;

        var brokenStubSource = generatedSource.Replace("IWeatherApi", "ExternalService.Contracts.Drifted.IDriftedWeatherApi");

        // Act & Assert: Roslyn compilation MUST fail because IDriftedWeatherApi member is not implemented
        var act = () => CompileSourceToAssembly(brokenStubSource, alteredContractSource);
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*CS0535*"); // CS0535: does not implement interface member
    }

    [Fact]
    public async Task Test5_GeneratedStub_CanBeUsedDirectlyInMemoryWithoutHttp()
    {
        // Arrange
        var interactions = new List<RecordedInteraction>
        {
            new()
            {
                Endpoint = TypedMock.Core.Contracts.RestEaseContractScanner.Scan<IWeatherApi>()
                    .First(e => e.Method.Name == nameof(IWeatherApi.GetForecastByCityAsync)),
                PathParameters = new() { { "city", "London" } },
                StatusCode = 200,
                ResponseBody = "{}",
                DeserializedResponse = new WeatherForecastDto
                {
                    City = "London",
                    TemperatureC = 20,
                    Summary = "Sunny",
                    Date = DateOnly.FromDateTime(DateTime.Today)
                }
            }
        };

        var sourceCode = CSharpStubGenerator.Generate<IWeatherApi>(interactions);
        var assembly = CompileSourceToAssembly(sourceCode);
        var controllerType = assembly.GetType("Generated.Stubs.WeatherApiStubController")!;

        // Act: Instantiate stub directly as IWeatherApi in memory (no HTTP server)
        var stubInstance = (IWeatherApi)Activator.CreateInstance(controllerType)!;
        var forecast = await stubInstance.GetForecastByCityAsync("London");

        // Assert
        forecast.Should().NotBeNull();
        forecast.City.Should().Be("London");
        forecast.TemperatureC.Should().Be(20);
        forecast.Summary.Should().Be("Sunny");
    }

    [Fact]
    public async Task Test6_StubServerHost_WhenNonControllerTypeProvided_ThrowsArgumentException()
    {
        var act = () => StubServerHost.StartAsync(typeof(CSharpStubIntegrationTests));
        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*must inherit from ControllerBase*");
    }

    private static Assembly CompileSourceToAssembly(string sourceCode, params string[] additionalSources)
    {
        var syntaxTrees = new List<SyntaxTree>
        {
            CSharpSyntaxTree.ParseText(sourceCode)
        };

        foreach (var src in additionalSources)
        {
            syntaxTrees.Add(CSharpSyntaxTree.ParseText(src));
        }

        var references = new List<MetadataReference>();

        // Load all platform assemblies from runtime context
        if (AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") is string tpa)
        {
            var paths = tpa.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);
            foreach (var path in paths)
            {
                references.Add(MetadataReference.CreateFromFile(path));
            }
        }
        else
        {
            foreach (var a in AppDomain.CurrentDomain.GetAssemblies().Where(a => !a.IsDynamic && !string.IsNullOrWhiteSpace(a.Location)))
            {
                references.Add(MetadataReference.CreateFromFile(a.Location));
            }
        }

        // Ensure contracts and TypedMock assemblies are explicitly referenced
        references.Add(MetadataReference.CreateFromFile(typeof(IWeatherApi).Assembly.Location));
        references.Add(MetadataReference.CreateFromFile(typeof(TypedMock.Core.Exceptions.StubHttpException).Assembly.Location));
        references.Add(MetadataReference.CreateFromFile(typeof(Microsoft.AspNetCore.Mvc.ControllerBase).Assembly.Location));

        var compilation = CSharpCompilation.Create(
            "DynamicStubs_" + Guid.NewGuid().ToString("N"),
            syntaxTrees,
            references.DistinctBy(r => r.Display),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        using var ms = new MemoryStream();
        var emitResult = compilation.Emit(ms);

        if (!emitResult.Success)
        {
            var errors = string.Join(Environment.NewLine, emitResult.Diagnostics
                .Where(d => d.Severity == DiagnosticSeverity.Error)
                .Select(d => d.ToString()));
            throw new InvalidOperationException($"Roslyn compilation failed:\n{errors}\n\nSource:\n{sourceCode}");
        }

        ms.Seek(0, SeekOrigin.Begin);
        return Assembly.Load(ms.ToArray());
    }
}
