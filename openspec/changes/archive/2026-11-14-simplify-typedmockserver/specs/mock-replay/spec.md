# Spec Delta

## REMOVED Requirements

### Requirement: Programmatic Setup API
**Reason**: The fluent `.For<T>().Setup()` API in `TypedMockServer` competes with editing generated C# stub controllers directly. Editable, debuggable, version-controlled controller code is the preferred mechanism for configuring custom responses, delays, and error statuses. Scenario hooks in generated controllers provide equivalent per-test customization.
**Migration**: Replace `mockServer.For<T>().Setup(x => x.Method(...)).Returns(dto)` with either: (1) editing the generated stub controller's scenario hook (`OnMethodAsync()`) to return the desired DTO, or (2) using the `X-Mock-Scenario` header to select a pre-configured scenario branch in the generated controller.

### Requirement: Stub Controller Replay Source
**Reason**: `TypedMockServer` no longer operates in Replay mode. Stub controllers are hosted by their own standalone stubs project or via `StubServerHost`, not by `TypedMockServer`'s internal pipeline. The dispatch precedence chain (stub controllers → programmatic rules → hybrid forwarding) is removed.
**Migration**: Host compiled stub controllers via `StubServerHost.StartAsync(assembly, port)` or by running the stubs project directly (`dotnet run --project *.Stubs`). Do not register stub controllers on `TypedMockServer`.

## MODIFIED Requirements

### Requirement: Offline Replay Execution
The system SHALL serve mocked responses matching registered contract endpoints through hosted stub controllers without connecting to any external upstream services.

#### Scenario: Replay contract response
- **WHEN** a client invokes a registered contract endpoint on a hosted stubs server
- **THEN** the system returns a contract-typed response with an HTTP 200 status code.

#### Scenario: Unmatched request
- **WHEN** an incoming request does not match any hosted stub controller route or method
- **THEN** the system returns an HTTP 404 Not Found response.

### Requirement: Scenario Resolution
The system SHALL resolve the active mock scenario using request headers, query parameters, or default fallback, making the resolved scenario available to stub controllers via `HttpContext.Items`.

#### Scenario: Scenario header match
- **WHEN** an incoming request includes the `X-Mock-Scenario` header specifying a scenario name
- **THEN** the system selects the matching mock scenario and makes it available to the stub controller.

#### Scenario: Scenario query parameter match
- **WHEN** an incoming request includes the `?__scenario=<name>` query parameter
- **THEN** the system selects the specified scenario and makes it available to the stub controller.

#### Scenario: Default scenario fallback
- **WHEN** neither scenario header nor scenario query parameter is provided
- **THEN** the system falls back to the `"default"` scenario.
