using ExternalService.Contracts;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using RestEase;
using SampleClientApp;
using TypedMock.Core;
using TypedMock.Core.Exceptions;
using TypedMock.Core.Hosting;
using TypedMock.Core.Proxy;
using TypedMock.Core.Scenarios;

namespace TypedMock.Tests;

public class TypedMockIntegrationTests
{
    [Fact]
    public async Task Scenario1_RecordTraffic_FromRealService_CapturesAndValidatesTypedInteractions()
    {
        // 1. Arrange: Start the REAL external service
        await using var realService = await SampleExternalService.Program.StartServerAsync(port: 0);
        var realServiceAddresses = realService.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>();
        var realServiceUrl = realServiceAddresses!.Addresses.First();

        // 2. Start TypedMock in RECORD mode, pointing to the real service
        var mockOptions = new TypedMockOptions
        {
            Mode = MockMode.Record,
            TargetUrl = realServiceUrl
        };

        await using var mockServer = new TypedMockServer(mockOptions);
        mockServer.RegisterContract<IWeatherApi>();
        await mockServer.StartAsync();

        // 3. Client calls the Mock Proxy (thinking it's the external service)
        var client = WeatherReportService.Create(mockServer.BaseUrl);

        var summary = await client.GetCitySummaryAsync("London");
        var avgTemp = await client.GetAverageTemperatureAsync();

        // Verify client got real service data
        summary.Should().Contain("London: 12°C");
        avgTemp.Should().Be(17); // (12 + 18 + 22) / 3 = 17.33 -> 17

        // 4. Assert: In-memory interactions were recorded and validated against DTOs (Zero-JSON on disk)
        mockServer.RecordedInteractions.Should().HaveCount(2);

        var londonInteraction = mockServer.RecordedInteractions.First(i => i.Endpoint.Method.Name == "GetForecastByCityAsync");
        var dto = londonInteraction.DeserializedResponse as WeatherForecastDto;
        dto.Should().NotBeNull();
        dto!.City.Should().Be("London");
        dto.TemperatureC.Should().Be(12);

        // 5. Assert: Can generate clean C# code directly from recorded interactions
        var csharpCode = mockServer.GenerateStubSourceCode<IWeatherApi>();
        csharpCode.Should().Contain("public partial class WeatherApiStubController : ControllerBase, IWeatherApi");
        csharpCode.Should().Contain("\"london\" =>");
    }

    [Fact]
    public async Task Scenario2_ReplayTraffic_WithoutRealService_ReturnsValidContractData()
    {
        // 1. Arrange: Start Mock Server in REPLAY mode with a typed programmatic setup (Real service is OFFLINE)
        var mockOptions = new TypedMockOptions
        {
            Mode = MockMode.Replay
        };

        await using var mockServer = new TypedMockServer(mockOptions);
        mockServer.For<IWeatherApi>()
            .Setup(x => x.GetForecastByCityAsync("Paris"))
            .Returns(new WeatherForecastDto
            {
                City = "Paris",
                Date = new DateOnly(2026, 11, 4),
                TemperatureC = 25,
                Summary = "Warm & Sunny"
            });

        await mockServer.StartAsync();

        // 2. Act: Client calls the mock server
        var client = WeatherReportService.Create(mockServer.BaseUrl);
        var summary = await client.GetCitySummaryAsync("Paris");

        // 3. Assert: Data was served by the mock without any real external service running
        summary.Should().Be("Paris: 25°C (76°F), Warm & Sunny");
    }

    [Fact]
    public async Task Scenario3_ProgrammaticTypeSafeSetup_AllowsNewTestCases_WithoutRawJson()
    {
        // 1. Arrange: Start mock server in Replay mode
        var mockOptions = new TypedMockOptions { Mode = MockMode.Replay };
        await using var mockServer = new TypedMockServer(mockOptions);

        // Strongly-typed programmatic setup directly in C# using RestEase interface!
        mockServer.For<IWeatherApi>()
            .Setup(x => x.GetForecastByCityAsync("Atlantis"))
            .Returns(new WeatherForecastDto
            {
                City = "Atlantis",
                Date = DateOnly.FromDateTime(DateTime.Today),
                TemperatureC = 30,
                Summary = "Tropical Underwater"
            });

        await mockServer.StartAsync();

        // 2. Act
        var client = WeatherReportService.Create(mockServer.BaseUrl);
        var result = await client.GetCitySummaryAsync("Atlantis");

        // 3. Assert
        result.Should().Be("Atlantis: 30°C (85°F), Tropical Underwater");
    }

    [Fact]
    public async Task Scenario4_ErrorSimulation_SimulatesDependencyFailure()
    {
        // 1. Arrange: Configure an endpoint to return 500 Internal Server Error
        var mockOptions = new TypedMockOptions { Mode = MockMode.Replay };
        await using var mockServer = new TypedMockServer(mockOptions);

        mockServer.For<IWeatherApi>()
            .Setup(x => x.GetForecastByCityAsync("BrokenCity"))
            .ReturnsStatus(500, new { error = "Internal Database Timeout" });

        await mockServer.StartAsync();

        // 2. Act & Assert: Client handles the error cleanly
        var client = WeatherReportService.Create(mockServer.BaseUrl);

        var act = async () => await client.GetCitySummaryAsync("BrokenCity");
        var ex = await act.Should().ThrowAsync<ApiException>();
        ex.Which.StatusCode.Should().Be(System.Net.HttpStatusCode.InternalServerError);
    }

    [Fact]
    public async Task Scenario5_ContractDrift_DetectsSchemaMismatch_FailsValidationImmediately()
    {
        // Arrange: Start a fake service returning schema-incompatible JSON (temperatureC is string)
        var builder = Microsoft.AspNetCore.Builder.WebApplication.CreateEmptyBuilder(new Microsoft.AspNetCore.Builder.WebApplicationOptions());
        builder.Services.AddRouting();
        builder.WebHost.UseKestrel(k => k.Listen(System.Net.IPAddress.Loopback, 0));
        var app = builder.Build();
        app.MapGet("/weatherforecast/London", () => Microsoft.AspNetCore.Http.Results.Content(
            "{\"city\":\"London\",\"temperatureC\":\"NOT_A_VALID_INT\"}", "application/json"));
        await app.StartAsync();

        var serverAddresses = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>();
        var corruptedServiceUrl = serverAddresses!.Addresses.First();

        try
        {
            await using var mockServer = new TypedMockServer(new TypedMockOptions
            {
                Mode = MockMode.Record,
                TargetUrl = corruptedServiceUrl
            });
            mockServer.RegisterContract<IWeatherApi>();
            await mockServer.StartAsync();

            var client = RestClient.For<IWeatherApi>(mockServer.BaseUrl);

            // Act & Assert: Proxy detects contract drift during deserialization and fails fast
            var act = () => client.GetForecastByCityAsync("London");
            await act.Should().ThrowAsync<ApiException>();
        }
        finally
        {
            await app.StopAsync();
            await app.DisposeAsync();
        }
    }

    [Fact]
    public async Task Scenario6_PostRequestWithBody_RecordsAndGeneratesTypedStubCorrectly()
    {
        // 1. Arrange: Start REAL service
        await using var realService = await SampleExternalService.Program.StartServerAsync(port: 0);
        var realServiceAddresses = realService.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>();
        var realServiceUrl = realServiceAddresses!.Addresses.First();

        // Record mode
        var recordOptions = new TypedMockOptions
        {
            Mode = MockMode.Record,
            TargetUrl = realServiceUrl
        };
        await using var recordServer = new TypedMockServer(recordOptions);
        recordServer.RegisterContract<IWeatherApi>();
        await recordServer.StartAsync();

        var client = WeatherReportService.Create(recordServer.BaseUrl);
        var created = await client.AddForecastAsync("Berlin", 16, "Partly Cloudy");
        created.City.Should().Be("Berlin");
        created.TemperatureC.Should().Be(16);

        await recordServer.StopAsync();

        // 2. Assert interaction recorded with typed request body
        recordServer.RecordedInteractions.Should().HaveCount(1);
        var postInteraction = recordServer.RecordedInteractions.First();
        var reqDto = postInteraction.DeserializedRequestBody as CreateForecastRequest;
        reqDto.Should().NotBeNull();
        reqDto!.City.Should().Be("Berlin");
        reqDto.TemperatureC.Should().Be(16);

        // 3. Assert generated code uses deterministic pattern matching for request DTO
        var generatedCode = recordServer.GenerateStubSourceCode<IWeatherApi>();
        generatedCode.Should().Contain("return request switch");
        generatedCode.Should().Contain("City: \"Berlin\"");
    }

    [Fact]
    public async Task Scenario7_WithScenario_MatchesSpecificScenarioCorrectly()
    {
        // Arrange
        var mockOptions = new TypedMockOptions { Mode = MockMode.Replay };
        await using var mockServer = new TypedMockServer(mockOptions);

        mockServer.For<IWeatherApi>()
            .Setup(x => x.GetForecastByCityAsync("Tokyo"))
            .WithScenario("TyphoonAlert")
            .Returns(new WeatherForecastDto
            {
                City = "Tokyo",
                Date = DateOnly.FromDateTime(DateTime.Today),
                TemperatureC = 14,
                Summary = "Heavy Rain & Wind"
            });

        await mockServer.StartAsync();

        // Act with scenario header
        using var client = new HttpClient { BaseAddress = new Uri(mockServer.BaseUrl) };
        client.DefaultRequestHeaders.Add("X-Mock-Scenario", "TyphoonAlert");
        var restEaseClient = RestClient.For<IWeatherApi>(client);

        var result = await restEaseClient.GetForecastByCityAsync("Tokyo");

        // Assert
        result.City.Should().Be("Tokyo");
        result.Summary.Should().Be("Heavy Rain & Wind");
    }

    [Fact]
    public async Task Scenario8_ContractDeduplication_DoesNotDuplicateEndpointsOnMultipleCalls()
    {
        var mockOptions = new TypedMockOptions { Mode = MockMode.Replay };
        await using var mockServer = new TypedMockServer(mockOptions);

        // Multiple setups for the same contract
        mockServer.For<IWeatherApi>()
            .Setup(x => x.GetForecastByCityAsync("Oslo"))
            .Returns(new WeatherForecastDto { City = "Oslo", TemperatureC = 2, Summary = "Freezing" });

        mockServer.For<IWeatherApi>()
            .Setup(x => x.GetForecastByCityAsync("Madrid"))
            .Returns(new WeatherForecastDto { City = "Madrid", TemperatureC = 28, Summary = "Hot" });

        mockServer.RegisterContract<IWeatherApi>();

        await mockServer.StartAsync();

        var client = WeatherReportService.Create(mockServer.BaseUrl);
        var oslo = await client.GetCitySummaryAsync("Oslo");
        var madrid = await client.GetCitySummaryAsync("Madrid");

        oslo.Should().Contain("Oslo");
        madrid.Should().Contain("Madrid");
    }

    [Fact]
    public async Task Scenario9_UnmatchedRequest_Returns404RatherThanSilentFallback()
    {
        var mockOptions = new TypedMockOptions { Mode = MockMode.Replay };
        await using var mockServer = new TypedMockServer(mockOptions);
        mockServer.RegisterContract<IWeatherApi>();
        await mockServer.StartAsync();

        var client = RestClient.For<IWeatherApi>(mockServer.BaseUrl);

        var act = async () => await client.GetForecastByCityAsync("UnregisteredCity");
        var ex = await act.Should().ThrowAsync<ApiException>();
        ex.Which.StatusCode.Should().Be(System.Net.HttpStatusCode.NotFound);
    }
}
