# syntax=docker/dockerfile:1.7

# ---- Frontend: build the Nuxt SPA into static files ----
# Runs in parallel with the .NET stages (BuildKit picks the layering).
# Output at /web/.output/public is copied into the final image's wwwroot/.
FROM node:22-alpine AS web-build
WORKDIR /web
ENV CI=1
COPY web/package.json web/pnpm-lock.yaml ./
# `--prefer-frozen-lockfile` keeps the fast headless install when the lockfile
# is in sync with package.json; falls back to a regular resolution if a
# developer bumped a dep without running `pnpm install` locally so the image
# still builds.
RUN --mount=type=cache,target=/root/.local/share/pnpm/store \
    corepack enable && pnpm install --prefer-frozen-lockfile
COPY web/ ./
RUN pnpm run generate

# ---- Backend: SDK Alpine for a smaller, musl-aligned build context ----
# Alpine SDK keeps the build cache lean and matches the runtime libc, so
# native EF Core / Npgsql / SqlClient bundles resolve to linux-musl-x64
# without ambiguity.
FROM mcr.microsoft.com/dotnet/sdk:10.0-alpine AS publish
ARG BUILD_CONFIGURATION=Release
WORKDIR /src

# Grpc.Tools ships `protoc` and `grpc_csharp_plugin` as precompiled glibc
# binaries (`linux_x64`) which won't execute on Alpine/musl. Install the
# native Alpine packages and override the msbuild paths during publish.
RUN apk add --no-cache protoc grpc-plugins

COPY global.json Directory.Build.props Directory.Packages.props .editorconfig ./
COPY .config/ ./.config/

# Restore on csproj-only layers so the package cache survives source changes.
COPY ["src/OpenTelemetryDashboard.Host/OpenTelemetryDashboard.Host.csproj",                  "src/OpenTelemetryDashboard.Host/"]
COPY ["src/OpenTelemetryDashboard.Core/OpenTelemetryDashboard.Core.csproj",                  "src/OpenTelemetryDashboard.Core/"]
COPY ["src/OpenTelemetryDashboard.Dashboards/OpenTelemetryDashboard.Dashboards.csproj",      "src/OpenTelemetryDashboard.Dashboards/"]
COPY ["src/OpenTelemetryDashboard.Persistence/OpenTelemetryDashboard.Persistence.csproj",    "src/OpenTelemetryDashboard.Persistence/"]
COPY ["src/OpenTelemetryDashboard.Persistence.Sqlite/OpenTelemetryDashboard.Persistence.Sqlite.csproj",         "src/OpenTelemetryDashboard.Persistence.Sqlite/"]
COPY ["src/OpenTelemetryDashboard.Persistence.SqlServer/OpenTelemetryDashboard.Persistence.SqlServer.csproj",   "src/OpenTelemetryDashboard.Persistence.SqlServer/"]
COPY ["src/OpenTelemetryDashboard.Persistence.PostgreSql/OpenTelemetryDashboard.Persistence.PostgreSql.csproj", "src/OpenTelemetryDashboard.Persistence.PostgreSql/"]
COPY ["src/OpenTelemetryDashboard.Ingestion/OpenTelemetryDashboard.Ingestion.csproj",        "src/OpenTelemetryDashboard.Ingestion/"]
COPY ["src/OpenTelemetryDashboard.Api/OpenTelemetryDashboard.Api.csproj",                    "src/OpenTelemetryDashboard.Api/"]

RUN --mount=type=cache,target=/root/.nuget/packages \
    dotnet restore "src/OpenTelemetryDashboard.Host/OpenTelemetryDashboard.Host.csproj"

COPY proto/ proto/
COPY src/ src/

# Single publish step: skip the intermediate `build` stage that the default
# template carries from Visual Studio (it just duplicates work). Symbols and
# debug info are stripped because container images run with no debugger.
RUN --mount=type=cache,target=/root/.nuget/packages \
    dotnet publish "src/OpenTelemetryDashboard.Host/OpenTelemetryDashboard.Host.csproj" \
        -c $BUILD_CONFIGURATION \
        -o /app/publish \
        --no-restore \
        /p:UseAppHost=false \
        /p:DebugType=None \
        /p:DebugSymbols=false \
        /p:Protobuf_ProtocFullPath=/usr/bin/protoc \
        /p:gRPC_PluginFullPath=/usr/bin/grpc_csharp_plugin

# ---- Runtime: Alpine ASP.NET (~110 MB compressed, vs. ~220 MB Debian-slim) ----
# Alpine ships busybox which already provides `wget` for the healthcheck, so
# we don't pay the apt-get + curl install cost.
FROM mcr.microsoft.com/dotnet/aspnet:10.0-alpine AS final
WORKDIR /app

# Non-root user. Alpine's adduser syntax differs from useradd (Debian).
# `/app/builtin-libraries` is the default second scan path for widget libraries
# and `/app/builtin-dashboards` for built-in dashboards baked into derived
# images — derived Dockerfiles just `COPY` here, no env vars or appsettings
# overrides required.
RUN adduser -D -u 10001 otel \
    && mkdir -p /app/data /app/builtin-libraries /app/builtin-dashboards \
    && chown -R otel:otel /app

# OTLP gRPC and OTLP HTTP/Protobuf (also serves /healthz and the SPA).
EXPOSE 4317 4318

COPY --from=publish --chown=otel:otel /app/publish ./
# Drop the Nuxt SPA next to the .NET DLLs so `UseStaticFiles()` picks it up.
COPY --from=web-build --chown=otel:otel /web/.output/public ./wwwroot

# Runtime tuning. Two groups:
#  * App wiring — ASPNETCORE_*, sqlite default location.
#  * .NET runtime — globalization invariance (we only ever format with
#    `CultureInfo.InvariantCulture` server-side, so dropping ICU saves both
#    image size and resident set), plus GC settings that bias the runtime
#    toward releasing memory back to the OS instead of holding it for
#    headroom (DOTNET_GCConserveMemory=9, DOTNET_GCRetainVM=0).
ENV ASPNETCORE_ENVIRONMENT=Production \
    ASPNETCORE_URLS="" \
    DOTNET_RUNNING_IN_CONTAINER=true \
    DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=true \
    DOTNET_GCConserveMemory=9 \
    DOTNET_GCRetainVM=0 \
    DOTNET_TieredPGO=1 \
    Dashboard__Storage__Provider="Sqlite" \
    ConnectionStrings__Sqlite="Data Source=/app/data/telemetry.db"

USER otel

# Built-in busybox wget — no extra package needed.
HEALTHCHECK --interval=15s --timeout=3s --start-period=15s --retries=3 \
    CMD wget -q -O - http://localhost:4318/healthz >/dev/null 2>&1 || exit 1

ENTRYPOINT ["dotnet", "OpenTelemetryDashboard.Host.dll"]
