FROM mcr.microsoft.com/dotnet/sdk:10.0-alpine AS build
WORKDIR /src

# Restore separately so the next build only rebuilds when sources change.
COPY Server.csproj ./
RUN dotnet restore Server.csproj

COPY . ./
RUN dotnet publish Server.csproj -c Release -o /out --no-restore /p:UseAppHost=false

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
