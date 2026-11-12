# TypedMock.Server

Standalone CLI contract-driven mock proxy server for .NET microservices.

## Installation as a .NET Global Tool

```bash
dotnet tool install -g TypedMock.Server
```

## Usage

```bash
# Run in Replay mode
typedmock --mode=Replay --port=5050

# Run in Record mode against upstream service
typedmock --mode=Record --port=5050 --target=http://localhost:5001 --out-dir=./GeneratedStubs

# Run with Dynamic Contract Discovery and Multi-Upstream routing
typedmock --mode=Record --port=5050 --contracts-dir=./contracts --upstreams="IWeatherApi=http://localhost:5001,IPaymentApi=http://localhost:5002" --out-dir=./GeneratedStubs
```

## Diagnostic Endpoint

Once started, check server status and registered contracts at:
`http://localhost:5050/__typedmock/status`

## License

MIT
