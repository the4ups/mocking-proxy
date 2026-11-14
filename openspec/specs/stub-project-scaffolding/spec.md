# stub-project-scaffolding Specification

## Purpose
Scaffolds a self-contained ASP.NET Core stubs host project from the CLI, producing all files needed to compile, run, and containerize generated stub controllers without depending on TypedMock.Core at runtime.

## Requirements

### Requirement: Project Scaffolding via CLI
The system SHALL generate a new stubs host project directory when the `init` command is invoked, producing a compilable ASP.NET Core executable project skeleton.

#### Scenario: Scaffold a new stubs project
- **WHEN** the user runs `typedmock init --name=MyService.Stubs --contracts=../MyService.Contracts/MyService.Contracts.csproj --out-dir=./src`
- **THEN** the system creates `./src/MyService.Stubs/` containing `MyService.Stubs.csproj`, `Program.cs`, `Dockerfile`, and an empty `Controllers/` directory

#### Scenario: Generated project compiles without TypedMock.Core
- **WHEN** the scaffolded project is compiled with `dotnet build`
- **THEN** the build succeeds without any reference to `TypedMock.Core`

#### Scenario: Scaffold refuses to overwrite existing project
- **WHEN** `typedmock init` targets a directory that already contains a `.csproj` file
- **THEN** the system reports an error and does not modify existing files

### Requirement: Self-Contained Stubs Host
The scaffolded project SHALL function as a standalone ASP.NET Core application that discovers and hosts all MVC controllers in its assembly, with CLI port configuration and graceful shutdown support.

#### Scenario: Run scaffolded project as standalone host
- **WHEN** the user runs `dotnet run --project MyService.Stubs -- 5050`
- **THEN** the application starts an HTTP server on port 5050 hosting all controllers found in the assembly

#### Scenario: Graceful shutdown on SIGINT
- **WHEN** a SIGINT signal is sent to the running stubs host
- **THEN** the application shuts down cleanly without error

### Requirement: Scenario Resolution in Stubs Host
The scaffolded project SHALL include built-in middleware or a base class that resolves the active mock scenario from request headers or query parameters and makes it available to hosted controllers via the HTTP context.

#### Scenario: Scenario resolved from header
- **WHEN** an incoming request includes the `X-Mock-Scenario` header with value `"error-case"`
- **THEN** hosted controllers can read `"error-case"` from `HttpContext.Items["TypedMock.Scenario"]`

#### Scenario: Default scenario fallback
- **WHEN** an incoming request includes neither a scenario header nor a `__scenario` query parameter
- **THEN** the resolved scenario name is `"default"`

### Requirement: Dockerfile Generation
The scaffolded project SHALL include a `Dockerfile` suitable for building and running the stubs host as a container image.

#### Scenario: Docker build succeeds
- **WHEN** `docker build` is run against the scaffolded project directory
- **THEN** a container image is produced that exposes the stubs host on port 8080
