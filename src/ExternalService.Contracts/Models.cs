using System.Text.Json.Serialization;

namespace ExternalService.Contracts;

public record WeatherForecastDto
{
    [JsonPropertyName("date")]
    public DateOnly Date { get; init; }

    [JsonPropertyName("temperatureC")]
    public int TemperatureC { get; init; }

    [JsonPropertyName("summary")]
    public string? Summary { get; init; }

    [JsonPropertyName("city")]
    public string? City { get; init; }

    [JsonPropertyName("temperatureF")]
    public int TemperatureF => 32 + (int)(TemperatureC / 0.5556);
}

public record CreateForecastRequest
{
    [JsonPropertyName("city")]
    public required string City { get; init; }

    [JsonPropertyName("temperatureC")]
    public int TemperatureC { get; init; }

    [JsonPropertyName("summary")]
    public string? Summary { get; init; }
}
