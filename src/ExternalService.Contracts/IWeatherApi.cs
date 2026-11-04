using RestEase;

namespace ExternalService.Contracts;

public interface IWeatherApi
{
    [Get("weatherforecast")]
    Task<List<WeatherForecastDto>> GetForecastsAsync();

    [Get("weatherforecast/{city}")]
    Task<WeatherForecastDto> GetForecastByCityAsync([Path("city")] string city);

    [Post("weatherforecast")]
    Task<WeatherForecastDto> CreateForecastAsync([Body] CreateForecastRequest request);
}
