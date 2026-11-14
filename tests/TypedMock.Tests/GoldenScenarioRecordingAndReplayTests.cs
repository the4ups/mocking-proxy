using ExternalService.Contracts;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using RestEase;
using TypedMock.Core;
using TypedMock.Core.Generators;
using TypedMock.Core.Hosting;

namespace TypedMock.Tests;

public class GoldenScenarioRecordingAndReplayTests
{
    private static string GetSolutionRoot()
    {
        var current = AppContext.BaseDirectory;
        var dir = new DirectoryInfo(current);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "MockingProxyProofOfConcept.slnx")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException("Could not find solution root directory.");
    }

    [Fact]
    public async Task Step1_RecordGoldenScenarios_SavesToExternalServiceStubsControllers()
    {
        var solutionRoot = GetSolutionRoot();
        var controllersDir = Path.Combine(solutionRoot, "src", "ExternalService.Stubs", "Controllers");

        // 1. Start real upstream service
        await using var upstreamService = await SampleExternalService.Program.StartServerAsync(port: 0);
        var upstreamAddresses = upstreamService.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>();
        var upstreamUrl = upstreamAddresses!.Addresses.First();

        // 2. Start TypedMock in Record mode pointing to ExternalService.Stubs/Controllers
        var mockOptions = new TypedMockOptions
        {
            TargetUrl = upstreamUrl,
            OutputDirectory = controllersDir,
            StubNamespace = "ExternalService.Stubs.Controllers"
        };

        await using var mockServer = new TypedMockServer(mockOptions);
        mockServer.RegisterContract<IWeatherApi>();
        await mockServer.StartAsync();

        // 3. Play golden requests through the proxy
        var client = RestClient.For<IWeatherApi>(mockServer.BaseUrl);

        var forecasts = await client.GetForecastsAsync();
        forecasts.Should().NotBeEmpty();

        var london = await client.GetForecastByCityAsync("London");
        london.City.Should().Be("London");

        var paris = await client.GetForecastByCityAsync("Paris");
        paris.City.Should().Be("Paris");

        var created = await client.CreateForecastAsync(new CreateForecastRequest
        {
            City = "Berlin",
            TemperatureC = 23,
            Summary = "Sunny and clear"
        });
        created.City.Should().Be("Berlin");

        // 4. Save stubs with custom namespace matching the project
        var savedFiles = await mockServer.SaveAllStubsAsync(controllersDir, new CSharpStubGeneratorOptions
        {
            Namespace = "ExternalService.Stubs.Controllers"
        });

        savedFiles.Should().NotBeEmpty();
        var stubFile = Path.Combine(controllersDir, "WeatherApiStubController.cs");
        File.Exists(stubFile).Should().BeTrue();

        var stubCode = await File.ReadAllTextAsync(stubFile);
        stubCode.Should().Contain("namespace ExternalService.Stubs.Controllers;");
        stubCode.Should().Contain("public partial class WeatherApiStubController : ControllerBase, IWeatherApi");
        stubCode.Should().Contain("City: \"Berlin\"");
    }

    [Fact]
    public async Task Step2_ConsumerIntegration_SampleClientApp_RunsAgainstRepositoryStubController()
    {
        // 1. Host the repository-stored WeatherApiStubController via StubServerHost
        await using var stubHost = await StubServerHost.StartAsync<ExternalService.Stubs.Controllers.WeatherApiStubController>(port: 0);
        var stubUrl = StubServerHost.GetServerUrl(stubHost);

        // 2. Run SampleClientApp WeatherReportService against the hosted stub server
        var weatherReportService = SampleClientApp.WeatherReportService.Create(stubUrl);

        var londonSummary = await weatherReportService.GetCitySummaryAsync("London");
        londonSummary.Should().Contain("London: 12°C");

        var parisSummary = await weatherReportService.GetCitySummaryAsync("Paris");
        parisSummary.Should().Contain("Paris: 18°C");

        var avgTemp = await weatherReportService.GetAverageTemperatureAsync();
        avgTemp.Should().BeGreaterThan(0);

        // Pattern match on request body DTO
        var berlinForecast = await weatherReportService.AddForecastAsync("Berlin", 23, "Sunny and clear");
        berlinForecast.City.Should().Be("Berlin");
        berlinForecast.TemperatureC.Should().Be(23);
    }

    [Fact]
    public async Task Step3_PartialClassHookOverride_CustomizesStubBehavior()
    {
        // Subclass repository-stored WeatherApiStubController to override an extensibility hook
        await using var stubHost = await StubServerHost.StartAsync<OverriddenWeatherStubController>(port: 0);
        var stubUrl = StubServerHost.GetServerUrl(stubHost);

        var client = RestClient.For<IWeatherApi>(stubUrl);

        // London uses overridden hook logic returning custom status/data
        var london = await client.GetForecastByCityAsync("London");
        london.Summary.Should().Be("Overridden Hook Summary");
        london.TemperatureC.Should().Be(99);

        // Paris still uses recorded baseline data from base WeatherApiStubController!
        var paris = await client.GetForecastByCityAsync("Paris");
        paris.City.Should().Be("Paris");
        paris.TemperatureC.Should().Be(18);
    }
}

[Microsoft.AspNetCore.Mvc.ApiController]
public class OverriddenWeatherStubController : ExternalService.Stubs.Controllers.WeatherApiStubController
{
    protected override Task<WeatherForecastDto?> OnGetForecastByCityAsync(string city)
    {
        if (string.Equals(city, "London", StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult<WeatherForecastDto?>(new WeatherForecastDto
            {
                City = "London",
                TemperatureC = 99,
                Summary = "Overridden Hook Summary"
            });
        }

        return base.OnGetForecastByCityAsync(city);
    }
}
