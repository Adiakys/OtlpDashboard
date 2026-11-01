# Benchmarks

Two tools, two questions:

| Project | Question | Runs against |
|---|---|---|
| `OpenTelemetryDashboard.Benchmarks` | How much CPU and memory does each step of the ingest path cost? | In-process code (BenchmarkDotNet) |
| `OpenTelemetryDashboard.LoadTest` | How much telemetry can a real instance take, how fast does it show up, and how fast are the queries? | A running dashboard over OTLP/gRPC and the query API |

Both share `OtlpPayloadFactory.cs` (in this project, linked into the load test), which builds payloads shaped like a
.NET service instrumented with the OpenTelemetry SDK: HTTP server spans with DB
and HTTP client children, correlated logs, HTTP duration histograms and runtime
metrics, spread across 10 services.

## Micro-benchmarks

```bash
dotnet run -c Release --project tests/OpenTelemetryDashboard.Benchmarks -- --filter '*'
```

- `TranslatorBenchmarks`: protobuf decode and translation to domain batches, per signal.
- `SinkBenchmarks`: one EF Core sink flush (the unit of work of the background
  writer) on SQLite. Set `BENCH_POSTGRES` to a connection string of a database
  the benchmark may drop to include PostgreSQL:

```bash
docker run -d --rm --name oteldash-bench-pg -p 55432:5432 -e POSTGRES_USER=otel -e POSTGRES_PASSWORD=otel postgres:16-alpine
```

```bash
BENCH_POSTGRES="Host=localhost;Port=55432;Username=otel;Password=otel;Database=oteldash_bench" dotnet run -c Release --project tests/OpenTelemetryDashboard.Benchmarks -- --filter '*Sink*'
```

## Load test

`tests/OpenTelemetryDashboard.LoadTest/run.sh` publishes the host in Release, starts it on ports 14317/14318
against a fresh SQLite file or a throwaway PostgreSQL container, runs the load
test and cleans up. Rate limits are raised so the run measures the engine,
not the limiter; the host log goes to `tests/OpenTelemetryDashboard.LoadTest/.out/host.log`.

```bash
tests/OpenTelemetryDashboard.LoadTest/run.sh sqlite --duration 60 --workers 8 --batch 128
```

```bash
tests/OpenTelemetryDashboard.LoadTest/run.sh postgres --duration 60 --workers 8 --batch 128
```

Any option after the provider goes to the load test:

| Option | Default | Meaning |
|---|---|---|
| `--duration` | 30 | Seconds of ingest |
| `--workers` | 16 | Concurrent exporters |
| `--batch` | 128 | Spans, log records or data points per request |
| `--rate` | 0 | Total requests per second across workers; 0 means as fast as accepted |
| `--signals` | `traces,logs,metrics` | Signals to send, round robin per worker |
| `--services` | 10 | Distinct `service.name` resources |
| `--query-iterations` | 30 | Repetitions of each read query after ingest |
| `--skip-query` | | Skip the query phase |

The load test can also target any running instance (Docker included) directly:

```bash
dotnet run -c Release --project tests/OpenTelemetryDashboard.LoadTest -- --grpc http://localhost:4317 --http http://localhost:4318 --pid 12345
```

`--pid` enables RSS and CPU sampling from `/proc`; for a container, pass the
host PID from `docker inspect -f '{{.State.Pid}}' <container>`.

### What it reports

- **Accepted items/s**: what the OTLP endpoints took in, per signal, with
  export call latency. Rejected requests are `ResourceExhausted` answers from
  a full ingestion channel.
- **Drain** and **persisted items/s**: after sending stops, a marker trace is
  sent and polled until the query API returns it. The writer is a single FIFO
  consumer, so at that point everything accepted is on disk.
- **Freshness**: every 2 seconds during ingest a marker trace measures the
  delay between export and being queryable, which is what a user watching
  the dashboard sees.
- **Memory and CPU** of the dashboard process, sampled every second.
- **Query latency** p50/p95 for the queries behind the traces, logs, metrics
  and service map screens, against the data just ingested.

Results go to `tests/OpenTelemetryDashboard.LoadTest/results/<timestamp>-<label>.json` (git-ignored) with the
per-second timeline. Numbers depend heavily on the machine and disk: compare
runs from the same machine only, and record the hardware next to any number
published outside this repository.
