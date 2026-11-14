# Mock Replay Specification

## Purpose
Provides high-performance, deterministic HTTP replay of contract-compliant responses without requiring external upstream dependencies or recording files, supporting programmatic test setups and scenario branching.

## Requirements

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
