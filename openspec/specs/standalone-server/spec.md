# Standalone Server Specification

## Purpose
Provides a standalone CLI executable and container host (`TypedMock.Server`) for dev environments, test stands, and CI/CD pipelines, supporting CLI arguments, health checks, multi-upstream configuration, and automated stub export on exit.

## Requirements

### Requirement: CLI Options and Configuration
The system SHALL support configuring execution mode, listening port, upstream targets, contract discovery paths, and output stub directories via command-line arguments.

#### Scenario: Launch in Record mode with target and stub output
- **WHEN** the server is launched with `--mode=Record --port=5050 --target=<url> --out-dir=<path>`
- **THEN** the server starts listening on port 5050 in Record mode and saves generated stub controllers to the specified output directory upon termination.

#### Scenario: Multi-upstream configuration via CLI
- **WHEN** the server is launched with `--upstreams="IContract1=http://host1,IContract2=http://host2"` and `--contracts-dir=<path>`
- **THEN** the server loads contracts from the directory and routes each contract to its respective upstream destination.

### Requirement: Diagnostic Health Status
The system SHALL expose an HTTP diagnostic status endpoint to report server health, active mode, registered contracts, and recorded interaction statistics.

#### Scenario: Query health status endpoint
- **WHEN** an HTTP GET request is sent to `/__typedmock/status`
- **THEN** the server returns an HTTP 200 JSON response containing current mode, uptime, registered contracts, and interaction counts.

### Requirement: Automatic Stub Export on Exit
The system SHALL persist all recorded C# stub controllers to disk when a graceful shutdown signal is received in Record mode.

#### Scenario: Graceful shutdown in Record mode
- **WHEN** a SIGINT or SIGTERM signal is received by the standalone server running in Record mode with `--out-dir` or `--out-stubs` configured
- **THEN** all recorded interactions are converted into C# controller source files and saved to the designated directory before process exit.
