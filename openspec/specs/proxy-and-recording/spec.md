# Proxy and Recording Specification

## Purpose
Reverse-proxies incoming HTTP calls to upstream dependency services, records typed interactions, validates upstream response payloads against contract DTO schemas in real time, and supports routing distinct contracts to multiple upstream destinations.

## Requirements

### Requirement: Reverse Proxy Forwarding
The system SHALL reverse-proxy HTTP requests to configured upstream targets in Record mode and return the upstream response to the caller.

#### Scenario: Forward request to upstream target
- **WHEN** a client sends an HTTP request matching a registered contract route in Record mode
- **THEN** the request is forwarded to the upstream target and the upstream response is returned to the client.

#### Scenario: Missing target URL in Record mode
- **WHEN** Record mode is initiated without specifying a target URL for a contract
- **THEN** the system returns an HTTP 500 error indicating the upstream target is required.

### Requirement: Runtime Contract Drift Validation
The system SHALL validate upstream response bodies against contract DTO return types at runtime and fail immediately if schema corruption or mismatch occurs.

#### Scenario: Valid upstream response
- **WHEN** upstream returns a response body that successfully deserializes into the contract DTO return type
- **THEN** the response is captured as a valid interaction and passed through to the client.

#### Scenario: Corrupted or mismatched upstream schema
- **WHEN** upstream returns a response body that fails schema validation or cannot deserialize into the contract DTO return type
- **THEN** the system throws a `ContractValidationException` to prevent stale or corrupted schemas from being recorded.

### Requirement: Multi-Upstream Routing
The system SHALL support dispatching distinct contract interfaces to separate, independent upstream service endpoints.

#### Scenario: Dispatch calls to separate upstream services
- **WHEN** multiple contracts are registered with different upstream URLs (e.g. `IWeatherApi` -> Service A, `IPaymentApi` -> Service B)
- **THEN** incoming requests are forwarded to their respective upstream destinations based on contract route matching.
