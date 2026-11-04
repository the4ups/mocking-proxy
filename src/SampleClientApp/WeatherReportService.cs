using ExternalService.Contracts;
using RestEase;

namespace SampleClientApp;

public class WeatherReportService(IWeatherApi weatherApi)
{
    public static WeatherReportService Create(string baseUrl)
    {
        var api = RestClient.For<IWeatherApi>(baseUrl);
        return new WeatherReportService(api);
    }

    public async Task<string> GetCitySummaryAsync(string city)
    {
        var forecast = await weatherApi.GetForecastByCityAsync(city);
        return $"{forecast.City}: {forecast.TemperatureC}°C ({forecast.TemperatureF}°F), {forecast.Summary}";
    }

    public async Task<int> GetAverageTemperatureAsync()
    {
        var all = await weatherApi.GetForecastsAsync();
        return all.Count == 0 ? 0 : (int)all.Average(x => x.TemperatureC);
    }

    public async Task<WeatherForecastDto> AddForecastAsync(string city, int tempC, string summary)
    {
        return await weatherApi.CreateForecastAsync(new CreateForecastRequest
        {
            City = city,
            TemperatureC = tempC,
            Summary = summary
        });
    }
}
