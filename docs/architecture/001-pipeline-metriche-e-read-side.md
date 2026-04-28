# ADR-001 — Pipeline metriche coerente + infrastruttura read-side

**Status**: proposto
**Data**: 2026-04-23
**Scope**: refactor architetturale interno, nessun impatto sul protocollo OTLP esterno

---

## Contesto

Oggi i tre segnali OTLP non sono trattati in modo coerente nella pipeline d'ingestion:

| Segnale  | Pipeline attuale                                                         |
|----------|--------------------------------------------------------------------------|
| Traces   | translator → `TelemetryChannel` → `TelemetryWriter` (BG) → EF Core       |
| Logs     | translator → `TelemetryChannel` → `TelemetryWriter` (BG) → EF Core       |
| Metrics  | translator **scrive sincronamente** in `IMetricStore` (bypassa il channel) |

Due problemi emergono da questa asimmetria:

1. **Accoppiamento improprio del traduttore**. `OtlpMetricTranslator` ha una dipendenza diretta da `IMetricStore` (implementazione-concept leak). Non eredita i benefici del resto della pipeline: no batching, no backpressure uniforme, no drain controllato su shutdown.
2. **Interfaccia monolitica**. `IMetricStore` mescola metodi di scrittura (`Record`) e lettura (`Keys`, `GetInstrument`, `Snapshot`). Diverge dal pattern usato per trace/log (DbContext EF Core) ed è difficile da estendere pulitamente verso una futura API di query o verso backend alternativi.

Inoltre la fase 1 non prevede un read-side, ma la fase 2 (API HTTP di query) lo richiederà. Conviene fissare **adesso** i contratti di lettura separati, così l'API HTTP potrà iniettarli senza toccare né il write-side né le entità del dominio.

## Obiettivi

1. **Uniformità pipeline**. Anche le metriche producono un `MetricBatch` che viaggia sul `TelemetryChannel`, viene accumulato dal `TelemetryWriter` e dispacciato a uno Sink — "come se fossero persistite", salvo che il sink in-memory le mette in un ring-buffer invece che in un DB.
2. **CQRS-light**. Separare i contratti di scrittura (`*Sink`) da quelli di lettura (`*Reader`). Niente `IRepository<T>` generico. Niente `IUnitOfWork` esplicito.
3. **Read-side pronto per l'API**. Ogni storage (SQLite, in-memory) implementa sia un Sink che un Reader. L'API futura inietterà solo i Reader, senza mai vedere `DbContext` né la metà write-side.

## Decisione

Adottare il pattern **Sink (write) + Reader (read)**, con tre coppie di interfacce — una per segnale — definite in `Core.Abstractions`. Il `TelemetryWriter` diventa un **dispatcher** che raggruppa i batch per tipo concreto e invoca il sink corrispondente.

### Perché non Repository + UnitOfWork

Sono stati considerati quattro approcci:

| Approccio | Pro | Contro | Esito |
|---|---|---|---|
| **A. Repository classico** (una `IxxRepository` con Get/Add) | familiare | mescola scrittura pipeline-oriented (batched, async) e lettura query-oriented (AsNoTracking, proiezioni): si finisce per mettere in conflitto due assi di ottimizzazione | scartato |
| **B. Repository + UnitOfWork** | transazioni cross-signal esplicite | `DbContext` EF Core **è già** un Repository (via `DbSet<T>`) e un UnitOfWork (via `SaveChanges`). Aggiungere un livello ad hoc è boilerplate senza beneficio (linea guida MS esplicita); per le metriche non c'è nemmeno una transazione da coordinare | scartato |
| **C. CQRS-light: Sink + Reader** | ogni interfaccia focalizzata su un solo asse (write-path batched vs read-path query); per gli store in-memory il "Reader" è O(1) senza UoW; testabile in isolamento | più interfacce (3+3 = 6), ma ognuna piccola e chiara | **SCELTO** |
| **D. EF Core InMemory provider per metriche** | path completamente unificato "sempre DbContext" | l'InMemory provider non supporta eviction custom (ring buffer), né è pensato per high-throughput, né gestisce query arbitrarie. Perderemmo le semantiche ring-buffer | scartato |

## Nuovi contratti

### `Core/Abstractions/` — write side

```csharp
namespace OpenTelemetryDashboard.Core.Abstractions;

public interface ITraceSink
{
    Task WriteAsync(IReadOnlyList<TraceBatch> batches, CancellationToken cancellationToken);
}

public interface ILogSink
{
    Task WriteAsync(IReadOnlyList<LogBatch> batches, CancellationToken cancellationToken);
}

public interface IMetricSink
{
    Task WriteAsync(IReadOnlyList<MetricBatch> batches, CancellationToken cancellationToken);
}
```

Ogni sink accetta **una lista di batch** (finestra accumulata dal writer) per permettere una singola `SaveChangesAsync` su EF Core o un'unica acquisizione di lock per il ring-buffer in-memory.

### `Core/Abstractions/` — read side (minimo v1)

```csharp
namespace OpenTelemetryDashboard.Core.Abstractions;

public interface ITraceReader
{
    Task<Span?> FindSpanAsync(TraceId traceId, SpanId spanId, CancellationToken ct);
    IAsyncEnumerable<Span> GetSpansInTraceAsync(TraceId traceId, CancellationToken ct);
}

public interface ILogReader
{
    IAsyncEnumerable<LogRecord> QueryRecentAsync(int take, CancellationToken ct);
}

public interface IMetricReader
{
    IReadOnlyCollection<InstrumentKey> GetInstrumentKeys();
    Instrument? GetInstrument(InstrumentKey key);
    IReadOnlyList<DataPoint> GetPoints(InstrumentKey key);
}
```

I Reader sono volutamente **minimi**. Saranno estesi dalla fase API quando i casi d'uso concreti emergeranno (filtri, paginazione, proiezioni). Non aggiungere metodi finché non servono.

### `Core/Ingestion/` — nuovo batch type

```csharp
public sealed record MetricBatch(
    IReadOnlyList<Resource> Resources,
    IReadOnlyList<MetricSample> Samples)
    : TelemetryBatch(Resources);

public sealed record MetricSample(
    InstrumentKey Key,
    Instrument Instrument,
    DataPoint Point);
```

Un `MetricSample` è un singolo data point tipizzato con la sua chiave e il suo instrument. Il batch li tiene flat; il sink può raggrupparli internamente se lo desidera.

## Cosa viene rimosso

- `Core/Abstractions/IMetricStore.cs` → rimpiazzato da `IMetricSink` + `IMetricReader`.
- `Metrics.InMemory/InMemoryMetricStore.cs` → separata in `InMemoryMetricSink` + `InMemoryMetricReader`, più una classe interna `InMemoryMetricStorage` che tiene lo stato condiviso.

## Impatto per progetto

### `Core`
- **Aggiunge** `Abstractions/{ITraceSink, ILogSink, IMetricSink, ITraceReader, ILogReader, IMetricReader}.cs`.
- **Aggiunge** `Ingestion/MetricBatch.cs` (extends `TelemetryBatch`).
- **Rimuove** `Abstractions/IMetricStore.cs`.

### `Persistence`
- **Aggiunge** `Sinks/EfCoreTraceSink.cs`, `Sinks/EfCoreLogSink.cs` — incapsulano la logica di persistence oggi dentro `TelemetryWriter`. Condividono la `ResourceCache` (iniettata).
- **Aggiunge** `Readers/EfCoreTraceReader.cs`, `Readers/EfCoreLogReader.cs` — implementazione minima v1 (`FindSpanAsync`, `GetSpansInTraceAsync`, `QueryRecentAsync`). Usano `IDbContextFactory<TelemetryDbContext>` con `AsNoTracking` obbligatorio.
- **Modifica** `TelemetryWriter.cs`: diventa un **dispatcher**. Non conosce più `TelemetryDbContext`. Raggruppa i batch per tipo concreto (`TraceBatch`/`LogBatch`/`MetricBatch`), invoca il sink corrispondente, drena il channel su shutdown. L'`IDbContextFactory` e la `ResourceCache` **spariscono** dalle sue dipendenze — sono dettagli dei sink EF Core.
- **Modifica** `ServiceCollectionExtensions.AddTelemetryPersistenceCore` ora registra anche i due sink + i due reader EF Core.

### `Persistence.Sqlite`
- Nessun cambiamento strutturale. La registrazione delle migration resta qui. `AddSqliteTelemetryStore` invoca `AddTelemetryPersistenceCore` che ora tira su sink+reader insieme al DbContext.

### `Metrics.InMemory`
- **Aggiunge** `InMemoryMetricSink.cs`, `InMemoryMetricReader.cs`.
- **Aggiunge** `InMemoryMetricStorage.cs` (sealed, internal o public ma concettualmente interno al modulo): incapsula il `ConcurrentDictionary<InstrumentKey, Entry>` condiviso tra sink e reader. Singleton.
- **Rimuove** `InMemoryMetricStore.cs`.
- **Modifica** `ServiceCollectionExtensions.AddInMemoryMetricStore` registra: storage (singleton) + sink (singleton) + reader (singleton).

### `Ingestion`
- **Modifica** `OtlpMetricTranslator.cs`: NON chiama più lo store. Il suo metodo `Ingest(ExportMetricsServiceRequest)` diventa `Translate(...)` e restituisce un `MetricBatch?` (nullable — null se niente da fare), coerente con i traduttori di trace/log.
- **Modifica** `Grpc/OtlpMetricsService.cs`: invoca `_translator.Translate(...)` + `_channel.TryWrite(...)` + gestisce backpressure con `ResourceExhausted` — esattamente come `OtlpTraceService`.
- **Modifica** `Http/OtlpHttpEndpoints.cs` (handler `/v1/metrics`): stessa cosa in HTTP, con 429 Retry-After su channel pieno.
- **Rimuove** la dipendenza da `IMetricStore` dalla registrazione del servizio.

### `Host`
- `Program.cs`: la sola differenza è che `AddTelemetryCore` resta invariato; `AddInMemoryMetricStore` ora registra sink+reader (dietro le quinte); `AddSqliteTelemetryStore` ora registra sink+reader EF Core (dietro le quinte). `TelemetryWriter` è già registrato via `AddTelemetryWriter` e si auto-risolve i tre sink via DI.

### Test
- `UnitTests`:
  - Invariati: `RingBuffer`, `TraceId`, `SpanId`, `ResourceHasher`, `ResourceCache`.
  - Aggiornati: `InMemoryMetricStoreTests` → split in `InMemoryMetricSinkTests` + `InMemoryMetricReaderTests` + `InMemoryMetricStorageTests`.
  - Nuovo: `TelemetryWriterDispatchTests` — con tre sink mock, verifica che i batch misti vengano dispacciati correttamente per tipo e che un'eccezione in un sink non affondi gli altri.
  - Nuovo: `EfCoreTraceSinkTests` + `EfCoreLogSinkTests` con SQLite `:memory:` — dedup resource e singolo SaveChanges per batch.
- `IntegrationTests`: invariati. La path OTLP → DB è la stessa dal di fuori. Rimane solo da aggiornare il test metrics perché oggi preleva dal servizio `IMetricStore`; deve passare a `IMetricReader`. Aggiungere un test che conferma il drain shutdown delle metriche (prima non serviva perché path sincrono).

## Passi d'implementazione (eseguibili in sequenza)

1. **Contratti Core** — 6 interfacce + `MetricBatch`/`MetricSample`. Zero rottura: nessuno li usa ancora.
2. **Storage in-memory** — scorpora `InMemoryMetricStorage` dalla classe esistente; crea `InMemoryMetricSink` e `InMemoryMetricReader`. Aggiorna la registrazione DI.
3. **Sinks EF Core** — crea `EfCoreTraceSink` ed `EfCoreLogSink` estraendoli dal `TelemetryWriter` esistente. La logica di dedup con `ResourceCache` si sposta nei sink.
4. **Readers EF Core** — crea `EfCoreTraceReader` ed `EfCoreLogReader` con i metodi minimi v1. Non esposti dall'HTTP in questa fase (saranno usati dall'API in un piano successivo).
5. **Refactor TelemetryWriter** — elimina DbContext dalle dipendenze, inietta `ITraceSink`/`ILogSink`/`IMetricSink`. Dispatch per tipo concreto. Shutdown drain invariato.
6. **Ingestion metrics** — modifica `OtlpMetricTranslator` per restituire `MetricBatch?`. Aggiorna `OtlpMetricsService` e `OtlpHttpEndpoints` per enqueue sul channel.
7. **Rimozione del deprecato** — elimina `IMetricStore` e la vecchia `InMemoryMetricStore` (monolitica). Aggiorna gli import residui.
8. **Test** — sposta/rinomina test esistenti, aggiungi quelli nuovi (§Test sopra).
9. **Build Release + test** — tutto verde, zero warning.
10. **Smoke test** manuale con `dotnet run`: spedisci un payload metrico e verifica via `IMetricReader` (temporaneamente esposto da un endpoint `GET /debug/metrics` solo in Development, da rimuovere a fine smoke).
11. **README** — aggiorna la sezione "chi fa cosa" per Core / Metrics.InMemory / Persistence e la tabella "dove metto il codice se…".

## Strategia di verifica

- `dotnet build OpenTelemetryDashboard.slnx -c Release` → 0 warning / 0 errori.
- `dotnet test OpenTelemetryDashboard.slnx` → tutti verdi.
- `OtlpHttpIngestionTests.Post_Metrics_Records_Gauge_In_Store` (rinominato se serve) → gauge scritto via HTTP è visibile via `IMetricReader`.
- Nuovo `TelemetryWriterDispatchTests.Drains_MetricBatch_On_Shutdown` → dopo `StopAsync`, le metriche in-flight nel channel sono visibili via reader.
- Nuovo `EfCoreTraceSinkTests.Shares_Resource_Across_Sinks` → trace sink e log sink vedono la stessa `ResourceCache` e non duplicano righe su Resources.

## Rischi e mitigazioni

| Rischio | Mitigazione |
|---|---|
| **Latenza extra per le metriche** (ora passano per un channel) | irrilevante in pratica: il channel è bounded e il sink in-memory è O(1); la differenza è <100µs per batch. |
| **Perdita di metriche su shutdown** | drain del channel è già implementato con timeout configurabile. Aggiungere test mirato (punto 10). |
| **Regressioni su dedup risorse** | test unitario che verifica la cache condivisa e il conteggio delle righe `resources`. |
| **Proliferazione di micro-interfacce** | 6 interfacce sono sostenibili. Se in futuro diventano troppe, si accorpa via generic `ISink<T>`/`IReader<T>`. |

## Fuori scope (piani futuri)

- **Implementazione concreta della Query API** (endpoint HTTP, DTO, controller, filtri): piano dedicato.
- **Provider DB aggiuntivi** (PostgreSQL, SQL Server, MySQL): già accomodati dal pattern — nuovo progetto `Persistence.<Nome>` con sink/reader/migrations propri.
- **Metriche persistite su DB**: emergerà come `EfCoreMetricSink` + `EfCoreMetricReader`, rimpiazzabili a runtime tramite la composizione in `Host`. L'architettura lo accomoda senza ulteriori modifiche ai contratti.
- **`IUnitOfWork` esplicito**: non necessario v1. Se in futuro si renderà necessaria una transazione cross-signal (es. salvare traces + logs come operazione atomica), si introdurrà lì.

## Follow-up (2026-04-23)

Dopo aver implementato la decisione sopra, il progetto dedicato `OpenTelemetryDashboard.Metrics.InMemory` è stato consolidato dentro `OpenTelemetryDashboard.Persistence` come sottocartella `Metrics/InMemory/`. La decisione architetturale (CQRS-light Sink+Reader, `MetricBatch` sullo stesso channel) resta valida al 100% — cambia solo la granularità fisica dei progetti: meno csproj e ProjectReference da mantenere a fronte di una separazione che sarebbe stata utile solo con un secondo metric provider, oggi speculativo. I namespace sono passati da `OpenTelemetryDashboard.Metrics.InMemory` a `OpenTelemetryDashboard.Persistence.Metrics.InMemory`. Il patto "tutto in-memory vive qui finché non servirà davvero un secondo provider, nel qual caso estraiamo" è documentato in `README.md`.
