# Mock Replay Specification

## Purpose
Provides high-performance, deterministic HTTP replay of contract-compliant responses without requiring external upstream dependencies or recording files, supporting programmatic test setups and scenario branching.

## Requirements

### Requirement: Offline Replay Execution
The system SHALL serve mocked responses matching registered contract endpoints in Replay mode without connecting to any external upstream services.

#### Scenario: Replay contract response
- **WHEN** a client invokes a registered contract endpoint in Replay mode
- **THEN** the system returns a contract-typed response with an HTTP 200 status code.

#### Scenario: Unmatched request
- **WHEN** an incoming request does not match any registered contract route or method
- **THEN** the system returns an HTTP 404 Not Found response.

### Requirement: Programmatic Setup API
The system SHALL provide a fluent C# API (`mockServer.For<TContract>().Setup(...)`) to configure custom return DTOs, delays, or error statuses.

#### Scenario: Programmatic DTO configuration
- **WHEN** a test configures a custom DTO return value for a specific contract method
- **THEN** subsequent calls matching that method return the configured DTO.

#### Scenario: Error simulation
- **WHEN** a test configures a contract method to simulate a server error (e.g., HTTP 500)
- **THEN** calls to that method return an HTTP 500 Internal Server Error status.

### Requirement: Scenario Resolution
The system SHALL resolve the active mock scenario using request headers, query parameters, or default fallback.

#### Scenario: Scenario header match
- **WHEN** an incoming request includes the `X-Mock-Scenario` header specifying a scenario name
- **THEN** the system selects and executes the matching mock scenario rule.

#### Scenario: Scenario query parameter match
- **WHEN** an incoming request includes the `?__scenario=<name>` query parameter
- **THEN** the system selects and executes the specified scenario rule.

#### Scenario: Default scenario fallback
- **WHEN** neither scenario header nor scenario query parameter is provided
- **THEN** the system falls back to the "default" scenario.
