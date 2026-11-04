using System.Text.Json;
using System.Text.Json.Nodes;

namespace TypedMock.Core.Fixtures;

public class TypedFixtureRequest
{
    public string HttpMethod { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public Dictionary<string, string> PathParameters { get; set; } = [];
    public Dictionary<string, string> QueryParameters { get; set; } = [];
    public JsonNode? Body { get; set; }
}

public class TypedFixtureResponse
{
    public int StatusCode { get; set; } = 200;
    public string ContentType { get; set; } = "application/json";
    public JsonNode? Data { get; set; }
}

public class TypedFixture
{
    public string Contract { get; set; } = string.Empty;
    public string Method { get; set; } = string.Empty;
    public string Scenario { get; set; } = "default";
    public TypedFixtureRequest Request { get; set; } = new();
    public TypedFixtureResponse Response { get; set; } = new();
}
