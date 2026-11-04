using ExternalService.Contracts;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using RestEase;
using SampleClientApp;
using TypedMock.Core;
using TypedMock.Core.Fixtures;
using TypedMock.Core.Scenarios;

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
    public async Task Scenario9_ExplicitScenario_NotFound_Returns404RatherThanSilentFallback()
    {
        // Arrange
        var tempFixturesDir = Path.Combine(Path.GetTempPath(), "typedmock_scenario_404_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempFixturesDir);

        try
        {
            var store = new TypedFixtureStore();
            await store.SaveFixtureAsync(tempFixturesDir, new TypedFixture
            {
                Contract = "IWeatherApi",
                Method = "GetForecastByCityAsync",
                Scenario = "default",
                Request = new TypedFixtureRequest
                {
                    HttpMethod = "GET",
                    Path = "/weatherforecast/Rome",
                    PathParameters = new() { { "city", "Rome" } }
                },
                Response = new TypedFixtureResponse
                {
                    StatusCode = 200,
                    Data = System.Text.Json.Nodes.JsonNode.Parse("""
                    {
                        "date": "2026-11-04",
                        "city": "Rome",
                        "temperatureC": 21,
                        "summary": "Pleasant"
                    }
                    """)
                }
            });

            var mockOptions = new TypedMockOptions
            {
                Mode = MockMode.Replay,
                FixturesDirectory = tempFixturesDir
            };
            await using var mockServer = new TypedMockServer(mockOptions);
            mockServer.RegisterContract<IWeatherApi>();
            await mockServer.StartAsync();

            using var httpClient = new HttpClient { BaseAddress = new Uri(mockServer.BaseUrl) };
            httpClient.DefaultRequestHeaders.Add("X-Mock-Scenario", "NonExistentScenario");
            var client = RestClient.For<IWeatherApi>(httpClient);

            var act = async () => await client.GetForecastByCityAsync("Rome");
            var ex = await act.Should().ThrowAsync<ApiException>();
            ex.Which.StatusCode.Should().Be(System.Net.HttpStatusCode.NotFound);
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
    public void Scenario10_ToFixture_SerializesProgrammaticRuleToFixture()
    {
        var method = typeof(IWeatherApi).GetMethod(nameof(IWeatherApi.GetForecastByCityAsync))!;
        var rule = new ProgrammaticMockRule
        {
            InterfaceType = typeof(IWeatherApi),
            Method = method,
            ExpectedPathParameters = new() { { "city", "Kyoto" } },
            ScenarioName = "CustomScenario",
            StatusCode = 200,
            ResponseData = new WeatherForecastDto
            {
                City = "Kyoto",
                Date = DateOnly.FromDateTime(DateTime.Today),
                TemperatureC = 19,
                Summary = "Breezy"
            }
        };

        var fixture = rule.ToFixture();

        fixture.Contract.Should().Be(nameof(IWeatherApi));
        fixture.Method.Should().Be(nameof(IWeatherApi.GetForecastByCityAsync));
        fixture.Scenario.Should().Be("CustomScenario");
        fixture.Response.StatusCode.Should().Be(200);
        fixture.Response.Data.Should().NotBeNull();

        var deserialized = TypedFixtureStore.ValidateAndDeserialize(fixture, typeof(WeatherForecastDto)) as WeatherForecastDto;
        deserialized.Should().NotBeNull();
        deserialized!.City.Should().Be("Kyoto");
        deserialized.TemperatureC.Should().Be(19);
    }
}
