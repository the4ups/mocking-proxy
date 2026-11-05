using TypedMock.Core.Models;

namespace TypedMock.Core.Generators;

public class RecordedInteraction
{
    public required ContractEndpoint Endpoint { get; init; }
    public Dictionary<string, string> PathParameters { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, string> QueryParameters { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    public string? RequestBody { get; init; }
    public int StatusCode { get; init; }
    public string? ResponseBody { get; init; }
    public object? DeserializedResponse { get; set; }
}
