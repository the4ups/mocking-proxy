# Design

## Context

Today, `TypedMockServer` runs a single terminal request delegate (`_app.Run(HandleRequestAsync)`), while `StubServerHost` runs standard ASP.NET Core MVC controllers via `builder.Services.AddControllers().AddApplicationPart(...)`. They are separate and unintegrated. For motivation and problem statement, see [proposal.md](proposal.md).

For the proof of concept, stubs are distributed via git inside the repository rather than external package feeds or runtime compilation tools. Stubs are generated into a dedicated project `src/ExternalService.Stubs`, committed directly to source control, and wired into `TypedMockServer` for Replay mode.

## Goals / Non-Goals

**Goals:**
- Provide a dedicated, version-controlled stubs project (`src/ExternalService.Stubs`) in the solution that compiles against `ExternalService.Contracts` and `TypedMock.Core`.
- Unify ASP.NET Core MVC controller hosting with `TypedMockServer`'s existing request processing pipeline in a single Kestrel server.
- Provide clean registration APIs: `mockServer.RegisterStubController(Type)` and `mockServer.RegisterStubController<T>()`.
- Establish deterministic request dispatch precedence: Admin routes → Scenario context middleware → MVC Stub Controllers → Programmatic Rules → Hybrid Fallback → 404.
- Provide a standalone runnable host entrypoint in `ExternalService.Stubs/Program.cs` for local developer stands.
- Demonstrate consumer integration in tests using both `StubServerHost` and `TypedMockServer.RegisterStubController`.

**Non-Goals:**
- Runtime Roslyn compilation of loose `.cs` files (standard `dotnet build` on the stubs project satisfies compilation and drift detection).
- Custom JSON metadata manifests (git history and `git diff` provide complete provenance tracking).
- Dynamic package publishing to external NuGet feeds.

## Decisions

### Decision 1: Repository-First Distribution Model
* **Choice**: Stubs are stored in a dedicated `src/ExternalService.Stubs` project within the repository, with `Controllers/WeatherApiStubController.cs` committed directly.
* **Rationale**: Eliminates NuGet feed dependencies and runtime compilation overhead. `dotnet build` guarantees that all recorded stubs satisfy contract interfaces at compile time, and `git diff` provides readable visual reviews of recorded payload changes.
* **Alternatives Considered**: Packaging into NuGet packages or dynamic runtime Roslyn compilation. Rejected for PoC as premature complexity when git already provides versioning, diffing, and drift detection.

### Decision 2: Unified Kestrel Pipeline in `TypedMockServer`
* **Choice**: Single Kestrel instance where `MapControllers()` handles MVC-routed controllers and unmatched requests fall through to `_app.Run(HandleFallbackRequestAsync)`.
* **Rationale**: Consumers only need a single base URL (`mockServer.BaseUrl`) across all contracts and setups. Running multiple ports requires external proxying and complicates consumer configuration.
* **Alternatives Considered**: Running `StubServerHost` on a separate port and using reverse-proxy forwarding inside `TypedMockServer`. Rejected because it introduces internal HTTP hops, latency, and socket exhaustion risks.

### Decision 3: Request Dispatch Precedence Order
* **Choice**: Strict sequential evaluation:
  1. Administrative/Diagnostic routes (`/__typedmock/*`)
  2. Exception & Scenario Context Middleware
  3. ASP.NET Core MVC Controllers (Registered stubs)
  4. Programmatic Mock Rules (`.For<T>().Setup(...)`)
  5. Hybrid Upstream Proxy Forwarding (if `Mode == MockMode.Hybrid`)
  6. Detailed 404 Not Found response
* **Rationale**: Stubs represent baseline recorded behavior; admin routes must always remain accessible regardless of routes; programmatic rules act as granular test-specific overrides when stubs are absent; hybrid acts as fallback.

### Decision 4: Standalone Host via `StubServerHost` in Stubs Project
* **Choice**: `ExternalService.Stubs/Program.cs` serves as a lightweight console runner using `StubServerHost.StartAsync<WeatherApiStubController>(port)`.
* **Rationale**: Developers can spin up an instant mock stand with `dotnet run --project src/ExternalService.Stubs -- 5050` without any proxy configuration.

## Risks / Trade-offs

- **[Controller Route Collisions]** If multiple registered stub controllers define identical routes on different contract interfaces, ASP.NET Core MVC throws an ambiguous route exception at runtime.
  → *Mitigation*: Contract interfaces map to distinct REST endpoints. In integration tests, only the relevant stub controllers for the service dependencies are registered.
- **[Git Noise on Re-recording]** Recording non-deterministic timestamps or random IDs could generate noisy git diffs.
  → *Mitigation*: Use fixed golden scenario requests in the recording runner to ensure deterministic C# code generation.
