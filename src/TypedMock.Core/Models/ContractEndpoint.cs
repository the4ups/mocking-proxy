using System.Reflection;

namespace TypedMock.Core.Models;

public enum ParameterKind
{
    Path,
    Query,
    Body,
    Header
}

public record ParameterMapping(
    string ParameterName,
    string TargetName,
    ParameterKind Kind,
    Type ParameterType);

public class ContractEndpoint
{
    public required Type InterfaceType { get; init; }
    public required MethodInfo Method { get; init; }
    public required HttpMethod HttpMethod { get; init; }
    public required string PathTemplate { get; init; }
    public Type? ResponseType { get; init; }
    public Type? RequestBodyType { get; init; }
    public List<ParameterMapping> Parameters { get; init; } = [];

    public override string ToString() =>
        $"{HttpMethod.Method} {PathTemplate} -> {InterfaceType.Name}.{Method.Name}() : {ResponseType?.Name ?? "void"}";
}
