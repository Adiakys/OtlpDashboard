# Multi-Provider Storage Support — Design Spec

**Date:** 2026-04-28
**Author:** brainstorming session
**Status:** approved (pending implementation plan)

## Goal

Estendere la persistence layer dell'OpenTelemetry Dashboard per supportare **SQL Server** e **PostgreSQL** accanto a SQLite, mantenendo un singolo binario/immagine Docker selezionabile a runtime.

## Non-goals

- Migrazione automatica dei dati tra provider (es. `sqlite → postgres`). Cambio provider = DB vuoto.
- Configurazione TLS/SSL esposta in `StorageOptions`. Tutto via connection string.
- Pooling configurabile lato Postgres (PgBouncer ecc.). Default Npgsql.
- Health-check DB-aware (`/healthz/db`). Il `MigrateAsync()` allo startup è già un canary.
- CLI `dotnet ef` come flow operativo. L'auto-migrate on boot copre il caso d'uso.
- JSON columns native (`jsonb` su Postgres, `JSON` SQL Server 2025). Resta string converter (YAGNI).

## Approach

Continuare il pattern già stabilito da `Persistence.Sqlite`: due nuovi progetti specchio (`Persistence.SqlServer`, `Persistence.Postgres`), ognuno con le proprie migrations, design-time factory ed extension method DI. Il composition root `Host` referenzia tutti e 3 i provider; lo `switch` su `StorageProvider` enum in `Program.cs:75-84` si estende a 3 case. Singolo binario, singola immagine Docker, scelta del provider via configurazione runtime.

Le connection string vivono nella sezione standard .NET `ConnectionStrings` (accessibile via `IConfiguration.GetConnectionString(name)`), con nome = nome del provider (`Sqlite`, `SqlServer`, `Postgres`).

## Architettura e struttura dei progetti

```
src/
├── OpenTelemetryDashboard.Persistence/             [invariato — provider-agnostic]
├── OpenTelemetryDashboard.Persistence.Sqlite/      [esistente]
├── OpenTelemetryDashboard.Persistence.SqlServer/   [NUOVO]
│   ├── SqlServerTelemetryStoreExtensions.cs        — AddSqlServerTelemetryStore
│   ├── SqlServerTelemetryDesignTimeDbContextFactory.cs
│   ├── OpenTelemetryDashboard.Persistence.SqlServer.csproj
│   └── Migrations/                                  — Init.cs + Designer + Snapshot
└── OpenTelemetryDashboard.Persistence.Postgres/    [NUOVO]
    ├── PostgresTelemetryStoreExtensions.cs         — AddPostgresTelemetryStore
    ├── PostgresTelemetryDesignTimeDbContextFactory.cs
    ├── OpenTelemetryDashboard.Persistence.Postgres.csproj
    └── Migrations/                                  — Init.cs + Designer + Snapshot
```

**Package EF Core (aggiunti a `Directory.Packages.props`):**
- `Microsoft.EntityFrameworkCore.SqlServer` v10.x
- `Npgsql.EntityFrameworkCore.PostgreSQL` v10.x (fallback v9.x se v10 non ancora disponibile per .NET 10)

**Perché non un unico progetto multi-provider:** EF Core genera migrations relative a un singolo `DbContext` e a un singolo provider per assembly. Tenerli separati è obbligatorio.

**Composition root** (`Host.csproj`): referenzia tutti e 3 i provider via `<ProjectReference>`. L'extension method del provider non scelto a runtime semplicemente non viene chiamato.

## Selezione provider e configurazione

**`StorageOptions`** (`Host/Configuration/StorageOptions.cs`):

```csharp
public enum StorageProvider { Sqlite, SqlServer, Postgres }

public sealed class StorageOptions
{
    public const string SectionName = "OpenTelemetryDashboard:Storage";
    public StorageProvider Provider { get; set; } = StorageProvider.Sqlite;
}
```

Niente più sub-section per le connection string — vivono nella sezione standard `ConnectionStrings`.

**`appsettings.json`** estesi:

```json
"OpenTelemetryDashboard": {
  "Storage": { "Provider": "Sqlite" }
},
"ConnectionStrings": {
  "Sqlite":    "Data Source=./data/telemetry.db",
  "SqlServer": "",
  "Postgres":  ""
}
```

**Switch in `Program.cs:75-84`:**

```csharp
switch (storageProvider)
{
    case StorageProvider.Sqlite:
        builder.Services.AddSqliteTelemetryStore(ResolveConnectionString("Sqlite"));
        break;
    case StorageProvider.SqlServer:
        builder.Services.AddSqlServerTelemetryStore(ResolveConnectionString("SqlServer"));
        break;
    case StorageProvider.Postgres:
        builder.Services.AddPostgresTelemetryStore(ResolveConnectionString("Postgres"));
        break;
    default:
        throw new InvalidOperationException(
            $"Storage provider '{storageProvider}' is not supported in this build.");
}

static Func<IServiceProvider, string> ResolveConnectionString(string name) =>
    sp => sp.GetRequiredService<IConfiguration>().GetConnectionString(name)
          ?? throw new InvalidOperationException($"ConnectionStrings:{name} missing");
```

**Lazy resolution** (via `Func<IServiceProvider, string>`) preserva il pattern già usato dall'overload 2 di `AddSqliteTelemetryStore`, necessario per gli integration test che fanno override config dopo `WebApplication.CreateBuilder`.

**Env vars (Docker / Kubernetes):**

```
OpenTelemetryDashboard__Storage__Provider=Postgres
ConnectionStrings__Postgres=Host=...;Database=...;Username=...;Password=...
```

`IConfiguration` mappa `__` → `:` automaticamente.

## Schema e tipi colonna

Lo schema logico (entità, FK, indici) **resta identico** ai 3 provider — già definito in `Persistence/Configurations/*.cs`, è provider-agnostic. EF Core mappa i tipi .NET ai tipi colonna nativi:

| Tipo .NET / dominio | SQLite (oggi) | SQL Server | PostgreSQL |
|---|---|---|---|
| `byte[]` (TraceId, SpanId, ResourceHash) | `BLOB` | `varbinary(N)` (16/8/32) | `bytea` |
| `string` (Name, Body, attributes JSON) | `TEXT` | `nvarchar(N)` / `nvarchar(max)` | `text` |
| `long` (timestamps `*_unix_nano`) | `INTEGER` | `bigint` | `bigint` |
| `int` (Kind, StatusCode, SeverityNumber) | `INTEGER` | `int` | `integer` |
| `uint` (Flags, dropped counts) | `INTEGER` | `bigint` | `bigint` |
| ID surrogato `long` autoincrement | `Sqlite:Autoincrement` | `IDENTITY(1,1)` | `GENERATED BY DEFAULT AS IDENTITY` |

**Regola**: niente `HasColumnType` provider-specifici nelle `IEntityTypeConfiguration<T>` esistenti. Le differenze sono assorbite dal driver EF Core.

**Indici**: tutti gli indici già definiti (`ix_spans_trace_id`, `ix_logs_time_unix_nano`, ecc.) sono B-tree standard — funzionano nativi su SQL Server e Postgres senza modifiche.

**Naming `snake_case`**: `ApplySnakeCaseNaming()` resta in `TelemetryDbContext.OnModelCreating`. Coerenza tra provider; idiomatico su Postgres, legale (case-insensitive default) su SQL Server.

**JSON attributi**: `AttributesJsonConverter` resta string-based. Compatibile con tutti i 3 provider (mappa a `TEXT`/`nvarchar(max)`/`text`).

**Versioni minime supportate:**
- PostgreSQL 14+
- SQL Server 2019+ (incluso `Express`/`Developer`)
- SQLite — invariato (libreria embedded)

## Migrations strategy

**Generazione:** `dotnet ef migrations add Init -p src/OpenTelemetryDashboard.Persistence.SqlServer -s src/OpenTelemetryDashboard.Host` (analogo per Postgres). Le migrations sono **frozen snapshot** del modello al momento dell'aggiunta — vivono in `Migrations/` di ognuno dei 2 nuovi progetti.

**`MigrationsAssembly`**: ogni extension method DI specifica `MigrationsAssembly(typeof(...).Assembly.GetName().Name)` come già fa `Persistence.Sqlite`.

**Auto-migrate on startup** (`Program.cs:112-117`): `MigrateAsync()` invariato. Sceglie l'assembly migrations dal provider attivo.

**Costo "fisiologico"**: future modifiche allo schema (in `Persistence`) richiederanno di rigenerare una nuova migration in **tutti e tre** i provider. Documentato come parte del workflow contributor.

## Retention e ingestion

**Retention** (`Persistence/Retention/EfCore*RetentionPolicy.cs`): usa `ExecuteDeleteAsync()` con filtro `Where(x => x.TimeUnixNano < cutoffNano)`. EF Core lo traduce in `DELETE FROM` nativo per ciascun provider. **Niente cambia.**

**Ingestion sink** (`EfCoreTraceSink`, `EfCoreLogSink`): provider-agnostic, usano `IDbContextFactory<TelemetryDbContext>` e `SaveChangesAsync()`. **Niente cambia.**

**Resource dedup** (`ResourceHasher` SHA-256 → primary key `byte[32]`): primary key su `varbinary(32)` (SQL Server) e `bytea` (Postgres) supportata e indicizzabile. **Niente cambia.**

## Test integration con Testcontainers

**Strategia: livello B** — Testcontainers per Postgres + SQL Server, SQLite resta in-process.

**Dipendenze nuove** (`tests/OpenTelemetryDashboard.IntegrationTests/`):
- `Testcontainers.PostgreSql`
- `Testcontainers.MsSql`
- `Xunit.SkippableFact` (per soft-skip senza Docker)

**Fixture provider-aware:** i 25 test esistenti vengono parametrizzati. Tre fixture distinte:

- `SqliteFactoryFixture` — invariato (file SQLite temp, in-process)
- `PostgresFactoryFixture` — `IAsyncLifetime`, `PostgreSqlBuilder` per container effimero, override config con `Provider=Postgres` + `ConnectionStrings:Postgres` dal container
- `SqlServerFactoryFixture` — analogo con `MsSqlBuilder`

**Pattern xUnit:** decisione finale (xUnit `[Theory]` con `MemberData<TProviderFixture>` vs xUnit `[Collection]` triplicato) presa in fase di plan. Preferenza: `Collection` per provider per evitare overhead start container condiviso tra test.

**Nuovi test specifici cross-provider:**
- Migration apply su DB vuoto (assert tabelle/indici esistono)
- Round-trip `byte[]` con NUL bytes (TraceId/SpanId)
- FK integrity (Resource referenziato da Span/Log con `OnDelete.Restrict`)
- Retention su volume realistico (~10k record), verifica conteggi pre/post
- Dedup Resource concorrente (2 batch con stessa Resource hash, no duplicati)

**Skip soft:** test su Postgres/SqlServer marcati `[SkippableFact]` — fail soft se Docker manca, sviluppatore SQLite-only non bloccato. CI obbligatoria con Docker.

## Docker compose

**Un solo servizio dashboard.** Container DB in profili separati. Operatore sceglie provider via env vars (inline o `.env`).

```yaml
services:
  dashboard:
    build: .
    ports: ["4317:4317", "4318:4318"]
    environment:
      OpenTelemetryDashboard__Storage__Provider: ${STORAGE_PROVIDER:-Sqlite}
      ConnectionStrings__Sqlite:    ${CONN_SQLITE:-Data Source=/data/telemetry.db}
      ConnectionStrings__SqlServer: ${CONN_SQLSERVER:-}
      ConnectionStrings__Postgres:  ${CONN_POSTGRES:-}
    volumes: [dashboard-data:/data]

  postgres:
    profiles: ["postgres"]
    image: postgres:16-alpine
    environment:
      POSTGRES_DB: telemetry
      POSTGRES_USER: otel
      POSTGRES_PASSWORD: ${POSTGRES_PASSWORD:-otel}
    volumes: [postgres-data:/var/lib/postgresql/data]

  sqlserver:
    profiles: ["sqlserver"]
    image: mcr.microsoft.com/mssql/server:2022-latest
    environment:
      ACCEPT_EULA: "Y"
      MSSQL_SA_PASSWORD: ${MSSQL_SA_PASSWORD:-Otel-Strong!2026}
    volumes: [sqlserver-data:/var/opt/mssql]

volumes:
  dashboard-data:
  postgres-data:
  sqlserver-data:
```

**Uso operativo:**

```bash
# Default (SQLite, niente DB container)
docker compose up

# Postgres
STORAGE_PROVIDER=Postgres \
CONN_POSTGRES="Host=postgres;Database=telemetry;Username=otel;Password=otel" \
docker compose --profile postgres up

# SQL Server
STORAGE_PROVIDER=SqlServer \
CONN_SQLSERVER="Server=sqlserver;Database=telemetry;User Id=sa;Password=Otel-Strong!2026;TrustServerCertificate=True" \
docker compose --profile sqlserver up
```

Template `.env.example` fornito a repo root.

**Dockerfile invariato** — l'immagine include tutti i provider via composition root.

## Error handling

- **Boot — migration failure**: `MigrateAsync()` exception → fatal, container crash → restart loop Kubernetes/Docker. Log esplicito con provider name e connection string redatta (no password).
- **Boot — connection string vuota**: `InvalidOperationException` allo startup con messaggio "ConnectionStrings:{name} missing".
- **Boot — provider sconosciuto**: già coperto dal `default` del switch.
- **Runtime — `SaveChangesAsync` failure** (FK violation, deadlock, disconnessione): bubble up al `TelemetryWriter`, già gestito con retry + log.
- **Test — Docker non disponibile**: `[SkippableFact]` produce `Skipped`, non `Failed`.

## Documentazione

**README esteso** con:
- Tabella provider supportati + versioni minime
- Esempio connection string per ognuno (con placeholder credenziali)
- Sezione "scelta del provider" con trade-off (SQLite single-process / SqlServer enterprise / Postgres)
- Sezione "come aggiungere un nuovo provider" (workflow contributor)
- Note su breaking change: chi ha `OpenTelemetryDashboard:Storage:Sqlite:ConnectionString` deve spostare a `ConnectionStrings:Sqlite`

**CHANGELOG**: nuova entry "Multi-provider storage support — added SqlServer and Postgres" con nota breaking change config.

## Breaking changes

**Configurazione storage**: la connection string SQLite si sposta da `OpenTelemetryDashboard:Storage:Sqlite:ConnectionString` a `ConnectionStrings:Sqlite`. Niente fallback automatico — chi aggiorna deve modificare `appsettings.json`/env vars. Documentato in README e CHANGELOG.

## Scope esclusi (riferimento)

- Migrazione dati cross-provider (operatore parte da DB vuoto)
- TLS/SSL config esposta in opzioni (tutto via connection string)
- Pooling Npgsql custom (default driver)
- Health-check DB-aware
- CLI `dotnet ef` come flow operativo
- JSON columns native (`jsonb` Postgres / `JSON` SQL Server 2025) — resta string converter

## Successo

- Tutti i 25+ integration test esistenti passano in green su SQLite, Postgres, SqlServer
- `docker compose --profile postgres up` e `docker compose --profile sqlserver up` portano la dashboard a uno stato `healthy` con telemetria ingestita correttamente
- Cambio provider = solo modifica env var + restart, nessuna ricompilazione
- Niente regressione perf su SQLite (stesso path codice del provider attivo)
