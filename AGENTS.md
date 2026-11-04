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

# Verify code formatting
dotnet format --verify-no-changes

# Run Standalone Mock Proxy (Replay mode)
dotnet run --project src/TypedMock.Server -- --mode=Replay --port=5050 --fixtures=./fixtures

# Run Standalone Mock Proxy (Record mode, upstream target required)
dotnet run --project src/TypedMock.Server -- --mode=Record --port=5050 --target=http://localhost:5001 --fixtures=./fixtures

# Run Sample External Service (Upstream dependency)
dotnet run --project src/SampleExternalService -- 5001

# Run Sample Client App
dotnet run --project src/SampleClientApp -- http://localhost:5050
```

## Architecture & Boundaries

- `src/ExternalService.Contracts`: Shared interface library (`IWeatherApi`, DTOs). In real use, this represents the external team's published NuGet contract.
- `src/TypedMock.Core`: Core engine library.
  - Hosts an embedded Kestrel server (`TypedMockServer`).
  - Scans RestEase contract interfaces via `RestEaseContractScanner` (`[Get]`, `[Post]`, `[Path]`, `[Body]`, `[Query]`, `[Header]`).
  - Modes: `Replay` (local fixtures / programmatic rules), `Record` (reverse proxy to upstream + save typed fixtures), `Hybrid` (replay if matched, fallback to upstream proxy).
  - Enforces schema safety: validates that fixture JSON response bodies deserialize into contract DTO return types at runtime; throws `ContractValidationException` on schema drift.
- `src/TypedMock.Server`: Standalone CLI host for test stands / containers. Exposes diagnostic health status at `/__typedmock/status`.
- `src/SampleExternalService`: Upstream test server exposing minimal API endpoints and `Program.StartServerAsync(int port)`.
- `src/SampleClientApp`: Consumer client invoking `WeatherReportService` via `RestClient.For<IWeatherApi>(url)`.
- `tests/TypedMock.Tests`: xUnit integration test suite proving record, replay, programmatic setups, 500 error simulations, contract drift detection, and POST payload round-tripping.

## Operational & Testing Quirks

- **Test Filtering**: Always use `--filter "FullyQualifiedName~<Pattern>"` when targeting individual tests. `--filter "Name~..."` yields 0 matched tests under this runner.
- **Port Allocation**: In integration tests, always pass `port: 0` (e.g., `SampleExternalService.Program.StartServerAsync(port: 0)` or `TypedMockOptions { Port = 0 }`) and resolve the actual address via `IServerAddressesFeature` to prevent port collisions.
- **Record Mode Prerequisite**: `TypedMockOptions.TargetUrl` (or `--target=...`) is mandatory in `Record` mode; omitting it returns HTTP 500.
- **Scenario Resolution**: Mock scenarios are resolved in order:
  1. `X-Mock-Scenario` request header (configurable via `TypedMockOptions.ScenarioHeader`).
  2. `?__scenario=<name>` query string parameter.
  3. Defaults to `"default"`.
- **Fixture Storage Hierarchy**: Serialized as `{FixturesDirectory}/{ContractName}/{MethodName}/{SanitizedScenario}.json`. When recording path-parameterized endpoints, scenario defaults to `<paramName>_<paramValue>` (e.g., `city_London.json`).
