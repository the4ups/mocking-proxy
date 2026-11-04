using SampleClientApp;

var targetUrl = args.Length > 0 ? args[0] : "http://localhost:5050";

Console.WriteLine($"[SampleClientApp] Connecting to service at {targetUrl}...");

var service = WeatherReportService.Create(targetUrl);

try
{
    Console.WriteLine("[SampleClientApp] Requesting city summary for London...");
    var london = await service.GetCitySummaryAsync("London");
    Console.WriteLine($"[SampleClientApp] Response: {london}");

    Console.WriteLine("[SampleClientApp] Requesting average temperature...");
    var avg = await service.GetAverageTemperatureAsync();
    Console.WriteLine($"[SampleClientApp] Average Temperature: {avg}°C");
}
catch (Exception ex)
{
    Console.ForegroundColor = ConsoleColor.Red;
    Console.WriteLine($"[SampleClientApp] Error: {ex.Message}");
    Console.ResetColor();
}
