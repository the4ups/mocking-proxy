# stub-hosting Specification

## Purpose
Enables TypedMockServer to load pre-compiled C# stub controllers into its HTTP pipeline and serve recorded traffic alongside programmatic rules, with defined dispatch precedence and scenario context propagation.

## Requirements

### Requirement: Scenario Context Propagation to Stub Controllers
The stubs host project SHALL resolve the active scenario from request headers or query parameters and make it available to hosted stub controllers via the HTTP context, using built-in middleware in the scaffolded `Program.cs`.

#### Scenario: Scenario header accessible to stub controller
- **WHEN** a request includes the `X-Mock-Scenario` header and is dispatched to a stub controller in the stubs host
- **THEN** the stub controller can read the resolved scenario name from `HttpContext.Items`.

#### Scenario: Default scenario propagation
- **WHEN** a request with no scenario header or query parameter is dispatched to a stub controller in the stubs host
- **THEN** the resolved scenario name `"default"` is available in `HttpContext.Items`.
