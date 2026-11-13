# Tasks

## 1. Unified Request Pipeline and Controller Registration in Core

- [x] 1.1 Add `RegisterStubController(Type)` and generic overload to `TypedMockServer`, enforcing that registered types inherit from `ControllerBase`
- [x] 1.2 Add scenario context middleware in `TypedMockServer` to store the resolved scenario name in `HttpContext.Items["TypedMock.Scenario"]`
- [x] 1.3 Refactor `TypedMockServer.StartAsync()` to wire `AddControllers()`, `UseRouting()`, `MapControllers()`, and fallback handling to `HandleRequestAsync`
- [x] 1.4 Write integration tests in `TypedMock.Tests` verifying request dispatch precedence (admin routes → MVC stub controllers → programmatic rules → 404)

## 2. Dedicated Stubs Project in Repository

- [x] 2.1 Create `src/ExternalService.Stubs/ExternalService.Stubs.csproj` targeting `net10.0` and referencing `ExternalService.Contracts` and `TypedMock.Core`
- [x] 2.2 Add `src/ExternalService.Stubs` to the solution (`MockingProxyProofOfConcept.slnx`) and verify `dotnet build` succeeds
- [x] 2.3 Implement runnable standalone host in `src/ExternalService.Stubs/Program.cs` using `StubServerHost.StartAsync` with port argument support
- [x] 2.4 Add Dockerfile for `src/ExternalService.Stubs` supporting containerized standalone execution

## 3. Golden Recording and Replay Integration

- [x] 3.1 Create a repeatable recording test or runner that records golden scenarios against `SampleExternalService` and saves `WeatherApiStubController.cs` into `src/ExternalService.Stubs/Controllers/`
- [x] 3.2 Verify that `src/ExternalService.Stubs` builds with `dotnet build` without compilation or contract drift errors
- [x] 3.3 Write consumer integration test proving `SampleClientApp` executes successfully against `WeatherApiStubController` hosted via `TypedMockServer` in Replay mode
- [x] 3.4 Write integration test proving partial-class hook scenario overrides work seamlessly with the repository-stored stub controller

## 4. Verification and Documentation

- [x] 4.1 Run full test suite (`dotnet test`) ensuring zero regressions across all existing Record, Replay, and multi-upstream scenarios
- [x] 4.2 Run `dotnet format --verify-no-changes` to enforce code style across all modified files
- [x] 4.3 Update `README.md` and `AGENTS.md` documenting the repository-first stub distribution workflow and `RegisterStubController` API
