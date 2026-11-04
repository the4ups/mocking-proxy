using System.Reflection;
using RestEase;
using TypedMock.Core.Models;

namespace TypedMock.Core.Contracts;

public class RestEaseContractScanner
{
    public static List<ContractEndpoint> Scan<TInterface>() where TInterface : class
    {
        return Scan(typeof(TInterface));
    }

    public static List<ContractEndpoint> Scan(Type interfaceType)
    {
        if (!interfaceType.IsInterface)
        {
            throw new ArgumentException($"Type '{interfaceType.FullName}' must be an interface.", nameof(interfaceType));
        }

        var endpoints = new List<ContractEndpoint>();
        var basePathAttr = interfaceType.GetCustomAttribute<BasePathAttribute>();
        var basePath = basePathAttr?.BasePath?.Trim('/') ?? string.Empty;

        foreach (var method in interfaceType.GetMethods(BindingFlags.Public | BindingFlags.Instance))
        {
            var reqAttr = method.GetCustomAttribute<RequestAttributeBase>();
            if (reqAttr is null) continue;

            var subPath = (reqAttr.Path ?? string.Empty).Trim('/');
            var fullPath = string.IsNullOrEmpty(basePath)
                ? subPath
                : string.IsNullOrEmpty(subPath) ? basePath : $"{basePath}/{subPath}";

            var responseType = ExtractResponseType(method.ReturnType);
            var parameters = new List<ParameterMapping>();
            Type? requestBodyType = null;

            foreach (var param in method.GetParameters())
            {
                var bodyAttr = param.GetCustomAttribute<BodyAttribute>();
                if (bodyAttr != null)
                {
                    requestBodyType = param.ParameterType;
                    parameters.Add(new ParameterMapping(param.Name!, string.Empty, ParameterKind.Body, param.ParameterType));
                    continue;
                }

                var pathAttr = param.GetCustomAttribute<PathAttribute>();
                if (pathAttr != null)
                {
                    var targetName = string.IsNullOrWhiteSpace(pathAttr.Name) ? param.Name! : pathAttr.Name;
                    parameters.Add(new ParameterMapping(param.Name!, targetName, ParameterKind.Path, param.ParameterType));
                    continue;
                }

                var queryAttr = param.GetCustomAttribute<QueryAttribute>();
                if (queryAttr != null)
                {
                    var targetName = string.IsNullOrWhiteSpace(queryAttr.Name) ? param.Name! : queryAttr.Name;
                    parameters.Add(new ParameterMapping(param.Name!, targetName, ParameterKind.Query, param.ParameterType));
                    continue;
                }

                var headerAttr = param.GetCustomAttribute<HeaderAttribute>();
                if (headerAttr != null)
                {
                    var targetName = string.IsNullOrWhiteSpace(headerAttr.Name) ? param.Name! : headerAttr.Name;
                    parameters.Add(new ParameterMapping(param.Name!, targetName, ParameterKind.Header, param.ParameterType));
                    continue;
                }

                // If path template contains {paramName}, treat as Path parameter by default
                if (fullPath.Contains($"{{{param.Name}}}", StringComparison.OrdinalIgnoreCase))
                {
                    parameters.Add(new ParameterMapping(param.Name!, param.Name!, ParameterKind.Path, param.ParameterType));
                }
                else
                {
                    // Default to query parameter if not decorated
                    parameters.Add(new ParameterMapping(param.Name!, param.Name!, ParameterKind.Query, param.ParameterType));
                }
            }

            endpoints.Add(new ContractEndpoint
            {
                InterfaceType = interfaceType,
                Method = method,
                HttpMethod = reqAttr.Method,
                PathTemplate = fullPath,
                ResponseType = responseType,
                RequestBodyType = requestBodyType,
                Parameters = parameters
            });
        }

        return endpoints;
    }

    private static Type? ExtractResponseType(Type returnType)
    {
        if (returnType == typeof(Task) || returnType == typeof(void))
        {
            return null;
        }

        if (returnType.IsGenericType && returnType.GetGenericTypeDefinition() == typeof(Task<>))
        {
            var taskResultType = returnType.GetGenericArguments()[0];

            if (taskResultType.IsGenericType && taskResultType.GetGenericTypeDefinition() == typeof(Response<>))
            {
                return taskResultType.GetGenericArguments()[0];
            }

            return taskResultType;
        }

        return returnType;
    }
}
