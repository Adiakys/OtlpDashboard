import type { InstrumentDto } from '~/services/types'

const POSTGRES_SCOPE =
  'github.com/open-telemetry/opentelemetry-collector-contrib/receiver/postgresqlreceiver'
const REDIS_SCOPE =
  'github.com/open-telemetry/opentelemetry-collector-contrib/receiver/redisreceiver'
const DOTNET_SCOPE = 'System.Runtime'
const ASPNETCORE_SCOPE = 'Microsoft.AspNetCore.Hosting'
const SAMPLE_SERVER_SCOPE = 'SampleServer'
const SAMPLE_CLIENT_SCOPE = 'SampleClient'

/**
 * Per-instrument generator configuration, keyed alongside an `InstrumentDto`
 * the demo's `/v1/metrics` handler returns. `baseline / drift / jitter` feed
 * the random-walk generator in `generators/metrics.ts`; `splitBy` describes
 * an attribute that fans out one walk per attribute value (e.g. one
 * connection count per database).
 */
export interface InstrumentSpec {
  /** What `/v1/metrics` advertises. */
  dto: InstrumentDto
  /** Mean value at t=now-window, used to seed the walk. */
  baseline: number
  /** Mean step per emitted point. Positive on monotonic Sum instruments
   *  (cumulative counters tick up); 0 for stationary gauges. */
  drift: number
  /** Std-dev of the per-step Gaussian noise. */
  jitter: number
  /** Inclusive lower bound (clamped). */
  min?: number
  /** Inclusive upper bound (clamped). */
  max?: number
  /** Optional split-by attribute. When set, the generator emits one
   *  series per `values[]` entry. The widget queries with
   *  `includeAttributes: true` to get them disaggregated. */
  splitBy?: {
    attr: string
    values: { value: string; baseline: number; drift?: number }[]
  }
}

/**
 * Stable resourceHash per (service, instance). The real server produces
 * the hash from the OTel resource attributes (which include
 * <c>service.name</c> AND <c>service.instance.id</c>); for the demo we
 * just need a deterministic string that round-trips through the catalog
 * lookup and is distinct per instance so the SPA's resolver can tell
 * <c>server-1</c> from <c>server-2</c>.
 */
function resourceHashFor(serviceName: string, serviceInstanceId: string): string {
  return `demo-${serviceName}-${serviceInstanceId}`
}

function dto(
  serviceName: string,
  serviceInstanceId: string,
  scopeName: string,
  name: string,
  kind: 'Sum' | 'Gauge' | 'Histogram',
  unit: string,
  isMonotonic: boolean,
  description: string,
  approxPointCount: number = 60
): InstrumentDto {
  return {
    resourceHash: resourceHashFor(serviceName, serviceInstanceId),
    serviceName,
    serviceInstanceId,
    scopeName,
    name,
    kind,
    description,
    unit,
    isMonotonic,
    temporality: kind === 'Gauge' ? 'Unspecified' : 'Cumulative',
    pointCount: approxPointCount
  }
}

/**
 * Service-instance topology the demo emulates. Mirrors the docker-compose
 * stack referenced by the bundled default dashboard:
 * <list type="bullet">
 *   <item><c>sample-server</c> with two replicas (server-1, server-2).</item>
 *   <item><c>sample-client</c> with one replica (client-1).</item>
 *   <item><c>postgresql</c> exposed via the OTel postgres receiver per
 *         logical scope: cluster-level (postgres:5432) and per-database
 *         (telemetry, sampleapp).</item>
 * </list>
 * Each instance gets its own resourceHash and its own seeded random walk,
 * so the dashboard's per-instance widgets show distinct numbers.
 */
const SAMPLE_SERVER_INSTANCES = ['server-1', 'server-2'] as const
const SAMPLE_CLIENT_INSTANCES = ['client-1'] as const
const POSTGRES_INSTANCES = ['postgres:5432', 'telemetry', 'sampleapp'] as const
const REDIS_INSTANCES = ['redis-0'] as const

function dotnetRuntimeSpecsFor(serviceName: string, instance: string): InstrumentSpec[] {
  return [
    {
      dto: dto(serviceName, instance, DOTNET_SCOPE,
        'dotnet.gc.last_collection.heap.size', 'Sum', 'By', false,
        'The managed heap size (bytes) at the last GC.'),
      baseline: 84_000_000, drift: 0, jitter: 4_000_000, min: 30_000_000
    },
    {
      dto: dto(serviceName, instance, DOTNET_SCOPE,
        'dotnet.gc.last_collection.memory.committed_size', 'Sum', 'By', false,
        'The amount of committed memory (bytes) at the last GC.'),
      baseline: 210_000_000, drift: 0, jitter: 8_000_000, min: 100_000_000
    },
    {
      dto: dto(serviceName, instance, DOTNET_SCOPE,
        'dotnet.thread_pool.thread.count', 'Sum', '{thread}', false,
        'Number of thread pool threads.'),
      baseline: 22, drift: 0, jitter: 2, min: 8, max: 64
    },
    {
      dto: dto(serviceName, instance, DOTNET_SCOPE,
        'dotnet.exceptions', 'Sum', '{exception}', true,
        'Number of exceptions thrown by managed code (cumulative).'),
      baseline: 14, drift: 0.07, jitter: 0.4, min: 0
    },
    {
      dto: dto(serviceName, instance, DOTNET_SCOPE,
        'dotnet.gc.collections', 'Sum', '{collection}', true,
        'GCs that have occurred (cumulative), split by generation.'),
      baseline: 0, drift: 0, jitter: 0,
      splitBy: {
        attr: 'gc.heap.generation',
        values: [
          { value: 'gen0', baseline: 240, drift: 0.55 },
          { value: 'gen1', baseline: 38, drift: 0.07 },
          { value: 'gen2', baseline: 7, drift: 0.012 }
        ]
      }
    },
    {
      dto: dto(serviceName, instance, DOTNET_SCOPE,
        'dotnet.monitor.lock_contentions', 'Sum', '{contention}', true,
        'Lock contentions on the runtime (cumulative).'),
      baseline: 38, drift: 0.12, jitter: 0.6, min: 0
    }
  ]
}

function postgresSpecsFor(instance: string): InstrumentSpec[] {
  return [
    {
      dto: dto('postgresql', instance, POSTGRES_SCOPE,
        'postgresql.backends', 'Sum', '{connection}', false,
        'Number of backends.'),
      baseline: 0, drift: 0, jitter: 0,
      splitBy: {
        attr: 'postgresql.database.name',
        values: [
          { value: 'sample', baseline: 18, drift: 0 },
          { value: 'app', baseline: 9, drift: 0 },
          { value: 'jobs', baseline: 4, drift: 0 }
        ]
      }
    },
    {
      dto: dto('postgresql', instance, POSTGRES_SCOPE,
        'postgresql.connection.max', 'Gauge', '{connection}', false,
        'Configured maximum number of client connections allowed.'),
      baseline: 100, drift: 0, jitter: 0, min: 100, max: 100
    },
    {
      dto: dto('postgresql', instance, POSTGRES_SCOPE,
        'postgresql.db_size', 'Sum', 'By', false,
        'The database disk usage (bytes).'),
      baseline: 1_280_000_000, drift: 80_000, jitter: 600_000, min: 1_000_000_000
    },
    {
      dto: dto('postgresql', instance, POSTGRES_SCOPE,
        'postgresql.deadlocks', 'Sum', '{deadlock}', true,
        'The number of deadlocks (cumulative).'),
      baseline: 0, drift: 0.005, jitter: 0.05, min: 0
    },
    {
      dto: dto('postgresql', instance, POSTGRES_SCOPE,
        'postgresql.database.count', 'Sum', '{database}', false,
        'Number of user databases on the cluster.'),
      baseline: 3, drift: 0, jitter: 0, min: 3, max: 3
    },
    {
      dto: dto('postgresql', instance, POSTGRES_SCOPE,
        'postgresql.table.count', 'Sum', '{table}', false,
        'Number of user tables in the cluster.'),
      baseline: 47, drift: 0, jitter: 0, min: 47, max: 47
    },
    {
      dto: dto('postgresql', instance, POSTGRES_SCOPE,
        'postgresql.wal.age', 'Gauge', 's', false,
        'Age of the oldest WAL file (seconds since the latest checkpoint).'),
      baseline: 12, drift: 0, jitter: 4, min: 0, max: 120
    },
    {
      dto: dto('postgresql', instance, POSTGRES_SCOPE,
        'postgresql.replication.data_delay', 'Gauge', 's', false,
        'Replication delay in seconds.'),
      baseline: 0.18, drift: 0, jitter: 0.1, min: 0, max: 5
    },
    {
      dto: dto('postgresql', instance, POSTGRES_SCOPE,
        'postgresql.commits', 'Sum', '{transaction}', true,
        'The number of commits (cumulative).'),
      baseline: 0, drift: 4.2, jitter: 1.6, min: 0
    },
    {
      dto: dto('postgresql', instance, POSTGRES_SCOPE,
        'postgresql.rollbacks', 'Sum', '{transaction}', true,
        'The number of rollbacks (cumulative).'),
      baseline: 0, drift: 0.06, jitter: 0.3, min: 0
    }
  ]
}

/**
 * Custom counters emitted by the .NET sample-server worker. Mirrors what
 * the docker-compose stack's <c>SampleServer.Counter</c> meter would
 * produce — counter reads / mutations cumulative since process start.
 */
function sampleServerSpecsFor(instance: string): InstrumentSpec[] {
  return [
    {
      dto: dto('sample-server', instance, SAMPLE_SERVER_SCOPE,
        'sample_server.counter.reads', 'Sum', '{read}', true,
        'Number of counter reads served (cumulative).'),
      baseline: 0, drift: 6.5, jitter: 1.4, min: 0
    },
    {
      dto: dto('sample-server', instance, SAMPLE_SERVER_SCOPE,
        'sample_server.counter.mutations', 'Sum', '{mutation}', true,
        'Number of counter mutations served (cumulative).'),
      baseline: 0, drift: 1.8, jitter: 0.6, min: 0
    }
  ]
}

/**
 * ASP.NET Core Hosting metrics emitted by every sample-server replica.
 * <c>http.server.active_requests</c> is in-flight count, sampled every
 * point; <c>http.server.request.duration</c> is the histogram heatmaps
 * read.
 */
function aspnetCoreSpecsFor(serviceName: string, instance: string): InstrumentSpec[] {
  return [
    {
      dto: dto(serviceName, instance, ASPNETCORE_SCOPE,
        'http.server.active_requests', 'Sum', '{request}', false,
        'Number of in-flight HTTP server requests.'),
      baseline: 4, drift: 0, jitter: 1.5, min: 0, max: 32
    },
    {
      dto: dto(serviceName, instance, ASPNETCORE_SCOPE,
        'http.server.request.duration', 'Histogram', 's', false,
        'Duration of HTTP server requests (seconds).'),
      baseline: 0.04, drift: 0, jitter: 0.015, min: 0.002, max: 1
    }
  ]
}

/**
 * Custom meter on the .NET sample-client worker. Iterations are the
 * outer loop counter; iteration_latency is the histogram driving the
 * heatmap on the demo dashboard.
 */
function sampleClientSpecsFor(instance: string): InstrumentSpec[] {
  return [
    {
      dto: dto('sample-client', instance, SAMPLE_CLIENT_SCOPE,
        'sample_client.iterations', 'Sum', '{iteration}', true,
        'Iterations completed by the client worker (cumulative).'),
      baseline: 0, drift: 4.2, jitter: 0.9, min: 0
    },
    {
      dto: dto('sample-client', instance, SAMPLE_CLIENT_SCOPE,
        'sample_client.iteration_latency', 'Histogram', 's', false,
        'Latency of one client iteration (seconds).'),
      baseline: 0.12, drift: 0, jitter: 0.04, min: 0.005, max: 2
    }
  ]
}

/**
 * Redis receiver metrics. The widget bindings on the demo dashboard
 * leave <c>serviceInstanceId</c> unset, so a single instance per
 * (scope, name, kind) is enough — the resolver falls back to the
 * unique match when no instance is requested.
 */
function redisSpecsFor(instance: string): InstrumentSpec[] {
  return [
    {
      dto: dto('redis', instance, REDIS_SCOPE,
        'redis.memory.used', 'Gauge', 'By', false,
        'Memory used by the Redis server.'),
      baseline: 32_000_000, drift: 0, jitter: 1_500_000, min: 8_000_000
    },
    {
      dto: dto('redis', instance, REDIS_SCOPE,
        'redis.clients.connected', 'Sum', '{client}', false,
        'Number of clients currently connected.'),
      baseline: 6, drift: 0, jitter: 1, min: 1, max: 32
    },
    {
      dto: dto('redis', instance, REDIS_SCOPE,
        'redis.commands.processed', 'Sum', '{command}', true,
        'Total number of commands processed by the server (cumulative).'),
      baseline: 0, drift: 14, jitter: 3, min: 0
    }
  ]
}

/**
 * Catalog covering every instrument the demo dashboard's widgets need,
 * plus a few neighbours (commits / rollbacks for transaction-pulse,
 * replication.data_delay for the server-card LED, etc.).
 *
 * Adding instruments here automatically:
 *  - exposes them in `/v1/metrics`
 *  - makes `/v1/metrics/points` return a generated series
 *
 * Order matters only for display in pickers (alphabetical-by-service is
 * reasonable but not enforced — the SPA sorts).
 */
export const INSTRUMENT_CATALOG: InstrumentSpec[] = [
  ...SAMPLE_SERVER_INSTANCES.flatMap((instance) => [
    ...dotnetRuntimeSpecsFor('sample-server', instance),
    ...aspnetCoreSpecsFor('sample-server', instance),
    ...sampleServerSpecsFor(instance)
  ]),
  ...SAMPLE_CLIENT_INSTANCES.flatMap((instance) => [
    ...dotnetRuntimeSpecsFor('sample-client', instance),
    ...sampleClientSpecsFor(instance)
  ]),
  ...POSTGRES_INSTANCES.flatMap((instance) => postgresSpecsFor(instance)),
  ...REDIS_INSTANCES.flatMap((instance) => redisSpecsFor(instance))
]

/**
 * Lookup table keyed by the four-tuple every widget query carries. Used
 * by the demo metrics handler to find the matching `InstrumentSpec`.
 */
export function findInstrument(
  scopeName: string,
  instrumentName: string,
  kind: string,
  serviceName?: string | null
): InstrumentSpec | null {
  return (
    INSTRUMENT_CATALOG.find(
      (i) =>
        i.dto.scopeName === scopeName &&
        i.dto.name === instrumentName &&
        i.dto.kind === kind &&
        (serviceName == null || i.dto.serviceName === serviceName)
    ) ?? null
  )
}

/**
 * Lookup by `resourceHash + scope + name + kind`. The `resourceHash` is
 * stable per service in the demo, so this is mostly the same as
 * `findInstrument` but gives the SPA a way to keep the late-binding
 * contract intact.
 */
export function findInstrumentByHash(
  resourceHash: string,
  scopeName: string,
  instrumentName: string,
  kind: string
): InstrumentSpec | null {
  return (
    INSTRUMENT_CATALOG.find(
      (i) =>
        i.dto.resourceHash === resourceHash &&
        i.dto.scopeName === scopeName &&
        i.dto.name === instrumentName &&
        i.dto.kind === kind
    ) ?? null
  )
}
