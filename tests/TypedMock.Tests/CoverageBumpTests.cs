using System.Globalization;
using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using ExternalService.Contracts;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using RestEase;
using TypedMock.Core;
using TypedMock.Core.Contracts;
using TypedMock.Core.Exceptions;
using TypedMock.Core.Generators;
using TypedMock.Core.Hosting;
using TypedMock.Core.Models;
using TypedMock.Core.Scenarios;
using Xunit;

namespace TypedMock.Tests;

public class CoverageBumpTests
{
    // =========================================================================
    // 1. DYNAMIC CONTRACT LOADER TESTS
    // =========================================================================

    [Fact]
    public void DynamicContractLoader_LoadFromAssembly_ThrowsIfFileNotFound()
    {
        var act = () => DynamicContractLoader.LoadContractsFromAssembly("C:\\non_existent_assembly_xyz_123.dll");
        act.Should().Throw<FileNotFoundException>();
    }

    [Fact]
    public void DynamicContractLoader_LoadFromAssembly_SucceedsForValidAssembly()
    {
        var path = typeof(IWeatherApi).Assembly.Location;
        var contracts = DynamicContractLoader.LoadContractsFromAssembly(path);
        contracts.Should().Contain(typeof(IWeatherApi));
        contracts.Should().Contain(typeof(IPaymentApi));
    }

    [Fact]
    public void DynamicContractLoader_LoadFromDirectory_ThrowsIfDirectoryNotFound()
    {
        var act = () => DynamicContractLoader.LoadContractsFromDirectory("C:\\non_existent_directory_xyz_123");
        act.Should().Throw<DirectoryNotFoundException>();
    }

    [Fact]
    public void DynamicContractLoader_LoadFromDirectory_DiscoversContractsInBin()
    {
        var dir = Path.GetDirectoryName(typeof(IWeatherApi).Assembly.Location)!;
        var contracts = DynamicContractLoader.LoadContractsFromDirectory(dir);
        contracts.Should().Contain(typeof(IWeatherApi));
    }

    [Fact]
    public void DynamicContractLoader_IsRestEaseContract_HandlesVariousTypes()
    {
        DynamicContractLoader.IsRestEaseContract(typeof(string)).Should().BeFalse();
        DynamicContractLoader.IsRestEaseContract(typeof(IDisposable)).Should().BeFalse();
        DynamicContractLoader.IsRestEaseContract(typeof(IWeatherApi)).Should().BeTrue();
    }

    [Fact]
    public void DynamicContractLoader_DiscoverContractInterfaces_ExtractsRestEaseInterfaces()
    {
        var contracts = DynamicContractLoader.DiscoverContractInterfaces(typeof(IWeatherApi).Assembly);
        contracts.Should().Contain(typeof(IWeatherApi));
        contracts.Should().Contain(typeof(IPaymentApi));
    }

    // =========================================================================
    // 2. EXCEPTION AND MIDDLEWARE TESTS
    // =========================================================================

    [Fact]
    public void StubHttpException_PropertiesSetCorrectly()
    {
        var data = new { reason = "bad" };
        var ex = new StubHttpException(400, "Custom error", data);
        ex.StatusCode.Should().Be(400);
        ex.Message.Should().Be("Custom error");
        ex.ResponseData.Should().BeSameAs(data);
    }

    [Fact]
    public async Task StubExceptionMiddleware_HandlesStubHttpException_WithCustomPayload()
    {
        var middleware = new StubExceptionMiddleware(_ => throw new StubHttpException(418, "I am a teapot", new { custom = 123 }));
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();

        await middleware.InvokeAsync(context);

        context.Response.StatusCode.Should().Be(418);
        context.Response.ContentType.Should().Contain("application/json");

        context.Response.Body.Seek(0, SeekOrigin.Begin);
        var json = await new StreamReader(context.Response.Body).ReadToEndAsync();
        json.Should().Contain("\"custom\":123");
    }

    [Fact]
    public async Task StubExceptionMiddleware_HandlesStubHttpException_WithoutPayload()
    {
        var middleware = new StubExceptionMiddleware(_ => throw new StubHttpException(403, "Forbidden"));
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();

        await middleware.InvokeAsync(context);

        context.Response.StatusCode.Should().Be(403);
        context.Response.Body.Seek(0, SeekOrigin.Begin);
        var json = await new StreamReader(context.Response.Body).ReadToEndAsync();
        json.Should().Contain("Forbidden");
    }

    [Fact]
    public async Task StubExceptionMiddleware_HandlesStubNotFoundException()
    {
        var middleware = new StubExceptionMiddleware(_ => throw new StubNotFoundException("Missing stub"));
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();

        await middleware.InvokeAsync(context);

        context.Response.StatusCode.Should().Be(404);
        context.Response.Body.Seek(0, SeekOrigin.Begin);
        var json = await new StreamReader(context.Response.Body).ReadToEndAsync();
        json.Should().Contain("Missing stub");
    }

    [Fact]
    public async Task StubExceptionMiddleware_HandlesHttpRequestException_WithStatusCode()
    {
        var middleware = new StubExceptionMiddleware(_ => throw new HttpRequestException("Bad Gateway", null, System.Net.HttpStatusCode.BadGateway));
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();

        await middleware.InvokeAsync(context);

        context.Response.StatusCode.Should().Be(502);
        context.Response.Body.Seek(0, SeekOrigin.Begin);
        var json = await new StreamReader(context.Response.Body).ReadToEndAsync();
        json.Should().Contain("Bad Gateway");
    }

    [Fact]
    public async Task StubExceptionMiddleware_HandlesKeyNotFoundException()
    {
        var middleware = new StubExceptionMiddleware(_ => throw new KeyNotFoundException("Item not found"));
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();

        await middleware.InvokeAsync(context);

        context.Response.StatusCode.Should().Be(404);
        context.Response.Body.Seek(0, SeekOrigin.Begin);
        var json = await new StreamReader(context.Response.Body).ReadToEndAsync();
        json.Should().Contain("Item not found");
    }

    [Fact]
    public async Task StubExceptionMiddleware_PassesThroughWhenNoException()
    {
        var executed = false;
        var middleware = new StubExceptionMiddleware(ctx =>
        {
            executed = true;
            ctx.Response.StatusCode = 200;
            return Task.CompletedTask;
        });
        var context = new DefaultHttpContext();

        await middleware.InvokeAsync(context);

        executed.Should().BeTrue();
        context.Response.StatusCode.Should().Be(200);
    }

    // =========================================================================
    // 3. TYPED MOCK SERVER: STUBS & DIAGNOSTICS & SCENARIO QUERIES
    // =========================================================================

    [Fact]
    public async Task TypedMockServer_SaveStubSourceCodeAsync_SavesIndividualContractStub()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"WeatherStub_{Guid.NewGuid():N}.cs");
        try
        {
            await using var mockServer = new TypedMockServer();
            mockServer.RegisterContract<IWeatherApi>();

            var savedCode = await mockServer.SaveStubSourceCodeAsync<IWeatherApi>(tempFile);
            savedCode.Should().Contain("public partial class WeatherApiStubController");
            File.Exists(tempFile).Should().BeTrue();

            var readBack = await File.ReadAllTextAsync(tempFile);
            readBack.Should().Be(savedCode);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task TypedMockServer_SaveAllStubsAsync_WhenNoInteractionsRecorded_GeneratesForRegisteredContracts()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"Stubs_{Guid.NewGuid():N}");
        try
        {
            await using var mockServer = new TypedMockServer();
            mockServer.RegisterContract<IWeatherApi>();
            mockServer.RegisterContract<IPaymentApi>();

            var files = await mockServer.SaveAllStubsAsync(tempDir);
            files.Should().HaveCount(2);
            files.Any(f => f.EndsWith("WeatherApiStubController.cs")).Should().BeTrue();
            files.Any(f => f.EndsWith("PaymentApiStubController.cs")).Should().BeTrue();
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public async Task TypedMockServer_StatusEndpoint_AndRegistrationMethods()
    {
        await using var mockServer = new TypedMockServer(new TypedMockOptions { Port = 0 });
        mockServer.RegisterContractsFromAssembly(typeof(IWeatherApi).Assembly.Location);
        var dir = Path.GetDirectoryName(typeof(IWeatherApi).Assembly.Location)!;
        mockServer.RegisterContractsFromDirectory(dir);

        await mockServer.StartAsync();

        using var client = new HttpClient { BaseAddress = new Uri(mockServer.BaseUrl) };
        var response = await client.GetAsync("/__typedmock/status");
        response.IsSuccessStatusCode.Should().BeTrue();

        var content = await response.Content.ReadAsStringAsync();
        content.Should().Contain("\"status\":\"Running\"");
        content.Should().Contain("IWeatherApi");
    }

    [Fact]
    public async Task TypedMockServer_RecordMode_WithoutTargetUrl_Returns500WithDetail()
    {
        await using var mockServer = new TypedMockServer(new TypedMockOptions
        {
            Port = 0,
            Mode = MockMode.Record
            // TargetUrl is intentionally null
        });
        mockServer.RegisterContract<IWeatherApi>();
        await mockServer.StartAsync();

        using var client = new HttpClient { BaseAddress = new Uri(mockServer.BaseUrl) };
        var response = await client.GetAsync("/weatherforecast");
        response.StatusCode.Should().Be(System.Net.HttpStatusCode.InternalServerError);

        var content = await response.Content.ReadAsStringAsync();
        content.Should().Contain("No upstream TargetUrl configured");
    }

    [Fact]
    public async Task TypedMockServer_ScenarioResolution_ViaQueryString()
    {
        await using var mockServer = new TypedMockServer(new TypedMockOptions { Port = 0 });
        mockServer.For<IWeatherApi>()
            .Setup(x => x.GetForecastByCityAsync("Tokyo"))
            .WithScenario("rainy")
            .Returns(new WeatherForecastDto
            {
                City = "Tokyo",
                TemperatureC = 15,
                Summary = "Heavy rain"
            });

        await mockServer.StartAsync();

        var client = RestClient.For<IWeatherApi>(mockServer.BaseUrl);

        // Via query parameter __scenario=rainy
        using var rawClient = new HttpClient { BaseAddress = new Uri(mockServer.BaseUrl) };
        var resp = await rawClient.GetAsync("/weatherforecast/Tokyo?__scenario=rainy");
        resp.IsSuccessStatusCode.Should().BeTrue();

        var dto = await resp.Content.ReadFromJsonAsync<WeatherForecastDto>();
        dto.Should().NotBeNull();
        dto!.Summary.Should().Be("Heavy rain");

        // Request with missing scenario or unmatched scenario returns 404
        var unmatchedResp = await rawClient.GetAsync("/weatherforecast/Tokyo?__scenario=sunny");
        unmatchedResp.StatusCode.Should().Be(System.Net.HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task TypedMockServer_UnmatchedRequest_ReturnsDetailed404Json()
    {
        await using var mockServer = new TypedMockServer(new TypedMockOptions { Port = 0 });
        mockServer.RegisterContract<IWeatherApi>();
        await mockServer.StartAsync();

        using var client = new HttpClient { BaseAddress = new Uri(mockServer.BaseUrl) };
        var resp = await client.GetAsync("/unknown/endpoint/route");
        resp.StatusCode.Should().Be(System.Net.HttpStatusCode.NotFound);

        var content = await resp.Content.ReadAsStringAsync();
        content.Should().Contain("TypedMock: No matching mock rule found.");
    }

    [Fact]
    public async Task TypedMockServer_StopAsync_SavesStubsWhenOutputDirectorySet()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"AutoSave_{Guid.NewGuid():N}");
        await using var realService = await SampleExternalService.Program.StartServerAsync(port: 0);
        var realServiceAddresses = realService.Services.GetRequiredService<Microsoft.AspNetCore.Hosting.Server.IServer>().Features.Get<Microsoft.AspNetCore.Hosting.Server.Features.IServerAddressesFeature>();
        var realServiceUrl = realServiceAddresses!.Addresses.First();

        try
        {
            var mockServer = new TypedMockServer(new TypedMockOptions
            {
                Port = 0,
                Mode = MockMode.Record,
                TargetUrl = realServiceUrl,
                OutputDirectory = tempDir
            });
            mockServer.RegisterContract<IWeatherApi>();
            await mockServer.StartAsync();

            var client = RestClient.For<IWeatherApi>(mockServer.BaseUrl);
            await client.GetForecastsAsync();

            await mockServer.StopAsync();

            Directory.Exists(tempDir).Should().BeTrue();
            Directory.GetFiles(tempDir, "*.cs").Should().NotBeEmpty();
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
        }
    }

    // =========================================================================
    // 4. PROGRAMMATIC RULES & CONTRACT MOCK SETUP EDGE CASES
    // =========================================================================

    public class PropContainer
    {
        public string CityProp { get; set; } = "Rome";
        public string CityField = "Madrid";
    }

    [Fact]
    public async Task ContractMockSetup_SupportsPropertiesFieldsAndComplexExpressions()
    {
        await using var mockServer = new TypedMockServer(new TypedMockOptions { Port = 0 });

        var container = new PropContainer();
        // Property access in expression
        mockServer.For<IWeatherApi>()
            .Setup(x => x.GetForecastByCityAsync(container.CityProp))
            .Returns(new WeatherForecastDto { City = "Rome", TemperatureC = 25, Summary = "Warm" });

        // Field access in expression
        mockServer.For<IWeatherApi>()
            .Setup(x => x.GetForecastByCityAsync(container.CityField))
            .Returns(new WeatherForecastDto { City = "Madrid", TemperatureC = 30, Summary = "Hot" });

        // Complex dynamically evaluated expression
        mockServer.For<IWeatherApi>()
            .Setup(x => x.GetForecastByCityAsync("O" + "slo"))
            .Returns(new WeatherForecastDto { City = "Oslo", TemperatureC = 5, Summary = "Cold" });

        await mockServer.StartAsync();

        var client = RestClient.For<IWeatherApi>(mockServer.BaseUrl);

        var r1 = await client.GetForecastByCityAsync("Rome");
        r1.Summary.Should().Be("Warm");

        var r2 = await client.GetForecastByCityAsync("Madrid");
        r2.Summary.Should().Be("Hot");

        var r3 = await client.GetForecastByCityAsync("Oslo");
        r3.Summary.Should().Be("Cold");
    }

    [Fact]
    public void ContractMockSetup_ThrowsOnNonMethodCallExpression()
    {
        var setup = new ContractMockSetup<IWeatherApi>(_ => { });
        var act = () => setup.Setup<WeatherForecastDto>(_ => null!);
        act.Should().Throw<ArgumentException>().WithMessage("*Expression must be a method call*");
    }

    public interface IEmptyContract
    {
        Task<string> NoAttributeMethod();
    }

    [Fact]
    public void ContractMockSetup_ThrowsWhenMethodNotFoundInEndpoints()
    {
        var setup = new ContractMockSetup<IEmptyContract>(_ => { });
        var act = () => setup.Setup(x => x.NoAttributeMethod());
        act.Should().Throw<InvalidOperationException>().WithMessage("*not found in IEmptyContract contract endpoints*");
    }

    [Fact]
    public void ContractMockSetup_MapsQueryParametersProperly()
    {
        ProgrammaticMockRule? registeredRule = null;
        var setup = new ContractMockSetup<ITestContract>(r => registeredRule = r);
        setup.Setup(x => x.GetWithHeaderAndExplicitQuery(123, "myHeader", "myQuery", "myImplicit"));

        registeredRule.Should().NotBeNull();
        registeredRule!.ExpectedPathParameters.Should().ContainKey("id").WhoseValue.Should().Be("123");
        registeredRule.ExpectedQueryParameters.Should().ContainKey("q").WhoseValue.Should().Be("myQuery");
        registeredRule.ExpectedQueryParameters.Should().ContainKey("implicitQuery").WhoseValue.Should().Be("myImplicit");
    }

    [Fact]
    public void ProgrammaticMockRule_Matches_EvaluatesCorrectly()
    {
        var method = typeof(IWeatherApi).GetMethod(nameof(IWeatherApi.GetForecastByCityAsync))!;
        var rule = new ProgrammaticMockRule
        {
            InterfaceType = typeof(IWeatherApi),
            Method = method,
            ScenarioName = "custom",
            ExpectedPathParameters = new Dictionary<string, string> { { "city", "Athens" } },
            ExpectedQueryParameters = new Dictionary<string, string> { { "filter", "all" } }
        };

        // Method name mismatch
        rule.Matches("OtherMethod", new Dictionary<string, string> { { "city", "Athens" } }, new Dictionary<string, string> { { "filter", "all" } }, "custom")
            .Should().BeFalse();

        // Scenario mismatch
        rule.Matches(nameof(IWeatherApi.GetForecastByCityAsync), new Dictionary<string, string> { { "city", "Athens" } }, new Dictionary<string, string> { { "filter", "all" } }, "otherScenario")
            .Should().BeFalse();

        // Path param mismatch
        rule.Matches(nameof(IWeatherApi.GetForecastByCityAsync), new Dictionary<string, string> { { "city", "Rome" } }, new Dictionary<string, string> { { "filter", "all" } }, "custom")
            .Should().BeFalse();

        // Query param mismatch
        rule.Matches(nameof(IWeatherApi.GetForecastByCityAsync), new Dictionary<string, string> { { "city", "Athens" } }, new Dictionary<string, string> { { "filter", "none" } }, "custom")
            .Should().BeFalse();

        // Exact match
        rule.Matches(nameof(IWeatherApi.GetForecastByCityAsync), new Dictionary<string, string> { { "city", "Athens" } }, new Dictionary<string, string> { { "filter", "all" } }, "custom")
            .Should().BeTrue();

        // Overload with 3 parameters
        rule.Matches(nameof(IWeatherApi.GetForecastByCityAsync), new Dictionary<string, string> { { "city", "Athens" } }, "custom")
            .Should().BeTrue();
    }

    // =========================================================================
    // 5. REST EASE CONTRACT SCANNER TESTS
    // =========================================================================

    [BasePath("api/v1/test")]
    public interface ITestContract
    {
        [Get("")]
        Task<Response<string>> GetEmptySubPath();

        [Get("sub/{id}")]
        Task<string> GetWithHeaderAndExplicitQuery(
            [Path("id")] int id,
            [Header("X-Custom-Header")] string header,
            [Query("q")] string query,
            string implicitQuery);

        [Post("submit")]
        void VoidSubmit([Body] WeatherForecastDto dto);

        [Delete("remove")]
        Task SimpleTask();
    }

    [Fact]
    public void RestEaseContractScanner_ThrowsForNonInterface()
    {
        var act = () => RestEaseContractScanner.Scan(typeof(string));
        act.Should().Throw<ArgumentException>().WithMessage("*must be an interface*");
    }

    [Fact]
    public void RestEaseContractScanner_ScansComplexContractAccurately()
    {
        var endpoints = RestEaseContractScanner.Scan<ITestContract>();
        endpoints.Should().HaveCount(4);

        var emptySubPath = endpoints.First(e => e.Method.Name == nameof(ITestContract.GetEmptySubPath));
        emptySubPath.PathTemplate.Should().Be("api/v1/test");
        emptySubPath.ResponseType.Should().Be(typeof(string));

        var withParams = endpoints.First(e => e.Method.Name == nameof(ITestContract.GetWithHeaderAndExplicitQuery));
        withParams.PathTemplate.Should().Be("api/v1/test/sub/{id}");
        withParams.Parameters.Should().Contain(p => p.Kind == ParameterKind.Path && p.TargetName == "id");
        withParams.Parameters.Should().Contain(p => p.Kind == ParameterKind.Header && p.TargetName == "X-Custom-Header");
        withParams.Parameters.Should().Contain(p => p.Kind == ParameterKind.Query && p.TargetName == "q");
        withParams.Parameters.Should().Contain(p => p.Kind == ParameterKind.Query && p.TargetName == "implicitQuery");

        var voidMethod = endpoints.First(e => e.Method.Name == nameof(ITestContract.VoidSubmit));
        voidMethod.ResponseType.Should().BeNull();
        voidMethod.RequestBodyType.Should().Be(typeof(WeatherForecastDto));

        var taskMethod = endpoints.First(e => e.Method.Name == nameof(ITestContract.SimpleTask));
        taskMethod.ResponseType.Should().BeNull();
    }

    // =========================================================================
    // 6. C# VALUE EMITTER COMPREHENSIVE TESTS
    // =========================================================================

    public enum SampleStatus
    {
        Active,
        Disabled
    }

    public class ComplexTypeModel
    {
        public bool Flag { get; set; }
        public char Letter { get; set; }
        public short ShortVal { get; set; }
        public byte ByteVal { get; set; }
        public sbyte SByteVal { get; set; }
        public ushort UShortVal { get; set; }
        public uint UIntVal { get; set; }
        public long LongVal { get; set; }
        public ulong ULongVal { get; set; }
        public float FloatVal { get; set; }
        public double DoubleVal { get; set; }
        public decimal DecimalVal { get; set; }
        public DateOnly Date { get; set; }
        public TimeOnly Time { get; set; }
        public DateTime DateTimeVal { get; set; }
        public DateTimeOffset DateTimeOffsetVal { get; set; }
        public TimeSpan TimeSpanVal { get; set; }
        public Guid GuidVal { get; set; }
        public SampleStatus Status { get; set; }
        public int[]? ArrayVal { get; set; }
        public List<string> EmptyList { get; set; } = new();
    }

    [Fact]
    public void CSharpValueEmitter_EmitsAllPrimitiveAndStandardTypes()
    {
        var namespaces = new HashSet<string>();

        CSharpValueEmitter.Emit(null).Should().Be("null");
        CSharpValueEmitter.Emit(true).Should().Be("true");
        CSharpValueEmitter.Emit(false).Should().Be("false");
        CSharpValueEmitter.Emit("quote: \" and slash: \\ and \r\n\t").Should().Contain("\\\"");
        CSharpValueEmitter.Emit('X').Should().Be("'X'");

        CSharpValueEmitter.Emit((short)10).Should().Be("10");
        CSharpValueEmitter.Emit((byte)20).Should().Be("20");
        CSharpValueEmitter.Emit((sbyte)-5).Should().Be("-5");
        CSharpValueEmitter.Emit((ushort)30).Should().Be("30");
        CSharpValueEmitter.Emit(40u).Should().Be("40u");
        CSharpValueEmitter.Emit(50L).Should().Be("50L");
        CSharpValueEmitter.Emit(60uL).Should().Be("60uL");
        CSharpValueEmitter.Emit(1.5f).Should().Be("1.5f");
        CSharpValueEmitter.Emit(2.5d).Should().Be("2.5d");
        CSharpValueEmitter.Emit(3.5m).Should().Be("3.5m");

        var date = new DateOnly(2026, 11, 12);
        CSharpValueEmitter.Emit(date, collectedNamespaces: namespaces).Should().Contain("DateOnly.Parse(\"2026-11-12\"");

        var time = new TimeOnly(14, 30, 0);
        CSharpValueEmitter.Emit(time, collectedNamespaces: namespaces).Should().Contain("TimeOnly.Parse(\"14:30:00\"");

        var dt = new DateTime(2026, 11, 12, 10, 0, 0, DateTimeKind.Utc);
        CSharpValueEmitter.Emit(dt, collectedNamespaces: namespaces).Should().Contain("DateTime.Parse");

        var dto = new DateTimeOffset(2026, 11, 12, 10, 0, 0, TimeSpan.Zero);
        CSharpValueEmitter.Emit(dto, collectedNamespaces: namespaces).Should().Contain("DateTimeOffset.Parse");

        var ts = TimeSpan.FromMinutes(45);
        CSharpValueEmitter.Emit(ts, collectedNamespaces: namespaces).Should().Contain("TimeSpan.Parse");

        var guid = Guid.NewGuid();
        CSharpValueEmitter.Emit(guid, collectedNamespaces: namespaces).Should().Be($"Guid.Parse(\"{guid}\")");

        CSharpValueEmitter.Emit(SampleStatus.Active, collectedNamespaces: namespaces).Should().Be("SampleStatus.Active");

        var emptyListCode = CSharpValueEmitter.Emit(new List<int>(), collectedNamespaces: namespaces);
        emptyListCode.Should().Be("new List<int>()");

        var arrayCode = CSharpValueEmitter.Emit(new[] { 1, 2, 3 }, collectedNamespaces: namespaces);
        arrayCode.Should().Contain("new List<int>");
        arrayCode.Should().Contain("1,");
    }

    [Fact]
    public void CSharpValueEmitter_EmitsPatternsForVariousTypes()
    {
        var namespaces = new HashSet<string>();

        CSharpValueEmitter.EmitPattern(null).Should().Be("null");
        CSharpValueEmitter.EmitPattern(true).Should().Be("true");
        CSharpValueEmitter.EmitPattern(false).Should().Be("false");
        CSharpValueEmitter.EmitPattern("hello").Should().Be("\"hello\"");
        CSharpValueEmitter.EmitPattern('C').Should().Be("'C'");
        CSharpValueEmitter.EmitPattern(100).Should().Be("100");
        CSharpValueEmitter.EmitPattern((short)10).Should().Be("10");
        CSharpValueEmitter.EmitPattern((byte)20).Should().Be("20");
        CSharpValueEmitter.EmitPattern((sbyte)5).Should().Be("5");
        CSharpValueEmitter.EmitPattern((ushort)30).Should().Be("30");
        CSharpValueEmitter.EmitPattern(40u).Should().Be("40u");
        CSharpValueEmitter.EmitPattern(50L).Should().Be("50L");
        CSharpValueEmitter.EmitPattern(60uL).Should().Be("60uL");
        CSharpValueEmitter.EmitPattern(1.5f).Should().Be("1.5f");
        CSharpValueEmitter.EmitPattern(2.5d).Should().Be("2.5d");
        CSharpValueEmitter.EmitPattern(3.5m).Should().Be("3.5m");
        CSharpValueEmitter.EmitPattern(SampleStatus.Disabled, namespaces).Should().Be("SampleStatus.Disabled");

        var complex = new CreateForecastRequest
        {
            City = "Rome",
            TemperatureC = 25,
            Summary = "Warm"
        };
        var pat = CSharpValueEmitter.EmitPattern(complex, namespaces);
        pat.Should().Contain("City: \"Rome\"");
        pat.Should().Contain("TemperatureC: 25");

        var emptyObj = new object();
        CSharpValueEmitter.EmitPattern(emptyObj).Should().Be("{ }");
    }

    [Fact]
    public void CSharpValueEmitter_GetCleanTypeName_HandlesComplexTypes()
    {
        var ns = new HashSet<string>();

        CSharpValueEmitter.GetCleanTypeName(typeof(void)).Should().Be("void");
        CSharpValueEmitter.GetCleanTypeName(typeof(string)).Should().Be("string");
        CSharpValueEmitter.GetCleanTypeName(typeof(int)).Should().Be("int");
        CSharpValueEmitter.GetCleanTypeName(typeof(long)).Should().Be("long");
        CSharpValueEmitter.GetCleanTypeName(typeof(bool)).Should().Be("bool");
        CSharpValueEmitter.GetCleanTypeName(typeof(double)).Should().Be("double");
        CSharpValueEmitter.GetCleanTypeName(typeof(float)).Should().Be("float");
        CSharpValueEmitter.GetCleanTypeName(typeof(decimal)).Should().Be("decimal");
        CSharpValueEmitter.GetCleanTypeName(typeof(object)).Should().Be("object");

        CSharpValueEmitter.GetCleanTypeName(typeof(int?[])).Should().Be("int?[]");
        CSharpValueEmitter.GetCleanTypeName(typeof(Dictionary<string, List<int>>), ns).Should().Be("Dictionary<string, List<int>>");
        ns.Should().Contain("System.Collections.Generic");
    }

    // =========================================================================
    // 7. SAMPLE CLIENT APP & EXTERNAL SERVICE INTEGRATION TESTS
    // =========================================================================

    [Fact]
    public async Task WeatherReportService_AllMethodsExecuteProperly()
    {
        await using var mockServer = new TypedMockServer(new TypedMockOptions { Port = 0 });

        mockServer.For<IWeatherApi>()
            .Setup(x => x.GetForecastByCityAsync("London"))
            .Returns(new WeatherForecastDto
            {
                City = "London",
                TemperatureC = 15,
                Summary = "Pleasant"
            });

        mockServer.For<IWeatherApi>()
            .Setup(x => x.GetForecastsAsync())
            .Returns(new List<WeatherForecastDto>
            {
                new() { City = "A", TemperatureC = 10, Summary = "X" },
                new() { City = "B", TemperatureC = 20, Summary = "Y" }
            });

        mockServer.For<IWeatherApi>()
            .Setup(x => x.CreateForecastAsync(null!))
            .Returns(new WeatherForecastDto
            {
                City = "Berlin",
                TemperatureC = 21,
                Summary = "Sunny"
            });

        await mockServer.StartAsync();

        var service = SampleClientApp.WeatherReportService.Create(mockServer.BaseUrl);

        var summary = await service.GetCitySummaryAsync("London");
        summary.Should().Contain("London: 15°C (58°F), Pleasant");

        var avg = await service.GetAverageTemperatureAsync();
        avg.Should().Be(15);

        var created = await service.AddForecastAsync("Berlin", 21, "Sunny");
        created.City.Should().Be("Berlin");
    }

    [Fact]
    public async Task WeatherReportService_GetAverageTemperatureAsync_WhenEmpty_ReturnsZero()
    {
        await using var mockServer = new TypedMockServer(new TypedMockOptions { Port = 0 });
        mockServer.For<IWeatherApi>()
            .Setup(x => x.GetForecastsAsync())
            .Returns(new List<WeatherForecastDto>());

        await mockServer.StartAsync();

        var service = SampleClientApp.WeatherReportService.Create(mockServer.BaseUrl);
        var avg = await service.GetAverageTemperatureAsync();
        avg.Should().Be(0);
    }

    [Fact]
    public async Task TypedMockServer_SaveStubSourceCodeAsync_WithTypeOverload_CreatesNestedDirectories()
    {
        var nestedDir = Path.Combine(Path.GetTempPath(), $"Sub_{Guid.NewGuid():N}", "Deep");
        var tempFile = Path.Combine(nestedDir, "Stub.cs");
        try
        {
            await using var mockServer = new TypedMockServer();
            mockServer.RegisterContract<IWeatherApi>();

            var savedCode = await mockServer.SaveStubSourceCodeAsync(typeof(IWeatherApi), tempFile);
            savedCode.Should().Contain("public partial class WeatherApiStubController");
            File.Exists(tempFile).Should().BeTrue();
        }
        finally
        {
            if (Directory.Exists(Path.GetDirectoryName(nestedDir)))
            {
                Directory.Delete(Path.GetDirectoryName(nestedDir)!, true);
            }
        }
    }
}
