# OpenTelemetry Dashboard

Servizio .NET 10 che riceve telemetria OTLP (traces, logs, metrics) da client instrumentati e la persiste / espone per consultazione. Si ispira al dashboard di .NET Aspire ma, a differenza di quello, usa **EF Core con storage relazionale reale** per traces e logs (Aspire tiene tutto in RAM).

> **Stato attuale**: pipeline OTLP completa + GET `/api/v1/logs` e `/api/v1/traces` (paginate keyset-based) + **UI web** Vue 3 / Nuxt 4 in SPA mode, servita dallo stesso host .NET sulla porta `:4318` + **autenticazione a bearer token statici** opt-in (`DASHBOARD__BROWSERTOKEN` per la read-API, `DASHBOARD__OTLP__APIKEY` per l'ingestion OTLP).

---

## Panoramica

| Segnale | Trasporto | Storage |
|---|---|---|
| Traces  | gRPC 4317 / HTTP 4318 (Protobuf) | **EF Core → SQLite** (provider agnostico) |
| Logs    | gRPC 4317 / HTTP 4318 (Protobuf) | **EF Core → SQLite** |
| Metrics | gRPC 4317 / HTTP 4318 (Protobuf) | **In-memory ring buffer** (non persistite in v1) |

**Non in scope** (annotati per il futuro): TLS, login form UI (il BrowserToken va passato a mano), provider Postgres/SQL Server/MySQL, metriche persistite, partial-success granulare, sampling server-side, filtri avanzati sulla query API (severity, service, attributi, full-text), streaming live/tailing su UI.

---

## Architettura

### Dipendenze tra moduli

```
                       ┌────────────────────────────┐
                       │            Host            │  ← composition root
                       └──┬──────┬──────────┬───────┘
                          │      │          │
           ┌──────────────┘      │          └─────────────┐
           ▼                     ▼                        ▼
   ┌───────────────┐   ┌──────────────────┐   ┌────────────────────────┐
   │   Ingestion   │   │       Api        │   │  Persistence.Sqlite    │
   │ (OTLP write)  │   │  (GET read API)  │   │ (provider + migrations)│
   └──────┬────────┘   └────────┬─────────┘   └──────────┬─────────────┘
          │                     │                        │
          ▼                     ▼                        ▼
     ┌─────────────┐   ┌────────────────────┐   ┌────────────────────┐
     │    Core     │◄──┤ (uses *Reader)     │◄──┤    Persistence     │
     │ (dominio,   │   │                    │   │ (DbContext, sink + │
     │  channel,   │   │                    │   │  reader EF Core,   │
     │ *Sink/*Read │   │                    │   │  Writer dispatcher,│
     │ contratti)  │   │                    │   │  Metrics/InMemory) │
     └─────────────┘   └────────────────────┘   └────────────────────┘
```

Le frecce puntano verso le dipendenze. Regole:
- **`Core` è puro dominio**: niente EF Core, niente ASP.NET, niente Protobuf. Contiene solo entità, value object, `TelemetryChannel`, e i sei contratti `ITraceSink`/`ILogSink`/`IMetricSink` + `ITraceReader`/`ILogReader`/`IMetricReader`. Niente implementazioni infrastrutturali.
- **`Persistence`** ospita tutte le implementazioni di storage/ingestion-backend: i sink+reader EF Core su `TelemetryDbContext`, il `TelemetryWriter` dispatcher, la `ResourceCache`, e — in una sottocartella `Metrics/InMemory/` — il ring-buffer che implementa `IMetricSink`+`IMetricReader`. Il nome "Persistence" è un po' largo perché include anche storage effimero (in-memory metrics), ma tiene insieme quello che è tutto "infrastruttura di ingestion". Quando servirà un altro provider metrics (ClickHouse, Prometheus, DB persistente) lo si estrarrà in un progetto dedicato.
- **`Persistence.Sqlite`** è il provider EF Core specifico per SQLite (migrations + `.UseSqlite(...)`). Stesso pattern per futuri `Persistence.PostgreSql`, `Persistence.SqlServer`.
- **`Ingestion`** sa solo di OTLP e del dominio — non conosce storage né sink, mette batch sul channel e basta.
- **`Api`** è la read-API HTTP/JSON: referenzia solo `Core` (i contratti `ITraceReader`/`ILogReader`), non conosce EF Core né Protobuf. Simmetrico a `Ingestion` sul lato opposto (write vs read).

Questo disaccoppiamento permette di sostituire un pezzo senza toccare gli altri.

### Flusso di un request OTLP (uniforme per tutti i segnali)

```
 Client OTLP (.NET SDK / Python SDK / OTel Collector / ...)
          │
          ▼  POST /v1/{traces,logs,metrics}       Export RPC
     ┌────────────────┐  ┌────────────────────┐
     │ Kestrel :4318  │  │ Kestrel :4317 (h2) │
     └────────┬───────┘  └────────┬───────────┘
              │                   │
   OtlpHttpEndpoints        OtlpTraceService / LogsService / MetricsService
   (Ingestion/Http)         (Ingestion/Grpc)
              │                   │
              └─────────┬─────────┘
                        ▼
         Otlp*Translator  ← proto → dominio + validazione canonica
                        │   (restituisce TraceBatch / LogBatch / MetricBatch)
                        ▼
         TelemetryChannel.TryWrite  ← bounded; full ⇒ 429 / ResourceExhausted
                        │
                        ▼  (async)
         TelemetryWriter (BackgroundService) — DISPATCHER
         • legge batch, raggruppa per tipo concreto
         • drain deterministico su shutdown
                        │
         ┌──────────────┼──────────────┐
         ▼              ▼              ▼
   ITraceSink      ILogSink       IMetricSink
   EfCoreTraceSink EfCoreLogSink  InMemoryMetricSink
   (Persistence)   (Persistence)  (Persistence/Metrics/InMemory)
         │              │              │
         ▼              ▼              ▼
      DbContext      DbContext      InMemoryMetricStorage
      SaveChanges    SaveChanges    (ring-buffer per instrument key)
         │              │
         ▼              ▼
      SQLite (resources / spans / span_events / span_links / log_records)
```

Il read-side (contratti `ITraceReader`/`ILogReader`/`IMetricReader`) è simmetrico: una futura API inietta il Reader del segnale e ottiene i dati senza vedere DbContext né lo storage in-memory.

Le **metriche** seguono **lo stesso percorso**: il translator produce un `MetricBatch`, che finisce sullo stesso `TelemetryChannel`; il `TelemetryWriter` dispatcha per tipo concreto invocando `IMetricSink.WriteAsync` (che nell'implementazione in-memory scrive nel ring-buffer). Il contratto di lettura separato `IMetricReader` permette a una futura API di query di accedere ai dati senza conoscere il sink.

---

## Moduli — chi fa cosa

### `src/OpenTelemetryDashboard.Core`
**Ruolo**: il cuore del dominio. Niente dipendenze infrastrutturali (niente EF Core, niente ASP.NET, niente Protobuf). Solo modelli, value object e **contratti**.

- `Domain/` — entità POCO: `Resource`, `Span`, `SpanEvent`, `SpanLink`, `LogRecord`, `Instrument`, `DataPoint`. Value object `TraceId` (16 byte come 2×`ulong`), `SpanId` (8 byte come 1 `ulong`), entrambi con `ToString()` che emette hex lowercase canonical W3C.
- `Abstractions/` — i **sei contratti** della pipeline (CQRS-light):
  - **Write-side (sink)**: `ITraceSink`, `ILogSink`, `IMetricSink`. Accettano `IReadOnlyList<TBatch>` per permettere un `SaveChanges` amortizzato lato infrastruttura.
  - **Read-side (reader)**: `ITraceReader`, `ILogReader`, `IMetricReader`. API minima in v1 — saranno estesi dalla fase query API.
- `Hashing/ResourceHasher.cs` — SHA-256 deterministico sulla forma canonica di una Resource (service.name + instance.id + schema_url + attributi ordinati con tag di tipo). Chiave primaria delle Resource su DB, fulcro del dedup.
- `Ingestion/TelemetryBatch.cs` — gerarchia sealed `TelemetryBatch → { TraceBatch, LogBatch, MetricBatch }`. `MetricSample(InstrumentKey, Instrument, DataPoint)` è l'atomo della `MetricBatch`.
- `Ingestion/TelemetryChannel.cs` — wrapper bounded su `Channel<TelemetryBatch>`, con `TelemetryChannelOptions` + `IngestionShutdownOptions`.
- `Metrics/InstrumentKey.cs` — value object che identifica una time-series (hex hash + scope + name + kind). È in Core perché parte del contratto `IMetricSink`/`IMetricReader`.
- `Common/ByteArrayEqualityComparer.cs` — equality content-based per `byte[]`.

**Quando tocchi questo modulo**: cambi il modello di dominio, aggiungi/modifichi un contratto di I/O, modifichi la politica di backpressure del channel. **Niente implementazioni concrete qui dentro.**

### `src/OpenTelemetryDashboard.Persistence`
**Ruolo**: mapping EF Core provider-agnostico, sink+reader relazionali, e il `TelemetryWriter` dispatcher.

- `TelemetryDbContext.cs` — `DbSet<Resource>`, `DbSet<Span>`, `DbSet<LogRecord>`. Applica le `IEntityTypeConfiguration` e poi `ApplySnakeCaseNaming()`.
- `Configurations/` — mapping delle entità: chiavi, FK, indici, owned collections (`Span.Events` e `Span.Links` come tabelle separate via `OwnsMany`).
- `Converters/` — `TraceIdConverter`, `SpanIdConverter`, `NullableSpanIdConverter`, `AttributesJsonConverter`, `ObjectJsonConverter`.
- `Naming/SnakeCaseNamingExtensions.cs` — convention snake_case applicata a tabelle, colonne, PK, FK, indici.
- `Sinks/EfCoreTraceSink.cs` e `Sinks/EfCoreLogSink.cs` — implementano `ITraceSink` / `ILogSink`. Apre un `TelemetryDbContext` dal pool, dedupe le Resource via `ResourceCache` (condivisa tra sink), `AddRange` + `SaveChangesAsync`.
- `Readers/EfCoreTraceReader.cs` e `Readers/EfCoreLogReader.cs` — implementano `ITraceReader` / `ILogReader`. Sempre `AsNoTracking`. API minima in v1 (find-by-id, query-by-trace, recent-logs): da estendere quando arriverà la fase API.
- `Ingestion/TelemetryWriter.cs` — `BackgroundService` **dispatcher**: consuma il channel, accumula fino a `MaxBatchSize`/`FlushIntervalMs`, raggruppa i batch per tipo concreto e invoca il sink corrispondente (`ITraceSink`/`ILogSink`/`IMetricSink`). Non conosce più `DbContext`. `StopAsync` chiude il channel e fa drain con scadenza (`DrainTimeoutSeconds`).
- `Ingestion/ResourceCache.cs` — LRU bounded thread-safe condivisa dai due sink EF Core.
- `Ingestion/ResourceUpserter.cs` — helper interno per la logica di dedup + inserimento Resource. Caching post-SaveChanges per evitare di avvelenare la cache se `SaveChanges` fallisce.
- `Metrics/InMemory/` — implementazione in-memory (ring-buffer) dei contratti metriche:
  - `InMemoryMetricStorage.cs` — stato condiviso tra sink e reader: `ConcurrentDictionary<InstrumentKey, Entry>` con `Entry = (Instrument, RingBuffer<DataPoint>)`. Espone `TryRecord`, `Keys`, `GetInstrument`, `GetPoints`. Registrata come singleton.
  - `InMemoryMetricSink.cs` — implementa `IMetricSink`: per ogni `MetricSample` in ogni `MetricBatch` chiama `storage.TryRecord`.
  - `InMemoryMetricReader.cs` — implementa `IMetricReader`: delega al lato lettura di `InMemoryMetricStorage`.
  - `RingBuffer.cs` — buffer circolare thread-safe con wrap-around.
  - `InMemoryMetricStoreOptions.cs` — `MaxInstruments`, `MaxPointsPerInstrument`. Sezione config: `OpenTelemetryDashboard:Metrics:InMemory`.
  - `InMemoryMetricStoreServiceCollectionExtensions.cs` — `AddInMemoryMetricStore(configuration)` bind delle options e registra storage + sink + reader singleton.
- `ServiceCollectionExtensions.cs` — espone `AddTelemetryPersistenceCore(configureProvider)` (DbContext factory + sink+reader EF Core + cache) e `AddTelemetryWriter()` (hosted service).

**Quando tocchi questo modulo**: aggiungi/modifichi colonne o indici, estendi i reader con nuove query, cambi la strategia di dedup, tuni la batching.
**Vincolo duro su EF Core**: **niente API provider-specifica** (`HasComputedColumnSql`, `JSONB`, ecc.) nei file `TelemetryDbContext` / `Configurations/` / `Sinks/` / `Readers/`. Il codice EF Core deve compilare contro `Microsoft.EntityFrameworkCore.Relational` senza un provider concreto. Il contenuto sotto `Metrics/InMemory/` vive in-process e non ha niente a che fare con EF.

**Quando estrarre un nuovo progetto**: se `Metrics/InMemory/` dovesse crescere (eviction LRU, ingestion fanout, query language) o se si aggiungesse un secondo metric provider (ClickHouse, Prometheus remote-write), allora conviene estrarlo in `OpenTelemetryDashboard.Metrics.<Nome>` e rimuovere la sottocartella da qui. Per v1 non ne vale la pena.

### `src/OpenTelemetryDashboard.Persistence.Sqlite`
**Ruolo**: wire-up del provider SQLite + migrations.

- `SqliteTelemetryStoreExtensions.cs` — `AddSqliteTelemetryStore(connectionString)` delega al `AddTelemetryPersistenceCore` del progetto base, ma passa `.UseSqlite(...)`.
- `SqliteTelemetryDesignTimeDbContextFactory.cs` — richiesto da `dotnet-ef` a design time (legge `OTELDASHBOARD_SQLITE_DESIGNTIME` o fallback a `telemetry.design.db`).
- `Migrations/` — cartella delle migration EF Core per questo provider (prima migration: `Init`).

**Pattern per aggiungere un nuovo provider (es. PostgreSQL)**: crea `OpenTelemetryDashboard.Persistence.PostgreSql` con struttura identica — solo le migration saranno diverse (scriptate dal provider). Registralo nell'`Host` con uno `switch` su `StorageOptions.Provider`.

### `src/OpenTelemetryDashboard.Ingestion`
**Ruolo**: superficie OTLP (gRPC + HTTP) e mapping proto→dominio.

- `Protocol` (implicito) — i `.proto` vivono come git submodule in `proto/opentelemetry-proto/` pinato al tag `v1.10.0`; il csproj li referenzia con `<Protobuf Include="..." GrpcServices="Server" />` e `Grpc.Tools` genera i tipi server-side.
- `Translators/OtlpConversion.cs` — utility per trasformare `KeyValue`/`AnyValue` OTLP in `IReadOnlyDictionary<string, object?>` con tipi nativi .NET (string, bool, long, double, byte[], list, dict).
- `Translators/OtlpTraceTranslator.cs` / `OtlpLogTranslator.cs` — da `ExportXxxServiceRequest` → `TraceBatch`/`LogBatch`. Qui vivono le **validazioni canoniche** OTLP: scarta span con `trace_id`/`span_id` tutto zero o di lunghezza sbagliata, scarta `end_time < start_time`.
- `Translators/OtlpMetricTranslator.cs` — produce un `MetricBatch?` (null se niente da fare) nella stessa forma di trace/log; il handler lo mette sul `TelemetryChannel`. Supporta Gauge e Sum; Histogram/Exponential/Summary sono accettati ma droppati con log (future work).
- `Grpc/` — `OtlpTraceService`, `OtlpLogsService`, `OtlpMetricsService`. Ereditano dalle classi `*ServiceBase` generate da Grpc.Tools. Canale pieno ⇒ `RpcException(StatusCode.ResourceExhausted)`.
- `Http/OtlpHttpEndpoints.cs` — minimal API su `MapGroup("/v1")`: `POST /v1/traces`, `/v1/logs`, `/v1/metrics`. Content-Type obbligatorio `application/x-protobuf`; 415 se diverso, 400 se body malformato, 429 con `Retry-After: 1` se il channel è pieno.

**Quando tocchi questo modulo**: aggiungi un nuovo endpoint, rafforzi la validazione, supporti un nuovo tipo di metrica.

### `src/OpenTelemetryDashboard.Api`
**Ruolo**: read-API HTTP/JSON su logs e traces. Referenzia solo `Core` (consuma `ILogReader`/`ITraceReader`); nessuna dipendenza EF Core o Protobuf. Montata dal Host su `/api/v1/...` sullo stesso listener HTTP dell'OTLP (`:4318`), path distinti.

Struttura (3 sottocartelle + 4 file di root, layout *flat per ruolo trasversale / folder per risorsa*):

- `QueryApiExtensions.cs` — **superficie pubblica del modulo**. `AddQueryApi(configuration)` registra options + JSON serializer; `MapQueryApi()` monta le route su `/api/v1`.
- `QueryApiOptions.cs` — `DefaultLimit`, `MaxLimit`, `MaxWindowHours`. Sezione config: `OpenTelemetryDashboard:QueryApi`.
- `QueryValidation.cs` — traduce i parametri di query in `LogQuery`/`TraceQuery` o restituisce `Dictionary<string, string[]>` per `Results.ValidationProblem()` (RFC 7807). Regole: `from`/`to` UTC obbligatori (offset zero), `from < to`, `to - from ≤ MaxWindowHours`, `limit ∈ [1, MaxLimit]`, cursor decodificabile.
- `CursorCodec.cs` — cursor opaco base64url di `"{L|T}:{time}:{secondaryKey}"`. Il tag `L`/`T` impedisce cross-use fra endpoint log e trace. Non è un security token; la secondary-key garantisce ordinamento stabile anche con `TimeUnixNano` identici.
- `Contracts/` — DTO di risposta JSON, uno per file: `PagedResponse<T>`, `LogRecordDto`, `TraceSummaryDto`, `TraceDetailDto`, `SpanDto`, `SpanEventDto`, `SpanLinkDto`. TraceId/SpanId come stringa hex lowercase; timestamp come `DateTimeOffset` ISO-8601.
- `Endpoints/LogsEndpoints.cs` — `LogQueryParameters` (binding `[AsParameters]`) + handler `GetLogsAsync`.
- `Endpoints/TracesEndpoints.cs` — `TraceQueryParameters` + handler `GetTracesAsync` (listing) e `GetTraceAsync` (dettaglio).
- `Mappings/LogMappings.cs` — `LogRecord.ToDto()`.
- `Mappings/TraceMappings.cs` — `TraceSummary.ToDto()` + `Span.ToDto()` (inclusi eventi e link owned).

Le dependency injection vivono in `QueryApiExtensions.AddQueryApi` per la parte "registrazione", e come parametri dei metodi degli handler per la parte "risoluzione" (ASP.NET minimal API inietta `ILogReader`/`ITraceReader`/`IOptions<QueryApiOptions>` direttamente nella signature del delegate).

La conversione `nano ↔ DateTimeOffset` è fornita da `Core.Common.UnixNanoTime`.

**Quando tocchi questo modulo**: nuovi filtri sulla query, nuovi endpoint di lettura, nuovo DTO. **Non** aggiungere qui dipendenze da EF Core o da protobuf: consumi i dati tramite i contratti `*Reader` di `Core`.

### `src/OpenTelemetryDashboard.Host`
**Ruolo**: composition root. Unico progetto che sa "tutto" e compone i pezzi.

- `Program.cs` — builder ASP.NET Core:
  - legge `IngestionServerOptions` e `StorageOptions` (bind + `ValidateOnStart`);
  - configura Kestrel con **due listener**: `:4317` HTTP/2 solo (gRPC cleartext), `:4318` HTTP/1.1+2 (REST + gRPC fallback);
  - `AddGrpc` con `MaxReceiveMessageSize = 16 MiB` (il default 4 MiB silenziava export grossi);
  - `AddRateLimiter` policy `"otlp-http"` (fixed window) agganciata al gruppo HTTP `/v1`;
  - registra `AddTelemetryCore` + `AddOtlpIngestion` + `AddSqliteTelemetryStore` + `AddTelemetryWriter` + `AddQueryApi`;
  - esegue `Database.MigrateAsync()` in avvio in ogni environment (idempotente su schema già al corrente; garantisce che i container di produzione creino lo schema al primo boot);
  - `UseDefaultFiles()` + `UseStaticFiles()` servono la SPA Nuxt da `wwwroot/`; `MapFallbackToFile("index.html")` instrada le route SPA deep-link non-API al client-side router;
  - `AddDashboardAuth` registra lo scheme `StaticToken` + le policy `read-api` / `otlp-ingest`; `UseAuthentication()`/`UseAuthorization()` e `.RequireAuthorization(...)` sui map applicano le policy (vedi [Autenticazione](#autenticazione));
  - allunga `HostOptions.ShutdownTimeout` per lasciare al `TelemetryWriter` il tempo di drenare il channel.
- `Configuration/IngestionServerOptions.cs` — porte, message size, rate limit, shutdown timeout.
- `Configuration/StorageOptions.cs` — provider scelto (`Sqlite` per ora) + connection string.
- `Configuration/DashboardAuthOptions.cs` — bearer token statici (`BrowserToken`, `Otlp.ApiKey`). Bindato su `Dashboard:*`.
- `Authentication/StaticTokenAuthenticationHandler.cs` — handler custom `AuthenticationHandler<AuthenticationSchemeOptions>` che valida `Authorization: Bearer …` in tempo costante; assegna `role=browser` o `role=otlp`.
- `Authentication/AuthServiceCollectionExtensions.cs` — `AddDashboardAuth(configuration)` registra scheme + policy `read-api`/`otlp-ingest`; policy allow-all se il token corrispondente non è configurato (opt-in).
- `appsettings.json` + `appsettings.Development.json` — default runtime.
- `Dockerfile` — multi-stage: (1) `node:22-alpine` builda la SPA Nuxt con `pnpm generate`, (2) `sdk:10.0` compila e pubblica l'app .NET, (3) `aspnet:10.0` finale copia il publish + la SPA in `wwwroot/`. Utente non-root, volume `/app/data`, `curl` per health-check.
- `wwwroot/` — gitignored; riempito al build dalla build Nuxt. Servito da `UseStaticFiles()` sulla porta `:4318`.

**Quando tocchi questo modulo**: aggiungi una nuova dependency da comporre, cambi le porte, attivi un nuovo middleware, aggiungi configurazione.

### `web/`
**Ruolo**: frontend Vue 3 / Nuxt 4 in SPA mode. Compila in statico (`pnpm generate` → `.output/public/`) e viene servito dall'host .NET da `wwwroot/`. Niente Node in produzione.

- `nuxt.config.ts` — `ssr: false`, runtime config con `apiBaseUrl = '/api'` (same-origin), `nitro.devProxy` che inoltra `/api/*` a `http://localhost:4318/api/*` durante `pnpm dev`.
- `app/services/` — classi TypeScript pure:
  - `HttpClientService.ts` — wrapper minimale su `$fetch` (ofetch). Singleton condiviso. Se l'`AuthStore` contiene un token aggiunge `Authorization: Bearer …` a ogni request.
  - `AuthStore.ts` — persiste il bearer token in `localStorage` con TTL (default 30 min); espone `setToken/getToken/clear/isAuthenticated`. Unico punto che sa dove è salvato il token — così il futuro login page chiamerà solo `setToken(userInput)` senza toccare nient'altro.
  - `LogsService.ts`, `TraceService.ts` — API wrapper, consumano `HttpClientService` via costruttore.
  - `types.ts` — DTO TypeScript, mirror dei DTO C# in `Api/Contracts/`.
- `app/plugins/services.ts` — Nuxt plugin che (1) istanzia `AuthStore`, (2) cattura `?token=…` dall'URL di landing salvandolo e ripulendo l'address bar, (3) istanzia `HttpClientService` con il token provider dell'AuthStore, (4) costruisce `LogsService`/`TraceService`. Esposti via `useNuxtApp()` come `$authStore`, `$http`, `$logsService`, `$traceService`.
- `app/layouts/default.vue` — sidebar (Logs / Traces) + content slot.
- `app/pages/logs/`, `app/pages/traces/` — ogni pagina è un orchestratore dichiarativo: compone componenti da `components/` locale, delega state+fetch a un composable `usePage.ts` accanto alla pagina. Il composable riceve il service come dipendenza.
- `app/pages/traces/[traceId].vue` — dettaglio trace con lista span (tree indent via `parentSpanId`) + slide-over dettagli span.
- `app/components/` — cross-page: `TimeRangePicker` (normalizza sempre a UTC prima di chiamare l'API), `LoadMoreButton`.
- `tests/services/` — spec Vitest sui service (mock di `$fetch`).

**Quando tocchi questo modulo**: aggiungi una pagina, un campo DTO, un componente. **Non** toccare la struttura DI: chi serve il backend deve passare per `HttpClientService`.

### Test

- `tests/OpenTelemetryDashboard.UnitTests` — 63 test su: dominio (`TraceId`/`SpanId` round-trip + `TryParse`, `RingBuffer`, `ResourceHasher`), infrastruttura (`InMemoryMetricStorage` + `Sink`+`Reader`, `TelemetryWriter`, `ResourceCache`, `TelemetryDbContext`) e read API (`CursorCodec` round-trip + cross-tag reject, `QueryValidation` incluso UTC enforcement, `DomainMappings` con round-trip nano↔`DateTimeOffset`).
- `tests/OpenTelemetryDashboard.IntegrationTests` — 25 test E2E. `TestHostFixture` avvia l'app via `WebApplicationFactory<Program>` con un file SQLite unico per run sotto la temp dir. `OtlpHttpIngestionTests` copre ingestion (Content-Type sbagliato → 415, protobuf malformato → 400, `trace_id` zero droppato silenziosamente, persistenza trace/log, metrica gauge). `QueryApiTests` copre la read-API: `GET /api/v1/logs|traces` con `from`/`to`, paginazione keyset multi-pagina, finestra troppo larga → 400, cursor invalido → 400, dettaglio trace 200/400 (id malformato) / 404 (id sconosciuto), DB vuoto → items=[]. `AuthenticationTests` verifica con entrambi i token configurati: 401 senza header, 200 con token giusto, 403 con token del ruolo sbagliato, `/healthz` sempre pubblico.

---

## Prerequisiti

- .NET SDK **10.0.106** o superiore (vedi `global.json`, `rollForward: feature`).
- (Opzionale per sviluppo UI) **Node 22+** e **pnpm** (via corepack).
- (Opzionale) Docker + Docker Compose per il run contenuto (che builda anche la SPA automaticamente).
- Niente altro: SQLite è embedded, `dotnet-ef` è gestito come local tool in `.config/dotnet-tools.json`.

## Quickstart — run locale (solo backend)

```bash
# una-tantum: restore dei tool locali (dotnet-ef)
dotnet tool restore

# build + test della solution
dotnet build OpenTelemetryDashboard.slnx
dotnet test  OpenTelemetryDashboard.slnx

# run del servizio (migration automatiche all'avvio)
dotnet run --project src/OpenTelemetryDashboard.Host
# ora ascolta:
#   - gRPC  OTLP  : localhost:4317 (h2c)
#   - HTTP  OTLP  : localhost:4318 (API + UI + /healthz)
```

Smoke test manuale:
```bash
curl -fsS http://localhost:4318/healthz          # → Healthy
# in browser: http://localhost:4318 (se wwwroot/ popolato con la SPA)
```

Senza SPA buildata in `wwwroot/`, la root `/` restituisce 404 — per il frontend segui la sezione **[Web UI](#web-ui)**.

## Quickstart — docker compose

```bash
docker compose up --build
# gRPC  OTLP : localhost:4317
# HTTP  OTLP : localhost:4318 (API + UI + /healthz)
# dati      : volume `dashboard-data` (sqlite in /app/data/telemetry.db)
```

La build Docker include anche la SPA (stage Node), quindi `http://localhost:4318/` apre direttamente l'UI.

Il `docker-compose.yml` in root fa sia **build** (dal `src/OpenTelemetryDashboard.Host/Dockerfile`) sia **run**. Per un rebuild pulito: `docker compose build --no-cache`.

## Come mandare telemetria dall'esterno

Qualsiasi client OTLP punta bene a questo servizio. Esempio con OpenTelemetry SDK .NET:

```csharp
builder.Services.AddOpenTelemetry()
    .WithTracing(t => t
        .AddSource("MyApp")
        .AddOtlpExporter(o =>
        {
            o.Endpoint = new Uri("http://localhost:4317");
            o.Protocol = OtlpExportProtocol.Grpc;
        }));
```

Oppure via OTel Collector / qualunque SDK: basta puntare a `http://<host>:4317` (gRPC) o `http://<host>:4318/v1/{traces,logs,metrics}` (HTTP Protobuf).

---

## Query API (read-side)

HTTP/JSON sul listener `:4318`, path `/api/v1/...`. Paginazione **keyset cursor-based**: `limit + cursor` opzionali; il server restituisce un oggetto `{ items, nextCursor }` — `nextCursor` è `null` quando non ci sono altre pagine. `from` e `to` sono **obbligatori** (`DateTimeOffset` ISO-8601, UTC), la finestra massima è configurabile (`MaxWindowHours`, default 24h).

### `GET /api/v1/logs`

Query params:
- `from` *(ISO-8601, required)* — inclusivo.
- `to` *(ISO-8601, required)* — esclusivo.
- `limit` *(int, optional)* — default `DefaultLimit` (100), max `MaxLimit` (1000).
- `cursor` *(string, optional)* — token opaco restituito dalla pagina precedente.

```bash
curl "http://localhost:4318/api/v1/logs?from=2026-04-23T00:00:00Z&to=2026-04-23T23:59:59Z&limit=50"
```

Response (200):
```json
{
  "items": [
    {
      "time": "2026-04-23T12:34:56.789+00:00",
      "severityNumber": 9,
      "severityText": "INFO",
      "body": "user.login",
      "traceId": "0123456789abcdeffedcba9876543210",
      "spanId": "0123456789abcdef",
      "scopeName": "tests",
      "resourceHash": "…64 hex chars…",
      "attributes": { "user.id": 42 }
    }
  ],
  "nextCursor": "MTc0ODU2NTY5NjAwMDAwMDAwMDow"
}
```

### `GET /api/v1/traces`

Stessi parametri di `/logs`. Restituisce **trace summaries** (una riga per `TraceId`, aggregata dai suoi span).

```bash
curl "http://localhost:4318/api/v1/traces?from=2026-04-23T00:00:00Z&to=2026-04-23T23:59:59Z"
```

Response (200):
```json
{
  "items": [
    {
      "traceId": "0123456789abcdeffedcba9876543210",
      "rootSpanName": "http.GET /orders",
      "start": "2026-04-23T12:34:56.789+00:00",
      "end":   "2026-04-23T12:34:57.013+00:00",
      "durationMs": 224.0,
      "spanCount": 8,
      "rootStatusCode": "Ok",
      "resourceHash": "…"
    }
  ],
  "nextCursor": null
}
```

### `GET /api/v1/traces/{traceId}`

Dettaglio di un singolo trace: tutti gli span (ordinati per `StartUnixNano`), con events e links. `traceId` è 32 char hex lowercase (formato W3C). Malformato → 400; inesistente → 404.

### Errori

Formato RFC 7807 (`Results.ValidationProblem`) per 400 — campo `errors` per-parametro. 429 non è attualmente emesso dalla read-API (nessun rate limiting in questa fase). Se la finestra eccede `MaxWindowHours` il 400 esplicita il motivo nel campo errors.

### Note di sicurezza

Autenticazione opt-in via bearer token configurato da environment variable — vedi **[Autenticazione](#autenticazione)** più sotto. Se i token non sono valorizzati, gli endpoint restano pubblici (comportamento dev-friendly). Nessun TLS: in produzione internet-facing la API va messa dietro un reverse proxy TLS.

---

## Web UI

Vue 3 + Nuxt 4 in SPA mode. Sorgenti in `web/`, build statica in `web/.output/public/`, servita dall'host .NET da `src/OpenTelemetryDashboard.Host/wwwroot/` sulla stessa porta dell'API (`:4318`).

### URL map (in prod)

| Path | Risposta |
|---|---|
| `/` | `index.html` (SPA shell Nuxt) |
| `/logs`, `/traces`, `/traces/{id}` | `index.html` via SPA fallback; il router Vue risolve client-side |
| `/_nuxt/*.js`, `/_nuxt/*.css`, `/favicon.ico` | asset statici da `wwwroot/` |
| `/api/v1/...` | Query API JSON |
| `/v1/{traces,logs,metrics}` | OTLP ingestion (Protobuf) |
| `/healthz` | health check |

### Dev — modalità A (iterazione rapida, consigliata)

Due terminali:
```bash
# terminale 1: backend
dotnet run --project src/OpenTelemetryDashboard.Host

# terminale 2: frontend
cd web && pnpm install && pnpm dev
# Nuxt dev server su http://localhost:3000 con hot reload,
# proxy automatico /api/* → http://localhost:4318/api/*
```
Browser su `http://localhost:3000`. Il dev-proxy mantiene il same-origin (niente CORS da configurare).

### Dev — modalità B (simulazione prod)

```bash
cd web && pnpm install && pnpm generate
# copia .output/public in wwwroot del Host
mkdir -p ../src/OpenTelemetryDashboard.Host/wwwroot
cp -r .output/public/. ../src/OpenTelemetryDashboard.Host/wwwroot/

cd .. && dotnet run --project src/OpenTelemetryDashboard.Host
# browser: http://localhost:4318
```

### Produzione (Docker)

`docker compose up --build` produce un unico container che serve API + UI sulla porta `4318`. Il `Dockerfile` ha uno stage Node che builda la SPA e la copia in `wwwroot/` nell'immagine finale.

### Stack lato UI

- **Nuxt 4** + **Vue 3** + **TypeScript**
- **@nuxt/ui v4** (Tailwind v4 internamente)
- `$fetch` / `ofetch` per HTTP
- **Vitest** per i test dei service
- Zero state management globale: gli service sono stateless, ogni pagina gestisce il proprio state in un composable locale `usePage.ts`

### Architettura service layer

```
HttpClientService  ◄── singleton, costruito dal plugin services.ts
      ▲
      │
      ├── LogsService    ◄── costruito dal plugin
      └── TraceService   ◄── costruito dal plugin

useNuxtApp().$http | .$logsService | .$traceService
      ▲
      │
      └── usato dai composable usePage.ts delle pagine
             ▲
             │
             └── chiamato dall'orchestratore page *.vue
```

Per aggiungere un metodo API:
1. tipo TS in `web/app/services/types.ts` (mirror del DTO C#)
2. metodo in `LogsService.ts` o `TraceService.ts`
3. chiamata dal composable `usePage.ts` della pagina

---

## Autenticazione

Due bearer-token statici configurati via environment variable. L'implementazione usa meccanismi standard ASP.NET Core (`AddAuthentication` + `AddAuthorization`) con un custom scheme (`StaticToken`) che confronta constant-time il valore dell'header `Authorization: Bearer …` contro i due token configurati.

### Env var

| Env var | Serve a | Endpoint protetti |
|---|---|---|
| `DASHBOARD__BROWSERTOKEN` | Chiamare la read-API dalla UI (o curl) | `GET /api/v1/logs`, `/api/v1/traces`, `/api/v1/traces/{id}` |
| `DASHBOARD__OTLP__APIKEY` | Pushare telemetria OTLP | `POST /v1/{traces,logs,metrics}` (HTTP) + gRPC `:4317` |

Il doppio underscore è la convenzione .NET per separare la gerarchia della config (es. `DASHBOARD__OTLP__APIKEY` → `Dashboard:Otlp:ApiKey`).

### Comportamento opt-in

Se **un token non è valorizzato** (vuoto o assente), la policy corrispondente degrada a "allow-all" e gli endpoint restano **pubblici**. Questo preserva il workflow dev e i test esistenti senza dover manipolare ambienti. All'avvio, se uno o entrambi i token sono vuoti, viene emesso un `LogWarning` esplicito per non lasciare dubbi.

Per forzare auth piena in produzione: settare **entrambe** le env var con valori random ≥ 32 char.

### Codici di risposta

| Scenario | Codice |
|---|---|
| Nessun header `Authorization` | `401 Unauthorized` |
| Token sconosciuto | `401 Unauthorized` |
| Token corretto ma ruolo sbagliato (es. browser token su OTLP) | `403 Forbidden` |
| Token corretto e ruolo giusto | `200 OK` / `2xx` |
| `/healthz` | `200 OK` sempre (mai protetto) |
| `/`, `/logs`, `/traces/…`, `/_nuxt/*`, `/favicon.ico` | pubblici (SPA shell deve caricarsi) |

### Esempi client

Browser (curl) verso la read-API:
```bash
curl -H "Authorization: Bearer $DASHBOARD__BROWSERTOKEN" \
     "http://localhost:4318/api/v1/logs?from=2026-04-23T00:00:00Z&to=2026-04-23T01:00:00Z"
```

SDK OpenTelemetry .NET (ingestion):
```csharp
.AddOtlpExporter(o =>
{
    o.Endpoint = new Uri("http://dashboard:4317");
    o.Headers  = $"Authorization=Bearer {apiKey}";
});
```

Env var generico (tutti gli SDK OTel, Collector incluso):
```
OTEL_EXPORTER_OTLP_ENDPOINT=http://dashboard:4317
OTEL_EXPORTER_OTLP_HEADERS=Authorization=Bearer <DASHBOARD__OTLP__APIKEY>
```

docker-compose override:
```yaml
services:
  dashboard:
    environment:
      DASHBOARD__BROWSERTOKEN: <random-browser-token>
      DASHBOARD__OTLP__APIKEY: <random-otlp-apikey>
```

### UI — come autenticare

Due modalità (entrambe attive):

**Login form** (default): qualunque pagina che riceve una 401 dalla read-API fa redirect automatico a `/login?next=<pagina_richiesta>`. Il form accetta la password e, se valida, salva il token nell'`AuthStore` e riporta l'utente alla destinazione originale. La logica di redirect vive nel `$fetch` interceptor di `plugins/services.ts`: **nessuna pagina conosce l'autenticazione** — tutte le chiamate passano da `HttpClientService`, il 401 viene intercettato un'unica volta.

**Deep link con token** (comodo in dev): passando `?token=<DASHBOARD__BROWSERTOKEN>` nel query-string della prima visita (es. `http://localhost:4318/traces/abc?token=...`), il plugin lo salva e ripulisce l'URL. Utile per bookmark automatizzati o link generati da script.

Quando il token scade (30 min default) la prima call API successiva riceve 401 e manda l'utente a `/login`. Stesso percorso se la password è stata cambiata.

**Loop protection**: il form di login chiama l'API di validazione e gestisce il 401 localmente ("password errata"); l'interceptor salta il redirect quando `window.location.pathname === '/login'` per evitare cicli.

### Non in scope (step futuri)

- Bottone di logout nella sidebar (add: `$authStore.clear(); navigateTo('/login')`)
- JWT / OIDC / Identity
- Rotazione chiavi / multi-tenant
- RBAC granulare
- TLS (si demanda a un reverse proxy)
- Audit log degli accessi

---

## Dove metto il codice se…

| Obiettivo | File / progetto |
|---|---|
| aggiungere un **campo** al modello Span/Log | `Core/Domain/Span.cs` (o `LogRecord.cs`) + `Persistence/Configurations/SpanConfiguration.cs` + `dotnet ef migrations add <Nome>` dal progetto `Persistence.Sqlite` |
| aggiungere un **indice** DB | `Persistence/Configurations/*Configuration.cs` + nuova migration |
| aggiungere un **nuovo provider DB** (es. PostgreSQL) | nuovo progetto `Persistence.PostgreSql` con pattern identico a `Persistence.Sqlite`, poi aggiungi il caso nello `switch` di `Host/Program.cs` |
| sostituire lo **store metriche** (es. ClickHouse time-series) | inizialmente puoi affiancare una nuova implementazione di `IMetricSink`/`IMetricReader` accanto a `Persistence/Metrics/InMemory/`. Se cresce, estrai in un nuovo progetto `OpenTelemetryDashboard.Metrics.<Nome>`. In entrambi i casi sostituisci `AddInMemoryMetricStore(...)` in `Host/Program.cs` con la nuova extension |
| esporre una **nuova query sul DB** (es. API che restituisce gli span di un trace) | estendi `ITraceReader`/`ILogReader` in `Core/Abstractions` con il nuovo metodo, aggiungi l'implementazione in `Persistence/Readers/EfCoreXxxReader.cs` con `AsNoTracking`, e aggiungi l'endpoint in `Api/Endpoints/{Logs,Traces}Endpoints.cs` (registrandolo in `Api/QueryApiExtensions.MapQueryApi`) |
| aggiungere un **filtro** alla query API (es. `severity`, `service`) | estendi `LogQuery`/`TraceQuery` in `Core/Abstractions/Queries/ReaderQueries.cs`, fai passare il campo nel binding (`Api/Endpoints/{Logs,Traces}Endpoints.cs`) e nella validazione (`Api/QueryValidation.cs`), implementa la condizione EF Core in `Persistence/Readers/*Reader.cs` |
| cambiare il **formato del cursor** di paginazione | `Api/CursorCodec.cs` — il cursor è opaco lato client, non è un contratto pubblico |
| aggiungere una **validazione OTLP** | `Ingestion/Translators/OtlpTraceTranslator.cs` (o l'analogo per log/metric), dove già ci sono i check `IsEmpty` / lunghezze / timestamp |
| modificare la **backpressure policy** | `Core/Ingestion/TelemetryChannel.cs` (`BoundedChannelOptions`) + `TelemetryChannelOptions` |
| tunare la **batch size** del writer | `Core/Ingestion/TelemetryChannelOptions.cs` (`MaxBatchSize`, `FlushIntervalMs`) |
| aggiungere un **header di auth** | (fuori scope v1) middleware in `Host/Program.cs` prima di `MapOtlpGrpcServices` / `MapOtlpHttpEndpoints` |
| esporre un **nuovo endpoint di query HTTP** | handler in `Api/Endpoints/{Logs,Traces}Endpoints.cs` + nuovo DTO in `Api/Contracts/` + mapping in `Api/Mappings/{Log,Trace}Mappings.cs` + registrazione in `Api/QueryApiExtensions.MapQueryApi`. Inietta `ITraceReader`/`ILogReader`/`IMetricReader` come parametro dell'handler — **non** toccare `TelemetryDbContext` direttamente |
| aggiungere un **nuovo tipo di metrica** (es. Histogram) | `Ingestion/Translators/OtlpMetricTranslator.cs` — aggiungi `case Metric.DataOneofCase.Histogram` e decidi se persistere o mantenere in-memory |
| aggiungere una **pagina UI** | `web/app/pages/<nome>/index.vue` (orchestratore) + `usePage.ts` (composable di state/fetch) + `components/` (logica specifica). Aggiungi voce nel menu in `web/app/layouts/default.vue` |
| aggiungere una **chiamata API nel frontend** | tipo TS in `web/app/services/types.ts` → metodo in `LogsService.ts`/`TraceService.ts` → uso dal composable della pagina. **Non** usare `$fetch` diretto nelle pagine |
| aggiornare la **rotta SPA** | niente da toccare lato .NET: `MapFallbackToFile("index.html")` inoltra ogni path non-API al router Vue |
| aggiungere / modificare un **token di auth** | `Host/Configuration/DashboardAuthOptions.cs` per il binding + eventualmente una nuova policy in `Host/Authentication/AuthServiceCollectionExtensions.cs`. `StaticTokenAuthenticationHandler` confronta l'header contro tutti i token configurati |
| proteggere un **nuovo endpoint** | `.RequireAuthorization("read-api")` o `"otlp-ingest"` sulla sua `Map*` in `Host/Program.cs`. Se è un nuovo ruolo, aggiungi una nuova policy |

---

## Convenzioni

- **C# moderno**: `record` / `readonly record struct` / `required init` / file-scoped namespace / pattern matching esteso. `Nullable` enabled + `WarningsAsErrors` su tutta la solution, `AnalysisLevel=latest-recommended`. Il codice deve compilare **zero warning** in Release.
- **Identificatori OTLP** (`trace_id`, `span_id`) rimangono `byte[]` in rete (protobuf) ma nel dominio sono `TraceId`/`SpanId` struct — si confrontano by-value e stampano in hex lowercase (formato W3C Trace Context). Non loggare mai `byte[]` grezzo, usa `ToString()`.
- **Attributi** sono `IReadOnlyDictionary<string, object?>`. Il DB li conserva come **JSON in colonna `TEXT`** (con `AttributesJsonConverter` + `ObjectJsonConverter`). Questa scelta è provider-agnostica; la query strutturata sugli attributi è rimandata a quando serve.
- **Snake_case** per tabelle/colonne/indici/FK. Convention applicata in `SnakeCaseNamingExtensions.ApplySnakeCaseNaming()` sul modello finale.
- **Central Package Management** (`Directory.Packages.props`): nessuna `Version=` dentro ai csproj, solo `PackageVersion` in cima.
- **Directory.Build.props** test: `tests/Directory.Build.props` sopprime `CA1707` (underscore nei nomi dei test, idiomatici xUnit) e altre regole non utili ai test.
- **Design-time vs runtime DbContext**: il runtime lo prende via `IDbContextFactory<TelemetryDbContext>` (pooled). A design-time, `dotnet-ef` usa `SqliteTelemetryDesignTimeDbContextFactory`. Il `TelemetryWriter` crea un contesto per batch — non tenere mai un `DbContext` vivo a lungo.

---

## Struttura directory

```
.
├── .config/dotnet-tools.json          # dotnet-ef pinato
├── .editorconfig
├── .gitignore
├── .gitmodules
├── Directory.Build.props              # LangVersion, Nullable, WarningsAsErrors, AnalysisLevel
├── Directory.Packages.props           # CPM: tutte le versioni NuGet
├── docker-compose.yml                 # dev compose (build da sorgente + run)
├── global.json                        # SDK 10.0.106 rollForward feature
├── OpenTelemetryDashboard.slnx        # solution XML
├── README.md                          # questo file
│
├── proto/
│   └── opentelemetry-proto/           # submodule git @ v1.10.0
│
├── src/
│   ├── OpenTelemetryDashboard.Core/               # dominio + abstractions + pipeline
│   ├── OpenTelemetryDashboard.Persistence/        # EF Core + writer + Metrics/InMemory (ring-buffer)
│   ├── OpenTelemetryDashboard.Persistence.Sqlite/ # provider SQLite + migrations
│   ├── OpenTelemetryDashboard.Ingestion/          # OTLP write-side: gRPC + HTTP + translators
│   ├── OpenTelemetryDashboard.Api/                # read-side: GET /api/v1/logs|traces (JSON)
│   └── OpenTelemetryDashboard.Host/               # composition root + appsettings + Dockerfile
│       └── wwwroot/                               # (gitignored) SPA statica copiata da web/.output/public
│
├── tests/
│   ├── Directory.Build.props                      # convenzioni specifiche per test
│   ├── OpenTelemetryDashboard.UnitTests/
│   └── OpenTelemetryDashboard.IntegrationTests/
│
└── web/                                            # frontend Vue 3 / Nuxt 4 SPA
    ├── nuxt.config.ts                              # ssr: false + dev proxy /api → :4318
    ├── package.json
    ├── app/
    │   ├── app.vue · layouts/default.vue
    │   ├── components/                             # cross-page (TimeRangePicker, LoadMoreButton)
    │   ├── pages/
    │   │   ├── logs/ (index.vue, usePage.ts, components/)
    │   │   └── traces/ (index.vue, [traceId].vue, usePage.ts, useTracePage.ts, components/)
    │   ├── plugins/services.ts                     # DI singleton container
    │   └── services/ (HttpClientService, LogsService, TraceService, types)
    └── tests/services/                             # Vitest
```

---

## Troubleshooting

- **`dotnet ef` non trovato**: `dotnet tool restore` nella root.
- **Errore "WAL" o "database is locked" su SQLite**: il file DB è condiviso col writer; evita di aprirlo da un client esterno mentre l'app gira. Per ispezionarlo: ferma l'app, poi `sqlite3 src/OpenTelemetryDashboard.Host/telemetry.dev.db`.
- **Client OTLP non invia / "H2 connection reset"**: stai puntando al porto HTTP/1.1 (4318) col protocollo gRPC. Usa `http://host:4317` per gRPC, `http://host:4318` per HTTP/Protobuf.
- **Payload gRPC rifiutato senza errore chiaro**: controlla `MaxReceiveMessageSize` — è a 16 MiB di default; se i tuoi batch sono più grossi, aumenta `OpenTelemetryDashboard:Ingestion:Grpc:MaxReceiveMessageSize`.
- **Tutte le nuove proprietà del dominio sono null su read da DB**: hai aggiunto la proprietà ma ti sei dimenticato di rigenerare la migration. `dotnet ef migrations add <Nome> --project src/OpenTelemetryDashboard.Persistence.Sqlite --startup-project src/OpenTelemetryDashboard.Persistence.Sqlite`.
