namespace TypedMock.Core;

public enum MockMode
{
    Replay,
    Record,
    Hybrid
}

public class TypedMockOptions
{
    public MockMode Mode { get; set; } = MockMode.Replay;

    /// <summary>
    /// Default upstream target URL for single-service proxying or fallback.
    /// </summary>
    public string? TargetUrl { get; set; }

    /// <summary>
    /// Multi-upstream routing table mapping contract interface names (or type names) to upstream target URLs.
    /// E.g. ["IWeatherApi"] = "http://weather-service:5001", ["IPaymentApi"] = "http://payment-service:5002"
    /// </summary>
    public Dictionary<string, string> Upstreams { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Target directory where generated C# stub controllers will be saved per contract.
    /// E.g. "./GeneratedStubs" -> WeatherApiStubController.cs, PaymentApiStubController.cs
    /// </summary>
    public string? OutputDirectory { get; set; }

    /// <summary>
    /// Specific single file output path for generating a single stub controller.
    /// </summary>
    public string? OutputStubPath { get; set; }

    public int Port { get; set; } = 0; // 0 = dynamic available port
    public string ScenarioHeader { get; set; } = "X-Mock-Scenario";

    /// <summary>
    /// Resolves the upstream URL for a specific contract type name.
    /// Falls back to <see cref="TargetUrl"/> if not explicitly configured in <see cref="Upstreams"/>.
    /// </summary>
    public string? ResolveTargetUrl(string? contractName)
    {
        if (!string.IsNullOrEmpty(contractName) && Upstreams.TryGetValue(contractName, out var url))
        {
            return url;
        }

        return TargetUrl;
    }
}
