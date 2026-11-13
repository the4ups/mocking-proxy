# Proposal

## Why

Generated C# stub controllers exist as isolated output files today — `TypedMockServer` records and emits them to disk, but cannot host them back in its unified Replay pipeline. For a pragmatic proof of concept, we need a repository-centric distribution and replay model: generated stubs are committed into a dedicated `ExternalService.Stubs` project in git, where `dotnet build` validates compile-time contract compatibility and `git diff` tracks data changes. `TypedMockServer` must be able to wire up these repository-stored stub controllers directly in Replay mode so consumer integration tests and local dev stands can execute against them seamlessly.

## What Changes

- **Unified request pipeline**: `TypedMockServer` gains the ability to host registered ASP.NET Core stub controllers alongside programmatic rules in a single Kestrel server instance with defined dispatch precedence.
- **Stub controller registration API**: New `mockServer.RegisterStubController(Type)` and `mockServer.RegisterStubController<T>()` methods on `TypedMockServer` for registering compiled controller classes.
- **Dedicated Stubs project in repository (`src/ExternalService.Stubs`)**: A version-controlled project storing generated stub controllers (`Controllers/WeatherApiStubController.cs`), referencing `ExternalService.Contracts` and `TypedMock.Core`, and providing a runnable standalone host entrypoint (`Program.cs`).
- **Repeatable recording test/script**: Golden recording scenario verifying that upstream traffic is captured directly into `src/ExternalService.Stubs/Controllers/` with deterministic pattern matching.
- **Scenario context propagation**: A lightweight middleware in `TypedMockServer` resolving `X-Mock-Scenario` and `?__scenario=` and exposing it via `HttpContext.Items["TypedMock.Scenario"]` to stub controllers.

## Capabilities

### New Capabilities
- `stub-hosting`: Covers registering and serving pre-compiled C# stub controllers within `TypedMockServer`'s unified HTTP pipeline, establishing request dispatch precedence between MVC controllers, programmatic rules, and hybrid forwarding.

### Modified Capabilities
- `mock-replay`: Extends replay mode to dispatch matching requests to registered stub controllers before falling back to programmatic mock rules.

## Impact

- **TypedMock.Core**: Refactors `TypedMockServer.StartAsync()` to compose `UseRouting()`, `MapControllers()`, and fallback handling. Adds `RegisterStubController(Type)` and scenario context middleware.
- **New Project**: Adds `src/ExternalService.Stubs` to `MockingProxyProofOfConcept.slnx` containing generated stub controllers and a standalone runnable host.
- **Tests**: Adds integration tests in `TypedMock.Tests` proving that repository-stored stub controllers serve traffic in Replay mode, compose with programmatic rules, and allow test-specific scenario overrides.
