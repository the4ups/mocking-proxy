# Tasks

## 1. Strip Replay Machinery from TypedMockServer

- [x] 1.1 Delete `src/TypedMock.Core/Scenarios/ProgrammaticMockRule.cs` (includes `ProgrammaticMockRule`, `ContractMockSetup<T>`, `MethodMockSetup<T>`). Verify the file no longer exists and `dotnet build src/TypedMock.Core` reports errors only for references to the deleted types (expected — they're removed in the next tasks).
- [x] 1.2 Remove `MockMode` enum from `TypedMockOptions.cs`. Delete `MockMode.Replay`, `MockMode.Hybrid`, `MockMode.Record` and the `Mode` property. Remove the `using TypedMock.Core.Scenarios` import from `TypedMockServer.cs`. Verify `TypedMockOptions.cs` no longer contains `MockMode` or `Mode`.
- [x] 1.3 Remove from `TypedMockServer.cs`: the `_programmaticRules` field, `_registeredStubControllers` field, `RegisterStubController()` / `RegisterStubController<T>()` / `RegisterStubControllers()` methods, `For<T>()` method, `ExplicitControllerFeatureProvider` inner class, all MVC controller registration logic in `StartAsync()` (the `if (_registeredStubControllers.Count > 0)` block), `MapControllers()`, and the Replay/Hybrid mode branches in `HandleRequestAsync()`. The `HandleRequestAsync` fallback should only handle admin status endpoint and Record-mode forwarding; unmatched requests return 404. Verify `dotnet build src/TypedMock.Core` succeeds with zero errors.
- [x] 1.4 Remove the scenario context middleware from `TypedMockServer.StartAsync()` (the `_app.Use(async (context, next) => { ... scenario ... })` block). Scenario resolution remains only in the admin status handler if needed. Verify `dotnet build src/TypedMock.Core` succeeds.
- [x] 1.5 Update `TypedMockServer.Mode` property — remove it or replace with a constant. Clean up `TypedMockOptions` to remove `Mode` default. Verify the `TypedMockServer` public API exposes only: `BaseUrl`, `RecordedInteractions`, `RegisteredEndpoints`, `RegisterContract<T>()`, `RegisterContracts()`, `RegisterContractsFromAssembly()`, `RegisterContractsFromDirectory()`, `GenerateStubSourceCode<T>()`, `SaveStubSourceCodeAsync<T>()`, `SaveAllStubsAsync()`, `StartAsync()`, `StopAsync()`, `DisposeAsync()`.

## 2. Make ExternalService.Stubs Self-Contained

- [x] 2.1 Remove `<ProjectReference Include="..\TypedMock.Core\TypedMock.Core.csproj" />` from `src/ExternalService.Stubs/ExternalService.Stubs.csproj`. Verify the `.csproj` references only `ExternalService.Contracts`.
- [x] 2.2 Rewrite `src/ExternalService.Stubs/Program.cs` to use `WebApplication.CreateBuilder()` + `app.MapControllers()` with inline scenario resolution middleware (read `X-Mock-Scenario` header / `__scenario` query / default to `"default"`, write to `HttpContext.Items["TypedMock.Scenario"]`). Remove the `using TypedMock.Core.Hosting;` import. Verify `dotnet build src/ExternalService.Stubs` succeeds and `dotnet run --project src/ExternalService.Stubs -- 5050` starts and serves traffic from `WeatherApiStubController`.
- [x] 2.3 Verify the generated `WeatherApiStubController` in `src/ExternalService.Stubs/Controllers/` compiles without `TypedMock.Core`. If it references `StubNotFoundException` or `StubHttpException` from `TypedMock.Core.Exceptions`, replace with a local exception type or `HttpContext.Response.StatusCode = 404` pattern. Verify `dotnet build src/ExternalService.Stubs` succeeds with no `TypedMock.Core` references.

## 3. Update TypedMock.Server CLI

- [x] 3.1 Remove `--mode` argument parsing from `src/TypedMock.Server/Program.cs`. Remove all references to `MockMode`. The server always operates in Record mode. Verify `dotnet build src/TypedMock.Server` succeeds and `dotnet run --project src/TypedMock.Server -- --port=5050 --target=http://localhost:5001` starts without errors.
- [x] 3.2 Implement `init` subcommand in `src/TypedMock.Server/Program.cs`. When the first positional argument is `init`, parse `--name=<ProjectName>`, `--contracts=<path-to-contracts-csproj>`, `--out-dir=<path>` arguments. Generate: (a) `<out-dir>/<ProjectName>/<ProjectName>.csproj` with `Sdk="Microsoft.NET.Sdk.Web"`, `net10.0` target, reference to contracts project; (b) `<out-dir>/<ProjectName>/Program.cs` with `WebApplication.CreateBuilder()`, scenario middleware, `app.MapControllers()`, CLI port parsing, graceful shutdown; (c) `<out-dir>/<ProjectName>/Dockerfile`; (d) `<out-dir>/<ProjectName>/Controllers/` empty directory. Verify: `dotnet run --project src/TypedMock.Server -- init --name=TestStubs --contracts=../ExternalService.Contracts/ExternalService.Contracts.csproj --out-dir=./tmp-scaffold` creates all expected files, and `dotnet build ./tmp-scaffold/TestStubs` compiles successfully.
- [x] 3.3 Add overwrite protection to `init`: if target directory already contains a `.csproj` file, print an error and exit with non-zero code. Verify by running `init` twice targeting the same directory — second invocation fails with an error message.

## 4. Rewrite Tests

- [x] 4.1 Rewrite `ProgrammaticRule_ReturnsConfiguredDto`, `ErrorSimulation_Returns500Status`, `ScenarioHeader_MatchesSpecificScenario`, and `UnmatchedRequest_Returns404NotFound` in `TypedMockTests.cs`. These tests currently use `TypedMockServer` in Replay mode with `.For<T>().Setup()`. Rewrite to use either `StubServerHost.StartAsync()` with a test-specific controller, or keep the inline `TestWeatherStubController` pattern but host via `StubServerHost` instead of `TypedMockServer`. Verify `dotnet test --filter "FullyQualifiedName~TypedMockTests"` passes.
- [x] 4.2 Rewrite `UnifiedPipeline_HostsStubController_AndServesTraffic`, `RequestDispatchPrecedence_ControllerTakesPrecedenceOverProgrammaticRule`, and `ScenarioContextMiddleware_PropagatesScenarioToContext` in `TypedMockTests.cs`. These use `RegisterStubController<T>()` on `TypedMockServer`. Rewrite to host via `StubServerHost.StartAsync()`. Remove dispatch-precedence test (no longer applicable — stubs host uses standard MVC routing). Verify `dotnet test --filter "FullyQualifiedName~TypedMockTests"` passes.
- [x] 4.3 Rewrite `Step2_ConsumerIntegration_SampleClientApp_RunsAgainstRepositoryStubController` and `Step3_PartialClassHookOverride_CustomizesStubBehavior` in `GoldenScenarioRecordingAndReplayTests.cs`. Replace `TypedMockServer` in Replay mode + `RegisterStubController<T>()` with `StubServerHost.StartAsync()`. Verify `dotnet test --filter "FullyQualifiedName~GoldenScenarioRecordingAndReplayTests"` passes.
- [x] 4.4 Remove `RegisterStubController_RejectsNonControllerType` test (the API no longer exists). Verify `dotnet test --filter "FullyQualifiedName~TypedMockTests"` passes.
- [x] 4.5 Remove or update `CoverageBumpTests.cs` if it references removed APIs. Verify `dotnet test` (all tests) passes with zero failures.

## 5. Final Verification

- [x] 5.1 Run `dotnet build` at the solution root — all projects compile with zero errors and zero warnings related to removed APIs. Verify `dotnet format --verify-no-changes` passes.
- [x] 5.2 Run `dotnet test` — all tests pass. Verify no test references `MockMode.Replay`, `MockMode.Hybrid`, `RegisterStubController`, `For<T>().Setup()`, or `ProgrammaticMockRule`.
- [x] 5.3 Update `AGENTS.md` to reflect the simplified architecture: remove Replay/Hybrid mode references, remove `.For<T>().Setup()` examples, add `typedmock init` command documentation, update the `ExternalService.Stubs` description to note it has no `TypedMock.Core` dependency. Verify the file accurately describes the current architecture.

## Workflow follow-up

- Archive the change after review and verification.
- Update the `README.md` (if present) to reflect the simplified workflow.
