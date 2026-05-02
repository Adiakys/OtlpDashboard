FROM mcr.microsoft.com/dotnet/sdk:10.0-alpine AS build
WORKDIR /src

# Build context is the repository root so the CPM chain
# (Directory.Build.props + Directory.Packages.props + demo/Directory.Build.props)
# is in scope. Without it, restore fails NU1015 because the demo csproj
# files don't carry inline `Version` attributes — versions live centrally.
COPY global.json Directory.Build.props Directory.Packages.props ./
COPY demo/Directory.Build.props demo/

# Restore on csproj-only layers so the package cache survives source changes.
COPY demo/Server/Server.csproj demo/Server/
RUN dotnet restore demo/Server/Server.csproj

# Bring the rest of the source and publish.
COPY demo/Server/ demo/Server/
RUN dotnet publish demo/Server/Server.csproj -c Release -o /out --no-restore /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0-alpine
# `krb5-libs` provides libgssapi_krb5.so.2 — Npgsql 10 dlopens it on
# every connect and segfaults (SIGSEGV → exit 139) when the lib is
# missing, even though we don't actually use Kerberos auth.
RUN apk add --no-cache krb5-libs
WORKDIR /app
EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080 \
    DOTNET_GCConserveMemory=9 \
    DOTNET_GCRetainVM=0
COPY --from=build /out ./
ENTRYPOINT ["dotnet", "Server.dll"]
