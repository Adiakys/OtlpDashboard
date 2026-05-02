FROM mcr.microsoft.com/dotnet/sdk:10.0-alpine AS build
WORKDIR /src

# Build context is the repository root so Directory.Build.props +
# Directory.Packages.props + demo/Directory.Build.props are in scope.
# The demo csproj files use central package management — without these
# files restore fails NU1015 (PackageReference items have no Version).
COPY global.json Directory.Build.props Directory.Packages.props ./
COPY demo/Directory.Build.props demo/

COPY demo/Client/Client.csproj demo/Client/
RUN dotnet restore demo/Client/Client.csproj

COPY demo/Client/ demo/Client/
RUN dotnet publish demo/Client/Client.csproj -c Release -o /out --no-restore /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/runtime:10.0-alpine
WORKDIR /app
ENV DOTNET_GCConserveMemory=9 \
    DOTNET_GCRetainVM=0
COPY --from=build /out ./
ENTRYPOINT ["dotnet", "Client.dll"]
