using ExternalService.Contracts;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using RestEase;
using SampleClientApp;
using TypedMock.Core;
using TypedMock.Core.Fixtures;

namespace TypedMock.Tests;

public class TypedMockIntegrationTests
{
    [Fact]
    public async Task Scenario1_RecordTraffic_FromRealService_CapturesAndValidatesTypedFixtures()
    {
        // 1. Arrange: Start the REAL external service
        await using var realService = await SampleExternalService.Program.StartServerAsync(port: 0);
        var realServiceAddresses = realService.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>();
        var realServiceUrl = realServiceAddresses!.Addresses.First();

        var tempFixturesDir = Path.Combine(Path.GetTempPath(), "typedmock_fixtures_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempFixturesDir);

        try
        {
            // 2. Start TypedMock in RECORD mode, pointing to the real service
            var mockOptions = new TypedMockOptions
            {
                Mode = MockMode.Record,
                TargetUrl = realServiceUrl,
                FixturesDirectory = tempFixturesDir
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

            // 4. Assert: Fixtures were captured and saved to disk
            var londonFixturePath = Path.Combine(tempFixturesDir, "IWeatherApi", "GetForecastByCityAsync", "city_London.json");
            File.Exists(londonFixturePath).Should().BeTrue("TypedMock should have recorded the London request");

            var allForecastsFixturePath = Path.Combine(tempFixturesDir, "IWeatherApi", "GetForecastsAsync", "default.json");
            File.Exists(allForecastsFixturePath).Should().BeTrue("TypedMock should have recorded the default all forecasts request");

            // 5. Assert: Saved fixtures deserialize cleanly into the RestEase DTO types
            mockServer.FixtureStore.AllFixtures.Should().HaveCount(2);

            var londonFixture = mockServer.FixtureStore.AllFixtures.First(f => f.Method == "GetForecastByCityAsync");
            var dto = TypedFixtureStore.ValidateAndDeserialize(londonFixture, typeof(WeatherForecastDto)) as WeatherForecastDto;
            dto.Should().NotBeNull();
            dto!.City.Should().Be("London");
            dto.TemperatureC.Should().Be(12);
        }
        finally
        {
            if (Directory.Exists(tempFixturesDir))
            {
                Directory.Delete(tempFixturesDir, true);
            }
        }
    }

    [Fact]
    public async Task Scenario2_ReplayTraffic_WithoutRealService_ReturnsValidContractData()
    {
        // 1. Arrange: Setup recorded fixtures in a directory (Real service is OFFLINE)
        var tempFixturesDir = Path.Combine(Path.GetTempPath(), "typedmock_replay_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempFixturesDir);

        try
        {
            var store = new TypedFixtureStore();
            await store.SaveFixtureAsync(tempFixturesDir, new TypedFixture
            {
                Contract = "IWeatherApi",
                Method = "GetForecastByCityAsync",
                Scenario = "city_Paris",
                Request = new TypedFixtureRequest
                {
                    HttpMethod = "GET",
                    Path = "/weatherforecast/Paris",
                    PathParameters = new() { { "city", "Paris" } }
                },
                Response = new TypedFixtureResponse
                {
                    StatusCode = 200,
                    Data = System.Text.Json.Nodes.JsonNode.Parse("""
                    {
                        "date": "2026-11-04",
                        "city": "Paris",
                        "temperatureC": 25,
                        "summary": "Warm & Sunny"
                    }
                    """)
                }
            });

            // 2. Start Mock Server in REPLAY mode (no upstream target needed!)
            var mockOptions = new TypedMockOptions
            {
                Mode = MockMode.Replay,
                FixturesDirectory = tempFixturesDir
            };

            await using var mockServer = new TypedMockServer(mockOptions);
            mockServer.RegisterContract<IWeatherApi>();
            await mockServer.StartAsync();

            // 3. Act: Client calls the mock server
            var client = WeatherReportService.Create(mockServer.BaseUrl);
            var summary = await client.GetCitySummaryAsync("Paris");

            // 4. Assert: Data was served by the mock without any real external service running
            summary.Should().Be("Paris: 25°C (76°F), Warm & Sunny");
        }
        finally
        {
            if (Directory.Exists(tempFixturesDir))
            {
                Directory.Delete(tempFixturesDir, true);
            }
        }
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
        // Arrange: A fixture has corrupted data (e.g., TemperatureC is a string that cannot parse or invalid shape)
        var invalidFixture = new TypedFixture
        {
            Contract = "IWeatherApi",
            Method = "GetForecastByCityAsync",
            Scenario = "corrupted",
            Response = new TypedFixtureResponse
            {
                StatusCode = 200,
                Data = System.Text.Json.Nodes.JsonNode.Parse("""
                {
                    "city": "London",
                    "temperatureC": "THIS_IS_NOT_AN_INT"
                }
                """)
            }
        };

        // Act
        var act = () => TypedFixtureStore.ValidateAndDeserialize(invalidFixture, typeof(WeatherForecastDto));

        // Assert
        act.Should().Throw<ContractValidationException>()
            .WithMessage("*cannot be deserialized into contract type*");
    }

    [Fact]
    public async Task Scenario6_PostRequestWithBody_RecordsAndReplaysCorrectly()
    {
        // 1. Arrange: Start REAL service
        await using var realService = await SampleExternalService.Program.StartServerAsync(port: 0);
        var realServiceAddresses = realService.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>();
        var realServiceUrl = realServiceAddresses!.Addresses.First();

        var tempFixturesDir = Path.Combine(Path.GetTempPath(), "typedmock_post_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempFixturesDir);

        try
        {
            // Record mode
            var recordOptions = new TypedMockOptions
            {
                Mode = MockMode.Record,
                TargetUrl = realServiceUrl,
                FixturesDirectory = tempFixturesDir
            };
            await using var recordServer = new TypedMockServer(recordOptions);
            recordServer.RegisterContract<IWeatherApi>();
            await recordServer.StartAsync();

            var client = WeatherReportService.Create(recordServer.BaseUrl);
            var created = await client.AddForecastAsync("Berlin", 16, "Partly Cloudy");
            created.City.Should().Be("Berlin");
            created.TemperatureC.Should().Be(16);

            await recordServer.StopAsync();

            // 2. Replay mode (real service shut down)
            await realService.StopAsync();

            var replayOptions = new TypedMockOptions
            {
                Mode = MockMode.Replay,
                FixturesDirectory = tempFixturesDir
            };
            await using var replayServer = new TypedMockServer(replayOptions);
            replayServer.RegisterContract<IWeatherApi>();
            await replayServer.StartAsync();

            var replayClient = WeatherReportService.Create(replayServer.BaseUrl);
            var replayCreated = await replayClient.AddForecastAsync("Berlin", 16, "Partly Cloudy");
            replayCreated.City.Should().Be("Berlin");
            replayCreated.TemperatureC.Should().Be(16);
            replayCreated.Summary.Should().Be("Partly Cloudy");
        }
        finally
        {
            if (Directory.Exists(tempFixturesDir))
            {
                Directory.Delete(tempFixturesDir, true);
            }
        }
    }
}
