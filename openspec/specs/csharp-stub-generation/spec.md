# C# Stub Generation Specification

## Purpose
Generates strongly-typed, compile-time verified ASP.NET Core controller source code from recorded interactions with deterministic request pattern-matching, deduplication of repeated calls, and scenario hooks.

## Requirements

### Requirement: Strongly-Typed Controller Generation
The system SHALL generate valid C# ASP.NET Core controller source code that implements or mimics the contract interface, ensuring compile-time drift detection against contract changes.

#### Scenario: Generate controller from recorded interactions
- **WHEN** recorded interactions for a contract are processed by the C# stub generator
- **THEN** a complete C# source file containing a controller class is produced with proper usings, route attributes, and action methods.

#### Scenario: Compile-time contract drift detection
- **WHEN** a contract method signature, route, or DTO is modified upstream and the generated stub is compiled
- **THEN** the C# compiler produces compile-time errors (e.g., `CS0535` or `CS1061`) preventing silent breakage.

### Requirement: Deterministic Pattern Matching
The system SHALL generate deterministic C# switch pattern matching expressions on incoming path variables, query parameters, and request body DTO properties.

#### Scenario: Pattern match on request body DTO properties
- **WHEN** multiple POST/PUT requests with distinct payload DTO properties are recorded for an endpoint
- **THEN** the generated controller action contains a switch expression matching specific DTO property values to their respective recorded responses.

#### Scenario: Deduplication of repeated requests
- **WHEN** the exact same request with identical parameters or body is recorded multiple times
- **THEN** the generator deduplicates them into a single deterministic pattern-matching branch rather than stateful FIFO queues.

### Requirement: Remote Stub Hosting
The system SHALL provide an embedded test server harness (`StubServerHost`) capable of hosting compiled stub controllers over HTTP.

#### Scenario: Serve traffic from compiled stub
- **WHEN** a generated stub controller is compiled and hosted via `StubServerHost`
- **THEN** client applications can execute HTTP requests against the hosted stub and receive the expected contract DTO responses.
