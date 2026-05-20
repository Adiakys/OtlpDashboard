FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
USER $APP_UID
WORKDIR /app
EXPOSE 8080
EXPOSE 8081

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY global.json Directory.Build.props Directory.Packages.props .editorconfig ./
COPY .config/ ./.config/
COPY ["src/OpenTelemetryDashboard.Host/OpenTelemetryDashboard.Host.csproj", "src/OpenTelemetryDashboard.Host/"]
COPY ["src/OpenTelemetryDashboard.Core/OpenTelemetryDashboard.Core.csproj", "src/OpenTelemetryDashboard.Core/"]
COPY ["src/OpenTelemetryDashboard.Dashboards/OpenTelemetryDashboard.Dashboards.csproj", "src/OpenTelemetryDashboard.Dashboards/"]
COPY ["src/OpenTelemetryDashboard.Persistence/OpenTelemetryDashboard.Persistence.csproj", "src/OpenTelemetryDashboard.Persistence/"]
COPY ["src/OpenTelemetryDashboard.Persistence.Sqlite/OpenTelemetryDashboard.Persistence.Sqlite.csproj", "src/OpenTelemetryDashboard.Persistence.Sqlite/"]
COPY ["src/OpenTelemetryDashboard.Persistence.SqlServer/OpenTelemetryDashboard.Persistence.SqlServer.csproj", "src/OpenTelemetryDashboard.Persistence.SqlServer/"]
COPY ["src/OpenTelemetryDashboard.Persistence.PostgreSql/OpenTelemetryDashboard.Persistence.PostgreSql.csproj", "src/OpenTelemetryDashboard.Persistence.PostgreSql/"]
COPY ["src/OpenTelemetryDashboard.Ingestion/OpenTelemetryDashboard.Ingestion.csproj", "src/OpenTelemetryDashboard.Ingestion/"]
COPY ["src/OpenTelemetryDashboard.Api/OpenTelemetryDashboard.Api.csproj", "src/OpenTelemetryDashboard.Api/"]
RUN dotnet restore "src/OpenTelemetryDashboard.Host/OpenTelemetryDashboard.Host.csproj"
COPY . .
WORKDIR "/src/src/OpenTelemetryDashboard.Host"
RUN dotnet build "./OpenTelemetryDashboard.Host.csproj" -c $BUILD_CONFIGURATION -o /app/build

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "./OpenTelemetryDashboard.Host.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM node:22-alpine AS web-build
WORKDIR /web
ENV CI=1
COPY web/package.json web/pnpm-lock.yaml ./
RUN corepack enable && pnpm install --prefer-frozen-lockfile
COPY web/ ./
RUN pnpm run generate

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
COPY --from=web-build /web/.output/public ./wwwroot
ENTRYPOINT ["dotnet", "OpenTelemetryDashboard.Host.dll"]
