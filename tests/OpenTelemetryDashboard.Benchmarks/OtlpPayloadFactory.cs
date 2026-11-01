using Google.Protobuf;
using OpenTelemetry.Proto.Collector.Logs.V1;
using OpenTelemetry.Proto.Collector.Metrics.V1;
using OpenTelemetry.Proto.Collector.Trace.V1;
using OpenTelemetry.Proto.Common.V1;
using OpenTelemetry.Proto.Logs.V1;
using OpenTelemetry.Proto.Metrics.V1;
using OpenTelemetry.Proto.Resource.V1;
using OpenTelemetry.Proto.Trace.V1;

namespace OpenTelemetryDashboard.Benchmarks;

/// <summary>
/// Builds OTLP export requests shaped like what the .NET SDK emits for a
/// typical HTTP service: a server span with DB and HTTP client children,
/// correlated logs and HTTP/runtime metrics. Not thread-safe: one instance
/// per worker.
/// </summary>
public sealed class OtlpPayloadFactory
{
    private static readonly string[] Routes =
    [
        "/api/orders/{id}", "/api/orders", "/api/customers/{id}", "/api/cart",
        "/api/catalog/items", "/api/catalog/items/{id}", "/api/checkout", "/health",
    ];

    private static readonly string[] Methods = ["GET", "GET", "GET", "POST", "PUT", "DELETE"];

    private static readonly string[] DbStatements =
    [
        "SELECT * FROM orders WHERE id = @id",
        "SELECT * FROM customers WHERE id = @id",
        "INSERT INTO orders (customer_id, total) VALUES (@c, @t)",
        "UPDATE cart SET qty = @q WHERE item_id = @i",
    ];

    private static readonly string[] LogTemplates =
    [
        "Request {Method} {Route} completed in {Elapsed} ms",
        "Cache miss for key {Key}, loading from database",
        "Order {OrderId} created for customer {CustomerId}",
        "Retrying downstream call to {Peer} (attempt {Attempt})",
        "Validation failed for field {Field}: value out of range",
    ];

    private static readonly double[] DurationBuckets = [0, 5, 10, 25, 50, 75, 100, 250, 500, 750, 1000, 2500, 5000, 7500, 10000];

    private readonly Random _random;
    private readonly Resource[] _resources;
    private readonly string[] _serviceNames;

    public OtlpPayloadFactory(int seed, int serviceCount, string serviceNamePrefix = "bench-service")
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(serviceCount, 1);

        _random = new Random(seed);
        _serviceNames = new string[serviceCount];
        _resources = new Resource[serviceCount];
        for (var i = 0; i < serviceCount; i++)
        {
            _serviceNames[i] = $"{serviceNamePrefix}-{i:D2}";
            _resources[i] = BuildResource(_serviceNames[i], i);
        }
    }

    public IReadOnlyList<string> ServiceNames => _serviceNames;

    public ExportTraceServiceRequest CreateTraces(int spanCount, DateTimeOffset now)
    {
        var request = new ExportTraceServiceRequest();
        var perService = new Dictionary<int, ScopeSpans>();

        var produced = 0;
        while (produced < spanCount)
        {
            var serviceIndex = _random.Next(_resources.Length);
            if (!perService.TryGetValue(serviceIndex, out var scope))
            {
                scope = new ScopeSpans { Scope = new InstrumentationScope { Name = "Microsoft.AspNetCore", Version = "10.0.0" } };
                perService[serviceIndex] = scope;
                var resourceSpans = new ResourceSpans { Resource = _resources[serviceIndex] };
                resourceSpans.ScopeSpans.Add(scope);
                request.ResourceSpans.Add(resourceSpans);
            }

            produced += AddTrace(scope, serviceIndex, spanCount - produced, now);
        }

        return request;
    }

    public ExportTraceServiceRequest CreateMarkerTrace(ReadOnlySpan<byte> traceId, DateTimeOffset now)
    {
        var start = ToUnixNano(now);
        var span = new Span
        {
            TraceId = ByteString.CopyFrom(traceId),
            SpanId = ByteString.CopyFrom(RandomBytes(8)),
            Name = "bench.marker",
            Kind = Span.Types.SpanKind.Internal,
            StartTimeUnixNano = start,
            EndTimeUnixNano = start + 1_000_000,
        };
        var scope = new ScopeSpans { Scope = new InstrumentationScope { Name = "bench" } };
        scope.Spans.Add(span);
        var resourceSpans = new ResourceSpans { Resource = _resources[0] };
        resourceSpans.ScopeSpans.Add(scope);
        var request = new ExportTraceServiceRequest();
        request.ResourceSpans.Add(resourceSpans);
        return request;
    }

    public ExportLogsServiceRequest CreateLogs(int recordCount, DateTimeOffset now)
    {
        var request = new ExportLogsServiceRequest();
        var perService = new Dictionary<int, ScopeLogs>();

        for (var i = 0; i < recordCount; i++)
        {
            var serviceIndex = _random.Next(_resources.Length);
            if (!perService.TryGetValue(serviceIndex, out var scope))
            {
                scope = new ScopeLogs { Scope = new InstrumentationScope { Name = "BenchApp.Orders" } };
                perService[serviceIndex] = scope;
                var resourceLogs = new ResourceLogs { Resource = _resources[serviceIndex] };
                resourceLogs.ScopeLogs.Add(scope);
                request.ResourceLogs.Add(resourceLogs);
            }

            scope.LogRecords.Add(BuildLogRecord(now));
        }

        return request;
    }

    public ExportMetricsServiceRequest CreateMetrics(int pointCount, DateTimeOffset now)
    {
        var request = new ExportMetricsServiceRequest();
        var timeNano = ToUnixNano(now);
        var startNano = timeNano - 60_000_000_000UL;

        var produced = 0;
        while (produced < pointCount)
        {
            var serviceIndex = _random.Next(_resources.Length);
            var scope = new ScopeMetrics { Scope = new InstrumentationScope { Name = "Microsoft.AspNetCore.Hosting" } };
            var resourceMetrics = new ResourceMetrics { Resource = _resources[serviceIndex] };
            resourceMetrics.ScopeMetrics.Add(scope);
            request.ResourceMetrics.Add(resourceMetrics);

            var remaining = pointCount - produced;
            var histogramPoints = Math.Min(remaining, 6);
            scope.Metrics.Add(BuildDurationHistogram(histogramPoints, startNano, timeNano));
            produced += histogramPoints;

            if (produced < pointCount)
            {
                scope.Metrics.Add(BuildRequestCounter(startNano, timeNano));
                produced++;
            }

            if (produced < pointCount)
            {
                scope.Metrics.Add(BuildMemoryGauge(timeNano));
                produced++;
            }
        }

        return request;
    }

    private int AddTrace(ScopeSpans scope, int serviceIndex, int budget, DateTimeOffset now)
    {
        var traceId = ByteString.CopyFrom(RandomBytes(16));
        var rootId = ByteString.CopyFrom(RandomBytes(8));
        var method = Methods[_random.Next(Methods.Length)];
        var route = Routes[_random.Next(Routes.Length)];
        var isError = _random.NextDouble() < 0.03;
        var status = isError ? 500 : (method == "POST" ? 201 : 200);

        var rootDurationMs = 2 + _random.NextDouble() * 120;
        var rootEnd = ToUnixNano(now) - (ulong)_random.Next(0, 2_000) * 1_000_000UL;
        var rootStart = rootEnd - (ulong)(rootDurationMs * 1_000_000);

        var root = new Span
        {
            TraceId = traceId,
            SpanId = rootId,
            Name = $"{method} {route}",
            Kind = Span.Types.SpanKind.Server,
            StartTimeUnixNano = rootStart,
            EndTimeUnixNano = rootEnd,
            Status = new Status { Code = isError ? Status.Types.StatusCode.Error : Status.Types.StatusCode.Unset },
        };
        root.Attributes.Add(Str("http.request.method", method));
        root.Attributes.Add(Str("http.route", route));
        root.Attributes.Add(Str("url.path", route.Replace("{id}", _random.Next(1, 100_000).ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal)));
        root.Attributes.Add(Str("url.scheme", "http"));
        root.Attributes.Add(Int("http.response.status_code", status));
        root.Attributes.Add(Str("network.protocol.version", "1.1"));
        root.Attributes.Add(Str("server.address", "localhost"));
        root.Attributes.Add(Int("server.port", 8080));
        root.Attributes.Add(Str("user_agent.original", "Mozilla/5.0 (X11; Linux x86_64) AppleWebKit/537.36 Chrome/128.0"));
        root.Attributes.Add(Str("client.address", "10.0.0." + _random.Next(2, 250).ToString(System.Globalization.CultureInfo.InvariantCulture)));
        if (isError)
        {
            root.Attributes.Add(Str("error.type", "500"));
            var exception = new Span.Types.Event { Name = "exception", TimeUnixNano = rootEnd - 1_000_000 };
            exception.Attributes.Add(Str("exception.type", "System.InvalidOperationException"));
            exception.Attributes.Add(Str("exception.message", "Sequence contains no elements"));
            exception.Attributes.Add(Str("exception.stacktrace", "System.InvalidOperationException: Sequence contains no elements\n   at System.Linq.ThrowHelper.ThrowNoElementsException()\n   at BenchApp.Orders.OrderService.GetAsync(Int32 id)"));
            root.Events.Add(exception);
        }
        scope.Spans.Add(root);

        var children = Math.Min(budget - 1, _random.Next(1, 6));
        for (var c = 0; c < children; c++)
        {
            var childStart = rootStart + (ulong)(rootDurationMs * 1_000_000 * c / (children + 1));
            var childEnd = childStart + (ulong)(rootDurationMs * 1_000_000 / (children + 2));
            var isDb = _random.NextDouble() < 0.6;
            var child = new Span
            {
                TraceId = traceId,
                SpanId = ByteString.CopyFrom(RandomBytes(8)),
                ParentSpanId = rootId,
                Kind = Span.Types.SpanKind.Client,
                StartTimeUnixNano = childStart,
                EndTimeUnixNano = childEnd,
            };
            if (isDb)
            {
                var statement = DbStatements[_random.Next(DbStatements.Length)];
                child.Name = statement.Split(' ')[0] + " orders";
                child.Attributes.Add(Str("db.system", "postgresql"));
                child.Attributes.Add(Str("db.namespace", "shop"));
                child.Attributes.Add(Str("db.query.text", statement));
                child.Attributes.Add(Str("server.address", "postgres"));
                child.Attributes.Add(Int("server.port", 5432));
            }
            else
            {
                var peer = _serviceNames[_random.Next(_serviceNames.Length)];
                child.Name = "GET";
                child.Attributes.Add(Str("http.request.method", "GET"));
                child.Attributes.Add(Str("url.full", $"http://{peer}:8080/api/catalog/items"));
                child.Attributes.Add(Str("server.address", peer));
                child.Attributes.Add(Int("server.port", 8080));
                child.Attributes.Add(Int("http.response.status_code", 200));
                child.Attributes.Add(Str("service.peer.name", peer));
            }
            scope.Spans.Add(child);
        }

        return 1 + children;
    }

    private LogRecord BuildLogRecord(DateTimeOffset now)
    {
        var roll = _random.NextDouble();
        var (severity, text) = roll switch
        {
            < 0.02 => (SeverityNumber.Error, "Error"),
            < 0.08 => (SeverityNumber.Warn, "Warning"),
            < 0.70 => (SeverityNumber.Info, "Information"),
            _ => (SeverityNumber.Debug, "Debug"),
        };
        var template = LogTemplates[_random.Next(LogTemplates.Length)];
        var time = ToUnixNano(now) - (ulong)_random.Next(0, 2_000) * 1_000_000UL;

        var record = new LogRecord
        {
            TimeUnixNano = time,
            ObservedTimeUnixNano = time,
            SeverityNumber = severity,
            SeverityText = text,
            Body = new AnyValue { StringValue = template.Replace("{", "", StringComparison.Ordinal).Replace("}", "", StringComparison.Ordinal) + " #" + _random.Next(100_000).ToString(System.Globalization.CultureInfo.InvariantCulture) },
        };
        if (_random.NextDouble() < 0.7)
        {
            record.TraceId = ByteString.CopyFrom(RandomBytes(16));
            record.SpanId = ByteString.CopyFrom(RandomBytes(8));
        }
        record.Attributes.Add(Str("{OriginalFormat}", template));
        record.Attributes.Add(Str("Method", Methods[_random.Next(Methods.Length)]));
        record.Attributes.Add(Str("Route", Routes[_random.Next(Routes.Length)]));
        record.Attributes.Add(Int("Elapsed", _random.Next(1, 500)));
        record.Attributes.Add(Str("event.name", "BenchApp.Orders.RequestCompleted"));
        return record;
    }

    private Metric BuildDurationHistogram(int points, ulong startNano, ulong timeNano)
    {
        var histogram = new Histogram { AggregationTemporality = AggregationTemporality.Cumulative };
        for (var p = 0; p < points; p++)
        {
            var point = new HistogramDataPoint { StartTimeUnixNano = startNano, TimeUnixNano = timeNano };
            point.ExplicitBounds.Add(DurationBuckets);
            ulong count = 0;
            double sum = 0;
            for (var b = 0; b <= DurationBuckets.Length; b++)
            {
                var bucket = (ulong)_random.Next(0, 50);
                point.BucketCounts.Add(bucket);
                count += bucket;
                sum += bucket * (b < DurationBuckets.Length ? DurationBuckets[b] : 10_000);
            }
            point.Count = count;
            point.Sum = sum;
            point.Attributes.Add(Str("http.request.method", Methods[p % Methods.Length]));
            point.Attributes.Add(Str("http.route", Routes[p % Routes.Length]));
            point.Attributes.Add(Int("http.response.status_code", 200));
            point.Attributes.Add(Str("url.scheme", "http"));
            histogram.DataPoints.Add(point);
        }
        return new Metric { Name = "http.server.request.duration", Unit = "s", Histogram = histogram };
    }

    private Metric BuildRequestCounter(ulong startNano, ulong timeNano)
    {
        var sum = new Sum { AggregationTemporality = AggregationTemporality.Cumulative, IsMonotonic = true };
        sum.DataPoints.Add(new NumberDataPoint { StartTimeUnixNano = startNano, TimeUnixNano = timeNano, AsInt = _random.Next(1, 1_000_000) });
        return new Metric { Name = "dotnet.gc.collections", Unit = "{collection}", Sum = sum };
    }

    private Metric BuildMemoryGauge(ulong timeNano)
    {
        var gauge = new Gauge();
        gauge.DataPoints.Add(new NumberDataPoint { TimeUnixNano = timeNano, AsDouble = 50_000_000 + _random.NextDouble() * 200_000_000 });
        return new Metric { Name = "dotnet.process.memory.working_set", Unit = "By", Gauge = gauge };
    }

    private Resource BuildResource(string serviceName, int index)
    {
        var resource = new Resource();
        resource.Attributes.Add(Str("service.name", serviceName));
        resource.Attributes.Add(Str("service.instance.id", $"{serviceName}-{index:D4}-{_random.Next(1_000, 9_999)}"));
        resource.Attributes.Add(Str("service.version", "1.4.2"));
        resource.Attributes.Add(Str("deployment.environment.name", "bench"));
        resource.Attributes.Add(Str("host.name", $"node-{index % 4}"));
        resource.Attributes.Add(Int("process.pid", 1000 + index));
        resource.Attributes.Add(Str("telemetry.sdk.name", "opentelemetry"));
        resource.Attributes.Add(Str("telemetry.sdk.language", "dotnet"));
        resource.Attributes.Add(Str("telemetry.sdk.version", "1.15.3"));
        return resource;
    }

    private byte[] RandomBytes(int length)
    {
        var bytes = new byte[length];
        _random.NextBytes(bytes);
        bytes[0] |= 1;
        return bytes;
    }

    private static ulong ToUnixNano(DateTimeOffset time) => (ulong)(time.ToUnixTimeMilliseconds() * 1_000_000L);

    private static KeyValue Str(string key, string value) => new() { Key = key, Value = new AnyValue { StringValue = value } };

    private static KeyValue Int(string key, long value) => new() { Key = key, Value = new AnyValue { IntValue = value } };
}
