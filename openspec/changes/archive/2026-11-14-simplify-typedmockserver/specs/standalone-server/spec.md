# Spec Delta

## MODIFIED Requirements

### Requirement: CLI Options and Configuration
The system SHALL support configuring listening port, upstream targets, contract discovery paths, and output stub directories via command-line arguments. The server operates exclusively in Record mode.

#### Scenario: Launch in Record mode with target and stub output
- **WHEN** the server is launched with `--port=5050 --target=<url> --out-dir=<path>`
- **THEN** the server starts listening on port 5050 in Record mode and saves generated stub controllers to the specified output directory upon termination.

#### Scenario: Multi-upstream configuration via CLI
- **WHEN** the server is launched with `--upstreams="IContract1=http://host1,IContract2=http://host2"` and `--contracts-dir=<path>`
- **THEN** the server loads contracts from the directory and routes each contract to its respective upstream destination.

## ADDED Requirements

### Requirement: Stub Project Scaffolding Command
The standalone server SHALL provide an `init` subcommand that scaffolds a new stubs host project ready to receive generated controller files.

#### Scenario: Scaffold stubs project via CLI
- **WHEN** the user runs `typedmock init --name=MyService.Stubs --contracts=<path-to-contracts-csproj> --out-dir=<path>`
- **THEN** the system creates a new ASP.NET Core project directory containing `.csproj`, `Program.cs`, `Dockerfile`, and empty `Controllers/` directory

#### Scenario: Init refuses to overwrite existing project
- **WHEN** `typedmock init` targets a directory that already contains a `.csproj` file
- **THEN** the system reports an error and exits without modifying files

