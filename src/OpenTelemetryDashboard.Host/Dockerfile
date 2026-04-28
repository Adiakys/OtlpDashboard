FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
WORKDIR /app

RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/* \
    && useradd -r -u 10001 otel \
    && mkdir /app/data \
    && chown -R otel /app

EXPOSE 4317 4318
EXPOSE 8080 8081

# Frontend: build the Nuxt SPA into static files. Runs in parallel with the
# .NET build stages (BuildKit picks the layering). Output at
# /web/.output/public is copied into the final image's wwwroot/.
FROM node:22-alpine AS web-build
WORKDIR /web
COPY web/package.json web/pnpm-lock.yaml ./
RUN corepack enable && pnpm install --frozen-lockfile
COPY web/ ./
RUN pnpm run generate

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src

COPY global.json Directory.Build.props Directory.Packages.props ./
COPY .config/ ./.config/

COPY ["src/OpenTelemetryDashboard.Host/OpenTelemetryDashboard.Host.csproj", "src/OpenTelemetryDashboard.Host/"]
COPY ["src/OpenTelemetryDashboard.Core/OpenTelemetryDashboard.Core.csproj", "src/OpenTelemetryDashboard.Core/"]
COPY ["src/OpenTelemetryDashboard.Persistence/OpenTelemetryDashboard.Persistence.csproj", "src/OpenTelemetryDashboard.Persistence/"]
COPY ["src/OpenTelemetryDashboard.Persistence.Sqlite/OpenTelemetryDashboard.Persistence.Sqlite.csproj", "src/OpenTelemetryDashboard.Persistence.Sqlite/"]
COPY ["src/OpenTelemetryDashboard.Ingestion/OpenTelemetryDashboard.Ingestion.csproj", "src/OpenTelemetryDashboard.Ingestion/"]
COPY ["src/OpenTelemetryDashboard.Api/OpenTelemetryDashboard.Api.csproj", "src/OpenTelemetryDashboard.Api/"]

RUN dotnet restore "src/OpenTelemetryDashboard.Host/OpenTelemetryDashboard.Host.csproj"

COPY proto/ proto/
COPY src/ src/

WORKDIR "/src/src/OpenTelemetryDashboard.Host"

RUN dotnet build "OpenTelemetryDashboard.Host.csproj" -c $BUILD_CONFIGURATION -o /app/build

FROM build AS publish
ARG BUILD_CONFIGURATION=Release

RUN dotnet publish "OpenTelemetryDashboard.Host.csproj" \
    -c $BUILD_CONFIGURATION \
    -o /app/publish \
    --no-restore \
    /p:UseAppHost=false

FROM base AS final
WORKDIR /app

COPY --from=publish /app/publish .
# Drop the Nuxt SPA next to the .NET DLLs so `UseStaticFiles()` picks it up.
COPY --from=web-build /web/.output/public ./wwwroot

ENV ASPNETCORE_ENVIRONMENT=Production \
    ASPNETCORE_URLS="" \
    DOTNET_RUNNING_IN_CONTAINER=true \
    OpenTelemetryDashboard__Storage__Provider="Sqlite" \
    OpenTelemetryDashboard__Storage__Sqlite__ConnectionString="Data Source=/app/data/telemetry.db"

USER otel

ENTRYPOINT ["dotnet", "OpenTelemetryDashboard.Host.dll"]