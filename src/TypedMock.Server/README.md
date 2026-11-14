# TypedMock.Server

Standalone CLI contract-driven recording mock proxy and stubs project scaffolder for .NET microservices.

## Installation as a .NET Global Tool

```bash
dotnet tool install -g TypedMock.Server
```

## Commands

### 1. Scaffold a New Stubs Project (`init`)

Scaffold a self-contained ASP.NET Core project ready to receive generated controllers:

```bash
typedmock init --name=MyService.Stubs --contracts=../MyService.Contracts/MyService.Contracts.csproj --out-dir=./src
```

This generates:
- `MyService.Stubs.csproj` (referencing only the contracts, zero TypedMock dependency)
- `Program.cs` (Kestrel host with built-in scenario resolution middleware)
- `Dockerfile` (containerized stubs host)
- `Controllers/` directory for generated stub controllers

### 2. Record Traffic & Generate C# Stubs

Run the proxy against your live upstream dependency:

```bash
# Single upstream target:
typedmock --port=5050 --target=http://localhost:5001 --contracts=./contracts/MyService.Contracts.dll --out-dir=./src/MyService.Stubs/Controllers

# Multi-upstream routing:
typedmock --port=5050 --contracts-dir=./contracts --upstreams="IWeatherApi=http://localhost:5001,IPaymentApi=http://localhost:5002" --out-dir=./src/MyService.Stubs/Controllers
```

When you stop the proxy (`Ctrl+C`), strongly-typed C# stub controllers are automatically generated and saved!

### 3. Diagnostic Endpoint

While the server is running, query status, registered contracts, and upstream mappings at:
`http://localhost:5050/__typedmock/status`

## Running Generated Stubs

Once recorded, run the stubs host directly without TypedMock:

```bash
dotnet run --project src/MyService.Stubs -- 5050
```

## License

MIT
