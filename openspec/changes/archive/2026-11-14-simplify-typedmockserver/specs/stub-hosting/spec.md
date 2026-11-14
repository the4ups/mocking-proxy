# Spec Delta

## REMOVED Requirements

### Requirement: Stub Controller Registration
**Reason**: `TypedMockServer` no longer hosts stub controllers. The `RegisterStubController<T>()` API and the `ExplicitControllerFeatureProvider` are removed. Stubs are hosted by their own standalone project or via `StubServerHost`, which loads controllers from an assembly without requiring explicit registration.
**Migration**: Replace `mockServer.RegisterStubController<TController>()` followed by `mockServer.StartAsync()` with `StubServerHost.StartAsync(typeof(TController), port)` for in-memory test hosting, or `dotnet run --project *.Stubs` for standalone hosting.

### Requirement: Unified Request Dispatch Precedence
**Reason**: The dispatch precedence chain (admin endpoints → MVC stub controllers → programmatic rules → hybrid forwarding → 404) existed to support `TypedMockServer` as both a recorder and a replay engine. With `TypedMockServer` reduced to a recording proxy only, this chain is unnecessary. Stub controllers are hosted in plain ASP.NET Core with standard MVC routing.
**Migration**: The stubs host project uses standard ASP.NET Core MVC routing. Admin diagnostic endpoints (`/__typedmock/*`) remain on `TypedMockServer` for the recording proxy only. Hybrid fallback is removed entirely.

## MODIFIED Requirements

### Requirement: Scenario Context Propagation to Stub Controllers
The stubs host project SHALL resolve the active scenario from request headers or query parameters and make it available to hosted stub controllers via the HTTP context, using built-in middleware in the scaffolded `Program.cs`.

#### Scenario: Scenario header accessible to stub controller
- **WHEN** a request includes the `X-Mock-Scenario` header and is dispatched to a stub controller in the stubs host
- **THEN** the stub controller can read the resolved scenario name from `HttpContext.Items`.

#### Scenario: Default scenario propagation
- **WHEN** a request with no scenario header or query parameter is dispatched to a stub controller in the stubs host
- **THEN** the resolved scenario name `"default"` is available in `HttpContext.Items`.
