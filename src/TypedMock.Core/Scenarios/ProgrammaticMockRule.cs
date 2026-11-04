using System.Linq.Expressions;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using TypedMock.Core.Contracts;
using TypedMock.Core.Fixtures;
using TypedMock.Core.Models;

namespace TypedMock.Core.Scenarios;

public class ProgrammaticMockRule
{
    public required Type InterfaceType { get; init; }
    public required MethodInfo Method { get; init; }
    public Dictionary<string, string> ExpectedPathParameters { get; init; } = [];
    public string? ScenarioName { get; init; }
    public int StatusCode { get; set; } = 200;
    public object? ResponseData { get; set; }
    public string ContentType { get; set; } = "application/json";

    public bool Matches(string method, Dictionary<string, string> pathParams, string? requestedScenario)
    {
        if (!string.Equals(Method.Name, method, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(requestedScenario) && !string.IsNullOrWhiteSpace(ScenarioName))
        {
            if (!string.Equals(requestedScenario, ScenarioName, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        foreach (var (k, v) in ExpectedPathParameters)
        {
            if (!pathParams.TryGetValue(k, out var actualVal) || !string.Equals(v, actualVal, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return true;
    }

    public TypedFixture ToFixture()
    {
        JsonNode? node = null;
        if (ResponseData != null)
        {
            var json = JsonSerializer.Serialize(ResponseData);
            node = JsonNode.Parse(json);
        }

        return new TypedFixture
        {
            Contract = InterfaceType.Name,
            Method = Method.Name,
            Scenario = ScenarioName ?? "programmatic",
            Request = new TypedFixtureRequest
            {
                PathParameters = new Dictionary<string, string>(ExpectedPathParameters)
            },
            Response = new TypedFixtureResponse
            {
                StatusCode = StatusCode,
                ContentType = ContentType,
                Data = node
            }
        };
    }
}

public class ContractMockSetup<TInterface> where TInterface : class
{
    private readonly Action<ProgrammaticMockRule> _registerRule;
    private readonly List<ContractEndpoint> _endpoints;

    public ContractMockSetup(Action<ProgrammaticMockRule> registerRule)
    {
        _registerRule = registerRule;
        _endpoints = RestEaseContractScanner.Scan<TInterface>();
    }

    public MethodMockSetup<TResult> Setup<TResult>(Expression<Func<TInterface, Task<TResult>>> expression)
    {
        if (expression.Body is not MethodCallExpression methodCall)
        {
            throw new ArgumentException("Expression must be a method call", nameof(expression));
        }

        var methodInfo = methodCall.Method;
        var endpoint = _endpoints.FirstOrDefault(e => e.Method.Name == methodInfo.Name)
            ?? throw new InvalidOperationException($"Method {methodInfo.Name} not found in {typeof(TInterface).Name} contract endpoints.");

        var pathParams = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var methodParams = methodInfo.GetParameters();

        for (var i = 0; i < methodCall.Arguments.Count; i++)
        {
            var arg = methodCall.Arguments[i];
            var paramInfo = methodParams[i];
            var mapping = endpoint.Parameters.FirstOrDefault(p => p.ParameterName == paramInfo.Name);

            if (mapping?.Kind == ParameterKind.Path)
            {
                var val = EvaluateExpression(arg)?.ToString() ?? string.Empty;
                pathParams[mapping.TargetName] = val;
            }
        }

        var rule = new ProgrammaticMockRule
        {
            InterfaceType = typeof(TInterface),
            Method = methodInfo,
            ExpectedPathParameters = pathParams
        };

        _registerRule(rule);
        return new MethodMockSetup<TResult>(rule);
    }

    private static object? EvaluateExpression(Expression expression)
    {
        if (expression is ConstantExpression constant)
        {
            return constant.Value;
        }

        var lambda = Expression.Lambda(expression);
        return lambda.Compile().DynamicInvoke();
    }
}

public class MethodMockSetup<TResult>(ProgrammaticMockRule rule)
{
    public MethodMockSetup<TResult> WithScenario(string scenarioName)
    {
        // Set scenario name
        typeof(ProgrammaticMockRule).GetProperty(nameof(ProgrammaticMockRule.ScenarioName))
            ?.SetValue(rule, scenarioName);
        return this;
    }

    public void Returns(TResult result, int statusCode = 200)
    {
        rule.StatusCode = statusCode;
        rule.ResponseData = result;
    }

    public void ReturnsStatus(int statusCode, object? errorBody = null)
    {
        rule.StatusCode = statusCode;
        rule.ResponseData = errorBody;
    }
}
