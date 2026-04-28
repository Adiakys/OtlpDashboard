using OpenTelemetryDashboard.Api.Mappings;
using OpenTelemetryDashboard.Core.Abstractions.Queries;
using OpenTelemetryDashboard.Core.Common;
using OpenTelemetryDashboard.Core.Domain;

namespace OpenTelemetryDashboard.UnitTests.Api;

public sealed class DomainMappingsTests
{
    [Fact]
    public void UnixNanoTime_Round_Trips_DateTimeOffset()
    {
        var now = DateTimeOffset.UtcNow;
        var nanos = UnixNanoTime.ToUnixNanoseconds(now);
        var back = UnixNanoTime.FromUnixNanoseconds(nanos);

        // Sub-100ns precision is lost; compare at 100ns granularity.
        back.UtcTicks.ShouldBe(now.UtcTicks);
    }

    [Fact]
    public void LogRecord_Maps_All_Basic_Fields()
    {
        var time = new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
        var record = new LogRecord
        {
            ResourceHash = new byte[32],
            TimeUnixNano = UnixNanoTime.ToUnixNanoseconds(time),
            SeverityNumber = SeverityNumber.Info,
            SeverityText = "INFO",
            Body = "hello",
            ScopeName = "test",
            ScopeVersion = "1.0",
        };

        var dto = record.ToDto(serviceName: "my-service");

        dto.Time.ShouldBe(time);
        dto.SeverityText.ShouldBe("INFO");
        dto.Body.ShouldBe("hello");
        dto.SeverityNumber.ShouldBe((int)SeverityNumber.Info);
        dto.TraceId.ShouldBeNull();
        dto.SpanId.ShouldBeNull();
        dto.ResourceHash.Length.ShouldBe(64); // 32 bytes hex
        dto.ServiceName.ShouldBe("my-service");
    }

    [Fact]
    public void LogRecord_Exposes_TraceId_Hex_When_Present()
    {
        var traceBytes = new byte[16];
        traceBytes[0] = 0xaa;
        var record = new LogRecord
        {
            ResourceHash = new byte[32],
            TraceId = TraceId.FromBytes(traceBytes),
            SpanId = SpanId.FromBytes(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 }),
        };

        var dto = record.ToDto(serviceName: null);

        dto.TraceId.ShouldNotBeNull();
        dto.TraceId!.Length.ShouldBe(32);
        dto.SpanId.ShouldNotBeNull();
        dto.SpanId!.Length.ShouldBe(16);
    }

    [Fact]
    public void TraceSummary_Maps_Duration()
    {
        var start = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var summary = new TraceSummary
        {
            TraceId = TraceId.FromBytes(new byte[16]),
            ResourceHash = new byte[32],
            RootSpanName = "root",
            StartUnixNano = UnixNanoTime.ToUnixNanoseconds(start),
            EndUnixNano = UnixNanoTime.ToUnixNanoseconds(start.AddMilliseconds(250)),
            SpanCount = 3,
            RootStatusCode = SpanStatusCode.Ok,
        };

        var dto = summary.ToDto(serviceName: "root-service");

        dto.DurationMs.ShouldBe(250.0, 1.0);
        dto.SpanCount.ShouldBe(3);
        dto.RootStatusCode.ShouldBe("Ok");
        dto.ServiceName.ShouldBe("root-service");
    }
}
