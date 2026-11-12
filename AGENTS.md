# AGENTS.md

Contract-driven mock proxy for .NET microservices using RestEase interface contracts and DTOs.

## Environment & Toolchain

- **Runtime**: .NET 10 SDK (`net10.0` target across all projects).
- **Solution**: `MockingProxyProofOfConcept.slnx` (modern XML-based solution format).
- **Style/Linting**: Standard `dotnet format --verify-no-changes`. Nullable reference types and implicit usings enabled everywhere.

## Essential Commands

```bash
# Build
dotnet build

# Run all tests
dotnet test

# Run a single test or test subset (Note: Name~ does NOT match in this runner; use FullyQualifiedName~)
dotnet test --filter "FullyQualifiedName~Scenario1"
dotnet test --filter "FullyQualifiedName~TypedMockIntegrationTests"
dotnet test --filter "FullyQualifiedName~CSharpStubIntegrationTests"

# Verify code formatting
dotnet format --verify-no-changes

# Run Standalone Mock Proxy (Replay mode)
dotnet run --project src/TypedMock.Server -- --mode=Replay --port=5050

# Run Standalone Mock Proxy (Record mode, upstream target required, saves C# stub controller on exit)
dotnet run --project src/TypedMock.Server -- --mode=Record --port=5050 --target=http://localhost:5001 --out-dir=./GeneratedStubs

# Run Standalone Mock Proxy (Multi-Upstream + Dynamic Contract Discovery from assemblies)
dotnet run --project src/TypedMock.Server -- --mode=Record --port=5050 --contracts-dir=./contracts --upstreams="IWeatherApi=http://localhost:5001,IPaymentApi=http://localhost:5002" --out-dir=./GeneratedStubs

# Run Sample External Service (Upstream dependency)
dotnet run --project src/SampleExternalService -- 5001

# Run Sample Client App
dotnet run --project src/SampleClientApp -- http://localhost:5050

# Pack NuGet packages (Core library and Global Tool)
dotnet pack src/TypedMock.Core/TypedMock.Core.csproj -c Release
dotnet pack src/TypedMock.Server/TypedMock.Server.csproj -c Release

# Docker build and compose
docker build -t typedmock:latest .
docker compose up -d
```

## Architecture & Boundaries

- `src/ExternalService.Contracts`: Shared interface library (`IWeatherApi`, DTOs). In real use, this represents the external team's published NuGet contract.
- `src/TypedMock.Core`: Core engine library.
  - Hosts an embedded Kestrel server (`TypedMockServer`).
  - Scans RestEase contract interfaces via `RestEaseContractScanner` or dynamically in runtime via `DynamicContractLoader` from external assemblies (`.dll`) or directories without compile-time references.
  - Multi-Upstream proxying: dispatches distinct contract routes to independent upstream services and batch-generates separate controllers via `SaveAllStubsAsync`.
  - Modes: `Replay` (programmatic rules / hosted stubs), `Record` (reverse proxy to upstream + in-memory typing), `Hybrid` (replay if matched, fallback to upstream proxy).
  - Enforces schema safety: validates that HTTP response bodies deserialize into contract DTO return types at runtime; throws `ContractValidationException` on schema drift.
  - C# Code Generation: `CSharpStubGenerator`, `CSharpValueEmitter`, and `StubServerHost` to record upstream traffic into version-controlled ASP.NET Core controllers with deterministic request pattern-matching (Path/Query/Body DTO properties), deduplication of repeated calls, scenario hooks, and compile-time drift detection.
  - Language-Agnostic Contracts: Designed to source contracts either from C# NuGet interfaces (RestEase/Refit) or OpenAPI/Swagger schemas.
- `src/TypedMock.Server`: Standalone CLI host for test stands / containers. Exposes diagnostic health status at `/__typedmock/status`.
- `src/SampleExternalService`: Upstream test server exposing minimal API endpoints and `Program.StartServerAsync(int port)`.
- `src/SampleClientApp`: Consumer client invoking `WeatherReportService` via `RestClient.For<IWeatherApi>(url)`.
- `tests/TypedMock.Tests`: xUnit integration test suite proving record, replay, programmatic setups, 500 error simulations, contract drift detection, POST payload round-tripping, and C# stub controller generation.

## Operational & Testing Quirks

- **Test Filtering**: Always use `--filter "FullyQualifiedName~<Pattern>"` when targeting individual tests. `--filter "Name~..."` yields 0 matched tests under this runner.
- **Port Allocation**: In integration tests, always pass `port: 0` (e.g., `SampleExternalService.Program.StartServerAsync(port: 0)` or `TypedMockOptions { Port = 0 }`) and resolve the actual address via `IServerAddressesFeature` to prevent port collisions.
- **Record Mode Prerequisite**: `TypedMockOptions.TargetUrl` (or `--target=...`) is mandatory in `Record` mode; omitting it returns HTTP 500.
- **Scenario Resolution**: Mock scenarios are resolved in order:
  1. `X-Mock-Scenario` request header (configurable via `TypedMockOptions.ScenarioHeader`).
  2. `?__scenario=<name>` query string parameter.
  3. Defaults to `"default"`.
- **C# Stubs Generation**: Saved to `--out-stubs=<path>` or via `mockServer.SaveStubSourceCodeAsync<TContract>(path)`. Contains deterministic pattern matching for route, query, and request body DTOs without intermediate JSON.
