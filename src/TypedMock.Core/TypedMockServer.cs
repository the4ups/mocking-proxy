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
    public IReadOnlyList<ContractEndpoint> RegisteredEndpoints => _registeredEndpoints.AsReadOnly();

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

    /// <summary>
    /// Generates and saves separate C# stub controllers for every recorded contract into the specified directory.
    /// E.g. outputDirectory/WeatherApiStubController.cs, outputDirectory/PaymentApiStubController.cs
    /// </summary>
    public async Task<IReadOnlyList<string>> SaveAllStubsAsync(string outputDirectory, CSharpStubGeneratorOptions? baseOptions = null)
    {
        if (!Directory.Exists(outputDirectory))
        {
            Directory.CreateDirectory(outputDirectory);
        }

        var savedFiles = new List<string>();
        var recordedContracts = RecordedInteractions
            .Select(i => i.Endpoint.InterfaceType)
            .Distinct()
            .ToList();

        // If no interactions were recorded yet, generate stubs for all registered contracts
        if (recordedContracts.Count == 0)
        {
            recordedContracts = _registeredEndpoints
                .Select(e => e.InterfaceType)
                .Distinct()
                .ToList();
        }

        foreach (var contractType in recordedContracts)
        {
            var options = baseOptions ?? new CSharpStubGeneratorOptions();
            var className = contractType.Name.StartsWith('I') && contractType.Name.Length > 1
                ? contractType.Name[1..] + "StubController"
                : contractType.Name + "StubController";

            var filePath = Path.Combine(outputDirectory, $"{className}.cs");
            var code = CSharpStubGenerator.Generate(contractType, RecordedInteractions, new CSharpStubGeneratorOptions
            {
                Namespace = options.Namespace,
                ClassName = className,
                IncludeControllerAttributes = options.IncludeControllerAttributes,
                IncludeExtensibilityHooks = options.IncludeExtensibilityHooks,
                IsAbstract = options.IsAbstract
            });

            await File.WriteAllTextAsync(filePath, code);
            savedFiles.Add(filePath);
        }

        return savedFiles;
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

    public TypedMockServer RegisterContracts(IEnumerable<Type> interfaceTypes)
    {
        foreach (var contract in interfaceTypes)
        {
            RegisterContract(contract);
        }
        return this;
    }

    public TypedMockServer RegisterContractsFromAssembly(string assemblyPath)
    {
        var contracts = DynamicContractLoader.LoadContractsFromAssembly(assemblyPath);
        return RegisterContracts(contracts);
    }

    public TypedMockServer RegisterContractsFromDirectory(string directoryPath)
    {
        var contracts = DynamicContractLoader.LoadContractsFromDirectory(directoryPath);
        return RegisterContracts(contracts);
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
        // Batch auto-save to directory if specified
        if (!string.IsNullOrWhiteSpace(_options.OutputDirectory) && RecordedInteractions.Count > 0)
        {
            await SaveAllStubsAsync(_options.OutputDirectory);
        }
        // Single file auto-save if specified
        else if (!string.IsNullOrWhiteSpace(_options.OutputStubPath) && RecordedInteractions.Count > 0)
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

        // Admin Diagnostic Endpoints
        if (path.StartsWith("/__typedmock/status", StringComparison.OrdinalIgnoreCase))
        {
            await context.Response.WriteAsJsonAsync(new
            {
                status = "Running",
                mode = _options.Mode.ToString(),
                defaultTargetUrl = _options.TargetUrl,
                upstreams = _options.Upstreams,
                outputDirectory = _options.OutputDirectory,
                outputStubPath = _options.OutputStubPath,
                registeredContracts = _registeredEndpoints.Select(e => e.InterfaceType.Name).Distinct().ToList(),
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
            var targetUrl = matchedEndpoint != null
                ? _options.ResolveTargetUrl(matchedEndpoint.InterfaceType.Name)
                : _options.TargetUrl;

            if (string.IsNullOrEmpty(targetUrl))
            {
                context.Response.StatusCode = StatusCodes.Status500InternalServerError;
                var errDetail = matchedEndpoint != null
                    ? $"No upstream TargetUrl configured for contract '{matchedEndpoint.InterfaceType.Name}'."
                    : "No default TargetUrl configured.";
                await context.Response.WriteAsJsonAsync(new { error = errDetail, hint = "Specify TargetUrl or map contract in Upstreams dictionary." });
                return;
            }

            await _forwarder!.ForwardAndRecordAsync(
                context,
                new Uri(targetUrl),
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
        if (_options.Mode == MockMode.Hybrid)
        {
            var targetUrl = matchedEndpoint != null
                ? _options.ResolveTargetUrl(matchedEndpoint.InterfaceType.Name)
                : _options.TargetUrl;

            if (!string.IsNullOrEmpty(targetUrl))
            {
                var forwarded = await _forwarder!.ForwardAndRecordAsync(
                    context,
                    new Uri(targetUrl),
                    matchedEndpoint,
                    pathParams,
                    scenarioName);
                if (forwarded) return;
            }
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
