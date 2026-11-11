using System.Net;
using System.Reflection;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using TypedMock.Core.Contracts;
using TypedMock.Core.Generators;
using TypedMock.Core.Matching;
using TypedMock.Core.Models;
using TypedMock.Core.Proxy;
using TypedMock.Core.Scenarios;

namespace TypedMock.Core;

public class TypedMockServer : IAsyncDisposable
{
    private static readonly SocketsHttpHandler SharedHandler = new()
    {
        PooledConnectionLifetime = TimeSpan.FromMinutes(15)
    };

    private readonly TypedMockOptions _options;
    private readonly List<ContractEndpoint> _registeredEndpoints = [];
    private readonly List<ProgrammaticMockRule> _programmaticRules = [];
    private readonly HttpClient _upstreamClient = new(SharedHandler, disposeHandler: false);
    private HttpProxyForwarder? _forwarder;
    private WebApplication? _app;
    private string? _baseUrl;

    public TypedMockServer(TypedMockOptions? options = null)
    {
        _options = options ?? new TypedMockOptions();
    }

    public string BaseUrl => _baseUrl ?? throw new InvalidOperationException("Server has not been started yet.");
    public MockMode Mode => _options.Mode;
    public IReadOnlyList<RecordedInteraction> RecordedInteractions => _forwarder?.RecordedInteractions ?? [];

    public string GenerateStubSourceCode<TContract>(CSharpStubGeneratorOptions? options = null) where TContract : class
    {
        return GenerateStubSourceCode(typeof(TContract), options);
    }

    public string GenerateStubSourceCode(Type contractType, CSharpStubGeneratorOptions? options = null)
    {
        return CSharpStubGenerator.Generate(contractType, RecordedInteractions, options);
    }

    public async Task<string> SaveStubSourceCodeAsync<TContract>(string outputPath, CSharpStubGeneratorOptions? options = null) where TContract : class
    {
        return await SaveStubSourceCodeAsync(typeof(TContract), outputPath, options);
    }

    public async Task<string> SaveStubSourceCodeAsync(Type contractType, string outputPath, CSharpStubGeneratorOptions? options = null)
    {
        var code = GenerateStubSourceCode(contractType, options);
        var dir = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }
        await File.WriteAllTextAsync(outputPath, code);
        return code;
    }

    public TypedMockServer RegisterContract<TInterface>() where TInterface : class
    {
        return RegisterContract(typeof(TInterface));
    }

    public TypedMockServer RegisterContract(Type interfaceType)
    {
        if (_registeredEndpoints.Any(e => e.InterfaceType == interfaceType))
        {
            return this;
        }

        var endpoints = RestEaseContractScanner.Scan(interfaceType);
        _registeredEndpoints.AddRange(endpoints);
        return this;
    }

    public ContractMockSetup<TInterface> For<TInterface>() where TInterface : class
    {
        RegisterContract<TInterface>();
        return new ContractMockSetup<TInterface>(rule => _programmaticRules.Add(rule));
    }

    public async Task StartAsync()
    {
        _forwarder = new HttpProxyForwarder(_upstreamClient);

        var builder = WebApplication.CreateEmptyBuilder(new WebApplicationOptions
        {
            EnvironmentName = "Development"
        });

        builder.Services.AddRouting();
        builder.WebHost.UseKestrel(k =>
        {
            k.Listen(IPAddress.Loopback, _options.Port);
        });

        _app = builder.Build();
        _app.Run(HandleRequestAsync);

        await _app.StartAsync();

        var serverAddresses = _app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>();
        _baseUrl = serverAddresses?.Addresses.FirstOrDefault() ?? $"http://localhost:{_options.Port}";
    }

    public async Task StopAsync()
    {
        if (!string.IsNullOrWhiteSpace(_options.OutputStubPath) && RecordedInteractions.Count > 0)
        {
            var primaryContract = _registeredEndpoints.Select(e => e.InterfaceType).Distinct().FirstOrDefault();
            if (primaryContract != null)
            {
                await SaveStubSourceCodeAsync(primaryContract, _options.OutputStubPath);
            }
        }

        if (_app != null)
        {
            await _app.StopAsync();
            await _app.DisposeAsync();
            _app = null;
        }
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
        _upstreamClient.Dispose();
        GC.SuppressFinalize(this);
    }

    private async Task HandleRequestAsync(HttpContext context)
    {
        var path = context.Request.Path.Value ?? "/";
        var method = context.Request.Method;

        // Admin Endpoints
        if (path.StartsWith("/__typedmock/status", StringComparison.OrdinalIgnoreCase))
        {
            await context.Response.WriteAsJsonAsync(new
            {
                status = "Running",
                mode = _options.Mode.ToString(),
                targetUrl = _options.TargetUrl,
                outputStubPath = _options.OutputStubPath,
                registeredEndpoints = _registeredEndpoints.Select(e => e.ToString()).ToList(),
                recordedInteractionsCount = RecordedInteractions.Count
            });
            return;
        }

        // Scenario resolution
        var scenarioName = context.Request.Headers[_options.ScenarioHeader].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(scenarioName))
        {
            scenarioName = context.Request.Query["__scenario"].FirstOrDefault();
        }
        scenarioName ??= "default";

        // Route Matching
        ContractEndpoint? matchedEndpoint = null;
        Dictionary<string, string> pathParams = [];

        foreach (var endpoint in _registeredEndpoints)
        {
            if (!string.Equals(endpoint.HttpMethod.Method, method, StringComparison.OrdinalIgnoreCase)) continue;

            var match = RoutePatternMatcher.Match(endpoint.PathTemplate, path);
            if (match.IsMatch)
            {
                matchedEndpoint = endpoint;
                pathParams = match.PathParameters;
                break;
            }
        }

        // Mode Execution: Record Mode
        if (_options.Mode == MockMode.Record)
        {
            if (string.IsNullOrEmpty(_options.TargetUrl))
            {
                context.Response.StatusCode = StatusCodes.Status500InternalServerError;
                await context.Response.WriteAsJsonAsync(new { error = "TargetUrl must be configured in Record mode." });
                return;
            }

            await _forwarder!.ForwardAndRecordAsync(
                context,
                new Uri(_options.TargetUrl),
                matchedEndpoint,
                pathParams,
                scenarioName);
            return;
        }

        // Replay or Hybrid Mode
        if (matchedEndpoint != null)
        {
            var queryParams = context.Request.Query.ToDictionary(q => q.Key, q => q.Value.ToString(), StringComparer.OrdinalIgnoreCase);

            // 1. Check Programmatic Rules
            var rule = _programmaticRules.FirstOrDefault(r =>
                r.InterfaceType == matchedEndpoint.InterfaceType &&
                r.Matches(matchedEndpoint.Method.Name, pathParams, queryParams, scenarioName));

            if (rule != null)
            {
                context.Response.StatusCode = rule.StatusCode;
                context.Response.ContentType = rule.ContentType;

                if (rule.ResponseData != null)
                {
                    await context.Response.WriteAsJsonAsync(rule.ResponseData);
                }
                return;
            }
        }

        // 2. Fallback for Hybrid mode
        if (_options.Mode == MockMode.Hybrid && !string.IsNullOrEmpty(_options.TargetUrl))
        {
            var forwarded = await _forwarder!.ForwardAndRecordAsync(
                context,
                new Uri(_options.TargetUrl),
                matchedEndpoint,
                pathParams,
                scenarioName);
            if (forwarded) return;
        }

        // Unmatched request
        context.Response.StatusCode = StatusCodes.Status404NotFound;
        await context.Response.WriteAsJsonAsync(new
        {
            error = "TypedMock: No matching mock rule found.",
            method = context.Request.Method,
            path,
            matchedContractEndpoint = matchedEndpoint?.ToString(),
            requestedScenario = scenarioName,
            hint = "Record this endpoint first into a C# stub controller or configure a scenario rule using .For<T>().Setup(...)."
        });
    }
}
