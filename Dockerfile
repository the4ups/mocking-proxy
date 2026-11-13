# ==============================================================================
# Build Stage
# ==============================================================================
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy project files first for optimal layer caching
COPY ["src/TypedMock.Core/TypedMock.Core.csproj", "src/TypedMock.Core/"]
COPY ["src/TypedMock.Server/TypedMock.Server.csproj", "src/TypedMock.Server/"]

# Restore dependencies
RUN dotnet restore "src/TypedMock.Server/TypedMock.Server.csproj"

# Copy full source tree
COPY src/ src/

# Build and publish standalone server
WORKDIR "/src/src/TypedMock.Server"
RUN dotnet publish "TypedMock.Server.csproj" -c Release -o /app/publish /p:UseAppHost=false

# ==============================================================================
# Runtime Stage
# ==============================================================================
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# Setup default directories for dynamic contract scanning and stub generation
RUN mkdir -p /contracts /GeneratedStubs

# Copy published binaries
COPY --from=build /app/publish .

# Environment configuration
ENV ASPNETCORE_URLS=http://+:5050
ENV DOTNET_EnableDiagnostics=0

# Default port
EXPOSE 5050

# Volumes for external contracts and generated stubs
VOLUME ["/contracts", "/GeneratedStubs"]

# Entrypoint allowing CLI arguments to be passed directly:
# e.g.: docker run -p 5050:5050 typedmock --mode=Record --target=http://host.docker.internal:5001
ENTRYPOINT ["dotnet", "TypedMock.Server.dll"]
CMD ["--mode=Replay", "--port=5050"]
