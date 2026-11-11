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
    public string? TargetUrl { get; set; }
    public string? OutputStubPath { get; set; }
    public int Port { get; set; } = 0; // 0 = dynamic available port
    public string ScenarioHeader { get; set; } = "X-Mock-Scenario";
}
