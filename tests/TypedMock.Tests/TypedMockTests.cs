using System.Net.Http.Json;
using System.Reflection;
using ExternalService.Contracts;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.Extensions.DependencyInjection;
using RestEase;
using TypedMock.Core;
using TypedMock.Core.Contracts;
using TypedMock.Core.Exceptions;
using TypedMock.Core.Generators;
using TypedMock.Core.Hosting;

namespace TypedMock.Tests;

public class TypedMockTests
{
    // =========================================================================
    // 1. RECORD & C# CODE GENERATION TESTS
    // =========================================================================

    [Fact]
    public async Task Record_GeneratesCompilableCSharpStubController()
    {
        await using var realService = await SampleExternalService.Program.StartServerAsync(port: 0);
        var realServiceAddresses = realService.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>();
        var realServiceUrl = realServiceAddresses!.Addresses.First();

        var mockOptions = new TypedMockOptions
        {
            TargetUrl = realServiceUrl
        };

        await using var mockServer = new TypedMockServer(mockOptions);
        mockServer.RegisterContract<IWeatherApi>();
        await mockServer.StartAsync();

        var client = RestClient.For<IWeatherApi>(mockServer.BaseUrl);
        await client.GetForecastsAsync();
        await client.GetForecastByCityAsync("London");
        await client.GetForecastByCityAsync("Paris");
        await client.CreateForecastAsync(new CreateForecastRequest
        {
            City = "Berlin",
            TemperatureC = 23,
            Summary = "Sunny and clear"
        });

        var generatedSource = mockServer.GenerateStubSourceCode<IWeatherApi>(new CSharpStubGeneratorOptions
        {
            Namespace = "ExternalService.Stubs",
            ClassName = "WeatherApiStubController"
        });

        generatedSource.Should().Contain("public partial class WeatherApiStubController : ControllerBase, IWeatherApi");
        generatedSource.Should().Contain("[HttpGet(\"weatherforecast\")]");
        generatedSource.Should().Contain("[HttpGet(\"weatherforecast/{city}\")]");
        generatedSource.Should().Contain("[HttpPost(\"weatherforecast\")]");
        generatedSource.Should().Contain("return (city?.ToLowerInvariant()) switch");
        generatedSource.Should().Contain("\"london\" =>");
        generatedSource.Should().Contain("\"paris\" =>");
        generatedSource.Should().Contain("return request switch");
        generatedSource.Should().Contain("City: \"Berlin\"");

        var assembly = CompileSourceToAssembly(generatedSource);
        var controllerType = assembly.GetType("ExternalService.Stubs.WeatherApiStubController");
        controllerType.Should().NotBeNull();
        typeof(IWeatherApi).IsAssignableFrom(controllerType).Should().BeTrue();
    }

    [Fact]
    public async Task Host_CompiledGeneratedStub_ServesTrafficOverHttp()
    {
        await using var realService = await SampleExternalService.Program.StartServerAsync(port: 0);
        var realServiceAddresses = realService.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>();
        var realServiceUrl = realServiceAddresses!.Addresses.First();

        await using var mockServer = new TypedMockServer(new TypedMockOptions
        {
            TargetUrl = realServiceUrl
        });
        mockServer.RegisterContract<IWeatherApi>();
        await mockServer.StartAsync();

        var recordingClient = RestClient.For<IWeatherApi>(mockServer.BaseUrl);
        await recordingClient.GetForecastsAsync();
        await recordingClient.GetForecastByCityAsync("London");
        await recordingClient.GetForecastByCityAsync("Paris");

        var sourceCode = mockServer.GenerateStubSourceCode<IWeatherApi>();
        var assembly = CompileSourceToAssembly(sourceCode);
        var controllerType = assembly.GetType("Generated.Stubs.WeatherApiStubController")!;

        // Test StubServerHost
        await using var stubHost = await StubServerHost.StartAsync(controllerType, port: 0);
        var stubUrl = StubServerHost.GetServerUrl(stubHost);
        var client = RestClient.For<IWeatherApi>(stubUrl);

        var london = await client.GetForecastByCityAsync("London");
        london.City.Should().Be("London");
        london.TemperatureC.Should().Be(12);

        var paris = await client.GetForecastByCityAsync("Paris");
        paris.City.Should().Be("Paris");
        paris.TemperatureC.Should().Be(18);

        var act = () => client.GetForecastByCityAsync("Tokyo");
        await act.Should().ThrowAsync<ApiException>()
            .Where(ex => ex.StatusCode == System.Net.HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task DeterministicPatternMatching_DistinctBodiesAndRepeatedCalls()
    {
        await using var realService = await SampleExternalService.Program.StartServerAsync(port: 0);
        var realServiceAddresses = realService.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>();
        var realServiceUrl = realServiceAddresses!.Addresses.First();

        await using var mockServer = new TypedMockServer(new TypedMockOptions
        {
            TargetUrl = realServiceUrl
        });
        mockServer.RegisterContract<IWeatherApi>();
        await mockServer.StartAsync();

        var recordingClient = RestClient.For<IWeatherApi>(mockServer.BaseUrl);

        var reqBerlin = new CreateForecastRequest { City = "Berlin", TemperatureC = 23, Summary = "Sunny" };
        await recordingClient.CreateForecastAsync(reqBerlin);

        var reqRome = new CreateForecastRequest { City = "Rome", TemperatureC = 28, Summary = "Hot" };
        await recordingClient.CreateForecastAsync(reqRome);

        // Repeated call to Berlin to test deduplication
        await recordingClient.CreateForecastAsync(reqBerlin);

        var sourceCode = mockServer.GenerateStubSourceCode<IWeatherApi>();
        sourceCode.Should().Contain("return request switch");
        sourceCode.Should().Contain("City: \"Berlin\"");
        sourceCode.Should().Contain("City: \"Rome\"");

        // Deduplication check: only 1 pattern case for Berlin in switch
        var berlinPatternMatches = sourceCode.Split("City: \"Berlin\"").Length - 1;
        berlinPatternMatches.Should().Be(1);

        var assembly = CompileSourceToAssembly(sourceCode);
        var controllerType = assembly.GetType("Generated.Stubs.WeatherApiStubController")!;

        await using var stubHost = await StubServerHost.StartAsync(controllerType, port: 0);
        var client = RestClient.For<IWeatherApi>(StubServerHost.GetServerUrl(stubHost));

        var stubBerlin = await client.CreateForecastAsync(reqBerlin);
        stubBerlin.City.Should().Be("Berlin");
        stubBerlin.TemperatureC.Should().Be(23);

        var stubRome = await client.CreateForecastAsync(reqRome);
        stubRome.City.Should().Be("Rome");
        stubRome.TemperatureC.Should().Be(28);

        // Repeated call returns identical deterministic response
        var stubBerlinRepeat = await client.CreateForecastAsync(reqBerlin);
        stubBerlinRepeat.City.Should().Be("Berlin");

        var unrecorded = new CreateForecastRequest { City = "Oslo", TemperatureC = 0, Summary = "Snow" };
        var act = () => client.CreateForecastAsync(unrecorded);
        await act.Should().ThrowAsync<ApiException>()
            .Where(ex => ex.StatusCode == System.Net.HttpStatusCode.NotFound);
    }

    // =========================================================================
    // 2. DYNAMIC CONTRACT DISCOVERY & MULTI-UPSTREAM TESTS
    // =========================================================================

    [Fact]
    public void DynamicContractDiscovery_LoadsContractsFromExternalAssembly()
    {
        var contractsAssemblyPath = typeof(IWeatherApi).Assembly.Location;
        File.Exists(contractsAssemblyPath).Should().BeTrue();

        var discovered = DynamicContractLoader.LoadContractsFromAssembly(contractsAssemblyPath);
        discovered.Should().Contain(typeof(IWeatherApi));
        discovered.Should().Contain(typeof(IPaymentApi));

        var mockServer = new TypedMockServer();
        mockServer.RegisterContractsFromAssembly(contractsAssemblyPath);

        mockServer.RegisteredEndpoints.Should().Contain(e => e.InterfaceType.Name == nameof(IWeatherApi));
        mockServer.RegisteredEndpoints.Should().Contain(e => e.InterfaceType.Name == nameof(IPaymentApi));
    }

    [Fact]
    public async Task MultiUpstreamRouting_DispatchesDistinctServices_AndSavesMultipleControllers()
    {
        await using var weatherService = await SampleExternalService.Program.StartServerAsync(port: 0);
        var weatherAddresses = weatherService.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>();
        var weatherUrl = weatherAddresses!.Addresses.First();

        var paymentBuilder = WebApplication.CreateEmptyBuilder(new WebApplicationOptions());
        paymentBuilder.Services.AddRouting();
        paymentBuilder.WebHost.UseKestrel(k => k.Listen(System.Net.IPAddress.Loopback, 0));
        var paymentApp = paymentBuilder.Build();
        paymentApp.MapPost("/payments/charge", (PaymentRequestDto req) => Microsoft.AspNetCore.Http.Results.Ok(new PaymentResponseDto
        {
            TransactionId = "tx_multi_42",
            Status = "Success",
            Amount = req.Amount
        }));
        await paymentApp.StartAsync();

        var paymentAddresses = paymentApp.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>();
        var paymentUrl = paymentAddresses!.Addresses.First();

        var tempBatchDir = Path.Combine(Path.GetTempPath(), "typedmock_multi_" + Guid.NewGuid().ToString("N"));

        try
        {
            var mockOptions = new TypedMockOptions
            {
                Upstreams =
                {
                    [nameof(IWeatherApi)] = weatherUrl,
                    [nameof(IPaymentApi)] = paymentUrl
                },
                OutputDirectory = tempBatchDir
            };

            await using var mockServer = new TypedMockServer(mockOptions);
            mockServer.RegisterContract<IWeatherApi>();
            mockServer.RegisterContract<IPaymentApi>();
            await mockServer.StartAsync();

            var weatherClient = RestClient.For<IWeatherApi>(mockServer.BaseUrl);
            var london = await weatherClient.GetForecastByCityAsync("London");
            london.City.Should().Be("London");

            var paymentClient = RestClient.For<IPaymentApi>(mockServer.BaseUrl);
            var paymentResult = await paymentClient.ChargeAsync(new PaymentRequestDto
            {
                OrderId = "ord_multi_1",
                Amount = 150m,
                Currency = "EUR"
            });
            paymentResult.TransactionId.Should().Be("tx_multi_42");
            paymentResult.Status.Should().Be("Success");

            var savedFiles = await mockServer.SaveAllStubsAsync(tempBatchDir);
            savedFiles.Should().HaveCount(2);

            var weatherStubFile = Path.Combine(tempBatchDir, "WeatherApiStubController.cs");
            var paymentStubFile = Path.Combine(tempBatchDir, "PaymentApiStubController.cs");

            File.Exists(weatherStubFile).Should().BeTrue();
            File.Exists(paymentStubFile).Should().BeTrue();

            var weatherCode = await File.ReadAllTextAsync(weatherStubFile);
            weatherCode.Should().Contain("public partial class WeatherApiStubController : ControllerBase, IWeatherApi");

            var paymentCode = await File.ReadAllTextAsync(paymentStubFile);
            paymentCode.Should().Contain("public partial class PaymentApiStubController : ControllerBase, IPaymentApi");
            paymentCode.Should().Contain("OrderId: \"ord_multi_1\"");

            CompileSourceToAssembly(weatherCode).Should().NotBeNull();
            CompileSourceToAssembly(paymentCode).Should().NotBeNull();
        }
        finally
        {
            await paymentApp.StopAsync();
            await paymentApp.DisposeAsync();

            if (Directory.Exists(tempBatchDir))
            {
                Directory.Delete(tempBatchDir, true);
            }
        }
    }

    // =========================================================================
    // 3. STUB CONTROLLER HOSTING & SCENARIO TESTS
    // =========================================================================

    [Fact]
    public async Task StubController_ReturnsConfiguredDto()
    {
        await using var stubHost = await StubServerHost.StartAsync<TestWeatherStubController>(port: 0);
        var url = StubServerHost.GetServerUrl(stubHost);

        using var httpClient = new HttpClient { BaseAddress = new Uri(url) };
        var response = await httpClient.GetAsync("/weather-test/ControllerLondon");
        response.IsSuccessStatusCode.Should().BeTrue();
        var result = await response.Content.ReadFromJsonAsync<WeatherForecastDto>();

        result!.City.Should().Be("ControllerLondon");
        result.TemperatureC.Should().Be(19);
    }

    [Fact]
    public async Task ErrorSimulation_Returns500Status()
    {
        await using var stubHost = await StubServerHost.StartAsync<ErrorThrowingStubController>(port: 0);
        var url = StubServerHost.GetServerUrl(stubHost);

        using var httpClient = new HttpClient { BaseAddress = new Uri(url) };
        var response = await httpClient.GetAsync("/error-test");
        response.StatusCode.Should().Be(System.Net.HttpStatusCode.InternalServerError);
    }

    [Fact]
    public async Task ScenarioHeader_MatchesSpecificScenario()
    {
        await using var stubHost = await StubServerHost.StartAsync<ScenarioInspectingStubController>(port: 0);
        var url = StubServerHost.GetServerUrl(stubHost);

        using var httpClient = new HttpClient { BaseAddress = new Uri(url) };
        httpClient.DefaultRequestHeaders.Add("X-Mock-Scenario", "TyphoonAlert");

        var response = await httpClient.GetAsync("/scenario-inspect/Tokyo");
        response.IsSuccessStatusCode.Should().BeTrue();

        var result = await response.Content.ReadFromJsonAsync<WeatherForecastDto>();
        result!.Summary.Should().Be("TyphoonAlert");
    }

    [Fact]
    public async Task UnmatchedRequest_Returns404NotFound()
    {
        await using var stubHost = await StubServerHost.StartAsync<TestWeatherStubController>(port: 0);
        var url = StubServerHost.GetServerUrl(stubHost);

        using var httpClient = new HttpClient { BaseAddress = new Uri(url) };
        var response = await httpClient.GetAsync("/weather-test/UnknownCity");
        response.StatusCode.Should().Be(System.Net.HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task StubServerHost_HostsStubController_AndServesTraffic()
    {
        await using var stubHost = await StubServerHost.StartAsync<TestWeatherStubController>(port: 0);
        var url = StubServerHost.GetServerUrl(stubHost);

        using var httpClient = new HttpClient { BaseAddress = new Uri(url) };
        var forecastsResp = await httpClient.GetAsync("/weather-test");
        forecastsResp.IsSuccessStatusCode.Should().BeTrue();
        var forecasts = await forecastsResp.Content.ReadFromJsonAsync<List<WeatherForecastDto>>();
        forecasts.Should().ContainSingle(f => f.City == "ControllerCity");

        var londonResp = await httpClient.GetAsync("/weather-test/ControllerLondon");
        londonResp.IsSuccessStatusCode.Should().BeTrue();
        var london = await londonResp.Content.ReadFromJsonAsync<WeatherForecastDto>();
        london!.City.Should().Be("ControllerLondon");
        london.TemperatureC.Should().Be(19);
    }

    [Fact]
    public async Task ScenarioContextMiddleware_PropagatesScenarioToContext()
    {
        await using var stubHost = await StubServerHost.StartAsync<ScenarioInspectingStubController>(port: 0);
        var url = StubServerHost.GetServerUrl(stubHost);

        using var httpClient = new HttpClient { BaseAddress = new Uri(url) };
        httpClient.DefaultRequestHeaders.Add("X-Mock-Scenario", "CustomScenario");

        var response = await httpClient.GetAsync("/scenario-inspect/AnyCity");
        response.IsSuccessStatusCode.Should().BeTrue();
        var dto = await response.Content.ReadFromJsonAsync<WeatherForecastDto>();
        dto.Should().NotBeNull();
        dto!.Summary.Should().Be("CustomScenario");
    }

    // =========================================================================
    // 4. CONTRACT DRIFT TESTS (COMPILE-TIME & RUNTIME)
    // =========================================================================

    [Fact]
    public void CompileTimeDrift_WhenContractChanges_RoslynCompilationFails()
    {
        var interactions = new List<RecordedInteraction>
        {
            new()
            {
                Endpoint = RestEaseContractScanner.Scan<IWeatherApi>()
                    .First(e => e.Method.Name == nameof(IWeatherApi.GetForecastsAsync)),
                StatusCode = 200,
                DeserializedResponse = new List<WeatherForecastDto>()
            }
        };

        var generatedSource = CSharpStubGenerator.Generate<IWeatherApi>(interactions);

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

        var act = () => CompileSourceToAssembly(brokenStubSource, alteredContractSource);
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*CS0535*"); // CS0535: does not implement interface member
    }

    [Fact]
    public async Task RuntimeDrift_WhenUpstreamReturnsCorruptedSchema_ThrowsContractValidationException()
    {
        var builder = WebApplication.CreateEmptyBuilder(new WebApplicationOptions());
        builder.Services.AddRouting();
        builder.WebHost.UseKestrel(k => k.Listen(System.Net.IPAddress.Loopback, 0));
        var app = builder.Build();
        app.MapGet("/weatherforecast/London", () => Microsoft.AspNetCore.Http.Results.Content(
            "{\"city\":\"London\",\"temperatureC\":\"NOT_AN_INTEGER\"}", "application/json"));
        await app.StartAsync();

        var serverAddresses = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>();
        var corruptedServiceUrl = serverAddresses!.Addresses.First();

        try
        {
            await using var mockServer = new TypedMockServer(new TypedMockOptions
            {
                TargetUrl = corruptedServiceUrl
            });
            mockServer.RegisterContract<IWeatherApi>();
            await mockServer.StartAsync();

            var client = RestClient.For<IWeatherApi>(mockServer.BaseUrl);
            var act = () => client.GetForecastByCityAsync("London");
            await act.Should().ThrowAsync<ApiException>();
        }
        finally
        {
            await app.StopAsync();
            await app.DisposeAsync();
        }
    }

    // =========================================================================
    // HELPER: Dynamic In-Memory Roslyn Compilation
    // =========================================================================

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

        references.Add(MetadataReference.CreateFromFile(typeof(IWeatherApi).Assembly.Location));
        references.Add(MetadataReference.CreateFromFile(typeof(StubHttpException).Assembly.Location));
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

[ApiController]
[Route("weather-test")]
public class TestWeatherStubController : ControllerBase
{
    [HttpGet]
    public Task<List<WeatherForecastDto>> GetForecastsAsync() =>
        Task.FromResult(new List<WeatherForecastDto>
        {
            new() { City = "ControllerCity", TemperatureC = 20, Summary = "From Controller" }
        });

    [HttpGet("{city}")]
    public Task<WeatherForecastDto> GetForecastByCityAsync([FromRoute(Name = "city")] string city)
    {
        if (string.Equals(city, "ControllerLondon", StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult(new WeatherForecastDto
            {
                City = "ControllerLondon",
                TemperatureC = 19,
                Summary = "Controller Weather"
            });
        }

        throw new KeyNotFoundException($"No stub for {city}");
    }

    [HttpPost]
    public Task<WeatherForecastDto> CreateForecastAsync([FromBody] CreateForecastRequest request) =>
        Task.FromResult(new WeatherForecastDto { City = request.City, TemperatureC = request.TemperatureC, Summary = request.Summary });
}

[ApiController]
[Route("error-test")]
public class ErrorThrowingStubController : ControllerBase
{
    [HttpGet]
    public IActionResult GetError() => StatusCode(500, new { error = "Internal Database Timeout" });
}

[ApiController]
[Route("scenario-inspect")]
public class ScenarioInspectingStubController : ControllerBase
{
    [HttpGet("{city}")]
    public Task<WeatherForecastDto> GetForecastByCityAsync([FromRoute(Name = "city")] string city)
    {
        var scenario = HttpContext.Items["TypedMock.Scenario"] as string ?? "none";
        return Task.FromResult(new WeatherForecastDto
        {
            City = city,
            TemperatureC = 20,
            Summary = scenario
        });
    }
}
