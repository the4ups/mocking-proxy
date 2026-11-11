using System.Reflection;
using System.Text;
using TypedMock.Core.Contracts;
using TypedMock.Core.Models;

namespace TypedMock.Core.Generators;

public static class CSharpStubGenerator
{
    public static string Generate<TContract>(
        IEnumerable<RecordedInteraction> interactions,
        CSharpStubGeneratorOptions? options = null) where TContract : class
    {
        return Generate(typeof(TContract), interactions, options);
    }

    public static string Generate(
        Type contractType,
        IEnumerable<RecordedInteraction> interactions,
        CSharpStubGeneratorOptions? options = null)
    {
        if (!contractType.IsInterface)
        {
            throw new ArgumentException($"Type '{contractType.FullName}' must be an interface.", nameof(contractType));
        }

        options ??= new CSharpStubGeneratorOptions();

        var className = options.ClassName;
        if (string.IsNullOrWhiteSpace(className))
        {
            className = contractType.Name.StartsWith('I') && contractType.Name.Length > 1
                ? contractType.Name[1..] + "StubController"
                : contractType.Name + "StubController";
        }

        var namespaces = new SortedSet<string>(StringComparer.Ordinal)
        {
            "System",
            "System.Collections.Generic",
            "System.Globalization",
            "System.Net.Http",
            "System.Threading.Tasks",
            "Microsoft.AspNetCore.Mvc",
            "TypedMock.Core.Exceptions"
        };

        if (!string.IsNullOrEmpty(contractType.Namespace))
        {
            namespaces.Add(contractType.Namespace);
        }

        var endpoints = RestEaseContractScanner.Scan(contractType);
        var interactionsList = interactions
            .Where(i => i.Endpoint.InterfaceType == contractType)
            .ToList();

        var methodsSb = new StringBuilder();
        var hooksSb = new StringBuilder();

        foreach (var endpoint in endpoints)
        {
            var method = endpoint.Method;
            var cleanReturnType = CSharpValueEmitter.GetCleanTypeName(method.ReturnType, namespaces);

            // Determine ASP.NET Core HTTP attribute
            var httpMethodAttr = endpoint.HttpMethod.Method.ToUpperInvariant() switch
            {
                "GET" => "HttpGet",
                "POST" => "HttpPost",
                "PUT" => "HttpPut",
                "DELETE" => "HttpDelete",
                "PATCH" => "HttpPatch",
                "HEAD" => "HttpHead",
                _ => "HttpGet"
            };

            // Build action parameter declarations
            var paramDeclsWithAttrs = new List<string>();
            var paramNamesOnly = new List<string>();
            var paramDeclsNoAttrs = new List<string>();

            foreach (var p in method.GetParameters())
            {
                var cleanParamType = CSharpValueEmitter.GetCleanTypeName(p.ParameterType, namespaces);
                var mapping = endpoint.Parameters.FirstOrDefault(m => m.ParameterName == p.Name);

                var attr = string.Empty;
                if (options.IncludeControllerAttributes && mapping != null)
                {
                    attr = mapping.Kind switch
                    {
                        ParameterKind.Path => $"[FromRoute(Name = \"{mapping.TargetName}\")] ",
                        ParameterKind.Query => $"[FromQuery(Name = \"{mapping.TargetName}\")] ",
                        ParameterKind.Body => "[FromBody] ",
                        ParameterKind.Header => $"[FromHeader(Name = \"{mapping.TargetName}\")] ",
                        _ => string.Empty
                    };
                }

                paramDeclsWithAttrs.Add($"{attr}{cleanParamType} {p.Name}");
                paramNamesOnly.Add(p.Name!);
                paramDeclsNoAttrs.Add($"{cleanParamType} {p.Name}");
            }

            var hookMethodName = method.Name.EndsWith("Async", StringComparison.OrdinalIgnoreCase)
                ? $"On{method.Name}"
                : $"On{method.Name}Async";

            var isVoidReturn = method.ReturnType == typeof(Task) || method.ReturnType == typeof(void);
            var unwrappedResultType = endpoint.ResponseType;

            // Method header
            if (options.IncludeControllerAttributes)
            {
                var route = endpoint.PathTemplate;
                methodsSb.AppendLine($"    [{httpMethodAttr}(\"{route}\")]");
            }

            var asyncKeyword = method.ReturnType.IsAssignableTo(typeof(Task)) ? "async " : "";
            methodsSb.AppendLine($"    public virtual {asyncKeyword}{cleanReturnType} {method.Name}({string.Join(", ", paramDeclsWithAttrs)})");
            methodsSb.AppendLine("    {");

            // Extensibility Hook Check
            if (options.IncludeExtensibilityHooks)
            {
                var hookCallArgs = string.Join(", ", paramNamesOnly);
                if (isVoidReturn)
                {
                    methodsSb.AppendLine($"        if (await {hookMethodName}({hookCallArgs})) return;");
                }
                else
                {
                    methodsSb.AppendLine($"        var custom = await {hookMethodName}({hookCallArgs});");
                    methodsSb.AppendLine("        if (custom != null) return custom;");
                }
                methodsSb.AppendLine();
            }

            // Recorded interactions for this method
            var methodCalls = interactionsList
                .Where(i => i.Endpoint.Method.Name == method.Name)
                .ToList();

            if (methodCalls.Count == 0)
            {
                methodsSb.AppendLine($"        throw new NotImplementedException(\"No recorded traffic for '{endpoint.HttpMethod.Method} {endpoint.PathTemplate}'. Override {hookMethodName} to define scenario logic.\");");
            }
            else
            {
                var parameters = method.GetParameters();

                if (parameters.Length == 0)
                {
                    var latest = methodCalls.Last();
                    if (isVoidReturn)
                    {
                        methodsSb.AppendLine("        return;");
                    }
                    else if (latest.StatusCode is >= 200 and < 300)
                    {
                        var emittedCode = CSharpValueEmitter.Emit(latest.DeserializedResponse, indentLevel: 8, namespaces);
                        methodsSb.AppendLine($"        return {emittedCode};");
                    }
                    else
                    {
                        methodsSb.AppendLine($"        throw new StubHttpException({latest.StatusCode}, \"{CSharpValueEmitter.EscapeString(latest.ResponseBody ?? "Recorded error response")}\");");
                    }
                }
                else
                {
                    string switchExpr;
                    if (parameters.Length == 1)
                    {
                        var p = parameters[0];
                        switchExpr = p.ParameterType == typeof(string) ? $"({p.Name}?.ToLowerInvariant())" : p.Name!;
                    }
                    else
                    {
                        switchExpr = "(" + string.Join(", ", parameters.Select(p => p.ParameterType == typeof(string) ? $"{p.Name}?.ToLowerInvariant()" : p.Name!)) + ")";
                    }

                    methodsSb.AppendLine($"        return {switchExpr} switch");
                    methodsSb.AppendLine("        {");

                    var distinctCalls = methodCalls
                        .GroupBy(c => parameters.Length == 1
                            ? BuildParameterPattern(c, parameters[0], endpoint, namespaces)
                            : $"({string.Join(", ", parameters.Select(p => BuildParameterPattern(c, p, endpoint, namespaces)))})")
                        .ToList();

                    foreach (var group in distinctCalls)
                    {
                        var pattern = group.Key;
                        var latest = group.Last();

                        methodsSb.AppendLine($"            {pattern} =>");
                        AppendInteractionBranch(methodsSb, latest, 16, namespaces);
                    }

                    var notFoundMessage = parameters.Length == 1
                        ? $"No recorded stub for {parameters[0].Name} '{{{parameters[0].Name}}}'."
                        : "No recorded stub matching the provided request parameters.";
                    methodsSb.AppendLine($"            _ => throw new StubNotFoundException($\"{notFoundMessage}\")");
                    methodsSb.AppendLine("        };");
                }
            }

            methodsSb.AppendLine("    }");
            methodsSb.AppendLine();

            // Hook definition
            if (options.IncludeExtensibilityHooks)
            {
                if (isVoidReturn)
                {
                    hooksSb.AppendLine($"    protected virtual Task<bool> {hookMethodName}({string.Join(", ", paramDeclsNoAttrs)}) => Task.FromResult(false);");
                }
                else
                {
                    var cleanHookReturnType = unwrappedResultType != null
                        ? CSharpValueEmitter.GetCleanTypeName(unwrappedResultType, namespaces)
                        : "object";
                    hooksSb.AppendLine($"    protected virtual Task<{cleanHookReturnType}?> {hookMethodName}({string.Join(", ", paramDeclsNoAttrs)}) => Task.FromResult<{cleanHookReturnType}?>(null);");
                }
            }
        }

        // Assemble class
        var classSb = new StringBuilder();
        classSb.AppendLine("// <auto-generated />");
        classSb.AppendLine("// Generated by TypedMock CSharpStubGenerator");
        classSb.AppendLine($"// Source Contract: {contractType.FullName}");
        classSb.AppendLine("#nullable enable");
        classSb.AppendLine();

        foreach (var ns in namespaces)
        {
            classSb.AppendLine($"using {ns};");
        }

        classSb.AppendLine();
        classSb.AppendLine($"namespace {options.Namespace};");
        classSb.AppendLine();

        if (options.IncludeControllerAttributes && !options.IsAbstract)
        {
            classSb.AppendLine("[ApiController]");
        }

        var abstractKeyword = options.IsAbstract ? "abstract " : "";
        classSb.AppendLine($"public {abstractKeyword}partial class {className} : ControllerBase, {contractType.Name}");
        classSb.AppendLine("{");
        classSb.Append(methodsSb);

        if (options.IncludeExtensibilityHooks && hooksSb.Length > 0)
        {
            classSb.AppendLine("    // -------------------------------------------------------------------------");
            classSb.AppendLine("    // Extensibility Hooks: Override in a subclass or partial class to customize scenarios");
            classSb.AppendLine("    // -------------------------------------------------------------------------");
            classSb.Append(hooksSb);
        }

        classSb.AppendLine("}");

        return classSb.ToString();
    }

    private static string BuildParameterPattern(
        RecordedInteraction interaction,
        ParameterInfo param,
        ContractEndpoint endpoint,
        ISet<string> namespaces)
    {
        var mapping = endpoint.Parameters.FirstOrDefault(m => m.ParameterName == param.Name);

        if (mapping?.Kind == ParameterKind.Body)
        {
            return interaction.DeserializedRequestBody != null
                ? CSharpValueEmitter.EmitPattern(interaction.DeserializedRequestBody, namespaces)
                : (interaction.RequestBody ?? "_");
        }

        if (mapping?.Kind == ParameterKind.Query)
        {
            return interaction.QueryParameters.TryGetValue(mapping.TargetName, out var qVal)
                ? $"\"{CSharpValueEmitter.EscapeString(qVal.ToLowerInvariant())}\""
                : "_";
        }

        var targetName = mapping?.TargetName ?? param.Name!;
        if (interaction.PathParameters.TryGetValue(targetName, out var pathVal))
        {
            return $"\"{CSharpValueEmitter.EscapeString(pathVal.ToLowerInvariant())}\"";
        }

        return "_";
    }

    private static void AppendInteractionBranch(
        StringBuilder sb,
        RecordedInteraction latest,
        int indentLevel,
        ISet<string> namespaces)
    {
        var indent = new string(' ', indentLevel);
        if (latest.StatusCode is >= 200 and < 300)
        {
            var emittedCode = CSharpValueEmitter.Emit(latest.DeserializedResponse, indentLevel, namespaces);
            sb.AppendLine($"{indent}{emittedCode},");
        }
        else
        {
            sb.AppendLine($"{indent}throw new StubHttpException({latest.StatusCode}, \"{CSharpValueEmitter.EscapeString(latest.ResponseBody ?? "Recorded error response")}\"),");
        }
    }
}
