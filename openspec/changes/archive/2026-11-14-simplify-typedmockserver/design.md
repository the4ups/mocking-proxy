# Design

## Context

See [proposal.md](proposal.md) for motivation. The current `TypedMockServer` (430 lines) bundles recording, replay, programmatic rules, stub controller hosting, scenario middleware, and admin endpoints into a single class. The `ExternalService.Stubs` project already demonstrates the target pattern — a standalone ASP.NET Core host — but still depends on `TypedMock.Core` via `StubServerHost`. The tests use `TypedMockServer` in Replay mode in ~6 test methods that need rewriting.

The existing codebase provides a clear seam: `StubServerHost` already does pure assembly-based controller hosting without any `TypedMockServer` involvement. The stubs project's `Program.cs` already works as a standalone CLI host. The main work is removing the replay machinery from `TypedMockServer` and making `ExternalService.Stubs` self-sufficient.

## Goals / Non-Goals

**Goals:**
- `TypedMockServer` becomes a recording proxy only — no replay, no programmatic rules, no stub controller hosting
- `ExternalService.Stubs` (and any scaffolded stubs project) runs without referencing `TypedMock.Core`
- `typedmock init` scaffolds a new stubs project from CLI
- All existing recording and code-generation tests continue to pass
- All existing replay tests pass after rewriting to use `StubServerHost` directly

**Non-Goals:**
- Rewriting `StubServerHost` — it stays as-is in `TypedMock.Core` as a convenience utility for in-memory test hosting
- Removing scenario resolution entirely — it moves to the stubs side, not disappears
- Breaking the generated controller format — `CSharpStubGenerator` output remains unchanged
- OpenAPI/Swagger contract support — deferred to a separate change

## Decisions

### Decision 1: Keep `StubServerHost` in `TypedMock.Core`

**Choice**: `StubServerHost` remains in `TypedMock.Core.Hosting` as a lightweight utility.

**Why**: Tests that record traffic and then immediately host the compiled result in-memory (e.g., `Host_CompiledGeneratedStub_ServesTrafficOverHttp`) need `StubServerHost` in the same package as the recording engine. Moving it to a separate package adds dependency management overhead for no architectural benefit. It has no dependency on `TypedMockServer`, `ProgrammaticMockRule`, or any replay machinery — it's a standalone 57-line helper.

**Alternative**: Extract to a `TypedMock.Hosting` package. Rejected — premature packaging for a proof-of-concept repo.

### Decision 2: Inline scenario middleware into scaffolded `Program.cs`

**Choice**: The `typedmock init` scaffolder emits scenario resolution middleware directly in the generated `Program.cs`, not via a shared NuGet package or base class.

**Why**: The entire middleware is 8 lines of code. Putting it in a shared package creates a runtime dependency on `TypedMock.Core` (the exact thing we're eliminating). A base class requires controllers to inherit from it. Inline middleware is self-contained, visible, and editable.

**Alternative A**: Emit a `ScenarioMiddleware.cs` file alongside `Program.cs`. Viable but unnecessary — the middleware is trivially small and is better visible in `Program.cs`.

**Alternative B**: Generate a `TypedMockControllerBase` base class. Rejected — forces inheritance, limits extensibility, and the generated controllers already use `ControllerBase` + `IContract` pattern.

### Decision 3: Remove `MockMode` enum entirely

**Choice**: Delete the `MockMode` enum and the `TypedMockOptions.Mode` property. `TypedMockServer` always records.

**Why**: With only `Record` remaining, the enum adds no information. The `--mode` CLI flag is removed. This is cleaner than keeping a single-value enum.

**Alternative**: Keep the enum with just `Record`. Rejected — YAGNI. If new modes emerge later, the enum can be reintroduced.

### Decision 4: Scaffolder implementation in `TypedMock.Server`

**Choice**: The `init` subcommand lives in `TypedMock.Server/Program.cs` with embedded template strings for `.csproj`, `Program.cs`, and `Dockerfile`.

**Why**: The templates are small (each under 40 lines). String interpolation with the user-provided project name and contracts path is straightforward. No template engine needed. The scaffolder is a CLI concern, not a library concern, so it belongs in the server project.

**Alternative**: Use T4 templates or Scriban. Rejected — heavy dependency for trivial templates.

### Decision 5: `ExternalService.Stubs` becomes self-contained

**Choice**: Remove the `ProjectReference` to `TypedMock.Core` from `ExternalService.Stubs.csproj`. Replace `StubServerHost.StartAsync()` in `Program.cs` with plain `WebApplication.CreateBuilder()` + `app.MapControllers()` + inline scenario middleware.

**Why**: This is the whole point of the change — stubs projects should be zero-dependency artifacts. The current `ExternalService.Stubs/Program.cs` is already 46 lines; replacing `StubServerHost` calls with equivalent inline code keeps it under 50 lines.

## Risks / Trade-offs

**[Tests using `TypedMockServer` in Replay mode break]** → Rewrite affected tests to use `StubServerHost.StartAsync()` instead. ~6 methods in `TypedMockTests.cs` and `GoldenScenarioRecordingAndReplayTests.cs`. Low risk — the pattern is well-established in existing tests.

**[Users relying on `.For<T>().Setup()` lose programmatic API]** → This is a proof-of-concept repo with no external consumers. The migration path (edit controller code / use scenario hooks) is documented in the spec deltas. Acceptable trade-off.

**[StubServerHost still lives in TypedMock.Core]** → Tests that reference `TypedMock.Core` for recording also get `StubServerHost` for free. The only cost is that `ExternalService.Stubs` (production stubs project) must NOT reference `TypedMock.Core` — but that's exactly the constraint we're enforcing.

**[Scenario middleware duplication]** → Each scaffolded stubs project gets its own copy of the 8-line middleware. This is intentional — it's trivially small and eliminates cross-project coupling. If it ever grows complex enough to share, extract then.
