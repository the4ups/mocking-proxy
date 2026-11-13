# ==============================================================================
# Build Stage
# ==============================================================================
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy project references
COPY ["src/ExternalService.Contracts/ExternalService.Contracts.csproj", "src/ExternalService.Contracts/"]
COPY ["src/TypedMock.Core/TypedMock.Core.csproj", "src/TypedMock.Core/"]
COPY ["src/ExternalService.Stubs/ExternalService.Stubs.csproj", "src/ExternalService.Stubs/"]

# Restore dependencies
RUN dotnet restore "src/ExternalService.Stubs/ExternalService.Stubs.csproj"

# Copy full source
COPY src/ src/

# Build and publish stubs standalone host
WORKDIR "/src/src/ExternalService.Stubs"
RUN dotnet publish "ExternalService.Stubs.csproj" -c Release -o /app/publish /p:UseAppHost=false

# ==============================================================================
# Runtime Stage
# ==============================================================================
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

COPY --from=build /app/publish .

ENV ASPNETCORE_URLS=http://+:5050
ENV DOTNET_EnableDiagnostics=0

EXPOSE 5050

ENTRYPOINT ["dotnet", "ExternalService.Stubs.dll"]
CMD ["5050"]
