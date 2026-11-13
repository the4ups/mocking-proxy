# Spec Delta

## Purpose

Enables TypedMockServer to load pre-compiled C# stub controllers into its HTTP pipeline and serve recorded traffic alongside programmatic rules, with defined dispatch precedence and scenario context propagation.

## ADDED Requirements

### Requirement: Stub Controller Registration
The system SHALL accept pre-compiled ASP.NET Core controller types and register them into TypedMockServer's internal HTTP pipeline for serving in Replay or Hybrid mode.

#### Scenario: Register and serve a compiled stub controller
- **WHEN** a compiled stub controller type is registered via `RegisterStubController<T>()` and the server starts in Replay mode
- **THEN** HTTP requests matching the controller's route attributes are dispatched to the stub controller and return the controller's recorded responses.

#### Scenario: Register multiple stub controllers for distinct contracts
- **WHEN** stub controllers for `IWeatherApi` and `IPaymentApi` are both registered on the same server instance
- **THEN** requests to weather endpoints are handled by the weather stub controller and requests to payment endpoints are handled by the payment stub controller.

#### Scenario: Reject non-controller types
- **WHEN** a type that does not inherit from `ControllerBase` is passed to `RegisterStubController`
- **THEN** the system throws an `ArgumentException` before the server starts.

### Requirement: Unified Request Dispatch Precedence
The system SHALL dispatch incoming requests through a defined precedence chain: admin endpoints first, then MVC stub controllers, then programmatic rules, then hybrid forwarding, then 404.

#### Scenario: Stub controller takes precedence over programmatic rules
- **WHEN** a stub controller is registered for an endpoint AND a programmatic rule is configured for the same endpoint
- **THEN** the stub controller handles the request, and the programmatic rule is not evaluated.

#### Scenario: Programmatic rule handles unmatched stub routes
- **WHEN** a programmatic rule is configured for an endpoint that has no matching stub controller route
- **THEN** the programmatic rule handles the request.

#### Scenario: Hybrid fallback when no stub or rule matches
- **WHEN** the server runs in Hybrid mode and a request matches neither a stub controller route nor a programmatic rule
- **THEN** the request is forwarded to the configured upstream service.

### Requirement: Scenario Context Propagation to Stub Controllers
The system SHALL resolve the active scenario from request headers or query parameters and make it available to MVC-hosted stub controllers via the HTTP context.

#### Scenario: Scenario header accessible to stub controller
- **WHEN** a request includes the `X-Mock-Scenario` header and is dispatched to a stub controller
- **THEN** the stub controller can read the resolved scenario name from `HttpContext.Items`.

#### Scenario: Default scenario propagation
- **WHEN** a request with no scenario header or query parameter is dispatched to a stub controller
- **THEN** the resolved scenario name "default" is available in `HttpContext.Items`.
