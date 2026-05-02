FROM mcr.microsoft.com/dotnet/sdk:10.0-alpine AS build
WORKDIR /src

COPY Client.csproj ./
RUN dotnet restore Client.csproj

COPY . ./
RUN dotnet publish Client.csproj -c Release -o /out --no-restore /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/runtime:10.0-alpine
WORKDIR /app
ENV DOTNET_GCConserveMemory=9 \
    DOTNET_GCRetainVM=0
COPY --from=build /out ./
ENTRYPOINT ["dotnet", "Client.dll"]
