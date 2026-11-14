# Proposal

## Why

`TypedMockServer` is over-engineered for the project's actual workflow. It bundles two distinct concerns — **recording traffic** (dev-time proxy) and **replaying mocked responses** (test-time hosting) — into a single class with a complex unified routing pipeline (admin routes → scenario middleware → MVC stub controllers → programmatic rules → hybrid upstream forwarding → 404). The replay side (`RegisterStubController<T>()`, `.For<T>().Setup()`, Replay/Hybrid modes, scenario middleware) competes with the generated stubs project (`ExternalService.Stubs`), which already hosts recorded controllers as plain ASP.NET Core without any TypedMock.Core dependency at runtime. The programmatic fluent API competes with "just edit the C# file" — and editable, debuggable, version-controlled C# wins. Simplifying now removes dead complexity before the codebase grows further.

## What Changes

- **BREAKING** — Remove `TypedMockServer`'s Replay and Hybrid modes. The class becomes a recording proxy only (Record mode).
- **BREAKING** — Remove `RegisterStubController<T>()` / `RegisterStubControllers()` from `TypedMockServer`. Tests host stubs via `StubServerHost` or the stubs project directly.
- **BREAKING** — Remove the programmatic `.For<T>().Setup()` fluent API (`ContractMockSetup<T>`, `MethodMockSetup<T>`, `ProgrammaticMockRule`). Tests that need one-off overrides use scenario hooks in generated controllers or edit the controller code directly.
- **BREAKING** — Remove `MockMode.Replay`, `MockMode.Hybrid` enum values. Only `MockMode.Record` remains (or the enum is removed entirely).
- **BREAKING** — Remove the unified dispatch pipeline (scenario middleware, admin routes, programmatic rule fallback chain) from `TypedMockServer`. Recording proxy uses a simpler pipeline: register contracts → forward to upstream → record → generate.
- Remove `ExternalService.Stubs` dependency on `TypedMock.Core`. The stubs project becomes a self-contained ASP.NET Core app referencing only the contracts.
- Add a stub project scaffolding capability to `TypedMock.Server` (`typedmock init`): generates a new `*.Stubs` project skeleton (`.csproj`, `Program.cs`, `Dockerfile`, empty `Controllers/` directory) ready to receive generated controller files.
- Scenario resolution moves from `TypedMockServer` middleware to a lightweight base class or helper generated into the stubs project, available to generated controllers directly.

## Capabilities

### New Capabilities
- `stub-project-scaffolding`: Scaffolding a new stubs host project (`*.Stubs`) from the CLI, producing a self-contained ASP.NET Core executable with `.csproj`, `Program.cs`, `Dockerfile`, and `Controllers/` directory that can host generated stub controllers without depending on `TypedMock.Core`.

### Modified Capabilities
- `mock-replay`: **BREAKING** — Remove Programmatic Setup API requirement, remove Stub Controller Replay Source requirement. Scenario resolution moves to the stubs project itself. The Offline Replay Execution and Scenario Resolution requirements are satisfied by the stubs project hosting, not by `TypedMockServer`.
- `stub-hosting`: **BREAKING** — Remove Stub Controller Registration requirement and Unified Request Dispatch Precedence requirement from `TypedMockServer`. `StubServerHost` remains as the lightweight hosting utility. Scenario Context Propagation moves to stubs-side convention.
- `standalone-server`: Remove Replay mode from CLI. The standalone server becomes a recording tool only. `typedmock init` command is added.

## Impact

- **TypedMock.Core**: `TypedMockServer.cs` shrinks significantly — removes ~150 lines (stub controller registration, programmatic rules, replay/hybrid dispatch). `ProgrammaticMockRule.cs` and `Scenarios/` directory are deleted. `StubServerHost` remains unchanged.
- **TypedMock.Server**: `Program.cs` removes Replay mode path, adds `init` subcommand for project scaffolding.
- **ExternalService.Stubs**: `.csproj` drops `TypedMock.Core` project reference. `Program.cs` uses plain `WebApplication.CreateBuilder()` instead of `StubServerHost`. Scenario resolution is self-contained.
- **Tests**: Tests using `.For<T>().Setup()` and `RegisterStubController<T>()` on `TypedMockServer` must be rewritten to use `StubServerHost` directly or host the stubs project in-memory. ~6 test methods affected across `TypedMockTests.cs` and `GoldenScenarioRecordingAndReplayTests.cs`.
- **NuGet consumers**: Breaking change for anyone using `TypedMock.Core` as a replay engine with programmatic rules.
